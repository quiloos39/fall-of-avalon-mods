using BepInEx.Configuration;
using UnityEngine;

namespace QuestPath
{
    internal class QuestPathConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> ActiveByDefault { get; }
        public ConfigEntry<KeyCode> ToggleHotkey { get; }

        // Destination source priority
        public ConfigEntry<bool> UseCustomMarker { get; }
        public ConfigEntry<bool> UseTrackedQuest { get; }

        // Pathfinding
        public ConfigEntry<float> RecalcInterval { get; }
        public ConfigEntry<float> RecalcMoveThreshold { get; }
        public ConfigEntry<float> ArrivalDistance { get; }

        // Visualization — line (LEGACY chevron mode; now off by default in favour of flowers)
        public ConfigEntry<bool> ShowPathLine { get; }
        public ConfigEntry<float> LineWidth { get; }
        public ConfigEntry<float> LineHeightOffset { get; }
        public ConfigEntry<float> LineColorR { get; }
        public ConfigEntry<float> LineColorG { get; }
        public ConfigEntry<float> LineColorB { get; }
        public ConfigEntry<float> LineEmissive { get; }
        public ConfigEntry<float> LineFlowSpeed { get; }
        public ConfigEntry<float> LinePulseSpeed { get; }
        public ConfigEntry<float> LinePulseAmount { get; }
        public ConfigEntry<float> LineSimplifyTolerance { get; }
        public ConfigEntry<float> LineMaxRenderDistance { get; }

        // Visualization — flower trail (NEW default)
        public ConfigEntry<bool> ShowFlowerTrail { get; }
        public ConfigEntry<float> FlowerSpacing { get; }
        public ConfigEntry<float> FlowerScale { get; }
        public ConfigEntry<float> FlowerHeightOffset { get; }
        public ConfigEntry<float> FlowerEmissiveBoost { get; }
        public ConfigEntry<float> FlowerBobAmplitude { get; }
        public ConfigEntry<float> FlowerBobSpeed { get; }
        public ConfigEntry<float> FlowerMaxRenderDistance { get; }
        public ConfigEntry<int> FlowerCountCap { get; }

        // Visualization — destination beam + rune
        public ConfigEntry<bool> ShowSkyBeam { get; }
        public ConfigEntry<float> BeamHeight { get; }
        public ConfigEntry<float> BeamRadius { get; }
        public ConfigEntry<float> RuneDiscRadius { get; }
        public ConfigEntry<float> RuneDiscRotateSpeed { get; }

        public ConfigEntry<bool> Verbose { get; }

