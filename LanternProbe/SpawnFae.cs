using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Pathfinding;
using UnityEngine;
using Awaken.TG.MVC;
using Awaken.TG.Main.Templates;
using Awaken.TG.Main.Locations;
using Awaken.TG.Main.Locations.Setup;
using Awaken.TG.Main.Fights.NPCs;
using Awaken.TG.Main.Heroes;

namespace LanternProbe
{
    public static class SpawnFae
    {
        public static string Run() => Spawn("Spec_Enemy_Fae", 10);

        public static string Spawn(string templateName, int amount)
        {
            var bag = new Dictionary<string, object> { ["request"] = new { templateName, amount } };
            try
            {
                var hero = Hero.Current;
                if (hero == null) { bag["err"] = "Hero.Current is null"; return Json(bag); }

                var template = World.Services.Get<TemplatesProvider>()
                    .GetAllOfType<LocationTemplate>()
                    .Where(t => t.gameObject.GetComponent<NpcAttachment>() != null)
                    .FirstOrDefault(t => t.name == templateName);
                if (template == null) { bag["err"] = $"Template '{templateName}' not found"; return Json(bag); }

                var spawned = new List<object>();
                for (int i = 0; i < amount; i++)
                {
                    float angleDeg = -60f + (120f * i / Math.Max(1, amount - 1));
                    float angleRad = angleDeg * Mathf.Deg2Rad;
                    Vector3 localOffset = new Vector3(Mathf.Sin(angleRad) * 4f, 0f, Mathf.Cos(angleRad) * 5f);
                    Vector3 worldPos = hero.ActorTransform.TransformPoint(localOffset);
                    var nn = AstarPath.active != null ? AstarPath.active.GetNearest(worldPos) : default;
                    Vector3 final = (AstarPath.active != null && nn.node != null) ? (Vector3)nn.position : worldPos;
                    var loc = template.SpawnLocation(final, null, null, null, "", null);
                    spawned.Add(new { i, pos = $"({final.x:F2},{final.y:F2},{final.z:F2})", id = SafeStr(() => loc?.ID?.ToString()) });
                }
                bag["spawned"] = spawned;
                bag["heroPos"] = $"({hero.Coords.x:F2},{hero.Coords.y:F2},{hero.Coords.z:F2})";
            }
            catch (Exception e)
            {
                bag["err"] = e.GetBaseException().Message;
                bag["stack"] = e.StackTrace;
            }
            return Json(bag);
        }

        static string Json(object o) => JsonConvert.SerializeObject(o, Formatting.Indented);
        static string SafeStr(Func<string> f) { try { return f(); } catch { return null; } }
    }
}
