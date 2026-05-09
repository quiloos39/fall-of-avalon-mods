using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class AStarVerify
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // ── 1. Find AstarPath singleton (try various names + scene FindObjectsOfType) ──
                Type astarType = null;
                foreach (var ns in new[] { "Pathfinding.AstarPath", "Pathfinding.AstarData" })
                {
                    var t = ResolveType(ns);
                    if (t != null) bag["resolved_" + ns] = t.FullName;
                    if (ns == "Pathfinding.AstarPath" && t != null) astarType = t;
                }

                // Even if the type didn't show up in the keyword filter earlier, try harder.
                if (astarType == null)
                {
                    astarType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                        .FirstOrDefault(t => t != null && t.FullName == "Pathfinding.AstarPath");
                }
                bag["AstarPath_typeFound"] = astarType?.FullName;

                object astarInstance = null;
                if (astarType != null)
                {
                    var sceneAstars = UnityEngine.Object.FindObjectsOfType(astarType) as Component[];
                    bag["astarPathInScene"] = sceneAstars?.Length ?? 0;
                    if (sceneAstars != null && sceneAstars.Length > 0) astarInstance = sceneAstars[0];

                    var activeProp = astarType.GetField("active", BindingFlags.Public | BindingFlags.Static)
                                  ?? astarType.GetField("Active", BindingFlags.Public | BindingFlags.Static);
                    if (activeProp != null) astarInstance = astarInstance ?? activeProp.GetValue(null);

                    bag["astarPath_active_field"] = activeProp != null;
                    bag["astarPath_resolved"] = astarInstance != null;

                    if (astarInstance != null)
                    {
                        // Inspect graphs.
                        var graphsField = astarType.GetField("graphs", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        var graphsViaField = graphsField?.GetValue(astarInstance) as Array;
                        bag["graphs_count"] = graphsViaField?.Length ?? 0;
                        if (graphsViaField != null)
                        {
                            bag["graphs"] = graphsViaField.Cast<object>().Where(g => g != null).Select(g => new
                            {
                                type = g.GetType().FullName,
                                fields = g.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                                    .Take(8).Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList(),
                            }).ToList();
                        }
                    }
                }

                // ── 2. Sample existing Seekers in scene and grab one's GraphMask + DefaultGraphMask
                var seekerType = ResolveType("Pathfinding.Seeker");
                if (seekerType != null)
                {
                    var seekers = UnityEngine.Object.FindObjectsOfType(seekerType) as Component[];
                    bag["liveSeekerCount"] = seekers?.Length ?? 0;
                    if (seekers != null && seekers.Length > 0)
                    {
                        var s = seekers[0];
                        bag["sampleSeeker"] = new
                        {
                            owner = s.gameObject.name,
                            // Notable Seeker fields: graphMask, traversableTags, ...
                            fields = s.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                                .Where(f => !f.Name.Contains("modifier") && !f.Name.Contains("On"))
                                .Take(15).Select(f => $"{Pretty(f.FieldType)} {f.Name}={SafeStr(f.GetValue(s))}").ToList(),
                        };
                    }
                }

                // ── 3. Try our own Seeker on a fresh GameObject. Doesn't matter if we found
                //      AstarPath — Seeker.StartPath uses the static AstarPath.active internally.
                if (seekerType != null)
                {
                    var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                    var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    Vector3 playerPos = hero != null ? (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero) : Vector3.zero;
                    Vector3 dest = playerPos + new Vector3(0, 0, 50f); // 50m forward
                    bag["test_from"] = SafeStr(playerPos);
                    bag["test_to"] = SafeStr(dest);

                    GameObject testGo = new GameObject("StationIndicator_TestSeeker");
                    UnityEngine.Object.DontDestroyOnLoad(testGo);
                    var seeker = testGo.AddComponent(seekerType);

                    // Synchronous: call StartPath, then BlockUntilCalculated on the returned Path.
                    var startPathM = seekerType.GetMethods()
                        .FirstOrDefault(m => m.Name == "StartPath" && m.GetParameters().Length == 2
                                          && m.GetParameters()[0].ParameterType == typeof(Vector3)
                                          && m.GetParameters()[1].ParameterType == typeof(Vector3));
                    if (startPathM == null) { bag["test_startPathMethod"] = "NOT FOUND"; UnityEngine.Object.Destroy(testGo); }
                    else
                    {
                        var path = startPathM.Invoke(seeker, new object[] { playerPos, dest });
                        bag["pathReturnedType"] = path?.GetType().FullName;

                        // Block until calculated.
                        var blockM = path?.GetType().GetMethod("BlockUntilCalculated", BindingFlags.Public | BindingFlags.Instance);
                        blockM?.Invoke(path, null);

                        // Check error/state and read vectorPath
                        var errorProp = path?.GetType().GetProperty("error", BindingFlags.Public | BindingFlags.Instance);
                        var hasError = errorProp != null ? (bool)errorProp.GetValue(path) : true;
                        var errorLogProp = path?.GetType().GetProperty("errorLog");
                        var errorLog = errorLogProp?.GetValue(path) as string;
                        bag["test_pathError"] = hasError;
                        bag["test_errorLog"] = errorLog;

                        var vectorPathField = path?.GetType().GetField("vectorPath", BindingFlags.Public | BindingFlags.Instance);
                        var vectorPath = vectorPathField?.GetValue(path) as System.Collections.IList;
                        bag["test_vectorPathCount"] = vectorPath?.Count ?? 0;
                        if (vectorPath != null && vectorPath.Count > 0)
                        {
                            bag["test_firstCorners"] = vectorPath.Cast<object>().Take(10).Select(o => SafeStr(o)).ToList();
                        }

                        UnityEngine.Object.Destroy(testGo);
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
        private static string SafeStr(object v)
        {
            try
            {
                if (v is Vector3 v3) return $"({v3.x:F1},{v3.y:F1},{v3.z:F1})";
                return v?.ToString() ?? "null";
            }
            catch { return "ERR"; }
        }
    }
}
