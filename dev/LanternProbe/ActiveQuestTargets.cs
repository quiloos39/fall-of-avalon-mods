using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class ActiveQuestTargets
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var trackerType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTracker");
                var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var tracker = elementGeneric.MakeGenericMethod(trackerType).Invoke(hero, null);
                var activeQuest = trackerType.GetProperty("ActiveQuest").GetValue(tracker);
                bag["activeQuest_type"] = activeQuest?.GetType().FullName;
                bag["activeQuest_displayName"] = activeQuest?.GetType().GetProperty("DisplayName")?.GetValue(activeQuest)?.ToString();
                bag["activeQuest_state"] = activeQuest?.GetType().GetProperty("State")?.GetValue(activeQuest)?.ToString();

                if (activeQuest == null) return JsonConvert.SerializeObject(bag);

                // Walk ActiveObjectives → for each, try every angle to get coords.
                var objsProp = activeQuest.GetType().GetProperty("ActiveObjectives") ?? activeQuest.GetType().GetProperty("Objectives");
                var objs = (objsProp.GetValue(activeQuest) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                bag["activeObjectiveCount"] = objs?.Count ?? 0;

                var objRows = new List<object>();
                foreach (var obj in objs ?? new List<object>())
                {
                    var orow = new Dictionary<string, object>
                    {
                        ["objType"] = obj.GetType().Name,
                        ["objName"] = obj.GetType().GetProperty("Name")?.GetValue(obj)?.ToString(),
                        ["state"] = obj.GetType().GetProperty("State")?.GetValue(obj)?.ToString(),
                        ["anyMarkerVisible"] = obj.GetType().GetProperty("AnyMarkerVisible")?.GetValue(obj),
                    };

                    // Variant attempts of GetAllActiveTargets
                    var getAllM = obj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "GetAllActiveTargets" && m.GetParameters().Length == 2);
                    if (getAllM != null)
                    {
                        // Try (hero, null) first
                        try
                        {
                            var r = getAllM.Invoke(obj, new object[] { hero, null });
                            var list = (r as System.Collections.IEnumerable)?.Cast<object>().ToList();
                            orow["targets_heroNull_count"] = list?.Count ?? 0;
                            if (list != null && list.Count > 0)
                                orow["targets_heroNull_sample"] = list.Take(3).Select(t => $"{t.GetType().Name} pos={TryCoords(t)}").ToList();
                        }
                        catch (Exception e) { orow["targets_heroNull_err"] = e.InnerException?.Message ?? e.Message; }

                        // Try (null, null)
                        try
                        {
                            var r = getAllM.Invoke(obj, new object[] { null, null });
                            var list = (r as System.Collections.IEnumerable)?.Cast<object>().ToList();
                            orow["targets_nullNull_count"] = list?.Count ?? 0;
                            if (list != null && list.Count > 0)
                                orow["targets_nullNull_sample"] = list.Take(3).Select(t => $"{t.GetType().Name} pos={TryCoords(t)}").ToList();
                        }
                        catch (Exception e) { orow["targets_nullNull_err"] = e.InnerException?.Message ?? e.Message; }

                        // Try with mainScene
                        var mainScene = obj.GetType().GetProperty("MainTargetScene")?.GetValue(obj);
                        var openWorldScene = obj.GetType().GetProperty("MainTargetOpenWorldScene")?.GetValue(obj);
                        orow["mainScene_str"] = mainScene?.ToString();
                        orow["openWorldScene_str"] = openWorldScene?.ToString();
                        if (mainScene != null)
                        {
                            try
                            {
                                var r = getAllM.Invoke(obj, new object[] { hero, mainScene });
                                var list = (r as System.Collections.IEnumerable)?.Cast<object>().ToList();
                                orow["targets_mainScene_count"] = list?.Count ?? 0;
                                if (list != null && list.Count > 0)
                                    orow["targets_mainScene_sample"] = list.Take(3).Select(t => $"{t.GetType().Name} pos={TryCoords(t)}").ToList();
                            }
                            catch (Exception e) { orow["targets_mainScene_err"] = e.InnerException?.Message ?? e.Message; }
                        }
                    }

                    // MarkersData walk — pull coords from each MarkerData entry
                    var markersData = obj.GetType().GetProperty("MarkersData")?.GetValue(obj) as Array;
                    orow["markersDataCount"] = markersData?.Length ?? 0;
                    if (markersData != null)
                    {
                        var mdInfo = new List<string>();
                        foreach (var md in markersData)
                        {
                            if (md == null) continue;
                            // MarkerData has lots of fields — look for any IGrounded / Coords / Position
                            var line = new System.Text.StringBuilder(md.GetType().Name + " {");
                            foreach (var f in md.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                            {
                                try
                                {
                                    var v = f.GetValue(md);
                                    if (v == null) continue;
                                    line.Append($" {f.Name}={SafeStr(v)}");
                                }
                                catch { }
                            }
                            line.Append(" }");
                            mdInfo.Add(line.ToString());
                        }
                        orow["markersData_dump"] = mdInfo;
                    }

                    objRows.Add(orow);
                }
                bag["objectives"] = objRows;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static string TryCoords(object obj)
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
            return "(no coords)";
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
