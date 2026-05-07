using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class BabelInstanceHunt
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var bmType = ResolveType("Awaken.Babel.BabelManager");
                var bmInstance = (object)null;

                // 1) Scan static fields/properties across ALL TG/Awaken types for a BabelManager
                var candidates = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && (n.StartsWith("TG.") || n.StartsWith("Awaken.")); })
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .Where(t => t != null);

                foreach (var t in candidates)
                {
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (f.FieldType != bmType) continue;
                        try
                        {
                            var v = f.GetValue(null);
                            if (v != null)
                            {
                                bag["found_static_field"] = $"{t.FullName}.{f.Name}";
                                bmInstance = v;
                                goto Found;
                            }
                        }
                        catch { }
                    }
                    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (p.PropertyType != bmType) continue;
                        try
                        {
                            var v = p.GetValue(null);
                            if (v != null)
                            {
                                bag["found_static_prop"] = $"{t.FullName}.{p.Name}";
                                bmInstance = v;
                                goto Found;
                            }
                        }
                        catch { }
                    }
                }

                // 2) Try via World.Services without the generic constraint that failed before
                if (bmInstance == null)
                {
                    var worldType = ResolveType("Awaken.TG.MVC.World");
                    var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (services != null)
                    {
                        // Look for a non-generic Get(Type) method
                        var getByType = services.GetType().GetMethods()
                            .FirstOrDefault(m => m.Name == "Get" && !m.IsGenericMethod && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type));
                        if (getByType != null)
                        {
                            try
                            {
                                bmInstance = getByType.Invoke(services, new object[] { bmType });
                                if (bmInstance != null) bag["found_via_services_getbytype"] = true;
                            }
                            catch (Exception e) { bag["services_getbytype_err"] = e.InnerException?.Message ?? e.Message; }
                        }

                        // Inspect Services API
                        bag["Services_methods"] = services.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            .Take(20).Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})").ToList();
                    }
                }

                Found:
                bag["bmInstance_found"] = bmInstance != null;
                if (bmInstance == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                // Now inspect provider + locale data
                var providerFields = bmType.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                foreach (var f in providerFields.Where(f => f.FieldType.Name.Contains("Babel") || f.Name.IndexOf("provider", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    try
                    {
                        var v = f.GetValue(bmInstance);
                        bag["bm_field." + f.Name + "_type"] = v?.GetType().FullName;
                    }
                    catch { }
                }

                // Find provider via interface match
                var iBabelProvider = ResolveType("Awaken.Babel.IBabelProvider");
                var providerField = providerFields.FirstOrDefault(f => iBabelProvider != null && iBabelProvider.IsAssignableFrom(f.FieldType));
                bag["providerField"] = providerField?.Name;

                var provider = providerField?.GetValue(bmInstance);
                if (provider == null) { bag["provider_null"] = true; return JsonConvert.SerializeObject(bag, Formatting.Indented); }
                bag["provider_type"] = provider.GetType().FullName;

                var localeDataF = provider.GetType().GetField("_localeData", BindingFlags.NonPublic | BindingFlags.Instance);
                var localeData = localeDataF?.GetValue(provider);
                bag["localeData_type"] = localeData?.GetType().FullName;
                if (localeData == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                // Walk localeData fields
                foreach (var f in localeData.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    try
                    {
                        var v = f.GetValue(localeData);
                        if (v == null) continue;
                        if (v is System.Collections.IDictionary dict)
                        {
                            bag["localeData." + f.Name] = $"Dictionary count={dict.Count}";
                            // Sample
                            var samples = new List<string>();
                            int n = 0;
                            foreach (System.Collections.DictionaryEntry e in dict)
                            {
                                samples.Add($"{e.Key} → {Truncate(e.Value?.ToString(), 100)}");
                                if (++n >= 5) break;
                            }
                            bag["localeData." + f.Name + "_sample"] = samples;
                        }
                        else if (v is System.Collections.ICollection coll)
                        {
                            bag["localeData." + f.Name] = $"Collection count={coll.Count}";
                        }
                        else
                        {
                            bag["localeData." + f.Name] = $"{Pretty(v.GetType())} = {Truncate(v.ToString(), 80)}";
                        }
                    }
                    catch (Exception e) { bag["localeData." + f.Name + "_err"] = e.Message; }
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
