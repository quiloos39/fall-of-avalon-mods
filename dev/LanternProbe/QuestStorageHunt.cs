using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class QuestStorageHunt
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero == null) return "no hero";

                // 1) List Hero's Element<T>() candidates by enumerating loaded TG types whose
                //    name contains "Quest" and trying Hero.Element<T> on each.
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .Where(t => t != null && t.FullName != null
                             && (t.FullName.StartsWith("Awaken.TG.Main.Stories.Quests") || t.FullName.StartsWith("Awaken.TG.Main.Heroes"))
                             && !t.IsAbstract && !t.IsInterface && !t.IsGenericTypeDefinition
                             && !t.FullName.Contains("+<") && !t.FullName.Contains("d__"))
                    .Where(t => t.FullName.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                bag["candidateTypes"] = allTypes.Select(t => t.FullName).Take(40).ToList();

                // Also: Hero's Elements list — scan all elements to find quest-related ones.
                var modelType = ResolveType("Awaken.TG.MVC.Model");
                var elementsM = modelType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Elements" && m.IsGenericMethod && m.GetParameters().Length == 0);

                // For each candidate quest-related type, try Hero.Elements<T>() if it's an Element.
                var found = new List<object>();
                foreach (var t in allTypes)
                {
                    try
                    {
                        var iface = ResolveType("Awaken.TG.MVC.Elements.IElement");
                        if (iface == null || !iface.IsAssignableFrom(t)) continue;
                        var generic = elementsM?.MakeGenericMethod(t);
                        var result = generic?.Invoke(hero, null);
                        var list = (result as System.Collections.IEnumerable)?.Cast<object>().ToList();
                        if (list != null && list.Count > 0)
                        {
                            found.Add(new
                            {
                                heroElement = t.FullName,
                                count = list.Count,
                                firstType = list[0].GetType().FullName,
                            });
                        }
                    }
                    catch { }
                }
                bag["heroElements_quest_related"] = found;

                // Also try World.All<T> for a few likely Quest container types.
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var allWorld = worldType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "All" && m.IsGenericMethod && m.GetParameters().Length == 0);
                if (allWorld != null)
                {
                    foreach (var t in allTypes.Take(60))
                    {
                        try
                        {
                            var r = allWorld.MakeGenericMethod(t).Invoke(null, null);
                            var list = (r as System.Collections.IEnumerable)?.Cast<object>().ToList();
                            if (list != null && list.Count > 0)
                            {
                                bag["world_" + t.FullName] = list.Count;
                            }
                        }
                        catch { }
                    }
                }

                // Specific check: Hero has Element<HeroStorage>, Element<Inventory>, etc. Look for
                // anything Element<*Quest*>.
                bag["hero_props"] = hero.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(p => p.Name.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0
                             || p.Name.IndexOf("journal", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(p => $"{p.PropertyType.Name} {p.Name}").ToList();
                bag["hero_methods_questy"] = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => (m.Name.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0
                              || m.Name.IndexOf("journal", StringComparison.OrdinalIgnoreCase) >= 0)
                             && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_"))
                    .Take(15).Select(m => m.Name).ToList();
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
