using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // Calls GetAllActiveTargets the way the GAME does it: with SceneService's
    // ActiveSceneRef as the currentScene. If this returns a portal/target,
    // the fix to DestinationProvider is just "use ActiveSceneRef".
    public static class QuestActiveSceneProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero == null) { bag["err"] = "no hero"; return Json(bag); }

                // SceneService — World.Services.Get<SceneService>()
                var sceneServiceType = ResolveType("Awaken.TG.MVC.Domains.SceneService");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var servicesProp = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static);
                var services = servicesProp?.GetValue(null);
                bag["services"] = services?.GetType().Name ?? "null";

                object sceneService = null;
                if (services != null && sceneServiceType != null)
                {
                    var getMethod = services.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod);
                    sceneService = getMethod?.MakeGenericMethod(sceneServiceType).Invoke(services, null);
                }
                bag["sceneService"] = sceneService?.GetType().Name ?? "null";
                if (sceneService == null) return Json(bag);

                var activeSceneProp = sceneServiceType.GetProperty("ActiveSceneRef")
                                  ?? sceneServiceType.GetProperty("ActiveScene");
                var activeSceneRef = activeSceneProp?.GetValue(sceneService);
                bag["ActiveSceneRef_prop"] = activeSceneProp?.Name ?? "missing";
                bag["ActiveSceneRef_value"] = SceneRefName(activeSceneRef);

                // Walk objectives, call GetAllActiveTargets with activeSceneRef
                var qtType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTracker");
                var tracker = ElementOf(hero, qtType);
                var quest = qtType?.GetProperty("ActiveQuest")?.GetValue(tracker);
                bag["quest"] = quest?.GetType().GetProperty("DisplayName")?.GetValue(quest);
                if (quest == null) return Json(bag);

                var objs = (quest.GetType().GetProperty("ActiveObjectives")?.GetValue(quest) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                if (objs == null) return Json(bag);

                var perObjective = new List<object>();
                foreach (var obj in objs)
                {
                    var o = new Dictionary<string, object>();
                    o["name"] = obj.GetType().GetProperty("Name")?.GetValue(obj);
                    o["MainTargetScene"] = SceneRefName(obj.GetType().GetProperty("MainTargetScene")?.GetValue(obj));

                    var getAllTargets = obj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "GetAllActiveTargets" && m.GetParameters().Length == 2);
                    if (getAllTargets == null) { o["err"] = "no GetAllActiveTargets"; perObjective.Add(o); continue; }

                    try
                    {
                        var result = getAllTargets.Invoke(obj, new object[] { hero, activeSceneRef });
                        var list = (result as System.Collections.IEnumerable)?.Cast<object>().ToList();
                        o["targets[ActiveSceneRef]_count"] = list?.Count ?? 0;
                        if (list != null && list.Count > 0)
                        {
                            o["targets_first"] = $"type={list[0].GetType().Name},coords={CoordsOf(list[0])}";
                            o["targets_all"] = list.Select(t => $"{t.GetType().Name}@{CoordsOf(t)}").Take(5).ToArray();
                        }
                    }
                    catch (Exception e) { o["targets[ActiveSceneRef]_err"] = (e.InnerException ?? e).Message; }

                    // Walk the objective's child markers — Elements<PointMapMarker>() etc.
                    // The compass uses these to render arrows; each holds an IGrounded.
                    var elementsM = obj.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Elements" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    var mapMarkerType = ResolveType("Awaken.TG.Main.Maps.Markers.MapMarker");
                    var pointMapMarkerType = ResolveType("Awaken.TG.Main.Heroes.CharacterSheet.Map.Markers.PointMapMarker");
                    o["MapMarker_type"] = mapMarkerType?.FullName ?? "null";
                    o["PointMapMarker_type"] = pointMapMarkerType?.FullName ?? "null";

                    foreach (var (label, t) in new[] { ("MapMarker", mapMarkerType), ("PointMapMarker", pointMapMarkerType) })
                    {
                        if (elementsM == null || t == null) continue;
                        try
                        {
                            var els = elementsM.MakeGenericMethod(t).Invoke(obj, null) as System.Collections.IEnumerable;
                            var list2 = els?.Cast<object>().ToList();
                            o[$"Elements<{label}>_count"] = list2?.Count ?? 0;
                            if (list2 != null && list2.Count > 0)
                            {
                                o[$"Elements<{label}>_props"] = list2[0].GetType().GetProperties()
                                    .Where(p => p.Name == "Grounded" || p.Name == "Coords" || p.Name.Contains("Target") || p.Name.Contains("Position"))
                                    .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                                    .ToArray();
                                var first = list2[0];
                                var grounded = first.GetType().GetProperty("Grounded")?.GetValue(first);
                                o[$"Elements<{label}>_grounded_type"] = grounded?.GetType().Name ?? "null";
                                o[$"Elements<{label}>_grounded_coords"] = CoordsOf(grounded);
                            }
                        }
                        catch (Exception e) { o[$"Elements<{label}>_err"] = e.GetBaseException().Message; }
                    }

                    // MarkersData[i].LocationReference — see if we can pull coords from it
                    var markersData = obj.GetType().GetProperty("MarkersData")?.GetValue(obj) as System.Collections.IEnumerable;
                    var mdList = markersData?.Cast<object>().ToList();
                    o["MarkersData_count"] = mdList?.Count ?? 0;
                    if (mdList != null && mdList.Count > 0)
                    {
                        var md = mdList[0];
                        o["MarkerData_props"] = md.GetType().GetProperties()
                            .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                            .ToArray();
                        var locRef = md.GetType().GetProperty("LocationReference")?.GetValue(md);
                        o["LocationReference"] = locRef?.GetType().Name ?? "null";
                        if (locRef != null)
                        {
                            o["LocRef_props"] = locRef.GetType().GetProperties()
                                .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                                .ToArray();
                            o["LocRef_methods"] = locRef.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_"))
                                .Where(m => !typeof(object).GetMethods().Any(om => om.Name == m.Name))
                                .Select(m => $"{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})")
                                .Take(20)
                                .ToArray();

                            // What target type / actors / tags?
                            o["TargetsTags"] = locRef.GetType().GetProperty("TargetsTags")?.GetValue(locRef);
                            o["TargetsTemplates"] = locRef.GetType().GetProperty("TargetsTemplates")?.GetValue(locRef);
                            o["TargetsActors"] = locRef.GetType().GetProperty("TargetsActors")?.GetValue(locRef);
                            o["TargetsSpecs"] = locRef.GetType().GetProperty("TargetsSpecs")?.GetValue(locRef);

                            // Dump ALL fields on the location reference (not just properties)
                            o["LocRef_fields"] = locRef.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                .Select(f => $"{f.Name}:{f.FieldType.Name}={SafeFieldStr(f, locRef)}")
                                .ToArray();

                            var actors = (locRef.GetType().GetProperty("Actors")?.GetValue(locRef) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                            if (actors != null && actors.Count > 0)
                            {
                                var first = actors[0];
                                o["Actor_type"] = first.GetType().FullName;
                                o["Actor_props"] = first.GetType().GetProperties()
                                    .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                                    .ToArray();
                                // try to read identifying fields
                                foreach (var pname in new[] { "Name", "Guid", "GUID", "ActorRef", "DefinedActor", "ID", "DebugName" })
                                {
                                    var p = first.GetType().GetProperty(pname);
                                    if (p == null) continue;
                                    try { o[$"Actor_{pname}"] = p.GetValue(first)?.ToString(); } catch { }
                                }
                            }

                            // Try MatchingLocations(null)
                            var match = locRef.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                .FirstOrDefault(m => m.Name == "MatchingLocations" && m.GetParameters().Length == 1);
                            if (match != null)
                            {
                                try
                                {
                                    var matched = match.Invoke(locRef, new object[] { null }) as System.Collections.IEnumerable;
                                    var ml = matched?.Cast<object>().ToList();
                                    o["LocRef_MatchingLocations(null)_count"] = ml?.Count ?? 0;
                                    if (ml != null && ml.Count > 0)
                                    {
                                        o["LocRef_first_match"] = $"{ml[0].GetType().Name}@{CoordsOf(ml[0])}";
                                    }
                                }
                                catch (Exception e) { o["LocRef_match_err"] = e.GetBaseException().Message; }
                            }
                        }
                    }

                    perObjective.Add(o);
                }
                bag["objectives"] = perObjective;
            }
            catch (Exception e)
            {
                bag["err"] = e.GetBaseException().Message;
                bag["stack"] = e.StackTrace;
            }
            return Json(bag);
        }

        private static string SafeFieldStr(FieldInfo f, object obj)
        {
            try
            {
                var v = f.GetValue(obj);
                if (v == null) return "null";
                if (v is System.Collections.IEnumerable e && !(v is string))
                {
                    var items = e.Cast<object>().Select(x => x?.ToString() ?? "null").Take(8);
                    return "[" + string.Join(",", items) + "]";
                }
                return v.ToString();
            }
            catch (Exception ex) { return $"err:{ex.GetBaseException().Message}"; }
        }

        private static string SceneRefName(object sceneRef)
        {
            if (sceneRef == null) return "null";
            try
            {
                var name = sceneRef.GetType().GetProperty("Name")?.GetValue(sceneRef)
                        ?? sceneRef.GetType().GetField("name")?.GetValue(sceneRef)
                        ?? sceneRef.ToString();
                return name?.ToString() ?? "?";
            }
            catch { return "?"; }
        }

        private static string CoordsOf(object obj)
        {
            try
            {
                foreach (var pname in new[] { "Coords", "Position", "WorldPosition" })
                {
                    var p = obj.GetType().GetProperty(pname);
                    if (p == null) continue;
                    var v = p.GetValue(obj);
                    if (v is Vector3 v3) return $"({v3.x:F1},{v3.y:F1},{v3.z:F1})";
                }
            }
            catch { }
            return "no-coords";
        }

        private static object ElementOf(object hero, Type elemType)
        {
            var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
            if (elementGeneric == null) return null;
            try { return elementGeneric.MakeGenericMethod(elemType).Invoke(hero, null); }
            catch { return null; }
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = asm.GetType(fullName, throwOnError: false); if (t != null) return t; }
                catch { }
            }
            return null;
        }

        private static string Json(object o) => JsonConvert.SerializeObject(o, Formatting.Indented);
    }
}
