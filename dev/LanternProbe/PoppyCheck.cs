using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class PoppyCheck
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Mesh side
                var poppyMeshes = Resources.FindObjectsOfTypeAll<Mesh>()
                    .Where(m => m != null && m.name.IndexOf("poppy", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(m => new { name = m.name, vertices = m.vertexCount, submeshes = m.subMeshCount }).ToList();
                bag["poppyMeshes"] = poppyMeshes;

                // Material side
                var poppyMats = Resources.FindObjectsOfTypeAll<Material>()
                    .Where(m => m != null && m.name.IndexOf("poppy", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(m => new { name = m.name, shader = m.shader?.name }).ToList();
                bag["poppyMaterials"] = poppyMats;

                // Inspect glowing material's properties
                var glowMat = Resources.FindObjectsOfTypeAll<Material>()
                    .FirstOrDefault(m => m != null && m.name == "Mat_flower_common_poppy_01_Glowing");
                if (glowMat != null)
                {
                    var props = new Dictionary<string, object>();
                    foreach (var pname in new[] { "_BaseColorMap", "_NormalMap", "_MaskMap",
                                                  "_BaseColor", "_EmissionColor", "_EmissiveColor",
                                                  "_EmissionMap", "_EmissiveColorMap" })
                    {
                        if (!glowMat.HasProperty(pname)) continue;
                        try
                        {
                            if (pname.EndsWith("Map"))
                            {
                                var t = glowMat.GetTexture(pname);
                                props[pname] = t?.name + " (" + t?.width + "x" + t?.height + ")";
                            }
                            else
                            {
                                var c = glowMat.GetColor(pname);
                                props[pname] = $"({c.r:F2},{c.g:F2},{c.b:F2},{c.a:F2})";
                            }
                        }
                        catch { props[pname] = "ERR"; }
                    }
                    bag["glowMatInspection"] = props;
                }

                // Live scene poppies
                var livePoppy = UnityEngine.Object.FindObjectsOfType<MeshRenderer>()
                    .Where(r => r != null
                             && (r.GetComponent<MeshFilter>()?.sharedMesh?.name?.IndexOf("poppy", StringComparison.OrdinalIgnoreCase) >= 0
                              || r.sharedMaterial?.name?.IndexOf("poppy", StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(5)
                    .Select(r => new
                    {
                        go = r.name,
                        mesh = r.GetComponent<MeshFilter>()?.sharedMesh?.name,
                        material = r.sharedMaterial?.name,
                        components = r.gameObject.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToList(),
                    })
                    .ToList();
                bag["livePoppyRenderers"] = livePoppy;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }
    }
}
