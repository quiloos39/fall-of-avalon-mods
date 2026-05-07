using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // We confirmed CraftAnything generates 229 recipes successfully. The user still doesn't
    // see them in the forge UI. So either:
    //   (a) Generated recipes are invalid (Outcome doesn't resolve to a real ItemTemplate)
    //   (b) The UI bypasses CraftingTemplate.Recipes and reads the raw `recipes` field
    //   (c) Some other recipe-validity check in the UI rejects them
    //
    // This probe finds the live "CraftAnything_Recipes" GameObject, samples its children
    // (each one a runtime recipe) and inspects their state — Outcome resolution, ingredient
    // count, tag, and any other state that might be needed for UI display.
    public static class GeneratedRecipeAudit
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Find the host GameObject we created
                var host = GameObject.Find("CraftAnything_Recipes");
                bag["host_found"] = host != null;
                if (host == null)
                {
                    // Maybe HideFlags hides it from Find — search via FindObjectsOfType
                    var allGOs = UnityEngine.Resources.FindObjectsOfTypeAll<GameObject>();
                    host = allGOs.FirstOrDefault(g => g.name == "CraftAnything_Recipes");
                    bag["host_found_via_Resources"] = host != null;
                }
                if (host == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                bag["host_childCount"] = host.transform.childCount;

                // Inspect first 3 children
                var samples = new List<Dictionary<string, object>>();
                for (int i = 0; i < Math.Min(3, host.transform.childCount); i++)
                {
                    var child = host.transform.GetChild(i);
                    var sample = new Dictionary<string, object>();
                    sample["name"] = child.name;
                    sample["activeInHierarchy"] = child.gameObject.activeInHierarchy;
                    sample["activeSelf"] = child.gameObject.activeSelf;
                    var components = child.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName).ToList();
                    sample["components"] = components;

                    var recipe = child.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name.EndsWith("Recipe"));
                    if (recipe != null)
                    {
                        sample["recipe_type"] = recipe.GetType().FullName;
                        sample["recipe_name"] = recipe.name;
                        // BaseRecipe.Outcome (resolves the TemplateReference)
                        var baseRecipeType = ResolveType("Awaken.TG.Main.Crafting.Recipes.BaseRecipe");
                        var outcomeProp = baseRecipeType?.GetProperty("Outcome");
                        try
                        {
                            var outcome = outcomeProp?.GetValue(recipe);
                            sample["outcome_resolves"] = outcome != null;
                            sample["outcome_type"] = outcome?.GetType().FullName;
                            sample["outcome_name"] = outcome?.GetType().GetProperty("ItemName")?.GetValue(outcome)?.ToString();
                            sample["outcome_GUID"] = outcome?.GetType().GetProperty("GUID")?.GetValue(outcome)?.ToString();
                        }
                        catch (Exception e) { sample["outcome_err"] = e.GetBaseException().Message; }

                        // Other props that the UI might use
                        foreach (var pname in new[] { "Quantity", "IsHidden", "CanHaveItemLevel", "ItemCraftingDifficulty",
                                                     "Ingredients", "StatRequirement", "ProficiencyStat", "BonusLevelStat", "GUID" })
                        {
                            var p = baseRecipeType?.GetProperty(pname);
                            if (p == null) continue;
                            try
                            {
                                var v = p.GetValue(recipe);
                                if (v is System.Collections.IEnumerable e2 && !(v is string))
                                {
                                    int n = 0; foreach (var _ in e2) n++;
                                    sample[$"prop_{pname}_count"] = n;
                                }
                                else { sample[$"prop_{pname}"] = v?.ToString(); }
                            }
                            catch (Exception e) { sample[$"prop_{pname}_err"] = e.GetBaseException().Message; }
                        }

                        // Try DisplayString — UI uses this for tooltip/title
                        try
                        {
                            var ds = baseRecipeType.GetMethod("DisplayString")?.Invoke(recipe, null);
                            sample["DisplayString"] = ds?.ToString();
                        }
                        catch (Exception e) { sample["DisplayString_err"] = e.GetBaseException().Message; }

                        // Try OutcomeName
                        try
                        {
                            var on = baseRecipeType.GetMethod("OutcomeName")?.Invoke(recipe, null);
                            sample["OutcomeName"] = on?.ToString();
                        }
                        catch (Exception e) { sample["OutcomeName_err"] = e.GetBaseException().Message; }
                    }
                    samples.Add(sample);
                }
                bag["samples"] = samples;

                // Now check whether Recipes property actually returns our injected recipes when called
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

                var hcTemplates = getAllOfType?.MakeGenericMethod(hcType).Invoke(provider, new object[] { flagRegular }) as System.Collections.IEnumerable;
                var firstHC = hcTemplates?.Cast<object>().FirstOrDefault();
                bag["first_handcrafting_template_type"] = firstHC?.GetType().FullName;

                if (firstHC != null)
                {
                    // Call Recipes property — should fire our postfix and return existing + injected
                    var recipesProp = firstHC.GetType().GetProperty("Recipes");
                    var recipes = recipesProp?.GetValue(firstHC) as System.Collections.IEnumerable;
                    int total = 0, ourTagged = 0;
                    var sampleRecipeNames = new List<string>();
                    foreach (var r in recipes ?? new object[0])
                    {
                        total++;
                        var rname = (r as UnityEngine.Object)?.name ?? "?";
                        if (rname.StartsWith("CraftAnything_")) ourTagged++;
                        if (sampleRecipeNames.Count < 8) sampleRecipeNames.Add(rname);
                    }
                    bag["template_recipes_total"] = total;
                    bag["template_recipes_ours"] = ourTagged;
                    bag["template_recipe_samples"] = sampleRecipeNames;

                    // Also check raw `recipes` field — maybe UI reads this and bypasses our postfix
                    var rawField = firstHC.GetType().GetField("recipes", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    var rawRefs = rawField?.GetValue(firstHC) as System.Collections.IEnumerable;
                    int rawCount = 0;
                    foreach (var _ in rawRefs ?? new object[0]) rawCount++;
                    bag["template_raw_recipes_count"] = rawCount;
                }
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
