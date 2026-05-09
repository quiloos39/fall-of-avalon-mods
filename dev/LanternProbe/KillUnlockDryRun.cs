using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Walk all currently-loaded NpcElements, find dead ones, simulate v0.8's loot-table walk.
    // Tells us what items v0.8 WOULD unlock for kills in the current scene.
    //
    // Since we can also trigger this manually, this doubles as a "backfill scan" that the
    // user can run via probe in any scene where they've already killed bosses.
    public static class KillUnlockDryRun
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var npcElementType = ResolveType("Awaken.TG.Main.Fights.NPCs.NpcElement");
                var allMethod = worldType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "All" && m.IsGenericMethod && m.GetParameters().Length == 0);

                // Hero.Current sanity check
                var heroT = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hCurrent = heroT?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                bag["hero_present"] = hCurrent != null;

                // Try Resources.FindObjectsOfTypeAll for NpcElement (might be MonoBehaviour)
                try
                {
                    var byResources = UnityEngine.Resources.FindObjectsOfTypeAll(npcElementType);
                    bag["npc_via_resources"] = byResources?.Length ?? 0;
                }
                catch { }

                // World.All<NpcElement>() returns 0 because NpcElement is an Element of
                // Location, not a top-level Model. Walk Locations and collect their NpcElements.
                var locType = ResolveType("Awaken.TG.Main.Locations.Location");
                var allLocs = allMethod?.MakeGenericMethod(locType).Invoke(null, null) as System.Collections.IEnumerable;
                bag["loaded_locations"] = allLocs?.Cast<object>().Count() ?? 0;

                var npcs = new List<object>();
                foreach (var loc in allLocs ?? new object[0])
                {
                    try
                    {
                        var elementMethod = loc.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(m => m.Name == "TryGetElement" && m.IsGenericMethod && m.GetParameters().Length == 1);
                        var args = new object[] { null };
                        var ok = (bool)elementMethod.MakeGenericMethod(npcElementType).Invoke(loc, args);
                        if (ok && args[0] != null) npcs.Add(args[0]);
                    }
                    catch { }
                }
                bag["loaded_npc_elements"] = npcs.Count;

                int total = 0, dead = 0, named = 0;
                var deadNamed = new List<object>();
                foreach (var npc in npcs)
                {
                    total++;
                    bool alive = (bool)(npc.GetType().GetProperty("IsAlive")?.GetValue(npc) ?? true);
                    if (alive) continue;
                    dead++;

                    var template = npc.GetType().GetProperty("Template")?.GetValue(npc);
                    if (template == null) continue;
                    var tname = (template as UnityEngine.Object)?.name ?? "?";
                    // "Named" heuristic: not a generic mob template (those usually contain digits/colons in name)
                    bool isNamed = tname.Length > 0 && !char.IsDigit(tname[0]);
                    if (isNamed)
                    {
                        named++;
                        deadNamed.Add(npc);
                    }
                }
                bag["loaded_total_npcs"] = total;
                bag["loaded_dead_npcs"] = dead;
                bag["loaded_dead_named"] = named;

                // For each dead named NPC, simulate the unlock logic
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var heroItemsType = ResolveType("Awaken.TG.Main.Heroes.Items.HeroItems");
                var heroItems = hero?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0)
                    ?.MakeGenericMethod(heroItemsType).Invoke(hero, null);
                var known = heroItemsType.GetProperty("KnownItems")?.GetValue(heroItems) as HashSet<string>;

                int totalNewUnlocks = 0;
                var perNpc = new List<string>();
                int processed = 0;
                foreach (var npc in deadNamed)
                {
                    if (processed++ >= 8) break;     // sample first 8
                    var template = npc.GetType().GetProperty("Template")?.GetValue(npc);
                    if (template == null) continue;
                    var tname = (template as UnityEngine.Object)?.name ?? "?";

                    var possibleItems = new HashSet<string>();
                    var itemNames = new List<string>();

                    // inventoryItems (single LootTableWrapper)
                    var invField = template.GetType().GetField("inventoryItems", BindingFlags.Public | BindingFlags.Instance);
                    var inv = invField?.GetValue(template);
                    WalkWrapper(inv, possibleItems, itemNames);

                    // corpseLootTables (List<LootTableWrapper>)
                    var corpseField = template.GetType().GetField("corpseLootTables", BindingFlags.Public | BindingFlags.Instance);
                    var corpse = corpseField?.GetValue(template) as System.Collections.IEnumerable;
                    foreach (var w in corpse ?? new object[0]) WalkWrapper(w, possibleItems, itemNames);

                    // lootTables (List<LootTableWrapper>)
                    var lootField = template.GetType().GetField("lootTables", BindingFlags.Public | BindingFlags.Instance);
                    var loot = lootField?.GetValue(template) as System.Collections.IEnumerable;
                    foreach (var w in loot ?? new object[0]) WalkWrapper(w, possibleItems, itemNames);

                    int newOnes = possibleItems.Count(g => known == null || !known.Contains(g));
                    totalNewUnlocks += newOnes;
                    perNpc.Add($"'{tname}' → {possibleItems.Count} items (would add {newOnes} new). Sample: [{string.Join(", ", itemNames.Take(5))}]");
                }
                bag["sampled_npcs"] = perNpc;
                bag["estimated_new_unlocks"] = totalNewUnlocks;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static void WalkWrapper(object wrapper, HashSet<string> guidSet, List<string> nameList)
        {
            if (wrapper == null) return;
            try
            {
                var lootTableMethod = wrapper.GetType().GetMethod("LootTable", BindingFlags.Public | BindingFlags.Instance);
                var lootTable = lootTableMethod?.Invoke(wrapper, new object[] { null });
                if (lootTable == null) return;

                var editorPop = lootTable.GetType().GetMethod("EDITOR_PopLootData",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (editorPop == null) return;

                var data = editorPop.Invoke(lootTable, null) as System.Collections.IEnumerable;
                if (data == null) return;

                foreach (var d in data)
                {
                    var t = ExtractTemplate(d);
                    if (t == null) continue;
                    var guid = t.GetType().GetProperty("GUID")?.GetValue(t) as string;
                    if (string.IsNullOrEmpty(guid)) continue;
                    if (guidSet.Add(guid))
                    {
                        var n = t.GetType().GetProperty("ItemName")?.GetValue(t)?.ToString();
                        if (!string.IsNullOrEmpty(n)) nameList.Add(n);
                    }
                }
            }
            catch { }
        }

        private static object ExtractTemplate(object lootData)
        {
            if (lootData == null) return null;
            try
            {
                var t = lootData.GetType();
                foreach (var n in new[] { "ItemTemplate", "Template", "item" })
                {
                    var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                    if (p != null) { var v = p.GetValue(lootData); if (v != null) return v; }
                    var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) { var v = f.GetValue(lootData); if (v != null) return v; }
                }
            }
            catch { }
            return null;
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
