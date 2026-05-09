using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // Inspects whether the Compass + QuestTracker APIs DestinationProvider relies
    // on still match the live game. Lists actual types/properties so we can see
    // what was renamed in a game update.
    public static class QuestApiProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                bag["hero_alive"] = hero != null;
                if (hero == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                // 1) Compass element
                bag["compass"] = ProbeCompass(hero);

                // 2) QuestTracker element
                bag["quest_tracker"] = ProbeQuestTracker(hero);

                // 3) Hero elements list (sanity — what elements are on the hero?)
                bag["hero_elements"] = ListHeroElements(hero);
            }
            catch (Exception e)
            {
                bag["err"] = e.GetBaseException().Message;
                bag["stack"] = e.StackTrace;
            }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static object ProbeCompass(object hero)
        {
            var d = new Dictionary<string, object>();
            var compassType = ResolveType("Awaken.TG.Main.Maps.Compasses.Compass");
            d["type_resolved"] = compassType?.FullName ?? "null";
            if (compassType == null) return d;

            var elem = ElementOf(hero, compassType);
            d["element_alive"] = elem != null;
            if (elem == null) return d;

            // List public properties
            d["properties"] = compassType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                .ToArray();

            // Try CustomMarkerLocation specifically
            foreach (var pname in new[] { "CustomMarkerLocation", "SpyglassMarkerLocation" })
            {
                var p = compassType.GetProperty(pname);
                if (p == null) { d[pname] = "missing"; continue; }
                try
                {
                    var v = p.GetValue(elem);
                    if (v == null) { d[pname] = "null"; continue; }
                    var coordsProp = v.GetType().GetProperty("Coords") ?? v.GetType().GetProperty("Position");
                    var coords = coordsProp?.GetValue(v);
                    d[pname] = $"type={v.GetType().Name},coords={coords}";
                }
                catch (Exception e) { d[pname] = $"err:{e.GetBaseException().Message}"; }
            }
            return d;
        }

        private static object ProbeQuestTracker(object hero)
        {
            var d = new Dictionary<string, object>();
            var qtType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTracker");
            d["type_resolved"] = qtType?.FullName ?? "null";
            if (qtType == null) return d;

            var tracker = ElementOf(hero, qtType);
            d["tracker_alive"] = tracker != null;
            if (tracker == null) return d;

            d["properties"] = qtType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                .ToArray();

            var activeProp = qtType.GetProperty("ActiveQuest");
            if (activeProp == null) { d["ActiveQuest"] = "property missing"; return d; }
            var quest = activeProp.GetValue(tracker);
            d["ActiveQuest_alive"] = quest != null;
            if (quest == null) return d;

            d["ActiveQuest_type"] = quest.GetType().FullName;
            d["ActiveQuest_DisplayName"] = quest.GetType().GetProperty("DisplayName")?.GetValue(quest)?.ToString() ?? "?";
            d["ActiveQuest_props"] = quest.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name.IndexOf("Objective", StringComparison.OrdinalIgnoreCase) >= 0
                         || p.Name.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0
                         || p.Name == "Tasks")
                .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                .ToArray();

            // Get objectives
            var objsProp = quest.GetType().GetProperty("ActiveObjectives") ?? quest.GetType().GetProperty("Objectives");
            d["objectives_prop_used"] = objsProp?.Name ?? "none";
            var objs = (objsProp?.GetValue(quest) as System.Collections.IEnumerable)?.Cast<object>().ToList();
            d["objectives_count"] = objs?.Count ?? 0;

            if (objs != null && objs.Count > 0)
            {
                var first = objs[0];
                d["first_objective_type"] = first.GetType().FullName;
                d["first_objective_methods"] = first.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(m => $"{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})")
                    .ToArray();
                d["first_objective_props"] = first.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.Name.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0
                             || p.Name.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0
                             || p.Name == "Name")
                    .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                    .ToArray();
            }

            return d;
        }

        private static object ListHeroElements(object hero)
        {
            try
            {
                var elemsProp = hero.GetType().GetProperty("Elements") ?? hero.GetType().GetProperty("AllElements");
                if (elemsProp == null) return "no Elements property";
                var elems = elemsProp.GetValue(hero) as System.Collections.IEnumerable;
                if (elems == null) return "null";
                return elems.Cast<object>()
                    .Where(e => e != null)
                    .Select(e => e.GetType().Name)
                    .Where(n => n.IndexOf("Compass", StringComparison.OrdinalIgnoreCase) >= 0
                             || n.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0
                             || n.IndexOf("Tracker", StringComparison.OrdinalIgnoreCase) >= 0
                             || n.IndexOf("Marker", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Distinct()
                    .ToArray();
            }
            catch (Exception e) { return $"err:{e.GetBaseException().Message}"; }
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
    }
}
