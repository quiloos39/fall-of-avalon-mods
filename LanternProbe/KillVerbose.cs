using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LanternProbe
{
    public static class KillVerbose
    {
        // Reach into AutoCollect's static Plugin.Cfg.Verbose and turn it OFF live, so the
        // log spam (and the resulting frame stutters / "flicker") stop without a restart.
        public static string Run()
        {
            try
            {
                // Find AutoCollect's Plugin type wherever it loaded.
                var pluginType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("AutoCollect.Plugin", throwOnError: false))
                    .FirstOrDefault(t => t != null);
                if (pluginType == null) return "AutoCollect.Plugin not found";

                // Plugin holds Cfg as a static field/property called "Cfg".
                object cfg = null;
                var fld = pluginType.GetField("Cfg", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (fld != null) cfg = fld.GetValue(null);
                if (cfg == null)
                {
                    var prp = pluginType.GetProperty("Cfg", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    if (prp != null) cfg = prp.GetValue(null);
                }
                if (cfg == null) return "Plugin.Cfg static accessor not found";

                var verboseEntry = cfg.GetType().GetProperty("Verbose")?.GetValue(cfg);
                if (verboseEntry == null) return "Cfg.Verbose entry not found";

                var valueProp = verboseEntry.GetType().GetProperty("Value");
                bool before = (bool)valueProp.GetValue(verboseEntry);
                valueProp.SetValue(verboseEntry, false);
                bool after = (bool)valueProp.GetValue(verboseEntry);

                return $"AutoCollect.Verbose: {before} -> {after}";
            }
            catch (Exception e) { return "ERR: " + e; }
        }
    }
}
