using System;
using HarmonyLib;
using Awaken.TG.Main.Fights.NPCs;
using Awaken.TG.Main.Locations.Actions;

namespace SpoilsOfTheSlain
{
    // When the player initiates dialogue with an NPC, treat it the same as killing them:
    // unlock the NPC's loot/inventory pool. For vendors this surfaces all their wares as
    // craftable; for quest NPCs it covers their personal inventory + corpse loot tables
    // (which are populated even on friendly NPCs since the game expects them to die in
    // some branches). Notification per-item is suppressed by default — vendor inventories
    // can produce 100+ popups in a single conversation.
    [HarmonyPatch(typeof(DialogueAction), nameof(DialogueAction.StartDialogue))]
    internal static class DialogueAction_StartDialogue_Patch
    {
        // Per-NPC dedupe so re-opening the same dialogue doesn't re-walk the pool. The pool
        // walk is idempotent (KnownItems is a HashSet), so this is purely a perf guard.
        // Reset on plugin reload only — not persisted, since the data layer it writes to is.
        private static readonly System.Collections.Generic.HashSet<string> _seenThisSession
            = new System.Collections.Generic.HashSet<string>();

        static void Postfix(DialogueAction __instance)
        {
            if (Plugin.Cfg == null || !Plugin.Cfg.UnlockOnDialogue.Value) return;
            try
            {
                var npc = __instance?.ParentModel?.TryGetElement<NpcElement>();
                if (npc == null) return;

                // Dedupe by NPC template name (matches Catalog.NpcLootPools key).
                var name = (npc.Template as NpcTemplate)?.name;
                if (string.IsNullOrEmpty(name)) return;
                if (!_seenThisSession.Add(name)) return;

                KillUnlock.UnlockFromNpc(npc, announce: Plugin.Cfg.NotifyOnDialogueUnlock.Value, source: "talked to");
            }
            catch (Exception e)
            {
                if (Plugin.Cfg.Verbose.Value)
                    Plugin.Log.LogWarning($"[DialogueUnlock] hook failed: {e.GetBaseException().Message}");
            }
        }
    }
}
