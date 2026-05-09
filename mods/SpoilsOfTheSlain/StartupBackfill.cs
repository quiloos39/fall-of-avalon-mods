using System;
using System.Collections.Generic;
using System.Linq;
using Awaken.TG.Main.Fights.NPCs;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Templates;
using Awaken.TG.MVC;

namespace SpoilsOfTheSlain
{
    // One-shot work that runs once Hero.Current is loaded:
    //   1. Build Catalog (item index, NPC loot pools, vanilla outcomes)
    //   2. Inverse-loot unlock (extends KnownItems based on rare drops player owns)
    //   3. Issue a recipe for every item currently in KnownItems that qualifies
    //
    // Idempotent: each step's "TryAdd" / "AddToKnownItems" silently no-ops on duplicates,
    // so re-running is safe (e.g. across save reloads in the same session).
    internal static class StartupBackfill
    {
        private const int RareThreshold = 3;
        private static bool _hasRun;

        public static bool TryRun()
        {
            if (_hasRun) return true;

            var hero = Hero.Current;
            if (hero == null) return false;     // not loaded yet, retry later
            var heroItems = hero.Element<HeroItems>();
            if (heroItems == null) return false;
            var known = heroItems.KnownItems;
            if (known == null) return false;

            if (!Catalog.Build()) return false;

            int knownBefore = known.Count;

            // Step 1.5: inventory scan — any item the player currently owns but that isn't in
            // KnownItems (e.g. relics picked up before this mod was installed) gets registered.
            // The discovery hooks only fire for fresh pickups, so without this, pre-mod loot
            // never qualifies for a recipe. Counted separately so the player can see if the
            // backfill actually pulled anything in.
            int inventoryAdds = 0, inventoryGemAdds = 0;
            try
            {
                foreach (var item in heroItems.Items)
                {
                    var template = item?.Template;
                    if (template == null) continue;
                    string g = null;
                    try { g = template.GUID; } catch { }
                    if (string.IsNullOrEmpty(g)) continue;
                    if (known.Contains(g)) continue;
                    int before = known.Count;
                    heroItems.AddToKnownItems(template);
                    if (known.Count > before)
                    {
                        inventoryAdds++;
                        try { if (template.IsGem) inventoryGemAdds++; } catch { }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Startup] Inventory scan failed: {e.GetBaseException().Message}");
            }

            // Step 2: inverse-loot — for each NPC where player owns a rare drop, unlock pool
            int triggeredNpcs = 0, inverseUnlocks = 0;
            foreach (var pool in Catalog.NpcLootPools.Values)
            {
                bool ownsRareFromThisNpc = false;
                foreach (var g in pool)
                {
                    if (!known.Contains(g)) continue;
                    if (Catalog.DropFanout.TryGetValue(g, out var c) && c <= RareThreshold)
                    { ownsRareFromThisNpc = true; break; }
                }
                if (!ownsRareFromThisNpc) continue;
                triggeredNpcs++;

                foreach (var g in pool)
                {
                    if (known.Contains(g)) continue;
                    if (!Catalog.ItemByGuid.TryGetValue(g, out var item)) continue;
                    int before = known.Count;
                    heroItems.AddToKnownItems(item);
                    if (known.Count > before) inverseUnlocks++;
                }
            }

            // Step 2.5: walk every NPC currently loaded in the scene and unlock their loot
            // pool. The game has no global "talked-to" registry, so this is the best we can
            // do for backfilling dialogue-based unlocks: it catches NPCs in the player's
            // current zone (which is where they likely are right after loading a save).
            // Future dialogues elsewhere fill in the rest naturally via the dialogue hook.
            // Quiet (no per-recipe popup) — could fire for hundreds of NPCs.
            int npcBackfillCount = 0, npcBackfillItems = 0;
            if (Plugin.Cfg.BackfillFromLoadedNpcs.Value)
            {
                try
                {
                    foreach (var npc in World.All<NpcElement>().ToArraySlow())
                    {
                        if (npc == null || npc.HasBeenDiscarded) continue;
                        int added = KillUnlock.UnlockFromNpc(npc, announce: false, source: "nearby");
                        if (added > 0) { npcBackfillCount++; npcBackfillItems += added; }
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Startup] NPC backfill failed: {e.GetBaseException().Message}");
                }
            }

            // Step 3: issue a recipe for every known item that qualifies
            int issuedBefore = RecipeIssuer.IssuedForge + RecipeIssuer.IssuedAlchemy + RecipeIssuer.IssuedCooking;
            foreach (var guid in known)
            {
                if (!Catalog.ItemByGuid.TryGetValue(guid, out var item)) continue;
                RecipeIssuer.Issue(item);
            }
            int issuedNow = (RecipeIssuer.IssuedForge + RecipeIssuer.IssuedAlchemy + RecipeIssuer.IssuedCooking) - issuedBefore;

            _hasRun = true;
            Plugin.Log.LogInfo(
                $"[Startup] Backfill complete. Inventory scan: +{inventoryAdds} items ({inventoryGemAdds} relics). " +
                $"Inverse-loot: {triggeredNpcs} bosses recognized → +{inverseUnlocks} items. " +
                $"Loaded NPCs: {npcBackfillCount} contributed → +{npcBackfillItems} items. " +
                $"Recipes issued: forge={RecipeIssuer.IssuedForge} alchemy={RecipeIssuer.IssuedAlchemy} cooking={RecipeIssuer.IssuedCooking}. " +
                $"KnownItems: {knownBefore} → {known.Count}.");
            return true;
        }
    }
}
