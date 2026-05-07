using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace QuestProbe
{
    public static class SetMarker
    {
        // Inspect the Compass element to discover the right API for placing a custom marker.
        public static string Inspect()
        {
            try
            {
                object compass = GetCompass();
                if (compass == null) return "{\"error\":\"could not get Compass\"}";

                var sb = new StringBuilder();
                sb.Append("{\"compassType\":").Append(J(compass.GetType().FullName));

                sb.Append(",\"methods\":[");
                bool first = true;
                foreach (var m in compass.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic).OrderBy(m => m.Name))
                {
                    if (m.IsSpecialName) continue;  // skip property accessors
                    if (m.DeclaringType == typeof(object)) continue;
                    string lower = m.Name.ToLowerInvariant();
                    if (!lower.Contains("marker") && !lower.Contains("custom")) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{\"name\":").Append(J(m.Name));
                    sb.Append(",\"public\":").Append(m.IsPublic ? "true" : "false");
                    sb.Append(",\"params\":[");
                    bool fp = true;
                    foreach (var p in m.GetParameters())
                    {
                        if (!fp) sb.Append(",");
                        fp = false;
                        sb.Append("{\"name\":").Append(J(p.Name)).Append(",\"type\":").Append(J(p.ParameterType.FullName)).Append("}");
                    }
                    sb.Append("],\"returns\":").Append(J(m.ReturnType.FullName)).Append("}");
                }
                sb.Append("]");

                sb.Append(",\"properties\":[");
                first = true;
                foreach (var p in compass.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic).OrderBy(p => p.Name))
                {
                    string lower = p.Name.ToLowerInvariant();
                    if (!lower.Contains("marker") && !lower.Contains("custom")) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{\"name\":").Append(J(p.Name)).Append(",\"type\":").Append(J(p.PropertyType.FullName)).Append(",\"canRead\":").Append(p.CanRead?"true":"false").Append(",\"canWrite\":").Append(p.CanWrite?"true":"false").Append("}");
                }
                sb.Append("]");

                sb.Append(",\"fields\":[");
                first = true;
                foreach (var f in compass.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic).OrderBy(f => f.Name))
                {
                    string lower = f.Name.ToLowerInvariant();
                    if (!lower.Contains("marker") && !lower.Contains("custom")) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{\"name\":").Append(J(f.Name)).Append(",\"type\":").Append(J(f.FieldType.FullName)).Append("}");
                }
                sb.Append("]");

                sb.Append("}");
                return sb.ToString();
            }
            catch (Exception e)
            {
                var inner = (e is TargetInvocationException tie && tie.InnerException != null) ? tie.InnerException : e;
                return "{\"error\":" + J(inner.GetType().Name + ": " + inner.Message) + "}";
            }
        }

        // Set the custom map marker to Rowan's known coords.
        public static string Set()
        {
            try
            {
                object compass = GetCompass();
                if (compass == null) return "{\"error\":\"could not get Compass\"}";

                Vector3 rowan = new Vector3(-1118.9f, 113.9f, -2668.1f);

                MethodInfo place = compass.GetType().GetMethod("PlaceCustomMarker",
                    BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Vector3) }, null);
                if (place == null) return "{\"error\":\"PlaceCustomMarker(Vector3) not found\"}";

                object res = place.Invoke(compass, new object[] { rowan });

                // Verify by reading back CustomMarkerLocation
                object loc = compass.GetType().GetProperty("CustomMarkerLocation")?.GetValue(compass);
                Vector3? readback = null;
                if (loc != null)
                {
                    var p = loc.GetType().GetProperty("Coords");
                    if (p != null && p.GetValue(loc) is Vector3 v) readback = v;
                }

                return "{\"ok\":true,\"placedAt\":[-1118.9,113.9,-2668.1],"
                     + "\"markerObject\":" + J(res?.ToString() ?? "null")
                     + ",\"compassReadback\":" + (readback.HasValue ? $"[{readback.Value.x:F1},{readback.Value.y:F1},{readback.Value.z:F1}]" : "null")
                     + "}";
            }
            catch (Exception e)
            {
                var inner = (e is TargetInvocationException tie && tie.InnerException != null) ? tie.InnerException : e;
                return "{\"error\":" + J(inner.GetType().Name + ": " + inner.Message) + ",\"stack\":" + J(inner.StackTrace ?? "") + "}";
            }
        }

        private static object GetCompass()
        {
            Type heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
            object hero = heroType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (hero == null) return null;

            Type compassType = ResolveType("Awaken.TG.Main.Maps.Compasses.Compass");
            if (compassType == null) return null;

            var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
            return elementGeneric.MakeGenericMethod(compassType).Invoke(hero, null);
        }

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
