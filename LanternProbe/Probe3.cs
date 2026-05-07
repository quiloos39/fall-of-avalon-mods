using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class Probe3
    {
        // 1) Look at the cached prefab's DrakeMeshRenderer components and dump every field
        //    on every component, so we can find the Mesh + Material references.
        // 2) Look up Wyrdlantern templates and report what their PickablePrefab points to
        //    by reflection (just the runtimeKey / address, no async load).
        public static string Run()
        {
            var report = new Dictionary<string, object>();

            try
            {
                // Find the LanternController and pull its _cachedPrefab.
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");
                if (ctrl == null) { report["err"] = "no controller"; return JsonConvert.SerializeObject(report, Formatting.Indented); }

                var cachedPrefabF = ctrl.GetType().GetField("_cachedPrefab", BindingFlags.NonPublic | BindingFlags.Instance);
                var cachedPrefab = cachedPrefabF.GetValue(ctrl) as GameObject;
                if (cachedPrefab == null) { report["err"] = "no cached prefab"; return JsonConvert.SerializeObject(report, Formatting.Indented); }

                report["cachedPrefabName"] = cachedPrefab.name;

                // Walk every component and list fields/properties of any whose type name contains "Drake".
                var drakeComponents = cachedPrefab.GetComponentsInChildren<Component>(true)
                    .Where(c => c != null && c.GetType().FullName.Contains("Drake"))
                    .ToList();

                var drakeReports = new List<object>();
                foreach (var c in drakeComponents)
                {
                    var bag = new Dictionary<string, object>();
                    bag["go"] = c.gameObject.name;
                    bag["type"] = c.GetType().FullName;

                    var fields = new Dictionary<string, object>();
                    foreach (var f in c.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        try
                        {
                            var v = f.GetValue(c);
                            fields[f.Name] = DescribeAny(v);
                        }
                        catch (Exception e) { fields[f.Name] = "ERR: " + e.Message; }
                    }
                    bag["fields"] = fields;

                    var props = new Dictionary<string, object>();
                    foreach (var p in c.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (p.GetIndexParameters().Length > 0) continue;
                        try
                        {
                            var v = p.GetValue(c);
                            props[p.Name] = DescribeAny(v);
                        }
                        catch (Exception e) { props[p.Name] = "ERR: " + e.Message; }
                    }
                    bag["props"] = props;
                    drakeReports.Add(bag);
                }
                report["drakeComponents"] = drakeReports;

                // List all candidate templates (no prefab loading — too risky).
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
                string[] needles = { "wyrdlantern", "lantern", "torch" };
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
                    string prefabSummary = "null";
                    if (prefabRef != null)
                    {
                        var bag = new Dictionary<string, object>();
                        foreach (var f in prefabRef.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            try { bag[f.Name] = SafeStr(f.GetValue(prefabRef)); }
                            catch { }
                        }
                        foreach (var p in prefabRef.GetType().GetProperties())
                        {
                            if (p.GetIndexParameters().Length > 0) continue;
                            try { bag["prop:" + p.Name] = SafeStr(p.GetValue(prefabRef)); }
                            catch { }
                        }
                        prefabSummary = JsonConvert.SerializeObject(bag);
                    }

                    matches.Add(new Dictionary<string, object>
                    {
                        ["name"] = name,
                        ["templateType"] = t.GetType().FullName,
                        ["prefabRef"] = prefabSummary,
                    });
                }
                report["matchingTemplates"] = matches;
            }
            catch (Exception e)
            {
                report["err"] = e.ToString();
            }

            return JsonConvert.SerializeObject(report, Formatting.Indented);
        }

        // 3) Try DISABLING shadows + moving the light forward, to test the "light hidden inside torso" theory.
        public static string TestLightOutsideBody()
        {
            try
            {
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");
                if (ctrl == null) return "no controller";

                var pluginType = ResolveType("Lantern.Plugin");
                var cfg = pluginType.GetField("Cfg", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

                // Set OffsetForward = 0.6 (out in front of chest)
                // Set OffsetUp = 0.3 (above chest a bit)
                // Set CastShadows = false
                SetCfg(cfg, "OffsetForward", 0.6f);
                SetCfg(cfg, "OffsetUp", 0.3f);
                SetCfg(cfg, "CastShadows", false);
                SetCfg(cfg, "Intensity", 3000f);  // crank to ensure visibility

                return "applied: OffsetForward=0.6, OffsetUp=0.3, CastShadows=false, Intensity=3000";
            }
            catch (Exception e) { return "ERR: " + e.ToString(); }
        }

        private static void SetCfg(object cfg, string propName, object value)
        {
            var entry = cfg.GetType().GetProperty(propName).GetValue(cfg);
            entry.GetType().GetProperty("Value").SetValue(entry, value);
        }

        private static object DescribeAny(object v)
        {
            if (v == null) return null;
            if (v is UnityEngine.Object uo) return new Dictionary<string, object>
            {
                ["unityName"] = uo.name,
                ["unityType"] = uo.GetType().FullName,
            };
            if (v is string || v.GetType().IsPrimitive) return v;
            if (v is System.Collections.IEnumerable en && !(v is string))
            {
                var list = new List<object>();
                int n = 0;
                foreach (var x in en)
                {
                    list.Add(DescribeAny(x));
                    if (++n >= 10) break;
                }
                return list;
            }
            return SafeStr(v);
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
            try { return v?.ToString(); } catch (Exception e) { return "STR_ERR " + e.Message; }
        }
    }
}
