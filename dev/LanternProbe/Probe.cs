using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class Probe
    {
        // /eval entry point. Runs on main thread.
        public static string Run(System.Threading.CancellationToken ct)
        {
            var report = Inspect();
            return JsonConvert.SerializeObject(report, Formatting.Indented);
        }

        // /dlls/exec entry point. No args, returns JSON string.
        public static string Inspect_Json()
        {
            var report = Inspect();
            return JsonConvert.SerializeObject(report, Formatting.Indented);
        }

        private static object Inspect()
        {
            var bag = new Dictionary<string, object>();

            // 1) Find LanternController via FindObjectsOfType. It's a MonoBehaviour added to the
            //    Plugin's gameObject by Lantern.Plugin.Awake().
            var allMb = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
            MonoBehaviour ctrl = allMb.FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");

            bag["lanternControllerFound"] = ctrl != null;
            if (ctrl == null)
            {
                bag["candidateMonoBehaviourCount"] = allMb.Length;
                bag["lanternTypes"] = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name == "Lantern")
                    .SelectMany(a => SafeGetTypes(a))
                    .Select(t => t.FullName)
                    .ToList();
                return bag;
            }

            var ctrlType = ctrl.GetType();
            bag["controllerType"] = ctrlType.FullName;
            bag["controllerEnabled"] = ctrl.enabled;
            bag["controllerGameObjectActive"] = ctrl.gameObject.activeInHierarchy;

            // 2) Read every field on the controller — public + private.
            var fields = new Dictionary<string, object>();
            foreach (var f in ctrlType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                try
                {
                    var v = f.GetValue(ctrl);
                    fields[f.Name] = Describe(v);
                }
                catch (Exception e)
                {
                    fields[f.Name] = "READ_ERR: " + e.Message;
                }
            }
            // and properties
            foreach (var p in ctrlType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                try
                {
                    var v = p.GetValue(ctrl);
                    fields["prop:" + p.Name] = Describe(v);
                }
                catch (Exception e)
                {
                    fields["prop:" + p.Name] = "READ_ERR: " + e.Message;
                }
            }
            bag["controllerState"] = fields;

            // 3) Pull _root and _modelInstance specifically and inspect their hierarchy + transforms.
            var rootField = ctrlType.GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance);
            var modelField = ctrlType.GetField("_modelInstance", BindingFlags.NonPublic | BindingFlags.Instance);
            var attachedField = ctrlType.GetField("_attachedTo", BindingFlags.NonPublic | BindingFlags.Instance);
            var lightField = ctrlType.GetField("_light", BindingFlags.NonPublic | BindingFlags.Instance);
            var hdField = ctrlType.GetField("_hdData", BindingFlags.NonPublic | BindingFlags.Instance);

            var root = rootField?.GetValue(ctrl) as GameObject;
            var model = modelField?.GetValue(ctrl) as GameObject;
            var attached = attachedField?.GetValue(ctrl) as Transform;
            var light = lightField?.GetValue(ctrl) as Light;

            bag["root"] = DescribeGameObject(root);
            bag["modelInstance"] = DescribeGameObject(model);
            bag["attachBone"] = DescribeTransform(attached);
            bag["lightComponent"] = DescribeLight(light);

            // 4) HDAdditionalLightData via reflection (don't want to take a HDRP reference).
            object hd = hdField?.GetValue(ctrl);
            if (hd != null)
            {
                var hdInfo = new Dictionary<string, object>();
                hdInfo["type"] = hd.GetType().FullName;
                hdInfo["enabled"] = (hd as Behaviour)?.enabled;
                hdInfo["gameObjectActive"] = (hd as Component)?.gameObject?.activeInHierarchy;
                foreach (var pname in new[] { "intensity", "range", "color", "affectsVolumetric", "lightUnit", "shadowDimmer", "volumetricDimmer" })
                {
                    var pi = hd.GetType().GetProperty(pname);
                    if (pi != null) try { hdInfo[pname] = SafeStr(pi.GetValue(hd)); } catch (Exception e) { hdInfo[pname] = "ERR " + e.Message; }
                }
                bag["hdAdditionalLightData"] = hdInfo;
            }
            else bag["hdAdditionalLightData"] = null;

            // 5) Walk the model tree to find any renderers and report THEIR world positions.
            if (model != null)
            {
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                bag["modelRenderers"] = renderers.Select(r => new Dictionary<string, object>
                {
                    ["name"] = r.name,
                    ["type"] = r.GetType().Name,
                    ["enabled"] = r.enabled,
                    ["gameObjectActive"] = r.gameObject.activeInHierarchy,
                    ["worldPosition"] = SafeStr(r.transform.position),
                    ["localPosition"] = SafeStr(r.transform.localPosition),
                    ["lossyScale"] = SafeStr(r.transform.lossyScale),
                    ["boundsCenter"] = SafeStr(r.bounds.center),
                    ["boundsSize"] = SafeStr(r.bounds.size),
                    ["materialCount"] = r.sharedMaterials?.Length ?? 0,
                    ["sharedMesh"] = (r as MeshRenderer) != null ? r.GetComponent<MeshFilter>()?.sharedMesh?.name : null,
                }).ToList();

                // Also walk the full GameObject tree and list every component on every node.
                bag["modelHierarchy"] = WalkHierarchy(model.transform, depth: 0, maxDepth: 5);
            }

            // 6) Plugin.Cfg current values.
            var pluginType = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Lantern")
                ?.GetType("Lantern.Plugin");
            if (pluginType != null)
            {
                var cfgField = pluginType.GetField("Cfg", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                var cfg = cfgField?.GetValue(null);
                if (cfg != null)
                {
                    var cfgState = new Dictionary<string, object>();
                    foreach (var p in cfg.GetType().GetProperties())
                    {
                        try
                        {
                            var entry = p.GetValue(cfg);
                            var valueProp = entry?.GetType().GetProperty("Value");
                            cfgState[p.Name] = valueProp?.GetValue(entry);
                        }
                        catch (Exception e) { cfgState[p.Name] = "ERR " + e.Message; }
                    }
                    bag["liveCfg"] = cfgState;
                }
            }

            // 7) Hero / attach points.
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var currentProp = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                var hero = currentProp?.GetValue(null);
                bag["heroFound"] = hero != null;
                if (hero != null)
                {
                    var heroBag = new Dictionary<string, object>();
                    foreach (var pname in new[] { "Hips", "Torso", "Head", "MainHand", "OffHand" })
                    {
                        var p = hero.GetType().GetProperty(pname);
                        var t = p?.GetValue(hero) as Transform;
                        heroBag[pname] = DescribeTransform(t);
                    }
                    bag["hero"] = heroBag;
                }
            }
            catch (Exception e)
            {
                bag["heroErr"] = e.ToString();
            }

            return bag;
        }

        // ──────────── helpers ────────────

        private static IEnumerable<Type> SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch { return Array.Empty<Type>(); }
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

        private static object Describe(object v)
        {
            if (v == null) return null;
            if (v is GameObject go) return DescribeGameObject(go);
            if (v is Transform t) return DescribeTransform(t);
            if (v is Light l) return DescribeLight(l);
            if (v is Component c) return new Dictionary<string, object>
            {
                ["type"] = c.GetType().FullName,
                ["name"] = c.name,
                ["gameObjectActive"] = c.gameObject?.activeInHierarchy,
            };
            if (v is string || v.GetType().IsPrimitive || v is decimal) return v;
            if (v is Vector3 || v is Vector2 || v is Quaternion || v is Color) return SafeStr(v);
            return SafeStr(v);
        }

        private static Dictionary<string, object> DescribeGameObject(GameObject go)
        {
            if (go == null) return null;
            return new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["activeInHierarchy"] = go.activeInHierarchy,
                ["activeSelf"] = go.activeSelf,
                ["worldPos"] = SafeStr(go.transform.position),
                ["localPos"] = SafeStr(go.transform.localPosition),
                ["worldRotEuler"] = SafeStr(go.transform.eulerAngles),
                ["lossyScale"] = SafeStr(go.transform.lossyScale),
                ["parent"] = go.transform.parent?.name,
                ["childCount"] = go.transform.childCount,
                ["componentTypes"] = go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName).ToList(),
            };
        }

        private static Dictionary<string, object> DescribeTransform(Transform t)
        {
            if (t == null) return null;
            return new Dictionary<string, object>
            {
                ["name"] = t.name,
                ["worldPos"] = SafeStr(t.position),
                ["worldRotEuler"] = SafeStr(t.eulerAngles),
                ["parent"] = t.parent?.name,
            };
        }

        private static Dictionary<string, object> DescribeLight(Light l)
        {
            if (l == null) return null;
            return new Dictionary<string, object>
            {
                ["enabled"] = l.enabled,
                ["gameObjectActive"] = l.gameObject?.activeInHierarchy,
                ["type"] = l.type.ToString(),
                ["intensity"] = l.intensity,
                ["range"] = l.range,
                ["color"] = SafeStr(l.color),
                ["worldPos"] = SafeStr(l.transform.position),
                ["shadows"] = l.shadows.ToString(),
            };
        }

        private static List<object> WalkHierarchy(Transform t, int depth, int maxDepth)
        {
            var list = new List<object>();
            if (t == null || depth > maxDepth) return list;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = c.name,
                    ["active"] = c.gameObject.activeInHierarchy,
                    ["worldPos"] = SafeStr(c.position),
                    ["localPos"] = SafeStr(c.localPosition),
                    ["components"] = c.GetComponents<Component>().Where(x => x != null).Select(x => x.GetType().FullName).ToList(),
                    ["children"] = WalkHierarchy(c, depth + 1, maxDepth),
                });
            }
            return list;
        }

        private static string SafeStr(object v)
        {
            try
            {
                if (v is Vector3 v3) return $"({v3.x:F3}, {v3.y:F3}, {v3.z:F3})";
                if (v is Vector2 v2) return $"({v2.x:F3}, {v2.y:F3})";
                if (v is Quaternion q) return $"({q.x:F3}, {q.y:F3}, {q.z:F3}, {q.w:F3})";
                if (v is Color col) return $"rgba({col.r:F2}, {col.g:F2}, {col.b:F2}, {col.a:F2})";
                return v?.ToString();
            }
            catch (Exception e) { return "STR_ERR " + e.Message; }
        }
    }
}
