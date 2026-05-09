using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // Find the live RecipeGridUI (if forge is open), inspect its AllRecipes list and its
    // per-tab content. This tells us definitively whether our recipes are reaching the UI.
    public static class LiveUIProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Find any RecipeGridUI instance currently in the scene
                var gridType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.RecipeView.RecipeGridUI");
                bag["gridType_found"] = gridType != null;
                if (gridType == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                // RecipeGridUI is an MVC Element. Find it via World.All<T>()
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var allMethod = worldType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "All" && m.IsGenericMethod && m.GetParameters().Length == 0);
                bag["world_All_method_found"] = allMethod != null;

                System.Collections.IEnumerable allInstances = null;
                try
                {
                    allInstances = allMethod?.MakeGenericMethod(gridType).Invoke(null, null) as System.Collections.IEnumerable;
                }
                catch (Exception e) { bag["world_All_err"] = e.GetBaseException().Message; }

                int instCount = 0;
                object grid = null;
                foreach (var inst in allInstances ?? new object[0]) { if (grid == null) grid = inst; instCount++; }
                bag["RecipeGridUI_instances"] = instCount;

                // Also scan a bunch of crafting-related types to see what's currently registered
                var probeTypes = new[]
                {
                    "Awaken.TG.Main.Crafting.Crafting",
                    "Awaken.TG.Main.Crafting.HandCrafting.Handcrafting",
                    "Awaken.TG.Main.Crafting.AlchemyCrafting.Alchemy",
                    "Awaken.TG.Main.Crafting.HandCrafting.RecipeView.RecipeTabContents",
                    "Awaken.TG.Main.Crafting.HandCrafting.RecipeSlot",
                    "Awaken.TG.Main.Crafting.HandCrafting.RecipeView.RecipeTabs",
                    "Awaken.TG.Main.Crafting.Cooking.CraftingTabsUI",
                    "Awaken.TG.Main.Crafting.Cooking.CraftingTabs",
                };
                foreach (var tn in probeTypes)
                {
                    var pt = ResolveType(tn);
                    if (pt == null) { bag["scan_" + tn + "_status"] = "type_not_found"; continue; }
                    try
                    {
                        var insts = allMethod.MakeGenericMethod(pt).Invoke(null, null) as System.Collections.IEnumerable;
                        int n = 0; object first = null;
                        foreach (var x in insts ?? new object[0]) { if (first == null) first = x; n++; }
                        bag["scan_" + tn] = $"count={n} firstType={first?.GetType().FullName}";
                        if (grid == null && first != null) grid = first;     // fallback target
                    }
                    catch (Exception e) { bag["scan_" + tn + "_err"] = e.GetBaseException().Message; }
                }

                if (grid == null)
                {
                    // Last resort: scan all Models in World for ones of the given type
                    bag["note"] = "RecipeGridUI not found via World.All — forge UI not open or Element model not registered.";
                    return JsonConvert.SerializeObject(bag, Formatting.Indented);
                }
                bag["grid_type"] = grid.GetType().FullName;

                // Read AllRecipes
                var allRecipesProp = gridType.GetProperty("AllRecipes");
                var allRecipes = allRecipesProp?.GetValue(grid) as System.Collections.IEnumerable;
                int count = 0, ourCount = 0;
                var sampleNames = new List<string>();
                foreach (var r in allRecipes ?? new object[0])
                {
                    count++;
                    var n = (r as UnityEngine.Object)?.name ?? "?";
                    if (n.StartsWith("CraftAnything_")) ourCount++;
                    if (sampleNames.Count < 10) sampleNames.Add(n);
                }
                bag["AllRecipes_count"] = count;
                bag["AllRecipes_ours"] = ourCount;
                bag["AllRecipes_samples"] = sampleNames;

                // Read CurrentType + AllRecipesOfCurrentType
                var currentTypeProp = gridType.GetProperty("CurrentType");
                var currentType = currentTypeProp?.GetValue(grid);
                bag["CurrentType"] = currentType?.ToString();

                var allOfTypeProp = gridType.GetProperty("AllRecipesOfCurrentType");
                var allOfType = allOfTypeProp?.GetValue(grid) as System.Collections.IEnumerable;
                int typeCount = 0;
                foreach (var _ in allOfType ?? new object[0]) typeCount++;
                bag["AllRecipesOfCurrentType_count"] = typeCount;

                // RecipeCrafting (the model behind the UI)
                var rcProp = gridType.GetProperty("RecipeCrafting");
                var rc = rcProp?.GetValue(grid);
                bag["RecipeCrafting_type"] = rc?.GetType().FullName;
                if (rc != null)
                {
                    foreach (var p in rc.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        try
                        {
                            var v = p.GetValue(rc);
                            if (v is System.Collections.IEnumerable e && !(v is string))
                            {
                                int n = 0; foreach (var _ in e) n++;
                                bag["RecipeCrafting_" + p.Name] = $"{Pretty(p.PropertyType)} count={n}";
                            }
                            else if (v != null) bag["RecipeCrafting_" + p.Name] = $"{Pretty(p.PropertyType)} = {Truncate(v.ToString(), 80)}";
                        }
                        catch { }
                    }
                }

                // Find the source of AllRecipes — is it the same as our patched template.Recipes?
                // Trace back: find the field on grid that holds the list
                foreach (var f in gridType.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.Name.IndexOf("recipe", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try
                    {
                        var v = f.GetValue(grid);
                        if (v is System.Collections.IEnumerable e && !(v is string))
                        {
                            int n = 0; foreach (var _ in e) n++;
                            bag["grid_field_" + f.Name] = $"{Pretty(f.FieldType)} count={n}";
                        }
                        else { bag["grid_field_" + f.Name] = $"{Pretty(f.FieldType)} = {Truncate(v?.ToString(), 60)}"; }
                    }
                    catch { }
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static string Truncate(string s, int n) => s == null ? "null" : (s.Length > n ? s.Substring(0, n) + "…" : s);

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
