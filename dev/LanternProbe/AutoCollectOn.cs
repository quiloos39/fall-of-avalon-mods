using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LanternProbe
{
    public static class AutoCollectOn
    {
        public static string Run()
        {
            try
            {
                // Find AutoCollect.AutoCollector MonoBehaviour and call its Toggle (or set Active).
                var ac = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "AutoCollect.AutoCollector");
                if (ac == null) return "AutoCollector MB not found";

                var t = ac.GetType();
                var activeProp = t.GetProperty("Active", BindingFlags.Public | BindingFlags.Instance);
                bool wasActive = activeProp != null && (bool)activeProp.GetValue(ac);
                if (wasActive) return $"AutoCollector already on";

                var toggle = t.GetMethod("Toggle", BindingFlags.Public | BindingFlags.Instance);
                if (toggle != null) { toggle.Invoke(ac, null); return "AutoCollector toggled on via Toggle()"; }

                // Fallback — try setter on Active
                if (activeProp != null && activeProp.CanWrite) { activeProp.SetValue(ac, true); return "AutoCollector.Active = true (direct set)"; }

                return "Couldn't find Toggle() or settable Active";
            }
            catch (Exception e) { return "ERR: " + e; }
        }
    }
}
