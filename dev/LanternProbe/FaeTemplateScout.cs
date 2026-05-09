using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Awaken.TG.MVC;
using Awaken.TG.Main.Templates;
using Awaken.TG.Main.Locations;
using Awaken.TG.Main.Locations.Setup;
using Awaken.TG.Main.Fights.NPCs;

namespace LanternProbe
{
    public static class FaeTemplateScout
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var provider = World.Services.Get<TemplatesProvider>();
                var all = provider.GetAllOfType<LocationTemplate>();
                int total = 0, withNpc = 0;
                var faeMatches = new List<string>();
                var sample = new List<string>();
                foreach (var t in all)
                {
                    total++;
                    var go = t.gameObject;
                    if (go == null) continue;
                    if (go.GetComponent<NpcAttachment>() == null) continue;
                    withNpc++;
                    string n = t.name ?? "";
                    if (sample.Count < 30) sample.Add(n);
                    string l = n.ToLowerInvariant();
                    if (l.Contains("fae") || l.Contains("fairy") || l.Contains("sidhe") ||
                        l.Contains("nymph") || l.Contains("sprite") || l.Contains("pixie") ||
                        l.Contains("wyrd") || l.Contains("wisp"))
                        faeMatches.Add(n);
                }
                bag["templates_total"] = total;
                bag["templates_with_npc"] = withNpc;
                bag["fae_matches"] = faeMatches.OrderBy(s => s).ToArray();
                bag["sample"] = sample;
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
