using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace QuestProbe
{
    public static class FindRowan
    {
        public static string Get()
        {
            try
            {
                Type worldType = ResolveType("Awaken.TG.MVC.World");
                Type npcType = ResolveType("Awaken.TG.Main.Fights.NPCs.NpcElement");
                if (worldType == null || npcType == null)
                    return "{\"error\":\"World or NpcElement type not found\"}";

                // World.All<T>() — static
                var allMethod = worldType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "All" && m.IsGenericMethod && m.GetParameters().Length == 0);
                if (allMethod == null) return "{\"error\":\"World.All<T>() not found\"}";

                object allNpcs = allMethod.MakeGenericMethod(npcType).Invoke(null, null);
                var list = ForceEnumerate(allNpcs);
                if (list == null) return "{\"error\":\"could not enumerate NPCs\"}";

                Type heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                object hero = heroType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                Vector3 heroPos = hero != null ? (Vector3)(hero.GetType().GetProperty("Coords")?.GetValue(hero) ?? Vector3.zero) : Vector3.zero;

                var matches = new List<string>();
                int total = 0;
                foreach (var npc in list)
                {
                    total++;
                    string name = ReadStr(npc, "DisplayName") ?? ReadStr(npc, "Name") ?? "";
                    string id = ReadStr(npc, "ID") ?? "";
                    string templateName = "";
                    try
                    {
                        var tmpl = npc.GetType().GetProperty("Template")?.GetValue(npc);
                        if (tmpl != null) templateName = ReadStr(tmpl, "name") ?? tmpl.ToString();
                    }
                    catch { }

                    string lower = (name + " " + id + " " + templateName).ToLowerInvariant();
                    if (!lower.Contains("rowan")) continue;

                    Vector3? coords = ReadVec3(npc, "Coords") ?? ReadVec3(npc, "Position");

                    // Walk ParentModel chain looking for a non-sentinel coord (Location, RuntimeLocation, etc.
                    // hold the persistent world coord even when the NPC body isn't streamed in)
                    Vector3? parentCoords = null;
                    string parentChain = "";
                    object current = npc;
                    for (int hops = 0; hops < 6 && current != null; hops++)
                    {
                        var parent = current.GetType().GetProperty("ParentModel")?.GetValue(current);
                        if (parent == null || parent == current) break;
                        parentChain += " -> " + parent.GetType().Name;
                        var pc = ReadVec3(parent, "Coords") ?? ReadVec3(parent, "Position") ?? ReadVec3(parent, "WorldPosition");
                        if (pc.HasValue && !IsSentinel(pc.Value))
                        {
                            parentCoords = pc;
                            parentChain += "[got coords]";
                            break;
                        }
                        current = parent;
                    }

                    Vector3? bestCoords = (coords.HasValue && !IsSentinel(coords.Value)) ? coords : parentCoords;
                    float dist = bestCoords.HasValue ? Vector3.Distance(heroPos, bestCoords.Value) : -1f;

                    var sb = new StringBuilder();
                    sb.Append("{");
                    sb.Append("\"name\":").Append(J(name));
                    sb.Append(",\"id\":").Append(J(id));
                    sb.Append(",\"template\":").Append(J(templateName));
                    sb.Append(",\"npcCoords\":").Append(coords.HasValue
                        ? $"[{coords.Value.x:F1},{coords.Value.y:F1},{coords.Value.z:F1}]" : "null");
                    sb.Append(",\"parentChain\":").Append(J(parentChain));
                    sb.Append(",\"resolvedCoords\":").Append(bestCoords.HasValue
                        ? $"[{bestCoords.Value.x:F1},{bestCoords.Value.y:F1},{bestCoords.Value.z:F1}]" : "null");
                    sb.Append(",\"distanceFromHero\":").Append(dist >= 0 ? dist.ToString("F1") : "null");
                    sb.Append(",\"alive\":").Append(J(ReadStr(npc, "IsAlive") ?? ReadStr(npc, "Alive")));
                    sb.Append("}");
                    matches.Add(sb.ToString());
                }

                var result = new StringBuilder();
                result.Append("{\"totalNpcsLoaded\":").Append(total);
                result.Append(",\"heroPos\":").Append($"[{heroPos.x:F1},{heroPos.y:F1},{heroPos.z:F1}]");
                result.Append(",\"matches\":[").Append(string.Join(",", matches)).Append("]}");
                return result.ToString();
            }
            catch (Exception e)
            {
                var inner = (e is TargetInvocationException tie && tie.InnerException != null) ? tie.InnerException : e;
                return "{\"error\":" + J(inner.GetType().Name + ": " + inner.Message)
                       + ",\"stack\":" + J(inner.StackTrace ?? "") + "}";
            }
        }

        private static bool IsSentinel(Vector3 v)
        {
            return Mathf.Abs(v.x) >= 4500f && Mathf.Abs(v.y) >= 4500f && Mathf.Abs(v.z) >= 4500f;
        }

        private static List<object> ForceEnumerate(object collection)
        {
            if (collection == null) return null;
            if (collection is IEnumerable plain)
            { try { return plain.Cast<object>().ToList(); } catch { } }
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
                    while ((bool)moveNext.Invoke(en, null)) list.Add(current.GetValue(en));
                    return list;
                }
                catch { }
            }
            return null;
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
        { try { return obj?.GetType().GetProperty(prop)?.GetValue(obj)?.ToString(); } catch { return null; } }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }

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
