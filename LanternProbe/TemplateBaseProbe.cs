using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class TemplateBaseProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var template = ResolveType("Awaken.TG.Main.Templates.Template");
                if (template != null)
                {
                    bag["Template_full"] = template.FullName;
                    bag["Template_baseType"] = template.BaseType?.FullName;
                    bag["Template_baseBaseType"] = template.BaseType?.BaseType?.FullName;
                    bag["Template_baseBaseBaseType"] = template.BaseType?.BaseType?.BaseType?.FullName;
                    bag["Template_isScriptableObject"] = typeof(UnityEngine.ScriptableObject).IsAssignableFrom(template);
                    bag["Template_isMonoBehaviour"] = typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(template);
                    bag["Template_isComponent"] = typeof(UnityEngine.Component).IsAssignableFrom(template);
                    bag["Template_isUnityObject"] = typeof(UnityEngine.Object).IsAssignableFrom(template);
                }

                var br = ResolveType("Awaken.TG.Main.Crafting.Recipes.BaseRecipe");
                if (br != null)
                {
                    bag["BaseRecipe_inheritanceChain"] = GetChain(br);
                    bag["BaseRecipe_isScriptableObject"] = typeof(UnityEngine.ScriptableObject).IsAssignableFrom(br);
                }

                // Find an existing HandcraftingRecipe instance to confirm
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);
                var hcType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.HandcraftingRecipe");
                if (provider != null && hcType != null)
                {
                    var getAll = providerType.GetMethods().FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition);
                    var allRecipes = getAll?.MakeGenericMethod(hcType).Invoke(provider, null) as System.Collections.IEnumerable;
                    var first = allRecipes?.Cast<object>().FirstOrDefault();
                    if (first != null)
                    {
                        bag["live_recipe_actual_type"] = first.GetType().FullName;
                        bag["live_recipe_isScriptableObject"] = first is UnityEngine.ScriptableObject;
                        bag["live_recipe_isUnityObject"] = first is UnityEngine.Object;
                        bag["live_recipe_inheritanceChain"] = GetChain(first.GetType());
                    }
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static List<string> GetChain(Type t)
        {
            var list = new List<string>();
            while (t != null) { list.Add(t.FullName); t = t.BaseType; }
            return list;
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
