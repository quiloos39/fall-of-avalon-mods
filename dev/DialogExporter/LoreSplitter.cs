using System.Text;

namespace DialogExporter;

// Builds the LLM-friendly export layout: one file per discrete subject
// (character / quest / book / notice / tutorial / cutscene / encyclopedia
// entry) so a single read answers a single question without cross-referencing.
//
// Layout:
//   characters/<Name>.md          — speaker bio + every line they say
//   quests/<Name>.md              — quest description + tracker + endings
//   readables/<Title>.md          — full text of one book/letter/note/tablet
//   notices/<Title>.md            — one notice-board post or bounty
//   tutorials/<Title>.md          — one tutorial flow (text panels)
//   cutscenes/<Title>.md          — one cutscene narration
//   encyclopedia/<Entry>.md       — one bestiary/lore/journal entry
//   reference/                    — flat tables (items, talents, factions, …)
//   README.md                     — top-level navigation
public static class LoreSplitter
{
    public static void SplitInto(string targetDir, IList<StoryGraphData> graphs, BabelTranslator babel, ActorNameResolver actors)
    {
        Directory.CreateDirectory(targetDir);

        var lore = LoreExtractor.ExtractAll(babel);

        // Bucket records by what fields they carry.
        var items     = new List<LoreRecord>();
        var npcs      = new List<LoreRecord>();
        var factions  = new List<LoreRecord>();
        var talents   = new List<LoreRecord>();
        var quests    = new List<LoreRecord>();
        var journals  = new List<LoreRecord>();
        var questRecs = new List<LoreRecord>();
        var others    = new List<LoreRecord>();
        foreach (var rec in lore.Values)
        {
            bool hasItemName    = rec.Fields.ContainsKey("itemName");
            bool hasFactionName = rec.Fields.ContainsKey("factionName");
            bool hasTalentName  = rec.Fields.ContainsKey("talentName");
            bool hasDisplayName = rec.Fields.ContainsKey("displayName");
            bool isNpc          = hasDisplayName && rec.AssetGuid == LoreExtractor.NpcTypeHash;
            bool hasTrackerDesc = rec.Fields.ContainsKey("trackerDescription");
            bool isJournal      = rec.Fields.ContainsKey("data_description") ||
                                  rec.Fields.ContainsKey("data_entryName") ||
                                  rec.Fields.ContainsKey("data_name");
            bool isQuestRecords = rec.Fields.Keys.Any(f => f.StartsWith("records_Array"));
            if (hasItemName)         items.Add(rec);
            else if (hasFactionName) factions.Add(rec);
            else if (hasTalentName)  talents.Add(rec);
            else if (isNpc)          npcs.Add(rec);
            else if (isJournal)      journals.Add(rec);
            else if (isQuestRecords) questRecs.Add(rec);
            else if (hasDisplayName && (hasTrackerDesc || rec.Fields.ContainsKey("description")))
                                     quests.Add(rec);
            else                     others.Add(rec);
        }

        // Classify every story by inspecting babel-key prefixes used by its
        // dialog/choice steps. This routes books, letters, notice boards,
        // bounties, tutorials and cutscenes to their dedicated folders.
        var classified = new Dictionary<string, (StoryKind kind, string prefix)>();
        var byKind     = new Dictionary<StoryKind, List<StoryGraphData>>();
        foreach (var g in graphs)
        {
            string prefix = StoryClassifier.DominantPrefix(g, babel);
            // Skip dev/test/debug stories — they're never reachable in-game.
            if (IsTestStory(prefix)) continue;
            var kind = StoryClassifier.Classify(prefix);
            classified[g.Guid] = (kind, prefix);
            if (!byKind.TryGetValue(kind, out var list)) list = byKind[kind] = new();
            list.Add(g);
        }

        // -- per-subject folders --
        WriteReadablesPerFile(Path.Combine(targetDir, "readables"),
                              "readables",
                              new[] { StoryKind.Book, StoryKind.Letter, StoryKind.Note, StoryKind.Tablet },
                              "books, letters, notes, and tablets",
                              byKind, classified, babel);

        WriteReadablesPerFile(Path.Combine(targetDir, "notices"),
                              "notices",
                              new[] { StoryKind.NoticeBoard, StoryKind.Bounty },
                              "notice-board posts and bounties",
                              byKind, classified, babel);

        WriteReadablesPerFile(Path.Combine(targetDir, "tutorials"),
                              "tutorials",
                              new[] { StoryKind.Tutorial },
                              "tutorial popups",
                              byKind, classified, babel);

        WriteReadablesPerFile(Path.Combine(targetDir, "cutscenes"),
                              "cutscenes",
                              new[] { StoryKind.Cutscene },
                              "cutscene narration",
                              byKind, classified, babel);

        WriteQuestsPerFile(Path.Combine(targetDir, "quests"), quests, questRecs);

        WriteEncyPerFile(Path.Combine(targetDir, "encyclopedia"), journals);

        // -- per-character (NPCs + anonymous speakers) --
        CharacterProfiler.Run(targetDir, graphs, babel, actors, classified);

        // -- safety net: any classified-as-Dialogue story whose content is NOT
        //    yet captured anywhere (typically interaction-only stories with
        //    only Choice/Bookmark steps and no SaySpeechStep — "(Pray)",
        //    "(Make an offering)", etc.) goes to interactions/.
        WriteInteractionsPerFile(Path.Combine(targetDir, "interactions"), graphs, classified, babel, actors);

        // -- reference tables (flat lists) --
        string refDir = Path.Combine(targetDir, "reference");
        Directory.CreateDirectory(refDir);
        WriteItems   (Path.Combine(refDir, "items.md"),    items);
        WriteNpcs    (Path.Combine(refDir, "npcs.md"),     npcs);
        WriteFactions(Path.Combine(refDir, "factions.md"), factions);
        WriteTalents (Path.Combine(refDir, "talents.md"),  talents);
        WriteWorldText(Path.Combine(refDir, "world-text.md"), babel);
        WriteBarks   (Path.Combine(refDir, "barks.md"),    byKind, classified, babel);
        WriteOthers  (Path.Combine(refDir, "misc-records.md"), others);

        // Top-level README: navigation map for the whole export.
        WriteReadme(Path.Combine(targetDir, "README.md"),
                    targetDir,
                    items.Count, npcs.Count, quests.Count, factions.Count, talents.Count,
                    journals.Count, questRecs.Count, byKind);
    }

    // ---- per-subject writers (one .md file per item) ----------------------

