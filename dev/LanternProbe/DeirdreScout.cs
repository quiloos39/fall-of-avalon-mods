using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using Awaken.TG.MVC;
using Awaken.TG.Main.Locations;
using Awaken.TG.Main.Fights.NPCs;
using Awaken.TG.Main.Heroes;

namespace LanternProbe
{
    public static class DeirdreScout
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var hero = Hero.Current;
                bag["hero_coords"] = hero == null ? null : F(hero.Coords);
                bag["hero_scene_name"] = SafeString(() => hero == null ? null : hero.CurrentDomain.ToString());

                var loadedScenes = new List<string>();
                int n = SceneManager.sceneCount;
                for (int i = 0; i < n; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    loadedScenes.Add($"{s.name} (loaded={s.isLoaded}, path={s.path})");
                }
                bag["loaded_scenes"] = loadedScenes;

                var npcMatches = new List<object>();
                int npcCount = 0;
                foreach (var npc in World.All<NpcElement>())
                {
                    npcCount++;
                    string tname = SafeString(() => (npc.Template as UnityEngine.Object)?.name);
                    string disp = SafeString(() => npc.Name);
                    string idstr = SafeString(() => npc.ID?.ToString());
                    string blob = ((tname ?? "") + "|" + (disp ?? "") + "|" + (idstr ?? "")).ToLowerInvariant();
                    if (!blob.Contains("deirdre") && !blob.Contains("broden")) continue;

                    Vector3? coords = null;
                    try { coords = npc.Coords; } catch { }

                    npcMatches.Add(new {
                        templateName = tname,
                        display = disp,
                        id = idstr,
                        alive = SafeBool(() => npc.IsAlive),
                        coords = coords.HasValue ? F(coords.Value) : null,
                        parentLocId = SafeString(() => npc.ParentModel?.ID?.ToString()),
                        parentLocName = SafeString(() => (npc.ParentModel as Location)?.DisplayName?.ToString()),
                        parentLocCoords = SafeString(() =>
                            (npc.ParentModel as Location) != null
                                ? F(((Location)npc.ParentModel).Coords)
                                : null),
                        scene = SafeString(() => npc.CurrentDomain.ToString()),
                    });
                }
                bag["npc_count_total"] = npcCount;
                bag["npc_matches"] = npcMatches;

                var locMatches = new List<object>();
                int locCount = 0;
                foreach (var loc in World.All<Location>())
                {
                    locCount++;
                    string disp = SafeString(() => loc.DisplayName?.ToString());
                    string tname = SafeString(() => (loc.Template as UnityEngine.Object)?.name);
                    string idstr = SafeString(() => loc.ID?.ToString());
                    string blob = ((tname ?? "") + "|" + (disp ?? "") + "|" + (idstr ?? "")).ToLowerInvariant();
                    if (!blob.Contains("deirdre") && !blob.Contains("broden")) continue;

                    locMatches.Add(new {
                        templateName = tname,
                        display = disp,
                        id = idstr,
                        coords = F(loc.Coords),
                        scene = SafeString(() => loc.CurrentDomain.ToString()),
                    });
                }
                bag["loc_count_total"] = locCount;
                bag["loc_matches"] = locMatches;
            }
            catch (Exception e)
            {
                bag["err"] = e.GetBaseException().Message;
                bag["stack"] = e.StackTrace;
            }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        static string F(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
        static string SafeString(Func<string> f) { try { return f(); } catch { return null; } }
        static bool? SafeBool(Func<bool> f) { try { return f(); } catch { return null; } }
    }
}
