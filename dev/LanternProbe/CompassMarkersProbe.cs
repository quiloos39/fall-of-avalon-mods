using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Awaken.TG.MVC;
using Awaken.TG.Main.Maps.Compasses;
using Awaken.TG.Main.Maps.Markers;

namespace LanternProbe
{
    // Walks every CompassMarker / LocationMarker registered with World — these
    // are the live in-world quest pointers the compass UI uses. If we find
    // ones tied to the active quest, those are our destination coords.
    public static class CompassMarkersProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                int total = 0;
                var entries = new List<object>();
                foreach (var cm in World.All<CompassMarker>())
                {
                    total++;
                    string topText = null, parentName = null;
                    Vector3 coords = default;
                    bool enabled = false;
                    try { topText = cm.TopText; } catch { }
                    try { coords = cm.Coords; } catch { }
                    try { enabled = (bool)cm.GetType().GetProperty("Enabled", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(cm); } catch { }
                    try { parentName = cm.GetType().GetProperty("Marker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(cm)?.GetType()?.Name; } catch { }
                    entries.Add(new
                    {
                        type = cm.GetType().Name,
                        marker_type = parentName,
                        topText,
                        coords = $"({coords.x:F1},{coords.y:F1},{coords.z:F1})",
                        enabled,
                    });
                }
                bag["compass_marker_total"] = total;
                bag["compass_markers"] = entries;

                // Also LocationMarker (the underlying)
                int lmTotal = 0;
                var lmEntries = new List<object>();
                foreach (var lm in World.All<LocationMarker>())
                {
                    lmTotal++;
                    if (lmTotal > 30) break;
                    string text = null;
                    Vector3 pos = default;
                    string parentLocName = null;
                    string parentLocTags = null;
                    try { text = lm.GetType().GetProperty("TooltipText")?.GetValue(lm) as string; } catch { }
                    try { pos = lm.Position; } catch { }
                    try
                    {
                        var parent = lm.GetType().GetProperty("ParentModel")?.GetValue(lm);
                        parentLocName = parent?.GetType().GetProperty("DisplayName")?.GetValue(parent)?.ToString() ?? parent?.ToString();
                        var tags = parent?.GetType().GetProperty("Tags")?.GetValue(parent) as System.Collections.IEnumerable;
                        if (tags != null) parentLocTags = string.Join(",", tags.Cast<object>().Select(o => o?.ToString()).Take(8));
                    }
                    catch { }

                    lmEntries.Add(new
                    {
                        full_type = lm.GetType().FullName,
                        type = lm.GetType().Name,
                        text,
                        pos = $"({pos.x:F1},{pos.y:F1},{pos.z:F1})",
                        parent = parentLocName,
                        parent_tags = parentLocTags,
                    });
                }
                bag["location_marker_total"] = lmTotal;
                bag["location_markers_sample"] = lmEntries;
            }
            catch (Exception e)
            {
                bag["err"] = e.GetBaseException().Message;
                bag["stack"] = e.StackTrace;
            }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }
    }
}
