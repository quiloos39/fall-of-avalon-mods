using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Pathfinding;

namespace LanternProbe
{
    public static class PathHealthCheck
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // 1) Current player position + scene info
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                Vector3 playerPos = hero != null ? (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero) : Vector3.zero;
                bag["playerPos"] = $"({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})";

                // What scene is the player in? (Unity scene)
                var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                bag["activeUnityScene"] = activeScene.name;
                bag["loadedSceneCount"] = UnityEngine.SceneManagement.SceneManager.sceneCount;
                var loadedNames = new List<string>();
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                    loadedNames.Add(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name);
                bag["loadedSceneNames"] = loadedNames;

                // 2) Destination as resolved by the mod's provider
                var providerType = ResolveType("QuestPath.DestinationProvider");
                var resolveM = providerType?.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
                bag["DestinationProvider.Resolve"] = resolveM?.Invoke(null, null)?.ToString();

                // Active quest scene info
                var trackerType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTracker");
                var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var tracker = elementGeneric.MakeGenericMethod(trackerType).Invoke(hero, null);
                var activeQuest = trackerType.GetProperty("ActiveQuest").GetValue(tracker);
                if (activeQuest != null)
                {
                    bag["activeQuestName"] = activeQuest.GetType().GetProperty("DisplayName")?.GetValue(activeQuest)?.ToString();
                    var objs = (activeQuest.GetType().GetProperty("ActiveObjectives")?.GetValue(activeQuest) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                    if (objs != null && objs.Count > 0)
                    {
                        var obj0 = objs[0];
                        bag["objectiveName"] = obj0.GetType().GetProperty("Name")?.GetValue(obj0)?.ToString();
                        bag["objective_MainTargetScene"] = obj0.GetType().GetProperty("MainTargetScene")?.GetValue(obj0)?.ToString();
                        bag["objective_MainTargetOpenWorldScene"] = obj0.GetType().GetProperty("MainTargetOpenWorldScene")?.GetValue(obj0)?.ToString();

                        // Try all variations to see which (if any) returns targets
                        var getAllM = obj0.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(m => m.Name == "GetAllActiveTargets" && m.GetParameters().Length == 2);
                        if (getAllM != null)
                        {
                            // (hero, null)
                            try
                            {
                                var r = getAllM.Invoke(obj0, new object[] { hero, null }) as System.Collections.IEnumerable;
                                var l = r?.Cast<object>().ToList();
                                bag["targets_heroNull_count"] = l?.Count ?? 0;
                                if (l != null && l.Count > 0)
                                    bag["targets_heroNull_sample"] = l.Take(3).Select(t => $"{t.GetType().Name} pos={SafeCoords(t)}").ToList();
                            }
                            catch (Exception e) { bag["targets_heroNull_err"] = e.InnerException?.Message ?? e.Message; }

                            // With MainTargetScene
                            var ms = obj0.GetType().GetProperty("MainTargetScene")?.GetValue(obj0);
                            if (ms != null)
                            {
                                try
                                {
                                    var r = getAllM.Invoke(obj0, new object[] { hero, ms }) as System.Collections.IEnumerable;
                                    var l = r?.Cast<object>().ToList();
                                    bag["targets_mainScene_count"] = l?.Count ?? 0;
                                    if (l != null && l.Count > 0)
                                        bag["targets_mainScene_sample"] = l.Take(3).Select(t => $"{t.GetType().Name} pos={SafeCoords(t)}").ToList();
                                }
                                catch (Exception e) { bag["targets_mainScene_err"] = e.InnerException?.Message ?? e.Message; }
                            }
                        }

                        // MarkersData inspection — does this objective even have any?
                        var markersData = obj0.GetType().GetProperty("MarkersData")?.GetValue(obj0) as Array;
                        bag["markersDataCount"] = markersData?.Length ?? 0;
                        if (markersData != null && markersData.Length > 0)
                        {
                            var md0 = markersData.GetValue(0);
                            var mdInfo = new List<string>();
                            foreach (var f in md0.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                            {
                                try
                                {
                                    var v = f.GetValue(md0);
                                    if (v != null) mdInfo.Add($"{f.Name}={v}");
                                }
                                catch { }
                            }
                            bag["markersData[0]_dump"] = mdInfo;
                        }
                        bag["anyMarkerVisible"] = obj0.GetType().GetProperty("AnyMarkerVisible")?.GetValue(obj0);
                    }
                }

                // 3) Run a real pathfind ourselves and inspect the result.
                // Resolve the destination via the provider.
                var resolveResult = resolveM?.Invoke(null, null);
                Vector3? dest = null;
                try
                {
                    var coordsField = resolveResult?.GetType().GetField("Item1");
                    var v = coordsField?.GetValue(resolveResult);
                    if (v is Vector3 vv) dest = vv;
                }
                catch { }
                bag["destResolved"] = dest?.ToString();

                if (dest.HasValue)
                {
                    // Use a fresh seeker
                    var seekerGo = new GameObject("PathHealthCheck_Seeker");
                    UnityEngine.Object.DontDestroyOnLoad(seekerGo);
                    var seeker = seekerGo.AddComponent<Seeker>();
                    seeker.graphMask = -1;

                    var path = seeker.StartPath(playerPos, dest.Value);
                    path.BlockUntilCalculated();

                    bag["path_error"] = path.error;
                    bag["path_errorLog"] = path.errorLog;

                    // ABPath state via reflection (the field exists but its accessibility may
                    // be private/internal in this AstarPathfindingProject build).
                    var ab = path as ABPath;
                    if (ab != null)
                    {
                        var stateField = typeof(ABPath).GetField("completeState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        bag["abPath_completeState"] = stateField?.GetValue(ab)?.ToString();
                        bag["abPath_endPoint"] = $"({ab.endPoint.x:F1},{ab.endPoint.y:F1},{ab.endPoint.z:F1})";
                        bag["abPath_originalEndPoint"] = $"({ab.originalEndPoint.x:F1},{ab.originalEndPoint.y:F1},{ab.originalEndPoint.z:F1})";
                        bag["abPath_distFromEndToTarget"] = Vector3.Distance(ab.endPoint, dest.Value);
                    }

                    bag["vectorPathCount"] = path.vectorPath?.Count ?? 0;
                    if (path.vectorPath != null && path.vectorPath.Count > 0)
                    {
                        var first = path.vectorPath[0];
                        var last = path.vectorPath[path.vectorPath.Count - 1];
                        bag["vectorPath_first"] = $"({first.x:F1},{first.y:F1},{first.z:F1})";
                        bag["vectorPath_last"]  = $"({last.x:F1},{last.y:F1},{last.z:F1})";
                        bag["vectorPath_lastToTargetDistance"] = Vector3.Distance(last, dest.Value);

                        // Total path length
                        float len = 0f;
                        for (int i = 1; i < path.vectorPath.Count; i++) len += Vector3.Distance(path.vectorPath[i-1], path.vectorPath[i]);
                        bag["vectorPath_totalLength"] = len;
                    }

                    UnityEngine.Object.Destroy(seekerGo);
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static string SafeCoords(object obj)
        {
            if (obj == null) return "null";
            foreach (var pname in new[] { "Coords", "Position" })
            {
                var p = obj.GetType().GetProperty(pname);
                if (p == null) continue;
                try { var v = p.GetValue(obj); if (v is Vector3 vv) return $"({vv.x:F1},{vv.y:F1},{vv.z:F1})"; }
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
    }
}
