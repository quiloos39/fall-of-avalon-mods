using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class Probe4
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();

            try
            {
                // 1) What does OUR spawned model currently look like? (renderers, child tree)
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");
                if (ctrl != null)
                {
                    var t = ctrl.GetType();
                    var modelF = t.GetField("_modelInstance", BindingFlags.NonPublic | BindingFlags.Instance);
                    var rootF = t.GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance);
                    var matched = t.GetField("_matchedTemplateName", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrl) as string;
                    var cachedPrefab = t.GetField("_cachedPrefab", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrl) as GameObject;
                    var model = modelF.GetValue(ctrl) as GameObject;
                    var root = rootF.GetValue(ctrl) as GameObject;

                    bag["matchedTemplate"] = matched;
                    bag["cachedPrefabName"] = cachedPrefab?.name;
                    bag["cachedPrefabComponents"] = cachedPrefab?.GetComponentsInChildren<Component>(true)
                        .Where(c => c != null)
                        .Select(c => $"{c.name}:{c.GetType().FullName}")
                        .ToList();

                    if (model != null)
                    {
                        bag["modelInstance"] = new Dictionary<string, object>
                        {
                            ["name"] = model.name,
                            ["activeInHierarchy"] = model.activeInHierarchy,
                            ["worldPos"] = SafeStr(model.transform.position),
                            ["lossyScale"] = SafeStr(model.transform.lossyScale),
                            ["allComponentsRecursive"] = model.GetComponentsInChildren<Component>(true)
                                .Where(c => c != null)
                                .Select(c => $"{c.gameObject.name}:{c.GetType().FullName}")
                                .ToList(),
                            ["unityRenderers"] = model.GetComponentsInChildren<Renderer>(true)
                                .Select(r => new Dictionary<string, object>
                                {
                                    ["go"] = r.name,
                                    ["type"] = r.GetType().FullName,
                                    ["enabled"] = r.enabled,
                                    ["worldPos"] = SafeStr(r.transform.position),
                                    ["bounds"] = SafeStr(r.bounds),
                                    ["mesh"] = (r as MeshRenderer)?.GetComponent<MeshFilter>()?.sharedMesh?.name,
                                }).ToList(),
                        };
                    }
                    else bag["modelInstance"] = "(null)";
                }

                // 2) What does Hero have equipped in MainHand / OffHand?
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                if (hero != null)
                {
                    var equipBag = new Dictionary<string, object>();
                    foreach (var prop in new[] { "HeroItems", "Inventory" })
                    {
                        var p = hero.GetType().GetProperty(prop);
                        var val = p?.GetValue(hero);
                        equipBag[prop + "_type"] = val?.GetType().FullName;
                    }

                    var heroItems = hero.GetType().GetProperty("HeroItems")?.GetValue(hero);
                    if (heroItems != null)
                    {
                        // EquippedItem(EquipmentSlotType slot)
                        var equippedItemMethod = heroItems.GetType().GetMethods()
                            .FirstOrDefault(m => m.Name == "EquippedItem" && m.GetParameters().Length == 1);
                        if (equippedItemMethod != null)
                        {
                            var slotType = equippedItemMethod.GetParameters()[0].ParameterType;
                            // Enumerate slot enum
                            foreach (var slotName in Enum.GetNames(slotType))
                            {
                                try
                                {
                                    var slotVal = Enum.Parse(slotType, slotName);
                                    var item = equippedItemMethod.Invoke(heroItems, new object[] { slotVal });
                                    if (item != null)
                                    {
                                        var nameProp = item.GetType().GetProperty("DisplayName") ?? item.GetType().GetProperty("Name") ?? item.GetType().GetProperty("ItemName");
                                        var templateProp = item.GetType().GetProperty("Template");
                                        equipBag["slot:" + slotName] = new Dictionary<string, object>
                                        {
                                            ["display"] = nameProp?.GetValue(item)?.ToString(),
                                            ["templateType"] = templateProp?.GetValue(item)?.GetType().FullName,
                                        };
                                    }
                                }
                                catch (Exception e) { equipBag["slot:" + slotName + "_err"] = e.GetType().Name + ": " + e.Message; }
                            }
                        }
                        else equipBag["EquippedItemMethod"] = "(not found - listing methods)";

                        equipBag["heroItemsMethods"] = heroItems.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Where(m => m.DeclaringType == heroItems.GetType())
                            .Select(m => $"{m.ReturnType.Name} {m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})")
                            .Take(40).ToList();
                    }
                    bag["equipment"] = equipBag;

                    // 3) Walk children of the Hero's MainHand / OffHand bones to see what's hanging there.
                    foreach (var bp in new[] { "MainHand", "OffHand", "RightHand", "LeftHand", "RightHandSlot", "LeftHandSlot" })
                    {
                        var p = hero.GetType().GetProperty(bp);
                        var tr = p?.GetValue(hero) as Transform;
                        if (tr != null) bag["bone_" + bp + "_children"] = WalkChildren(tr, 0, 3);
                    }
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }

            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static List<object> WalkChildren(Transform t, int depth, int maxDepth)
        {
            var list = new List<object>();
            if (t == null || depth > maxDepth) return list;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                var renderers = c.GetComponents<Renderer>();
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = c.name,
                    ["worldPos"] = SafeStr(c.position),
                    ["componentTypes"] = c.GetComponents<Component>().Where(x => x != null).Select(x => x.GetType().FullName).ToList(),
                    ["renderers"] = renderers.Select(r => $"{r.GetType().Name} mesh={r.GetComponent<MeshFilter>()?.sharedMesh?.name}").ToList(),
                    ["children"] = WalkChildren(c, depth + 1, maxDepth),
                });
            }
            return list;
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
                if (v is Bounds b) return $"center={SafeStr(b.center)} size={SafeStr(b.size)}";
                return v?.ToString();
            }
            catch (Exception e) { return "STR_ERR " + e.Message; }
        }
    }
}