        public QuestPathConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "1. General", "Enabled", true,
                "Master switch.");
            ActiveByDefault = cfg.Bind(
                "1. General", "ActiveByDefault", true,
                "If true, path display is on at game start. Otherwise toggle with the hotkey.");
            ToggleHotkey = cfg.Bind(
                "1. General", "ToggleHotkey", KeyCode.F7,
                "Hotkey to toggle the path display on/off.");

            UseCustomMarker = cfg.Bind(
                "2. Destination", "UseCustomMarker", true,
                "Pathfind to the custom marker the player drops on the world map (vanilla feature). Has priority over tracked quest if both are set.");
            UseTrackedQuest = cfg.Bind(
                "2. Destination", "UseTrackedQuest", true,
                "Fallback to the player's currently-tracked quest objective when no custom marker is set. Works even for objectives whose map icon is hidden — we read the underlying target coords directly.");

            RecalcInterval = cfg.Bind(
                "3. Pathfinding", "RecalcInterval", 1.0f,
                new ConfigDescription(
                    "How often (seconds) to re-pathfind. Each call is async and runs on a worker thread, so cost is small. Lower = more responsive on detours.",
                    new AcceptableValueRange<float>(0.1f, 10f)));
            RecalcMoveThreshold = cfg.Bind(
                "3. Pathfinding", "RecalcMoveThreshold", 3.0f,
                new ConfigDescription(
                    "Force an extra recalc whenever the player has moved more than this many meters since the last one (catches sudden teleports / fast-travel).",
                    new AcceptableValueRange<float>(0.5f, 50f)));
            ArrivalDistance = cfg.Bind(
                "3. Pathfinding", "ArrivalDistance", 5.0f,
                new ConfigDescription(
                    "Hide the path when the player gets within this distance of the target.",
                    new AcceptableValueRange<float>(0f, 50f)));

            ShowPathLine = cfg.Bind(
                "4. Visualization — Line (legacy)", "ShowPathLine", false,
                "Legacy chevron-line trail. Off by default — Flowers (section 6) look way better. Turn this on to compare or fall back if flowers fail to load.");
            LineWidth = cfg.Bind(
                "4. Visualization — Line (legacy)", "LineWidth", 0.6f,
                new ConfigDescription("Line thickness in meters.", new AcceptableValueRange<float>(0.05f, 5f)));
            LineHeightOffset = cfg.Bind(
                "4. Visualization — Line (legacy)", "LineHeightOffset", 0.1f,
                new ConfigDescription("Lift the line above the navmesh corners by this much (meters), to avoid Z-fighting with the floor.", new AcceptableValueRange<float>(0f, 2f)));
            LineColorR = cfg.Bind("4. Visualization — Line (legacy)", "LineColorR", 1.00f, new ConfigDescription("Red 0-1. Default warm amber.",   new AcceptableValueRange<float>(0f, 1f)));
            LineColorG = cfg.Bind("4. Visualization — Line (legacy)", "LineColorG", 0.72f, new ConfigDescription("Green 0-1.", new AcceptableValueRange<float>(0f, 1f)));
            LineColorB = cfg.Bind("4. Visualization — Line (legacy)", "LineColorB", 0.30f, new ConfigDescription("Blue 0-1.",  new AcceptableValueRange<float>(0f, 1f)));
            LineEmissive = cfg.Bind(
                "4. Visualization — Line (legacy)", "LineEmissive", 6f,
                new ConfigDescription("Emissive intensity — boosts the color past 1.0 so it glows in HDR. 0 = matte.", new AcceptableValueRange<float>(0f, 50f)));
            LineFlowSpeed = cfg.Bind(
                "4. Visualization — Line (legacy)", "LineFlowSpeed", 1.2f,
                new ConfigDescription("How fast chevrons scroll forward (UV-units per second). Set negative to flow backward, 0 for static.", new AcceptableValueRange<float>(-10f, 10f)));
            LinePulseSpeed = cfg.Bind(
                "4. Visualization — Line (legacy)", "LinePulseSpeed", 1.5f,
                new ConfigDescription("Speed of the soft brightness pulse (Hz). 0 = no pulse.", new AcceptableValueRange<float>(0f, 10f)));
            LinePulseAmount = cfg.Bind(
                "4. Visualization — Line (legacy)", "LinePulseAmount", 0.30f,
                new ConfigDescription("Pulse depth — fraction of LineEmissive that the pulse modulates by.", new AcceptableValueRange<float>(0f, 1f)));
            LineSimplifyTolerance = cfg.Bind(
                "4. Visualization — Line (legacy)", "LineSimplifyTolerance", 0.4f,
                new ConfigDescription("Drop corner waypoints whose deviation from a straight line between neighbours is less than this (meters). 0 = keep every corner. 0.4 = smoother / fewer chevrons.", new AcceptableValueRange<float>(0f, 5f)));
            LineMaxRenderDistance = cfg.Bind(
                "4. Visualization — Line (legacy)", "LineMaxRenderDistance", 80f,
                new ConfigDescription("Only render the first N meters of the path from the player. Beyond that, the destination beam is enough. 0 = whole path.", new AcceptableValueRange<float>(0f, 1000f)));

            ShowSkyBeam = cfg.Bind(
                "5. Visualization — Beam", "ShowSkyBeam", true,
                "Draw a vertical beam at the destination so it's visible from any distance, even through terrain.");
            BeamHeight = cfg.Bind(
                "5. Visualization — Beam", "BeamHeight", 80f,
                new ConfigDescription("Beam height in meters. Tall enough to be visible from far, short enough not to break the sky.", new AcceptableValueRange<float>(10f, 1000f)));
            BeamRadius = cfg.Bind(
                "5. Visualization — Beam", "BeamRadius", 0.35f,
                new ConfigDescription("Beam thickness in meters at the base. Tapers to nothing at the top.", new AcceptableValueRange<float>(0.05f, 5f)));
            RuneDiscRadius = cfg.Bind(
                "5. Visualization — Beam", "RuneDiscRadius", 1.5f,
                new ConfigDescription("Radius of the glowing rune disc at the base of the beam (meters). 0 to disable.", new AcceptableValueRange<float>(0f, 10f)));
            RuneDiscRotateSpeed = cfg.Bind(
                "5. Visualization — Beam", "RuneDiscRotateSpeed", 20f,
                new ConfigDescription("Disc rotation speed in degrees/sec. Negative = counterclockwise.", new AcceptableValueRange<float>(-360f, 360f)));

            ShowFlowerTrail = cfg.Bind(
                "6. Visualization — Flowers", "ShowFlowerTrail", true,
                "Spawn glowing yellow poppies along the path corners — looks like a Witcher fairytale trail. Uses the game's own 'Mat_flower_common_poppy_01_Glowing' material.");
            FlowerSpacing = cfg.Bind(
                "6. Visualization — Flowers", "FlowerSpacing", 4.0f,
                new ConfigDescription("Distance between flower clusters along the path (meters). Lower = denser, Higher = sparser.", new AcceptableValueRange<float>(0.5f, 30f)));
            FlowerScale = cfg.Bind(
                "6. Visualization — Flowers", "FlowerScale", 1.0f,
                new ConfigDescription("Uniform scale applied to each flower cluster. The mesh is already a small patch of poppies; 1.0 looks natural.", new AcceptableValueRange<float>(0.1f, 5f)));
            FlowerHeightOffset = cfg.Bind(
                "6. Visualization — Flowers", "FlowerHeightOffset", 0.0f,
                new ConfigDescription("Lift each flower above the navmesh corner by this much (meters). 0 plants them at ground level (recommended).", new AcceptableValueRange<float>(-1f, 2f)));
            FlowerEmissiveBoost = cfg.Bind(
                "6. Visualization — Flowers", "FlowerEmissiveBoost", 1.0f,
                new ConfigDescription("Multiply the material's emissive intensity by this. 1.0 = vanilla glowing-poppy look. Higher = more magical/blinding.", new AcceptableValueRange<float>(0f, 20f)));
            FlowerBobAmplitude = cfg.Bind(
                "6. Visualization — Flowers", "FlowerBobAmplitude", 0.05f,
                new ConfigDescription("How far each flower bobs up and down (meters). 0 = static, 0.05 = subtle 'breathing' motion.", new AcceptableValueRange<float>(0f, 0.5f)));
            FlowerBobSpeed = cfg.Bind(
                "6. Visualization — Flowers", "FlowerBobSpeed", 1.0f,
                new ConfigDescription("Bob frequency (Hz). 0 = no bob, 1 = once per second.", new AcceptableValueRange<float>(0f, 5f)));
            FlowerMaxRenderDistance = cfg.Bind(
                "6. Visualization — Flowers", "FlowerMaxRenderDistance", 80f,
                new ConfigDescription("Only render flowers within the first N meters of the path. Beyond that the destination beam is enough.", new AcceptableValueRange<float>(0f, 1000f)));
            FlowerCountCap = cfg.Bind(
                "6. Visualization — Flowers", "FlowerCountCap", 30,
                new ConfigDescription("Maximum number of flower clusters to spawn at once (perf safety net). 4763 vertices each at LOD0; 30 = ~143k verts.", new AcceptableValueRange<int>(1, 200)));

            Verbose = cfg.Bind(
                "9. Debug", "Verbose", false,
                "Log every recalc + waypoint count.");
        }
    }
}
