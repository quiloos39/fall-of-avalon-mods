using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class IconDeepScan
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();

            try
            {
                // 1) Walk every MarkerAttachment in the scene and collect the (icon-sprite-name → wrapper-mode → template-name) tuples.
                var maType = ResolveType("Awaken.TG.Main.Maps.Markers.MarkerAttachment");
                var allMA = UnityEngine.Object.FindObjectsOfType(maType) as Component[];
                bag["totalMarkerAttachmentsInScene"] = allMA?.Length ?? 0;

                var perAttachment = new List<Dictionary<string, object>>();
                var iconNamesSeen = new Dictionary<string, int>();   // sprite name → count
                var templateNamesSeen = new Dictionary<string, int>();

                if (allMA != null)
                {
                    var wrapperField = maType.GetField("markerDataWrapper", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    var markerDataProp = maType.GetProperty("MarkerData");

                    foreach (var ma in allMA)
                    {
                        if (ma == null) continue;
                        var row = new Dictionary<string, object>
                        {
                            ["go"] = ma.gameObject.name,
                        };
                        var wrapper = wrapperField?.GetValue(ma);
                        if (wrapper != null)
                        {
                            // Read base-class fields: method (Embedded/Explicit), explicitData, embeddedData
                            var wt = wrapper.GetType();
                            object methodVal = null, explicitData = null, embeddedData = null;
                            for (var t = wt; t != null && t != typeof(object); t = t.BaseType)
                            {
                                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                                {
                                    try
                                    {
                                        if (f.Name == "method") methodVal = f.GetValue(wrapper);
                                        else if (f.Name == "explicitData") explicitData = f.GetValue(wrapper);
                                        else if (f.Name == "embeddedData") embeddedData = f.GetValue(wrapper);
                                    }
                                    catch { }
                                }
                            }
                            row["method"] = methodVal?.ToString();

                            string templateName = null;
                            if (explicitData is UnityEngine.Object soTpl) { templateName = soTpl.name; templateNamesSeen[templateName] = templateNamesSeen.GetValueOrDefault(templateName) + 1; }
                            row["template"] = templateName;

                            // Resolve actual MarkerData via the wrapper's MarkerData property and read its icon.
                            object md = null;
                            try { md = markerDataProp?.GetValue(ma); } catch { }
                            if (md != null)
                            {
                                row["mdType"] = md.GetType().Name;
                                string iconSpriteName = TryResolveIconName(md);
                                row["iconSprite"] = iconSpriteName;
                                if (iconSpriteName != null) iconNamesSeen[iconSpriteName] = iconNamesSeen.GetValueOrDefault(iconSpriteName) + 1;
                            }
                        }
                        perAttachment.Add(row);
                    }
                }

                bag["distinctIconSpritesInUse"] = iconNamesSeen
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} (×{kv.Value})").ToList();

                bag["distinctTemplatesInUse"] = templateNamesSeen
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key} (×{kv.Value})").ToList();

                // 2) Also scan ALL loaded Sprites with name beginning with "icon_" — exposes assets
                //    that exist in memory but aren't currently bound to any MarkerAttachment.
                var allSprites = Resources.FindObjectsOfTypeAll<Sprite>()
                    .Where(s => s != null && !string.IsNullOrEmpty(s.name) && s.name.StartsWith("icon_", StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.name)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();
                bag["allLoadedIconSprites"] = allSprites;
                bag["allLoadedIconSpriteCount"] = allSprites.Count;

                // 3) Group example GameObject names by icon, so user can see what KIND of thing uses each icon.
                var examplesByIcon = new Dictionary<string, List<string>>();
                foreach (var row in perAttachment)
                {
                    if (!row.ContainsKey("iconSprite") || row["iconSprite"] == null) continue;
                    var icon = (string)row["iconSprite"];
                    if (!examplesByIcon.TryGetValue(icon, out var list)) { list = new List<string>(); examplesByIcon[icon] = list; }
                    if (list.Count < 3 && !list.Contains((string)row["go"])) list.Add((string)row["go"]);
                }
                bag["examplesByIcon"] = examplesByIcon
                    .OrderBy(kv => kv.Key)
                    .ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            }
            catch (Exception e) { bag["err"] = e.ToString(); }

            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        // MarkerData.MarkerIcon → ShareableSpriteReference → arSpriteReference → load Sprite → name.
        private static string TryResolveIconName(object markerData)
        {
            try
            {
                var iconRefProp = markerData.GetType().GetProperty("MarkerIcon");
                var iconRef = iconRefProp?.GetValue(markerData);
                if (iconRef == null) return null;
                var arRefField = iconRef.GetType().GetField("arSpriteReference", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                var arRef = arRefField?.GetValue(iconRef);
                if (arRef == null) return null;
                var loadM = arRef.GetType().GetMethods().FirstOrDefault(m => m.Name == "LoadAsset" && m.IsGenericMethod);
                if (loadM == null) return null;
                var handle = loadM.MakeGenericMethod(typeof(Sprite)).Invoke(arRef, null);
                var waitM = handle?.GetType().GetMethod("WaitForCompletion");
                var s = waitM?.Invoke(handle, null) as Sprite;
                return s?.name;
            }
            catch { return null; }
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
