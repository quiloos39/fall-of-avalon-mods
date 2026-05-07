using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Awaken.TG.MVC;
using Awaken.TG.Main.Locations;
using Awaken.TG.Main.Fights.NPCs;

namespace LanternProbe
{
    // Search loaded Locations for an NPC named like "Claire" (case-insensitive,
    // partial match). The Quest API can't pin down its target — but the NPC
    // might be loaded in the scene already.
    public static class NpcByNameProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                int locCount = 0, withNpc = 0;
                var matches = new List<object>();
                var sampleNames = new List<string>();

                foreach (var loc in World.All<Location>())
                {
                    locCount++;
                    if (!loc.TryGetElement<NpcElement>(out var npc) || npc == null) continue;
                    withNpc++;

                    string name = null;
                    try
                    {
                        var template = npc.Template;
                        name = (template as UnityEngine.Object)?.name ?? template?.ToString();
                    }
                    catch { }
                    if (string.IsNullOrEmpty(name)) continue;

                    if (sampleNames.Count < 50) sampleNames.Add(name);

                    if (name.IndexOf("Claire", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matches.Add(new
                        {
                            name,
                            loc_coords = $"({loc.Coords.x:F1},{loc.Coords.y:F1},{loc.Coords.z:F1})",
                            alive = npc.IsAlive
                        });
                    }
                }

                bag["locations_total"] = locCount;
                bag["with_npc"] = withNpc;
                bag["claire_matches"] = matches;
                bag["sample_names"] = sampleNames.Take(40).ToArray();
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
