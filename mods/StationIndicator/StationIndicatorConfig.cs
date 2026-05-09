using BepInEx.Configuration;
using UnityEngine;

namespace StationIndicator
{
    internal class StationIndicatorConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<float> ScanInterval { get; }
        public ConfigEntry<KeyCode> RescanHotkey { get; }

        public ConfigEntry<bool> MarkBlacksmithing { get; }
        public ConfigEntry<bool> MarkAlchemy { get; }
        public ConfigEntry<bool> MarkAllCraftingStations { get; }
        public ConfigEntry<string> ExtraNamePatterns { get; }

        public ConfigEntry<bool> Verbose { get; }

        public StationIndicatorConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "1. General", "Enabled", true,
                "Master switch.");

            ScanInterval = cfg.Bind(
                "1. General", "ScanInterval", 5f,
                new ConfigDescription(
                    "Seconds between scene scans for new station spawns. Lower = faster discovery on scene load, higher = less CPU.",
                    new AcceptableValueRange<float>(0.5f, 60f)));

            RescanHotkey = cfg.Bind(
                "1. General", "RescanHotkey", KeyCode.None,
                "Optional hotkey to force an immediate rescan. Useful for debugging.");

            MarkBlacksmithing = cfg.Bind(
                "2. Targets", "MarkBlacksmithing", true,
                "Add a map indicator to forge / blacksmithing stations (Spec_CraftingStation_Blacksmithing*).");

            MarkAlchemy = cfg.Bind(
                "2. Targets", "MarkAlchemy", true,
                "Add a map indicator to alchemy stations (Spec_CraftingStation_Alchemy*) — useful for variants the base game leaves unmarked.");

            MarkAllCraftingStations = cfg.Bind(
                "2. Targets", "MarkAllCraftingStations", false,
                "If true, mark every LocationSpec with a StartCraftingAttachment component, regardless of name. Catches cooking pots / ovens / etc. that the named filters miss.");

            ExtraNamePatterns = cfg.Bind(
                "2. Targets", "ExtraNamePatterns", "",
                "Comma-separated, case-insensitive substrings. Any LocationSpec whose GameObject.name contains one of these will also be marked. E.g. 'cookingpot,workbench'.");

            Verbose = cfg.Bind(
                "9. Debug", "Verbose", false,
                "Log every discovered station + every marker injection. Off by default.");
        }
    }
}
