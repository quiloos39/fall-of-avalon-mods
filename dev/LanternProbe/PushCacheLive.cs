using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Awaken.TG.Main.Crafting.Recipes;
using Awaken.TG.Main.Templates;
using Awaken.TG.MVC;

namespace LanternProbe
{
    // Replace CraftAnything's RecipeInjectionCache entries directly so the forge sees ALL
    // OurRecipes for each station, not just what BuildAllForStation rebuilt last. Dedupes
    // by outcome GUID and excludes vanilla outcomes.
    public static class PushCacheLive
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var caAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "CraftAnything");
                if (caAsm == null) { bag["err"] = "CraftAnything not loaded"; return JsonConvert.SerializeObject(bag, Formatting.Indented); }

                var cacheType = caAsm.GetType("CraftAnything.RecipeInjectionCache");
                var stationType = caAsm.GetType("CraftAnything.RecipeFactory+Station");
                var ourRecipesField = cacheType.GetField("OurRecipes", BindingFlags.Public | BindingFlags.Static);
                var ourRecipes = ourRecipesField.GetValue(null);

                // Snapshot OurRecipes
                var ourList = new List<IRecipe>();
                foreach (var r in ourRecipes as System.Collections.IEnumerable)
                {
                    if (r is IRecipe ir) ourList.Add(ir);
                }
                bag["our_recipes_total"] = ourList.Count;

                // Determine each recipe's station from its component type
                var hcRecipeType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.HandcraftingRecipe");
                var alchRecipeType = ResolveType("Awaken.TG.Main.Crafting.AlchemyCrafting.AlchemyRecipe");
                var cookRecipeType = ResolveType("Awaken.TG.Main.Crafting.Cooking.CookingRecipe");

                var forgeRecipes = new List<IRecipe>();
                var alchRecipes = new List<IRecipe>();
                var cookRecipes = new List<IRecipe>();
                var seenForge = new HashSet<string>();
                var seenAlch = new HashSet<string>();
                var seenCook = new HashSet<string>();

                foreach (var r in ourList)
                {
                    var g = r.Outcome?.GUID;
                    if (string.IsNullOrEmpty(g)) continue;
                    if (hcRecipeType != null && r.GetType() == hcRecipeType)
                    {
                        if (seenForge.Add(g)) forgeRecipes.Add(r);
                    }
                    else if (alchRecipeType != null && r.GetType() == alchRecipeType)
                    {
                        if (seenAlch.Add(g)) alchRecipes.Add(r);
                    }
                    else if (cookRecipeType != null && r.GetType() == cookRecipeType)
                    {
                        if (seenCook.Add(g)) cookRecipes.Add(r);
                    }
                }
                bag["dedup_forge"] = forgeRecipes.Count;
                bag["dedup_alchemy"] = alchRecipes.Count;
                bag["dedup_cooking"] = cookRecipes.Count;

                // Get vanilla outcome GUIDs to exclude from our injection
                var provider = World.Services?.Get<TemplatesProvider>();
                var getAllOfType = typeof(TemplatesProvider).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

                HashSet<string> ExistingOutcomes(string templateTypeName)
                {
                    var t = ResolveType(templateTypeName);
                    var set = new HashSet<string>();
                    if (t == null) return set;
                    foreach (var ct in (getAllOfType.MakeGenericMethod(t).Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable))
                    {
                        var recipes = ct.GetType().GetProperty("Recipes")?.GetValue(ct) as System.Collections.IEnumerable;
                        foreach (var r in recipes ?? new object[0])
                        {
                            var rname = (r as UnityEngine.Object)?.name ?? "";
                            if (rname.StartsWith("CraftAnything_")) continue;
                            if (r is IRecipe ir) { var g = ir.Outcome?.GUID; if (!string.IsNullOrEmpty(g)) set.Add(g); }
                        }
                    }
                    return set;
                }
                var vanillaForge = ExistingOutcomes("Awaken.TG.Main.Crafting.HandCrafting.HandcraftingTemplate");
                var vanillaAlch = ExistingOutcomes("Awaken.TG.Main.Crafting.AlchemyCrafting.AlchemyTemplate");
                var vanillaCook = ExistingOutcomes("Awaken.TG.Main.Crafting.Cooking.CookingTemplate");

                forgeRecipes = forgeRecipes.Where(r => !vanillaForge.Contains(r.Outcome.GUID)).ToList();
                alchRecipes = alchRecipes.Where(r => !vanillaAlch.Contains(r.Outcome.GUID)).ToList();
                cookRecipes = cookRecipes.Where(r => !vanillaCook.Contains(r.Outcome.GUID)).ToList();

                // Now stuff each station's cache entry with our deduped list
                var cacheField = cacheType.GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static);
                var cache = cacheField.GetValue(null) as System.Collections.IDictionary;

                // Cache value type: ValueTuple<int, List<IRecipe>, HashSet<string>>
                var stationForge = Enum.Parse(stationType, "Forge");
                var stationAlch = Enum.Parse(stationType, "Alchemy");
                var stationCook = Enum.Parse(stationType, "Cooking");

                // Build the tuple type to write back
                var tupleType = typeof(ValueTuple<,,>).MakeGenericType(typeof(int), typeof(List<IRecipe>), typeof(HashSet<string>));
                int forceCount = -1;
                object MakeEntry(int count, List<IRecipe> recipes, HashSet<string> existing)
                    => Activator.CreateInstance(tupleType, new object[] { count, recipes, existing });

                // Use the current known count so cache hits on next forge open.
                var heroT = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroT.GetProperty("Current", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                var heroItemsT = ResolveType("Awaken.TG.Main.Heroes.Items.HeroItems");
                var heroItems = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0)
                    .MakeGenericMethod(heroItemsT).Invoke(hero, null);
                var known = heroItemsT.GetProperty("KnownItems").GetValue(heroItems) as HashSet<string>;
                int knownCount = known.Count;

                cache[stationForge] = MakeEntry(knownCount, forgeRecipes, vanillaForge);
                cache[stationAlch] = MakeEntry(knownCount, alchRecipes, vanillaAlch);
                cache[stationCook] = MakeEntry(knownCount, cookRecipes, vanillaCook);

                bag["pushed_forge"] = forgeRecipes.Count;
                bag["pushed_alchemy"] = alchRecipes.Count;
                bag["pushed_cooking"] = cookRecipes.Count;
                bag["known_count"] = knownCount;
                bag["vanilla_forge_outcomes"] = vanillaForge.Count;
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
