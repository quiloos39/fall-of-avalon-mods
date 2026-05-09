using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class TemplateRefProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var refType = ResolveType("Awaken.TG.Main.Templates.TemplateReference");
                if (refType == null) { bag["err"] = "TemplateReference not found"; return JsonConvert.SerializeObject(bag, Formatting.Indented); }

                bag["full"] = refType.FullName;
                bag["isStruct"] = refType.IsValueType;
                bag["isClass"] = refType.IsClass;
                bag["isAbstract"] = refType.IsAbstract;
                bag["base"] = refType.BaseType?.FullName;
                bag["interfaces"] = refType.GetInterfaces().Select(i => i.FullName).ToList();

                bag["constructors"] = refType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(c => $"({string.Join(",", c.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name))})")
                    .ToList();

                bag["fields"] = refType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();

                bag["properties"] = refType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();

                bag["methods"] = refType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                    .Take(30).ToList();

                // Try to construct one and see what happens.
                try
                {
                    var instance = Activator.CreateInstance(refType);
                    bag["activator_ok"] = instance != null;
                    bag["activator_typeName"] = instance?.GetType().FullName;
                }
                catch (Exception e) { bag["activator_err"] = e.GetBaseException().Message; }

                // Look at a real existing TemplateReference inside an existing recipe to see how it's populated.
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);

                var flagType = ResolveType("Awaken.TG.Main.Templates.TemplateTypeFlag");
                var flagRegular = Enum.Parse(flagType, "Regular");
                var hcType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.HandcraftingTemplate");
                var getAllOfType = providerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

                var hcTemplates = getAllOfType?.MakeGenericMethod(hcType).Invoke(provider, new object[] { flagRegular }) as System.Collections.IEnumerable;
                object firstRecipe = null;
                foreach (var ct in hcTemplates ?? new object[0])
                {
                    var recipes = ct.GetType().GetProperty("Recipes")?.GetValue(ct) as System.Collections.IEnumerable;
                    foreach (var r in recipes ?? new object[0])
                    {
                        firstRecipe = r;
                        break;
                    }
                    if (firstRecipe != null) break;
                }

                if (firstRecipe != null)
                {
                    bag["sample_recipe_type"] = firstRecipe.GetType().FullName;
                    var outcomeField = firstRecipe.GetType().GetField("outcome", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    var outcomeRef = outcomeField?.GetValue(firstRecipe);
                    bag["sample_outcomeRef_type"] = outcomeRef?.GetType().FullName;
                    bag["sample_outcomeRef_isNull"] = outcomeRef == null;

                    if (outcomeRef != null)
                    {
                        // Dump every field
                        foreach (var f in outcomeRef.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            try
                            {
                                var v = f.GetValue(outcomeRef);
                                bag["outcomeRef_field_" + f.Name] = $"{Pretty(f.FieldType)} = {Truncate(v?.ToString(), 80)}";
                            }
                            catch (Exception e) { bag["outcomeRef_field_" + f.Name + "_err"] = e.Message; }
                        }
                        // And properties
                        foreach (var p in outcomeRef.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            if (p.GetIndexParameters().Length > 0) continue;
                            try
                            {
                                var v = p.GetValue(outcomeRef);
                                bag["outcomeRef_prop_" + p.Name] = $"{Pretty(p.PropertyType)} = {Truncate(v?.ToString(), 80)}";
                            }
                            catch (Exception e) { bag["outcomeRef_prop_" + p.Name + "_err"] = e.Message; }
                        }
                    }
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
