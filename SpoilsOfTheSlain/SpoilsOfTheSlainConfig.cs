using BepInEx.Configuration;

namespace SpoilsOfTheSlain
{
    internal class SpoilsOfTheSlainConfig
    {
        public ConfigEntry<bool> Enabled { get; }

        // Discovery expansion — closing the AutoCollect gap
        public ConfigEntry<bool> ExpandDiscoveryOnDrop { get; }
        public ConfigEntry<bool> ExpandDiscoveryOnContainer { get; }
        public ConfigEntry<bool> ExpandDiscoveryOnNpcDrop { get; }

        // Recipe injection
        public ConfigEntry<bool> InjectIntoForge { get; }
        public ConfigEntry<bool> InjectIntoAlchemy { get; }
        public ConfigEntry<bool> InjectIntoCooking { get; }

        // Cost tuning
        public ConfigEntry<int> BaseGoldCost { get; }
        public ConfigEntry<float> GoldMultiplierByTier { get; }
        public ConfigEntry<bool> UseItemBasePrice { get; }
        public ConfigEntry<float> BasePriceMultiplier { get; }

        // Filters
        public ConfigEntry<bool> ExcludeQuestItems { get; }
        public ConfigEntry<bool> ExcludeUnique { get; }
        public ConfigEntry<bool> EquippablesOnlyForForge { get; }

        // Notifications
        public ConfigEntry<bool> NotifyOnDiscovery { get; }

        // Debug
        public ConfigEntry<bool> Verbose { get; }
        public ConfigEntry<bool> LogDiscoveryGrowth { get; }

        public SpoilsOfTheSlainConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "1. General", "Enabled", true,
                "Master switch. If false, no patches are installed.");

            ExpandDiscoveryOnDrop = cfg.Bind(
                "2. Discovery", "ExpandDiscoveryOnDrop", true,
                "When any item drop spawns in the world, mark it as discovered (added to KnownItems). Closes the AutoCollect gap.");

            ExpandDiscoveryOnContainer = cfg.Bind(
                "2. Discovery", "ExpandDiscoveryOnContainer", true,
                "When a container reveals an item (you open a chest), mark all its contents as discovered.");

            ExpandDiscoveryOnNpcDrop = cfg.Bind(
                "2. Discovery", "ExpandDiscoveryOnNpcDrop", true,
                "When an NPC corpse drops items via NPCItemDroppedElement, mark them as discovered. Catches boss-only loot you don't pick up.");

            InjectIntoForge = cfg.Bind(
                "3. Recipes", "InjectIntoForge", true,
                "Add a recipe for every discovered weapon/armor at handcrafting (forge) stations. " +
                "Recipes inherit ingredient cost from a similar-tier vanilla recipe so they feel balanced.");

            InjectIntoAlchemy = cfg.Bind(
                "3. Recipes", "InjectIntoAlchemy", true,
                "Add a recipe for every discovered potion/elixir at alchemy stations.");

            InjectIntoCooking = cfg.Bind(
                "3. Recipes", "InjectIntoCooking", false,
                "Add a recipe for every discovered food at cooking stations. Off by default — cooking has experimental flow that may conflict.");

            BaseGoldCost = cfg.Bind(
                "4. Cost", "BaseGoldCost", 50,
                "Flat gold added to every generated recipe.");

            GoldMultiplierByTier = cfg.Bind(
                "4. Cost", "GoldMultiplierByTier", 2.5f,
                "Gold cost = BaseGoldCost + (Tier * Multiplier * BasePrice). Higher = more expensive.");

            UseItemBasePrice = cfg.Bind(
                "4. Cost", "UseItemBasePrice", true,
                "If true, recipe gold cost scales with the item's BasePrice. If false, only BaseGoldCost+Tier scaling applies.");

            BasePriceMultiplier = cfg.Bind(
                "4. Cost", "BasePriceMultiplier", 1.5f,
                "Multiplier applied to the item's BasePrice when UseItemBasePrice=true. 1.0 = same as buying it. 2.0 = costs twice as much to craft as to buy.");

            ExcludeQuestItems = cfg.Bind(
                "5. Filters", "ExcludeQuestItems", true,
                "Skip items tagged Quest / CannotBeDropped — preventing duplicate quest items.");

            ExcludeUnique = cfg.Bind(
                "5. Filters", "ExcludeUnique", false,
                "Skip items tagged Unique. Off by default — unique items are usually the whole point of this mod.");

            EquippablesOnlyForForge = cfg.Bind(
                "5. Filters", "EquippablesOnlyForForge", true,
                "If true, the forge only shows weapons/armor/jewelry — not crafting materials, books, etc. Off would let you craft any known item at the forge.");

            NotifyOnDiscovery = cfg.Bind(
                "6. Notifications", "NotifyOnDiscovery", true,
                "Pop the vanilla recipe-learned notification (icon + name in the middle screen) every " +
                "time we issue a new recipe from a discovered weapon/armor/potion/dish. " +
                "Startup backfill is silent — only mid-session discoveries (drops, containers, kills) trigger this.");

            Verbose = cfg.Bind(
                "9. Debug", "Verbose", false,
                "Log every recipe injection + every discovery hook fire. Loud. Off by default.");

            LogDiscoveryGrowth = cfg.Bind(
                "9. Debug", "LogDiscoveryGrowth", true,
                "Log when KnownItems grows by 5+ items. Useful to confirm the mod is working without log spam.");
        }
    }
}
