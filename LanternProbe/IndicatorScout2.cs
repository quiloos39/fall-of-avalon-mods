using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class IndicatorScout2
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();

            try
            {
                // ── 1. Dump structure of MarkerAttachment + LocationSpec to learn the API
                foreach (var typeName in new[] {
                    "Awaken.TG.Main.Maps.Markers.MarkerAttachment",
                    "Awaken.TG.Main.Maps.Markers.MarkerData",
                    "Awaken.TG.Main.Maps.Markers.DiscoveryMarkerData",
                    "Awaken.TG.Main.Maps.Markers.DiscoveryMarkerDataTemplate",
                    "Awaken.TG.Main.Maps.Markers.LocationMarker",
                    "Awaken.TG.Main.Heroes.CharacterSheet.Map.Markers.PointMapMarker",
                    "Awaken.TG.Main.Heroes.CharacterSheet.Map.Markers.VPointMapMarker",
                    "Awaken.TG.Main.Heroes.Storage.HeroStorageAttachment",
                    "Awaken.TG.Main.Locations.Setup.LocationSpec",
                })
                {
                    var t = ResolveType(typeName);
                    if (t == null) { bag[typeName] = "(not found)"; continue; }

                    bag[typeName] = new Dictionary<string, object>
                    {
                        ["base"] = t.BaseType?.FullName,
                        ["interfaces"] = t.GetInterfaces().Select(i => i.FullName).ToList(),
                        ["fields"] = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                            .Take(40)
                            .Select(f => $"{(f.IsStatic ? "static " : "")}{Pretty(f.FieldType)} {f.Name}")
                            .ToList(),
                        ["properties"] = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                            .Take(40)
                            .Select(p => $"{Pretty(p.PropertyType)} {p.Name}")
                            .ToList(),
                        ["methods"] = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                            .Where(m => m.DeclaringType == t)
                            .Take(40)
                            .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                            .ToList(),
                    };
                }

                // ── 2. Find all "attachment"-like component types in the scene that frequently
                //      appear together with LocationSpec — those are the things that distinguish
                //      a forge from a stash from an alchemy bench.
                var allLocSpecs = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .Where(c => c != null && c.GetType().FullName == "Awaken.TG.Main.Locations.Setup.LocationSpec")
                    .ToList();
                bag["totalLocationSpecsInScene"] = allLocSpecs.Count;

                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                Vector3 heroPos = hero != null ? (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero) : Vector3.zero;

                // For each LocationSpec near the hero (within 60m), report its name and sibling component types.
                var nearby = allLocSpecs
                    .Where(c => Vector3.Distance(c.transform.position, heroPos) < 60f)
                    .OrderBy(c => Vector3.Distance(c.transform.position, heroPos))
                    .Take(60)
                    .Select(c => new
                    {
                        name = c.name,
                        distance = Vector3.Distance(c.transform.position, heroPos),
                        siblingComponents = c.gameObject.GetComponents<Component>()
                            .Where(x => x != null)
                            .Select(x => x.GetType().FullName)
                            .ToList(),
                    })
                    .ToList();
                bag["locationSpecsNearby"] = nearby;

                // ── 3. Look in the loaded TG types for forge / smith / craft / station / stand types
                //      that *aren't* in the keyword filter.
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && (n.StartsWith("TG.") || n.StartsWith("Awaken.")); })
                    .SelectMany(SafeTypes)
                    .Where(t => t != null && !string.IsNullOrEmpty(t.FullName))
                    .ToList();
                string[] more = { "smithing", "smith", "blacksmith", "anvil", "craft", "alchemy", "station", "workbench", "interaction", "attachment" };
                var byKeyword = new Dictionary<string, List<string>>();
                foreach (var k in more) byKeyword[k] = new List<string>();
                foreach (var t in allTypes)
                {
                    var lname = t.FullName.ToLowerInvariant();
                    foreach (var k in more)
                    {
                        // Skip junk: any "+" nested or generic noise
                        if (lname.Contains(k) && !lname.Contains("+<") && byKeyword[k].Count < 30) byKeyword[k].Add(t.FullName);
                    }
                }
                bag["typesByKeyword2"] = byKeyword.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => (object)kv.Value);
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

        private static string Pretty(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.Name;
            return t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(g => g.Name)) + ">";
        }
    }
}
