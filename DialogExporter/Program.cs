using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DialogExporter;

public static class Program
{
    public static int Main(string[] args)
    {
        // Make stdout unbuffered so progress shows up under stdout-redirect.
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

        var opts = ParseArgs(args);
        if (opts == null) return 1;

        Console.WriteLine($"Story archive  : {opts.StoryArch}");
        Console.WriteLine($"Languages arch : {opts.LanguagesArch}");
        Console.WriteLine($"Locale         : {opts.Locale}");
        Console.WriteLine($"Out dir        : {opts.OutDir}");
        Console.WriteLine();

        if (Environment.GetEnvironmentVariable("DEXPORTER_DIAG") == "1")
        {
            DiagDumpEntries.Run(opts.LanguagesArch);
            DiagDumpEntries.Run(opts.StoryArch);
            return 0;
        }

        if (Environment.GetEnvironmentVariable("DEXPORTER_KEYS") == "1")
        {
            var b = BabelTranslator.Load(opts.LanguagesArch, opts.Locale);
            KeysExplorer.Run(b);
            return 0;
        }

        var drillEnv = Environment.GetEnvironmentVariable("DEXPORTER_DRILL");
        if (!string.IsNullOrEmpty(drillEnv))
        {
            var b2 = BabelTranslator.Load(opts.LanguagesArch, opts.Locale);
            KeysExplorer.RunDrilldown(b2, drillEnv);
            return 0;
        }
        var fullDrillEnv = Environment.GetEnvironmentVariable("DEXPORTER_FULLDRILL");
        if (!string.IsNullOrEmpty(fullDrillEnv))
        {
            var b2 = BabelTranslator.Load(opts.LanguagesArch, opts.Locale);
            KeysExplorer.RunFullDrill(b2, fullDrillEnv);
            return 0;
        }
        var guidsUnderEnv = Environment.GetEnvironmentVariable("DEXPORTER_GUIDS_UNDER");
        if (!string.IsNullOrEmpty(guidsUnderEnv))
        {
            var b2 = BabelTranslator.Load(opts.LanguagesArch, opts.Locale);
            KeysExplorer.RunGuidsUnder(b2, guidsUnderEnv);
            return 0;
        }
        var allPrefEnv = Environment.GetEnvironmentVariable("DEXPORTER_ALLPREFIXES");
        if (!string.IsNullOrEmpty(allPrefEnv))
        {
            var b2 = BabelTranslator.Load(opts.LanguagesArch, opts.Locale);
            KeysExplorer.RunAllPrefixes(b2, allPrefEnv == "1" ? null : allPrefEnv);
            return 0;
        }
        var searchEnv = Environment.GetEnvironmentVariable("DEXPORTER_SEARCH");
        if (!string.IsNullOrEmpty(searchEnv))
        {
            var b3 = BabelTranslator.Load(opts.LanguagesArch, opts.Locale);
            KeysExplorer.RunSearch(b3, searchEnv);
            return 0;
        }
        var coverageEnv = Environment.GetEnvironmentVariable("DEXPORTER_COVERAGE");
        if (!string.IsNullOrEmpty(coverageEnv))
        {
            var b4 = BabelTranslator.Load(opts.LanguagesArch, opts.Locale);
            KeysExplorer.RunCoverage(b4, coverageEnv);
            return 0;
        }

        // 1. Load babel translator
        Console.WriteLine("Loading babel...");
        var babel = BabelTranslator.Load(opts.LanguagesArch, opts.Locale);
        Console.WriteLine($"  keys         : {babel.Keys.Length}");
        Console.WriteLine($"  translations : {babel.Translations.Length}");
        Console.WriteLine($"  available    : {string.Join(", ", babel.AvailableLocales)}");
        Console.WriteLine();

        Directory.CreateDirectory(opts.OutDir);
        string dialogsDir = Path.Combine(opts.OutDir, "dialogs");
        Directory.CreateDirectory(dialogsDir);
        string flatDir = Path.Combine(opts.OutDir, "flat");
        Directory.CreateDirectory(flatDir);

        StoryArchiveReader.TraceGuid = opts.TraceGuid;
        Console.WriteLine("Mounting story archive...");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 2. Walk every .story file, decode, then resolve actor names, then dump
        int total = 0, success = 0, partial = 0, failed = 0;
        long textSteps = 0, choiceSteps = 0;

        var manifest = new List<object>();
        var unhandledTypes = new SortedDictionary<byte, int>();
        var lastSuccessfulHist = new SortedDictionary<int, int>();
        var crashedAtHist = new SortedDictionary<int, int>();

        // Decode all graphs first so we can build the actor name map from
        // them in one pass (the resolver needs to see every speech line).
        var decodedGraphs = new List<StoryGraphData>();
        foreach (var graph in StoryArchiveReader.ReadAll(opts.StoryArch))
        {
            total++;
            if (opts.Limit > 0 && total > opts.Limit) break;
            decodedGraphs.Add(graph);
            if (total <= 10 || total % 200 == 0)
                Console.WriteLine($"  [{sw.Elapsed.TotalSeconds:F1}s] decoded {total}: {graph.Guid}");
        }
        Console.WriteLine($"  [{sw.Elapsed.TotalSeconds:F1}s] decoded {decodedGraphs.Count} graphs total");

        // Build the actor name resolver from the dialog corpus.
        var actors = new ActorNameResolver();
        actors.Build(decodedGraphs, babel);
        Console.WriteLine($"  actor labels : {actors.Labels.Count}");

        foreach (var graph in decodedGraphs)
        {
            var jsonPath = Path.Combine(dialogsDir, graph.Guid + ".json");

            // Build a JSON document for this story
            var doc = BuildDocument(graph, babel, actors, ref textSteps, ref choiceSteps);
            File.WriteAllText(jsonPath, doc.ToString(Formatting.Indented));

            // Flat human-readable dump
            var flat = BuildFlatDump(graph, babel, actors);
            if (!string.IsNullOrEmpty(flat))
                File.WriteAllText(Path.Combine(flatDir, graph.Guid + ".txt"), flat);

            if (graph.DecodingFailed)
            {
                if (graph.DecodingError != null && graph.DecodingError.StartsWith("Unhandled step type "))
                {
                    int spStart = "Unhandled step type ".Length;
                    int spEnd = graph.DecodingError.IndexOf(' ', spStart);
                    if (spEnd > spStart && byte.TryParse(graph.DecodingError.AsSpan(spStart, spEnd - spStart), out byte t))
                    {
                        unhandledTypes.TryGetValue(t, out int c);
                        unhandledTypes[t] = c + 1;
                    }
                }
                if (graph.LastDecodedType >= 0)
                {
                    lastSuccessfulHist.TryGetValue(graph.LastDecodedType, out int c);
                    lastSuccessfulHist[graph.LastDecodedType] = c + 1;
                }
                if (graph.CrashedAtType >= 0)
                {
                    crashedAtHist.TryGetValue(graph.CrashedAtType, out int c);
                    crashedAtHist[graph.CrashedAtType] = c + 1;
                }
                if (graph.LastDecodedChapter >= 0) partial++;
                else failed++;
            }
            else
            {
                success++;
            }

            manifest.Add(new
            {
                guid = graph.Guid,
                tags = graph.Tags,
                chapters = graph.Chapters?.Length ?? 0,
                shared = graph.SharedBetweenMultipleNPCs,
                ok = !graph.DecodingFailed,
                error = graph.DecodingError,
                textSteps = CountSteps<SaySpeechStep>(graph),
                choiceSteps = CountSteps<ChoiceStep>(graph),
                file = "dialogs/" + graph.Guid + ".json",
            });
        }

        // Generate the consolidated lore.txt
        Console.WriteLine();
        Console.WriteLine("Building lore.txt...");
        LoreConsolidator.Generate(opts.OutDir, decodedGraphs, babel, actors);
        Console.WriteLine($"  lore.txt    : {new FileInfo(Path.Combine(opts.OutDir, "lore.txt")).Length / 1024} KB");

        // If --split is given, also build a categorised folder layout there.
        if (!string.IsNullOrEmpty(opts.SplitDir))
        {
            Console.WriteLine();
            Console.WriteLine($"Splitting into categorised folder at: {opts.SplitDir}");
            LoreSplitter.SplitInto(opts.SplitDir, decodedGraphs, babel, actors);
            Console.WriteLine("  done.");
        }

        File.WriteAllText(
            Path.Combine(opts.OutDir, "manifest.json"),
            JsonConvert.SerializeObject(manifest, Formatting.Indented));

        Console.WriteLine();
        Console.WriteLine($"Stories total : {total}");
        Console.WriteLine($"  full ok     : {success}");
        Console.WriteLine($"  partial     : {partial}  (decoded up to an unknown step type)");
        Console.WriteLine($"  failed      : {failed}   (decoded zero chapters)");
        Console.WriteLine($"  text steps  : {textSteps}");
        Console.WriteLine($"  choice steps: {choiceSteps}");
        Console.WriteLine();

        if (unhandledTypes.Count > 0)
        {
            Console.WriteLine("Unhandled step types (sorted by frequency):");
            foreach (var kv in unhandledTypes.OrderByDescending(kv => kv.Value))
            {
                Console.WriteLine($"  type {kv.Key} (0x{kv.Key:X2}) : {kv.Value} stories blocked");
            }
        }

        if (lastSuccessfulHist.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Last-successful step type before failure (top suspects):");
            foreach (var kv in lastSuccessfulHist.OrderByDescending(kv => kv.Value).Take(15))
            {
                Console.WriteLine($"  type {kv.Key,3} (0x{kv.Key:X2}) : {kv.Value} stories");
            }
        }

        if (crashedAtHist.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Step type byte at crash position (high-byte values likely garbage from drift):");
            foreach (var kv in crashedAtHist.OrderByDescending(kv => kv.Value).Take(15))
            {
                Console.WriteLine($"  type {kv.Key,3} (0x{kv.Key:X2}) : {kv.Value} stories");
            }
        }

        return 0;
    }