    static void WriteReadablesPerFile(string dir, string slug, StoryKind[] kinds, string description,
                                       Dictionary<StoryKind, List<StoryGraphData>> byKind,
                                       Dictionary<string, (StoryKind, string)> classified,
                                       BabelTranslator babel)
    {
        Directory.CreateDirectory(dir);
        var stories = kinds.Where(byKind.ContainsKey).SelectMany(k => byKind[k]).ToList();
        stories = stories.Where(g => !IsTestStory(classified[g.Guid].Item2)).ToList();

        // Render each story's body once so we can dedupe by content. Many
        // games place the same shrine/altar prompt at multiple physical
        // locations — the content is identical and an LLM only needs to
        // read it once.
        var rendered = stories.Select(g =>
        {
            var (kind, prefix) = classified[g.Guid];
            string title = StoryClassifier.TitleFor(prefix) ?? "(untitled)";
            string hint  = ContentHint(g, babel);
            var (body, lines) = RenderReadableBody(kind, g, babel);
            return new
            {
                Story = g, Kind = kind, Prefix = prefix, Title = title, Hint = hint,
                Body  = body, Lines = lines,
                BodyKey = NormaliseForDedup(body),
            };
        }).ToList();

        // Bucket by (title + body) — stories sharing both collapse into one file.
        var dedupGroups = rendered.GroupBy(m => (m.Title, m.BodyKey)).Select(g =>
        {
            var first = g.First();
            return new
            {
                Story  = first.Story,
                Kind   = first.Kind,
                Prefix = first.Prefix,
                Title  = first.Title,
                Hint   = first.Hint,
                Body   = first.Body,
                Lines  = first.Lines,
                Duplicates = g.Select(m => m.Story.Guid).ToList(),
            };
        }).ToList();

        // Then group surviving members by title to assign clean filenames.
        var titleGroups = dedupGroups.GroupBy(m => m.Title, StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string title, StoryKind kind, string file, int lines, int dups)>();
        foreach (var group in titleGroups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            string baseName = SafeFilename(group.Key);
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in members)
            {
                string disambig = members.Count > 1 ? m.Hint : null;
                string candidate = !string.IsNullOrEmpty(disambig)
                    ? $"{baseName}_{SafeFilename(disambig)}"
                    : baseName;
                seen.TryGetValue(candidate, out int n);
                seen[candidate] = n + 1;
                string fileName = n == 0 ? candidate + ".md" : $"{candidate}_{n + 1}.md";
                string displayTitle = members.Count > 1 && !string.IsNullOrEmpty(disambig)
                    ? $"{m.Title} — {disambig}"
                    : m.Title;

                using var w = new StreamWriter(Path.Combine(dir, fileName));
                w.WriteLine($"# {displayTitle}");
                w.WriteLine();
                w.WriteLine($"_Type:_ **{m.Kind}**  ");
                if (m.Duplicates.Count > 1)
                {
                    w.WriteLine($"_Identical content placed at {m.Duplicates.Count} locations in the world._  ");
                    w.WriteLine($"_Story ids:_ `{string.Join("`, `", m.Duplicates)}`");
                }
                else
                {
                    w.WriteLine($"_Story id:_ `{m.Story.Guid}`");
                }
                w.WriteLine();
                w.Write(m.Body);
                entries.Add((displayTitle, m.Kind, fileName, m.Lines, m.Duplicates.Count));
            }
        }

