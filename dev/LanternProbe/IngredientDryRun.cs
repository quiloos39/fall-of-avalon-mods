using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Dry-run v0.7's CostGenerator logic without restarting. For each tier, sample what
    // vanilla forge ingredients look like — so we can confirm we're picking real materials,
    // not Weatherfish. Also pick 5 of the user's known weapons and show what ingredients
    // they'd get under v0.7.
    public static class IngredientDryRun
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);

                var hcType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.HandcraftingTemplate");
                var flagType = ResolveType("Awaken.TG.Main.Templates.TemplateTypeFlag");
                var flagRegular = Enum.Parse(flagType, "Regular");
                var getAllOfType = providerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

                // Walk vanilla forge recipes, group ingredients by tier
                var byTier = new Dictionary<int, List<List<(string name, int count)>>>();
                var hcTemplates = getAllOfType?.MakeGenericMethod(hcType).Invoke(provider, new object[] { flagRegular }) as System.Collections.IEnumerable;
                foreach (var ct in hcTemplates ?? new object[0])
                {
                    var recipes = ct.GetType().GetProperty("Recipes")?.GetValue(ct) as System.Collections.IEnumerable;
                    foreach (var r in recipes ?? new object[0])
                    {
                        var rname = (r as UnityEngine.Object)?.name ?? "";
                        if (rname.StartsWith("CraftAnything_")) continue;

                        var outcome = r.GetType().GetProperty("Outcome")?.GetValue(r);
                        if (outcome == null) continue;
                        int tier = SafeTierValue(outcome);

                        var ings = r.GetType().GetProperty("Ingredients")?.GetValue(r) as Array;
                        if (ings == null || ings.Length == 0) continue;
                        var snap = new List<(string, int)>();
                        foreach (var ing in ings)
                        {
                            var ingT = ing.GetType().GetProperty("Template")?.GetValue(ing);
                            int c = (int)(ing.GetType().GetProperty("Count")?.GetValue(ing) ?? 0);
                            string ingName = ingT?.GetType().GetProperty("ItemName")?.GetValue(ingT)?.ToString();
                            if (string.IsNullOrEmpty(ingName)) ingName = (ingT as UnityEngine.Object)?.name;
                            if (!string.IsNullOrEmpty(ingName) && c > 0) snap.Add((ingName, c));
                        }
                        if (snap.Count == 0) continue;
                        if (!byTier.ContainsKey(tier)) byTier[tier] = new List<List<(string, int)>>();
                        byTier[tier].Add(snap);
                    }
                }

                // Sample first 3 ingredient sets per tier (gives us a sense of the typical recipe shape)
                foreach (var kv in byTier.OrderBy(x => x.Key))
                {
                    var samples = kv.Value.Take(3).Select(s =>
                        string.Join(", ", s.Select(p => $"{p.count}× {p.name}"))
                    ).ToList();
                    bag[$"vanilla_tier{kv.Key}_count"] = kv.Value.Count;
                    bag[$"vanilla_tier{kv.Key}_samples"] = samples;
                }

                // Now pick 5 of the user's known weapons (tier 1+) and show what ingredients v0.7
                // would generate for them
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var heroItemsType = ResolveType("Awaken.TG.Main.Heroes.Items.HeroItems");
                var heroItems = hero?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0)
                    ?.MakeGenericMethod(heroItemsType).Invoke(hero, null);
                var known = heroItemsType.GetProperty("KnownItems")?.GetValue(heroItems) as HashSet<string>;

                var itemTemplate = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                var allItems = getAllOfType?.MakeGenericMethod(itemTemplate).Invoke(provider, new object[] { flagRegular }) as System.Collections.IEnumerable;
                var byGuid = new Dictionary<string, object>();
                foreach (var it in allItems ?? new object[0])
                {
                    var g = it?.GetType().GetProperty("GUID")?.GetValue(it) as string;
                    if (!string.IsNullOrEmpty(g)) byGuid[g] = it;
                }

                int picked = 0;
                var dryRunResults = new List<string>();
                foreach (var guid in known ?? new HashSet<string>())
                {
                    if (picked >= 5) break;
                    if (!byGuid.TryGetValue(guid, out var t)) continue;

                    // v0.7 filters: tier 1+, has ItemName, weapon tag
                    var name = t.GetType().GetProperty("ItemName")?.GetValue(t)?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    int tier = SafeTierValue(t);
                    if (tier < 1) continue;

                    var tagsList = new List<string>();
                    var tags = t.GetType().GetProperty("Tags")?.GetValue(t) as System.Collections.IEnumerable;
                    if (tags != null) foreach (var tag in tags) tagsList.Add(tag?.ToString());
                    if (!tagsList.Contains("item:weapon") && !tagsList.Any(x => x?.StartsWith("weapons:") == true)) continue;

                    // What ingredients would v0.7 give it? (deterministic by GUID hash)
                    if (byTier.TryGetValue(tier, out var tierSamples) && tierSamples.Count > 0)
                    {
                        int idx = Math.Abs(guid.GetHashCode()) % tierSamples.Count;
                        var picked_ings = tierSamples[idx];
                        var ingsStr = string.Join(", ", picked_ings.Select(p => $"{p.count}× {p.name}"));
                        dryRunResults.Add($"\"{name}\" (tier{tier}) → {ingsStr}");
                    }
                    else
                    {
                        dryRunResults.Add($"\"{name}\" (tier{tier}) → NO VANILLA REFERENCE FOR THIS TIER");
                    }
                    picked++;
                }
                bag["dry_run_results"] = dryRunResults;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        // Tier is encoded as a tag like "item:tier3" — not the .Tier RichEnum (which is 0 for everything we've seen).
        private static int SafeTierValue(object t)
        {
            try
            {
                var tags = t.GetType().GetProperty("Tags")?.GetValue(t) as System.Collections.IEnumerable;
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        var s = tag?.ToString();
                        if (s == null) continue;
                        if (s.Length == 10 && s.StartsWith("item:tier") && char.IsDigit(s[9]))
                            return s[9] - '0';
                    }
                }
            }
            catch { }
            return 0;
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
