using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class BabelDataInspect
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Find the live BabelManager via static lookup or scene
                var bmType = ResolveType("Awaken.Babel.BabelManager");
                if (bmType == null) return "no BabelManager type";

                // BabelManager is a class instance — look for it via static field on a service
                // or by reflection on a GameServices/World.Services holder.

                var worldType = ResolveType("Awaken.TG.MVC.World");
                var servicesProp = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static);
                var services = servicesProp?.GetValue(null);
                if (services != null)
                {
                    // Try to get<BabelManager>() from services
                    var getMethod = services.GetType().GetMethods()
                        .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    var bm = getMethod?.MakeGenericMethod(bmType).Invoke(services, null);
                    bag["babelManager_resolved"] = bm != null;
                    if (bm != null)
                    {
                        // Find the provider field (PreloadedBabelProvider or StreamingBabelProvider)
                        var providerFields = bmType.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                            .Where(f => f.FieldType.Name.EndsWith("BabelProvider") || f.FieldType.Name == "IBabelProvider")
                            .ToList();
                        bag["bm_providerFieldNames"] = providerFields.Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();

                        var provider = providerFields.FirstOrDefault()?.GetValue(bm);
                        bag["provider_actualType"] = provider?.GetType().FullName;
                        if (provider != null)
                        {
                            var localeDataF = provider.GetType().GetField("_localeData", BindingFlags.NonPublic | BindingFlags.Instance);
                            var localeData = localeDataF?.GetValue(provider);
                            bag["localeData_type"] = localeData?.GetType().FullName;
                            if (localeData != null)
                            {
                                bag["localeData_fields"] = localeData.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                    .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(15).ToList();
                                bag["localeData_props"] = localeData.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                    .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(15).ToList();

                                // Walk fields looking for Dictionary<*, string> or similar
                                foreach (var f in localeData.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                {
                                    try
                                    {
                                        var v = f.GetValue(localeData);
                                        if (v is System.Collections.IDictionary dict)
                                        {
                                            bag["localeData." + f.Name + "_type"] = v.GetType().FullName;
                                            bag["localeData." + f.Name + "_count"] = dict.Count;
                                            // Sample first few entries
                                            int n = 0;
                                            var samples = new List<string>();
                                            foreach (System.Collections.DictionaryEntry e in dict)
                                            {
                                                samples.Add($"{e.Key} → {Truncate(e.Value?.ToString(), 80)}");
                                                if (++n >= 5) break;
                                            }
                                            bag["localeData." + f.Name + "_sample"] = samples;
                                        }
                                        else if (v is System.Collections.ICollection coll)
                                        {
                                            bag["localeData." + f.Name + "_type"] = v.GetType().FullName;
                                            bag["localeData." + f.Name + "_count"] = coll.Count;
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }

                        // Try Translate() as a sanity check on a known LocString
                        // Pick the active quest's name
                        var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                        var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        if (hero != null)
                        {
                            var trackerType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTracker");
                            var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                            var tracker = elementGeneric?.MakeGenericMethod(trackerType).Invoke(hero, null);
                            var activeQuest = trackerType.GetProperty("ActiveQuest")?.GetValue(tracker);
                            if (activeQuest != null)
                            {
                                bag["sampleQuest_displayName"] = activeQuest.GetType().GetProperty("DisplayName")?.GetValue(activeQuest);
                                bag["sampleQuest_description"] = activeQuest.GetType().GetProperty("Description")?.GetValue(activeQuest);
                            }
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
