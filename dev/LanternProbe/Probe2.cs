using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // Inspects every candidate template prefab so we know whether ANY of them have a
    // standard Unity MeshRenderer (i.e., something we can actually display attached to
    // the player), or if they're all DrakeRenderer-based.
    public static class Probe2
    {
        public static string ScanCandidates()
        {
            var report = new Dictionary<string, object>();

            try
            {
                // World.Services.Get<TemplatesProvider>().GetAllOfType<ItemTemplate>()
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var servicesProp = worldType.GetProperty("Services", BindingFlags.Public | BindingFlags.Static);
                var services = servicesProp.GetValue(null);
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var getMethod = services.GetType().GetMethods()
                    .First(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0)
                    .MakeGenericMethod(providerType);
                var provider = getMethod.Invoke(services, null);

                var itemTemplateType = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                var getAllOfTypeM = providerType.GetMethods()
                    .First(m => m.Name == "GetAllOfType" && m.IsGenericMethod)
                    .MakeGenericMethod(itemTemplateType);
                var allEnumerable = getAllOfTypeM.Invoke(provider, null) as System.Collections.IEnumerable;

                var matches = new List<object>();
                string[] needles = { "torch", "wyrdlantern", "lantern" };

                foreach (var t in allEnumerable)
                {
                    if (t == null) continue;
                    var nameProp = t.GetType().GetProperty("ItemName");
                    string name = nameProp?.GetValue(t) as string;
                    if (string.IsNullOrEmpty(name)) continue;
                    var lname = name.ToLowerInvariant();
                    if (!needles.Any(n => lname.Contains(n))) continue;

                    var prefabRefProp = t.GetType().GetProperty("PickablePrefab");
                    var prefabRef = prefabRefProp?.GetValue(t);

                    bool isSet = false;
                    string assetGuid = null;
                    GameObject prefab = null;
                    string prefabError = null;
                    try
                    {
                        if (prefabRef != null)
                        {
                            var isSetProp = prefabRef.GetType().GetProperty("IsSet");
                            isSet = isSetProp != null && (bool)isSetProp.GetValue(prefabRef);

                            // Try .Get() to materialize ARAssetReference, then LoadAsset<GameObject>().WaitForCompletion()
                            var getM = prefabRef.GetType().GetMethod("Get");
                            var arRef = getM?.Invoke(prefabRef, null);
                            if (arRef != null)
                            {
                                var loadM = arRef.GetType().GetMethods().FirstOrDefault(m => m.Name == "LoadAsset" && m.IsGenericMethod);
                                if (loadM != null)
                                {
                                    var handle = loadM.MakeGenericMethod(typeof(GameObject)).Invoke(arRef, null);
                                    // Try to get the .Result of the async handle.
                                    var waitM = handle.GetType().GetMethod("WaitForCompletion");
                                    if (waitM != null) prefab = waitM.Invoke(handle, null) as GameObject;
                                    else prefab = handle.GetType().GetProperty("Result")?.GetValue(handle) as GameObject;
                                }
                            }
                        }
                    }
                    catch (Exception e) { prefabError = e.GetType().Name + ": " + e.Message; }

                    matches.Add(new Dictionary<string, object>
                    {
                        ["name"] = name,
                        ["templateType"] = t.GetType().Name,
                        ["isSet"] = isSet,
                        ["prefabError"] = prefabError,
                        ["prefab"] = prefab == null ? null : new Dictionary<string, object>
                        {
                            ["prefabName"] = prefab.name,
                            ["topLevelComponents"] = prefab.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName).ToList(),
                            ["unityMeshRenderers"] = prefab.GetComponentsInChildren<MeshRenderer>(true).Select(r => new Dictionary<string, object>
                            {
                                ["go"] = r.name,
                                ["mesh"] = r.GetComponent<MeshFilter>()?.sharedMesh?.name,
                                ["material"] = r.sharedMaterial?.name,
                            }).ToList(),
                            ["skinnedRenderers"] = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => new Dictionary<string, object>
                            {
                                ["go"] = r.name,
                                ["mesh"] = r.sharedMesh?.name,
                            }).ToList(),
                            ["allRenderers"] = prefab.GetComponentsInChildren<Renderer>(true).Select(r => r.GetType().Name + " on " + r.name).ToList(),
                            ["drakeMeshRenderers"] = prefab.GetComponentsInChildren<Component>(true)
                                .Where(c => c != null && c.GetType().FullName.Contains("DrakeMeshRenderer"))
                                .Select(c => DescribeDrake(c))
                                .ToList(),
                        },
                    });
                }

                report["candidates"] = matches;
            }
            catch (Exception e)
            {
                report["err"] = e.ToString();
            }

            return JsonConvert.SerializeObject(report, Formatting.Indented);
        }

        private static object DescribeDrake(Component c)
        {
            var bag = new Dictionary<string, object>();
            bag["go"] = c.name;
            bag["type"] = c.GetType().FullName;
            // Reflect over its public fields/properties for mesh / material references.
            foreach (var f in c.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                try { var v = f.GetValue(c); bag[f.Name] = v?.ToString() ?? "null"; if (v is UnityEngine.Object uo) bag[f.Name] = uo.name + " (" + uo.GetType().Name + ")"; }
                catch { }
            }
            foreach (var p in c.GetType().GetProperties())
            {
                if (p.GetIndexParameters().Length > 0) continue;
                try { var v = p.GetValue(c); bag["prop:" + p.Name] = v?.ToString() ?? "null"; if (v is UnityEngine.Object uo) bag["prop:" + p.Name] = uo.name + " (" + uo.GetType().Name + ")"; }
                catch { }
            }
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
    }
}
