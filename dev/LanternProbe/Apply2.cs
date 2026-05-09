using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LanternProbe
{
    public static class Apply2
    {
        // Brighter, larger range, no model (since our model can't actually render the Drake mesh
        // and may be leaking ECS entities each toggle that show as ghost artifacts).
        public static string CrankAndDropModel()
        {
            try
            {
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");
                if (ctrl == null) return "no controller";

                var pluginType = ResolveType("Lantern.Plugin");
                var cfg = pluginType.GetField("Cfg", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

                // Empty model match → EnsureModelLoadedAndAttached early-returns, no Drake instance.
                SetCfg(cfg, "ItemNameMatch", "");
                SetCfg(cfg, "Intensity", 15000f);
                SetCfg(cfg, "Range", 35f);
                SetCfg(cfg, "OffsetForward", 0.4f);
                SetCfg(cfg, "OffsetUp", 0.3f);
                SetCfg(cfg, "CastShadows", false);
                SetCfg(cfg, "Flicker", true);
                SetCfg(cfg, "FlickerAmount", 0.10f); // softer flicker so it looks lantern-like, not torch-like

                // Clear caches and force a clean re-spawn.
                var t = ctrl.GetType();
                t.GetField("_cachedPrefab", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                t.GetField("_cachedPrefabKey", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                t.GetField("_matchedTemplateName", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                t.GetField("_heldAssetReference", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                t.GetField("_prefabLoadInFlight", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, false);

                var activeProp = t.GetProperty("Active");
                var toggle = t.GetMethod("Toggle", BindingFlags.Public | BindingFlags.Instance);

                if ((bool)activeProp.GetValue(ctrl)) toggle.Invoke(ctrl, null); // off
                toggle.Invoke(ctrl, null); // on

                return "applied: ItemNameMatch=(empty) Intensity=15000 Range=35 OffsetFwd=0.4 OffsetUp=0.3 Flicker=0.10 NoShadows";
            }
            catch (Exception e) { return "ERR: " + e; }
        }

        // Even brighter test if 15000 isn't enough.
        public static string Cranker()
        {
            try
            {
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");
                if (ctrl == null) return "no controller";
                var pluginType = ResolveType("Lantern.Plugin");
                var cfg = pluginType.GetField("Cfg", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                SetCfg(cfg, "Intensity", 30000f);
                SetCfg(cfg, "Range", 50f);
                return "Intensity=30000 Range=50";
            }
            catch (Exception e) { return "ERR: " + e; }
        }

        // Sweep the scene for any orphaned "Lantern_Plugin*" GameObjects (would indicate a cleanup leak).
        public static string FindOrphans()
        {
            try
            {
                var matches = UnityEngine.Object.FindObjectsOfType<Transform>()
                    .Where(t => t != null && (t.name.StartsWith("Lantern_Plugin") || t.name.StartsWith("Lantern_PluginModel")))
                    .Select(t => new { name = t.name, parent = t.parent?.name, pos = $"({t.position.x:F2},{t.position.y:F2},{t.position.z:F2})" })
                    .ToList();
                return Newtonsoft.Json.JsonConvert.SerializeObject(matches, Newtonsoft.Json.Formatting.Indented);
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