    private static int CountSteps<T>(StoryGraphData g)
    {
        if (g.Chapters == null) return 0;
        int n = 0;
        foreach (var ch in g.Chapters)
            foreach (var s in ch.Steps)
                if (s is T) n++;
        return n;
    }

    private static JObject BuildDocument(StoryGraphData g, BabelTranslator babel, ActorNameResolver actors, ref long textCount, ref long choiceCount)
    {
        var root = new JObject
        {
            ["guid"] = g.Guid,
            ["tags"] = JArray.FromObject(g.Tags ?? Array.Empty<string>()),
            ["sharedBetweenMultipleNPCs"] = g.SharedBetweenMultipleNPCs,
            ["soundBanks"] = JArray.FromObject(g.UsedSoundBanks ?? Array.Empty<string>()),
        };
        if (g.DecodingFailed)
        {
            root["decodingError"] = g.DecodingError;
            root["lastDecodedChapter"] = g.LastDecodedChapter;
            root["lastDecodedStep"] = g.LastDecodedStep;
        }

        if (g.StartNode != null)
        {
            var sn = new JObject
            {
                ["enableChoices"] = g.StartNode.EnableChoices,
                ["involveHero"] = g.StartNode.InvolveHero,
                ["involveAI"] = g.StartNode.InvolveAI,
                ["continuationChapter"] = g.StartNode.ContinuationChapterIndex,
                ["choices"] = new JArray(g.StartNode.Choices.Select(c => StepToJson(c, babel, actors))),
            };
            root["startNode"] = sn;
        }

        var chaptersArr = new JArray();
        if (g.Chapters != null)
        {
            foreach (var ch in g.Chapters)
            {
                var stepsArr = new JArray();
                foreach (var s in ch.Steps)
                {
                    stepsArr.Add(StepToJson(s, babel, actors));
                    if (s is SaySpeechStep) textCount++;
                    else if (s is ChoiceStep) choiceCount++;
                }
                chaptersArr.Add(new JObject
                {
                    ["index"] = ch.Index,
                    ["continuation"] = ch.ContinuationIndex,
                    ["steps"] = stepsArr,
                });
            }
        }
        root["chapters"] = chaptersArr;
        return root;
    }

