using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class QuestPathState
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Find the QuestPathController instance.
                var ctrlType = ResolveType("QuestPath.QuestPathController");
                bag["QuestPathController_typeFound"] = ctrlType?.FullName;
                if (ctrlType == null) return JsonConvert.SerializeObject(bag);

                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "QuestPath.QuestPathController");
                bag["controllerInstanceFound"] = ctrl != null;
                if (ctrl == null) return JsonConvert.SerializeObject(bag);

                bag["controller_active_property"] = ctrl.GetType().GetProperty("Active")?.GetValue(ctrl);

                // Pull private state
                foreach (var fn in new[] { "_lastDest", "_lastPlayerPos", "_pathInFlight", "_seekerHost", "_lineHost", "_beamHost" })
                {
                    var f = ctrl.GetType().GetField(fn, BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f == null) continue;
                    var v = f.GetValue(ctrl);
                    if (v is GameObject go) bag[fn] = $"{go.name} active={go.activeInHierarchy}";
                    else bag[fn] = SafeStr(v);
                }

                // Resolve destination via DestinationProvider
                var providerType = ResolveType("QuestPath.DestinationProvider");
                if (providerType != null)
                {
                    var resolveM = providerType.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
                    if (resolveM != null)
                    {
                        var result = resolveM.Invoke(null, null);
                        bag["DestinationProvider_Resolve"] = result?.ToString();
                    }
                }

                // Compass state — what does the player currently have set?
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero != null)
                {
                    var compassType = ResolveType("Awaken.TG.Main.Maps.Compasses.Compass");
                    var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    var compass = elementGeneric?.MakeGenericMethod(compassType).Invoke(hero, null);
                    if (compass != null)
                    {
                        bag["compass.CompassEnabled"] = compassType.GetProperty("CompassEnabled")?.GetValue(compass);
                        var customLoc = compassType.GetProperty("CustomMarkerLocation")?.GetValue(compass);
                        bag["compass.CustomMarkerLocation"] = customLoc?.ToString() ?? "null";
                        if (customLoc != null)
                        {
                            foreach (var pname in new[] { "Coords", "Position" })
                            {
                                var p = customLoc.GetType().GetProperty(pname);
                                if (p != null) try { bag["compass.CustomMarker." + pname] = SafeStr(p.GetValue(customLoc)); } catch { }
                            }
                        }
                        var spyglassLoc = compassType.GetProperty("SpyglassMarkerLocation")?.GetValue(compass);
                        bag["compass.SpyglassMarkerLocation"] = spyglassLoc?.ToString() ?? "null";
                    }
                }

                // Tracked quests
                var qbase = ResolveType("Awaken.TG.Main.Stories.Quests.Quest");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                if (qbase != null && worldType != null)
                {
                    var allM = worldType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "All" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    if (allM != null)
                    {
                        var quests = (allM.MakeGenericMethod(qbase).Invoke(null, null) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                        bag["loadedQuestCount"] = quests?.Count ?? 0;
                        if (quests != null)
                        {
                            var trackedSummary = new List<object>();
                            foreach (var q in quests)
                            {
                                var trackedProp = qbase.GetProperty("IsTracked");
                                bool tracked = trackedProp != null && (bool)trackedProp.GetValue(q);
                                if (!tracked) continue;

                                var displayProp = qbase.GetProperty("DisplayName");
                                trackedSummary.Add(new
                                {
                                    name = displayProp?.GetValue(q)?.ToString(),
                                    type = q.GetType().Name,
                                });
                            }
                            bag["trackedQuests"] = trackedSummary;
                        }
                    }
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }

        private static string SafeStr(object v)
        {
            try
            {
                if (v == null) return "null";
                if (v is Vector3 v3) return $"({v3.x:F1},{v3.y:F1},{v3.z:F1})";
                return v.ToString();
            }
            catch { return "ERR"; }
        }
    }
}
