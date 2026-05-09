using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Learn Awaken's actual item tag taxonomy by:
    //  1. Sampling tags on items currently in HeroItems.KnownItems
    //  2. Looking at the OUTCOME items of every vanilla forge recipe to see what tags
    //     "things craftable at the forge" actually have. Same for alchemy & cooking.
    //
    // Output: a tag frequency table per station + sample items per station.
    // From this we can write a correct filter (or even auto-derive it).
    public static class TagTaxonomy
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var heroItemsType = ResolveType("Awaken.TG.Main.Heroes.Items.HeroItems");
                var heroItems = hero?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0)
                    ?.MakeGenericMethod(heroItemsType).Invoke(hero, null);

                var knownProp = heroItemsType.GetProperty("KnownItems");
                var known = knownProp?.GetValue(heroItems) as HashSet<string>;
                bag["known_count"] = known?.Count ?? 0;

                // Resolve provider + all ItemTemplates
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getServiceMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getServiceMethod?.MakeGenericMethod(providerType).Invoke(services, null);

                var itemTemplateType = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                var flagType = ResolveType("Awaken.TG.Main.Templates.TemplateTypeFlag");
                var flagRegular = Enum.Parse(flagType, "Regular");

                // Single-arg generic overload — the right one for our needs.
                var getAllOfType = providerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

                bag["provider_null"] = provider == null;
                bag["getAllOfType_null"] = getAllOfType == null;
                bag["itemTemplateType_null"] = itemTemplateType == null;

                System.Collections.IEnumerable allItemsRaw = null;
                try
                {
                    allItemsRaw = getAllOfType?.MakeGenericMethod(itemTemplateType).Invoke(provider, new object[] { flagRegular }) as System.Collections.IEnumerable;
                }
                catch (Exception e) { bag["getAllOfType_invoke_err"] = e.GetBaseException().Message; }
                bag["allItemsRaw_null"] = allItemsRaw == null;

                // Iterate manually instead of Cast<object>() — Mono has known issues with
                // typed enumerables and the LINQ Cast operator.
                var allItems = new List<object>();
                if (allItemsRaw != null)
                {
                    foreach (var x in allItemsRaw) if (x != null) allItems.Add(x);
                }
                bag["all_items_count"] = allItems.Count;

                // Build GUID lookup for fast match — use reflection so we don't have to reference TG.Main
                var byGuid = new Dictionary<string, object>();
                foreach (var it in allItems)
                {
                    var g = it.GetType().GetProperty("GUID", BindingFlags.Public | BindingFlags.Instance)?.GetValue(it) as string;
                    if (!string.IsNullOrEmpty(g)) byGuid[g] = it;
                }
                bag["byGuid_count"] = byGuid.Count;

                // 1) Tag frequency on KnownItems
                var knownTagCounts = new Dictionary<string, int>();
                int knownResolved = 0;
                foreach (var guid in known ?? new HashSet<string>())
                {
                    if (!byGuid.TryGetValue(guid, out var it)) continue;
                    knownResolved++;
                    foreach (var tag in GetTags(it))
                    {
                        if (!knownTagCounts.ContainsKey(tag)) knownTagCounts[tag] = 0;
                        knownTagCounts[tag]++;
                    }
                }
                bag["known_resolved"] = knownResolved;
                bag["known_tag_freq_top30"] = knownTagCounts
                    .OrderByDescending(kv => kv.Value)
                    .Take(30)
                    .Select(kv => $"{kv.Value}x  {kv.Key}")
                    .ToList();

                // 2) For each station, walk vanilla recipe outcomes and record their tags
                foreach (var (stationName, templateTypeName) in new[]
                {
                    ("Forge",   "Awaken.TG.Main.Crafting.HandCrafting.HandcraftingTemplate"),
                    ("Alchemy", "Awaken.TG.Main.Crafting.AlchemyCrafting.AlchemyTemplate"),
                    ("Cooking", "Awaken.TG.Main.Crafting.Cooking.CookingTemplate"),
                })
                {
                    var templateType = ResolveType(templateTypeName);
                    if (templateType == null) continue;

                    var allTemplates = getAllOfType?.MakeGenericMethod(templateType).Invoke(provider, new object[] { flagRegular }) as System.Collections.IEnumerable;
                    var stationTagCounts = new Dictionary<string, int>();
                    int recipesSeen = 0;
                    var sampleNames = new List<string>();

                    foreach (var ct in allTemplates ?? new List<object>())
                    {
                        var recipesProp = ct.GetType().GetProperty("Recipes");
                        var recipes = recipesProp?.GetValue(ct) as System.Collections.IEnumerable;
                        foreach (var r in recipes ?? new object[0])
                        {
                            recipesSeen++;
                            var outcomeProp = r.GetType().GetProperty("Outcome");
                            var outcome = outcomeProp?.GetValue(r);
                            if (outcome == null) continue;
                            if (sampleNames.Count < 8)
                            {
                                var name = outcome.GetType().GetProperty("ItemName")?.GetValue(outcome)?.ToString()
                                        ?? (outcome as UnityEngine.Object)?.name ?? "?";
                                sampleNames.Add(name);
                            }
                            foreach (var tag in GetTags(outcome))
                            {
                                if (!stationTagCounts.ContainsKey(tag)) stationTagCounts[tag] = 0;
                                stationTagCounts[tag]++;
                            }
                        }
                    }
                    bag[$"vanilla_{stationName}_recipe_count"] = recipesSeen;
                    bag[$"vanilla_{stationName}_top_tags"] = stationTagCounts
                        .OrderByDescending(kv => kv.Value)
                        .Take(20)
                        .Select(kv => $"{kv.Value}x  {kv.Key}")
                        .ToList();
                    bag[$"vanilla_{stationName}_outcome_samples"] = sampleNames;
                }

                // 3) Sample 10 known items with their tags + name (raw inspection)
                var samples = new List<Dictionary<string, object>>();
                int n = 0;
                foreach (var guid in known ?? new HashSet<string>())
                {
                    if (n >= 10) break;
                    if (!byGuid.TryGetValue(guid, out var it)) continue;
                    var name = it.GetType().GetProperty("ItemName")?.GetValue(it)?.ToString()
                            ?? (it as UnityEngine.Object)?.name ?? "?";
                    samples.Add(new Dictionary<string, object>
                    {
                        ["guid"] = guid,
                        ["name"] = name,
                        ["tags"] = GetTags(it).ToList(),
                    });
                    n++;
                }
                bag["known_samples"] = samples;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static IEnumerable<string> GetTags(object item)
        {
            try
            {
                var tagsProp = item.GetType().GetProperty("Tags", BindingFlags.Public | BindingFlags.Instance);
                if (tagsProp != null)
                {
                    var tags = tagsProp.GetValue(item) as System.Collections.IEnumerable;
                    if (tags != null)
                    {
                        foreach (var t in tags) if (t != null) yield return t.ToString();
                    }
                }
            }
            finally { }
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
