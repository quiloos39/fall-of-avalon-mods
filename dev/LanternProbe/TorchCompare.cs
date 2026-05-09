using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class TorchCompare
    {
        // Find the equipped torch in either hand and dump EVERY component on it + every child,
        // with special focus on Light + HDAdditionalLightData + VisualEffect + particle systems.
        // Then dump our Lantern_Plugin GameObject's setup. Side-by-side.
        public static string Run()
        {
            var bag = new Dictionary<string, object>();

            try
            {
                // ── 1. Find the torch in the hand bones ──────────────────────────────
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                if (hero == null) { bag["err"] = "no hero"; return Ser(bag); }

                Transform torchRoot = null;
                string foundIn = null;
                foreach (var bone in new[] { "MainHand", "OffHand", "RightHand", "LeftHand", "RightHandSlot", "LeftHandSlot" })
                {
                    var prop = hero.GetType().GetProperty(bone);
                    var t = prop?.GetValue(hero) as Transform;
                    if (t == null) continue;

                    // Walk the hand's children recursively, find one whose name contains "torch".
                    var torchHit = AllDescendants(t).FirstOrDefault(c =>
                        c.name.IndexOf("torch", StringComparison.OrdinalIgnoreCase) >= 0
                        || c.name.IndexOf("eqtorch", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (torchHit != null) { torchRoot = torchHit; foundIn = bone; break; }
                }

                bag["torchFoundIn"] = foundIn;
                if (torchRoot == null)
                {
                    // Fallback: scene-wide search for anything torch-named within 3m of hero.
                    var headProp = hero.GetType().GetProperty("Head");
                    var head = (headProp?.GetValue(hero) as Transform)?.position ?? Vector3.zero;
                    var candidate = UnityEngine.Object.FindObjectsOfType<Transform>()
                        .Where(t => t != null
                            && t.name.IndexOf("torch", StringComparison.OrdinalIgnoreCase) >= 0
                            && Vector3.Distance(t.position, head) < 3f)
                        .OrderBy(t => Vector3.Distance(t.position, head))
                        .FirstOrDefault();
                    if (candidate != null) { torchRoot = candidate; bag["torchFoundIn"] = "scene-near-head: " + candidate.parent?.name; }
                }

                if (torchRoot == null)
                {
                    bag["torch"] = "(not found)";
                }
                else
                {
                    // Walk up to find the equipable wrapper (the parent that holds it under the bone).
                    Transform wrap = torchRoot;
                    while (wrap.parent != null && !wrap.parent.name.ToLowerInvariant().EndsWith("hand")
                                                && !wrap.parent.name.ToLowerInvariant().EndsWith("handslot")
                                                && !wrap.parent.name.ToLowerInvariant().Contains("equipable"))
                    {
                        wrap = wrap.parent;
                    }
                    // Use the wrapper itself if its parent is a hand slot.
                    if (wrap.parent != null
                        && (wrap.parent.name.ToLowerInvariant().EndsWith("hand")
                         || wrap.parent.name.ToLowerInvariant().EndsWith("handslot")))
                    {
                        // wrap is the top-level under the bone
                    }
                    bag["torch"] = DescribeTreeWithLightingDetail(wrap, depth: 0, maxDepth: 8);
                }

                // ── 2. Dump our Lantern_Plugin GameObject setup ──────────────────────
                var lanternRoot = UnityEngine.Object.FindObjectsOfType<Transform>()
                    .FirstOrDefault(t => t != null && t.name == "Lantern_Plugin" && t.parent == null);
                if (lanternRoot == null)
                {
                    bag["ours"] = "(not found — toggle the lantern on first)";
                }
                else
                {
                    bag["ours"] = DescribeTreeWithLightingDetail(lanternRoot, depth: 0, maxDepth: 8);
                }

                // ── 3. Quick diff hint ───────────────────────────────────────────────
                bag["hint"] = "Compare HDAdditionalLightData properties on the torch's Light vs ours: lightUnit, intensity, radius, emissionRadius, fadeDistance, useShadowMatte, etc.";
            }
            catch (Exception e) { bag["err"] = e.ToString(); }

            return Ser(bag);
        }

        private static IEnumerable<Transform> AllDescendants(Transform t)
        {
            yield return t;
            for (int i = 0; i < t.childCount; i++)
                foreach (var d in AllDescendants(t.GetChild(i))) yield return d;
        }

        private static object DescribeTreeWithLightingDetail(Transform t, int depth, int maxDepth)
        {
            if (t == null || depth > maxDepth) return null;
            var bag = new Dictionary<string, object>
            {
                ["name"] = t.name,
                ["worldPos"] = SafeStr(t.position),
                ["activeInHierarchy"] = t.gameObject.activeInHierarchy,
                ["components"] = new List<object>(),
            };

            var compList = (List<object>)bag["components"];
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var compBag = new Dictionary<string, object>
                {
                    ["type"] = c.GetType().FullName,
                };

                if (c is Light l)
                {
                    compBag["enabled"] = l.enabled;
                    compBag["lightType"] = l.type.ToString();
                    compBag["intensity"] = l.intensity;
                    compBag["range"] = l.range;
                    compBag["color"] = SafeStr(l.color);
                    compBag["shadows"] = l.shadows.ToString();
                    compBag["spotAngle"] = l.spotAngle;
                    compBag["bounceIntensity"] = l.bounceIntensity;
                    compBag["renderMode"] = l.renderMode.ToString();
                    compBag["cullingMask"] = l.cullingMask;
                    compBag["cookie"] = l.cookie?.name;
                    // (lightmapBakeType lives in UnityEngine.AI module — skip)
                }
                else if (c.GetType().FullName == "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData")
                {
                    compBag["enabled"] = (c as Behaviour)?.enabled;
                    foreach (var pname in new[]
                    {
                        // Common HDRP light properties we care about.
                        "intensity", "range", "color", "lightUnit", "luxAtDistance",
                        "affectsVolumetric", "affectsDiffuse", "affectsSpecular",
                        "shapeRadius", "shapeWidth", "shapeHeight",
                        "shadowDimmer", "volumetricDimmer", "volumetricShadowDimmer",
                        "areaLightCookie", "lightCookieSize",
                        "fadeDistance", "shadowFadeDistance",
                        "applyRangeAttenuation", "useScreenSpaceShadows",
                        "interactsWithSky", "useShadowMatte",
                        "EnableShadows", "lightlayersMask",
                    })
                    {
                        var pi = c.GetType().GetProperty(pname, BindingFlags.Public | BindingFlags.Instance);
                        if (pi != null && pi.CanRead && pi.GetIndexParameters().Length == 0)
                        {
                            try { compBag["hd:" + pname] = SafeStr(pi.GetValue(c)); }
                            catch (Exception e) { compBag["hd:" + pname] = "ERR " + e.GetType().Name; }
                        }
                    }
                }
                else if (c.GetType().FullName == "UnityEngine.VFX.VisualEffect")
                {
                    var enabledP = c.GetType().GetProperty("enabled");
                    var assetP = c.GetType().GetProperty("visualEffectAsset");
                    compBag["enabled"] = enabledP?.GetValue(c);
                    var asset = assetP?.GetValue(c) as UnityEngine.Object;
                    compBag["visualEffectAsset"] = asset?.name;
                }
                else if (c.GetType().FullName == "UnityEngine.ParticleSystem")
                {
                    var psType = c.GetType();
                    compBag["isPlaying"] = psType.GetProperty("isPlaying")?.GetValue(c);
                    compBag["particleCount"] = psType.GetProperty("particleCount")?.GetValue(c);
                }
                else
                {
                    // Just type name is enough.
                }
                compList.Add(compBag);
            }

            // Children
            var children = new List<object>();
            for (int i = 0; i < t.childCount; i++)
            {
                children.Add(DescribeTreeWithLightingDetail(t.GetChild(i), depth + 1, maxDepth));
            }
            bag["children"] = children;
            return bag;
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

        private static string SafeStr(object v)
        {
            try
            {
                if (v is Vector3 v3) return $"({v3.x:F3}, {v3.y:F3}, {v3.z:F3})";
                if (v is Color col) return $"rgba({col.r:F2}, {col.g:F2}, {col.b:F2}, {col.a:F2})";
                return v?.ToString();
            }
            catch (Exception e) { return "STR_ERR " + e.Message; }
        }

        private static string Ser(object o) => JsonConvert.SerializeObject(o, Formatting.Indented);
    }
}
