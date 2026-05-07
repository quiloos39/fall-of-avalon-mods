using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Detailed inspection of IRecipe / IRuntimeRecipe / BaseRecipe / HandcraftingRecipe
    // so we know what shape our generated runtime recipes need to take.
    public static class RecipeInterfaceProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                Inspect(bag, "IRecipe", "Awaken.TG.Main.Crafting.Recipes.IRecipe");
                Inspect(bag, "IRuntimeRecipe", "Awaken.TG.Main.Crafting.Recipes.IRuntimeRecipe");
                Inspect(bag, "BaseRecipe", "Awaken.TG.Main.Crafting.Recipes.BaseRecipe");
                Inspect(bag, "HandcraftingRecipe", "Awaken.TG.Main.Crafting.HandCrafting.HandcraftingRecipe");
                Inspect(bag, "AlchemyRecipe", "Awaken.TG.Main.Crafting.AlchemyCrafting.AlchemyRecipe");
                Inspect(bag, "CookingRecipe", "Awaken.TG.Main.Crafting.Cooking.CookingRecipe");
                Inspect(bag, "Ingredient", "Awaken.TG.Main.Crafting.Recipes.Ingredient");
                Inspect(bag, "ItemTemplate", "Awaken.TG.Main.Heroes.Items.ItemTemplate");
                Inspect(bag, "CraftingTemplate", "Awaken.TG.Main.Crafting.CraftingTemplate");
                Inspect(bag, "HandcraftingTemplate", "Awaken.TG.Main.Crafting.HandCrafting.HandcraftingTemplate");

                // Sample HeroRecipes to see what's in it
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var heroRecipesType = ResolveType("Awaken.TG.Main.Heroes.HeroRecipes");
                if (heroRecipesType != null && hero != null)
                {
                    var elementMethod = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    var heroRecipes = elementMethod?.MakeGenericMethod(heroRecipesType).Invoke(hero, null);
                    bag["heroRecipes_resolved"] = heroRecipes != null;
                    if (heroRecipes != null)
                    {
                        bag["HeroRecipes_props"] = heroRecipesType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(20).ToList();
                        bag["HeroRecipes_fields"] = heroRecipesType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(20).ToList();
                        bag["HeroRecipes_methods"] = heroRecipesType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                            .Take(20).ToList();
                    }
                }

                // Find an existing HandcraftingRecipe instance to learn its concrete shape
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);
                var handcraftingTemplateType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.HandcraftingTemplate");
                if (provider != null && handcraftingTemplateType != null)
                {
                    var getAllOfType = providerType.GetMethods()
                        .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                    try
                    {
                        var allHCT = getAllOfType?.MakeGenericMethod(handcraftingTemplateType).Invoke(provider, null) as System.Collections.IEnumerable;
                        var first = allHCT?.Cast<object>().FirstOrDefault();
                        if (first != null)
                        {
                            bag["sample_handcrafting_template_type"] = first.GetType().FullName;
                            // Get its Recipes
                            var recipesProp = first.GetType().GetProperty("Recipes");
                            var recipes = recipesProp?.GetValue(first) as System.Collections.IEnumerable;
                            var firstRecipe = recipes?.Cast<object>().FirstOrDefault();
                            if (firstRecipe != null)
                            {
                                bag["sample_recipe_actualType"] = firstRecipe.GetType().FullName;
                                bag["sample_recipe_props"] = firstRecipe.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                    .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(30).ToList();
                                bag["sample_recipe_fields"] = firstRecipe.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                    .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(30).ToList();
                                // Try to dump the values of key fields
                                foreach (var pname in new[] { "Outcomes", "Ingredients", "RequiredStat", "Outcome", "Result", "Output" })
                                {
                                    var p = firstRecipe.GetType().GetProperty(pname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (p != null)
                                    {
                                        try
                                        {
                                            var v = p.GetValue(firstRecipe);
                                            if (v is System.Collections.IEnumerable enumer && !(v is string))
                                            {
                                                int n = 0; foreach (var _ in enumer) n++;
                                                bag["sample_recipe_" + pname + "_count"] = n;
                                            }
                                            else { bag["sample_recipe_" + pname] = v?.ToString(); }
                                        }
                                        catch { }
                                    }
                                }
                            }
                            // Also dump Recipes count
                            int recipeCount = 0;
                            foreach (var _ in recipes ?? new object[0]) recipeCount++;
                            bag["sample_template_recipe_count"] = recipeCount;
                        }
                    }
                    catch (Exception e) { bag["recipe_enum_err"] = e.GetBaseException().Message; }
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static void Inspect(Dictionary<string, object> bag, string key, string fullName)
        {
            var t = ResolveType(fullName);
            if (t == null) { bag[key + "_status"] = "TYPE_NOT_FOUND"; return; }
            bag[key + "_full"] = t.FullName;
            bag[key + "_isInterface"] = t.IsInterface;
            bag[key + "_isAbstract"] = t.IsAbstract;
            bag[key + "_baseType"] = t.BaseType?.FullName;
            bag[key + "_interfaces"] = t.GetInterfaces().Select(i => i.FullName).Take(10).ToList();
            bag[key + "_methods"] = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                .Take(25).ToList();
            bag[key + "_props"] = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(25).ToList();
            bag[key + "_fields"] = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(25).ToList();
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
