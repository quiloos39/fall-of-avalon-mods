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
    // Reach into the live CraftAnything assembly, force a rebuild of its recipe cache,
    // and report how many new recipes generate now that KnownItems has 223 extra entries.
    public static class InjectStateProbe
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
                var our = ourRecipesField?.GetValue(null) as System.Collections.ICollection;
                bag["our_recipes_before"] = our?.Count ?? -1;

                // Counters from RecipeFactory
                var factoryType = caAsm.GetType("CraftAnything.RecipeFactory");
                bag["build_success_before"] = factoryType.GetField("BuildSuccess").GetValue(null);
                bag["build_fail_noref_before"] = factoryType.GetField("BuildFailNoRef").GetValue(null);
                bag["build_fail_exc_before"] = factoryType.GetField("BuildFailException").GetValue(null);

                // Clear CraftAnything's cache
                var cacheField = cacheType.GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static);
                var cache = cacheField?.GetValue(null) as System.Collections.IDictionary;
                int cacheBefore = cache?.Count ?? -1;
                cache?.Clear();
                bag["cache_entries_cleared"] = cacheBefore;

                // Also clear OurRecipes (forces regeneration)
                if (our != null)
                {
                    var clearM = our.GetType().GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
                    clearM?.Invoke(our, null);
                }

                // Reset counters
                factoryType.GetField("BuildSuccess").SetValue(null, 0);
                factoryType.GetField("BuildFailNoRef").SetValue(null, 0);
                factoryType.GetField("BuildFailException").SetValue(null, 0);

                // Now invoke the Forge recipe rebuild via the patched template's getter
                // Simulate calling each station's Recipes property
                var hcType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.HandcraftingTemplate");
                var alchType = ResolveType("Awaken.TG.Main.Crafting.AlchemyCrafting.AlchemyTemplate");
                var provider = World.Services?.Get<TemplatesProvider>();
                var getAllOfType = typeof(TemplatesProvider).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

                int totalForge = 0, totalAlch = 0;
                foreach (var ct in (getAllOfType.MakeGenericMethod(hcType).Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable) ?? new object[0])
                {
                    var recipes = ct.GetType().GetProperty("Recipes")?.GetValue(ct) as System.Collections.IEnumerable;
                    foreach (var _ in recipes ?? new object[0]) totalForge++;
                }
                foreach (var ct in (getAllOfType.MakeGenericMethod(alchType).Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable) ?? new object[0])
                {
                    var recipes = ct.GetType().GetProperty("Recipes")?.GetValue(ct) as System.Collections.IEnumerable;
                    foreach (var _ in recipes ?? new object[0]) totalAlch++;
                }

                bag["forge_recipes_total_after"] = totalForge;
                bag["alchemy_recipes_total_after"] = totalAlch;
                bag["our_recipes_after"] = (ourRecipesField?.GetValue(null) as System.Collections.ICollection)?.Count;
                bag["build_success_after"] = factoryType.GetField("BuildSuccess").GetValue(null);
                bag["build_fail_noref_after"] = factoryType.GetField("BuildFailNoRef").GetValue(null);
                bag["build_fail_exc_after"] = factoryType.GetField("BuildFailException").GetValue(null);

                // Also peek at KnownItems count
                var hero = Hero.Current;
                var heroItems = hero?.Element<HeroItems>();
                bag["known_count"] = heroItems?.KnownItems?.Count;
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
