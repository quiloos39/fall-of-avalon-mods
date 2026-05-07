using System.Text;

namespace DialogExporter;

// Aggregates everything we know about each named character into one file:
//   - Display name from the NPC list (Template/displayName_*_<NpcPrefab>)
//   - Encyclopedia biography (if their name matches a Characters/Lore entry)
//   - All dialogue lines across every actor-label whose name fuzzy-matches them
//
// Three sources don't share a stable ID across the babel data, so we join
// them by NAME — case-insensitive, with fuzzy matching for prefixed labels
// like "Sirja_StoryOnDeath" → "Sirja".
//
// Dialogue labels that don't match any named NPC still get their own file
// (the actor exists in the corpus but has no roster entry) so the LLM has
// one and only one place to find any speaker — no separate "by-label" folder.

public static class CharacterProfiler
{
    public static void Run(string targetDir, IList<StoryGraphData> graphs, BabelTranslator babel, ActorNameResolver actors,
                           Dictionary<string, (StoryKind kind, string prefix)> classified = null)
    {
        var lore = LoreExtractor.ExtractAll(babel);

        // 1) Pull NPC display names — these are our canonical character roster.
        var npcNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in lore.Values)
        {
            if (rec.AssetGuid != LoreExtractor.NpcTypeHash) continue;
            string n = Get(rec, "displayName");
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (n == "?" || n.Length > 60) continue;
            npcNames.Add(n.Trim());
        }

