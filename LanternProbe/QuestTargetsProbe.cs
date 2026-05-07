using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // For the current tracked quest, calls GetAllActiveTargets directly with each
    // possible scene argument and reports what comes back. Also tries
    // ActiveObjectivesWithMarkers as an alternate source.
    public static class QuestTargetsProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero == null) { bag["err"] = "no hero"; return Json(bag); }

                var qtType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTracker");
                var tracker = ElementOf(hero, qtType);
                if (tracker == null) { bag["err"] = "no tracker"; return Json(bag); }
                var quest = qtType.GetProperty("ActiveQuest")?.GetValue(tracker);
                if (quest == null) { bag["err"] = "no active quest"; return Json(bag); }

                bag["quest_name"] = quest.GetType().GetProperty("DisplayName")?.GetValue(quest);

                // ActiveObjectivesWithMarkers — alternate path
                var withMarkersProp = quest.GetType().GetProperty("ActiveObjectivesWithMarkers");
                if (withMarkersProp != null)
                {
                    var withMarkers = (withMarkersProp.GetValue(quest) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                    bag["ActiveObjectivesWithMarkers_count"] = withMarkers?.Count ?? 0;
                    if (withMarkers != null && withMarkers.Count > 0)
                        bag["ActiveObjectivesWithMarkers_types"] = withMarkers.Select(o => o.GetType().Name).Distinct().ToArray();
                }

                // ActiveObjectives + GetAllActiveTargets walk
                var objs = (quest.GetType().GetProperty("ActiveObjectives")?.GetValue(quest) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                bag["ActiveObjectives_count"] = objs?.Count ?? 0;

                if (objs == null) return Json(bag);

                var perObjective = new List<object>();
                foreach (var obj in objs)
                {
                    var o = new Dictionary<string, object>();
                    o["name"] = obj.GetType().GetProperty("Name")?.GetValue(obj);

                    var mainScene = obj.GetType().GetProperty("MainTargetScene")?.GetValue(obj);
                    var openScene = obj.GetType().GetProperty("MainTargetOpenWorldScene")?.GetValue(obj);
                    o["MainTargetScene"] = SceneRefName(mainScene);
                    o["MainTargetOpenWorldScene"] = SceneRefName(openScene);

                    var getAllTargets = obj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "GetAllActiveTargets" && m.GetParameters().Length == 2);
                    if (getAllTargets == null) { o["err"] = "no GetAllActiveTargets"; perObjective.Add(o); continue; }

                    foreach (var (label, sceneArg) in new[]
                    {
                        ("MainTargetScene", mainScene),
                        ("MainTargetOpenWorldScene", openScene),
                        ("null", (object)null),
                    })
                    {
                        try
                        {
                            var result = getAllTargets.Invoke(obj, new object[] { hero, sceneArg });
                            var list = (result as System.Collections.IEnumerable)?.Cast<object>().ToList();
                            o[$"targets[{label}]_count"] = list?.Count ?? 0;
                            if (list != null && list.Count > 0)
                            {
                                var first = list[0];
                                o[$"targets[{label}]_first"] = $"type={first.GetType().Name},coords={CoordsOf(first)}";
                            }
                        }
                        catch (Exception e)
                        {
                            o[$"targets[{label}]_err"] = (e.InnerException ?? e).Message;
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
