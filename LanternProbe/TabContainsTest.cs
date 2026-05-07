using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // For each RecipeTabType, call its Contains(IRecipe) on a vanilla recipe AND on one of
    // our generated recipes. See whether any tab claims our recipe — if not, we know why
    // the UI is silently hiding it.
    public static class TabContainsTest
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var hostGOs = UnityEngine.Resources.FindObjectsOfTypeAll<GameObject>();
                var host = hostGOs.FirstOrDefault(g => g.name == "CraftAnything_Recipes");
                bag["host_found"] = host != null;
                if (host == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                // Sample one of our recipes
                object ourRecipe = null;
                for (int i = 0; i < host.transform.childCount; i++)
                {
                    var child = host.transform.GetChild(i);
                    var r = child.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name.EndsWith("Recipe"));
                    if (r != null) { ourRecipe = r; break; }
                }
                bag["ourRecipe_found"] = ourRecipe != null;
                if (ourRecipe != null)
                {
                    bag["ourRecipe_name"] = (ourRecipe as UnityEngine.Object)?.name;
                    bag["ourRecipe_type"] = ourRecipe.GetType().FullName;

                    var outcome = ourRecipe.GetType().GetProperty("Outcome")?.GetValue(ourRecipe);
                    bag["ourRecipe_outcome"] = outcome?.GetType().GetProperty("ItemName")?.GetValue(outcome)?.ToString();
                    var tagsObj = outcome?.GetType().GetProperty("Tags")?.GetValue(outcome) as System.Collections.IEnumerable;
                    var tagsList = new List<string>();
                    if (tagsObj != null) foreach (var t in tagsObj) tagsList.Add(t?.ToString());
                    bag["ourRecipe_outcome_tags"] = tagsList;
                }

                // Get a vanilla recipe for control
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

                object vanillaRecipe = null;
                foreach (var t in (getAllOfType?.MakeGenericMethod(hcType).Invoke(provider, new object[] { flagRegular }) as System.Collections.IEnumerable) ?? new object[0])
                {
                    var recipes = t.GetType().GetProperty("Recipes")?.GetValue(t) as System.Collections.IEnumerable;
                    foreach (var r in recipes ?? new object[0])
                    {
                        var rname = (r as UnityEngine.Object)?.name ?? "";
                        if (!rname.StartsWith("CraftAnything_")) { vanillaRecipe = r; break; }
                    }
                    if (vanillaRecipe != null) break;
                }
                bag["vanilla_name"] = (vanillaRecipe as UnityEngine.Object)?.name;

                // Iterate every static RecipeTabType field and call Contains(ours) and Contains(vanilla)
                var tabType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.RecipeView.RecipeTabType");
                var containsM = tabType.GetMethod("Contains", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                bag["containsM_found"] = containsM != null;

                var results = new Dictionary<string, string>();
                foreach (var f in tabType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (f.FieldType != tabType) continue;     // skip array-typed fields
                    var tabValue = f.GetValue(null);
                    string vCheck = "?", oCheck = "?";
                    try { vCheck = containsM.Invoke(tabValue, new object[] { vanillaRecipe }).ToString(); }
                    catch (Exception e) { vCheck = "ERR " + e.GetBaseException().Message; }
                    try { oCheck = containsM.Invoke(tabValue, new object[] { ourRecipe }).ToString(); }
                    catch (Exception e) { oCheck = "ERR " + e.GetBaseException().Message; }
                    results[f.Name] = $"vanilla={vCheck}  ours={oCheck}";
                }
                bag["tab_contains_results"] = results;
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
