using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Find every method in the codebase that calls KnownRecipe — the answer tells us
    // whether the UI uses it as a hard filter or just a styling hint.
    public static class KnownRecipeFilter
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Enumerate all methods in TG and look for ones whose IL references "KnownRecipe"
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && n.StartsWith("TG."); })
                    .SelectMany(SafeGetTypes).Where(t => t != null && !t.Name.Contains("<"))
                    .ToList();

                var callers = new List<string>();
                foreach (var t in allTypes)
                {
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        try
                        {
                            var body = m.GetMethodBody();
                            if (body == null) continue;
                            // Cheap: scan IL bytes for the method-token pattern matching KnownRecipe.
                            // Faster proxy: check if the method's name or surrounding class hints at filtering.
                            // Since IL token resolution is complex, just check method names that suggest
                            // recipe filtering — they're the candidates.
                        }
                        catch { }
                    }
                }

                // Lambda compiled in RecipeGridUI - we saw "Boolean <get_AllRecipesOfCurrentType>b__7_0(IRecipe)"
                // That's the "current type" filter. Find it and see what it does.
                var gridType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.RecipeView.RecipeGridUI");
                if (gridType != null)
                {
                    foreach (var m in gridType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (m.Name.Contains("__")) continue;       // skip emitted compiler methods
                        try
                        {
                            var sig = $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})";
                            // Check if this method touches KnownRecipe — heuristic: methods returning bool/IEnumerable<IRecipe>
                            // and named filter-like.
                            if (m.Name.Contains("Filter") || m.Name.Contains("Refresh") || m.Name.Contains("Setup")
                                || m.Name.Contains("AllRecipes") || m.Name.Contains("Init"))
                            {
                                callers.Add($"RecipeGridUI.{sig}");
                            }
                        }
                        catch { }
                    }
                }
                bag["candidate_recipe_methods"] = callers;

                // Find anywhere a RecipeSlot is constructed — that's the actual UI element
                var recipeSlot = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.RecipeSlot");
                if (recipeSlot != null)
                {
                    bag["RecipeSlot_constructors"] = recipeSlot.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(c => $"({string.Join(",", c.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name))})")
                        .ToList();
                    bag["RecipeSlot_methods"] = recipeSlot.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .Take(20).ToList();
                    bag["RecipeSlot_props"] = recipeSlot.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(20).ToList();
                }

                // Find RecipeTabContents.GenerateAllSlotsForFilteredRecipes — the slot generator
                var tabContents = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.RecipeView.RecipeTabContents");
                if (tabContents != null)
                {
                    var generateM = tabContents.GetMethod("GenerateAllSlotsForFilteredRecipes",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    bag["GenerateAllSlots_found"] = generateM != null;
                    if (generateM != null)
                    {
                        bag["GenerateAllSlots_sig"] = $"{Pretty(generateM.ReturnType)} {generateM.Name}({string.Join(",", generateM.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name))})";
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