        WriteFolderIndex(Path.Combine(dir, "_index.md"),
            slug, description, entries.Count,
            entries.Select(e =>
            {
                string note = e.dups > 1 ? $"{e.lines} text steps · {e.dups} copies" : $"{e.lines} text steps";
                return ($"{e.kind}", e.title, e.file, note);
            }));
    }

    // Cheap content-hash key for dedup. Lower-case + collapse whitespace so
    // tiny formatting differences don't defeat dedup.
    static string NormaliseForDedup(string body)
    {
        if (string.IsNullOrEmpty(body)) return "";
        var sb = new StringBuilder(body.Length);
        bool prevSpace = false;
        foreach (var c in body)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(char.ToLowerInvariant(c));
                prevSpace = false;
            }
        }
        return sb.ToString();
    }

    // Skip dev/test/debug stories that aren't player-facing content.
    static bool IsTestStory(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return false;
        if (prefix.StartsWith("Debug_", StringComparison.OrdinalIgnoreCase)) return true;
        if (prefix.StartsWith("TalkTest", StringComparison.OrdinalIgnoreCase)) return true;
        if (prefix.IndexOf("_StoryTest", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (prefix.IndexOf("_Test_", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // Take a short, filename-safe identifying phrase from the first piece of
    // text content in the story. Used to disambiguate stories that share the
    // same source-prefix (e.g. all "Patient" notes get the patient's name).
    static string ContentHint(StoryGraphData g, BabelTranslator babel)
    {
        if (g.Chapters == null) return null;
        foreach (var ch in g.Chapters.OrderBy(c => c.Index))
            foreach (var step in ch.Steps)
            {
                string raw = step switch
                {
                    SaySpeechStep t        => babel.TranslateClean(t.TextId),
                    ChoiceStep c           => babel.TranslateClean(c.Choice.textIdx),
                    StoryStartChoiceStep s => babel.TranslateClean(s.TextId),
                    FancyPanelStep fp      => babel.TranslateClean(fp.TextId),
                    TutorialTextStep tt    => babel.TranslateByKeyClean(tt.TitleId) ?? babel.TranslateByKeyClean(tt.TextId),
                    FakeInteractionPromptStep fip => babel.TranslateByKeyClean(fip.TitleId) ?? babel.TranslateByKeyClean(fip.TextId),
                    PlayTutorialVideoStep ptv => babel.TranslateByKeyClean(ptv.TitleId) ?? babel.TranslateByKeyClean(ptv.TextId),
                    ShowTutorialGraphicStep stg => babel.TranslateByKeyClean(stg.TitleId) ?? babel.TranslateByKeyClean(stg.TextId),
                    _ => null,
                };
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string hint = ExtractHint(raw);
                if (!string.IsNullOrWhiteSpace(hint)) return hint;
            }
        return null;
    }

    // Generic field labels that, when they appear on the first line of a
    // readable, are not useful as a hint — we want what comes AFTER them.
    static readonly HashSet<string> GenericLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Author", "By", "Date", "Patient", "Name", "Title", "Subject",
        "From", "To", "Re", "Note", "Issued by", "Wanted", "Last seen",
        "Notable features", "Reward", "Bounty"
    };

    // Pull a short identifying phrase out of a piece of text.
    static string ExtractHint(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // First non-empty line. Strip markdown bold/italic markers and
        // surrounding whitespace so "**Wanted:** The head of …" becomes
        // "Wanted: The head of …".
        string first = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = StripMarkdownEmphasis(raw).Trim();
            if (!string.IsNullOrWhiteSpace(line)) { first = line; break; }
        }
        if (string.IsNullOrWhiteSpace(first)) return null;

        // Strip leading parens like "(Pray)" → "Pray".
        first = first.Trim('(', ')', ' ').Trim();

        // "Field: Value" pattern — if the field is a generic label (Author,
        // Wanted, Patient, …) recurse on the value side. Match either
        // "Field: Value" or "Field:Value" since markdown emphasis stripping
        // can leave the colon flush to the next word.
        int colon = first.IndexOf(':');
        if (colon > 0 && colon < 25)
        {
            string label = first[..colon].Trim();
            if (GenericLabels.Contains(label))
            {
                string value = first[(colon + 1)..].TrimStart(' ', '\t');
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var nested = ExtractHint(value);
                    if (!string.IsNullOrWhiteSpace(nested)) return nested;
                }
            }
        }

        // Stop at the first natural break, but prefer a longer chunk if
        // the immediate cut produces something too short to be useful
        // ("A" from "A. H. Bale" — keep walking until we have ≥3 chars).
        string chunk = TakeMeaningfulChunk(first);
        chunk = chunk.Trim();
        if (chunk.Length > 35)
        {
            int sp = chunk.LastIndexOf(' ', 34);
            chunk = sp > 10 ? chunk[..sp] : chunk[..35];
        }
        return chunk.Trim();
    }

    // Take the first sentence (or N chars worth of words) for use as a
    // disambiguating quest title. Stops at the first period/exclamation/
    // question mark or after `maxChars`, preferring word boundaries.
    static string LongHint(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var first = StripMarkdownEmphasis(text).Trim();
        // Stop at first sentence end.
        int end = first.IndexOfAny(new[] { '.', '!', '?', '\n' });
        string sentence = end > 0 ? first[..end] : first;
        sentence = sentence.Trim().TrimEnd(',', ';', ':');
        if (sentence.Length <= maxChars) return sentence;
        int sp = sentence.LastIndexOf(' ', maxChars - 1);
        return sp > 10 ? sentence[..sp].TrimEnd(',', ' ') : sentence[..maxChars];
    }

    // Strip Markdown bold (`**…**`) and italic (`*…*` / `_…_`) markers so
    // emphasis doesn't get baked into the filename hint.
    static string StripMarkdownEmphasis(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\*\*([^*]+)\*\*", "$1");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\*([^*\n]+)\*", "$1");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"_([^_\n]+)_", "$1");
        return s;
    }

    static string TakeMeaningfulChunk(string s)
    {
        char[] breaks = { ',', ';', ':', '!', '?', '\n' };
        int dot = -1;
        // Walk through characters; break on hard punctuation, but treat '.'
        // as a soft break (skip it if the chunk so far is < 3 chars).
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (Array.IndexOf(breaks, c) >= 0) break;
            if (c == '.')
            {
                int trimmedLen = s[..i].Trim().Length;
                if (trimmedLen >= 3) break;
                // skip — likely a middle-initial like "A. " in a name.
            }
            i++;
        }
        return s[..i];
    }

    // Render the body of a readable story (no title/header — those are added
     // by the caller so dedup can compare bodies). Returns (text, lineCount).
    static (string body, int lines) RenderReadableBody(StoryKind kind, StoryGraphData g, BabelTranslator babel)
    {
        var sw = new StringWriter();
        int lines = WriteReadableBody(sw, kind, g, babel);
        return (sw.ToString(), lines);
    }

    static int WriteReadableBody(TextWriter w, StoryKind kind, StoryGraphData g, BabelTranslator babel)
    {
        int linesEmitted = 0;
        if (g.Chapters == null) { w.WriteLine("(no extractable text)"); return 0; }

        foreach (var ch in g.Chapters.OrderBy(c => c.Index))
        {
            foreach (var step in ch.Steps)
            {
                switch (step)
                {
                    case SaySpeechStep t:
                    {
                        string text = babel.TranslateClean(t.TextId);
                        if (string.IsNullOrWhiteSpace(text)) break;
                        w.WriteLine(text);
                        w.WriteLine();
                        linesEmitted++;
                        break;
                    }
                    case ChoiceStep c:
                    {
                        string text = babel.TranslateClean(c.Choice.textIdx);
                        if (string.IsNullOrWhiteSpace(text)) break;
                        w.WriteLine($"  > {text}");
                        break;
                    }
                    case TutorialTextStep tt:
                    {
                        string head = babel.TranslateByKeyClean(tt.TitleId);
                        string body = babel.TranslateByKeyClean(tt.TextId);
                        if (string.IsNullOrWhiteSpace(head) && string.IsNullOrWhiteSpace(body)) break;
                        if (!string.IsNullOrWhiteSpace(head)) w.WriteLine($"## {head}");
                        if (!string.IsNullOrWhiteSpace(body)) { w.WriteLine(body); w.WriteLine(); }
                        linesEmitted++;
                        break;
                    }
                    case FakeInteractionPromptStep fip:
                    {
                        string head = babel.TranslateByKeyClean(fip.TitleId);
                        string body = babel.TranslateByKeyClean(fip.TextId);
                        if (string.IsNullOrWhiteSpace(head) && string.IsNullOrWhiteSpace(body)) break;
                        if (!string.IsNullOrWhiteSpace(head)) w.WriteLine($"_Prompt:_ {head}");
                        if (!string.IsNullOrWhiteSpace(body)) w.WriteLine(body);
                        linesEmitted++;
                        break;
                    }
                    case FancyPanelStep fp:
                    {
                        string text = babel.TranslateClean(fp.TextId);
                        if (string.IsNullOrWhiteSpace(text)) break;
                        w.WriteLine(text);
                        w.WriteLine();
                        linesEmitted++;
                        break;
                    }
                    case PlayTutorialVideoStep ptv:
                    {
                        string head = babel.TranslateByKeyClean(ptv.TitleId);
                        string body = babel.TranslateByKeyClean(ptv.TextId);
                        if (string.IsNullOrWhiteSpace(head) && string.IsNullOrWhiteSpace(body)) break;
                        if (!string.IsNullOrWhiteSpace(head)) w.WriteLine($"## {head}");
                        if (!string.IsNullOrWhiteSpace(body)) { w.WriteLine(body); w.WriteLine(); }
                        linesEmitted++;
                        break;
                    }
                    case ShowTutorialGraphicStep stg:
                    {
                        string head = babel.TranslateByKeyClean(stg.TitleId);
                        string body = babel.TranslateByKeyClean(stg.TextId);
                        if (string.IsNullOrWhiteSpace(head) && string.IsNullOrWhiteSpace(body)) break;
                        if (!string.IsNullOrWhiteSpace(head)) w.WriteLine($"## {head}");
                        if (!string.IsNullOrWhiteSpace(body)) { w.WriteLine(body); w.WriteLine(); }
                        linesEmitted++;
                        break;
                    }
                    case StatDependantChoiceStep sdc:
                    {
                        string label = babel.TranslateClean(sdc.OverrideLabelId);
                        if (string.IsNullOrWhiteSpace(label)) break;
                        w.WriteLine($"  > [stat check] {label}");
                        break;
                    }
                }
            }
        }
        if (linesEmitted == 0) w.WriteLine("(no extractable text)");
        return linesEmitted;
    }

    // ---- interactions (safety net for choice-only / prompt-only stories) ---
    static void WriteInteractionsPerFile(string dir, IList<StoryGraphData> graphs,
                                          Dictionary<string, (StoryKind kind, string prefix)> classified,
                                          BabelTranslator babel, ActorNameResolver actors)
    {
        // A story belongs here when:
        //   - it was classified as Dialogue (not a Book/Letter/Note/Tablet/etc.)
        //   - it has at least one renderable text content (Choice/Bookmark/etc.)
        //   - it has NO SaySpeechStep (which means CharacterProfiler skipped it)
        var candidates = new List<(StoryGraphData g, string prefix)>();
        foreach (var g in graphs)
        {
            if (g.Chapters == null) continue;
            if (!classified.TryGetValue(g.Guid, out var ck) || ck.kind != StoryKind.Dialogue) continue;
            bool hasSay = false, hasChoiceOrPrompt = false;
            foreach (var ch in g.Chapters)
                foreach (var s in ch.Steps)
                {
                    if (s is SaySpeechStep) hasSay = true;
                    if (s is ChoiceStep || s is StoryStartChoiceStep || s is FakeInteractionPromptStep ||
                        s is FancyPanelStep || s is OnHoverChoicePreviewStep || s is OpenHouseUnlockStep)
                        hasChoiceOrPrompt = true;
                }
            if (!hasSay && hasChoiceOrPrompt) candidates.Add((g, ck.prefix));
        }

        if (candidates.Count == 0) return;
        Directory.CreateDirectory(dir);

        // Render every candidate to a string first, so we can dedup by content
        // (many shrines share identical interaction prompts placed at multiple
        // physical locations).
        var rendered = candidates.Select(c =>
        {
            var (body, lines) = RenderInteractionBody(c.g, babel);
            return new
            {
                Story  = c.g,
                Prefix = c.prefix,
                Title  = StoryClassifier.TitleFor(c.prefix) ?? "Story_" + c.g.Guid[..8],
                Hint   = ContentHint(c.g, babel),
                Body   = body,
                Lines  = lines,
                BodyKey = NormaliseForDedup(body),
            };
        }).ToList();

        // Dedup: same title + same body → one file mentioning all instances.
        var dedup = rendered.GroupBy(m => (m.Title, m.BodyKey)).Select(g =>
        {
            var first = g.First();
            return new
            {
                first.Story, first.Prefix, first.Title, first.Hint, first.Body, first.Lines,
                Duplicates = g.Select(m => m.Story.Guid).ToList()
            };
        }).ToList();

        var entries = new List<(string title, string file, int lines, int dups)>();
        var titleGroups = dedup.GroupBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
                                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var grp in titleGroups)
        {
            var members = grp.ToList();
            string baseName = SafeFilename(grp.Key);
            var seenLocal = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in members)
            {
                string disambig = members.Count > 1 ? m.Hint : null;
                string candidate = !string.IsNullOrEmpty(disambig)
                    ? $"{baseName}_{SafeFilename(disambig)}"
                    : baseName;
                seenLocal.TryGetValue(candidate, out int n);
                seenLocal[candidate] = n + 1;
                string fileName = n == 0 ? candidate + ".md" : $"{candidate}_{n + 1}.md";
                string displayTitle = members.Count > 1 && !string.IsNullOrEmpty(disambig)
                    ? $"{m.Title} — {disambig}"
                    : m.Title;

                using var w = new StreamWriter(Path.Combine(dir, fileName));
                w.WriteLine($"# {displayTitle}");
                w.WriteLine();
                if (m.Duplicates.Count > 1)
                {
                    w.WriteLine($"_Identical content placed at {m.Duplicates.Count} locations in the world._  ");
                    w.WriteLine($"_Source prefix:_ `{m.Prefix}`  ");
                    w.WriteLine($"_Story ids:_ `{string.Join("`, `", m.Duplicates)}`");
                }
                else
                {
                    w.WriteLine($"_Story id:_ `{m.Story.Guid}`  ");
                    w.WriteLine($"_Source prefix:_ `{m.Prefix}`");
                }
                w.WriteLine();
                w.Write(m.Body);
                entries.Add((displayTitle, fileName, m.Lines, m.Duplicates.Count));
            }
        }

        WriteFolderIndex(Path.Combine(dir, "_index.md"),
            "interactions", "interaction-prompt stories — offerings, repairs, prayers, action menus, choice-only flows",
            entries.Count,
            entries.Select(e =>
            {
                string note = e.dups > 1 ? $"{e.lines} lines · {e.dups} copies" : $"{e.lines} lines";
                return ("Interaction", e.title, e.file, note);
            }));
    }

    static (string body, int lines) RenderInteractionBody(StoryGraphData g, BabelTranslator babel)
    {
        var sw = new StringWriter();
        int lineCount = 0;
        foreach (var ch in g.Chapters.OrderBy(c => c.Index))
        {
            foreach (var step in ch.Steps)
            {
                switch (step)
                {
                    case ChoiceStep c:
                    {
                        string text = babel.TranslateClean(c.Choice.textIdx);
                        if (string.IsNullOrWhiteSpace(text)) break;
                        string tag = c.ChoiceKindFlag switch
                        {
                            ChoiceKind.Exit    => "[exit]",
                            ChoiceKind.Submenu => "[submenu]",
                            _                  => c.Choice.isMain ? "[*]" : "",
                        };
                        sw.WriteLine($"  > {tag} {text}");
                        lineCount++;
                        break;
                    }
                    case StoryStartChoiceStep ss:
                    {
                        string text = babel.TranslateClean(ss.TextId);
                        if (string.IsNullOrWhiteSpace(text)) break;
                        sw.WriteLine($"  > [start] {text}");
                        lineCount++;
                        break;
                    }
                    case FakeInteractionPromptStep fip:
                    {
                        string head = babel.TranslateByKeyClean(fip.TitleId);
                        string body = babel.TranslateByKeyClean(fip.TextId);
                        if (!string.IsNullOrWhiteSpace(head)) sw.WriteLine($"_Prompt:_ {head}");
                        if (!string.IsNullOrWhiteSpace(body)) sw.WriteLine(body);
                        if (!string.IsNullOrWhiteSpace(head) || !string.IsNullOrWhiteSpace(body)) lineCount++;
                        break;
                    }
                    case FancyPanelStep fp:
                    {
                        string text = babel.TranslateClean(fp.TextId);
                        if (string.IsNullOrWhiteSpace(text)) break;
                        sw.WriteLine(text);
                        lineCount++;
                        break;
                    }
                    case OnHoverChoicePreviewStep ohp:
                    {
                        string text = babel.TranslateByKeyClean(ohp.ProficiencySetKey);
                        if (string.IsNullOrWhiteSpace(text)) break;
                        sw.WriteLine($"  [hover] {text}");
                        lineCount++;
                        break;
                    }
                    case OpenHouseUnlockStep ohu:
                    {
                        string nm = babel.TranslateClean(ohu.HouseNameId);
                        string ds = babel.TranslateClean(ohu.HouseDescriptionId);
                        if (!string.IsNullOrWhiteSpace(nm) || !string.IsNullOrWhiteSpace(ds))
                        {
                            sw.WriteLine($"_House:_ {nm}");
                            if (!string.IsNullOrWhiteSpace(ds)) sw.WriteLine(ds);
                            lineCount++;
                        }
                        break;
                    }
                    case StatDependantChoiceStep sdc:
                    {
                        string text = babel.TranslateClean(sdc.OverrideLabelId);
                        if (string.IsNullOrWhiteSpace(text)) break;
                        sw.WriteLine($"  > [stat check] {text}");
                        lineCount++;
                        break;
                    }
                }
            }
        }
        if (lineCount == 0) sw.WriteLine("(no extractable text)");
        return (sw.ToString(), lineCount);
    }

    // ---- quests (one file per quest) --------------------------------------

    static void WriteQuestsPerFile(string dir, List<LoreRecord> quests, List<LoreRecord> questRecs)
    {
        Directory.CreateDirectory(dir);

        // questRecs (memorable moments) live on the quest's own asset guid,
        // so we can join them by guid.
        var endingsByGuid = questRecs.ToDictionary(r => r.AssetGuid, r => r);

        var entries = new List<(string title, string file, int objectives, int endings)>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var rec in quests.OrderBy(r => Get(r, "displayName") ?? r.AssetGuid))
        {
            string title = Get(rec, "displayName");
            if (string.IsNullOrWhiteSpace(title)) continue;
            string baseName = SafeFilename(title);
            seen.TryGetValue(baseName, out int n);
            seen[baseName] = n + 1;
            string fileName = n == 0 ? baseName + ".md" : $"{baseName}_{n + 1}.md";
            int objectives = 0, endings = 0;

            using var w = new StreamWriter(Path.Combine(dir, fileName));
            w.WriteLine($"# {title}");
            w.WriteLine();
            w.WriteLine($"_Asset id:_ `{rec.AssetGuid}`");
            w.WriteLine();

            string desc  = Get(rec, "description");
            string track = Get(rec, "trackerDescription");
            if (!string.IsNullOrWhiteSpace(desc))
            {
                w.WriteLine("## Description");
                w.WriteLine();
                w.WriteLine(desc);
                w.WriteLine();
            }
            if (!string.IsNullOrWhiteSpace(track) && track != desc)
            {
                w.WriteLine("## Tracker");
                w.WriteLine();
                w.WriteLine(track);
                w.WriteLine();
            }

            // Multi-stage objectives are stored as `description#1`, `trackerDescription#1`, etc.
            var stagedDesc = rec.Fields.Where(kv => kv.Key.StartsWith("description#")).OrderBy(kv => kv.Key).ToList();
            var stagedTrk  = rec.Fields.Where(kv => kv.Key.StartsWith("trackerDescription#")).OrderBy(kv => kv.Key).ToList();
            if (stagedDesc.Count > 0 || stagedTrk.Count > 0)
            {
                w.WriteLine("## Objective stages");
                w.WriteLine();
                foreach (var (k, v) in stagedDesc)
                {
                    w.WriteLine($"- {v}");
                    objectives++;
                }
                foreach (var (k, v) in stagedTrk)
                {
                    w.WriteLine($"- _Tracker:_ {v}");
                }
                w.WriteLine();
            }

            // Endings (memorable-moment records) joined by asset guid.
            if (endingsByGuid.TryGetValue(rec.AssetGuid, out var endRec))
            {
                var lines = endRec.Fields.Where(kv => kv.Key.StartsWith("records_Array_data") &&
                                                       kv.Key.EndsWith("_text"))
                                          .OrderBy(kv => kv.Key)
                                          .Select(kv => kv.Value)
                                          .ToList();
                if (lines.Count > 0)
                {
                    w.WriteLine("## Endings (memorable moments)");
                    w.WriteLine();
                    foreach (var l in lines) { w.WriteLine($"- {l.TrimEnd()}"); endings++; }
                    w.WriteLine();
                }
            }

            entries.Add((title, fileName, objectives, endings));
        }

        // Quest records that don't have a matching template — emit standalone.
        var pairedGuids = new HashSet<string>(quests.Select(r => r.AssetGuid));
        foreach (var rec in questRecs.OrderBy(r => Get(r, "displayName") ?? Get(r, "data_displayName") ?? r.AssetGuid))
        {
            if (pairedGuids.Contains(rec.AssetGuid)) continue;
            string title = Get(rec, "displayName") ?? Get(rec, "data_displayName");
            if (string.IsNullOrWhiteSpace(title))
            {
                // No displayName — derive a hint from the first ending text.
                // Quest endings commonly start with "You entrusted Arthur's soul to <X>"
                // and only the suffix differs, so we take a longer slice (up
                // to the first sentence break OR ~70 chars) to capture <X>.
                string firstEnding = rec.Fields.Where(kv => kv.Key.StartsWith("records_Array_data") &&
                                                             kv.Key.EndsWith("_text"))
                                                .OrderBy(kv => kv.Key)
                                                .Select(kv => kv.Value)
                                                .FirstOrDefault();
                string hint = LongHint(firstEnding, 70);
                title = !string.IsNullOrWhiteSpace(hint)
                    ? "Quest — " + hint
                    : "Quest_" + rec.AssetGuid[..8];
            }
            string baseName = SafeFilename(title);
            seen.TryGetValue(baseName, out int n);
            seen[baseName] = n + 1;
            string fileName = n == 0 ? baseName + ".md" : $"{baseName}_{n + 1}.md";
            int endings = 0;

            using var w = new StreamWriter(Path.Combine(dir, fileName));
            w.WriteLine($"# {title}");
            w.WriteLine();
            w.WriteLine($"_Asset id:_ `{rec.AssetGuid}`");
            w.WriteLine();
            w.WriteLine("## Endings (memorable moments)");
            w.WriteLine();
            var lines = rec.Fields.Where(kv => kv.Key.StartsWith("records_Array_data") &&
                                                kv.Key.EndsWith("_text"))
                                  .OrderBy(kv => kv.Key)
                                  .Select(kv => kv.Value);
            foreach (var l in lines) { w.WriteLine($"- {l.TrimEnd()}"); endings++; }
            w.WriteLine();

            entries.Add((title, fileName, 0, endings));
        }

        WriteFolderIndex(Path.Combine(dir, "_index.md"),
            "quests", "quests with descriptions, tracker, objective stages, and endings",
            entries.Count,
            entries.Select(e => ("Quest", e.title, e.file, $"{e.objectives} stage(s), {e.endings} ending(s)")));
    }

    // ---- encyclopedia (one file per entry) --------------------------------

    static void WriteEncyPerFile(string dir, List<LoreRecord> journals)
    {
        Directory.CreateDirectory(dir);

        var entries = new List<(string category, string title, string file, int subentries)>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Group by prefab guid so the LLM can see which "encyclopedia book"
        // an entry belongs to (Bestiary / Lore / Characters / Tutorials / Fish
        // all share one prefab class — different prefabs hold different sets).
        var prefabSizes = journals.GroupBy(r => r.AssetGuid).ToDictionary(g => g.Key, g => g.Count());
        // Heuristic category labels: largest bucket = "Bestiary", next = "Lore", etc.
        // We don't actually know which is which, so just label by entry-count rank.
        var prefabRank = prefabSizes.OrderByDescending(kv => kv.Value).Select((kv, i) => (kv.Key, i)).ToDictionary(t => t.Key, t => t.i);

        foreach (var rec in journals)
        {
            string title = Get(rec, "data_entryName") ?? Get(rec, "data_name");
            string desc  = Get(rec, "data_description");
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(desc)) continue;
            string display = title ?? GuessTitle(desc) ?? "(no title)";
            string category = $"Journal-{(char)('A' + prefabRank[rec.AssetGuid])}";

            string baseName = SafeFilename(display);
            seen.TryGetValue(baseName, out int n);
            seen[baseName] = n + 1;
            string fileName = n == 0 ? baseName + ".md" : $"{baseName}_{n + 1}.md";

            int subCount = 0;
            using var w = new StreamWriter(Path.Combine(dir, fileName));
            w.WriteLine($"# {display}");
            w.WriteLine();
            w.WriteLine($"_Category:_ **{category}**  ");
            w.WriteLine($"_Prefab:_ `{rec.AssetGuid}`");
            w.WriteLine();
            if (!string.IsNullOrWhiteSpace(desc))
            {
                w.WriteLine(desc);
                w.WriteLine();
            }
            var subs = rec.Fields.Where(kv => kv.Key.StartsWith("data_subentries_Array_data") &&
                                               kv.Key.EndsWith("_textToShow"))
                                 .OrderBy(kv => kv.Key)
                                 .Select(kv => kv.Value)
                                 .ToList();
            if (subs.Count > 0)
            {
                w.WriteLine("## Sub-entries");
                w.WriteLine();
                foreach (var s in subs) { w.WriteLine($"- {s}"); subCount++; }
                w.WriteLine();
            }
            entries.Add((category, display, fileName, subCount));
        }

        WriteFolderIndex(Path.Combine(dir, "_index.md"),
            "encyclopedia", "in-game encyclopedia / journal entries (Bestiary, Characters, Lore, Tutorials, Fish)",
            entries.Count,
            entries.OrderBy(e => e.category).ThenBy(e => e.title, StringComparer.OrdinalIgnoreCase)
                   .Select(e => (e.category, e.title, e.file, e.subentries > 0 ? $"{e.subentries} sub-entries" : "")));
    }

    // ---- reference tables (one file each) ---------------------------------

    static void WriteItems(string path, List<LoreRecord> items)
    {
        using var w = new StreamWriter(path);
        Header(w, "ITEMS", items.Count);
        foreach (var rec in items.OrderBy(r => Get(r, "itemName")))
        {
            string name = Get(rec, "itemName");
            string desc = Get(rec, "description");
            string flav = Get(rec, "flavor");
            if (string.IsNullOrEmpty(name)) continue;
            w.WriteLine($"### {name}");
            if (!string.IsNullOrEmpty(flav)) w.WriteLine($"_Flavor:_ {flav}");
            if (!string.IsNullOrEmpty(desc)) w.WriteLine(desc);
            w.WriteLine();
        }
    }

    static void WriteNpcs(string path, List<LoreRecord> npcs)
    {
        using var w = new StreamWriter(path);
        Header(w, "NPC ROSTER (display names)", npcs.Count);
        w.WriteLine("Canonical NPC name list. For each speaker, see `characters/<Name>.md` for");
        w.WriteLine("biography + every line of dialogue across all stories.");
        w.WriteLine();
        foreach (var rec in npcs.OrderBy(r => Get(r, "displayName")))
        {
            string name = Get(rec, "displayName");
            string desc = Get(rec, "description");
            if (string.IsNullOrEmpty(name)) continue;
            w.WriteLine($"### {name}");
            if (!string.IsNullOrEmpty(desc)) w.WriteLine(desc);
            w.WriteLine();
        }
    }

    static void WriteFactions(string path, List<LoreRecord> factions)
    {
        using var w = new StreamWriter(path);
        Header(w, "FACTIONS", factions.Count);
        foreach (var rec in factions.OrderBy(r => Get(r, "factionName")))
        {
            string name = Get(rec, "factionName");
            string desc = Get(rec, "factionDescription");
            if (string.IsNullOrEmpty(name)) continue;
            w.WriteLine($"### {name}");
            if (!string.IsNullOrEmpty(desc) && desc != "Default faction description")
                w.WriteLine(desc);
            w.WriteLine();
        }
    }

    static void WriteTalents(string path, List<LoreRecord> talents)
    {
        using var w = new StreamWriter(path);
        Header(w, "TALENTS / SKILLS", talents.Count);
        foreach (var rec in talents.OrderBy(r => Get(r, "talentName")))
        {
            string name = Get(rec, "talentName");
            string desc = Get(rec, "description");
            string lc   = Get(rec, "lightCastInfo");
            string hc   = Get(rec, "heavyCastInfo");
            if (string.IsNullOrEmpty(name)) continue;
            w.WriteLine($"### {name}");
            if (!string.IsNullOrEmpty(desc)) w.WriteLine(desc);
            if (!string.IsNullOrEmpty(lc))   w.WriteLine($"_Light cast:_ {lc}");
            if (!string.IsNullOrEmpty(hc))   w.WriteLine($"_Heavy cast:_ {hc}");
            w.WriteLine();
        }
    }

    static void WriteWorldText(string path, BabelTranslator babel)
    {
        using var w = new StreamWriter(path);
        Header(w, "WORLD TEXT (UI / stats / keywords / region strings)", -1);
        w.WriteLine("Untemplated babel keys grouped by top-level prefix. Excludes per-story dialogue");
        w.WriteLine("(those are in `characters/` / `readables/` / `notices/` / `tutorials/` / `cutscenes/`).");
        w.WriteLine();
        var buckets = new SortedDictionary<string, List<(string key, string value)>>();
        for (int i = 0; i < babel.Keys.Length; i++)
        {
            var key = babel.Keys[i];
            var val = RichText.Clean(babel.Translations[i]);
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(val)) continue;
            if (key.StartsWith("Template/")) continue;
            if (LooksLikeDialogKey(key)) continue;
            int slash = key.IndexOf('/');
            string bucket = slash > 0 ? key[..slash] : "(root)";
            if (!buckets.TryGetValue(bucket, out var list))
                list = buckets[bucket] = new();
            list.Add((key, val));
        }
        foreach (var (bucket, list) in buckets)
        {
            w.WriteLine($"## {bucket}  ({list.Count} strings)");
            foreach (var (key, val) in list.OrderBy(p => p.key))
            {
                string trimmed = val.Replace('\r', ' ').Replace('\n', ' ');
                w.WriteLine($"  • {trimmed}");
            }
            w.WriteLine();
        }
    }

    static void WriteBarks(string path, Dictionary<StoryKind, List<StoryGraphData>> byKind,
                            Dictionary<string, (StoryKind, string)> classified, BabelTranslator babel)
    {
        using var w = new StreamWriter(path);
        if (!byKind.TryGetValue(StoryKind.Bark, out var stories) || stories.Count == 0)
        {
            Header(w, "AMBIENT BARKS", 0);
            w.WriteLine("(none)");
            return;
        }
        Header(w, "AMBIENT BARKS (one-liners spoken by NPCs in the world)", stories.Count);
        foreach (var g in stories.OrderBy(g => StoryClassifier.TitleFor(classified[g.Guid].Item2) ?? g.Guid))
        {
            var (_, prefix) = classified[g.Guid];
            string title = StoryClassifier.TitleFor(prefix);
            w.WriteLine($"### {title}");
            if (g.Chapters != null)
            {
                foreach (var ch in g.Chapters.OrderBy(c => c.Index))
                    foreach (var step in ch.Steps)
                        if (step is SaySpeechStep t)
                        {
                            string text = babel.TranslateClean(t.TextId);
                            if (!string.IsNullOrWhiteSpace(text)) w.WriteLine($"  • {text}");
                        }
            }
            w.WriteLine();
        }
    }

    static void WriteOthers(string path, List<LoreRecord> others)
    {
        using var w = new StreamWriter(path);
        Header(w, "MISC TEMPLATED RECORDS (records that didn't fit other categories)", others.Count);
        foreach (var rec in others.OrderBy(r => Get(r, "displayName") ?? "").ThenBy(r => r.AssetGuid))
        {
            string label = Get(rec, "displayName") ?? Get(rec, "title") ?? rec.AssetGuid;
            string desc  = Get(rec, "description");
            string flav  = Get(rec, "flavor");
            string lstr  = Get(rec, "locString");
            if (string.IsNullOrEmpty(label) && string.IsNullOrEmpty(desc) && string.IsNullOrEmpty(flav) && string.IsNullOrEmpty(lstr)) continue;
            if (!string.IsNullOrEmpty(label) && label != rec.AssetGuid)
                w.WriteLine($"### {label}");
            if (!string.IsNullOrEmpty(flav)) w.WriteLine($"_Flavor:_ {flav}");
            if (!string.IsNullOrEmpty(desc)) w.WriteLine(desc);
            if (!string.IsNullOrEmpty(lstr)) w.WriteLine(lstr);
            w.WriteLine();
        }
    }

    // ---- index + README ---------------------------------------------------

    static void WriteFolderIndex(string path, string folder, string description, int total,
                                  IEnumerable<(string category, string title, string file, string note)> rows)
    {
        using var w = new StreamWriter(path);
        w.WriteLine($"# {char.ToUpper(folder[0]) + folder[1..]} — Index");
        w.WriteLine();
        w.WriteLine($"One file per entry — {description}.");
        w.WriteLine();
        w.WriteLine($"Total entries: **{total}**.");
        w.WriteLine();
        w.WriteLine("| Type | Title | File | Notes |");
        w.WriteLine("| --- | --- | --- | --- |");
        foreach (var (cat, title, file, note) in rows)
            w.WriteLine($"| {cat} | {title} | [{file}]({file}) | {note} |");
    }

    static void WriteReadme(string path, string targetDir,
                             int items, int npcs, int quests, int factions, int talents,
                             int journals, int questEnds,
                             Dictionary<StoryKind, List<StoryGraphData>> byKind)
    {
        int Get(StoryKind k) => byKind.TryGetValue(k, out var l) ? l.Count : 0;
        int booksLetters = Get(StoryKind.Book) + Get(StoryKind.Letter) + Get(StoryKind.Note) + Get(StoryKind.Tablet);
        int notices      = Get(StoryKind.NoticeBoard) + Get(StoryKind.Bounty);
        int tutorials    = Get(StoryKind.Tutorial);
        int cutscenes    = Get(StoryKind.Cutscene);

        // Count character files actually written.
        string charDir = Path.Combine(targetDir, "characters");
        int charFiles = Directory.Exists(charDir)
            ? Directory.GetFiles(charDir, "*.md").Count(p => !p.EndsWith("_index.md"))
            : 0;
        string intDir = Path.Combine(targetDir, "interactions");
        int intFiles = Directory.Exists(intDir)
            ? Directory.GetFiles(intDir, "*.md").Count(p => !p.EndsWith("_index.md"))
            : 0;

        using var w = new StreamWriter(path);
        w.WriteLine("# Tainted Grail: Fall of Avalon — Lore Export");
        w.WriteLine();
        w.WriteLine("Static export of every translatable string in the game (English locale).");
        w.WriteLine("Each subject (character, quest, book, notice, encyclopedia entry, …) is in");
        w.WriteLine("**its own file** so a single read answers a single question — no need to");
        w.WriteLine("cross-reference multiple files.");
        w.WriteLine();
        w.WriteLine("## How to navigate");
        w.WriteLine();
        w.WriteLine("1. Pick a folder by the kind of question you have:");
        w.WriteLine("   - \"What does X say / who is X?\" → `characters/`");
        w.WriteLine("   - \"What is the quest Y about?\" → `quests/`");
        w.WriteLine("   - \"What does this book say?\" → `readables/`");
        w.WriteLine("   - \"What's on this notice board / bounty?\" → `notices/`");
        w.WriteLine("   - \"What is the lore for creature/place Z?\" → `encyclopedia/`");
        w.WriteLine("   - \"What does this tutorial say?\" → `tutorials/`");
        w.WriteLine("   - \"What's the narration of cutscene W?\" → `cutscenes/`");
        w.WriteLine("   - \"What does the (Pray) / (Repair) / (Make an offering) prompt do?\" → `interactions/`");
        w.WriteLine("   - Items / talents / factions / UI / barks → `reference/`");
        w.WriteLine("2. Open the folder's `_index.md` to find the file that matches your subject.");
        w.WriteLine("3. Read that one file — everything you need is in it.");
        w.WriteLine();
        w.WriteLine("## Folder map");
        w.WriteLine();
        w.WriteLine("| Folder | Files | What's in each file |");
        w.WriteLine("| --- | ---: | --- |");
        w.WriteLine($"| `characters/` | {charFiles} | One per speaker — display name, biography (if any), every line they say across every story |");
        w.WriteLine($"| `quests/` | {quests + questEnds} | One per quest — description, tracker, objective stages, endings/memorable moments |");
        w.WriteLine($"| `readables/` | {booksLetters} | One per book/letter/note/tablet — full text |");
        w.WriteLine($"| `notices/` | {notices} | One per notice-board post or bounty — full text |");
        w.WriteLine($"| `tutorials/` | {tutorials} | One per tutorial flow — title, panel text, prompts |");
        w.WriteLine($"| `cutscenes/` | {cutscenes} | One per cutscene — full narration in chapter order |");
        w.WriteLine($"| `interactions/` | {intFiles} | One per interaction-prompt story — offerings, repairs, prayers, action menus |");
        w.WriteLine($"| `encyclopedia/` | {journals} | One per Bestiary / Lore / Characters / Tutorials / Fish entry |");
        w.WriteLine($"| `reference/items.md` | 1 | All {items} items with descriptions and flavor |");
        w.WriteLine($"| `reference/npcs.md` | 1 | All {npcs} NPC display names (canonical roster) |");
        w.WriteLine($"| `reference/talents.md` | 1 | All {talents} talents/skills |");
        w.WriteLine($"| `reference/factions.md` | 1 | All {factions} factions |");
        w.WriteLine( "| `reference/world-text.md` | 1 | UI strings, stat names, keywords, region text |");
        w.WriteLine( "| `reference/barks.md` | 1 | Ambient one-liners spoken by NPCs (grouped by region) |");
        w.WriteLine( "| `reference/misc-records.md` | 1 | Misc templated records that don't fit elsewhere |");
        w.WriteLine();
        w.WriteLine("## Source archives");
        w.WriteLine();
        w.WriteLine("Generated by the `DialogExporter` tool from the game's `Stroy/story.arch` and");
        w.WriteLine("`Languages/languages.arch` Unity FS bundles. The raw per-story JSON + flat dumps");
        w.WriteLine("for every story graph are at `../exports/dialogs/` and `../exports/flat/` if you");
        w.WriteLine("need branch/condition-level detail.");
    }

    // ---- helpers ----------------------------------------------------------

    static string Get(LoreRecord r, string name) => r.Fields.TryGetValue(name, out var v) ? v : null;

    static void Header(StreamWriter w, string title, int count)
    {
        w.WriteLine("=".PadRight(72, '='));
        w.WriteLine(count >= 0 ? $"{title}  ({count} entries)" : title);
        w.WriteLine("=".PadRight(72, '='));
        w.WriteLine();
    }

    static bool LooksLikeDialogKey(string key)
    {
        int slash = key.IndexOf('/');
        if (slash <= 0) return false;
        string rest = key[(slash + 1)..];
        return rest.StartsWith("text_") || rest.StartsWith("choice_text_") ||
               rest.StartsWith("choiceText_") || rest.StartsWith("Text_");
    }

    static string SafeFilename(string s)
    {
        if (string.IsNullOrEmpty(s)) return "_";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        // Collapse runs of underscores so titles like "Foo - Bar  Baz" don't
        // become "Foo___Bar__Baz".
        var raw = sb.ToString();
        var collapsed = new StringBuilder(raw.Length);
        bool prevUnderscore = false;
        foreach (var c in raw)
        {
            if (c == '_')
            {
                if (!prevUnderscore) collapsed.Append('_');
                prevUnderscore = true;
            }
            else
            {
                collapsed.Append(c);
                prevUnderscore = false;
            }
        }
        string r = collapsed.ToString().Trim('_');
        if (r.Length > 80) r = r[..80];
        if (string.IsNullOrEmpty(r)) r = "_";
        return r;
    }

    static string GuessTitle(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc)) return null;
        int dot = desc.IndexOfAny(new[] { '.', '!', '?', '\n' });
        string first = (dot > 0 ? desc[..dot] : desc).Trim();
        if (first.Length == 0) return null;

        // Try "Subject is/are/were/has …" — the noun phrase before the verb is
        // usually the subject ("Wyrdspawns are …" → "Wyrdspawns").
        var verbs = new[] { " are ", " is ", " were ", " was ", " has ", " have ", " serves ", " moves ", " lurks ", " lurk ", " stalks ", " stalk " };
        foreach (var v in verbs)
        {
            int idx = first.IndexOf(v, StringComparison.Ordinal);
            if (idx > 0) return CapTitle(TrimAtComma(first[..idx]));
        }

        // Some entries lead with "X, the Y of Z" — take the part before the
        // first comma if it starts with a capitalised name.
        int comma = first.IndexOf(", ");
        if (comma > 0 && char.IsUpper(first[0])) return CapTitle(first[..comma]);

        // Fallback: first ~40 chars of the sentence.
        return CapTitle(first);
    }

    // Drop everything from the first ", " onward — keeps "A Keeper of the South,
    // though how he earned that title" → "A Keeper of the South".
    static string TrimAtComma(string s)
    {
        int c = s.IndexOf(", ", StringComparison.Ordinal);
        return c > 0 ? s[..c].TrimEnd(',', ' ') : s.TrimEnd(',', ' ');
    }

    static string CapTitle(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().TrimEnd(',', ';', ':');
        const int max = 40;
        if (s.Length <= max) return s;
        // Prefer to cut at the last word boundary inside the cap.
        int sp = s.LastIndexOf(' ', max - 1);
        if (sp > max / 2) return s[..sp].TrimEnd();
        return s[..max].TrimEnd();
    }
}
