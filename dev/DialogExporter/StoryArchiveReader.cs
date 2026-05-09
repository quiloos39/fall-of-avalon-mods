namespace DialogExporter;

// Loads story.arch (a UnityFS bundle) and parses each .story binary into a
// chapter-graph + decoded steps. Mirrors StoryGraphRuntime + StoryReader
// from the game.

public sealed class StoryGraphData
{
    public string Guid;                       // taken from the file name
    public string[] UsedSoundBanks;
    public string[] Tags;
    public bool SharedBetweenMultipleNPCs;
    public StartNodeData StartNode;           // null if no startNode
    public ChapterData[] Chapters;
    public int ConditionsCount;               // we don't decode condition contents (yet)
    public bool DecodingFailed;               // unknown step type encountered
    public string DecodingError;
    public int LastDecodedChapter = -1;
    public int LastDecodedStep = -1;
    public int LastDecodedType = -1;          // last step byte that was decoded BEFORE the one that crashed
    public int CrashedAtType = -1;            // step byte we were decoding when the crash happened (or its position drifted into garbage)
}

public sealed class ChapterData
{
    public int Index;
    public List<IStepInfo> Steps = new();
    public int ContinuationIndex = -1;        // -1 = null
}

public sealed class StartNodeData
{
    public bool EnableChoices;
    public bool InvolveHero;
    public bool InvolveAI;
    public List<IStepInfo> Choices = new();   // SStoryStartChoice steps
    public int ContinuationChapterIndex = -1;
}

public static class StoryArchiveReader
{
    public static string TraceGuid = null;
    public static bool Tracing = false;

    // Yields one StoryGraphData per ".story" entry in the bundle.
    public static IEnumerable<StoryGraphData> ReadAll(string storyArchPath)
    {
        var bundle = UnityFsBundle.Load(storyArchPath);

        foreach (var entry in bundle.Entries)
        {
            if (!entry.Path.EndsWith(".story", StringComparison.OrdinalIgnoreCase)) continue;
            string guid = ExtractGuid(entry.Path);

            Tracing = (TraceGuid != null && guid.StartsWith(TraceGuid));
            var graph = new StoryGraphData { Guid = guid };
            try
            {
                ReadOne(entry.Data, graph);
            }
            catch (Exception ex)
            {
                graph.DecodingFailed = true;
                graph.DecodingError = ex.GetType().Name + ": " + ex.Message;
            }
            yield return graph;
        }
    }

    private static string ExtractGuid(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name; // every story file is "<guid>.story"
    }

