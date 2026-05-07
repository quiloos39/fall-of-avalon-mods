using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class IndicatorScout3
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Find every LocationSpec in the scene and group by GameObject.name (template).
                var specs = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .Where(c => c != null && c.GetType().FullName == "Awaken.TG.Main.Locations.Setup.LocationSpec")
                    .ToList();

                var grouped = specs
                    .GroupBy(s => s.gameObject.name)
                    .OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        name = g.Key,
                        count = g.Count(),
                        // Collect the union of sibling component types for any one example.
                        siblingComponents = g.First().gameObject
                            .GetComponents<Component>()
                            .Where(x => x != null && x.GetType() != typeof(Transform))
                            .Select(x => x.GetType().FullName)
                            .ToList(),
                    })
                    .ToList();
                bag["specsTotal"] = specs.Count;
                bag["specGroupCount"] = grouped.Count;

                // Filter to those with promising names (forge/anvil/alchemy/cauldron/smith/craft/stand/bench/stash).
                string[] needles = { "forge", "anvil", "alchemy", "cauldron", "smith", "stand", "bench", "stash", "table", "workbench", "still", "press", "loom", "kiln" };
                var promising = grouped.Where(g => needles.Any(n => g.name.ToLowerInvariant().Contains(n))).ToList();
                bag["promisingSpecGroups"] = promising;

                // Find the stash spec example and dump its MarkerAttachment data so we can grab the icon ref.
                var stashSpec = specs.FirstOrDefault(s => s.gameObject.name.IndexOf("stash", StringComparison.OrdinalIgnoreCase) >= 0);
                if (stashSpec == null) { bag["stashIcon"] = "(no stash spec found in scene)"; }
                else
                {
                    var ma = stashSpec.gameObject.GetComponents<Component>()
                        .FirstOrDefault(c => c != null && c.GetType().FullName == "Awaken.TG.Main.Maps.Markers.MarkerAttachment");
                    bag["stashSpecName"] = stashSpec.gameObject.name;
                    bag["stashSpecComponents"] = stashSpec.gameObject.GetComponents<Component>()
                        .Where(c => c != null).Select(c => c.GetType().FullName).ToList();

                    if (ma != null)
                    {
                        var maType = ma.GetType();
                        var wrapperField = maType.GetField("markerDataWrapper", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                        var wrapper = wrapperField?.GetValue(ma);
                        bag["stashWrapperType"] = wrapper?.GetType().FullName;
                        bag["stashWrapperFields"] = wrapper == null ? null : wrapper.GetType()
                            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .Select(f => $"{Pretty(f.FieldType)} {f.Name} = {SafeStr(f.GetValue(wrapper))}")
                            .ToList();
                        // Pull MarkerData property
                        var markerDataProp = maType.GetProperty("MarkerData");
                        var markerData = markerDataProp?.GetValue(ma);
                        bag["stashMarkerDataType"] = markerData?.GetType().FullName;
                        if (markerData != null)
                        {
                            var iconProp = markerData.GetType().GetProperty("MarkerIcon");
                            var icon = iconProp?.GetValue(markerData);
                            bag["stashMarkerIcon"] = DescribeIconRef(icon);
                            bag["stashMarkerData"] = markerData.GetType()
                                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                .Select(f => $"{Pretty(f.FieldType)} {f.Name} = {SafeStr(f.GetValue(markerData))}")
                                .ToList();
                        }
                    }
                }

                // Also list IMarkerDataWrapper implementations so we know what type to instantiate.
                var wrapperTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(SafeTypes)
                    .Where(t => t != null && !t.IsAbstract && !t.IsInterface)
                    .Where(t => t.GetInterfaces().Any(i => i.FullName == "Awaken.TG.Main.Maps.Markers.IMarkerDataWrapper"))
                    .Select(t => new
                    {
                        full = t.FullName,
                        ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .Select(c => $"({string.Join(",", c.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name))})")
                            .ToList(),
                    })
                    .ToList();
                bag["markerDataWrapperImpls"] = wrapperTypes;

                // Also dump SimpleMarkerDataWrapper specifically.
                var simpleType = ResolveType("Awaken.TG.Main.Maps.Markers.SimpleMarkerDataWrapper");
                if (simpleType != null)
                {
                    bag["SimpleMarkerDataWrapper"] = new Dictionary<string, object>
                    {
                        ["fields"] = simpleType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                            .Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList(),
                        ["ctors"] = simpleType.GetConstructors().Select(c => "(" + string.Join(",", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")").ToList(),
                    };
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }

            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static string DescribeIconRef(object iconRef)
        {
            if (iconRef == null) return "null";
            var t = iconRef.GetType();
            var sb = new System.Text.StringBuilder();
            sb.Append(t.FullName).Append(" {");
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                try { sb.Append(f.Name).Append("=").Append(SafeStr(f.GetValue(iconRef))).Append(", "); } catch { }
            }
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                try { sb.Append(p.Name).Append("=").Append(SafeStr(p.GetValue(iconRef))).Append(", "); } catch { }
            }
            sb.Append("}");
            return sb.ToString();
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
