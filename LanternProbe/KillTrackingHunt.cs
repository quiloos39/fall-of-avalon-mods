using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // The user is right: if you kill a named boss, the game remembers (no respawn, quest completes).
    // So death MUST be persisted somewhere. Hunting for:
    //   1. Per-NPC death flags / NpcElement.Alive / IsDead / Killed
    //   2. Story flags written on NPC death (FactsTracker pattern)
    //   3. Per-template kill counters (e.g. "you've killed 47 goblins")
    //   4. Death events / hooks we can subscribe to
    //   5. NpcTemplate / NpcLootTable wiring (so we can know which template drops what)
    public static class KillTrackingHunt
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var tgAsms = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && (n.StartsWith("TG.") || n.StartsWith("Awaken.")); })
                    .ToList();
                var allTypes = tgAsms.SelectMany(SafeGetTypes).Where(t => t != null).ToList();

                // 1. Death-related types (excluding compiler-generated state machines)
                var deathTypes = allTypes.Where(t =>
                    !t.Name.Contains("<")
                    && (t.Name.IndexOf("Death", StringComparison.OrdinalIgnoreCase) >= 0
                     || t.Name.IndexOf("Killer", StringComparison.OrdinalIgnoreCase) >= 0
                     || t.Name.IndexOf("DeadBody", StringComparison.OrdinalIgnoreCase) >= 0
                     || t.Name.Equals("Health", StringComparison.OrdinalIgnoreCase)))
                    .Select(t => t.FullName).OrderBy(n => n).Take(60).ToList();
                bag["death_types"] = deathTypes;

                // 2. NPC core types
                var npcTypes = allTypes.Where(t =>
                    !t.Name.Contains("<")
                    && (t.Name == "NpcElement" || t.Name == "NpcTemplate" || t.Name == "NpcDeath"
                     || t.Name == "NpcAliveStatus" || t.Name == "AliveStats" || t.Name == "NpcStats"
                     || t.Name == "AliveDespawnElement" || t.Name == "Alive" || t.Name.StartsWith("DeathInfo")))
                    .Select(t => t.FullName).OrderBy(n => n).Take(40).ToList();
                bag["npc_core_types"] = npcTypes;

                // 3. Inspect NpcElement deeply
                var npcElement = ResolveType("Awaken.TG.Main.AI.NpcElement")
                              ?? allTypes.FirstOrDefault(t => t.Name == "NpcElement");
                if (npcElement != null)
                {
                    bag["NpcElement_full"] = npcElement.FullName;
                    bag["NpcElement_props_aliveDeath"] = npcElement.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(p => p.Name.IndexOf("alive", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.IndexOf("dead", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.IndexOf("kill", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.IndexOf("health", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(30).ToList();
                    bag["NpcElement_fields_aliveDeath"] = npcElement.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(f => f.Name.IndexOf("alive", StringComparison.OrdinalIgnoreCase) >= 0
                                 || f.Name.IndexOf("dead", StringComparison.OrdinalIgnoreCase) >= 0
                                 || f.Name.IndexOf("kill", StringComparison.OrdinalIgnoreCase) >= 0
                                 || f.Name.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0
                                 || f.Name.IndexOf("loot", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(30).ToList();
                    bag["NpcElement_events"] = npcElement.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(e => $"{Pretty(e.EventHandlerType)} {e.Name}").Take(30).ToList();
                }

                // 4. Look for "killed" / "defeated" event types or dispatcher classes
                var killEventTypes = allTypes.Where(t =>
                    !t.Name.Contains("<") && !t.Name.Contains("Marker") && !t.Name.Contains("Camera")
                    && (t.Name.IndexOf("Killed", StringComparison.OrdinalIgnoreCase) >= 0
                     || t.Name.IndexOf("Defeated", StringComparison.OrdinalIgnoreCase) >= 0
                     || t.Name.IndexOf("OnDeath", StringComparison.OrdinalIgnoreCase) >= 0
                     || t.Name.IndexOf("DiedEvent", StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(t => t.FullName).OrderBy(n => n).Take(40).ToList();
                bag["kill_event_types"] = killEventTypes;

                // 5. Hero element types — full list (since first probe failed to find HeroItems path)
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero != null)
                {
                    var elementsProp = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Elements" && m.GetParameters().Length == 0 && !m.IsGenericMethodDefinition);
                    if (elementsProp != null)
                    {
                        try
                        {
                            var elements = elementsProp.Invoke(hero, null) as System.Collections.IEnumerable;
                            if (elements != null)
                            {
                                bag["hero_elements_actual"] = elements.Cast<object>()
                                    .Select(e => e.GetType().FullName).Distinct().OrderBy(n => n).ToList();
                            }
                        }
                        catch (Exception e) { bag["hero_elements_err"] = e.Message; }
                    }

                    // Try GetMethod approach
                    var elementsGetter = hero.GetType().GetProperty("Elements", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (elementsGetter != null)
                    {
                        try
                        {
                            var els = elementsGetter.GetValue(hero) as System.Collections.IEnumerable;
                            if (els != null)
                            {
                                bag["hero_elements_via_prop"] = els.Cast<object>()
                                    .Select(e => e.GetType().FullName).Distinct().OrderBy(n => n).ToList();
                            }
                        }
                        catch (Exception e) { bag["hero_elements_via_prop_err"] = e.Message; }
                    }
                }

                // 6. FactsTracker / Story flags — find the tracker and inspect
                var factsTracker = allTypes.FirstOrDefault(t => t.Name == "FactsTracker")
                                ?? allTypes.FirstOrDefault(t => t.Name == "StoryFlags")
                                ?? allTypes.FirstOrDefault(t => t.Name == "ContextualFacts");
                if (factsTracker != null)
                {
                    bag["factsTracker_type"] = factsTracker.FullName;
                    bag["factsTracker_methods"] = factsTracker.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .Take(40).ToList();
                }

                // 7. Hero kill counter / journal bestiary — does it record per-template?
                var bestiary = ResolveType("Awaken.TG.Main.Heroes.CharacterSheet.Journal.Tabs.JournalBestiary");
                if (bestiary != null)
                {
                    bag["bestiary_methods"] = bestiary.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .Take(30).ToList();
                    bag["bestiary_fields"] = bestiary.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(30).ToList();
                }

                // 8. Look for "Memory" / "Memories" — the game might call its persistent fact store this
                var memTypes = allTypes.Where(t =>
                    !t.Name.Contains("<")
                    && (t.Name == "Memory" || t.Name == "Memories" || t.Name == "MemoryContext"
                     || t.Name == "MemoriesData" || t.Name == "GlobalMemory"
                     || t.Name == "ContextualFacts" || t.Name == "WorldMemory"))
                    .Select(t => t.FullName).OrderBy(n => n).ToList();
                bag["memory_types"] = memTypes;

                // 9. ContextualFacts — has Get/Set patterns? It's already in our types list.
                var ctxFacts = ResolveType("Awaken.TG.Main.Memories.ContextualFacts");
                if (ctxFacts != null)
                {
                    bag["ContextualFacts_methods"] = ctxFacts.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .Take(30).ToList();
                    bag["ContextualFacts_fields"] = ctxFacts.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(30).ToList();
                }

                // 10. The DeathInfo / death dispatcher — usually has a static event
                var deathType = ResolveType("Awaken.TG.Main.Fights.DeathInfo")
                             ?? allTypes.FirstOrDefault(t => t.Name == "DeathInfo")
                             ?? allTypes.FirstOrDefault(t => t.Name == "Health");
                if (deathType != null)
                {
                    bag["DeathInfo_full"] = deathType.FullName;
                    bag["DeathInfo_props"] = deathType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(30).ToList();
                    bag["DeathInfo_events"] = deathType.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .Select(e => $"{Pretty(e.EventHandlerType)} {e.Name}").Take(20).ToList();
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch { return Array.Empty<Type>(); }
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
