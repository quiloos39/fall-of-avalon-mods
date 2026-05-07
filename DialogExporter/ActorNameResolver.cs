namespace DialogExporter;

// Resolves an ActorRef.guid to a human-readable label.
//
// We don't have a direct map (it lives in Actors.prefab inside the addressable
// AssetBundles, which would need a full Unity-asset reader to extract). But
// every SText line in a story has a babel KEY of the form
//   <storyGraphName>/text_<hash>_<storyGuid>
// and the <storyGraphName> almost always corresponds to the NPC's controller —
// e.g. "HOS_Quartermaster", "FS_NPC_Hjarn_GeneralTalk", "OW_Caradoc_..." etc.
//
// So for each actor GUID, we tally the babel-key prefixes of the lines they
// speak across all stories, pick the most common one, and use that as the
// label. It's not a substitute for the in-game display name but it's stable,
// deterministic, present for every actor, and good enough for "who said what?"
// queries.

public sealed class ActorNameResolver
{
    private readonly Dictionary<string, string> _labels = new();

    public IReadOnlyDictionary<string, string> Labels => _labels;

    // (actorGuid -> label). Tally only — no babel calls needed beyond the Keys array.
    public void Build(IEnumerable<StoryGraphData> graphs, BabelTranslator babel)
    {
        // For each actor: prefix -> count of lines using that prefix.
        var prefixCounts = new Dictionary<string, Dictionary<string, int>>();

        foreach (var g in graphs)
        {
            if (g.Chapters == null) continue;
            foreach (var ch in g.Chapters)
            {
                foreach (var step in ch.Steps)
                {
                    if (step is not SaySpeechStep t) continue;
                    if (string.IsNullOrEmpty(t.ActorGuid)) continue;
                    string key = babel.KeyOf(t.TextId);
                    if (string.IsNullOrEmpty(key)) continue;
                    int slash = key.IndexOf('/');
                    if (slash <= 0) continue;
                    string prefix = key[..slash];

                    if (!prefixCounts.TryGetValue(t.ActorGuid, out var inner))
                        inner = prefixCounts[t.ActorGuid] = new Dictionary<string, int>();
                    inner.TryGetValue(prefix, out int c);
                    inner[prefix] = c + 1;
                }
            }
        }

        // For each actor, the prefix used in the most lines wins.
        foreach (var (actorGuid, prefixes) in prefixCounts)
        {
            string best = prefixes.OrderByDescending(p => p.Value).First().Key;
            _labels[actorGuid] = CleanPrefix(best);
        }
        // Hard-code the few well-known sentinel actors:
        _labels["Hero"] = "Hero";
        _labels["None"] = "(none)";
    }

    public string Resolve(string actorGuid)
    {
        if (string.IsNullOrEmpty(actorGuid)) return "?";
        if (_labels.TryGetValue(actorGuid, out var v)) return v;
        return actorGuid; // unknown — leave as guid
    }

    // Strip common region/category prefixes so labels like "HOS_Quartermaster"
    // become just "Quartermaster".  We keep enough context that ambiguous
    // names (e.g. multiple guards) stay distinguishable.
    private static string CleanPrefix(string p)
    {
        // Drop the most common topical prefixes — but only ONE level. Keep the
        // rest so we don't collapse distinct NPCs.
        string[] strip = {
            "HOS_NPC_", "FS_NPC_", "SOS_NPC_", "CT_NPC_", "OW_NPC_", "DR_",
            "FV_", "HOS_", "FS_", "SOS_", "CT_", "OW_",
            "PJ_Logic_", "PJ_NPC_", "MQ_", "Special_NPC_", "BERLIN_MOVE_IT_",
            "Cuanacht", "CuanachtOpenWorld", "OpenWorld",
            "COM_", "SQ_", "DalRiata_NPC_", "DalRiata_",
        };
        foreach (var s in strip)
        {
            if (p.StartsWith(s, StringComparison.Ordinal))
            {
                string rest = p[s.Length..];
                // Drop trailing tags like "_GeneralTalk", "_InitialTalk", etc.
                rest = TrimSuffixes(rest);
                if (rest.Length > 0) return rest;
            }
        }
        return TrimSuffixes(p);
    }

    private static string TrimSuffixes(string s)
    {
        string[] suffixes = {
            "_GeneralTalks", "_GeneralTalk_NEW", "_GeneralTalk", "_InitialTalk",
            "_StoryInteraction", "_Controller", "_FirstTalk", "_Story",
            "_FireplaceTalk", "_Questions", "_EnviroVoices", "_Ending",
            "_GeneralBarks", "_Barks",
        };
        foreach (var x in suffixes)
            if (s.EndsWith(x, StringComparison.Ordinal))
                return s[..^x.Length];
        return s;
    }
}
