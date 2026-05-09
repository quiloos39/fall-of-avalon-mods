using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class PathfindingUtilsScout
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                foreach (var typeName in new[] {
                    "Awaken.TG.Main.Utility.PathfindingUtils",
                    "Awaken.TG.Main.Locations.Paths.LocationPath",
                    "Awaken.TG.Main.Locations.Paths.PathProvider",
                    "Awaken.TG.Main.Locations.Paths.VertexPath",
                    "Awaken.TG.Main.Locations.Paths.VertexPathSpec",
                    "Awaken.TG.Main.Locations.Paths.PathAttachment",
                    "Awaken.TG.Main.Heroes.MovementSystems.DialogueNavmeshBasedMovement",
                    "Awaken.TG.Main.Maps.Markers.MapMarker",
                    "Awaken.TG.Main.Heroes.CharacterSheet.Map.Markers.MapMarker",
                    "Awaken.TG.Main.Maps.Compasses.Compass",
                })
                {
                    var t = ResolveType(typeName);
                    if (t == null) { bag[typeName] = "(not found)"; continue; }

                    bag[typeName] = new
                    {
                        baseType = t.BaseType?.FullName,
                        interfaces = t.GetInterfaces().Select(i => i.Name).ToList(),
                        staticMethods = t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                            .Take(40).Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name))})").ToList(),
                        instanceMethods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                            .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_"))
                            .Take(40).Select(m => $"{(m.IsPublic ? "" : "internal ")}{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name))})").ToList(),
                        fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                            .Take(30).Select(f => $"{(f.IsStatic ? "static " : "")}{Pretty(f.FieldType)} {f.Name}").ToList(),
                        properties = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                            .Take(20).Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList(),
                    };
                }
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
    }
}
