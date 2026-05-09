using System;
using Awaken.TG.Main.Crafting.HandCrafting.RecipeView;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.Attachments;

namespace BetterSortingCrafting
{
    // Live filter/sort selections for the crafting recipe grid, plus a
    // reference to the active grid so popup callbacks and the search bar
    // can apply changes back to the game model. Inventory uses the game's
    // OverrideFilter pipeline instead and tracks its own state inside
    // InventorySearchInjector.
    internal static class FilterState
    {
        public static WeaponSubFilter WeaponFilter = WeaponSubFilter.All;
        public static string SearchText = string.Empty;
        public static RecipeGridUI ActiveGrid;

        // Cached once — Enum.GetValues allocates a fresh array on every call,
        // and BuildFilterOptions iterates this on each popup open.
        public static readonly WeaponSubFilter[] AllValues =
            (WeaponSubFilter[])Enum.GetValues(typeof(WeaponSubFilter));

        public static void Reset()
        {
            WeaponFilter = WeaponSubFilter.All;
            SearchText = string.Empty;
        }
    }

    internal enum WeaponSubFilter
    {
        All,
        OneHanded,
        TwoHanded,
        Ranged,
        Shield,
        Arrow,
    }

    internal static class WeaponSubFilterExt
    {
        public static bool Matches(this WeaponSubFilter f, ItemTemplate t)
        {
            if (t == null) return false;
            switch (f)
            {
                case WeaponSubFilter.All:       return true;
                case WeaponSubFilter.OneHanded: return t.IsOneHanded && t.IsMelee;
                case WeaponSubFilter.TwoHanded: return t.IsTwoHanded && t.IsMelee;
                case WeaponSubFilter.Ranged:    return t.IsRanged;
                case WeaponSubFilter.Shield:    return t.IsShield;
                case WeaponSubFilter.Arrow:     return t.IsArrow;
                default: return true;
            }
        }

        public static string Label(this WeaponSubFilter f)
        {
            switch (f)
            {
                case WeaponSubFilter.All:       return "All weapons";
                case WeaponSubFilter.OneHanded: return "One-handed";
                case WeaponSubFilter.TwoHanded: return "Two-handed";
                case WeaponSubFilter.Ranged:    return "Ranged";
                case WeaponSubFilter.Shield:    return "Shields";
                case WeaponSubFilter.Arrow:     return "Arrows";
                default: return f.ToString();
            }
        }
    }

    // Helpers for fishing damage/armor off ItemTemplate (via its
    // ItemStatsAttachment MonoBehaviour, when present).
    internal static class StatsLookup
    {
        public static float MaxDamage(ItemTemplate t)
        {
            var stats = t?.GetAttachment<ItemStatsAttachment>();
            return stats != null ? stats.maxDamage : 0f;
        }

        public static float Armor(ItemTemplate t)
        {
            var stats = t?.GetAttachment<ItemStatsAttachment>();
            return stats != null ? stats.armor : 0f;
        }
    }
}
