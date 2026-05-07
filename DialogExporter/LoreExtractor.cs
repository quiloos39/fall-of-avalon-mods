namespace DialogExporter;

// Builds structured "lore" records from the babel translation database.
//
// The babel keys follow a pattern produced by the game's localization pipeline:
//   Template/<field>_<longHash>_<guidHash>
//   <longHash>  is a 64-bit per-LocString hash — different LocStrings on the
//               same asset get different longHashes (e.g. itemName + description
//               on one item have different longHashes).
//   <guidHash>  is a 32-char hex GUID identifying the asset that *owns* the
//               LocString. For multi-component assets like Actors.prefab
//               (containing many ActorSpec children) ALL of them share the
//               same prefab GUID — so grouping naively by guidHash collapses
//               every NPC into one record.

public sealed class LoreRecord
{
    public string AssetGuid;       // Whatever guid we keyed on (could be prefab or standalone asset).
    public string TypeHash;        // longHash from the first field we saw.
    public Dictionary<string, string> Fields = new();   // fieldName -> translated text
    public List<string> Keys = new(); // joined keys (debug).
}

public static class LoreExtractor
{
    // Known type-hash GUIDs that surface what kind of prefab/asset a record describes.
    public static readonly string NpcTypeHash    = "7909eee15dd99b04f8e84647e3c04a73";
    public static readonly string SpriteTypeHash = "47538215d91edd946b42c1b2a5555276";

    public static Dictionary<string, LoreRecord> ExtractAll(BabelTranslator babel)
    {
        // Pass 1: parse every Template/ key into (fieldPath, longHash, guid, value).
        // Format: Template/<fieldPath>_<longHash>_<guid>
        // The last two underscore-separated tokens are always <longHash> and
        // <guid>. Everything before is the (possibly multi-segment) field
        // path — e.g. "records_Array_data4_text" or "data_subentries_Array_data0_textToShow".
        var entries = new List<(string field, string longHash, string guid, string value, string key)>();
        for (int i = 0; i < babel.Keys.Length; i++)
        {
            var key = babel.Keys[i];
            if (key == null || !key.StartsWith("Template/")) continue;
            string body = key["Template/".Length..];

            int last = body.LastIndexOf('_');
            if (last <= 0) continue;
            string guid  = body[(last + 1)..];
            string head1 = body[..last];

            int second = head1.LastIndexOf('_');
            if (second <= 0) continue;
            string longHash = head1[(second + 1)..];
            string field    = head1[..second];

            string value = RichText.Clean(babel.Translations[i] ?? string.Empty);
            if (string.IsNullOrWhiteSpace(value)) continue;
            entries.Add((field, longHash, guid, value, key));
        }

        // Pass 2: count entries per guidHash. If a guid carries many entries
        // it's a *prefab* whose children we must keep separate (use longHash
        // as part of the grouping key). Otherwise the guid IS the standalone
        // asset and grouping by guid alone collapses its fields correctly.
        var perGuidCount = new Dictionary<string, int>();
        foreach (var e in entries)
        {
            perGuidCount.TryGetValue(e.guid, out int c);
            perGuidCount[e.guid] = c + 1;
        }
        const int prefabThreshold = 50;

        // Pass 3: group.
        var byAsset = new Dictionary<string, LoreRecord>();
        foreach (var e in entries)
        {
            string assetKey = perGuidCount[e.guid] > prefabThreshold
                ? e.longHash + "@" + e.guid          // sub-component on prefab
                : e.guid;                              // standalone asset

            if (!byAsset.TryGetValue(assetKey, out var rec))
            {
                rec = new LoreRecord { AssetGuid = e.guid, TypeHash = e.longHash };
                byAsset[assetKey] = rec;
            }
            string fname = e.field;
            int dup = 0;
            while (rec.Fields.ContainsKey(fname)) fname = $"{e.field}#{++dup}";
            rec.Fields[fname] = e.value;
            rec.Keys.Add(e.key);
        }
        return byAsset;
    }
}
