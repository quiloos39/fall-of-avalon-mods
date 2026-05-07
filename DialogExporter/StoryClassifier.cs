namespace DialogExporter;

// Categorises a story by inspecting the babel-key prefix used by its SText
// lines. The game uses very stable naming conventions (Readable_Book_*,
// Readable_Letter_*, *_Noticeboard_*, *_Bounty_*) so we can route each story
// to the right output file even though the underlying StoryGraph type is the
// same for all of them.

public enum StoryKind
{
    Dialogue,        // normal NPC conversation
    Book,            // Readable_Book_*
    Letter,          // Readable_Letter_* / Readable_Letters_*
    Note,            // Readable_Note_* / Readable_Diary_* / similar
    Tablet,          // Readable_Tablet_*
    NoticeBoard,     // *_Noticeboard_*
    Bounty,          // *_Bounty_* / Bounty_*
    Bark,            // CommonBarks/, *_Barks_*, *_GeneralBarks
    Tutorial,        // Tutorial_*
    Cutscene,        // Cutscene_*, Prologue_*
    Other,
}

public static class StoryClassifier
{
    // Returns the dominant babel-key prefix used by this story's SText lines.
    public static string DominantPrefix(StoryGraphData g, BabelTranslator babel)
    {
        if (g.Chapters == null) return null;
        var counts = new Dictionary<string, int>();
        void Tally(uint id)
        {
            if (id == 0) return;
            string key = babel.KeyOf(id);
            if (string.IsNullOrEmpty(key)) return;
            int slash = key.IndexOf('/');
            if (slash <= 0) return;
            string prefix = key[..slash];
            counts.TryGetValue(prefix, out int existing);
            counts[prefix] = existing + 1;
        }
        void TallyKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            int slash = key.IndexOf('/');
            if (slash <= 0) return;
            string prefix = key[..slash];
            counts.TryGetValue(prefix, out int existing);
            counts[prefix] = existing + 1;
        }

        foreach (var ch in g.Chapters)
            foreach (var step in ch.Steps)
            {
                switch (step)
                {
                    case SaySpeechStep t:        Tally(t.TextId); break;
                    case StoryStartChoiceStep s: Tally(s.TextId); break;
                    case ChoiceStep cs:          Tally(cs.Choice.textIdx); break;
                    case FancyPanelStep fp:      Tally(fp.TextId); break;
                    case StatDependantChoiceStep sdc: Tally(sdc.OverrideLabelId); break;
                    case OpenHouseUnlockStep ohu:
                        Tally(ohu.HouseNameId); Tally(ohu.HouseDescriptionId); break;
                    case TutorialTextStep tt:    TallyKey(tt.TitleId); TallyKey(tt.TextId); break;
                    case FakeInteractionPromptStep fip: TallyKey(fip.TitleId); TallyKey(fip.TextId); break;
                    case PlayTutorialVideoStep ptv: TallyKey(ptv.TitleId); TallyKey(ptv.TextId); break;
                    case ShowTutorialGraphicStep stg: TallyKey(stg.TitleId); TallyKey(stg.TextId); break;
                    case OnHoverChoicePreviewStep ohp: TallyKey(ohp.ProficiencySetKey); break;
                }
            }
        if (counts.Count == 0) return null;
        return counts.OrderByDescending(kv => kv.Value).First().Key;
    }

    public static StoryKind Classify(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return StoryKind.Other;
        // Strip common region prefixes once, so e.g. "SOS_Readable_Book_…" is
        // treated like "Readable_Book_…".
        string p = StripRegionPrefix(prefix);

        if (p.StartsWith("Readable_Book", StringComparison.OrdinalIgnoreCase))     return StoryKind.Book;
        if (p.StartsWith("Readable_Letter", StringComparison.OrdinalIgnoreCase))   return StoryKind.Letter;
        if (p.StartsWith("Readable_Note", StringComparison.OrdinalIgnoreCase))     return StoryKind.Note;
        if (p.StartsWith("Readable_Diary", StringComparison.OrdinalIgnoreCase))    return StoryKind.Note;
        if (p.StartsWith("Readable_Tablet", StringComparison.OrdinalIgnoreCase))   return StoryKind.Tablet;
        if (p.StartsWith("Readable_", StringComparison.OrdinalIgnoreCase))         return StoryKind.Note;

        if (p.IndexOf("Noticeboard", StringComparison.OrdinalIgnoreCase) >= 0)     return StoryKind.NoticeBoard;
        if (p.StartsWith("Bounty_", StringComparison.OrdinalIgnoreCase))           return StoryKind.Bounty;
        if (p.IndexOf("_Bounty_", StringComparison.OrdinalIgnoreCase) >= 0)        return StoryKind.Bounty;

        if (p.Equals("CommonBarks", StringComparison.OrdinalIgnoreCase))           return StoryKind.Bark;
        if (p.StartsWith("Barks_", StringComparison.OrdinalIgnoreCase))            return StoryKind.Bark;
        if (p.EndsWith("_GeneralBarks", StringComparison.OrdinalIgnoreCase))       return StoryKind.Bark;

        if (p.StartsWith("Tutorial", StringComparison.OrdinalIgnoreCase))          return StoryKind.Tutorial;
        if (p.StartsWith("Cutscene_", StringComparison.OrdinalIgnoreCase))         return StoryKind.Cutscene;
        if (p.EndsWith("_Cutscene", StringComparison.OrdinalIgnoreCase))           return StoryKind.Cutscene;
        if (p.IndexOf("_Cutscene_", StringComparison.OrdinalIgnoreCase) >= 0)      return StoryKind.Cutscene;
        if (p.StartsWith("Prologue_", StringComparison.OrdinalIgnoreCase))         return StoryKind.Cutscene;
        if (p.StartsWith("Prologue", StringComparison.OrdinalIgnoreCase))          return StoryKind.Cutscene;

        return StoryKind.Dialogue;
    }

    public static string StripRegionPrefix(string p)
    {
        string[] regions = { "HOS_", "FS_", "SOS_", "CT_", "OW_", "DR_", "FV_", "DalRiata_", "OpenWorld_" };
        foreach (var r in regions)
            if (p.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                return p[r.Length..];
        return p;
    }

    // A friendly title for a readable story — basically a humanised version of
    // its prefix.  Drops region prefixes and the Readable_<Type>_ prefix so
    // the user sees just the descriptive name.
    public static string TitleFor(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return "(unknown)";
        string p = StripRegionPrefix(prefix);
        string[] toStrip = {
            "Readable_Book_", "Readable_Letter_", "Readable_Letters_",
            "Readable_Note_", "Readable_Notes_", "Readable_Diary_",
            "Readable_Tablet_", "Readable_",
            "Bounty_",
        };
        foreach (var s in toStrip)
            if (p.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                return SpaceCamel(p[s.Length..]);
        return SpaceCamel(p);
    }

    static string SpaceCamel(string s)
    {
        // Convert underscores to spaces and split CamelCase / PascalCase boundaries.
        var sb = new System.Text.StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '_') { sb.Append(' '); continue; }
            // Don't add a CamelCase space when the previous character is already
            // a separator (underscore in source / space in output) — that would
            // produce "Noticeboard  Bounty" instead of "Noticeboard Bounty".
            if (i > 0 && char.IsUpper(c) && s[i - 1] != '_' &&
                (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]))))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
