using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class QuestStorageHunt2
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // 1) Look for live VQuestTracker / VQuestTrackerObjective in scene — these are the
                //    MonoBehaviours that render the on-screen quest tracker UI.
                foreach (var typeName in new[] {
                    "Awaken.TG.Main.Stories.Quests.VQuestTracker",
                    "Awaken.TG.Main.Stories.Quests.VQuestTrackerObjective",
                    "Awaken.TG.Main.Stories.Quests.VQuest3DMarker",
                })
                {
                    var t = ResolveType(typeName);
                    if (t == null) continue;
                    var live = UnityEngine.Object.FindObjectsOfType(t) as Component[];
                    bag["live_" + t.Name] = live?.Length ?? 0;

                    if (live != null && live.Length > 0)
                    {
                        var sample = live[0];
                        var props = new List<string>();
                        foreach (var f in sample.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            try { props.Add($"{f.Name}={SafeStr(f.GetValue(sample))}"); } catch { }
                        }
                        foreach (var p in sample.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            try { props.Add($"{p.Name}={SafeStr(p.GetValue(sample))}"); } catch { }
                        }
                        bag["sample_" + t.Name] = props.Take(30).ToList();
                    }
                }

                // 2) Look at the World class deeply.
                var worldType = ResolveType("Awaken.TG.MVC.World");
                if (worldType != null)
                {
                    bag["World_staticMembers"] = worldType.GetMembers(BindingFlags.Public | BindingFlags.Static)
                        .Take(40).Select(m => $"{m.MemberType} {m.Name}").ToList();
                    bag["World_staticMethods"] = worldType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_"))
                        .Take(20).Select(m => $"{m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})").ToList();
                }

                // 3) Look for Domain types — MVC framework probably has multiple Domains.
                var domainType = ResolveType("Awaken.TG.MVC.Domains.Domain");
                if (domainType != null)
                {
                    bag["Domain_staticMembers"] = domainType.GetMembers(BindingFlags.Public | BindingFlags.Static)
                        .Take(40).Select(m => $"{m.MemberType} {m.Name}").ToList();
                }

                // 4) Try QuestUtils with no-arg lookups
                var quType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestUtils");
                if (quType != null)
                {
                    bag["QuestUtils_allMethods"] = quType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Take(30).Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name))})").ToList();
                }

                // 5) Brute force: enumerate every Element on Hero by walking its ModelElements internals
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero != null)
                {
                    // ModelElements field on Model — has internal collections
                    var mt = ResolveType("Awaken.TG.MVC.Model");
                    var meField = mt?.GetField("_modelElements", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (meField == null)
                    {
                        // Try property
                        var meProp = mt?.GetProperty("ModelElements", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        bag["ModelElements_prop_kind"] = meProp != null ? "property" : "(neither)";
                    }
                    else bag["ModelElements_field_kind"] = "field";

                    // Try a Hero.AllElements() variant.
                    var allElementsM = mt?.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == "AllElements" || m.Name == "GetElements");
                    if (allElementsM != null)
                    {
                        try
                        {
                            var els = (allElementsM.Invoke(hero, null) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                            bag["heroAllElements_count"] = els?.Count ?? 0;
                            if (els != null)
                            {
                                bag["heroAllElements_types"] = els.Select(e => e.GetType().Name).Distinct().Take(50).ToList();
                                bag["heroAllElements_questAny"] = els.Where(e => e.GetType().Name.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0)
                                    .Select(e => e.GetType().FullName).ToList();
                            }
                        }
                        catch (Exception e) { bag["heroAllElements_err"] = e.Message; }
                    }
                }

                // 6) World.All called for ALL element types in TG to find anything quest-shaped.
                // Cap result.
                var allM = worldType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "All" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var modelType = ResolveType("Awaken.TG.MVC.Model");
                if (allM != null && modelType != null)
                {
                    var modelTypes = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                        .Where(t => t != null && !t.IsAbstract && !t.IsInterface && !t.IsGenericTypeDefinition
                                 && modelType.IsAssignableFrom(t)
                                 && (t.FullName.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0
                                  || t.FullName.IndexOf("tracker", StringComparison.OrdinalIgnoreCase) >= 0
                                  || t.FullName.IndexOf("objective", StringComparison.OrdinalIgnoreCase) >= 0))
                        .ToList();

                    var nonZero = new Dictionary<string, int>();
                    foreach (var t in modelTypes.Take(80))
                    {
                        try
                        {
                            var r = allM.MakeGenericMethod(t).Invoke(null, null);
                            var c = (r as System.Collections.IEnumerable)?.Cast<object>().Count() ?? 0;
                            if (c > 0) nonZero[t.FullName] = c;
                        }
                        catch { }
                    }
                    bag["worldAll_questModels"] = nonZero;
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

        private static string SafeStr(object v)
        {
            try
            {
                if (v == null) return "null";
                if (v is Vector3 v3) return $"({v3.x:F1},{v3.y:F1},{v3.z:F1})";
                if (v is UnityEngine.Object uo) return $"{uo.name}({uo.GetType().Name})";
                return v.ToString();
            }
            catch { return "ERR"; }
        }
    }
}