        // 2) Index encyclopedia entries by guessed title for biography lookup.
        var encyByTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in lore.Values)
        {
            string desc = Get(rec, "data_description");
            if (string.IsNullOrEmpty(desc)) continue;
            string title = Get(rec, "data_entryName") ?? Get(rec, "data_name") ?? GuessTitle(desc);
            if (string.IsNullOrWhiteSpace(title)) continue;
            if (!encyByTitle.ContainsKey(title))
                encyByTitle[title] = BuildEncyEntry(rec, desc, title);
        }

        // 3) Group dialogue lines by actor label, but skip stories that are
        //    routed elsewhere (books, letters, notices, etc. — those go to
        //    readables/ + notices/ + tutorials/ + cutscenes/).
        var dialogueByLabel = new Dictionary<string, List<(StoryGraphData g, ChapterData ch, SaySpeechStep t)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in graphs)
        {
            if (g.Chapters == null) continue;
            if (classified != null && classified.TryGetValue(g.Guid, out var ck) && IsReadable(ck.kind)) continue;
            foreach (var ch in g.Chapters)
                foreach (var s in ch.Steps)
                    if (s is SaySpeechStep t)
                    {
                        string label = actors.Resolve(t.ActorGuid) ?? "(unknown)";
                        if (!dialogueByLabel.TryGetValue(label, out var list))
                            list = dialogueByLabel[label] = new();
                        list.Add((g, ch, t));
                    }
        }

        string charDir = Path.Combine(targetDir, "characters");
        Directory.CreateDirectory(charDir);

        // 4) For each NPC name, find matching dialogue labels and build the file.
        var indexEntries = new List<(string name, int dialogueLines, bool hasEncy, string fileRef, bool isAnon)>();
        var consumedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in npcNames)
        {
            var matches = new List<string>();
            foreach (var label in dialogueByLabel.Keys)
            {
                if (label.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || label.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)
                    || label.EndsWith("_" + name, StringComparison.OrdinalIgnoreCase)
                    || label.IndexOf("_" + name + "_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matches.Add(label);
                }
            }

            bool hasEncy = encyByTitle.ContainsKey(name);
            if (matches.Count == 0 && !hasEncy) continue;

            foreach (var m in matches) consumedLabels.Add(m);

            int totalLines = matches.Sum(m => dialogueByLabel[m].Count);
            string fileName = SafeFilename(name) + ".md";
            indexEntries.Add((name, totalLines, hasEncy, fileName, false));

            using var w = new StreamWriter(Path.Combine(charDir, fileName));
            WriteCharacterFile(w, name, hasEncy ? encyByTitle[name] : null, matches, dialogueByLabel, babel, actors);
        }

        // 5) Labels not absorbed by any named NPC — emit their own file so no
        //    speaker is missing from `characters/`. The "?" actor (resolver
        //    couldn't find a label) gets a clearer name so the LLM doesn't
        //    have to guess at a "_.md" file.
        foreach (var (label, lines) in dialogueByLabel)
        {
            if (consumedLabels.Contains(label)) continue;
            if (label == "(unknown)") continue;          // pure noise
            if (lines.Count == 0) continue;

            string display = label == "?" ? "Unknown_Speaker" : label;
            string fileName = SafeFilename(display) + ".md";
            indexEntries.Add((display, lines.Count, false, fileName, true));

            using var w = new StreamWriter(Path.Combine(charDir, fileName));
            WriteCharacterFile(w, display, null, new List<string> { label }, dialogueByLabel, babel, actors);
        }

        // 6) Master index sorted by total dialogue lines.
        using var idx = new StreamWriter(Path.Combine(charDir, "_index.md"));
        idx.WriteLine("# Characters — Index");
        idx.WriteLine();
        idx.WriteLine("One file per speaker. Files marked **N** are named NPCs (display name + biography + dialogue);");
        idx.WriteLine("files marked **A** are anonymous speakers (dialogue only — the label exists in stories but no");
        idx.WriteLine("matching NPC display name was found).");
        idx.WriteLine();
        idx.WriteLine($"Total: **{indexEntries.Count}** speakers ({indexEntries.Count(e => !e.isAnon)} named, {indexEntries.Count(e => e.isAnon)} anonymous).");
        idx.WriteLine();
        idx.WriteLine("| Type | Bio | Lines | Name | File |");
        idx.WriteLine("| --- | --- | ---: | --- | --- |");
        foreach (var e in indexEntries.OrderByDescending(e => e.dialogueLines).ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase))
        {
            string typ = e.isAnon ? "A" : "N";
            string bio = e.hasEncy ? "Y" : "—";
            idx.WriteLine($"| {typ} | {bio} | {e.dialogueLines} | {e.name} | [{e.fileRef}]({e.fileRef}) |");
        }
    }

    static void WriteCharacterFile(StreamWriter w, string name, string biography,
                                    List<string> matchingLabels,
                                    Dictionary<string, List<(StoryGraphData g, ChapterData ch, SaySpeechStep t)>> dialogueByLabel,
                                    BabelTranslator babel, ActorNameResolver actors)
    {
        int totalLines = matchingLabels.Sum(m => dialogueByLabel.TryGetValue(m, out var l) ? l.Count : 0);
        w.WriteLine($"# {name}");
        w.WriteLine();
        if (name == "Unknown_Speaker")
        {
            w.WriteLine("_Lines from speakers whose actor reference could not be resolved to a named NPC._");
            w.WriteLine("_Heterogeneous content — generic barks, anonymous letters, narrator lines, etc. Use the_");
            w.WriteLine("_`--- story <guid> ---` headers below to disambiguate._");
            w.WriteLine();
        }
        if (biography != null)
        {
            w.WriteLine("## Biography");
            w.WriteLine();
            w.WriteLine(biography);
            w.WriteLine();
        }
        if (totalLines == 0) return;

        w.WriteLine($"## Dialogue ({totalLines} lines)");
        w.WriteLine();
        foreach (var label in matchingLabels.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            if (!dialogueByLabel.TryGetValue(label, out var lines)) continue;
            if (matchingLabels.Count > 1)
            {
                w.WriteLine($"### From label `{label}`  ({lines.Count} lines)");
                w.WriteLine();
            }
            foreach (var grp in lines.GroupBy(x => x.g.Guid).OrderBy(g => g.Key))
            {
                w.WriteLine($"--- story {grp.Key} ---");
                foreach (var (_, ch, t) in grp.OrderBy(x => x.ch.Index))
                {
                    string text = babel.TranslateClean(t.TextId);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    string target = actors.Resolve(t.TargetActorGuid);
                    w.WriteLine($"  ch{ch.Index} → {target}: {text}");
                }
                w.WriteLine();
            }
        }
    }

    static bool IsReadable(StoryKind k)
        => k == StoryKind.Book || k == StoryKind.Letter || k == StoryKind.Note ||
           k == StoryKind.Tablet || k == StoryKind.NoticeBoard || k == StoryKind.Bounty ||
           k == StoryKind.Tutorial || k == StoryKind.Cutscene;

    static string Get(LoreRecord r, string n) => r.Fields.TryGetValue(n, out var v) ? v : null;

    static string BuildEncyEntry(LoreRecord rec, string desc, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine(desc);
        var subs = rec.Fields.Where(kv => kv.Key.StartsWith("data_subentries_Array_data") &&
                                           kv.Key.EndsWith("_textToShow"))
                             .OrderBy(kv => kv.Key)
                             .Select(kv => kv.Value);
        foreach (var s in subs) sb.AppendLine($"  • {s}");
        return sb.ToString().TrimEnd();
    }

    static string GuessTitle(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc)) return null;
        int dot = desc.IndexOfAny(new[] { '.', '!', '?', '\n' });
        string first = (dot > 0 ? desc[..dot] : desc).Trim();
        if (first.Length == 0) return null;
        var verbs = new[] { " are ", " is ", " were ", " was ", " has ", " have " };
        foreach (var v in verbs)
        {
            int idx = first.IndexOf(v, StringComparison.Ordinal);
            if (idx > 0) return first[..idx].TrimEnd(',', ' ');
        }
        return null;
    }

    static string SafeFilename(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        string r = sb.ToString();
        if (r.Length > 80) r = r[..80];
        if (string.IsNullOrEmpty(r)) r = "_";
        return r;
    }
}
