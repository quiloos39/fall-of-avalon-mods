using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Awaken.TG.Main.Locations;
using Awaken.TG.Main.Fights.NPCs;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.MVC;

namespace LanternProbe
{
    // Walks every loaded Location, finds NpcElements where IsAlive=false, dumps stats.
    // Direct ModelsSet enumeration (not reflection) — much more reliable for Awaken's
    // struct-based enumerators.
    public static class LiveDeadScan
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                int locCount = 0, withNpc = 0, alive = 0, dead = 0;
                var deadNames = new List<string>();
                var deadTemplateGuids = new HashSet<string>();

                foreach (var loc in World.All<Location>())
                {
                    locCount++;
                    NpcElement npc;
                    if (!loc.TryGetElement(out npc) || npc == null) continue;
                    withNpc++;

                    if (npc.IsAlive) { alive++; continue; }
                    dead++;

                    var template = npc.Template;
                    if (template == null) continue;
                    var name = (template as UnityEngine.Object)?.name ?? "?";
                    if (deadNames.Count < 30) deadNames.Add(name);
                    var guid = template.GUID;
                    if (!string.IsNullOrEmpty(guid)) deadTemplateGuids.Add(guid);
                }

                bag["locations"] = locCount;
                bag["with_npc"] = withNpc;
                bag["alive"] = alive;
                bag["dead"] = dead;
                bag["dead_unique_templates"] = deadTemplateGuids.Count;
                bag["dead_names_sample"] = deadNames;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }
    }
}
