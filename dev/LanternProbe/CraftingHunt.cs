using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Investigates whether a "craft anything you've discovered/sold" mod is feasible.
    // Hunts for:
    //   1. Recipe / RecipeTemplate / CraftingRecipe types (do recipes exist as data?)
    //   2. The crafting station logic (how does Forge enumerate recipes?)
    //   3. Discovery / "ever owned" tracking (is there a flag per item, or only current inventory?)
    //   4. Enemy kill tracking (for unique armor drops)
    //   5. Loot tables on enemy templates
    public static class CraftingHunt
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var tgAsms = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && (n.StartsWith("TG.") || n.StartsWith("Awaken.")); })
                    .ToList();

                var allTypes = tgAsms.SelectMany(SafeGetTypes).Where(t => t != null).ToList();
                bag["scanned_types"] = allTypes.Count;

                // 1. Recipe-related types
                var recipeTypes = allTypes.Where(t =>
                    t.Name.IndexOf("Recipe", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Crafting", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Forge", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Alchem", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Cook", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Ingredient", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(t => t.FullName)
                    .OrderBy(n => n)
                    .Take(80)
                    .ToList();
                bag["crafting_types"] = recipeTypes;

                // 2. Discovery / "known" / "discovered" / "owned" / "seen"
                var discoveryTypes = allTypes.Where(t =>
                    (t.Name.IndexOf("Discover", StringComparison.OrdinalIgnoreCase) >= 0
                     || t.Name.IndexOf("Known", StringComparison.OrdinalIgnoreCase) >= 0
                     || t.Name.IndexOf("Identified", StringComparison.OrdinalIgnoreCase) >= 0
                     || (t.Name.IndexOf("Item", StringComparison.OrdinalIgnoreCase) >= 0 && t.Name.IndexOf("Track", StringComparison.OrdinalIgnoreCase) >= 0))
                    && !t.IsGenericTypeDefinition)
                    .Select(t => t.FullName)
                    .OrderBy(n => n)
                    .Take(50)
                    .ToList();
                bag["discovery_types"] = discoveryTypes;

                // 3. Story facts / progression keys (item discovery often baked into story flags)
                var factTypes = allTypes.Where(t =>
                    t.Name.IndexOf("StoryFlag", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Facts", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("FactsTracker", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Progression", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(t => t.FullName)
                    .OrderBy(n => n)
                    .Take(40)
                    .ToList();
                bag["facts_types"] = factTypes;

                // 4. Loot drop / kill tracking
                var killLootTypes = allTypes.Where(t =>
                    t.Name.IndexOf("Loot", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Drop", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Kill", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Defeated", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Bestiary", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(t => t.FullName)
                    .OrderBy(n => n)
                    .Take(60)
                    .ToList();
                bag["kill_loot_types"] = killLootTypes;

                // 5. HeroItems / inventory element
                var heroItemTypes = allTypes.Where(t =>
                    t.Name.IndexOf("HeroItems", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Inventory", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(t => t.FullName)
                    .OrderBy(n => n)
                    .Take(20)
                    .ToList();
                bag["hero_inventory_types"] = heroItemTypes;

                // 6. Detailed inspection of the most promising "Recipe" type
                Type recipeType = allTypes.FirstOrDefault(t => t.Name == "Recipe")
                                 ?? allTypes.FirstOrDefault(t => t.Name == "RecipeTemplate")
                                 ?? allTypes.FirstOrDefault(t => t.Name == "CraftingRecipe");
                if (recipeType != null)
                {
                    bag["recipe_inspect_type"] = recipeType.FullName;
                    bag["recipe_inspect_base"] = recipeType.BaseType?.FullName;
                    bag["recipe_inspect_fields"] = recipeType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(40).ToList();
                    bag["recipe_inspect_props"] = recipeType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(40).ToList();
                }

                // 7. Detailed inspection of any "*Crafting*" attachment / spec
                var craftAttach = allTypes.FirstOrDefault(t => t.Name == "CraftingAttachment")
                              ?? allTypes.FirstOrDefault(t => t.Name == "CraftingTemplate")
                              ?? allTypes.FirstOrDefault(t => t.Name.Contains("CraftingAttachment"))
                              ?? allTypes.FirstOrDefault(t => t.Name == "CraftingSpec");
                if (craftAttach != null)
                {
                    bag["craft_attach_type"] = craftAttach.FullName;
                    bag["craft_attach_fields"] = craftAttach.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(40).ToList();
                    bag["craft_attach_props"] = craftAttach.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(40).ToList();
                }

                // 8. Try to resolve TemplatesProvider and count recipe instances
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);
                bag["provider_resolved"] = provider != null;

                if (provider != null && recipeType != null)
                {
                    var getAllOfType = providerType.GetMethods()
                        .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                    try
                    {
                        var allRecipes = getAllOfType?.MakeGenericMethod(recipeType).Invoke(provider, null) as System.Collections.IEnumerable;
                        int count = 0;
                        var samples = new List<string>();
                        foreach (var r in allRecipes ?? new object[0])
                        {
                            count++;
                            if (samples.Count < 10)
                            {
                                var nameProp = r.GetType().GetProperty("name") ?? r.GetType().GetProperty("Name") ?? r.GetType().GetProperty("DisplayName");
                                samples.Add($"{r.GetType().Name}: {nameProp?.GetValue(r)}");
                            }
                        }
                        bag["recipe_total_in_provider"] = count;
                        bag["recipe_samples"] = samples;
                    }
                    catch (Exception e) { bag["recipe_enum_err"] = e.GetBaseException().Message; }
                }

                // 9. Hero element types
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                bag["hero_resolved"] = hero != null;
                if (hero != null)
                {
                    // Try to enumerate all elements
                    var elementsProp = hero.GetType().GetProperty("Elements")
                                     ?? hero.GetType().BaseType?.GetProperty("Elements");
                    var elements = elementsProp?.GetValue(hero) as System.Collections.IEnumerable;
                    if (elements != null)
                    {
                        bag["hero_elements"] = elements.Cast<object>().Select(e => e.GetType().FullName).Distinct().Take(80).ToList();
                    }

                    // Try generic Element<T> on candidate types - HeroFactsTracker, HeroItems, etc.
                    var elementMethod = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);

                    foreach (var typeName in new[] {
                        "Awaken.TG.Main.Heroes.HeroItems",
                        "Awaken.TG.Main.Stories.Core.FactsTracker",
                        "Awaken.TG.Main.Stories.Quests.QuestTracker",
                        "Awaken.TG.Main.Heroes.HeroDiscovery",
                        "Awaken.TG.Main.Heroes.HeroEnemiesTracker",
                        "Awaken.TG.Main.Heroes.HeroLoot",
                    })
                    {
                        var candidate = ResolveType(typeName);
                        if (candidate == null) continue;
                        try
                        {
                            var el = elementMethod?.MakeGenericMethod(candidate).Invoke(hero, null);
                            bag["hero_element_" + candidate.Name] = el != null ? el.GetType().FullName : "null";
                        }
                        catch (Exception e)
                        {
                            bag["hero_element_" + candidate.Name + "_err"] = e.GetBaseException().Message;
                        }
                    }
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch { return Array.Empty<Type>(); }
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }

        private static string Pretty(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.Name;
            return t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(g => g.Name)) + ">";
        }
    }
}
