using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class MarkerTypes
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();

            try
            {
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && (n.StartsWith("TG.") || n.StartsWith("Awaken.")); })
                    .SelectMany(SafeTypes).Where(t => t != null).ToList();

                // 1. Concrete MarkerData subclasses (the data shape behind a marker)
                var markerDataBase = ResolveType("Awaken.TG.Main.Maps.Markers.MarkerData");
                bag["MarkerData_subclasses"] = allTypes
                    .Where(t => t != markerDataBase && markerDataBase != null && markerDataBase.IsAssignableFrom(t))
                    .Select(t => new
                    {
                        full = t.FullName,
                        baseType = t.BaseType?.Name,
                        fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            .Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList(),
                    }).ToList();

                // 2. MarkerDataTemplate-derived ScriptableObject classes (asset-template shapes)
                var iMarkerTemplate = ResolveType("Awaken.TG.Main.Maps.Markers.IMarkerDataTemplate");
                bag["MarkerDataTemplate_subclasses"] = allTypes
                    .Where(t => iMarkerTemplate != null && iMarkerTemplate.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    .Select(t => t.FullName).ToList();

                // 3. CompassMarkerType enum values
                var cmt = ResolveType("Awaken.TG.Main.Maps.Markers.CompassMarkerType");
                if (cmt != null && cmt.IsEnum)
                {
                    bag["CompassMarkerType_values"] = Enum.GetNames(cmt);
                }

                // 4. CompassMarker subclasses (the actual rendered compass element types)
                var compassMarker = ResolveType("Awaken.TG.Main.Maps.Compasses.CompassMarker");
                bag["CompassMarker_subclasses"] = allTypes
                    .Where(t => compassMarker != null && compassMarker.IsAssignableFrom(t))
                    .Select(t => t.FullName).ToList();

                // 5. ICompassMarker implementers (what objects can BE a compass marker)
                var iCompass = ResolveType("Awaken.TG.Main.Maps.Markers.ICompassMarker");
                bag["ICompassMarker_implementers"] = allTypes
                    .Where(t => iCompass != null && iCompass.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    .Select(t => t.FullName).ToList();

                // 6. IMarkerDataWrapper implementers (the wrappers that hold MarkerData refs on attachments)
                var iWrap = ResolveType("Awaken.TG.Main.Maps.Markers.IMarkerDataWrapper");
                bag["IMarkerDataWrapper_implementers"] = allTypes
                    .Where(t => iWrap != null && iWrap.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    .Select(t => t.FullName).ToList();

                // 7. PointMapMarker / SpriteMapMarker subclasses (the world-map view-side renderers)
                var pmm = ResolveType("Awaken.TG.Main.Heroes.CharacterSheet.Map.Markers.PointMapMarker");
                bag["PointMapMarker_subclasses"] = pmm == null ? null : allTypes
                    .Where(t => pmm.IsAssignableFrom(t)).Select(t => t.FullName).ToList();

                // 8. The Method enum used in MarkerDataWrapper<T> (Explicit | Embedded | …)
                var mdwGeneric = ResolveType("Awaken.TG.Main.Maps.Markers.MarkerDataWrapper`1");
                if (mdwGeneric != null)
                {
                    var nested = mdwGeneric.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
                    bag["MarkerDataWrapper_nestedTypes"] = nested.Select(n => new {
                        n.FullName,
                        isEnum = n.IsEnum,
                        values = n.IsEnum ? Enum.GetNames(n) : null,
                    }).ToList();
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }

            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch { return Array.Empty<Type>(); }
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
    }
}
