using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class DaffodilCheck
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Find the daffodil mesh + any daffodil material
                var meshes = Resources.FindObjectsOfTypeAll<Mesh>()
                    .Where(m => m != null && m.name.IndexOf("daffodil", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(m => new { name = m.name, vertices = m.vertexCount, submeshes = m.subMeshCount })
                    .Distinct().ToList();
                bag["daffodilMeshes"] = meshes;

                var materials = Resources.FindObjectsOfTypeAll<Material>()
                    .Where(m => m != null && m.name.IndexOf("daffodil", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(m => new { name = m.name, shader = m.shader?.name })
                    .Distinct().ToList();
                bag["daffodilMaterials"] = materials;

                // Check live daffodil renderers in scene
                var liveDaffodils = UnityEngine.Object.FindObjectsOfType<MeshRenderer>()
                    .Where(r => r != null
                             && (r.GetComponent<MeshFilter>()?.sharedMesh?.name?.IndexOf("daffodil", StringComparison.OrdinalIgnoreCase) >= 0
                              || r.sharedMaterial?.name?.IndexOf("daffodil", StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(5)
                    .Select(r => new
                    {
                        go = r.name,
                        parent = r.transform.parent?.name,
                        mesh = r.GetComponent<MeshFilter>()?.sharedMesh?.name,
                        material = r.sharedMaterial?.name,
                        worldPos = $"({r.transform.position.x:F1},{r.transform.position.y:F1},{r.transform.position.z:F1})",
                    })
                    .ToList();
                bag["liveDaffodilsInScene"] = liveDaffodils;

                // Look at the rose for comparison — what shader/material setup does it use?
                var liveRose = UnityEngine.Object.FindObjectsOfType<MeshRenderer>()
                    .FirstOrDefault(r => r != null && r.sharedMaterial?.name == "Mat_Prop_Rose_01");
                if (liveRose != null)
                {
                    bag["sampleRoseMaterial"] = new
                    {
                        shader = liveRose.sharedMaterial.shader?.name,
                        textures = new[] { "_BaseColorMap", "_MainTex", "_NormalMap", "_MaskMap", "_BumpMap" }
                            .Where(p => liveRose.sharedMaterial.HasProperty(p))
                            .Select(p => new { prop = p, texName = (liveRose.sharedMaterial.GetTexture(p))?.name })
                            .ToList(),
                    };
                }

                // Maybe the daffodil material isn't loaded yet — check what scene materials we DO have
                var anyFlowerMat = Resources.FindObjectsOfTypeAll<Material>()
                    .Where(m => m != null && m.name.StartsWith("Mat_Prop_"))
                    .Select(m => m.name).Distinct().Take(40).ToList();
                bag["loadedPropMaterials_sample"] = anyFlowerMat;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }
    }
}
