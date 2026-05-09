using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LanternProbe
{
    public static class Toggle
    {
        // /dlls/exec entry point with no args. Calls LanternController.Toggle() on main thread.
        public static string On()
        {
            var ctrl = Find();
            if (ctrl == null) return "no controller";

            // Read current Active first.
            var activeProp = ctrl.GetType().GetProperty("Active");
            bool wasActive = (bool)activeProp.GetValue(ctrl);
            if (wasActive) return "already on";

            ctrl.GetType().GetMethod("Toggle", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(ctrl, null);
            return "toggled on";
        }

        public static string Off()
        {
            var ctrl = Find();
            if (ctrl == null) return "no controller";

            var activeProp = ctrl.GetType().GetProperty("Active");
            bool wasActive = (bool)activeProp.GetValue(ctrl);
            if (!wasActive) return "already off";

            ctrl.GetType().GetMethod("Toggle", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(ctrl, null);
            return "toggled off";
        }

        private static MonoBehaviour Find() =>
            UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");
    }
}
