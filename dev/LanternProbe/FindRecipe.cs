using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Templates;
using Awaken.TG.Main.Crafting.Recipes;
using Awaken.TG.MVC;

namespace LanternProbe
{
    // Find "Duel Knight"-related items, check whether they're in KnownItems, and whether
    // a forge recipe currently exists for them. Exposes exactly where the gap is.
    public static class FindRecipe
    {
        public static string Run(string searchTerm)
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var hero = Hero.Current;
                var heroItems = hero?.Element<HeroItems>();
                var known = heroItems?.KnownItems ?? new HashSet<string>();
                bag["known_count"] = known.Count;

                var provider = World.Services?.Get<TemplatesProvider>();
                var getAllOfType = typeof(TemplatesProvider).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

                // Find ItemTemplates whose ItemName contains the search term
                var allItems = getAllOfType?.MakeGenericMethod(typeof(ItemTemplate)).Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable;
                var matches = new List<Dictionary<string, object>>();
                foreach (var x in allItems ?? new object[0])
                {
                    if (x is ItemTemplate it)
                    {
                        var name = it.ItemName;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (name.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0) continue;

                        var tags = it.Tags?.ToList() ?? new List<string>();
                        var entry = new Dictionary<string, object>
                        {
                            ["name"] = name,
                            ["guid"] = it.GUID,
                            ["in_KnownItems"] = known.Contains(it.GUID),
                            ["tags"] = tags,
                            ["IsWeapon"] = TryProp<bool>(it, "IsWeapon"),
                            ["IsArmor"] = TryProp<bool>(it, "IsArmor"),
                            ["IsShield"] = TryProp<bool>(it, "IsShield"),
                            ["IsRanged"] = TryProp<bool>(it, "IsRanged"),
                            ["IsArrow"] = TryProp<bool>(it, "IsArrow"),
                            ["IsJewelry"] = TryProp<bool>(it, "IsJewelry"),
                            ["IsConsumable"] = TryProp<bool>(it, "IsConsumable"),
                            ["IsHidden"] = TryProp<bool>(it, "HiddenOnUI"),
                            ["CannotBeDropped"] = TryProp<bool>(it, "CannotBeDropped"),
                        };
                        matches.Add(entry);
                    }
                }
                bag["match_count"] = matches.Count;
                bag["matches"] = matches.Take(40).ToList();

                // For matches that ARE in KnownItems, check whether a recipe exists in the forge
                var hcType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.HandcraftingTemplate");
                var allHcTemplates = getAllOfType.MakeGenericMethod(hcType).Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable;

                var matchGuidsInKnown = matches.Where(m => (bool)m["in_KnownItems"]).Select(m => (string)m["guid"]).ToHashSet();
                var recipeOutcomeGuids = new HashSet<string>();
                int totalRecipes = 0;
                foreach (var ct in allHcTemplates)
                {
                    var recipes = ct.GetType().GetProperty("Recipes")?.GetValue(ct) as System.Collections.IEnumerable;
                    foreach (var r in recipes ?? new object[0])
                    {
                        totalRecipes++;
                        var ir = r as IRecipe;
                        var outcome = ir?.Outcome;
                        if (outcome == null) continue;
                        var g = outcome.GUID;
                        if (!string.IsNullOrEmpty(g)) recipeOutcomeGuids.Add(g);
                    }
                }
                bag["forge_total_recipes"] = totalRecipes;
                bag["forge_unique_outcomes"] = recipeOutcomeGuids.Count;

                var hasRecipe = matchGuidsInKnown.Where(g => recipeOutcomeGuids.Contains(g)).ToList();
                var noRecipe = matchGuidsInKnown.Where(g => !recipeOutcomeGuids.Contains(g)).ToList();
                bag["matches_with_recipe"] = hasRecipe.Count;
                bag["matches_without_recipe"] = noRecipe.Count;
                bag["no_recipe_names"] = matches.Where(m => noRecipe.Contains((string)m["guid"])).Select(m => m["name"]).ToList();
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static T TryProp<T>(object obj, string name)
        {
            try
            {
                var p = obj.GetType().GetProperty(name);
                if (p == null) return default;
                return (T)p.GetValue(obj);
            }
            catch { return default; }
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
