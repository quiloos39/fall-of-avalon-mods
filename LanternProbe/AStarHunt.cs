using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class AStarHunt
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Look for A* Pathfinding Project (Pathfinding.* namespace) in loaded assemblies.
                var pathfindingTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .Where(t => t != null && t.Namespace != null && t.Namespace.StartsWith("Pathfinding"))
                    .ToList();

                bag["AStarTypeCount"] = pathfindingTypes.Count;
                bag["AStarKeyTypes"] = pathfindingTypes
                    .Where(t => new[] { "AstarPath", "Seeker", "Path", "RecastGraph", "GridGraph", "PointGraph", "NavGraph", "ABPath", "GraphNode", "RichAI" }.Contains(t.Name))
                    .Select(t => t.FullName).ToList();
                bag["AStarSampleTypes"] = pathfindingTypes.Take(40).Select(t => t.FullName).ToList();

                // Find the active AstarPath instance in the scene.
                var astarPathType = pathfindingTypes.FirstOrDefault(t => t.Name == "AstarPath");
                if (astarPathType != null)
                {
                    bag["AstarPath_publicMethods"] = astarPathType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_"))
                        .Take(40).Select(m => $"{(m.IsStatic ? "static " : "")}{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name))})").ToList();
                    bag["AstarPath_publicProps"] = astarPathType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Take(20).Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();

                    // Check for the live singleton.
                    var activeProp = astarPathType.GetProperty("active", BindingFlags.Public | BindingFlags.Static)
                                  ?? astarPathType.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
                    var activeInstance = activeProp?.GetValue(null);
                    bag["AstarPath_active_instance"] = activeInstance != null;

                    if (activeInstance != null)
                    {
                        // Inspect graph types
                        var graphsProp = astarPathType.GetProperty("graphs", BindingFlags.Public | BindingFlags.Instance);
                        var graphs = graphsProp?.GetValue(activeInstance) as Array;
                        if (graphs != null)
                        {
                            bag["AstarPath_graphs"] = graphs.Cast<object>().Where(g => g != null).Select(g => new
                            {
                                type = g.GetType().FullName,
                                fields = g.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                    .Take(15).Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList(),
                            }).ToList();
                        }

                        // Try snapping the player position to nearest graph node — proves pathfinding is alive at our coords.
                        var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                        var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        Vector3 playerPos = hero != null ? (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero) : Vector3.zero;
                        bag["playerPos"] = SafeStr(playerPos);

                        var getNearestM = astarPathType.GetMethods()
                            .FirstOrDefault(m => m.Name == "GetNearest" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Vector3));
                        if (getNearestM != null)
                        {
                            try
                            {
                                var nn = getNearestM.Invoke(activeInstance, new object[] { playerPos });
                                bag["GetNearest_result_type"] = nn?.GetType().FullName;
                                if (nn != null)
                                {
                                    foreach (var p in nn.GetType().GetProperties())
                                    {
                                        try { bag["GetNearest_" + p.Name] = SafeStr(p.GetValue(nn)); } catch { }
                                    }
                                }
                            }
                            catch (Exception e) { bag["GetNearest_err"] = e.InnerException?.Message ?? e.Message; }
                        }
                    }
                }

                // Find Seeker — typically attached to NPCs / hero
                var seekerType = pathfindingTypes.FirstOrDefault(t => t.Name == "Seeker");
                if (seekerType != null)
                {
                    bag["Seeker_publicMethods"] = seekerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && m.Name.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Take(20).Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType) + " " + p.Name))})").ToList();

                    var allSeekers = UnityEngine.Object.FindObjectsOfType(seekerType) as Component[];
                    bag["liveSeekerCount"] = allSeekers?.Length ?? 0;
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

        private static string SafeStr(object v)
        {
            try
            {
                if (v is Vector3 v3) return $"({v3.x:F1},{v3.y:F1},{v3.z:F1})";
                return v?.ToString() ?? "null";
            }
            catch { return "ERR"; }
        }
    }
}
