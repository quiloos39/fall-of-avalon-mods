using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Dump World.ModelsByType — the central registry of loaded Models. Filter to types
    // related to NPCs / Locations / kills so we know exactly where dead enemies live.
    public static class WorldDump
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var modelsByTypeF = worldType?.GetField("ModelsByType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var modelsByType = modelsByTypeF?.GetValue(null);
                bag["modelsByType_type"] = modelsByType?.GetType().FullName;

                // HierarchicalDictionary<Type, IModel> probably has a way to enumerate
                var hdType = modelsByType?.GetType();
                bag["HD_methods"] = hdType?.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                    .Take(30).ToList();
                bag["HD_props"] = hdType?.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
                bag["HD_fields"] = hdType?.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();

                // Try to enumerate keys
                var keysProp = hdType?.GetProperty("Keys");
                var keys = keysProp?.GetValue(modelsByType) as System.Collections.IEnumerable;
                if (keys != null)
                {
                    var npcRelated = new List<string>();
                    var allKeys = new List<string>();
                    foreach (var k in keys)
                    {
                        var name = k?.ToString() ?? "";
                        allKeys.Add(name);
                        if (name.IndexOf("Npc", StringComparison.OrdinalIgnoreCase) >= 0
                         || name.IndexOf("Location", StringComparison.OrdinalIgnoreCase) >= 0
                         || name.IndexOf("Hero", StringComparison.OrdinalIgnoreCase) >= 0
                         || name.IndexOf("Death", StringComparison.OrdinalIgnoreCase) >= 0
                         || name.IndexOf("Alive", StringComparison.OrdinalIgnoreCase) >= 0)
                            npcRelated.Add(name);
                    }
                    bag["all_registered_types_count"] = allKeys.Count;
                    bag["npc_or_location_types"] = npcRelated.Take(40).ToList();
                }

                // World.AllInOrder() returns StructList<Model> — let's count by category
                var allInOrderM = worldType.GetMethod("AllInOrder", BindingFlags.Public | BindingFlags.Static);
                var allInOrder = allInOrderM?.Invoke(null, null);
                if (allInOrder != null)
                {
                    // It's a StructList<Model> — try ToString or iterate
                    var enumer = allInOrder as System.Collections.IEnumerable;
                    if (enumer == null)
                    {
                        // Try GetEnumerator method
                        var getEnum = allInOrder.GetType().GetMethod("GetEnumerator");
                        if (getEnum != null)
                        {
                            var e = getEnum.Invoke(allInOrder, null) as System.Collections.IEnumerator;
                            int n = 0;
                            var byType = new Dictionary<string, int>();
                            while (e != null && e.MoveNext() && n < 5000)
                            {
                                n++;
                                var t = e.Current?.GetType().Name;
                                if (t == null) continue;
                                if (!byType.ContainsKey(t)) byType[t] = 0;
                                byType[t]++;
                            }
                            bag["allInOrder_total"] = n;
                            bag["top_model_types"] = byType.OrderByDescending(kv => kv.Value).Take(30)
                                .Select(kv => $"{kv.Value}× {kv.Key}").ToList();
                        }
                    }
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
