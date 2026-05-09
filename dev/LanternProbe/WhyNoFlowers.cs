using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class WhyNoFlowers
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "QuestPath.QuestPathController");
                if (ctrl == null) return "no controller — mod didn't init";

                var t = ctrl.GetType();
                bag["Active"] = t.GetProperty("Active")?.GetValue(ctrl);

                // Pull every interesting field on the controller
                foreach (var fn in new[] {
                    "_lastDest", "_lastPlayerPos", "_pathInFlight", "_lastPathReachedTarget",
                    "_currentPath", "_seekerHost", "_lineHost", "_beamHost",
                })
                {
                    var f = t.GetField(fn, BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f == null) continue;
                    var v = f.GetValue(ctrl);
                    if (v is GameObject go) bag[fn] = $"{go.name} active={go.activeInHierarchy}";
                    else if (v is System.Collections.ICollection c) bag[fn] = $"count={c.Count}";
                    else bag[fn] = v?.ToString() ?? "null";
                }

                // Flower pool state
                var pool = t.GetField("_flowerPool", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ctrl) as System.Collections.IList;
                bag["flowerPoolCount"] = pool?.Count ?? -1;
                if (pool != null)
                {
                    int active = 0;
                    foreach (var f in pool) if (((GameObject)f).activeSelf) active++;
                    bag["flowerPool_activeCount"] = active;
                }

                // Static asset resolution status
                var meshField = t.GetField("s_flowerMesh", BindingFlags.NonPublic | BindingFlags.Static);
                var matField = t.GetField("s_flowerMaterial", BindingFlags.NonPublic | BindingFlags.Static);
                var resolvedField = t.GetField("s_flowerAssetsResolved", BindingFlags.NonPublic | BindingFlags.Static);
                bag["s_flowerAssetsResolved"] = resolvedField?.GetValue(null);
                bag["s_flowerMesh"] = (meshField?.GetValue(null) as Mesh)?.name ?? "null";
                bag["s_flowerMaterial"] = (matField?.GetValue(null) as Material)?.name ?? "null";

                // Live destination
                var providerType = ResolveType("QuestPath.DestinationProvider");
                var resolveM = providerType?.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
                bag["DestinationProvider.Resolve"] = resolveM?.Invoke(null, null)?.ToString();

                // Compass + active quest
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero != null)
                {
                    Vector3 playerPos = (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero);
                    bag["playerPos"] = $"({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1})";

                    var trackerType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTracker");
                    var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    var tracker = elementGeneric.MakeGenericMethod(trackerType).Invoke(hero, null);
                    var activeQuest = trackerType.GetProperty("ActiveQuest").GetValue(tracker);
                    bag["activeQuest"] = activeQuest?.GetType().GetProperty("DisplayName")?.GetValue(activeQuest)?.ToString() ?? "null";

                    var compassType = ResolveType("Awaken.TG.Main.Maps.Compasses.Compass");
                    var compass = elementGeneric.MakeGenericMethod(compassType).Invoke(hero, null);
                    if (compass != null)
                    {
                        var cm = compassType.GetProperty("CustomMarkerLocation")?.GetValue(compass);
                        bag["customMarker"] = cm?.ToString() ?? "null";
                    }
                }

                bag["Cfg.ShowFlowerTrail"] = GetCfgVal("ShowFlowerTrail");
                bag["Cfg.ShowSkyBeam"] = GetCfgVal("ShowSkyBeam");
                bag["Cfg.ShowPathLine"] = GetCfgVal("ShowPathLine");
                bag["Cfg.ActiveByDefault"] = GetCfgVal("ActiveByDefault");
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static object GetCfgVal(string key)
        {
            try
            {
                var pt = ResolveType("QuestPath.Plugin");
                var cfg = pt.GetField("Cfg", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                var entry = cfg.GetType().GetProperty(key)?.GetValue(cfg);
                return entry?.GetType().GetProperty("Value")?.GetValue(entry);
            }
            catch { return "ERR"; }
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