    private static JObject StepToJson(IStepInfo s, BabelTranslator babel, ActorNameResolver actors)
    {
        var o = new JObject { ["kind"] = s.Kind };
        switch (s)
        {
            case SaySpeechStep t:
                o["actor"] = actors.Resolve(t.ActorGuid);
                o["actorGuid"] = t.ActorGuid;
                o["targetActor"] = actors.Resolve(t.TargetActorGuid);
                o["targetActorGuid"] = t.TargetActorGuid;
                o["lookAtOnlyWithHead"] = t.LookAtOnlyWithHead;
                o["text"] = babel.TranslateClean(t.TextId);
                o["textKey"] = babel.KeyOf(t.TextId);
                o["gestureKey"] = t.GestureKey;
                o["audioFmodGuid"] = FormatFmodGuid(t.AudioFmodGuid);
                o["hasVoice"] = t.HasVoice;
                if (t.IconGuid != null) o["iconGuid"] = t.IconGuid;
                break;

            case ChoiceStep c:
                o["choiceKind"] = c.ChoiceKindFlag.ToString();
                o["text"] = babel.TranslateClean(c.Choice.textIdx);
                o["textKey"] = babel.KeyOf(c.Choice.textIdx);
                o["isMain"] = c.Choice.isMain;
                o["targetChapter"] = c.TargetChapter;
                if (c.ChoiceKindFlag == ChoiceKind.Exit)
                    o["hiddenFromPlayer"] = c.HiddenFromPlayer;
                break;

            case StoryStartChoiceStep ss:
                o["text"] = babel.TranslateClean(ss.TextId);
                o["textKey"] = babel.KeyOf(ss.TextId);
                o["targetChapter"] = ss.TargetChapter;
                break;

            case BookmarkStep b:
                o["flag"] = b.Flag;
                o["storySettings"] = b.StorySettings;
                break;

            case TutorialTextStep tt:
                o["title"]    = babel.TranslateByKeyClean(tt.TitleId);
                o["text"]     = babel.TranslateByKeyClean(tt.TextId);
                o["titleKey"] = tt.TitleId;
                o["textKey"]  = tt.TextId;
                break;

            case FakeInteractionPromptStep fip:
                o["title"]    = babel.TranslateByKeyClean(fip.TitleId);
                o["text"]     = babel.TranslateByKeyClean(fip.TextId);
                o["titleKey"] = fip.TitleId;
                o["textKey"]  = fip.TextId;
                break;

            case FancyPanelStep fp:
                o["text"]      = babel.TranslateClean(fp.TextId);
                o["textKey"]   = babel.KeyOf(fp.TextId);
                o["panelType"] = fp.PanelType;
                break;

            case OnHoverChoicePreviewStep ohp:
                o["text"]    = babel.TranslateByKeyClean(ohp.ProficiencySetKey);
                o["textKey"] = ohp.ProficiencySetKey;
                break;

            case OpenHouseUnlockStep ohu:
                o["houseName"]           = babel.TranslateClean(ohu.HouseNameId);
                o["houseNameKey"]        = babel.KeyOf(ohu.HouseNameId);
                o["houseDescription"]    = babel.TranslateClean(ohu.HouseDescriptionId);
                o["houseDescriptionKey"] = babel.KeyOf(ohu.HouseDescriptionId);
                o["price"]               = ohu.Price;
                break;

            case PlayTutorialVideoStep ptv:
                o["title"]    = babel.TranslateByKeyClean(ptv.TitleId);
                o["text"]     = babel.TranslateByKeyClean(ptv.TextId);
                o["titleKey"] = ptv.TitleId;
                o["textKey"]  = ptv.TextId;
                break;

            case ShowTutorialGraphicStep stg:
                o["title"]      = babel.TranslateByKeyClean(stg.TitleId);
                o["text"]       = babel.TranslateByKeyClean(stg.TextId);
                o["titleKey"]   = stg.TitleId;
                o["textKey"]    = stg.TextId;
                o["spriteGuid"] = stg.SpriteGuid;
                break;

            case StatDependantChoiceStep sdc:
                o["text"]                  = babel.TranslateClean(sdc.OverrideLabelId);
                o["textKey"]               = babel.KeyOf(sdc.OverrideLabelId);
                o["affectedStat"]          = sdc.AffectedStat;
                o["statValue"]             = sdc.StatValue;
                o["comparison"]            = sdc.Comparison;
                o["certainSuccessAtValue"] = sdc.CertainSuccessAtValue;
                o["isVisibleToPlayer"]     = sdc.IsVisibleToPlayer;
                o["isChanceVisible"]       = sdc.IsChanceVisible;
                o["targetChapter"]         = sdc.SuccessChapter;
                o["requirementType"]       = sdc.RequirementType;
                break;

            case GenericStep g:
                o["kind"] = g.Kind;
                break;
        }
        return o;
    }

