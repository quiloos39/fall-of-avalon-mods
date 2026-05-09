using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LanternProbe
{
    public static class QuestPathReload
    {
        // Push the new amber-trail config values into the running mod's ConfigEntry<T>.Value
        // so we don't need a game restart for the look change to apply.
        public static string Run()
        {
            try
            {
                var pluginType = ResolveType("QuestPath.Plugin");
                var cfg = pluginType.GetField("Cfg", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                if (cfg == null) return "Plugin.Cfg null";

                Set(cfg, "LineColorR", 1.0f);
                Set(cfg, "LineColorG", 0.72f);
                Set(cfg, "LineColorB", 0.30f);
                Set(cfg, "LineWidth", 0.6f);
                Set(cfg, "LineEmissive", 6f);
                Set(cfg, "BeamHeight", 80f);
                Set(cfg, "BeamRadius", 0.35f);

                return "applied: ColorRGB=(1,0.72,0.30) Width=0.6 Emissive=6 BeamH=80 BeamR=0.35";
            }
            catch (Exception e) { return "ERR: " + e; }
        }

        private static void Set(object cfg, string key, object val)
        {
            var entry = cfg.GetType().GetProperty(key)?.GetValue(cfg);
            entry?.GetType().GetProperty("Value")?.SetValue(entry, val);
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
