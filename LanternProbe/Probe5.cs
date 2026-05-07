using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class Probe5
    {
        public static string ScanNearbyTorchLikeStuff()
        {
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                if (hero == null) return "no hero";

                var headProp = hero.GetType().GetProperty("Head");
                var headT = headProp?.GetValue(hero) as Transform;
                if (headT == null) return "no head";
                Vector3 origin = headT.position;

                // Scan ALL renderers in the scene within 5m of the player's head.
                var allRenderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
                var nearby = allRenderers
                    .Where(r => r != null && Vector3.Distance(r.transform.position, origin) < 5f)
                    .Select(r =>
                    {
                        var mesh = r.GetComponent<MeshFilter>()?.sharedMesh?.name
                                ?? (r as SkinnedMeshRenderer)?.sharedMesh?.name
                                ?? "";
                        return new
                        {
                            type = r.GetType().Name,
                            go = r.name,
                            mesh,
                            material = r.sharedMaterial?.name,
                            distance = Vector3.Distance(r.transform.position, origin),
                            worldPos = $"({r.transform.position.x:F2}, {r.transform.position.y:F2}, {r.transform.position.z:F2})",
                        };
                    })
                    .OrderBy(x => x.distance)
                    .ToList();

                // Specifically flag suspicious names.
                string[] keywords = { "torch", "lantern", "flame", "fire", "lamp", "candle", "light" };
                var suspicious = nearby
                    .Where(n =>
                        keywords.Any(k => n.go.ToLowerInvariant().Contains(k))
                        || keywords.Any(k => (n.mesh ?? "").ToLowerInvariant().Contains(k))
                        || keywords.Any(k => (n.material ?? "").ToLowerInvariant().Contains(k)))
                    .ToList();

                // Also list ALL Lights in the scene within 10m.
                var lights = UnityEngine.Object.FindObjectsOfType<Light>()
                    .Where(l => l != null && Vector3.Distance(l.transform.position, origin) < 10f)
                    .Select(l => new
                    {
                        go = l.gameObject.name,
                        type = l.type.ToString(),
                        intensity = l.intensity,
                        range = l.range,
                        color = $"rgba({l.color.r:F2},{l.color.g:F2},{l.color.b:F2})",
                        worldPos = $"({l.transform.position.x:F2},{l.transform.position.y:F2},{l.transform.position.z:F2})",
                        distance = Vector3.Distance(l.transform.position, origin),
                    })
                    .OrderBy(x => x.distance)
                    .ToList();

                // Also list all GameObjects within 3m whose name has those keywords (renderer or not).
                var allGo = UnityEngine.Object.FindObjectsOfType<Transform>()
                    .Where(t =>
                    {
                        if (t == null) return false;
                        if (Vector3.Distance(t.position, origin) > 3f) return false;
                        var n = t.name.ToLowerInvariant();
                        return keywords.Any(k => n.Contains(k));
                    })
                    .Select(t => new
                    {
                        name = t.name,
                        worldPos = $"({t.position.x:F2},{t.position.y:F2},{t.position.z:F2})",
                        distance = Vector3.Distance(t.position, origin),
                        components = t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToList(),
                        parent = t.parent?.name,
                    })
                    .OrderBy(x => x.distance)
                    .Take(50)
                    .ToList();

                return JsonConvert.SerializeObject(new
                {
                    heroHeadPos = $"({origin.x:F2},{origin.y:F2},{origin.z:F2})",
                    nearbyRendererCount = nearby.Count,
                    suspiciousRenderers = suspicious,
                    lightsWithin10m = lights,
                    keywordGameObjectsWithin3m = allGo,
                }, Formatting.Indented);
            }
            catch (Exception e) { return "ERR: " + e; }
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
    }
}
