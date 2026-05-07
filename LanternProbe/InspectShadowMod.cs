using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class InspectShadowMod
    {
        // Find TGAllLightsCastShadows.ShadowManager (the MonoBehaviour) and dump every public/
        // private field + method signature. Also list the Plugin's Cfg fields.
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var mgr = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(m => m != null && m.GetType().FullName == "TGAllLightsCastShadows.ShadowManager");
                if (mgr == null) return "ShadowManager MB not found";

                var t = mgr.GetType();
                bag["typeFullName"] = t.FullName;

                var fields = new Dictionary<string, object>();
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    try { fields[f.Name + (f.IsStatic ? " [static]" : "")] = f.GetValue(f.IsStatic ? null : mgr)?.ToString() ?? "null"; }
                    catch (Exception e) { fields[f.Name] = "ERR " + e.Message; }
                }
                bag["fields"] = fields;

                var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.DeclaringType == t)
                    .Select(m => $"{(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})")
                    .ToList();
                bag["methods"] = methods;

                // Plugin.Cfg fields too
                var pluginType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("TGAllLightsCastShadows.Plugin", false))
                    .FirstOrDefault(x => x != null);
                if (pluginType != null)
                {
                    var staticFields = pluginType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    var pluginBag = new Dictionary<string, object>();
                    foreach (var f in staticFields)
                    {
                        try
                        {
                            var v = f.GetValue(null);
                            // If it's a ConfigEntry-shaped thing, get .Value
                            if (v != null)
                            {
                                var valueProp = v.GetType().GetProperty("Value");
                                if (valueProp != null) pluginBag[f.Name + ".Value"] = valueProp.GetValue(v)?.ToString();
                                else pluginBag[f.Name] = v.ToString();
                            }
                            else pluginBag[f.Name] = "null";
                        }
                        catch (Exception e) { pluginBag[f.Name] = "ERR " + e.Message; }
                    }
                    bag["pluginStatic"] = pluginBag;
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        // Disable the mod's component to test if the flicker stops.
        public static string DisableShadowMod()
        {
            try
            {
                var mgr = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(m => m != null && m.GetType().FullName == "TGAllLightsCastShadows.ShadowManager");
                if (mgr == null) return "ShadowManager not found";
                mgr.enabled = false;
                return "ShadowManager disabled (set component.enabled=false). Move around and check if flicker stops.";
            }
            catch (Exception e) { return "ERR: " + e; }
        }

        public static string EnableShadowMod()
        {
            try
            {
                var mgr = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(m => m != null && m.GetType().FullName == "TGAllLightsCastShadows.ShadowManager");
                if (mgr == null) return "ShadowManager not found";
                mgr.enabled = true;
                return "ShadowManager re-enabled.";
            }
            catch (Exception e) { return "ERR: " + e; }
        }
    }
}
