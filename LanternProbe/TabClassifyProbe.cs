using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // The forge UI splits recipes across tabs (Weapons, Armor, etc.). Find what mechanism
    // does the splitting, so we can ensure our recipes have the right metadata.
    public static class TabClassifyProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var recipeTabType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.RecipeView.RecipeTabType");
                if (recipeTabType != null)
                {
                    bag["RecipeTabType_full"] = recipeTabType.FullName;
                    bag["RecipeTabType_isClass"] = recipeTabType.IsClass;
                    bag["RecipeTabType_baseType"] = recipeTabType.BaseType?.FullName;

                    var staticFields = recipeTabType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();
                    bag["RecipeTabType_static_fields"] = staticFields;

                    var methods = recipeTabType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Select(m => $"{(m.IsStatic ? "static " : "")}{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => $"{Pretty(p.ParameterType)} {p.Name}"))})")
                        .Take(30).ToList();
                    bag["RecipeTabType_methods"] = methods;
                }

                var recipeTabs = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.RecipeView.RecipeTabs");
                if (recipeTabs != null)
                {
                    bag["RecipeTabs_methods"] = recipeTabs.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Select(m => $"{(m.IsStatic ? "static " : "")}{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .Take(30).ToList();
                    bag["RecipeTabs_fields"] = recipeTabs.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(30).ToList();
                }

                var recipeTabContents = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.RecipeView.RecipeTabContents");
                if (recipeTabContents != null)
                {
                    bag["RecipeTabContents_methods"] = recipeTabContents.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Select(m => $"{(m.IsStatic ? "static " : "")}{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .Take(30).ToList();
                }

                var recipeGridUi = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.RecipeView.RecipeGridUI");
                if (recipeGridUi != null)
                {
                    bag["RecipeGridUI_methods"] = recipeGridUi.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Select(m => $"{(m.IsStatic ? "static " : "")}{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .Take(30).ToList();
                }

                // RichEnumReference — find a constructor and how to make a "no requirement" one
                var richEnumRef = ResolveType("Awaken.TG.Main.Utility.RichEnums.RichEnumReference");
                if (richEnumRef != null)
                {
                    bag["RichEnumReference_constructors"] = richEnumRef.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(c => $"({string.Join(",", c.GetParameters().Select(p => $"{Pretty(p.ParameterType)} {p.Name}"))})")
                        .ToList();
                    bag["RichEnumReference_fields"] = richEnumRef.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();
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

        private static string Pretty(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.Name;
            return t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(g => g.Name)) + ">";
        }
    }
}
