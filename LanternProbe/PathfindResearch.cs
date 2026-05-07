using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;

namespace LanternProbe
{
    public static class PathfindResearch
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();

            // Resolve hero
            object hero = null;
            Vector3 playerPos = Vector3.zero;
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero != null) playerPos = (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero);
                bag["playerPos"] = SafeStr(playerPos);
            }
            catch { }

            // ── 1. Search Awaken/TG namespaces for Path/Navigation/Route/Beacon-related types ──
            var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => { var n = a.GetName().Name; return n != null && (n.StartsWith("TG.") || n.StartsWith("Awaken.")); })
                .SelectMany(SafeTypes).Where(t => t != null && !string.IsNullOrEmpty(t.FullName))
                .ToList();

            string[] keywords = { "navmesh", "navigation", "pathfind", "pathfind", "path.", "route", "beacon", "waypoint", "trail", "compass", "minimap", "guide", "navi", "directions", "drake.ai" };
            var byKw = new Dictionary<string, List<string>>();
            foreach (var k in keywords) byKw[k] = new List<string>();
            foreach (var t in allTypes)
            {
                var lname = t.FullName.ToLowerInvariant();
                if (lname.Contains("+<") || lname.Contains("d__")) continue; // skip compiler junk
                foreach (var k in keywords) if (lname.Contains(k.Replace(".", ""))) byKw[k].Add(t.FullName);
            }
            bag["typesByKeyword"] = byKw.Where(kv => kv.Value.Count > 0)
                .ToDictionary(kv => kv.Key, kv => (object)kv.Value.Take(20).ToList());

            // ── 2. Sample NavMesh at 8 cardinal directions out to 100m ──
            // (test if there's NavMesh nearby that we just didn't find at exact player pos)
            var samples = new List<object>();
            var dirs = new[] {
                Vector3.zero, Vector3.forward * 50, Vector3.forward * 100,
                Vector3.right * 50, Vector3.right * 100, Vector3.left * 50, Vector3.left * 100,
                Vector3.back * 50, Vector3.back * 100,
                new Vector3(50,0,50), new Vector3(-50,0,-50), new Vector3(50,0,-50), new Vector3(-50,0,50),
            };
            foreach (var off in dirs)
            {
                var p = playerPos + off;
                bool found = NavMesh.SamplePosition(p, out var hit, 30f, NavMesh.AllAreas);
                samples.Add(new
                {
                    offset = SafeStr(off),
                    target = SafeStr(p),
                    found,
                    hit = found ? SafeStr(hit.position) : null,
                    distance = found ? Vector3.Distance(p, hit.position) : -1f,
                });
            }
            bag["navMesh_widerSampling"] = samples;

            // Count NavMesh triangles loaded by querying CalculateTriangulation (gives all baked triangles).
            try
            {
                var tri = NavMesh.CalculateTriangulation();
                bag["navMesh_globalTriangulation"] = new
                {
                    vertices = tri.vertices?.Length ?? 0,
                    indices = tri.indices?.Length ?? 0,
                    areas = tri.areas?.Length ?? 0,
                };
            }
            catch (Exception e) { bag["navMesh_triangulationErr"] = e.Message; }

            // ── 3. Look for NPCs in scene and inspect what their AI uses for movement ──
            // If they walk via Awaken's custom system, we'll see types like NpcMovement, AILocomotion, etc.
            var npcType = ResolveType("Awaken.TG.Main.Fights.NPCs.NpcElement");
            if (npcType != null)
            {
                try
                {
                    var worldType = ResolveType("Awaken.TG.MVC.World");
                    var allM = worldType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "All" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    var npcs = (allM?.MakeGenericMethod(npcType).Invoke(null, null) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                    bag["liveNpcCount"] = npcs?.Count ?? 0;
                    if (npcs != null && npcs.Count > 0)
                    {
                        // Walk first NPC's properties for movement-related types
                        var n = npcs[0];
                        bag["sampleNpc_propsLikely"] = n.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.Name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0
                                     || p.Name.IndexOf("nav", StringComparison.OrdinalIgnoreCase) >= 0
                                     || p.Name.IndexOf("move", StringComparison.OrdinalIgnoreCase) >= 0
                                     || p.Name.IndexOf("agent", StringComparison.OrdinalIgnoreCase) >= 0
                                     || p.Name.IndexOf("locomotion", StringComparison.OrdinalIgnoreCase) >= 0)
                            .Take(20).Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
                    }
                }
                catch (Exception e) { bag["npcInspectErr"] = e.Message; }
            }

            // ── 4. Quest3DMarker: does the game already have a 3D beacon for visible quest markers? ──
            var q3dview = ResolveType("Awaken.TG.Main.Stories.Quests.VQuest3DMarker");
            if (q3dview != null)
            {
                bag["VQuest3DMarker"] = new
                {
                    fields = q3dview.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Take(20).Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList(),
                    methods = q3dview.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Take(20).Select(m => m.Name).ToList(),
                };
            }

            // ── 5. Hook into existing Compass system ──
            // Compass already shows direction to objectives — the mod could just inject a CompassMarker.
            var compassType = ResolveType("Awaken.TG.Main.Maps.Compasses.Compass");
            if (compassType != null)
            {
                bag["Compass_publicMethods"] = compassType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Take(20).Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})").ToList();
                bag["Compass_publicProps"] = compassType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Take(20).Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
            }

            // ── 6. Drake / DOTS pathfinding system search ──
            var drakeAi = allTypes.Where(t => t.FullName.IndexOf("Drake", StringComparison.OrdinalIgnoreCase) >= 0
                                           && (t.FullName.IndexOf("AI", StringComparison.OrdinalIgnoreCase) >= 0
                                            || t.FullName.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0
                                            || t.FullName.IndexOf("Navi", StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(t => t.FullName).Distinct().Take(30).ToList();
            bag["drakeAiTypes"] = drakeAi;

            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); } catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); } catch { return Array.Empty<Type>(); }
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
