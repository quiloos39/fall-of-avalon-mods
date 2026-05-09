using BepInEx.Configuration;

namespace GuardVariety
{
    internal class GuardVarietyConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> RandomizeUniqueGuards { get; }
        public ConfigEntry<bool> AlsoRandomizeDefenders { get; }
        public ConfigEntry<bool> ClearBeardOnFemale { get; }
        public ConfigEntry<bool> RemoveHelmet { get; }
        public ConfigEntry<bool> ForceRandomize { get; }
        public ConfigEntry<bool> StableSeed { get; }
        public ConfigEntry<bool> SwapToFemalePrefab { get; }
        public ConfigEntry<float> SwapToFemaleProbability { get; }
        public ConfigEntry<bool> Verbose { get; }

        public GuardVarietyConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "1. General", "Enabled", true,
                "Master switch. When true, city guards get randomized faces, hair, skin, eyes etc. on first spawn.");

            RandomizeUniqueGuards = cfg.Bind(
                "1. General", "RandomizeUniqueGuards", true,
                "If true, named/unique guards are also randomized. Disable to keep authored faces for specific named guards.");

            AlsoRandomizeDefenders = cfg.Bind(
                "1. General", "AlsoRandomizeDefenders", false,
                "If true, also randomize NPCs with Defender archetype (militia / faction soldiers who fight back). Off by default — they aren't 'guards' strictly.");

            ClearBeardOnFemale = cfg.Bind(
                "2. Tweaks", "ClearBeardOnFemale", true,
                "If a guard's Gender marker is Female, clear any beard the randomizer rolled. Avoids bearded women on prefabs that share male/female pools.");

            RemoveHelmet = cfg.Bind(
                "2. Tweaks", "RemoveHelmet", true,
                "Strip helmets off guards so the randomized faces are actually visible. Mirrors the game's own DummifyNPCs editor tool: unequip then discard any item with EquipmentType.Helmet. Loot is unaffected (drops come from the template's Loot table, not live inventory).");

            ForceRandomize = cfg.Bind(
                "2. Tweaks", "ForceRandomize", true,
                "Re-randomize even when BodyFeatures.BlockRandomization is already true. Required for existing playthroughs: vanilla guards have BlockRandomization=true at save time, so without this flag they never get re-rolled. With StableSeed=true the result is deterministic per NPC, so each guard still looks the same every load.");

            StableSeed = cfg.Bind(
                "2. Tweaks", "StableSeed", true,
                "Seed UnityEngine.Random with a hash of (template name + NPC ID) before calling RandomizeFeatures, then restore the previous RNG state. Same guard => same face every time. Disable for chaotic per-load shuffling.");

            SwapToFemalePrefab = cfg.Bind(
                "3. Tier3 - PrefabSwap", "SwapToFemalePrefab", true,
                "Tier 3: replace some guards' visual prefab with a female humanoid prefab discovered from other NPCs in the world. The swap pool is built passively as you explore (registered to BepInEx/config/com.user.guardvariety.prefabs.txt) — first-time visitors will see fewer swaps until villagers/priests etc. have been encountered. Armor mismatches expected: guard cuirass/etc. attaches via shared body sockets, so it should still display, but proportions may look off on the swapped rig.");

            SwapToFemaleProbability = cfg.Bind(
                "3. Tier3 - PrefabSwap", "SwapToFemaleProbability", 0.4f,
                new ConfigDescription(
                    "Per-guard probability of being swapped to a female humanoid prefab when one is available in the registry. Roll is deterministic from the same seed used by StableSeed, so a given guard is either always swapped or never swapped (consistent across loads).",
                    new AcceptableValueRange<float>(0f, 1f)));

            Verbose = cfg.Bind(
                "9. Debug", "Verbose", false,
                "Log every guard randomization, prefab swap, and visual confirmation line. Off by default — only flip when debugging.");
        }
    }
}
