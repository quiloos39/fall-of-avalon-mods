using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class BroadFlowerScan
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var allRends = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                bag["totalSceneRenderers"] = allRends.Length;

                // Distinct flower-like material+mesh pairs anywhere in the scene
                string[] flowerKw = { "flower", "poppy", "daffodil", "petal", "bloom", "rose", "carnation", "milkweed", "rosemary", "burdock", "mandrake", "jimsonweed", "wild_rose", "wolfsbane" };
                var flowerLike = allRends
                    .Where(r => r != null)
                    .Where(r =>
                    {
                        var mat = r.sharedMaterial?.name?.ToLowerInvariant() ?? "";
                        var mesh = r.GetComponent<MeshFilter>()?.sharedMesh?.name?.ToLowerInvariant() ?? "";
                        return flowerKw.Any(k => mat.Contains(k) || mesh.Contains(k));
                    })
                    .GroupBy(r => (r.GetComponent<MeshFilter>()?.sharedMesh?.name ?? "?") + " | " + (r.sharedMaterial?.name ?? "?"))
                    .OrderByDescending(g => g.Count())
                    .Select(g => new { key = g.Key, count = g.Count() })
                    .Take(30).ToList();
                bag["flowerLike_anywhere"] = flowerLike;

                // Distinct emissive materials anywhere in scene
                var emitMats = new HashSet<string>();
                var distinctEmit = new List<object>();
                foreach (var r in allRends)
                {
                    var m = r.sharedMaterial;
                    if (m == null) continue;
                    if (!m.HasProperty("_EmissiveColor")) continue;
                    var c = m.GetColor("_EmissiveColor");
                    if (c.r + c.g + c.b < 0.05f) continue;
                    if (emitMats.Add(m.name))
                    {
                        distinctEmit.Add(new
                        {
                            material = m.name,
                            emissive = $"({c.r:F2},{c.g:F2},{c.b:F2})",
                            mesh = r.GetComponent<MeshFilter>()?.sharedMesh?.name,
                        });
                    }
                }
                bag["distinctEmissiveMaterials_anywhere"] = distinctEmit.Take(40).ToList();

                // Verify our chosen poppy mesh + material are still in memory
                var poppyMesh = Resources.FindObjectsOfTypeAll<Mesh>()
                    .FirstOrDefault(m => m != null && m.name.IndexOf("poppy", StringComparison.OrdinalIgnoreCase) >= 0);
                var poppyMat = Resources.FindObjectsOfTypeAll<Material>()
                    .FirstOrDefault(m => m != null && m.name == "Mat_flower_common_poppy_01_Glowing");
                bag["poppyMeshStillResolvable"] = poppyMesh?.name;
                bag["poppyMaterialStillResolvable"] = poppyMat?.name;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }
    }
}
