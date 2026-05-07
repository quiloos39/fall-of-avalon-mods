using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class Force
    {
        // Hard-reset: ensure config is set, clear caches, force OFF, then force ON.
        // Then wait up to 3 seconds for the async prefab load + attach to finish, then return state.
        public static System.Collections.Generic.IEnumerator<object> _ = null; // unused, can't use coroutines from /eval

        public static string FullCycleAndReport()
        {
            try
            {
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");
                if (ctrl == null) return "no controller";

                var t = ctrl.GetType();
                var pluginType = ResolveType("Lantern.Plugin");
                var cfg = pluginType.GetField("Cfg", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

                // Reaffirm config (in case anything reset).
                SetCfg(cfg, "ItemNameMatch", "wyrdlantern,lantern");
                SetCfg(cfg, "Intensity", 6000f);
                SetCfg(cfg, "Range", 25f);
                SetCfg(cfg, "OffsetForward", 0.4f);
                SetCfg(cfg, "OffsetUp", 0.3f);
                SetCfg(cfg, "CastShadows", false);
                SetCfg(cfg, "Scale", 0.3f);

                // Clear caches so EnsureModelLoadedAndAttached redoes the lookup.
                t.GetField("_cachedPrefab", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                t.GetField("_cachedPrefabKey", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                t.GetField("_matchedTemplateName", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                t.GetField("_heldAssetReference", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                t.GetField("_prefabLoadInFlight", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, false);

                var activeProp = t.GetProperty("Active");
                var toggle = t.GetMethod("Toggle", BindingFlags.Public | BindingFlags.Instance);

                // Off if currently on.
                if ((bool)activeProp.GetValue(ctrl)) toggle.Invoke(ctrl, null);
                // On.
                toggle.Invoke(ctrl, null);

                return "cycled. caches cleared. wait ~1s then call ReportNow.";
            }
            catch (Exception e) { return "ERR: " + e; }
        }

        public static string ReportNow()
        {
            try
            {
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");
                if (ctrl == null) return "no controller";

                var t = ctrl.GetType();
                var bag = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["active"] = t.GetProperty("Active").GetValue(ctrl),
                    ["matchedTemplate"] = t.GetField("_matchedTemplateName", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrl) as string,
                    ["cachedPrefab"] = (t.GetField("_cachedPrefab", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrl) as GameObject)?.name,
                    ["loadInFlight"] = t.GetField("_prefabLoadInFlight", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrl),
                    ["modelInstance"] = (t.GetField("_modelInstance", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrl) as GameObject)?.name,
                    ["root"] = (t.GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrl) as GameObject)?.name,
                };

                var modelGo = t.GetField("_modelInstance", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrl) as GameObject;
                if (modelGo != null)
                {
                    bag["modelChildTree"] = modelGo.GetComponentsInChildren<Transform>(true)
                        .Select(tr => $"{tr.name} components=[{string.Join(",", tr.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name))}]")
                        .ToList();
                    bag["modelRenderersAfter"] = modelGo.GetComponentsInChildren<Renderer>(true).Select(r => r.GetType().Name + " on " + r.name).ToList();
                }

                return JsonConvert.SerializeObject(bag, Formatting.Indented);
            }
            catch (Exception e) { return "ERR: " + e; }
        }

        private static void SetCfg(object cfg, string propName, object value)
        {
            var entry = cfg.GetType().GetProperty(propName).GetValue(cfg);
            entry.GetType().GetProperty("Value").SetValue(entry, value);
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
