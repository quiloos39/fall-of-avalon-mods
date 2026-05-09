using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class QuestDestDebug
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero == null) return "no hero";

                var qbase = ResolveType("Awaken.TG.Main.Stories.Quests.Quest");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var allM = worldType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "All" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var quests = (allM?.MakeGenericMethod(qbase).Invoke(null, null) as System.Collections.IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                bag["loadedQuestCount"] = quests.Count;

                // For every quest, dump tracking + objective info, including target resolution attempts.
                var dump = new List<object>();
                foreach (var q in quests)
                {
                    var trackedProp = qbase.GetProperty("IsTracked");
                    bool tracked = trackedProp != null && (bool)trackedProp.GetValue(q);
                    var displayName = qbase.GetProperty("DisplayName")?.GetValue(q) as string;
                    var stateProp = qbase.GetProperty("State");
                    var state = stateProp?.GetValue(q)?.ToString();

                    var qrow = new Dictionary<string, object>
                    {
                        ["name"] = displayName,
                        ["state"] = state,
                        ["isTracked"] = tracked,
                    };

                    if (tracked || dump.Count == 0)  // dump tracked, plus first non-tracked as a sample
                    {
                        var objsProp = qbase.GetProperty("ActiveObjectives") ?? qbase.GetProperty("Objectives");
                        var objs = (objsProp?.GetValue(q) as System.Collections.IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                        qrow["objectiveCount"] = objs.Count;
                        var objRows = new List<object>();
                        foreach (var obj in objs)
                        {
                            var orow = new Dictionary<string, object>
                            {
                                ["objType"] = obj.GetType().Name,
                                ["objName"] = obj.GetType().GetProperty("Name")?.GetValue(obj)?.ToString(),
                            };
                            // ObjectiveState
                            var stateP = obj.GetType().GetProperty("State");
                            orow["state"] = stateP?.GetValue(obj)?.ToString();
                            var anyMarker = obj.GetType().GetProperty("AnyMarkerVisible")?.GetValue(obj);
                            orow["anyMarkerVisible"] = anyMarker;
                            var markersData = obj.GetType().GetProperty("MarkersData")?.GetValue(obj) as Array;
                            orow["markersDataCount"] = markersData?.Length ?? 0;

                            var mainScene = obj.GetType().GetProperty("MainTargetScene")?.GetValue(obj);
                            var openWorldScene = obj.GetType().GetProperty("MainTargetOpenWorldScene")?.GetValue(obj);
                            orow["mainScene"] = mainScene?.ToString();
                            orow["mainOpenWorldScene"] = openWorldScene?.ToString();

                            // Try GetAllActiveTargets(hero, null)
                            var getAllM = obj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                .FirstOrDefault(m => m.Name == "GetAllActiveTargets" && m.GetParameters().Length == 2);
                            if (getAllM != null)
                            {
                                orow["GetAllActiveTargets_signature"] = $"({string.Join(",", getAllM.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})";

                                // Variant 1: (hero, null)
                                try
                                {
                                    var r1 = getAllM.Invoke(obj, new object[] { hero, null });
                                    var list = (r1 as System.Collections.IEnumerable)?.Cast<object>().ToList();
                                    orow["targets_heroNull_count"] = list?.Count ?? 0;
                                    if (list != null && list.Count > 0)
                                        orow["targets_heroNull_first"] = $"{list[0]?.GetType().Name} pos={TryGetCoords(list[0])}";
                                }
                                catch (Exception e) { orow["targets_heroNull_err"] = e.InnerException?.Message ?? e.Message; }

                                // Variant 2: (hero, mainScene)
                                if (mainScene != null)
                                {
                                    try
                                    {
                                        var r2 = getAllM.Invoke(obj, new object[] { hero, mainScene });
                                        var list = (r2 as System.Collections.IEnumerable)?.Cast<object>().ToList();
                                        orow["targets_mainScene_count"] = list?.Count ?? 0;
                                        if (list != null && list.Count > 0)
                                            orow["targets_mainScene_first"] = $"{list[0]?.GetType().Name} pos={TryGetCoords(list[0])}";
                                    }
                                    catch (Exception e) { orow["targets_mainScene_err"] = e.InnerException?.Message ?? e.Message; }
                                }

                                // Variant 3: (hero, openWorldScene)
                                if (openWorldScene != null)
                                {
                                    try
                                    {
                                        var r3 = getAllM.Invoke(obj, new object[] { hero, openWorldScene });
                                        var list = (r3 as System.Collections.IEnumerable)?.Cast<object>().ToList();
                                        orow["targets_openWorldScene_count"] = list?.Count ?? 0;
                                        if (list != null && list.Count > 0)
                                            orow["targets_openWorldScene_first"] = $"{list[0]?.GetType().Name} pos={TryGetCoords(list[0])}";
                                    }
                                    catch (Exception e) { orow["targets_openWorldScene_err"] = e.InnerException?.Message ?? e.Message; }
                                }
                            }

                            // Also try MarkersData entries for coords
                            if (markersData != null && markersData.Length > 0)
                            {
                                var mdInfo = new List<string>();
                                foreach (var md in markersData)
                                {
                                    if (md == null) continue;
                                    mdInfo.Add(md.GetType().Name);
                                    foreach (var p in md.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                                    {
                                        try { var v = p.GetValue(md); if (v != null) mdInfo.Add($"  {p.Name}={SafeStr(v)}"); } catch { }
                                    }
                                }
                                orow["markersData_dump"] = mdInfo;
                            }

                            objRows.Add(orow);
                        }
                        qrow["objectives"] = objRows;
                    }

                    dump.Add(qrow);
                }
                bag["quests"] = dump;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static string TryGetCoords(object obj)
        {
            if (obj == null) return "null";
            foreach (var pname in new[] { "Coords", "Position", "WorldPosition" })
            {
                try
                {
                    var p = obj.GetType().GetProperty(pname);
                    if (p == null) continue;
                    var v = p.GetValue(obj);
                    if (v is Vector3 v3) return $"({v3.x:F1},{v3.y:F1},{v3.z:F1})";
                }
                catch { }
            }
            return "(no coords prop)";
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }

        private static string SafeStr(object v)
        {
            try
            {
                if (v is Vector3 v3) return $"({v3.x:F1},{v3.y:F1},{v3.z:F1})";
                return v?.ToString();
            }
            catch { return "ERR"; }
        }
    }
}
