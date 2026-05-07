using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Compare a vanilla forge recipe vs one of our generated ones field-by-field. The UI
    // reads ALL recipes via the property (which fires our postfix), but something inside
    // the UI is rejecting our recipes. Find the missing field.
    public static class VanillaRecipeCompare
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
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
                object firstTemplate = null;
                foreach (var t in hcTemplates ?? new object[0]) { firstTemplate = t; break; }
                if (firstTemplate == null) { bag["err"] = "no HC template"; return JsonConvert.SerializeObject(bag, Formatting.Indented); }

                var recipes = firstTemplate.GetType().GetProperty("Recipes")?.GetValue(firstTemplate) as System.Collections.IEnumerable;
                object vanillaRecipe = null;
                object ourRecipe = null;
                foreach (var r in recipes ?? new object[0])
                {
                    var rname = (r as UnityEngine.Object)?.name ?? "";
                    if (vanillaRecipe == null && !rname.StartsWith("CraftAnything_")) vanillaRecipe = r;
                    if (ourRecipe == null && rname.StartsWith("CraftAnything_")) ourRecipe = r;
                    if (vanillaRecipe != null && ourRecipe != null) break;
                }

                bag["vanilla_name"] = (vanillaRecipe as UnityEngine.Object)?.name;
                bag["our_name"] = (ourRecipe as UnityEngine.Object)?.name;

                var baseRecipeType = ResolveType("Awaken.TG.Main.Crafting.Recipes.BaseRecipe");

                // Compare every field on BaseRecipe
                var compare = new Dictionary<string, object>();
                foreach (var f in baseRecipeType.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
                {
                    object vV = null, oV = null;
                    try { vV = f.GetValue(vanillaRecipe); } catch (Exception e) { vV = "ERR " + e.Message; }
                    try { oV = f.GetValue(ourRecipe); } catch (Exception e) { oV = "ERR " + e.Message; }
                    var vS = Describe(vV);
                    var oS = Describe(oV);
                    compare[f.Name] = $"vanilla={vS}    ours={oS}";
                }
                bag["field_compare"] = compare;

                // Also dump StatRequirement RichEnumReference internals from a vanilla recipe
                var statReqF = baseRecipeType.GetField("statRequirement", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                var vanillaStatReq = statReqF?.GetValue(vanillaRecipe);
                if (vanillaStatReq != null)
                {
                    bag["vanilla_statReq_type"] = vanillaStatReq.GetType().FullName;
                    foreach (var f in vanillaStatReq.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
                    {
                        try { bag["vanilla_statReq_field_" + f.Name] = Describe(f.GetValue(vanillaStatReq)); }
                        catch (Exception e) { bag["vanilla_statReq_field_" + f.Name + "_err"] = e.Message; }
                    }
                }

                // Look at the Handcrafting (runtime) class to understand how it uses recipes
                var handcrafting = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.Handcrafting");
                if (handcrafting != null)
                {
                    bag["Handcrafting_props"] = handcrafting.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(20).ToList();
                    bag["Handcrafting_methods"] = handcrafting.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .Take(25).ToList();
                    bag["Handcrafting_fields"] = handcrafting.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(20).ToList();
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static string Describe(object v)
        {
            if (v == null) return "null";
            if (v is string s) return $"\"{Truncate(s, 80)}\"";
            if (v is System.Collections.IEnumerable e && !(v is string))
            {
                int n = 0; foreach (var _ in e) n++;
                return $"<{v.GetType().Name} count={n}>";
            }
            try { return Truncate(v.ToString(), 100); } catch { return "?"; }
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
