using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Crafting;
using Awaken.TG.Main.Crafting.Recipes;
using Awaken.TG.Main.Crafting.HandCrafting;
using Awaken.TG.Main.Crafting.AlchemyCrafting;
using Awaken.TG.Main.Crafting.Cooking;
using Awaken.TG.Main.Templates;
using Awaken.TG.MVC;

namespace SpoilsOfTheSlain
{
    // Cost model rewrite: instead of fishing for "anything tagged material" (which gave us
    // Weatherfish — a fish — as the forge ingredient), we LEARN ingredients by sampling
    // vanilla recipes per station + per tier. That guarantees:
    //   - Forge weapons use real forge materials (iron ingots, leather, etc.)
    //   - Alchemy potions use real alchemy reagents (herbs, etc.)
    //   - Cooking dishes use real cooking ingredients (raw food)
    //   - The UI can render the recipe because the ingredients are well-formed game items
    internal static class CostGenerator
    {
        // station name → tier → list of ingredient sets pulled from vanilla recipes
        private static readonly Dictionary<RecipeFactory.Station, Dictionary<int, List<List<(ItemTemplate template, int count)>>>> _vanillaByStationTier
            = new Dictionary<RecipeFactory.Station, Dictionary<int, List<List<(ItemTemplate, int)>>>>();
        private static bool _learned;

        public static void EnsureLearned()
        {
            if (_learned) return;
            _learned = true;
            try
            {
                var provider = World.Services?.Get<TemplatesProvider>();
                if (provider == null) return;

                LearnFromTemplates<HandcraftingTemplate>(RecipeFactory.Station.Forge, provider);
                LearnFromTemplates<AlchemyTemplate>(RecipeFactory.Station.Alchemy, provider);
                LearnFromTemplates<CookingTemplate>(RecipeFactory.Station.Cooking, provider);

                if (Plugin.Cfg.Verbose.Value || Plugin.Cfg.LogDiscoveryGrowth.Value)
                {
                    foreach (var station in _vanillaByStationTier.Keys)
                    {
                        var totals = _vanillaByStationTier[station]
                            .OrderBy(kv => kv.Key)
                            .Select(kv => $"tier{kv.Key}={kv.Value.Count}");
                        Plugin.Log.LogInfo($"[Cost] Learned {station}: " + string.Join(", ", totals));
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Cost] EnsureLearned failed: {e.GetBaseException().Message}");
            }
        }

        private static void LearnFromTemplates<T>(RecipeFactory.Station station, TemplatesProvider provider) where T : CraftingTemplate
        {
            try
            {
                var getAllOfType = typeof(TemplatesProvider).GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                if (getAllOfType == null) return;

                var allTemplates = getAllOfType.MakeGenericMethod(typeof(T))
                    .Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable;
                if (allTemplates == null) return;

                var byTier = new Dictionary<int, List<List<(ItemTemplate, int)>>>();

                foreach (var ct in allTemplates)
                {
                    var recipes = (ct as CraftingTemplate)?.Recipes;
                    if (recipes == null) continue;
                    foreach (var r in recipes)
                    {
                        if (r == null) continue;
                        var outcome = r.Outcome;
                        if (outcome == null) continue;

                        // Skip our own (in case this runs after generation)
                        if ((r as UnityEngine.Object)?.name?.StartsWith("SpoilsOfTheSlain_") == true) continue;

                        int tier = SafeTierValue(outcome);

                        // Snapshot the ingredient list as ItemTemplate + count tuples
                        var ings = r.Ingredients;
                        if (ings == null || ings.Length == 0) continue;
                        var snapshot = new List<(ItemTemplate, int)>();
                        foreach (var ing in ings)
                        {
                            try
                            {
                                var ingT = ing.Template;
                                int c = ing.Count;
                                if (ingT != null && c > 0) snapshot.Add((ingT, c));
                            }
                            catch { }
                        }
                        if (snapshot.Count == 0) continue;

                        if (!byTier.ContainsKey(tier)) byTier[tier] = new List<List<(ItemTemplate, int)>>();
                        byTier[tier].Add(snapshot);
                    }
                }
                _vanillaByStationTier[station] = byTier;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Cost] LearnFromTemplates<{typeof(T).Name}> failed: {e.GetBaseException().Message}");
            }
        }

        public static List<(ItemTemplate template, int count)> BuildIngredients(RecipeFactory.Station station, ItemTemplate outcome)
        {
            EnsureLearned();
            var fallback = new List<(ItemTemplate, int)>();
            if (outcome == null) return fallback;

            int tier = Math.Max(0, SafeTierValue(outcome));

            if (!_vanillaByStationTier.TryGetValue(station, out var tierMap)) return fallback;

            // Find the closest tier with samples — exact match preferred, then walk down then up.
            List<List<(ItemTemplate, int)>> samples = null;
            if (tierMap.TryGetValue(tier, out var exact)) samples = exact;
            for (int delta = 1; samples == null && delta <= 5; delta++)
            {
                if (tierMap.TryGetValue(tier - delta, out var lower) && lower.Count > 0) samples = lower;
                else if (tierMap.TryGetValue(tier + delta, out var higher) && higher.Count > 0) samples = higher;
            }
            if (samples == null || samples.Count == 0) return fallback;

            // Pick a deterministic sample by hashing the outcome's GUID — same outcome always
            // gets same recipe cost. Avoids different forge openings showing different costs.
            int idx = 0;
            try { idx = Math.Abs((outcome.GUID ?? outcome.name ?? "").GetHashCode()) % samples.Count; }
            catch { }
            var picked = samples[idx];

            // Apply a small markup so crafting is slightly more expensive than vanilla.
            float multiplier = Math.Max(1f, Plugin.Cfg.BasePriceMultiplier.Value);
            var result = new List<(ItemTemplate, int)>();
            foreach (var (t, c) in picked)
            {
                int adjusted = Math.Max(1, (int)Math.Round(c * multiplier));
                result.Add((t, adjusted));
            }
            return result;
        }

        // Awaken stores the *displayed* tier as a tag like "item:tier3" rather than via the
        // RichEnum.Tier property (which seems to default to 0 for everything we've inspected).
        // Tags are the source of truth — check them first.
        private static int SafeTierValue(ItemTemplate t)
        {
            try
            {
                var tags = t.Tags;
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        if (tag == null) continue;
                        // Match "item:tierN" exactly (cases tier0 through tier9 are realistic)
                        if (tag.Length == 10 && tag.StartsWith("item:tier") && char.IsDigit(tag[9]))
                            return tag[9] - '0';
                    }
                }
            }
            catch { }
            // Fallback: try the RichEnum if tags didn't reveal anything
            try
            {
                var tier = t.Tier;
                var tierType = tier.GetType();
                var indexProp = tierType.GetProperty("EnumValue") ?? tierType.GetProperty("Index") ?? tierType.GetProperty("Value");
                if (indexProp != null)
                {
                    var v = indexProp.GetValue(tier);
                    if (v is int i) return i;
                    if (v != null && int.TryParse(v.ToString(), out var parsed)) return parsed;
                }
            }
            catch { }
            return 0;
        }
    }
}
