using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LanternProbe
{
    public static class Apply
    {
        // Swap to Wyrdlantern, much brighter light, larger range. Forces the controller to
        // re-resolve the cached prefab by clearing _cachedPrefab + _cachedPrefabKey, then
        // toggling off+on so EnsureModelLoadedAndAttached runs again.
        public static string SwapToWyrdlanternBrighter()
        {
            try
            {
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");
                if (ctrl == null) return "no controller";

                var pluginType = ResolveType("Lantern.Plugin");
                var cfg = pluginType.GetField("Cfg", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

                // Tweak config first.
                SetCfg(cfg, "ItemNameMatch", "wyrdlantern,lantern,torch");
                SetCfg(cfg, "Intensity", 6000f);
                SetCfg(cfg, "Range", 25f);
                SetCfg(cfg, "OffsetForward", 0.4f);
                SetCfg(cfg, "OffsetUp", 0.3f);
                SetCfg(cfg, "CastShadows", false);
                SetCfg(cfg, "Scale", 0.3f);

                // Clear the cached prefab so EnsureModelLoadedAndAttached re-runs the lookup.
                var t = ctrl.GetType();
                t.GetField("_cachedPrefab", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                t.GetField("_cachedPrefabKey", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                t.GetField("_matchedTemplateName", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                // Also drop any held asset reference so the next lookup gets a fresh one.
                t.GetField("_heldAssetReference", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);

                // Toggle off if currently on, then back on.
                var activeProp = t.GetProperty("Active");
                bool wasActive = (bool)activeProp.GetValue(ctrl);
                var toggle = t.GetMethod("Toggle", BindingFlags.Public | BindingFlags.Instance);
                if (wasActive) toggle.Invoke(ctrl, null);    // off
                toggle.Invoke(ctrl, null);                    // on (will re-resolve)

                return $"applied: needles=wyrdlantern,lantern,torch | Intensity=6000 | Range=25 | OffsetForward=0.4 OffsetUp=0.3 | CastShadows=off | Scale=0.3 | wasActive={wasActive}";
            }
            catch (Exception e) { return "ERR: " + e; }
        }

        // After swap, peek at what got resolved.
        public static string PeekModel()
        {
            try
            {
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");
                if (ctrl == null) return "no controller";
                var t = ctrl.GetType();
                string name = t.GetField("_matchedTemplateName", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrl) as string;
                var prefab = t.GetField("_cachedPrefab", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrl) as GameObject;
                bool inFlight = (bool)t.GetField("_prefabLoadInFlight", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrl);
                return $"matchedTemplate={name ?? "(null)"} | cachedPrefab={prefab?.name ?? "(null)"} | loadInFlight={inFlight}";
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
