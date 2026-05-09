using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;

namespace LanternProbe
{
    public static class PathfindFeasibility
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();

            // Resolve hero once for everyone.
            object hero = null;
            Vector3 playerPos = Vector3.zero;
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero != null) playerPos = (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero);
                bag["heroFound"] = hero != null;
                bag["playerPos"] = SafeStr(playerPos);
            }
            catch (Exception e) { bag["heroErr"] = e.ToString(); }

            // ── 1. NavMesh availability ──────────────────────────────────────────────
            try
            {
                bool sampled = NavMesh.SamplePosition(playerPos, out var hit, 5f, NavMesh.AllAreas);
                bag["navMesh_SamplePosition_neighborhood"] = new
                {
                    found = sampled,
                    hitPos = sampled ? SafeStr(hit.position) : null,
                    distanceFromPlayer = sampled ? Vector3.Distance(playerPos, hit.position) : -1f,
                    areaMask = sampled ? hit.mask : 0,
                };

                var dest = playerPos + new Vector3(30f, 0f, 10f);
                bool destSampled = NavMesh.SamplePosition(dest, out var destHit, 30f, NavMesh.AllAreas);
                Vector3 destSnapped = destSampled ? destHit.position : dest;

                var path = new NavMeshPath();
                bool calc = NavMesh.CalculatePath(playerPos, destSnapped, NavMesh.AllAreas, path);
                bag["navMesh_CalculatePath_test"] = new
                {
                    requestedDest = SafeStr(dest),
                    snappedDest = SafeStr(destSnapped),
                    calcReturned = calc,
                    status = path.status.ToString(),
                    cornerCount = path.corners?.Length ?? 0,
                    firstCorners = path.corners?.Take(5).Select(c => SafeStr((object)c)).ToList(),
                };

                bag["navMesh_agentTypeCount"] = NavMesh.GetSettingsCount();
                var agents = new List<string>();
                for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
                {
                    var s = NavMesh.GetSettingsByIndex(i);
                    agents.Add($"id={s.agentTypeID} radius={s.agentRadius} height={s.agentHeight} step={s.agentClimb}");
                }
                bag["navMesh_agents"] = agents;
            }
            catch (Exception e) { bag["navMeshErr"] = e.ToString(); }

            // ── 2. Quest / Objective API ──────────────────────────────────────────────
            try
            {
                var qbase = ResolveType("Awaken.TG.Main.Stories.Quests.Quest");
                var objBase = ResolveType("Awaken.TG.Main.Stories.Quests.Objectives.Objective");
                bag["Quest_typeFound"] = qbase != null;
                bag["Objective_typeFound"] = objBase != null;

                if (qbase != null)
                {
                    bag["Quest_props"] = qbase.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Take(40).Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
                }
                if (objBase != null)
                {
                    bag["Objective_props"] = objBase.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Take(40).Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
                    bag["Objective_methods"] = objBase.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Take(40).Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})").ToList();
                    bag["Objective_fields"] = objBase.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Take(40).Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();
                }

                // Walk all loaded Quests via World.All<Quest>()
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var allMethod = worldType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "All" && m.IsGenericMethod && m.GetParameters().Length == 0);
                if (allMethod != null && qbase != null)
                {
                    var allQuestsRet = allMethod.MakeGenericMethod(qbase).Invoke(null, null);
                    var quests = (allQuestsRet as System.Collections.IEnumerable)?.Cast<object>().ToList();
                    bag["loadedQuestCount"] = quests?.Count ?? 0;

                    if (quests != null && quests.Count > 0)
                    {
                        var snap = new List<object>();
                        foreach (var q in quests.Take(15))
                        {
                            var row = new Dictionary<string, object> { ["qType"] = q.GetType().Name };
                            foreach (var pname in new[] { "Name", "DisplayName", "ID", "State", "Tracked", "IsTracked", "IsActive" })
                            {
                                var p = q.GetType().GetProperty(pname, BindingFlags.Public | BindingFlags.Instance);
                                if (p != null) try { row[pname] = SafeStr(p.GetValue(q)); } catch { }
                            }
                            var objsProp = q.GetType().GetProperty("Objectives") ?? q.GetType().GetProperty("ActiveObjectives");
                            if (objsProp != null)
                            {
                                var objs = (objsProp.GetValue(q) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                                row["objectiveCount"] = objs?.Count ?? 0;
                                if (objs != null && objs.Count > 0)
                                {
                                    var o = objs[0];
                                    row["firstObjective_type"] = o?.GetType().FullName;
                                    row["firstObjective_props"] = o?.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                        .Take(20).Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
                                }
                            }
                            snap.Add(row);
                        }
                        bag["questSamples"] = snap;
                    }
                }

                // Tracked quest / focus quest hooks on Hero
                if (hero != null)
                {
                    foreach (var pname in new[] { "TrackedQuest", "ActiveQuest", "FocusedQuest" })
                    {
                        var p = hero.GetType().GetProperty(pname);
                        if (p != null) try { bag["hero." + pname] = SafeStr(p.GetValue(hero)); } catch { }
                    }
                }

                // Static service singletons
                foreach (var typeName in new[] {
                    "Awaken.TG.Main.Stories.Quests.QuestTracker",
                    "Awaken.TG.Main.Stories.Quests.QuestUtils",
                    "Awaken.TG.Main.Stories.Quests.QuestUtility",
                    "Awaken.TG.Main.Stories.Quests.QuestService",
                })
                {
                    var t = ResolveType(typeName);
                    if (t != null)
                        bag["found_" + t.Name] = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                            .Take(15)
                            .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                            .ToList();
                }

                // Quest3DMarker live instances (these are the world-side markers — exact target coords)
                var q3d = ResolveType("Awaken.TG.Main.Stories.Quests.Quest3DMarker");
                if (q3d != null)
                {
                    bag["Quest3DMarker_props"] = q3d.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Take(20).Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();

                    var allMethod2 = worldType?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "All" && m.IsGenericMethod);
                    if (allMethod2 != null)
                    {
                        var live = (allMethod2.MakeGenericMethod(q3d).Invoke(null, null) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                        bag["liveQuest3DMarkerCount"] = live?.Count ?? 0;
                        if (live != null && live.Count > 0)
                        {
                            bag["sample_Quest3DMarker"] = live.Take(3).Select(m =>
                            {
                                var info = new Dictionary<string, object> { ["type"] = m.GetType().Name };
                                foreach (var p in m.GetType().GetProperties().Take(15))
                                {
                                    try { info[p.Name] = SafeStr(p.GetValue(m)); } catch { info[p.Name] = "ERR"; }
                                }
                                return info;
                            }).ToList();
                        }
                    }
                }
            }
            catch (Exception e) { bag["questErr"] = e.ToString(); }

            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(fullName, throwOnError: false);
                if (t != null) return t;
            }
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
