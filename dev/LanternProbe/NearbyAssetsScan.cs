using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class NearbyAssetsScan
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
                Vector3 playerPos = hero != null ? (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero) : Vector3.zero;
                bag["playerPos"] = $"({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})";

                var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();

                // 1) All renderers within 30m, grouped by material name, counts.
                var nearby = renderers
                    .Where(r => r != null && Vector3.Distance(r.transform.position, playerPos) < 30f)
                    .ToList();
                bag["renderersWithin30m"] = nearby.Count;

                // 2) Flower / vegetation / glowy materials within 30m
                var flowerLike = nearby
                    .Where(r =>
                    {
                        var mat = r.sharedMaterial?.name?.ToLowerInvariant() ?? "";
                        var mesh = r.GetComponent<MeshFilter>()?.sharedMesh?.name?.ToLowerInvariant() ?? "";
                        return mat.Contains("flower") || mat.Contains("poppy") || mat.Contains("daffodil")
                            || mat.Contains("herb") || mat.Contains("petal") || mat.Contains("bloom")
                            || mesh.Contains("flower") || mesh.Contains("poppy") || mesh.Contains("daffodil")
                            || mat.Contains("glow") || mat.Contains("wyrd") || mat.Contains("emissive");
                    })
                    .GroupBy(r => r.sharedMaterial?.name + " | " + r.GetComponent<MeshFilter>()?.sharedMesh?.name)
                    .OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        key = g.Key,
                        count = g.Count(),
                        nearestDist = g.Min(r => Vector3.Distance(r.transform.position, playerPos)),
                        sampleGo = g.First().name,
                    })
                    .ToList();
                bag["flowerLikeMaterials_nearby"] = flowerLike;

                // 3) Materials whose _EmissiveColor is non-trivial — i.e. they actually emit.
                var emissiveMats = new HashSet<string>();
                var emittingNearby = new List<object>();
                foreach (var r in nearby)
                {
                    var m = r.sharedMaterial;
                    if (m == null) continue;
                    if (!m.HasProperty("_EmissiveColor")) continue;
                    var c = m.GetColor("_EmissiveColor");
                    float lum = c.r + c.g + c.b;
                    if (lum < 0.05f) continue;
                    if (emissiveMats.Add(m.name))
                    {
                        emittingNearby.Add(new
                        {
                            material = m.name,
                            emissive = $"({c.r:F2},{c.g:F2},{c.b:F2})",
                            mesh = r.GetComponent<MeshFilter>()?.sharedMesh?.name,
                            distance = Vector3.Distance(r.transform.position, playerPos),
                            shader = m.shader?.name,
                        });
                    }
                }
                bag["distinctEmissiveMaterials_nearby"] = emittingNearby.Take(40).ToList();

                // 4) Find any LIVE poppy in scene at all (broaden search)
                var anyPoppy = renderers
                    .Where(r => r != null && (r.sharedMaterial?.name?.IndexOf("poppy", StringComparison.OrdinalIgnoreCase) >= 0
                                          || r.GetComponent<MeshFilter>()?.sharedMesh?.name?.IndexOf("poppy", StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(5)
                    .Select(r => new
                    {
                        name = r.name,
                        mesh = r.GetComponent<MeshFilter>()?.sharedMesh?.name,
                        material = r.sharedMaterial?.name,
                        distance = Vector3.Distance(r.transform.position, playerPos),
                        worldPos = $"({r.transform.position.x:F1},{r.transform.position.y:F1},{r.transform.position.z:F1})",
                    })
                    .ToList();
                bag["livePoppiesAnywhere"] = anyPoppy;
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
    }
}
