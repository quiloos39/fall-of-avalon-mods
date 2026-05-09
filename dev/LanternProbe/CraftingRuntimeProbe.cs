using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Find where the runtime Handcrafting model gets its recipe list. The UI shows whatever
    // this model exposes — if it bypasses our patched property, we need to patch the model
    // instead of the template.
    public static class CraftingRuntimeProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Crafting<T> generic, Crafting non-generic, Handcrafting concrete
                Inspect(bag, "Crafting", "Awaken.TG.Main.Crafting.Crafting");
                Inspect(bag, "Handcrafting", "Awaken.TG.Main.Crafting.HandCrafting.Handcrafting");
                Inspect(bag, "Alchemy", "Awaken.TG.Main.Crafting.AlchemyCrafting.Alchemy");
                Inspect(bag, "ICrafting", "Awaken.TG.Main.Crafting.ICrafting");
                Inspect(bag, "IRecipeCrafting", "Awaken.TG.Main.Crafting.HandCrafting.IRecipeCrafting");
                Inspect(bag, "RecipeCrafting1", "Awaken.TG.Main.Crafting.HandCrafting.RecipeCrafting`1");

                // Find what the Crafting class does with the template
                var craftingType = ResolveType("Awaken.TG.Main.Crafting.Crafting");
                if (craftingType != null)
                {
                    bag["Crafting_inheritance"] = GetChain(craftingType);
                }
                var hc = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.Handcrafting");
                if (hc != null)
                {
                    bag["Handcrafting_inheritance"] = GetChain(hc);
                }

                // Look for AllRecipes anywhere
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && n.StartsWith("TG."); })
                    .SelectMany(SafeGetTypes).Where(t => t != null);

                var allRecipesUsages = new List<string>();
                foreach (var t in allTypes)
                {
                    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (p.Name == "AllRecipes" || p.Name == "Recipes")
                        {
                            allRecipesUsages.Add($"{t.FullName}.{p.Name} : {Pretty(p.PropertyType)}");
                        }
                    }
                }
                bag["AllRecipes_or_Recipes_props_in_TG"] = allRecipesUsages.Take(40).ToList();
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
            bag[key + "_baseType"] = t.BaseType?.FullName;
            bag[key + "_methods"] = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                .Take(15).ToList();
            bag[key + "_props"] = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(20).ToList();
            bag[key + "_fields"] = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(20).ToList();
        }

        private static List<string> GetChain(Type t)
        {
            var list = new List<string>();
            while (t != null) { list.Add(t.FullName); t = t.BaseType; }
            return list;
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
