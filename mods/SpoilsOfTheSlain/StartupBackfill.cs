using System;
using System.Collections.Generic;
using System.Linq;
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
                $"[Startup] Backfill complete. Inverse-loot: {triggeredNpcs} bosses recognized → +{inverseUnlocks} items. " +
                $"Recipes issued: forge={RecipeIssuer.IssuedForge} alchemy={RecipeIssuer.IssuedAlchemy} cooking={RecipeIssuer.IssuedCooking}. " +
                $"KnownItems: {knownBefore} → {known.Count}.");
            return true;
        }
    }
}
