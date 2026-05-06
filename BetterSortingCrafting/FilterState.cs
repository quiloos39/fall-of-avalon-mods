using System;
using Awaken.TG.Main.Crafting.HandCrafting.RecipeView;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.Attachments;

namespace BetterSortingCrafting
{
    // Live filter/sort selections, plus references to the active grid so the
    // popup callbacks can apply changes back to the game model.
    internal static class FilterState
    {
        public static RecipeFilter ItemFilter = RecipeFilter.All;
        public static string SearchText = string.Empty;
        public static RecipeGridUI ActiveGrid;

        public static void Reset()
        {
            ItemFilter = RecipeFilter.All;
            SearchText = string.Empty;
        }
    }

    // Single filter rule = a labelled predicate over ItemTemplate, grouped into
    // a category so the filter popup can be a two-level menu (top-level
    // category → sub-popup of specific filters in that category).
    //
    // Reference equality is used to detect "active filter" — every filter is
    // a static singleton instance.
    internal sealed class RecipeFilter
    {
        public string Group { get; }
        public string Label { get; }
        private readonly Func<ItemTemplate, bool> _predicate;

        private RecipeFilter(string group, string label, Func<ItemTemplate, bool> predicate)
        {
            Group = group;
            Label = label;
            _predicate = predicate;
        }

        public bool Matches(ItemTemplate t) => t != null && _predicate(t);

        public string PromptLabel =>
            string.IsNullOrEmpty(Group) ? Label : $"{Group} / {Label}";

        public static readonly RecipeFilter All = new RecipeFilter("", "All", _ => true);

        public static readonly RecipeFilter[] Weapons = new[]
        {
            new RecipeFilter("Weapons", "All weapons",  t => t.IsWeapon),
            new RecipeFilter("Weapons", "Melee",        t => t.IsMelee),
            new RecipeFilter("Weapons", "Ranged",       t => t.IsRanged),
            new RecipeFilter("Weapons", "One-handed",   t => t.IsOneHanded && t.IsMelee),
            new RecipeFilter("Weapons", "Two-handed",   t => t.IsTwoHanded && t.IsMelee),
            new RecipeFilter("Weapons", "Sword",        t => t.IsSword),
            new RecipeFilter("Weapons", "Dagger",       t => t.IsDagger),
            new RecipeFilter("Weapons", "Axe",          t => t.IsAxe),
            new RecipeFilter("Weapons", "Blunt",        t => t.IsBlunt),
            new RecipeFilter("Weapons", "Polearm",      t => t.IsPolearm),
            new RecipeFilter("Weapons", "Bow",          t => t.IsShortBow || t.IsMediumBow || t.IsHeavyBow),
            new RecipeFilter("Weapons", "Rod / magic",  t => t.IsRod || t.IsMagic),
            new RecipeFilter("Weapons", "Throwable",    t => t.IsThrowable),
            new RecipeFilter("Weapons", "Shield",       t => t.IsShield),
            new RecipeFilter("Weapons", "Arrow",        t => t.IsArrow),
        };

        public static readonly RecipeFilter[] Armor = new[]
        {
            new RecipeFilter("Armor", "All armor",          t => t.IsArmor),
            new RecipeFilter("Armor", "Light",              t => t.IsLightArmor),
            new RecipeFilter("Armor", "Medium",             t => t.IsMediumArmor),
            new RecipeFilter("Armor", "Heavy",              t => t.IsHeavyArmor),
            new RecipeFilter("Armor", "Helmet",             t => GetEqType(t) == EquipmentType.Helmet),
            new RecipeFilter("Armor", "Cuirass (chest)",    t => GetEqType(t) == EquipmentType.Cuirass),
            new RecipeFilter("Armor", "Gauntlets (gloves)", t => GetEqType(t) == EquipmentType.Gauntlets),
            new RecipeFilter("Armor", "Greaves (pants)",    t => GetEqType(t) == EquipmentType.Greaves),
            new RecipeFilter("Armor", "Boots",              t => GetEqType(t) == EquipmentType.Boots),
            new RecipeFilter("Armor", "Cloak (back)",       t => GetEqType(t) == EquipmentType.Back),
        };

        public static readonly RecipeFilter[] Jewelry = new[]
        {
            new RecipeFilter("Jewelry", "Any jewelry", t => t.IsJewelry),
            new RecipeFilter("Jewelry", "Amulet",      t => GetEqType(t) == EquipmentType.Amulet),
            new RecipeFilter("Jewelry", "Ring",        t => GetEqType(t) == EquipmentType.Ring),
        };

        public static readonly RecipeFilter[] Consumables = new[]
        {
            new RecipeFilter("Consumables", "Potion",      t => t.IsPotion),
            new RecipeFilter("Consumables", "Food (raw)",  t => t.IsPlainFood),
            new RecipeFilter("Consumables", "Dish",        t => t.IsDish),
            new RecipeFilter("Consumables", "Fish",        t => t.IsFish),
            new RecipeFilter("Consumables", "Throwable",   t => t.IsThrowable),
        };

        public static readonly RecipeFilter[] Components = new[]
        {
            new RecipeFilter("Components", "Crafting", t => t.IsCraftingComponent),
            new RecipeFilter("Components", "Alchemy",  t => t.IsAlchemyComponent),
            new RecipeFilter("Components", "Cooking",  t => t.IsCookingComponent),
        };

        // Top-level groups in the order they appear in the popup. Each entry is
        // (label, sub-filters); the popup callback opens a sub-popup with those.
        public static readonly (string Label, RecipeFilter[] Filters)[] Groups = new[]
        {
            ("Weapons",     Weapons),
            ("Armor",       Armor),
            ("Jewelry",     Jewelry),
            ("Consumables", Consumables),
            ("Components",  Components),
        };

        private static EquipmentType GetEqType(ItemTemplate t) =>
            t?.GetAttachment<ItemEquipSpec>()?.EquipmentType;
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
