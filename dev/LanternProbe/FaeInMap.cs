using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using Awaken.TG.MVC;
using Awaken.TG.Main.Locations;
using Awaken.TG.Main.Fights.NPCs;
using Awaken.TG.Main.Heroes;

namespace LanternProbe
{
    public static class FaeInMap
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var hero = Hero.Current;
                bag["heroPos"] = hero == null ? null : F(hero.Coords);

                var loaded = new List<string>();
                int n = SceneManager.sceneCount;
                for (int i = 0; i < n; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    loaded.Add(s.name);
                }
                bag["loaded_scenes"] = loaded;

                var faeNpcs = new List<object>();
                int npcTotal = 0;
                foreach (var npc in World.All<NpcElement>())
                {
                    npcTotal++;
                    string tname = SafeStr(() => (npc.Template as UnityEngine.Object)?.name) ?? "";
                    string locTname = SafeStr(() => (npc.ParentModel?.Template as UnityEngine.Object)?.name) ?? "";
                    string blob = (tname + "|" + locTname).ToLowerInvariant();
                    if (!blob.Contains("fae")) continue;
                    Vector3? c = null; try { c = npc.Coords; } catch { }
                    float? dist = (hero != null && c.HasValue) ? Vector3.Distance(hero.Coords, c.Value) : (float?)null;
                    faeNpcs.Add(new
                    {
                        npcTemplate = tname,
                        locTemplate = locTname,
                        coords = c.HasValue ? F(c.Value) : null,
                        distFromHero = dist.HasValue ? dist.Value.ToString("F1") : null,
                        alive = SafeBool(() => npc.IsAlive),
                        id = SafeStr(() => npc.ID?.ToString()),
                    });
                }
                bag["npc_total"] = npcTotal;
                bag["fae_npc_count"] = faeNpcs.Count;
                bag["fae_npcs"] = faeNpcs.OrderBy(x => x?.GetType().GetProperty("distFromHero")?.GetValue(x)?.ToString()).ToArray();

                var faeLocs = new List<object>();
                int locTotal = 0;
                foreach (var loc in World.All<Location>())
                {
                    locTotal++;
                    string tname = SafeStr(() => (loc.Template as UnityEngine.Object)?.name) ?? "";
                    if (!tname.ToLowerInvariant().Contains("fae")) continue;
                    float? dist = hero != null ? Vector3.Distance(hero.Coords, loc.Coords) : (float?)null;
                    faeLocs.Add(new
                    {
                        locTemplate = tname,
                        coords = F(loc.Coords),
                        distFromHero = dist.HasValue ? dist.Value.ToString("F1") : null,
                        id = SafeStr(() => loc.ID?.ToString()),
                    });
                }
                bag["loc_total"] = locTotal;
                bag["fae_loc_count"] = faeLocs.Count;
                bag["fae_locs"] = faeLocs.ToArray();
            }
            catch (Exception e)
            {
                bag["err"] = e.GetBaseException().Message;
                bag["stack"] = e.StackTrace;
            }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        static string F(Vector3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";
        static string SafeStr(Func<string> f) { try { return f(); } catch { return null; } }
        static bool? SafeBool(Func<bool> f) { try { return f(); } catch { return null; } }
    }
}
