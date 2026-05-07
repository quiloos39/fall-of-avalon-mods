using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class TestMarker
    {
        // Inject a MarkerAttachment onto the live forge spec, cloning wrapper from the stash spec.
        // Returns whether anything happened. The user then opens their map and verifies the icon.
        public static string Run()
        {
            try
            {
                var locSpecType = ResolveType("Awaken.TG.Main.Locations.Setup.LocationSpec");
                var maType = ResolveType("Awaken.TG.Main.Maps.Markers.MarkerAttachment");
                if (locSpecType == null || maType == null) return "types missing";

                var allSpecs = UnityEngine.Object.FindObjectsOfType(locSpecType) as Component[];
                var stash = allSpecs?.FirstOrDefault(s => s != null && s.gameObject.name.IndexOf("stash", StringComparison.OrdinalIgnoreCase) >= 0);
                var forge = allSpecs?.FirstOrDefault(s => s != null && s.gameObject.name.IndexOf("Blacksmith", StringComparison.OrdinalIgnoreCase) >= 0);
                if (stash == null) return "no stash spec found in scene";
                if (forge == null) return "no Blacksmithing spec found in scene (player may need to load that area)";

                var stashMa = stash.gameObject.GetComponent(maType);
                if (stashMa == null) return "stash has no MarkerAttachment to copy from";

                var wrapperField = maType.GetField("markerDataWrapper", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                var wrapper = wrapperField?.GetValue(stashMa);
                if (wrapper == null) return "stash MarkerAttachment.markerDataWrapper is null";

                // If forge already has one, abort.
                if (forge.gameObject.GetComponent(maType) != null) return $"forge '{forge.gameObject.name}' already has MarkerAttachment — nothing to do";

                var newMa = forge.gameObject.AddComponent(maType);
                wrapperField.SetValue(newMa, wrapper);

                // Verify
                var verify = forge.gameObject.GetComponent(maType);
                var mdProp = maType.GetProperty("MarkerData");
                var md = mdProp?.GetValue(verify);

                return JsonConvert.SerializeObject(new
                {
                    stashSpec = stash.gameObject.name,
                    forgeSpec = forge.gameObject.name,
                    forgePos = $"({forge.transform.position.x:F1},{forge.transform.position.y:F1},{forge.transform.position.z:F1})",
                    markerAdded = verify != null,
                    markerDataResolved = md != null,
                    markerDataType = md?.GetType().FullName,
                    note = "Open your map and look for a stash icon at the forge position. If you see one, the technique works."
                }, Formatting.Indented);
            }
            catch (Exception e) { return "ERR: " + e; }
        }

        // After Run, check if a corresponding LocationMarker model element exists for the forge.
        public static string CheckLocationMarker()
        {
            try
            {
                var locSpecType = ResolveType("Awaken.TG.Main.Locations.Setup.LocationSpec");
                var allSpecs = UnityEngine.Object.FindObjectsOfType(locSpecType) as Component[];
                var forge = allSpecs?.FirstOrDefault(s => s != null && s.gameObject.name.IndexOf("Blacksmith", StringComparison.OrdinalIgnoreCase) >= 0);
                if (forge == null) return "no forge spec";

                var modelProp = locSpecType.GetProperty("Model");
                var model = modelProp?.GetValue(forge);
                if (model == null) return "forge LocationSpec.Model is null (location not initialized yet — far from player or not streamed)";

                // Walk model.Elements (or similar) to look for LocationMarker
                var elementsProp = model.GetType().GetProperty("Elements");
                var elements = elementsProp?.GetValue(model) as System.Collections.IEnumerable;
                var elementSummary = new List<string>();
                if (elements != null)
                {
                    foreach (var e in elements)
                    {
                        elementSummary.Add(e?.GetType().Name ?? "null");
                        if (elementSummary.Count > 30) { elementSummary.Add("..."); break; }
                    }
                }
                return JsonConvert.SerializeObject(new
                {
                    modelType = model.GetType().FullName,
                    elementCount = elementSummary.Count,
                    elements = elementSummary,
                }, Formatting.Indented);
            }
            catch (Exception e) { return "ERR: " + e; }
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
