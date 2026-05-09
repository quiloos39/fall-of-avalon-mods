using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class GlowFlowerScout
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Search loaded meshes / materials / textures for "glow", "wyrd", "magic", "bioluminescent",
                // "emissive", "shrine" — anything that smells magical + flower-related.
                string[] kw = { "glow", "wyrd", "wisp", "magic", "shimmer", "spirit", "ethereal", "fae", "fairy" };

                var glowMeshes = Resources.FindObjectsOfTypeAll<Mesh>()
                    .Where(m => m != null && kw.Any(k => m.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(m => $"{m.name}  vertices={m.vertexCount}").Distinct().Take(30).ToList();
                bag["glow_meshes"] = glowMeshes;

                var glowMaterials = Resources.FindObjectsOfTypeAll<Material>()
                    .Where(m => m != null && kw.Any(k => m.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(m => $"{m.name}  shader={m.shader?.name}").Distinct().Take(30).ToList();
                bag["glow_materials"] = glowMaterials;

                // Live renderers in scene with glowy names
                var liveGlow = UnityEngine.Object.FindObjectsOfType<Renderer>()
                    .Where(r => r != null && kw.Any(k =>
                        (r.name?.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (r.GetComponent<MeshFilter>()?.sharedMesh?.name?.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (r.sharedMaterial?.name?.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)))
                    .Take(15)
                    .Select(r => new
                    {
                        type = r.GetType().Name,
                        go = r.name,
                        mesh = r.GetComponent<MeshFilter>()?.sharedMesh?.name,
                        material = r.sharedMaterial?.name,
                    })
                    .ToList();
                bag["live_glowRenderers"] = liveGlow;

                // Specifically look for: VFXGraph_* with flower/petal/glow/magic in their full path
                var vfxGraphs = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .Where(t => t != null && t.FullName != null && t.FullName.Contains("VFX")).Take(30).Select(t => t.FullName).ToList();
                bag["vfx_types_sample"] = vfxGraphs;

                // ParticleSystem prefabs with flower/firefly/petal in name
                var particles = Resources.FindObjectsOfTypeAll<UnityEngine.GameObject>()
                    .Where(g => g != null && kw.Concat(new[] { "petal", "firefly", "flower" }).Any(k => g.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(g => g.name).Distinct().Take(30).ToList();
                bag["go_glowOrFlower_named"] = particles;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }
    }
}