    private static string FormatFmodGuid(byte[] g)
    {
        if (g == null || g.Length != 16) return null;
        // FMOD GUID is 16 bytes; render as a stable hex string
        return Convert.ToHexString(g);
    }

    // Render the dialog tree as plain text so it's grep-able / readable.
    // We linearize by chapter index and show each step on one line.
    private static string BuildFlatDump(StoryGraphData g, BabelTranslator babel, ActorNameResolver actors)
    {
        if (g.Chapters == null || g.Chapters.Length == 0) return null;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {g.Guid}");
        if (g.Tags?.Length > 0) sb.AppendLine($"# tags: {string.Join(", ", g.Tags)}");
        if (g.SharedBetweenMultipleNPCs) sb.AppendLine("# (shared graph)");
        if (g.DecodingFailed) sb.AppendLine($"# DECODING FAILED: {g.DecodingError}");
        sb.AppendLine();

        foreach (var ch in g.Chapters)
        {
            bool any = ch.Steps.Any(HasRenderableContent);
            if (!any) continue;
            sb.AppendLine($"--- chapter {ch.Index} (-> {ch.ContinuationIndex}) ---");
            foreach (var s in ch.Steps)
            {
                switch (s)
                {
                    case BookmarkStep b:
                        sb.AppendLine($"  [bookmark: {b.Flag}]");
                        break;
                    case SaySpeechStep t:
                        sb.AppendLine($"  {actors.Resolve(t.ActorGuid)}: {babel.TranslateClean(t.TextId)}");
                        break;
                    case ChoiceStep c:
                        string text = babel.TranslateClean(c.Choice.textIdx);
                        string tag = c.ChoiceKindFlag switch
                        {
                            ChoiceKind.Exit => "[exit]",
                            ChoiceKind.Submenu => "[submenu]",
                            _ => c.Choice.isMain ? "[*]" : "",
                        };
                        sb.AppendLine($"  > {tag} {text}  -> ch {c.TargetChapter}");
                        break;
                    case StoryStartChoiceStep ss:
                        sb.AppendLine($"  > [start] {babel.TranslateClean(ss.TextId)}  -> ch {ss.TargetChapter}");
                        break;
                    case TutorialTextStep tt:
                        sb.AppendLine($"  [tutorial] {babel.TranslateByKeyClean(tt.TitleId)}: {babel.TranslateByKeyClean(tt.TextId)}");
                        break;
                    case FakeInteractionPromptStep fip:
                        sb.AppendLine($"  [prompt] {babel.TranslateByKeyClean(fip.TitleId)}: {babel.TranslateByKeyClean(fip.TextId)}");
                        break;
                    case FancyPanelStep fp:
                    {
                        string panelText = babel.TranslateClean(fp.TextId);
                        if (!string.IsNullOrWhiteSpace(panelText))
                            sb.AppendLine($"  [panel] {panelText}");
                        break;
                    }
                    case OnHoverChoicePreviewStep ohp:
                    {
                        string hover = babel.TranslateByKeyClean(ohp.ProficiencySetKey);
                        if (!string.IsNullOrWhiteSpace(hover))
                            sb.AppendLine($"  [hover-preview] {hover}");
                        break;
                    }
                    case OpenHouseUnlockStep ohu:
                    {
                        string nm = babel.TranslateClean(ohu.HouseNameId);
                        string ds = babel.TranslateClean(ohu.HouseDescriptionId);
                        sb.AppendLine($"  [house unlock] {nm}: {ds} (price {ohu.Price})");
                        break;
                    }
                    case PlayTutorialVideoStep ptv:
                        sb.AppendLine($"  [video] {babel.TranslateByKeyClean(ptv.TitleId)}: {babel.TranslateByKeyClean(ptv.TextId)}");
                        break;
                    case ShowTutorialGraphicStep stg:
                        sb.AppendLine($"  [graphic] {babel.TranslateByKeyClean(stg.TitleId)}: {babel.TranslateByKeyClean(stg.TextId)}");
                        break;
                    case StatDependantChoiceStep sdc:
                    {
                        string label = babel.TranslateClean(sdc.OverrideLabelId);
                        if (string.IsNullOrWhiteSpace(label)) label = "(stat-gated choice)";
                        string stat = string.IsNullOrEmpty(sdc.AffectedStat) ? "stat" : ShortStatName(sdc.AffectedStat);
                        sb.AppendLine($"  > [{stat} check, value {sdc.StatValue:G}] {label}  -> ch {sdc.SuccessChapter}");
                        break;
                    }
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // Step types that surface text content in the flat dump.
    private static bool HasRenderableContent(IStepInfo s) =>
        s is SaySpeechStep or ChoiceStep or StoryStartChoiceStep or BookmarkStep or
              TutorialTextStep or FakeInteractionPromptStep or FancyPanelStep or
              OnHoverChoicePreviewStep or OpenHouseUnlockStep or PlayTutorialVideoStep or
              ShowTutorialGraphicStep or StatDependantChoiceStep;

    // RichEnumReference serializes as "Awaken.TG.Main…HeroRPGStatType, TG.Main, …:Practicality"
    // — the leaf after the last ':' or '/' is the actual stat name.
    private static string ShortStatName(string richEnumRef)
    {
        if (string.IsNullOrEmpty(richEnumRef)) return "?";
        int sep = Math.Max(richEnumRef.LastIndexOf(':'), richEnumRef.LastIndexOf('/'));
        return sep >= 0 ? richEnumRef[(sep + 1)..] : richEnumRef;
    }

    private static string ShortActor(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return "?";
        if (guid == "Hero" || guid == "None") return guid;
        // Truncate UUID-style actors to first 8 chars
        return guid.Length > 8 ? guid[..8] : guid;
    }

    // ---- args ----

    private sealed class Options
    {
        public string StoryArch;
        public string LanguagesArch;
        public string Locale = "en";
        public string OutDir = "exports";
        public int Limit = -1;
        public string TraceGuid = null;
        public string SplitDir = null;
    }

    private static Options ParseArgs(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--story":      o.StoryArch     = args[++i]; break;
                case "--languages":  o.LanguagesArch = args[++i]; break;
                case "--locale":     o.Locale        = args[++i]; break;
                case "--out":        o.OutDir        = args[++i]; break;
                case "--limit":      o.Limit         = int.Parse(args[++i]); break;
                case "--trace":      o.TraceGuid     = args[++i]; break;
                case "--split":      o.SplitDir      = args[++i]; break;
                case "--game":
                    string root = args[++i];
                    o.StoryArch     = Path.Combine(root, "Fall of Avalon_Data", "StreamingAssets", "Stroy", "story.arch");
                    o.LanguagesArch = Path.Combine(root, "Fall of Avalon_Data", "StreamingAssets", "Languages", "languages.arch");
                    break;
                case "-h": case "--help":
                    PrintUsage(); return null;
            }
        }
        // Default: assume the standard Steam install
        if (o.StoryArch == null)
        {
            string defaultRoot = @"C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA";
            o.StoryArch = Path.Combine(defaultRoot, "Fall of Avalon_Data", "StreamingAssets", "Stroy", "story.arch");
            o.LanguagesArch = Path.Combine(defaultRoot, "Fall of Avalon_Data", "StreamingAssets", "Languages", "languages.arch");
        }
        if (!File.Exists(o.StoryArch))
        {
            Console.Error.WriteLine($"Story archive not found: {o.StoryArch}");
            PrintUsage();
            return null;
        }
        if (!File.Exists(o.LanguagesArch))
        {
            Console.Error.WriteLine($"Languages archive not found: {o.LanguagesArch}");
            PrintUsage();
            return null;
        }
        return o;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("DialogExporter --game <gameRoot> [--locale en] [--out exports]");
        Console.WriteLine("  or --story <story.arch> --languages <languages.arch>");
    }
}
