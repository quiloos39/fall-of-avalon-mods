using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // The drop side: when items enter the world (NPC death drops, container contents,
    // pickup spawns), so we can record discovery even if AutoCollect doesn't grab them.
    // We also need the inventory-add hook for quest gifts and the merchant-sell hook.
    public static class DropHookHunt
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // 1. NPCItemDroppedElement — fired when a corpse drops items
                Inspect(bag, "NPCItemDroppedElement", "Awaken.TG.Main.Heroes.Combat.NPCItemDroppedElement");

                // 2. DroppedItemSpawner — generic loot spawn
                Inspect(bag, "DroppedItemSpawner", "Awaken.TG.Main.Heroes.Items.DroppedItemSpawner");

                // 3. DroppedItemData — what's in a drop
                Inspect(bag, "DroppedItemData", "Awaken.TG.Main.Heroes.Items.DroppedItemData");

                // 4. HeroItems — inventory add hook
                Inspect(bag, "HeroItems", "Awaken.TG.Main.Heroes.Items.HeroItems");

                // 5. ContainerInventory — chest/lootable contents
                Inspect(bag, "ContainerInventory", "Awaken.TG.Main.Locations.Containers.ContainerInventory");

                // 6. ItemSpawningData — earlier scan listed it
                Inspect(bag, "ItemSpawningData", "Awaken.TG.Main.Heroes.Items.LootTables.ItemSpawningData");

                // 7. LootTableAsset — for inferring drops without watching
                Inspect(bag, "LootTableAsset", "Awaken.TG.Main.Heroes.Items.LootTables.LootTableAsset");

                // 8. Merchant / Shop / Sell types — find sell hook
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && (n.StartsWith("TG.") || n.StartsWith("Awaken.")); })
                    .SelectMany(SafeGetTypes).Where(t => t != null && !t.Name.Contains("<"))
                    .ToList();

                bag["merchant_types"] = allTypes.Where(t =>
                    t.Name.IndexOf("Merchant", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Shop", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Trade", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.Equals("Sell", StringComparison.OrdinalIgnoreCase)
                    || t.Name.IndexOf("Vendor", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(t => t.FullName).OrderBy(n => n).Take(60).ToList();

                // 9. Item / Items Add methods — find the inventory-add entry point
                var heroItems = ResolveType("Awaken.TG.Main.Heroes.Items.HeroItems");
                if (heroItems != null)
                {
                    bag["HeroItems_addLikeMethods"] = heroItems.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(m => m.Name.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0
                                 || m.Name.IndexOf("Receive", StringComparison.OrdinalIgnoreCase) >= 0
                                 || m.Name.IndexOf("Pickup", StringComparison.OrdinalIgnoreCase) >= 0
                                 || m.Name.IndexOf("Acquire", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .Take(40).ToList();
                }

                // 10. Item entity (the runtime instance) — has a constructor we could patch
                var item = ResolveType("Awaken.TG.Main.Heroes.Items.Item");
                if (item != null)
                {
                    bag["Item_full"] = item.FullName;
                    bag["Item_constructors"] = item.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(c => $"({string.Join(",", c.GetParameters().Select(p => Pretty(p.ParameterType)))})").Take(10).ToList();
                    bag["Item_propsForKeyAndOwner"] = item.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(p => p.Name.IndexOf("Template", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.IndexOf("Owner", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.IndexOf("Quantity", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
                        .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(20).ToList();
                }

                // 11. Confirm ContextualFacts.GetAll() works on a live instance — find one and dump key sample
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero != null)
                {
                    var elementsProp = hero.GetType().GetProperty("Elements", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var elements = elementsProp?.GetValue(hero) as System.Collections.IEnumerable;
                    if (elements != null)
                    {
                        var elList = elements.Cast<object>().ToList();
                        bag["hero_element_count"] = elList.Count;

                        // Look for any element that contains a ContextualFacts inside
                        var ctxFactsType = ResolveType("Awaken.TG.Main.Memories.ContextualFacts");
                        foreach (var el in elList)
                        {
                            var elT = el.GetType();
                            foreach (var f in elT.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                            {
                                if (f.FieldType == ctxFactsType || (f.FieldType.Name == "ContextualFacts"))
                                {
                                    try
                                    {
                                        var cf = f.GetValue(el);
                                        if (cf == null) continue;
                                        var getAll = ctxFactsType.GetMethod("GetAll", BindingFlags.Public | BindingFlags.Instance);
                                        var allFacts = getAll?.Invoke(cf, null) as System.Collections.IEnumerable;
                                        if (allFacts == null) continue;
                                        var sample = new List<string>();
                                        int n = 0;
                                        foreach (var kvp in allFacts)
                                        {
                                            var key = kvp.GetType().GetProperty("Key")?.GetValue(kvp);
                                            var val = kvp.GetType().GetProperty("Value")?.GetValue(kvp);
                                            sample.Add($"{key} => {Truncate(val?.ToString(), 60)}");
                                            if (++n >= 30) break;
                                        }
                                        bag["facts_via_" + elT.Name + "." + f.Name] = sample;
                                        bag["facts_total_via_" + elT.Name + "." + f.Name] = n;
                                    }
                                    catch (Exception e) { bag["facts_err_" + elT.Name] = e.Message; }
                                }
                            }
                        }
                    }
                }

                // 12. World-level facts. Some games keep facts global rather than on hero.
                var worldType = ResolveType("Awaken.TG.MVC.World");
                if (worldType != null)
                {
                    var services = worldType.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (services != null)
                    {
                        // Find any service of type ContextualFacts or that contains one
                        var allMethod = services.GetType().GetMethods()
                            .FirstOrDefault(m => m.Name == "All" && m.GetParameters().Length == 0 && !m.IsGenericMethodDefinition);
                        if (allMethod != null)
                        {
                            try
                            {
                                var all = allMethod.Invoke(services, null) as System.Collections.IEnumerable;
                                bag["world_services"] = all?.Cast<object>().Select(s => s.GetType().FullName).Distinct().Take(40).ToList();
                            }
                            catch (Exception e) { bag["world_services_err"] = e.Message; }
                        }
                    }
                }

                // 13. ConditionNpcKilled — dive in to see how it queries kill state
                var condKilled = ResolveType("Awaken.TG.Main.Locations.Clearing.ConditionNpcKilled");
                if (condKilled != null)
                {
                    bag["ConditionNpcKilled_full"] = condKilled.FullName;
                    bag["ConditionNpcKilled_fields"] = condKilled.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(20).ToList();
                    bag["ConditionNpcKilled_methods"] = condKilled.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .Take(20).ToList();
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static void Inspect(Dictionary<string, object> bag, string key, string fullName)
        {
            var t = ResolveType(fullName);
            if (t == null) { bag[key + "_status"] = "TYPE_NOT_FOUND"; return; }
            bag[key + "_full"] = t.FullName;
            bag[key + "_methods"] = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                .Take(25).ToList();
            bag[key + "_fields"] = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(20).ToList();
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

        private static string Truncate(string s, int n) => s == null ? "null" : (s.Length > n ? s.Substring(0, n) + "…" : s);
    }
}
