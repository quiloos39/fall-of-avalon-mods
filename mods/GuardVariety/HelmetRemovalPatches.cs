using System;
using System.Linq;
using HarmonyLib;
using Awaken.TG.Main.Character;
using Awaken.TG.Main.Fights.Factions.Crimes;
using Awaken.TG.Main.Fights.NPCs;
using Awaken.TG.Main.Heroes.Items;

namespace GuardVariety
{
    // Strip helmets off guard NPCs so the randomized face is visible.
    // Mirrors Awaken.TG.EditorOnly.DummifyNPCs (line 51-60 in refs):
    //
    //   from item in npc.Inventory.Items.ToArray()
    //   where item.EquipmentType == EquipmentType.Helmet
    //   ...
    //   if (item.IsEquipped) npc.Inventory.Unequip(item);
    //   item.Discard();
    //
    // We hook NpcInitializer.NotifyVisualLoaded — by the time it returns,
    // FullyInitEnemyBaseClass has called OnInventoryInitialized so all
    // template-driven items are in the inventory. Discard is safe for loot
    // because death drops resolve through Npc.Template.Loot (a SearchAction
    // built in NpcInitializer.AddSavedElementsOnInitialize:108), not the
    // live inventory.
    [HarmonyPatch(typeof(NpcInitializer), "NotifyVisualLoaded")]
    internal static class NpcInitializer_NotifyVisualLoaded_HelmetRemoval_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)] // run after randomization & probe postfixes
        private static void Postfix(NpcInitializer __instance)
        {
            try
            {
                if (Plugin.Cfg == null || !Plugin.Cfg.Enabled.Value) return;
                if (!Plugin.Cfg.RemoveHelmet.Value) return;

                var npc = __instance?.Npc;
                if (npc?.Template == null) return;

                if (!IsTargetArchetype(npc.Template.CrimeReactionArchetype)) return;
                if (npc.IsUnique && !Plugin.Cfg.RandomizeUniqueGuards.Value) return;

                if (Plugin.Cfg.Verbose.Value) LogVisualConfirmation(npc);

                RemoveHelmets(npc);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[GuardVariety] Helmet removal failed: {e.GetBaseException().Message}");
            }
        }

        private static bool IsTargetArchetype(CrimeReactionArchetype archetype)
        {
            if (archetype == CrimeReactionArchetype.Guard) return true;
            if (Plugin.Cfg.AlsoRandomizeDefenders.Value && archetype == CrimeReactionArchetype.Defender) return true;
            return false;
        }

        private static void RemoveHelmets(NpcElement npc)
        {
            var inventory = npc.Inventory;
            if (inventory?.Items == null) return;

            // .ToArray() defends against concurrent-modification when Discard
            // mutates the underlying collection.
            var helmets = inventory.Items
                .Where(it => it != null && it.EquipmentType == EquipmentType.Helmet)
                .ToArray();

            if (helmets.Length == 0) return;

            int removed = 0;
            foreach (var item in helmets)
            {
                try
                {
                    if (item.IsEquipped)
                    {
                        inventory.Unequip(item);
                    }
                    item.Discard();
                    removed++;
                }
                catch (Exception inner)
                {
                    Plugin.Log.LogWarning(
                        $"[GuardVariety] Could not remove helmet '{item?.Template?.name ?? "<?>"}' " +
                        $"from {SafeName(npc)}: {inner.GetBaseException().Message}");
                }
            }

            if (removed > 0 && Plugin.Cfg.Verbose.Value)
            {
                Plugin.Log.LogInfo($"[GuardVariety] Removed {removed} helmet(s) from {SafeName(npc)}");
            }
        }

        private static string SafeName(NpcElement npc)
        {
            try { return npc?.Name ?? npc?.Template?.name ?? "<unnamed>"; }
            catch { return "<error>"; }
        }

        // Logs once per (template, loadedVisualName) pair so the log doesn't drown
        // in repeated lines for the same template. Surfaces enough info to tell
        // whether the swap actually changed the visual that loaded.
        private static readonly System.Collections.Generic.HashSet<string> _seenVisuals = new System.Collections.Generic.HashSet<string>();
        private static void LogVisualConfirmation(NpcElement npc)
        {
            try
            {
                var go = npc.SpawnedVisualPrefab;
                if (go == null) return;

                string templateName = npc.Template?.name ?? "?";
                string visualName = go.name ?? "?";
                string addr = npc._visualPrefab?.Address ?? "?";

                string key = templateName + "|" + visualName;
                if (!_seenVisuals.Add(key)) return; // dedupe per session

                var marker = go.GetComponentInChildren<Awaken.TG.Main.Fights.NPCs.NpcGenderMarker>(true);
                string markerGender = marker != null ? marker.Gender.ToString() : "<none>";

                Plugin.Log.LogInfo(
                    $"[GuardVariety][Visual] guard template='{templateName}' " +
                    $"loaded prefab GameObject='{visualName}' " +
                    $"address={addr} markerGender={markerGender}");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[GuardVariety][Visual] log failed: {e.GetBaseException().Message}");
            }
        }
    }
}
