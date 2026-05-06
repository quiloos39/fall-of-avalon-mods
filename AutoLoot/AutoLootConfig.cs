using BepInEx.Configuration;
using UnityEngine;

namespace AutoLoot
{
    internal class AutoLootConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> EnabledByDefault { get; }
        public ConfigEntry<KeyCode> ToggleHotkey { get; }

        public ConfigEntry<float> ScanInterval { get; }
        public ConfigEntry<float> ScanRadius { get; }
        public ConfigEntry<int> MaxPickupsPerScan { get; }
        public ConfigEntry<float> SpecCacheRefreshInterval { get; }

        public ConfigEntry<float> MinValuePerWeight { get; }
        public ConfigEntry<int> MinValueWhenWeightless { get; }

        public ConfigEntry<bool> IgnoreReadables { get; }
        public ConfigEntry<bool> IgnoreQuestItems { get; }
        public ConfigEntry<bool> IgnoreIllegal { get; }
        public ConfigEntry<bool> IgnoreHidden { get; }
        public ConfigEntry<bool> IgnoreLocked { get; }

        public ConfigEntry<bool> ShowToggleNotification { get; }

        public ConfigEntry<bool> AlwaysFood { get; }
        public ConfigEntry<bool> AlwaysDrinkable { get; }
        public ConfigEntry<bool> AlwaysCrafting { get; }
        public ConfigEntry<bool> AlwaysReadable { get; }
        public ConfigEntry<bool> AlwaysWeightless { get; }
        public ConfigEntry<bool> AlwaysValueless { get; }

        public ConfigEntry<bool> Verbose { get; }

        public AutoLootConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "1. General", "Enabled", true,
                "Master switch for the AutoLoot mod.");

            EnabledByDefault = cfg.Bind(
                "1. General", "EnabledByDefault", true,
                "If true, auto-collect starts active when the game loads. Otherwise it must be toggled on with the hotkey.");

            ToggleHotkey = cfg.Bind(
                "1. General", "ToggleHotkey", KeyCode.F6,
                "Hotkey to toggle auto-collect on/off in-game.");

            ScanInterval = cfg.Bind(
                "2. Scanning", "ScanInterval", 0.5f,
                new ConfigDescription(
                    "How often (in seconds) to scan the world for nearby items.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            ScanRadius = cfg.Bind(
                "2. Scanning", "ScanRadius", 5f,
                new ConfigDescription(
                    "Maximum distance (in meters) from the hero at which items will be auto-collected.",
                    new AcceptableValueRange<float>(1f, 30f)));

            MaxPickupsPerScan = cfg.Bind(
                "2. Scanning", "MaxPickupsPerScan", 5,
                new ConfigDescription(
                    "Maximum number of items to pick up in a single scan tick. Lower values spread pickups over multiple ticks to avoid notification/sound spam and frame stutters when looting a corpse with many items. 0 = unlimited.",
                    new AcceptableValueRange<int>(0, 100)));

            SpecCacheRefreshInterval = cfg.Bind(
                "2. Scanning", "SpecCacheRefreshInterval", 2f,
                new ConfigDescription(
                    "Maximum time (in seconds) between refreshes of the cached list of static-scene pickables (PickableSpec / MutablePickableSpec). The cache is *also* refreshed automatically whenever the hero moves more than one ScanRadius since the last refresh, so the practical refresh rate while moving is ~ScanRadius/heroSpeed. Lower this if you stand still in cluttered areas and feel the cache is stale; raise it if you're seeing stutters when idle.",
                    new AcceptableValueRange<float>(0.5f, 60f)));

            MinValuePerWeight = cfg.Bind(
                "3. Filter", "MinValuePerWeight", 50f,
                new ConfigDescription(
                    "Minimum item Price/Weight ratio for an item to be auto-collected. " +
                    "Higher numbers = pickier (only valuable-per-weight items). Set to 0 to collect everything.",
                    new AcceptableValueRange<float>(0f, 10000f)));

            MinValueWhenWeightless = cfg.Bind(
                "3. Filter", "MinValueWhenWeightless", 1,
                new ConfigDescription(
                    "Minimum absolute Price for items that have 0 weight (e.g. gold, lockpicks, quest items). " +
                    "These cannot be ranked by ratio, so they use this absolute floor instead.",
                    new AcceptableValueRange<int>(0, 100000)));

            IgnoreReadables = cfg.Bind(
                "3. Filter", "IgnoreReadables", true,
                "If true, books / scrolls / notes are skipped (otherwise they trigger a 'read' UI overlay).");

            IgnoreQuestItems = cfg.Bind(
                "3. Filter", "IgnoreQuestItems", false,
                "If true, quest items are skipped. Most players want quest items auto-collected, so default is false.");

            IgnoreIllegal = cfg.Bind(
                "3. Filter", "IgnoreIllegal", true,
                "If true, items that would count as theft (in town, owned by NPCs) are skipped to avoid auto-stealing.");

            IgnoreHidden = cfg.Bind(
                "3. Filter", "IgnoreHidden", true,
                "If true, items hidden from the UI (internal/script-only) are skipped.");

            IgnoreLocked = cfg.Bind(
                "3. Filter", "IgnoreLocked", true,
                "If true, locked containers (chests, doors, drawers) are skipped — both their PickItemAction and their contents. Set to false to bypass locks entirely and auto-loot anything in range.");

            ShowToggleNotification = cfg.Bind(
                "4. UI", "ShowToggleNotification", true,
                "Show an on-screen notification when the toggle hotkey flips auto-collect on or off.");

            AlwaysFood = cfg.Bind(
                "5. AlwaysCollect", "AlwaysFood", true,
                "If true, food items (IsPlainFood, IsDish, IsFish) are always collected, ignoring the value/weight threshold.");
            AlwaysDrinkable = cfg.Bind(
                "5. AlwaysCollect", "AlwaysDrinkable", true,
                "If true, potions and alcohol (IsAlcohol, IsPotion) are always collected, ignoring the value/weight threshold.");
            AlwaysCrafting = cfg.Bind(
                "5. AlwaysCollect", "AlwaysCrafting", true,
                "If true, crafting components (IsCrafting — alchemy, cooking, smithing materials, hides/pelts) are always collected, ignoring the value/weight threshold.");
            AlwaysReadable = cfg.Bind(
                "5. AlwaysCollect", "AlwaysReadable", true,
                "If true, books / scrolls / notes (IsReadable) are always collected. The mod silently adds them to inventory without opening the read UI; you can read them from inventory afterwards. Implies that IgnoreReadables is bypassed for these items.");
            AlwaysWeightless = cfg.Bind(
                "5. AlwaysCollect", "AlwaysWeightless", true,
                "If true, items with weight = 0 (price/weight ratio = infinite) are always collected. Includes gold, keys, lockpicks, and most weightless quest items. Overrides MinValueWhenWeightless.");
            AlwaysValueless = cfg.Bind(
                "5. AlwaysCollect", "AlwaysValueless", true,
                "If true, items with price = 0 (price/weight ratio = 0) are always collected. These are usually utility items, notes, or world props with no merchant value but possible quest/crafting use.");

            Verbose = cfg.Bind(
                "6. Debug", "Verbose", false,
                "Write detailed scan/filter/pickup info to BepInEx/LogOutput.log. Useful when nothing seems to happen.");
        }
    }
}
