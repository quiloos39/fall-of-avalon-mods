using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.LootTables;
using Awaken.TG.Main.Fights.NPCs;
using Awaken.TG.Main.Templates;
using Awaken.TG.MVC;

namespace LanternProbe
{
    // Inverse approach: if any item from a boss's loot table is in KnownItems, the boss
    // was killed → unlock the rest of the loot table. Retroactive without needing kill
    // state. Filters out generic mobs (loot table dropped by 3+ different NPCs) so we
    // don't unlock everything just because the user has Iron Ore.
    public static class InverseLootUnlock
    {
        // dryRun: report what WOULD be unlocked without actually adding to KnownItems
        public static string Run() => DoRun(dryRun: true);
        public static string RunCommit() => DoRun(dryRun: false);

        private static string DoRun(bool dryRun)
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var hero = Hero.Current;
                var heroItems = hero?.Element<HeroItems>();
                var known = heroItems?.KnownItems;
                if (known == null) { bag["err"] = "no KnownItems"; return JsonConvert.SerializeObject(bag, Formatting.Indented); }

                bag["known_before"] = known.Count;

                var provider = World.Services?.Get<TemplatesProvider>();
                var getAllOfType = typeof(TemplatesProvider).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                var allNpcs = getAllOfType?.MakeGenericMethod(typeof(NpcTemplate)).Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable;

                // Pass 1: build per-NPC loot pools, then count how many distinct NPCs drop each item
                var npcByName = new Dictionary<string, NpcTemplate>();
                var npcLootPools = new Dictionary<string, HashSet<string>>();   // npcName → set of item guids
                var dropCount = new Dictionary<string, int>();                  // itemGuid → # of NPCs that drop it
                int npcCount = 0;
                foreach (var npc in allNpcs ?? new object[0])
                {
                    var t = npc as NpcTemplate;
                    if (t == null) continue;
                    npcCount++;
                    var name = t.name;
                    if (string.IsNullOrEmpty(name)) continue;

                    var pool = new HashSet<string>();
                    AddPool(t.inventoryItems, pool);
                    if (t.corpseLootTables != null) foreach (var w in t.corpseLootTables) AddPool(w, pool);
                    if (t.lootTables != null) foreach (var w in t.lootTables) AddPool(w, pool);

                    if (pool.Count == 0) continue;
                    npcByName[name] = t;
                    npcLootPools[name] = pool;

                    foreach (var g in pool)
                    {
                        if (!dropCount.ContainsKey(g)) dropCount[g] = 0;
                        dropCount[g]++;
                    }
                }
                bag["npc_total"] = npcCount;
                bag["npc_with_loot"] = npcLootPools.Count;
                bag["distinct_droppable_items"] = dropCount.Count;

                // Pass 2: for each NPC, count how many of their drops are in user's KnownItems.
                // If 1+ of their drops is owned, treat as killed.
                // BUT: only count drops that are "rare" (dropped by ≤ 3 NPCs) — filters out
                // generic loot like Iron Ore that's in everyone's pool.
                var triggeredNpcs = new List<string>();
                var newUnlocks = new HashSet<string>();
                int rareThreshold = 3;
                foreach (var (name, pool) in npcLootPools.Select(kv => (kv.Key, kv.Value)))
                {
                    bool ownsRareFromThisNpc = pool.Any(g => known.Contains(g) && dropCount.GetValueOrDefault(g, 99) <= rareThreshold);
                    if (!ownsRareFromThisNpc) continue;
                    triggeredNpcs.Add(name);

                    // Mark every item in this NPC's pool as new-to-unlock if not already known
                    foreach (var g in pool)
                    {
                        if (!known.Contains(g)) newUnlocks.Add(g);
                    }
                }
                bag["triggered_npcs_count"] = triggeredNpcs.Count;
                bag["triggered_npcs_sample"] = triggeredNpcs.OrderBy(s => s).Take(20).ToList();
                bag["new_unlocks_count"] = newUnlocks.Count;

                // Show some example item names for the new unlocks
                var newUnlockNames = new List<string>();
                int n = 0;
                foreach (var g in newUnlocks)
                {
                    if (n >= 30) break;
                    var t = ResolveTemplate(g);
                    var iname = t?.ItemName;
                    if (!string.IsNullOrWhiteSpace(iname)) { newUnlockNames.Add(iname); n++; }
                }
                bag["new_unlock_sample"] = newUnlockNames;

                if (!dryRun)
                {
                    int added = 0;
                    foreach (var g in newUnlocks)
                    {
                        var t = ResolveTemplate(g);
                        if (t == null) continue;
                        int before = known.Count;
                        heroItems.AddToKnownItems(t);
                        if (known.Count > before) added++;
                    }
                    bag["committed_added"] = added;
                    bag["known_after"] = known.Count;
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static void AddPool(object wrapper, HashSet<string> pool)
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
                    var g = t.GUID;
                    if (!string.IsNullOrEmpty(g)) pool.Add(g);
                }
            }
            catch { }
        }

        private static ItemTemplate ExtractTemplate(object lootData)
        {
            if (lootData == null) return null;
            try
            {
                var t = lootData.GetType();
                foreach (var n in new[] { "ItemTemplate", "Template", "item" })
                {
                    var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                    if (p != null) { if (p.GetValue(lootData) is ItemTemplate it) return it; }
                    var f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) { if (f.GetValue(lootData) is ItemTemplate it2) return it2; }
                }
            }
            catch { }
            return null;
        }

        private static Dictionary<string, ItemTemplate> _itemByGuid;
        private static ItemTemplate ResolveTemplate(string guid)
        {
            if (_itemByGuid == null)
            {
                _itemByGuid = new Dictionary<string, ItemTemplate>();
                var provider = World.Services?.Get<TemplatesProvider>();
                var getAll = typeof(TemplatesProvider).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                var all = getAll?.MakeGenericMethod(typeof(ItemTemplate)).Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable;
                foreach (var x in all ?? new object[0])
                {
                    if (x is ItemTemplate it && !string.IsNullOrEmpty(it.GUID)) _itemByGuid[it.GUID] = it;
                }
            }
            _itemByGuid.TryGetValue(guid, out var v);
            return v;
        }
    }
}
