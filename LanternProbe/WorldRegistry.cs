using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Walk World's internal domain storage to enumerate every registered Model type +
    // count. Tells us what's actually loaded so we know what type to target.
    public static class WorldRegistry
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var worldType = ResolveType("Awaken.TG.MVC.World");
                if (worldType == null) { bag["err"] = "no World"; return JsonConvert.SerializeObject(bag, Formatting.Indented); }

                // Look for static fields/properties that hold the world's storage
                var staticMembers = worldType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();
                staticMembers.AddRange(worldType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Select(p => $"{Pretty(p.PropertyType)} {p.Name}"));
                bag["World_static_members"] = staticMembers;

                // Try World.AllModelTypes or similar to get registered model types
                var instanceMethods = worldType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name.StartsWith("All") || m.Name == "Models" || m.Name == "Domain")
                    .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                    .ToList();
                bag["World_static_methods"] = instanceMethods;

                // Try Hero.Current and walk its elements / parent
                var heroT = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroT?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                bag["hero_present"] = hero != null;
                if (hero != null)
                {
                    bag["hero_type"] = hero.GetType().FullName;
                    var elementsProp = hero.GetType().GetProperty("Elements", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var elements = elementsProp?.GetValue(hero) as System.Collections.IEnumerable;
                    var elTypes = new List<string>();
                    if (elements != null)
                    {
                        foreach (var el in elements)
                        {
                            elTypes.Add(el.GetType().Name);
                            if (elTypes.Count >= 30) break;
                        }
                    }
                    bag["hero_elements"] = elTypes;

                    // ParentModel of Hero?
                    var parentProp = hero.GetType().GetProperty("ParentModel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var parent = parentProp?.GetValue(hero);
                    bag["hero_parentModel_type"] = parent?.GetType().FullName;
                }

                // Resources fallback for NpcElement
                var npcType = ResolveType("Awaken.TG.Main.Fights.NPCs.NpcElement");
                if (npcType != null)
                {
                    try
                    {
                        var byResAll = UnityEngine.Resources.FindObjectsOfTypeAll(npcType);
                        bag["npc_via_resources_findAll"] = byResAll?.Length ?? 0;
                    }
                    catch (Exception e) { bag["npc_resources_err"] = e.Message; }
                }

                // Try MonoBehaviour scan
                try
                {
                    var allMb = UnityEngine.Object.FindObjectsOfType<UnityEngine.MonoBehaviour>();
                    var npcMbs = allMb.Where(m => m != null && m.GetType().Name.Contains("Npc")).Select(m => m.GetType().Name).Distinct().Take(20).ToList();
                    bag["mb_with_Npc_in_name"] = npcMbs;
                    bag["total_mb_count"] = allMb.Length;
                }
                catch (Exception e) { bag["mb_scan_err"] = e.Message; }

                // Try Hero.CurrentDomain if exists
                try
                {
                    var domainProp = hero?.GetType().GetProperty("CurrentDomain", BindingFlags.Public | BindingFlags.Instance);
                    var domain = domainProp?.GetValue(hero);
                    if (domain != null)
                    {
                        bag["hero_currentDomain_type"] = domain.GetType().FullName;
                        // Try domain.AllInstances<T>() or similar
                        var domainMethods = domain.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Where(m => !m.IsSpecialName && m.GetParameters().Length <= 1)
                            .Select(m => m.Name).Distinct().Take(20).ToList();
                        bag["hero_domain_methods"] = domainMethods;
                    }
                }
                catch { }
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

        private static string Pretty(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.Name;
            return t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(g => g.Name)) + ">";
        }
    }
}
