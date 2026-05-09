using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System.Reflection;

namespace LanternProbe
{
    public static class AnyRendererScan
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                Vector3 playerPos = hero != null ? (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero) : Vector3.zero;
                bag["playerPos"] = $"({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})";

                // 1) ALL Renderer subtypes (MeshRenderer + SkinnedMeshRenderer + BillboardRenderer + others)
                var allRends = UnityEngine.Object.FindObjectsOfType<Renderer>();
                bag["totalAnyRenderer"] = allRends.Length;
                var byType = allRends.GroupBy(r => r.GetType().Name).Select(g => $"{g.Key}={g.Count()}").ToList();
                bag["byRendererType"] = byType;

                // 2) Drake renderers — DOTS / ECS authoring components
                var drakeMeshType = ResolveType("Awaken.ECS.DrakeRenderer.Authoring.DrakeMeshRenderer");
                if (drakeMeshType != null)
                {
                    var drakes = UnityEngine.Object.FindObjectsOfType(drakeMeshType) as Component[];
                    bag["totalDrakeMeshRenderers"] = drakes?.Length ?? 0;

                    if (drakes != null)
                    {
                        // Group Drakes by mesh + material name (similar pattern to before)
                        // Drake stores AssetGUID-based references — we'll use MeshReferenceData
                        // and friends to get human-readable names.
                        var drakeNearby = drakes
                            .Where(d => Vector3.Distance(d.transform.position, playerPos) < 30f)
                            .Take(50)
                            .Select(d => new
                            {
                                go = d.name,
                                pos = $"({d.transform.position.x:F1},{d.transform.position.y:F1},{d.transform.position.z:F1})",
                                mesh = SafeStr(d.GetType().GetProperty("MeshReferenceData", BindingFlags.Public | BindingFlags.Instance)?.GetValue(d)),
                            })
                            .ToList();
                        bag["drakeNearby"] = drakeNearby;

                        // Group Drake by mesh GUID/name across whole scene
                        var drakeAll = drakes
                            .Select(d =>
                            {
                                var mr = d.GetType().GetProperty("MeshReferenceData", BindingFlags.Public | BindingFlags.Instance)?.GetValue(d);
                                return mr?.ToString();
                            })
                            .Where(s => s != null)
                            .GroupBy(s => s)
                            .OrderByDescending(g => g.Count())
                            .Select(g => new { mesh = g.Key, count = g.Count() })
                            .Take(30).ToList();
                        bag["drakeMeshFrequencyAcrossScene"] = drakeAll;

                        // Specifically look for poppy/flower/daffodil in the Drake mesh names
                        var drakeFlowers = drakes
                            .Select(d =>
                            {
                                var mr = d.GetType().GetProperty("MeshReferenceData", BindingFlags.Public | BindingFlags.Instance)?.GetValue(d);
                                return new { d, mrStr = mr?.ToString() };
                            })
                            .Where(x => x.mrStr != null
                                     && (x.mrStr.IndexOf("flower", StringComparison.OrdinalIgnoreCase) >= 0
                                      || x.mrStr.IndexOf("poppy", StringComparison.OrdinalIgnoreCase) >= 0
                                      || x.mrStr.IndexOf("daffodil", StringComparison.OrdinalIgnoreCase) >= 0
                                      || x.mrStr.IndexOf("petal", StringComparison.OrdinalIgnoreCase) >= 0
                                      || x.mrStr.IndexOf("bloom", StringComparison.OrdinalIgnoreCase) >= 0))
                            .Take(20)
                            .Select(x => new
                            {
                                go = x.d.name,
                                mesh = x.mrStr,
                                distance = Vector3.Distance(x.d.transform.position, playerPos),
                            })
                            .ToList();
                        bag["drakeFlowers_anywhere"] = drakeFlowers;
                    }
                }

                // 3) Look for any "Leshy" types — TG's vegetation rendering system
                var leshyTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .Where(t => t != null && t.FullName != null && t.FullName.IndexOf("Leshy", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Take(30).Select(t => t.FullName).ToList();
                bag["leshyTypes"] = leshyTypes;

                // Try to find Leshy live components in scene
                foreach (var leshyTypeName in leshyTypes.Where(n => !n.Contains("+<")).Take(8))
                {
                    var t = ResolveType(leshyTypeName);
                    if (t == null || !typeof(Component).IsAssignableFrom(t)) continue;
                    var live = UnityEngine.Object.FindObjectsOfType(t) as Component[];
                    if (live != null && live.Length > 0)
                        bag["leshy_live_" + t.Name] = live.Length;
                }

                // 4) Terrain — via reflection to avoid an extra assembly reference
                var terrainType = ResolveType("UnityEngine.Terrain");
                if (terrainType != null)
                {
                    var terrains = UnityEngine.Object.FindObjectsOfType(terrainType) as Component[];
                    bag["terrainCount"] = terrains?.Length ?? 0;
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

        private static string SafeStr(object v) { try { return v?.ToString() ?? "null"; } catch { return "ERR"; } }
    }
}
