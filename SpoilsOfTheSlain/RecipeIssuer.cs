using System;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Crafting.Recipes;

namespace SpoilsOfTheSlain
{
    // Single point of entry: given an ItemTemplate, ensure ONE recipe exists for it at the
    // appropriate station. Idempotent — calling twice for the same template no-ops.
    //
    // Replaces the old "rebuild on cache invalidation" pattern. Now recipes are added
    // exactly once each, lazily, as items become known.
    internal static class RecipeIssuer
    {
        public static int IssuedForge;
        public static int IssuedAlchemy;
        public static int IssuedCooking;

        // announce=true pushes the vanilla recipe-learned notification when a NEW recipe is
        // added. Startup backfill leaves it false to avoid spamming hundreds of toasts on
        // game load; mid-session discovery / kill paths set it true.
        public static void Issue(ItemTemplate outcome, bool announce = false)
        {
            if (outcome == null) return;
            string guid = null;
            try { guid = outcome.GUID; } catch { }
            if (string.IsNullOrEmpty(guid)) return;

            // Pick station + bail early if the item doesn't qualify or we already have one
            var station = StationFor(outcome);
            if (!station.HasValue) return;
            var s = station.Value;

            if (RecipeRegistry.Contains(s, guid)) return;

            // Skip vanilla outcomes — vanilla already provides the recipe
            if (Catalog.VanillaOutcomes != null
                && Catalog.VanillaOutcomes.TryGetValue(s, out var vanilla)
                && vanilla.Contains(guid)) return;

            if (!StationFilter.IsCraftableAt(s, outcome)) return;
            if (!PassesQuestFilter(outcome)) return;

            var ings = CostGenerator.BuildIngredients(s, outcome);
            var recipe = RecipeFactory.Build(s, outcome, ings);
            if (recipe == null) return;

            if (RecipeRegistry.TryAdd(s, outcome, recipe))
            {
                if (s == RecipeFactory.Station.Forge) IssuedForge++;
                else if (s == RecipeFactory.Station.Alchemy) IssuedAlchemy++;
                else IssuedCooking++;

                if (announce && Plugin.Cfg.NotifyOnDiscovery.Value)
                {
                    try { ItemUtils.AnnounceGettingRecipe(recipe); }
                    catch (Exception e)
                    {
                        if (Plugin.Cfg.Verbose.Value)
                            Plugin.Log.LogWarning($"[Issue] Notify failed for {outcome.ItemName ?? outcome.name}: {e.GetBaseException().Message}");
                    }
                }
            }
        }

        // Routes an item to its station based on the game's own classification + tags.
        // Returns null if the item doesn't belong at any station we inject into.
        private static RecipeFactory.Station? StationFor(ItemTemplate t)
        {
            if (t == null) return null;
            try
            {
                if (Plugin.Cfg.InjectIntoForge.Value
                    && (t.IsWeapon || t.IsArmor || t.IsShield || t.IsRanged || t.IsArrow || t.IsJewelry))
                    return RecipeFactory.Station.Forge;
            }
            catch { }

            // Tag-based routing for alchemy/cooking
            try
            {
                var tags = t.Tags;
                if (tags == null) return null;

                if (Plugin.Cfg.InjectIntoAlchemy.Value)
                {
                    foreach (var tag in tags)
                    {
                        if (tag == null) continue;
                        if (tag == "item:potion" || tag == "item:potionHP" || tag == "item:potionMP"
                         || tag == "item:potionSP" || tag == "item:potionOther" || tag == "item:weapongrease"
                         || tag.StartsWith("potions:") || tag.StartsWith("alchemy:"))
                            return RecipeFactory.Station.Alchemy;
                    }
                }

                if (Plugin.Cfg.InjectIntoCooking.Value)
                {
                    bool hasDish = false, hasFood = false, hasEdible = false;
                    foreach (var tag in tags)
                    {
                        if (tag == "item:dish") hasDish = true;
                        else if (tag == "item:foodstuff") hasFood = true;
                        else if (tag == "item:edible") hasEdible = true;
                    }
                    if (hasDish || (hasFood && hasEdible)) return RecipeFactory.Station.Cooking;
                }
            }
            catch { }
            return null;
        }

        private static bool PassesQuestFilter(ItemTemplate t)
        {
            try
            {
                if (Plugin.Cfg.ExcludeQuestItems.Value)
                {
                    if (t.CannotBeDropped) return false;
                    var tags = t.Tags;
                    if (tags != null)
                    {
                        foreach (var tag in tags)
                            if (tag != null && tag.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0)
                                return false;
                    }
                }
            }
            catch { }
            return true;
        }
    }
}
