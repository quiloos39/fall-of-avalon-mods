using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class IndicatorScout4
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Walk SimpleMarkerDataWrapper class hierarchy.
                var simple = ResolveType("Awaken.TG.Main.Maps.Markers.SimpleMarkerDataWrapper");
                if (simple != null)
                {
                    var hierarchy = new List<object>();
                    var t = simple;
                    while (t != null && t != typeof(object))
                    {
                        hierarchy.Add(new
                        {
                            type = t.FullName,
                            fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                .Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList(),
                            properties = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList(),
                        });
                        t = t.BaseType;
                    }
                    bag["SimpleMarkerDataWrapper_hierarchy"] = hierarchy;
                }

                // IMarkerDataWrapper interface
                var iface = ResolveType("Awaken.TG.Main.Maps.Markers.IMarkerDataWrapper");
                if (iface != null)
                {
                    bag["IMarkerDataWrapper"] = new
                    {
                        members = iface.GetMembers().Select(m => $"{m.MemberType} {m.Name}").ToList(),
                    };
                }

                // Get the live stash MarkerAttachment + its wrapper, walk wrapper class hierarchy
                // for actual stored fields including inherited ones, and report them.
                var stashSpec = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(c => c != null && c.GetType().FullName == "Awaken.TG.Main.Locations.Setup.LocationSpec"
                                       && c.gameObject.name.Contains("Stash"));
                if (stashSpec != null)
                {
                    var ma = stashSpec.gameObject.GetComponents<Component>()
                        .FirstOrDefault(c => c != null && c.GetType().FullName == "Awaken.TG.Main.Maps.Markers.MarkerAttachment");
                    if (ma != null)
                    {
                        var wrapperField = ma.GetType().GetField("markerDataWrapper", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                        var wrapper = wrapperField?.GetValue(ma);
                        if (wrapper != null)
                        {
                            var allFields = new List<string>();
                            for (var t = wrapper.GetType(); t != null && t != typeof(object); t = t.BaseType)
                            {
                                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                                {
                                    object val = null;
                                    try { val = f.GetValue(wrapper); } catch { }
                                    allFields.Add($"[{t.Name}] {Pretty(f.FieldType)} {f.Name} = {SafeStr(val)}");
                                }
                            }
                            bag["stashWrapper_AllFields_AllLevels"] = allFields;

                            // Try to read MarkerData via the property defined somewhere in hierarchy.
                            var mdProp = wrapper.GetType().GetProperty("MarkerData");
                            var md = mdProp?.GetValue(wrapper);
                            bag["stashWrapper_MarkerData_via_property"] = md == null ? null : new
                            {
                                type = md.GetType().FullName,
                                fields = md.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                    .Select(f => $"{Pretty(f.FieldType)} {f.Name} = {SafeStr(f.GetValue(md))}").ToList(),
                            };
                        }
                    }
                }

                // Find a forge spec in the scene (anywhere, not just nearby).
                var forgeSpec = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(c => c != null && c.GetType().FullName == "Awaken.TG.Main.Locations.Setup.LocationSpec"
                                       && c.gameObject.name.IndexOf("Blacksmith", StringComparison.OrdinalIgnoreCase) >= 0);
                if (forgeSpec != null)
                {
                    bag["forgeSpecFound"] = new
                    {
                        name = forgeSpec.gameObject.name,
                        worldPos = $"({forgeSpec.transform.position.x:F1},{forgeSpec.transform.position.y:F1},{forgeSpec.transform.position.z:F1})",
                        components = forgeSpec.gameObject.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName).ToList(),
                    };
                }
                else bag["forgeSpecFound"] = "(no forge spec in current scene)";

                // Get LocationSpec public API and how it relates to Location model.
                var locSpec = ResolveType("Awaken.TG.Main.Locations.Setup.LocationSpec");
                if (locSpec != null)
                {
                    bag["LocationSpec_publicMethods"] = locSpec.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Take(20)
                        .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .ToList();
                    bag["LocationSpec_publicProps"] = locSpec.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); } catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); } catch { return Array.Empty<Type>(); }
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
                if (v == null) return "null";
                if (v is UnityEngine.Object uo) return uo.name + " (" + uo.GetType().Name + ")";
                return v.ToString();
            }
            catch (Exception e) { return "ERR " + e.GetType().Name; }
        }
    }
}
