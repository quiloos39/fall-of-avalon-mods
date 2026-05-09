using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace QuestProbe
{
    public static class Run
    {
        public static string Get()
        {
            try
            {
                Type heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                if (heroType == null) return Err("Hero type not found");

                object hero = heroType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero == null) return Err("Hero.Current is null");

                Type questTrackerType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTracker");
                var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                object tracker = elementGeneric.MakeGenericMethod(questTrackerType).Invoke(hero, null);
                object quest = questTrackerType.GetProperty("ActiveQuest")?.GetValue(tracker);
                if (quest == null) return Err("no active quest");

                Vector3? heroPos = ReadVec3(hero, "Coords") ?? ReadVec3(hero, "Position");

                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"quest\":").Append(J(ReadStr(quest, "DisplayName")));
                sb.Append(",\"questId\":").Append(J(ReadStr(quest, "Id") ?? ReadStr(quest, "ID")));
                sb.Append(",\"questState\":").Append(J(ReadStr(quest, "State")));
                sb.Append(",\"heroPos\":").Append(JVec(heroPos));

                // Dump every public property on the quest object (filtered to readable scalars)
                sb.Append(",\"questAllProps\":").Append(DumpProps(quest, maxStr: 200));

                sb.Append(",\"activeObjectives\":[");
                bool first = true;
                var actProp = quest.GetType().GetProperty("ActiveObjectives");
                if (actProp?.GetValue(quest) is IEnumerable actObjs)
                {
                    foreach (var o in actObjs)
                    {
                        if (!first) sb.Append(",");
                        first = false;
                        sb.Append(DumpObjective(o, hero, heroPos));
                    }
                }
                sb.Append("]");

                // ALL objectives — to show what's already completed and what comes next.
                sb.Append(",\"allObjectivesShallow\":[");
                first = true;
                var allObjsRaw = quest.GetType().GetProperty("Objectives")?.GetValue(quest);
                var allObjs = allObjsRaw == null ? null : ForceEnumerate(allObjsRaw);
                if (allObjs != null)
                {
                    foreach (var o in allObjs)
                    {
                        if (!first) sb.Append(",");
                        first = false;
                        sb.Append("{");
                        sb.Append("\"name\":").Append(J(ReadStr(o, "Name")));
                        sb.Append(",\"state\":").Append(J(ReadStr(o, "State")));
                        sb.Append(",\"id\":").Append(J(ReadStr(o, "ID")));
                        sb.Append(",\"description\":").Append(J(ReadStr(o, "Description")));
                        sb.Append("}");
                    }
                }
                sb.Append("]");

                // Hero inventory items with "letter" or "note" in their name
                sb.Append(",\"letterItems\":").Append(DumpLetterItems(hero));

                sb.Append("}");
                return sb.ToString();
            }
            catch (Exception e)
            {
                var inner = (e is TargetInvocationException tie && tie.InnerException != null) ? tie.InnerException : e;
                return "{\"error\":" + J(inner.GetType().Name + ": " + inner.Message)
                       + ",\"stack\":" + J(inner.StackTrace ?? "") + "}";
            }
        }

        private static string DumpObjective(object obj, object hero, Vector3? heroPos)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"type\":").Append(J(obj.GetType().FullName));
            sb.Append(",\"name\":").Append(J(ReadStr(obj, "Name")));
            sb.Append(",\"state\":").Append(J(ReadStr(obj, "State")));
            sb.Append(",\"description\":").Append(J(ReadStr(obj, "Description")));
            sb.Append(",\"trackerDescription\":").Append(J(ResolveLocString(obj.GetType().GetProperty("TrackerDescription")?.GetValue(obj))));
            sb.Append(",\"trackers\":").Append(DumpTrackers(obj));
            sb.Append(",\"props\":").Append(DumpProps(obj, maxStr: 300));

            // Try to call GetAllActiveTargets(hero, scene) for both possible scene args
            var targets = new List<object>();
            var getAll = obj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetAllActiveTargets" && m.GetParameters().Length == 2);
            if (getAll != null)
            {
                var mainScene = obj.GetType().GetProperty("MainTargetScene")?.GetValue(obj);
                var openScene = obj.GetType().GetProperty("MainTargetOpenWorldScene")?.GetValue(obj);
                foreach (var (label, scene) in new[] { ("MainTargetScene", mainScene), ("MainTargetOpenWorldScene", openScene), ("null", (object)null) })
                {
                    object res;
                    try { res = getAll.Invoke(obj, new object[] { hero, scene }); }
                    catch (Exception e) { targets.Add(new { sceneArg = label, error = (e.InnerException ?? e).Message }); continue; }
                    if (res is IEnumerable list)
                    {
                        foreach (var t in list)
                        {
                            if (t == null) continue;
                            Vector3? c = ReadVec3(t, "Coords") ?? ReadVec3(t, "Position") ?? ReadVec3(t, "WorldPosition");
                            float? dist = (heroPos.HasValue && c.HasValue) ? Vector3.Distance(heroPos.Value, c.Value) : (float?)null;
                            targets.Add(new
                            {
                                sceneArg = label,
                                targetType = t.GetType().FullName,
                                name = ReadStr(t, "DisplayName") ?? ReadStr(t, "Name") ?? ReadStr(t, "ID"),
                                coords = c,
                                distanceFromHero = dist,
                            });
                        }
                    }
                }
            }
            sb.Append(",\"targets\":").Append(SerializeManually(targets));
            sb.Append("}");
            return sb.ToString();
        }

        private static string DumpLetterItems(object hero)
        {
            try
            {
                Type itemsType = ResolveType("Awaken.TG.Main.Heroes.Items.HeroItems")
                              ?? ResolveType("Awaken.TG.Main.Heroes.HeroItems");
                if (itemsType == null) return "\"<HeroItems type not found>\"";

                var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                object items = elementGeneric.MakeGenericMethod(itemsType).Invoke(hero, null);
                if (items == null) return "\"<no HeroItems element>\"";

                // Try common collection property names
                object collection = null;
                foreach (var pname in new[] { "Items", "AllItems", "OwnedItems", "Inventory" })
                {
                    var p = items.GetType().GetProperty(pname, BindingFlags.Public | BindingFlags.Instance);
                    if (p == null) continue;
                    try { collection = p.GetValue(items); if (collection != null) break; } catch { }
                }
                if (collection == null) return "\"<no items collection — props: " + string.Join(",", items.GetType().GetProperties().Select(p => p.Name)) + ">\"";

                var list = ForceEnumerate(collection);
                if (list == null) return "\"<could not enumerate>\"";

                var sb = new StringBuilder("[");
                bool first = true;
                foreach (var item in list)
                {
                    if (item == null) continue;
                    string name = ReadStr(item, "DisplayName") ?? ReadStr(item, "Name") ?? "";
                    string id   = ReadStr(item, "Id") ?? ReadStr(item, "ID") ?? "";
                    string lower = (name + " " + id).ToLowerInvariant();
                    if (!lower.Contains("letter") && !lower.Contains("note") && !lower.Contains("missive") && !lower.Contains("crooked")) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{\"name\":").Append(J(name)).Append(",\"id\":").Append(J(id));
                    sb.Append(",\"type\":").Append(J(item.GetType().FullName));
                    sb.Append(",\"template\":").Append(J(ReadStr(item, "Template")));
                    sb.Append("}");
                }
                sb.Append("]");
                return sb.ToString();
            }
            catch (Exception e) { return J("<<" + e.GetType().Name + ": " + e.Message + ">>"); }
        }

        // ModelsSet<T> doesn't implement plain IEnumerable in a way that `is IEnumerable` catches —
        // walk its GetEnumerator() reflectively, or fall back to LINQ Cast<object>.
        private static List<object> ForceEnumerate(object collection)
        {
            if (collection is IEnumerable plain)
            {
                try { return plain.Cast<object>().ToList(); } catch { }
            }
            // Look for a GetEnumerator method
            var ge = collection.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetEnumerator" && m.GetParameters().Length == 0);
            if (ge != null)
            {
                try
                {
                    var en = ge.Invoke(collection, null);
                    if (en == null) return null;
                    var moveNext = en.GetType().GetMethod("MoveNext");
                    var current = en.GetType().GetProperty("Current");
                    var list = new List<object>();
                    while ((bool)moveNext.Invoke(en, null))
                    {
                        list.Add(current.GetValue(en));
                    }
                    return list;
                }
                catch { }
            }
            return null;
        }

        // Try common ways game LocString-like wrappers expose their localized text.
        private static string ResolveLocString(object loc)
        {
            if (loc == null) return null;
            // Try methods first: Translate(), GetLocalizedString(), ToString()
            foreach (var mname in new[] { "Translate", "GetLocalizedString", "GetLocalized", "Localize" })
            {
                var m = loc.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(mm => mm.Name == mname && mm.GetParameters().Length == 0);
                if (m != null)
                {
                    try { var r = m.Invoke(loc, null); if (r != null) return r.ToString(); } catch { }
                }
            }
            // Try properties
            foreach (var pname in new[] { "Translation", "LocalizedString", "Text", "Value" })
            {
                var p = loc.GetType().GetProperty(pname, BindingFlags.Public | BindingFlags.Instance);
                if (p != null)
                {
                    try { var r = p.GetValue(loc); if (r != null && r.ToString() != loc.GetType().FullName) return r.ToString(); } catch { }
                }
            }
            // Last resort: check if there's an "ID" property and return that key (better than nothing)
            string id = ReadStr(loc, "ID") ?? ReadStr(loc, "Id");
            return id != null ? "(unresolved loc, key=" + id + ")" : null;
        }

        private static string DumpTrackers(object objective)
        {
            var trackersProp = objective.GetType().GetProperty("Trackers");
            if (trackersProp == null) return "null";
            object trackers;
            try { trackers = trackersProp.GetValue(objective); } catch { return "\"<read failed>\""; }
            if (trackers == null) return "null";

            var items = ForceEnumerate(trackers);
            if (items == null) return J(trackers.ToString() + " (no enumerator found)");

            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var t in items)
            {
                if (!first) sb.Append(",");
                first = false;
                if (t == null) { sb.Append("null"); continue; }
                sb.Append("{");
                sb.Append("\"type\":").Append(J(t.GetType().FullName));
                // Trackers usually expose Current/Target counts and an optional tracked target ref.
                foreach (var pname in new[] { "Name", "DisplayName", "Current", "Target", "TargetCount", "CurrentCount",
                                              "Count", "MaxCount", "IsCompleted", "Completed", "Progress",
                                              "TargetID", "TargetTemplate", "ItemTemplate", "LocationTemplate" })
                {
                    var p = t.GetType().GetProperty(pname, BindingFlags.Public | BindingFlags.Instance);
                    if (p == null || p.GetIndexParameters().Length > 0) continue;
                    object v;
                    try { v = p.GetValue(t); } catch { continue; }
                    if (v == null) continue;
                    sb.Append(",").Append(J(pname)).Append(":").Append(J(v.ToString()));
                }
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string DumpProps(object obj, int maxStr)
        {
            if (obj == null) return "{}";
            var dict = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                string val;
                try
                {
                    var v = p.GetValue(obj);
                    val = StringifyScalar(v, maxStr);
                }
                catch (Exception e) { val = "<<" + (e.InnerException ?? e).GetType().Name + ">>"; }
                if (val == null) continue;
                dict[p.Name] = val;
            }
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append(J(kv.Key)).Append(":").Append(J(kv.Value));
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string StringifyScalar(object v, int maxStr)
        {
            if (v == null) return "null";
            if (v is string s) return Truncate(s, maxStr);
            if (v is bool || v is int || v is long || v is float || v is double || v is decimal) return v.ToString();
            if (v is Vector3 v3) return $"({v3.x:F1},{v3.y:F1},{v3.z:F1})";
            var t = v.GetType();
            if (t.IsEnum) return v.ToString();
            // Skip noisy reference objects unless they have a friendly ToString
            if (t.IsClass)
            {
                var ts = v.ToString();
                if (ts == t.FullName || ts == t.Name) return null;     // default object.ToString — useless
                return Truncate(ts, maxStr);
            }
            return Truncate(v.ToString(), maxStr);
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

        private static string SerializeManually(IEnumerable list)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append(SerializeAnon(item));
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string SerializeAnon(object o)
        {
            if (o == null) return "null";
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var p in o.GetType().GetProperties())
            {
                if (!first) sb.Append(",");
                first = false;
                var v = p.GetValue(o);
                sb.Append(J(p.Name)).Append(":");
                if (v == null) sb.Append("null");
                else if (v is float f) sb.Append(f.ToString("F2"));
                else if (v is double d) sb.Append(d.ToString("F2"));
                else if (v is Vector3 v3) sb.Append(JVec(v3));
                else if (v is string s) sb.Append(J(s));
                else sb.Append(J(v.ToString()));
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static Vector3? ReadVec3(object obj, string prop)
        {
            try
            {
                var p = obj?.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
                if (p == null) return null;
                var v = p.GetValue(obj);
                if (v is Vector3 v3) return v3;
            }
            catch { }
            return null;
        }

        private static string ReadStr(object obj, string prop)
        {
            try { return obj?.GetType().GetProperty(prop)?.GetValue(obj)?.ToString(); } catch { return null; }
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

        private static string Err(string msg) => "{\"error\":" + J(msg) + "}";

        private static string JVec(Vector3? v) => v.HasValue ? $"[{v.Value.x:F1},{v.Value.y:F1},{v.Value.z:F1}]" : "null";

        private static string J(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.AppendFormat("\\u{0:X4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }
    }
}
