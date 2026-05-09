using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class QuestTrackerInspect
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero == null) return "no hero";

                var trackerType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTracker");
                if (trackerType == null) return "QuestTracker type missing";

                bag["QuestTracker_props"] = trackerType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Take(30).Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
                bag["QuestTracker_methods"] = trackerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_"))
                    .Take(30).Select(m => $"{(m.IsPublic ? "" : "internal ")}{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name))})").ToList();
                bag["QuestTracker_fields"] = trackerType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Take(30).Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();

                // Get the live QuestTracker via Hero.Element<QuestTracker>()
                var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var tracker = elementGeneric?.MakeGenericMethod(trackerType).Invoke(hero, null);
                bag["heroQuestTracker_live"] = tracker != null;
                if (tracker == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                // Walk every property/field of the live tracker — print contents.
                var liveDump = new Dictionary<string, object>();
                foreach (var p in trackerType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    try
                    {
                        var v = p.GetValue(tracker);
                        liveDump["prop:" + p.Name] = SafeStr(v);
                        if (v is System.Collections.IEnumerable en && !(v is string))
                        {
                            var list = en.Cast<object>().ToList();
                            liveDump["prop:" + p.Name + "/count"] = list.Count;
                            if (list.Count > 0)
                                liveDump["prop:" + p.Name + "/first"] = SafeStr(list[0]) + " (" + list[0]?.GetType().Name + ")";
                        }
                    }
                    catch (Exception e) { liveDump["prop:" + p.Name] = "ERR " + e.Message; }
                }
                foreach (var f in trackerType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    try
                    {
                        var v = f.GetValue(tracker);
                        liveDump["field:" + f.Name] = SafeStr(v);
                        if (v is System.Collections.IEnumerable en && !(v is string))
                        {
                            var list = en.Cast<object>().ToList();
                            liveDump["field:" + f.Name + "/count"] = list.Count;
                            if (list.Count > 0)
                                liveDump["field:" + f.Name + "/first"] = SafeStr(list[0]) + " (" + list[0]?.GetType().Name + ")";
                        }
                    }
                    catch (Exception e) { liveDump["field:" + f.Name] = "ERR " + e.Message; }
                }
                bag["heroQuestTracker_state"] = liveDump;
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
                return v.ToString();
            }
            catch { return "ERR"; }
        }
    }
}
