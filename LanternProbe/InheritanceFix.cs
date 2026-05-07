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
    // Bypass CraftAnything's tag-based StationFilter (which misses inheritance-only items)
    // by directly invoking RecipeFactory.Build for every KnownItems entry that's IsWeapon/
    // IsArmor/etc per the game's inheritance check, but doesn't yet have a recipe.
    //
    // After running, the new recipes are in OurRecipes (so IsLearned patch accepts them)
    // and will appear when the player reopens the forge UI.
    public static class InheritanceFix
    {
        public static string Run() => DoRun(commit: true);

        private static string DoRun(bool commit)
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var caAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "CraftAnything");
                if (caAsm == null) { bag["err"] = "CraftAnything not loaded"; return JsonConvert.SerializeObject(bag, Formatting.Indented); }

                var factoryType = caAsm.GetType("CraftAnything.RecipeFactory");
                var stationType = caAsm.GetType("CraftAnything.RecipeFactory+Station");
                var cacheType = caAsm.GetType("CraftAnything.RecipeInjectionCache");
                var ourRecipesField = cacheType.GetField("OurRecipes", BindingFlags.Public | BindingFlags.Static);
                var ourRecipes = ourRecipesField.GetValue(null);
                var ourAddM = ourRecipes.GetType().GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
                var ourContainsM = ourRecipes.GetType().GetMethod("Contains", BindingFlags.Public | BindingFlags.Instance);
                var ourCountP = ourRecipes.GetType().GetProperty("Count");

                var costGeneratorType = caAsm.GetType("CraftAnything.CostGenerator");
                var buildIngsM = costGeneratorType.GetMethod("BuildIngredients", BindingFlags.Public | BindingFlags.Static);
                var buildM = factoryType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);

                var stationForge = Enum.Parse(stationType, "Forge");
                var stationAlchemy = Enum.Parse(stationType, "Alchemy");

                var hero = Hero.Current;
                var heroItems = hero?.Element<HeroItems>();
                var known = heroItems?.KnownItems;
                if (known == null) { bag["err"] = "no KnownItems"; return JsonConvert.SerializeObject(bag, Formatting.Indented); }

                var provider = World.Services?.Get<TemplatesProvider>();
                var getAllOfType = typeof(TemplatesProvider).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

                // Build GUID → template lookup
                var byGuid = new Dictionary<string, ItemTemplate>();
                foreach (var x in (getAllOfType.MakeGenericMethod(typeof(ItemTemplate)).Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable))
                {
                    if (x is ItemTemplate it && !string.IsNullOrEmpty(it.GUID)) byGuid[it.GUID] = it;
                }

                // Build set of recipe outcome GUIDs already in vanilla forge & alchemy
                var existingForgeOutcomes = new HashSet<string>();
                var hcType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.HandcraftingTemplate");
                foreach (var ct in (getAllOfType.MakeGenericMethod(hcType).Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable))
                {
                    var recipes = ct.GetType().GetProperty("Recipes")?.GetValue(ct) as System.Collections.IEnumerable;
                    foreach (var r in recipes ?? new object[0])
                    {
                        if (r is IRecipe ir)
                        {
                            var rname = (r as UnityEngine.Object)?.name ?? "";
                            if (rname.StartsWith("CraftAnything_")) continue;     // skip our own
                            var g = ir.Outcome?.GUID;
                            if (!string.IsNullOrEmpty(g)) existingForgeOutcomes.Add(g);
                        }
                    }
                }

                // For OurRecipes — collect outcome GUIDs we've already generated recipes for
                var ourOutcomes = new HashSet<string>();
                foreach (var r in ourRecipes as System.Collections.IEnumerable)
                {
                    if (r is IRecipe ir) { var g = ir.Outcome?.GUID; if (!string.IsNullOrEmpty(g)) ourOutcomes.Add(g); }
                }
                bag["our_recipes_count_before"] = (int)ourCountP.GetValue(ourRecipes);
                bag["our_outcomes_unique"] = ourOutcomes.Count;

                // Walk KnownItems, generate recipes for items that are IsWeapon/IsArmor/etc
                // but don't yet have a recipe.
                int newForge = 0, newAlchemy = 0, alreadyHad = 0;
                var newSamples = new List<string>();

                foreach (var guid in known)
                {
                    if (!byGuid.TryGetValue(guid, out var t)) continue;
                    if (t.HiddenOnUI) continue;
                    if (string.IsNullOrWhiteSpace(t.ItemName)) continue;

                    // Already has a recipe (vanilla or ours)?
                    if (existingForgeOutcomes.Contains(guid) || ourOutcomes.Contains(guid)) { alreadyHad++; continue; }

                    var tags = t.Tags?.ToList() ?? new List<string>();
                    if (tags.Contains("item:tier0")) continue;
                    if (tags.Contains("item:book") || tags.Contains("item:recipe") || tags.Contains("item:spell") || tags.Contains("item:key")) continue;
                    if (tags.Any(x => x.StartsWith("ingredients:")) || tags.Any(x => x.StartsWith("craft:"))) continue;

                    object station;
                    if (t.IsWeapon || t.IsArmor || t.IsShield || t.IsRanged || t.IsArrow || t.IsJewelry)
                    {
                        station = stationForge;
                    }
                    else if (tags.Contains("item:potion") || tags.Contains("item:potionHP") || tags.Contains("item:potionMP")
                          || tags.Contains("item:potionSP") || tags.Contains("item:potionOther") || tags.Contains("item:weapongrease")
                          || tags.Any(x => x.StartsWith("potions:")) || tags.Any(x => x.StartsWith("alchemy:")))
                    {
                        station = stationAlchemy;
                    }
                    else continue;

                    if (commit)
                    {
                        try
                        {
                            var ings = buildIngsM.Invoke(null, new object[] { station, t });
                            var recipe = buildM.Invoke(null, new object[] { station, t, ings, 0 });
                            if (recipe != null)
                            {
                                ourAddM.Invoke(ourRecipes, new object[] { recipe });
                                if (station.Equals(stationForge)) newForge++;
                                else newAlchemy++;
                                if (newSamples.Count < 12) newSamples.Add(t.ItemName);
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        if (station.Equals(stationForge)) newForge++;
                        else newAlchemy++;
                        if (newSamples.Count < 12) newSamples.Add(t.ItemName);
                    }
                }

                bag["already_had_recipe"] = alreadyHad;
                bag["new_forge_recipes"] = newForge;
                bag["new_alchemy_recipes"] = newAlchemy;
                bag["new_recipe_samples"] = newSamples;
                bag["our_recipes_count_after"] = (int)ourCountP.GetValue(ourRecipes);
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
