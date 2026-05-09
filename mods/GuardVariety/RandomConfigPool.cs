using System.Linq;
using Awaken.TG.Main.Character.Features.Config;
using Awaken.TG.Main.Fights.NPCs;
using UnityEngine;

namespace GuardVariety
{
    // Locates populated NPCRandomConfigSO assets already loaded by the game so we
    // can hand them to RandomizeFeatures(features) without shipping our own asset
    // references. Crowd NPCs in cities have NpcRandomizer ViewComponents that
    // reference these SOs; once any one has loaded, Resources.FindObjectsOfTypeAll
    // finds it.
    //
    // The game ships gender-specific configs (e.g. "Features_MaleRandom",
    // "Features_FemaleRandom") with different hair pools and (probably) different
    // blendshape groups. Applying the male config to a female-marked NPC produces
    // a masculine-looking face — which defeats the prefab swap entirely. So we
    // pick a config matching the NPC's gender, with a fallback to whichever we
    // could find.
    internal static class RandomConfigPool
    {
        private static NPCRandomConfigSO _female;
        private static NPCRandomConfigSO _male;
        private static NPCRandomConfigSO _generic;
        private static int _lastScanCount;

        public static NPCRandomConfigSO GetForGender(Gender gender)
        {
            EnsureScan();
            if (gender == Gender.Female && _female != null) return _female;
            if (gender == Gender.Male && _male != null) return _male;
            return _female ?? _male ?? _generic;
        }

        // Backwards-compat shim — callers that don't have a gender (probe paths).
        public static NPCRandomConfigSO Get() => GetForGender(Gender.None);

        private static void EnsureScan()
        {
            // Re-scan if we don't yet have both gendered configs. New addressable
            // assets can stream in after the first scan.
            if (_female != null && _male != null) return;

            var all = Resources.FindObjectsOfTypeAll<NPCRandomConfigSO>();
            if (all == null || all.Length == 0) return;
            if (all.Length == _lastScanCount && _female != null && _male != null) return;
            _lastScanCount = all.Length;

            NPCRandomConfigSO bestFemale = null, bestMale = null, bestGeneric = null;
            int bestFemaleScore = -1, bestMaleScore = -1, bestGenericScore = -1;

            foreach (var c in all)
            {
                if (c == null) continue;
                int score = ScoreConfig(c);
                var bucket = ClassifyByName(c.name);

                if (bucket == GenderBucket.Female && score > bestFemaleScore) { bestFemale = c; bestFemaleScore = score; }
                else if (bucket == GenderBucket.Male && score > bestMaleScore) { bestMale = c; bestMaleScore = score; }
                else if (bucket == GenderBucket.Unknown && score > bestGenericScore) { bestGeneric = c; bestGenericScore = score; }
            }

            // Heuristic fallback: if no name-classified male/female found, infer
            // from content. A config with beardPool>0 is almost certainly male.
            if (bestMale == null && bestGeneric != null && bestGeneric.beardPool != null && bestGeneric.beardPool.Count > 0)
            {
                bestMale = bestGeneric;
            }
            if (bestFemale == null)
            {
                // Among unbucketed configs without beards, pick the most populated.
                foreach (var c in all)
                {
                    if (c == null || c == bestMale) continue;
                    if (c.beardPool != null && c.beardPool.Count > 0) continue;
                    int score = ScoreConfig(c);
                    if (score > bestFemaleScore) { bestFemale = c; bestFemaleScore = score; }
                }
            }

            bool femaleChanged = !ReferenceEquals(bestFemale, _female);
            bool maleChanged = !ReferenceEquals(bestMale, _male);
            bool genericChanged = !ReferenceEquals(bestGeneric, _generic);

            _female = bestFemale;
            _male = bestMale;
            _generic = bestGeneric;

            if (femaleChanged && _female != null) LogConfig("Female", _female);
            if (maleChanged && _male != null) LogConfig("Male", _male);
            if (genericChanged && _generic != null && _female == null && _male == null) LogConfig("Generic-fallback", _generic);
        }

        private static int ScoreConfig(NPCRandomConfigSO c)
        {
            int s = 0;
            if (c.skinTints != null) s += c.skinTints.Length;
            if (c.hairPool != null) s += c.hairPool.Count;
            if (c.hairColorPool != null) s += c.hairColorPool.Count;
            if (c.eyeTints != null) s += c.eyeTints.Count;
            if (c.bodyTexturesConfigPool != null) s += c.bodyTexturesConfigPool.Count;
            return s;
        }

        private enum GenderBucket { Unknown, Male, Female }
        private static GenderBucket ClassifyByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return GenderBucket.Unknown;
            string n = name.ToLowerInvariant();
            // Order matters: "female" contains "male" as substring.
            if (n.Contains("female") || n.Contains("woman") || n.Contains("girl")) return GenderBucket.Female;
            if (n.Contains("male") || n.Contains("man") || n.Contains("boy")) return GenderBucket.Male;
            return GenderBucket.Unknown;
        }

        private static void LogConfig(string label, NPCRandomConfigSO c)
        {
            Plugin.Log.LogInfo(
                $"[GuardVariety] Cached {label} NPCRandomConfigSO '{c.name}' " +
                $"(skinTints={c.skinTints?.Length ?? 0}, " +
                $"hairPool={c.hairPool?.Count ?? 0}, " +
                $"beardPool={c.beardPool?.Count ?? 0}, " +
                $"hairColors={c.hairColorPool?.Count ?? 0}, " +
                $"eyeTints={c.eyeTints?.Count ?? 0})");
        }
    }
}
