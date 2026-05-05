using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.LootTables;
using Awaken.TG.Main.Fights.NPCs;
using Awaken.TG.Main.Templates;
using Awaken.TG.MVC;

namespace SpoilsOfTheSlain
{
    // Read-only world catalogs built once, then queried for the rest of the session.
    // Eliminates the per-call reflection cost of TemplatesProvider.GetAllOfType.
    internal static class Catalog
    {
        public static Dictionary<string, ItemTemplate> ItemByGuid { get; private set; }
        public static List<NpcTemplate> AllNpcs { get; private set; }
        // npcName → set of item GUIDs that NPC can drop (inventory + corpse + regular loot)
        public static Dictionary<string, HashSet<string>> NpcLootPools { get; private set; }
        // item GUID → number of distinct NPCs that drop it
        public static Dictionary<string, int> DropFanout { get; private set; }
        // outcome GUIDs that vanilla recipes already cover, per station
        public static Dictionary<RecipeFactory.Station, HashSet<string>> VanillaOutcomes { get; private set; }

        public static bool IsBuilt { get; private set; }

        public static bool Build()
        {
            if (IsBuilt) return true;
            try
            {
                var provider = World.Services?.Get<TemplatesProvider>();
                if (provider == null) return false;

                var getAllOfType = typeof(TemplatesProvider).GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                if (getAllOfType == null) return false;

                // ItemByGuid
                ItemByGuid = new Dictionary<string, ItemTemplate>(4096);
                foreach (var x in (getAllOfType.MakeGenericMethod(typeof(ItemTemplate))
                    .Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable) ?? new object[0])
                {
                    if (x is ItemTemplate it && !string.IsNullOrEmpty(it.GUID)) ItemByGuid[it.GUID] = it;
                }

                // NPCs + loot pools + drop fanout
                AllNpcs = new List<NpcTemplate>(1024);
                NpcLootPools = new Dictionary<string, HashSet<string>>(1024);
                DropFanout = new Dictionary<string, int>(4096);

                foreach (var x in (getAllOfType.MakeGenericMethod(typeof(NpcTemplate))
                    .Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable) ?? new object[0])
                {
                    if (!(x is NpcTemplate t)) continue;
                    AllNpcs.Add(t);
                    var name = t.name;
                    if (string.IsNullOrEmpty(name)) continue;

                    var pool = new HashSet<string>();
                    AddPool(t.inventoryItems, pool);
                    if (t.corpseLootTables != null) foreach (var w in t.corpseLootTables) AddPool(w, pool);
                    if (t.lootTables != null) foreach (var w in t.lootTables) AddPool(w, pool);
                    if (pool.Count == 0) continue;

                    NpcLootPools[name] = pool;
                    foreach (var g in pool)
                    {
                        if (!DropFanout.ContainsKey(g)) DropFanout[g] = 0;
                        DropFanout[g]++;
                    }
                }

                // VanillaOutcomes per station — what the game already supplies as recipes.
                // Used to dedupe so we don't generate duplicates of vanilla recipes.
                VanillaOutcomes = new Dictionary<RecipeFactory.Station, HashSet<string>>
                {
                    [RecipeFactory.Station.Forge]   = HarvestVanillaOutcomes("Awaken.TG.Main.Crafting.HandCrafting.HandcraftingTemplate", provider, getAllOfType),
                    [RecipeFactory.Station.Alchemy] = HarvestVanillaOutcomes("Awaken.TG.Main.Crafting.AlchemyCrafting.AlchemyTemplate",   provider, getAllOfType),
                    [RecipeFactory.Station.Cooking] = HarvestVanillaOutcomes("Awaken.TG.Main.Crafting.Cooking.CookingTemplate",           provider, getAllOfType),
                };

                IsBuilt = true;
                Plugin.Log.LogInfo($"[Catalog] Built: {ItemByGuid.Count} items, {AllNpcs.Count} NPCs ({NpcLootPools.Count} with loot), " +
                                   $"vanilla forge outcomes={VanillaOutcomes[RecipeFactory.Station.Forge].Count}.");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Catalog] Build failed: {e.GetBaseException().Message}");
                return false;
            }
        }

        private static HashSet<string> HarvestVanillaOutcomes(string templateTypeName, TemplatesProvider provider, MethodInfo getAllOfType)
        {
            var set = new HashSet<string>();
            var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(templateTypeName, false)).FirstOrDefault(x => x != null);
            if (t == null) return set;

            foreach (var ct in (getAllOfType.MakeGenericMethod(t).Invoke(provider, new object[] { TemplateTypeFlag.Regular }) as System.Collections.IEnumerable) ?? new object[0])
            {
                var recipes = ct.GetType().GetProperty("Recipes")?.GetValue(ct) as System.Collections.IEnumerable;
                foreach (var r in recipes ?? new object[0])
                {
                    // skip our own (in case Catalog is rebuilt mid-session)
                    var rname = (r as UnityEngine.Object)?.name ?? "";
                    if (rname.StartsWith("SpoilsOfTheSlain_")) continue;
                    if (r is Awaken.TG.Main.Crafting.Recipes.IRecipe ir)
                    {
                        var g = ir.Outcome?.GUID;
                        if (!string.IsNullOrEmpty(g)) set.Add(g);
                    }
                }
            }
            return set;
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
    }
}
