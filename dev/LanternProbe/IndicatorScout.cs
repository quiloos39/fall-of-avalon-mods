using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class IndicatorScout
    {
        // 1) Find every type in TG.Main / Awaken.* whose name contains "stash", "forge",
        //    "alchemy", "smith", "marker", "indicator", "minimap", "map", "icon".
        // 2) For each "stash"-named type, list its base/interfaces and every public/private member
        //    so we can see how indicators are wired.
        // 3) Also list any *components* currently in the scene whose name contains those keywords.
        public static string Run()
        {
            var bag = new Dictionary<string, object>();

            try
            {
                string[] keywords = { "stash", "forge", "alchemy", "smith", "anvil", "cauldron", "marker", "indicator", "minimap", "compass", "icon", "poi", "waypoint" };

                // ── 1) Type discovery
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && (n.StartsWith("TG.") || n.StartsWith("Awaken.")); })
                    .SelectMany(SafeTypes)
                    .Where(t => t != null && !string.IsNullOrEmpty(t.FullName))
                    .ToList();

                bag["assembliesScanned"] = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && (n.StartsWith("TG.") || n.StartsWith("Awaken.")); })
                    .Select(a => a.GetName().Name).ToList();

                var byKeyword = new Dictionary<string, List<string>>();
                foreach (var k in keywords) byKeyword[k] = new List<string>();
                foreach (var t in allTypes)
                {
                    var lname = t.FullName.ToLowerInvariant();
                    foreach (var k in keywords)
                    {
                        if (lname.Contains(k)) byKeyword[k].Add(t.FullName);
                    }
                }
                // Keep only buckets with hits, cap each at 50.
                bag["typesByKeyword"] = byKeyword.Where(kv => kv.Value.Count > 0)
                    .ToDictionary(kv => kv.Key, kv => (object)kv.Value.Take(50).ToList());

                // ── 2) Scene components matching keywords (case-insensitive on name).
                var sceneMatches = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .Where(c => c != null)
                    .Select(c => c.GetType().FullName)
                    .Where(fn => fn != null && keywords.Any(k => fn.ToLowerInvariant().Contains(k)))
                    .GroupBy(x => x)
                    .Select(g => $"{g.Key}  (×{g.Count()})")
                    .OrderBy(x => x)
                    .ToList();
                bag["sceneComponentTypes"] = sceneMatches;

                // ── 3) Hero distance to nearest forge/alchemy/stash by name search in scene
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var heroPos = hero != null
                    ? (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero)
                    : Vector3.zero;
                bag["heroPos"] = $"({heroPos.x:F1},{heroPos.y:F1},{heroPos.z:F1})";

                // Find scene transforms whose name (or parent name) contains the keywords —
                // these are likely the actual placed station GameObjects.
                var nearbyByKeyword = new Dictionary<string, List<object>>();
                var allTr = UnityEngine.Object.FindObjectsOfType<Transform>();
                foreach (var k in new[] { "stash", "forge", "alchemy", "anvil", "cauldron" })
                {
                    nearbyByKeyword[k] = allTr
                        .Where(t => t != null && t.name.ToLowerInvariant().Contains(k))
                        .Select(t => new
                        {
                            name = t.name,
                            parent = t.parent?.name,
                            distance = (heroPos != Vector3.zero) ? Vector3.Distance(t.position, heroPos) : -1f,
                            worldPos = $"({t.position.x:F1},{t.position.y:F1},{t.position.z:F1})",
                            componentTypes = t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName).ToList(),
                        })
                        .OrderBy(x => x.distance < 0 ? float.MaxValue : x.distance)
                        .Take(8)
                        .Cast<object>()
                        .ToList();
                }
                bag["nearbySceneObjects"] = nearbyByKeyword;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }

            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); } catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); } catch { return Array.Empty<Type>(); }
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