    private static void ReadOne(byte[] data, StoryGraphData g)
    {
        var r = new StoryStreamReader(data);
        g.UsedSoundBanks = r.ReadStringArray();
        g.Tags = r.ReadStringArray();

        int varCount = r.ReadInt32();
        if (varCount < 0 || varCount > 1_000_000) throw new InvalidDataException($"varCount {varCount} out of range");
        for (int i = 0; i < varCount; i++) _ = r.ReadVariableDefine();

        int vrefCount = r.ReadInt32();
        if (vrefCount < 0 || vrefCount > 1_000_000) throw new InvalidDataException($"vrefCount {vrefCount} out of range");
        for (int i = 0; i < vrefCount; i++) _ = r.ReadVariableReferenceDefine();

        g.SharedBetweenMultipleNPCs = r.ReadBool();

        int chapterCount = r.ReadInt32();
        if (chapterCount < 0 || chapterCount > 100_000) throw new InvalidDataException($"chapterCount {chapterCount} out of range");
        g.Chapters = new ChapterData[chapterCount];
        for (int i = 0; i < chapterCount; i++) g.Chapters[i] = new ChapterData { Index = i };

        int condCount = r.ReadInt32();
        g.ConditionsCount = condCount;
        for (int i = 0; i < condCount; i++)
        {
            byte _condType = r.ReadByte();
            // Each StoryCondition has its own Read(StoryReader). We don't
            // decode condition payloads here; we just need to know how many
            // bytes they consume. SKIPPING IS UNSAFE — we will hit alignment
            // issues. So conditions are decoded later if/when we add support.
            // For now, we throw if we encounter any conditions block beyond
            // the count header. The way the game serializes is:
            //   for each cond: byte type, then later AT THE BOTTOM the body
            //   (inputs[] + conditions[]).
            // Actually re-reading StoryReader.Read: conditions are split:
            //     1) at the top, only the byte types
            //     2) after chapters, the actual bodies.
            // So just reading the byte type here is correct.
        }

        bool hasStartNode = r.ReadBool();
        if (hasStartNode)
        {
            var sn = new StartNodeData
            {
                EnableChoices = r.ReadBool(),
                InvolveHero = r.ReadBool(),
                InvolveAI = r.ReadBool(),
            };
            int choiceCount = r.ReadInt32();
            for (int i = 0; i < choiceCount; i++)
            {
                // SStoryStartChoice.Read():
                //   base.Read(reader) -> conditions[]
                //   text, span (int), spanFlag, targetChapter (int)
                var conds = r.ReadConditionInputArray();
                var choice = new StoryStartChoiceStep
                {
                    Conditions = conds,
                    TextId = r.ReadLightLocString(),
                    Span = r.ReadInt32(),
                    SpanFlag = r.ReadString(),
                    TargetChapter = r.ReadChapterIndex(),
                };
                sn.Choices.Add(choice);
            }
            sn.ContinuationChapterIndex = r.ReadChapterIndex();
            g.StartNode = sn;
        }

        int lastType = -1;
        for (int i = 0; i < chapterCount; i++)
        {
            var ch = g.Chapters[i];
            int stepCount;
            try { stepCount = r.ReadInt32(); }
            catch (Exception ex)
            {
                g.DecodingFailed = true;
                g.DecodingError = $"reading stepCount for chapter {i} failed: {ex.GetType().Name}: {ex.Message}";
                g.LastDecodedChapter = i;
                g.LastDecodedType = lastType;
                return;
            }
            if (stepCount < 0 || stepCount > 100_000)
            {
                g.DecodingFailed = true;
                g.DecodingError = $"chapter {i}: bad stepCount {stepCount} (last decoded step type was {lastType})";
                g.LastDecodedChapter = i;
                g.LastDecodedType = lastType;
                return;
            }
            for (int s = 0; s < stepCount; s++)
            {
                int posBefore = r.Pos;
                byte stepType = r.ReadByte();
                IStepInfo decoded;
                if (Tracing)
                    Console.WriteLine($"      ch{i} s{s} pos={posBefore} type={stepType}");
                try
                {
                    decoded = StepDispatcher.Decode(stepType, r);
                    if (Tracing)
                        Console.WriteLine($"        -> consumed {r.Pos - posBefore} bytes; now pos={r.Pos}");
                }
                catch (UnknownStepException uex)
                {
                    g.DecodingFailed = true;
                    g.DecodingError = uex.Message + $" (chapter {i}, step {s}, lastDecoded={lastType})";
                    g.LastDecodedChapter = i;
                    g.LastDecodedStep = s;
                    g.LastDecodedType = lastType;
                    g.CrashedAtType = stepType;
                    return;
                }
                catch (Exception ex)
                {
                    if (Tracing)
                    {
                        var sb = new System.Text.StringBuilder();
                        for (int b = posBefore; b < Math.Min(posBefore + 192, data.Length); b++)
                            sb.Append($"{data[b]:X2} ");
                        Console.WriteLine($"        CRASH at ch{i} s{s} type={stepType} pos={posBefore} cursorAtCrash={r.Pos}: {ex.GetType().Name}: {ex.Message}");
                        Console.WriteLine($"        bytes from posBefore: {sb}");
                    }
                    g.DecodingFailed = true;
                    g.DecodingError = $"{ex.GetType().Name}: {ex.Message} (chapter {i}, step {s}, type {stepType}, lastDecoded={lastType})";
                    g.LastDecodedChapter = i;
                    g.LastDecodedStep = s;
                    g.LastDecodedType = lastType;
                    g.CrashedAtType = stepType;
                    return;
                }
                ch.Steps.Add(decoded);
                lastType = stepType;
            }
            try { ch.ContinuationIndex = r.ReadChapterIndex(); }
            catch (Exception ex)
            {
                g.DecodingFailed = true;
                g.DecodingError = $"reading continuation for chapter {i} failed: {ex.GetType().Name}: {ex.Message} (lastDecoded={lastType})";
                g.LastDecodedChapter = i;
                g.LastDecodedType = lastType;
                return;
            }
        }

        // condition bodies — we also don't decode these yet (they don't carry
        // any displayed text), but to leave the cursor in a clean place for
        // future tools we abandon the rest of the file gracefully.
        // The cursor should now be at the start of the conditions block.
        // We don't need to advance further to extract dialog text.
    }
}
