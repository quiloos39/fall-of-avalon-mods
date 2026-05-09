using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // Inspects the live QuestPath plugin state — is it active, did Destination
    // resolve, did the last A* path complete, is the line/flowers/beam visible.
    // Lets us diagnose "the path doesn't show" without recompiling QuestPath.
    public static class QuestPathStateProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var ctrlType = ResolveType("QuestPath.QuestPathController");
                bag["controller_type_found"] = ctrlType != null;
                if (ctrlType == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                var ctrlObj = UnityEngine.Object.FindObjectOfType(ctrlType);
                bag["controller_alive"] = ctrlObj != null;
                if (ctrlObj == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                bag["Active"] = GetProp<bool>(ctrlObj, "Active");
                bag["lastDest"] = FormatNullable(GetField<Vector3?>(ctrlObj, "_lastDest"));
                bag["lastPlayerPos"] = FormatVec(GetField<Vector3>(ctrlObj, "_lastPlayerPos"));
                bag["pathInFlight"] = GetField<bool>(ctrlObj, "_pathInFlight");
                bag["lastPathReachedTarget"] = GetField<bool>(ctrlObj, "_lastPathReachedTarget");

                var path = GetField<System.Collections.IList>(ctrlObj, "_currentPath");
                bag["currentPath_count"] = path?.Count ?? 0;
                if (path != null && path.Count > 0)
                {
                    bag["path_first"] = FormatVec((Vector3)path[0]);
                    bag["path_last"] = FormatVec((Vector3)path[path.Count - 1]);
                }

                // Live destination resolve
                var destProvType = ResolveType("QuestPath.DestinationProvider");
                if (destProvType != null)
                {
                    var resolveMethod = destProvType.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
                    if (resolveMethod != null)
                    {
                        try
                        {
                            var result = resolveMethod.Invoke(null, null);
                            // ValueTuple<Vector3?, string>
                            var rt = result.GetType();
                            var coords = rt.GetField("Item1").GetValue(result);
                            var source = rt.GetField("Item2").GetValue(result) as string;
                            bag["resolve_coords"] = FormatNullable(coords as Vector3?);
                            bag["resolve_source"] = source;
                        }
                        catch (Exception e) { bag["resolve_err"] = e.GetBaseException().Message; }
                    }
                }

                // Hero state
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero != null)
                {
                    var coords = (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero);
                    bag["hero_coords"] = FormatVec(coords);
                }
                else { bag["hero"] = "null"; }

                // Visual hosts
                var lineHost = GetField<GameObject>(ctrlObj, "_lineHost");
                var beamHost = GetField<GameObject>(ctrlObj, "_beamHost");
                bag["lineHost"] = lineHost != null ? $"alive,active={lineHost.activeSelf}" : "null";
                bag["beamHost"] = beamHost != null ? $"alive,active={beamHost.activeSelf}" : "null";

                var flowerPool = GetField<System.Collections.IList>(ctrlObj, "_flowerPool");
                int activeFlowers = 0;
                if (flowerPool != null)
                {
                    foreach (var go in flowerPool)
                        if (go != null && ((GameObject)go).activeSelf) activeFlowers++;
                }
                bag["flowers_total"] = flowerPool?.Count ?? 0;
                bag["flowers_active"] = activeFlowers;

                // Plugin config snapshot
                var cfgType = ResolveType("QuestPath.QuestPathConfig");
                var pluginType = ResolveType("QuestPath.Plugin");
                var cfgObj = pluginType?.GetField("Cfg", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null);
                if (cfgObj != null && cfgType != null)
                {
                    bag["cfg.Enabled"] = ReadCfgValue(cfgObj, cfgType, "Enabled");
                    bag["cfg.ActiveByDefault"] = ReadCfgValue(cfgObj, cfgType, "ActiveByDefault");
                    bag["cfg.UseCustomMarker"] = ReadCfgValue(cfgObj, cfgType, "UseCustomMarker");
                    bag["cfg.UseTrackedQuest"] = ReadCfgValue(cfgObj, cfgType, "UseTrackedQuest");
                    bag["cfg.ShowPathLine"] = ReadCfgValue(cfgObj, cfgType, "ShowPathLine");
                    bag["cfg.ShowFlowerTrail"] = ReadCfgValue(cfgObj, cfgType, "ShowFlowerTrail");
                    bag["cfg.Verbose"] = ReadCfgValue(cfgObj, cfgType, "Verbose");
                    bag["cfg.ToggleHotkey"] = ReadCfgValue(cfgObj, cfgType, "ToggleHotkey");
                }
            }
            catch (Exception e)
            {
                bag["err"] = e.GetBaseException().Message;
                bag["stack"] = e.StackTrace;
            }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static object ReadCfgValue(object cfg, Type t, string propName)
        {
            try
            {
                var prop = t.GetProperty(propName);
                var entry = prop?.GetValue(cfg);
                if (entry == null) return null;
                var valueProp = entry.GetType().GetProperty("Value");
                return valueProp?.GetValue(entry)?.ToString();
            }
            catch (Exception e) { return $"err:{e.GetBaseException().Message}"; }
        }

        private static T GetField<T>(object obj, string name)
        {
            var f = obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (f == null) return default;
            var v = f.GetValue(obj);
            if (v == null) return default;
            return (T)v;
        }

        private static T GetProp<T>(object obj, string name)
        {
            var p = obj.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (p == null) return default;
            var v = p.GetValue(obj);
            if (v == null) return default;
            return (T)v;
        }

        private static string FormatVec(Vector3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";
        private static string FormatNullable(Vector3? v) => v.HasValue ? FormatVec(v.Value) : "null";

        private static Type ResolveType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
