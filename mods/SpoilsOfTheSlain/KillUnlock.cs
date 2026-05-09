using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.LootTables;
using Awaken.TG.Main.Fights.NPCs;
using Awaken.TG.Main.Locations.Attachments.Elements;

namespace SpoilsOfTheSlain
{
    // When an NPC dies, look up its loot pool from the prebuilt Catalog and unlock every
    // item in it — adds to KnownItems + issues recipes. Catalog must be built first
    // (StartupBackfill takes care of that on the first frame Hero is loaded).
    [HarmonyPatch(typeof(DeathElement), nameof(DeathElement.OnDeath))]
    internal static class DeathElement_OnDeath_Patch
    {
        static void Postfix(DeathElement __instance)
        {
            try
            {
                var npc = __instance?.ParentModel;
                if (npc == null) return;
                KillUnlock.UnlockFromNpc(npc);
            }
            catch (Exception e)
            {
                if (Plugin.Cfg.Verbose.Value)
                    Plugin.Log.LogWarning($"[KillUnlock] OnDeath hook failed: {e.GetBaseException().Message}");
            }
        }
    }

    internal static class KillUnlock
    {
        // Unlock every item in the NPC's loot/inventory pool. Used by:
        //   - kill hook (announce=true, source="killed")
        //   - dialogue hook (announce=Cfg.NotifyOnDialogueUnlock, source="talked to")
        //   - startup loaded-NPC backfill (announce=false, source="nearby")
        // Returns the number of items newly added to KnownItems (for logging by the caller).
        public static int UnlockFromNpc(NpcElement npc, bool announce = true, string source = "killed")
        {
            if (npc == null) return 0;
            if (!Catalog.IsBuilt) return 0;     // backfill hasn't completed yet — skip

            var template = npc.Template as NpcTemplate;
            if (template == null) return 0;
            var name = template.name;
            if (string.IsNullOrEmpty(name)) return 0;
            if (!Catalog.NpcLootPools.TryGetValue(name, out var pool)) return 0;

            var hero = Hero.Current;
            var heroItems = hero?.Element<HeroItems>();
            if (heroItems == null) return 0;
            var known = heroItems.KnownItems;
            if (known == null) return 0;

            int unlocked = 0;
            foreach (var guid in pool)
            {
                if (known.Contains(guid)) continue;
                if (!Catalog.ItemByGuid.TryGetValue(guid, out var item)) continue;
                int before = known.Count;
                heroItems.AddToKnownItems(item);
                if (known.Count > before) unlocked++;
                RecipeIssuer.Issue(item, announce: announce);
            }

            if (unlocked > 0 && Plugin.Cfg.LogDiscoveryGrowth.Value)
                Plugin.Log.LogInfo($"[KillUnlock] '{name}' {source} → +{unlocked} items unlocked.");
            return unlocked;
        }
    }
}
