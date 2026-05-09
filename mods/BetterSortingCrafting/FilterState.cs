using System;
using System.Collections.Generic;
using Awaken.TG.Main.Crafting.HandCrafting.RecipeView;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.Attachments;
using Awaken.TG.Main.Heroes.Items.Gems;
using Awaken.TG.Main.Heroes.Stats;
using Awaken.TG.Main.Skills;

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

        public static readonly RecipeFilter[] Relics = new[]
        {
            new RecipeFilter("Relics", "All relics",     t => t.IsGem),
            new RecipeFilter("Relics", "Weapon relic",   t => RelicStats.IsRelicOfType(t, GemType.Weapon)),
            new RecipeFilter("Relics", "Armor relic",    t => RelicStats.IsRelicOfType(t, GemType.Armor)),
            new RecipeFilter("Relics", "Damage bonus",   t => t.IsGem && RelicStats.HasStatIn(t, RelicStats.Damage)),
            new RecipeFilter("Relics", "Defense bonus",  t => t.IsGem && RelicStats.HasStatIn(t, RelicStats.Defense)),
            new RecipeFilter("Relics", "Stamina / Mana", t => t.IsGem && RelicStats.HasStatIn(t, RelicStats.Resource)),
            new RecipeFilter("Relics", "Mobility",       t => t.IsGem && RelicStats.HasStatIn(t, RelicStats.Mobility)),
            new RecipeFilter("Relics", "Stealth",        t => t.IsGem && RelicStats.HasStatIn(t, RelicStats.Stealth)),
            new RecipeFilter("Relics", "Healing / Buff", t => t.IsGem && RelicStats.HasStatIn(t, RelicStats.Healing)),
            new RecipeFilter("Relics", "Crafting bonus", t => t.IsGem && RelicStats.HasStatIn(t, RelicStats.Crafting)),
        };

        // Top-level groups in the order they appear in the popup. Each entry is
        // (label, sub-filters); the popup callback opens a sub-popup with those.
        public static readonly (string Label, RecipeFilter[] Filters)[] Groups = new[]
        {
            ("Weapons",     Weapons),
            ("Armor",       Armor),
            ("Jewelry",     Jewelry),
            ("Relics",      Relics),
            ("Consumables", Consumables),
            ("Components",  Components),
        };

        private static EquipmentType GetEqType(ItemTemplate t) =>
            t?.GetAttachment<ItemEquipSpec>()?.EquipmentType;
    }

    // Stat-bonus categorisation for relics (gems). A gem carries one or more
    // SkillReferences; each may target a StatType via its `enums` list. We
    // group those StatType.EnumName values into human-meaningful buckets so
    // the popup can offer "show me damage relics" / "stamina relics" etc.
    //
    // The buckets are curated from CharacterStatType / HeroStatType — stat
    // names are stable C# identifiers, so the sets won't drift unless the
    // game adds entirely new stats (in which case we'd see them as "Other"
    // and they'd just not match any stat-bucket filter — gracefully ignored).
    internal static class RelicStats
    {
        public static bool IsRelicOfType(ItemTemplate t, GemType type)
        {
            var gem = t?.GetAttachment<GemAttachment>();
            return gem != null && gem.Type == type;
        }

        public static bool HasStatIn(ItemTemplate t, HashSet<string> bucket)
        {
            var gem = t?.GetAttachment<GemAttachment>();
            if (gem == null) return false;
            foreach (var skillRef in gem.Skills)
            {
                if (skillRef?.enums == null) continue;
                foreach (var e in skillRef.enums)
                {
                    var stat = e?.enumReference?.EnumAs<StatType>();
                    if (stat != null && bucket.Contains(stat.EnumName))
                        return true;
                }
            }
            return false;
        }

        public static readonly HashSet<string> Damage = new HashSet<string>
        {
            "Strength", "StrengthLinear",
            "MeleeDamageMultiplier", "OneHandedMeleeDamageMultiplier",
            "TwoHandedMeleeDamageMultiplier", "UnarmedMeleeDamageMultiplier",
            "RangedDamageMultiplier", "MagicStrength",
            "AttackSpeed", "BowDrawSpeed",
            "OneHandedLightAttackSpeed", "OneHandedHeavyAttackSpeed",
            "TwoHandedLightAttackSpeed", "TwoHandedHeavyAttackSpeed",
            "DualHandedLightAttackSpeed", "DualHandedHeavyAttackSpeed",
            "FistLightAttackSpeed", "FistHeavyAttackSpeed",
            "SpellChargeSpeed",
            "LifeSteal", "MeleeRetaliation",
            "DebuffStrength", "DebuffDuration",
            "DeflectPrecision",
        };

        public static readonly HashSet<string> Defense = new HashSet<string>
        {
            "IncomingDamage", "Resistance", "DamageNullifier",
            "ManaShield", "ManaShieldRetaliation",
            "BlockPrepareSpeed", "BlockingMovementMultiplier", "HoldBlockCostReduction",
            "FallDamageMultiplier", "ArmorPenaltyMultiplier",
        };

        public static readonly HashSet<string> Resource = new HashSet<string>
        {
            "Stamina", "MaxStamina", "StaminaRegen", "StaminaUsageMultiplier",
            "SprintCostMultiplier",
            "DashStamina", "DashCostMultiplier", "DashRegenDurationMultiplier",
            "Mana", "MaxMana", "ManaUsageMultiplier", "ManaRegen", "ManaRegenPercentage",
        };

        public static readonly HashSet<string> Mobility = new HashSet<string>
        {
            "MoveSpeed", "MovementSpeedMultiplier", "SprintSpeed",
            "CrouchSpeedMultiplier", "SwimSpeed", "JumpHeight",
            "DashSpeed", "MaxDashOptimalCounter",
            "EncumbranceLimit", "ArmorWeightMultiplier",
        };

        public static readonly HashSet<string> Stealth = new HashSet<string>
        {
            "VisibilityMultiplier", "NoiseMultiplier",
            "CrouchNoiseMultiplier", "CrouchVisibilityMultiplier",
            "FootstepsNoisiness", "LockpickDamageMultiplier",
            "PickpocketHoldTimeModifier", "PickpocketRecoveryChance",
        };

        public static readonly HashSet<string> Healing = new HashSet<string>
        {
            "IncomingHealing", "ConsumableHealingBonus", "PotionHealingBonus",
            "BuffStrength", "BuffDuration",
        };

        public static readonly HashSet<string> Crafting = new HashSet<string>
        {
            "EquipmentLevelBonus", "CookingLevelBonus", "AlchemyLevelBonus",
            "UpgradeDiscount", "CraftingRequirementModifier", "CraftingSkillBonus",
        };
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
