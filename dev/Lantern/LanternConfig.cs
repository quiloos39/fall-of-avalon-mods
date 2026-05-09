using BepInEx.Configuration;
using UnityEngine;

namespace Lantern
{
    internal class LanternConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> EnabledByDefault { get; }
        public ConfigEntry<KeyCode> ToggleHotkey { get; }

        public ConfigEntry<float> Intensity { get; }
        public ConfigEntry<float> Range { get; }
        public ConfigEntry<float> ShapeRadius { get; }
        public ConfigEntry<bool> AffectsVolumetric { get; }
        public ConfigEntry<float> ColorR { get; }
        public ConfigEntry<float> ColorG { get; }
        public ConfigEntry<float> ColorB { get; }

        public ConfigEntry<float> OffsetUp { get; }
        public ConfigEntry<float> OffsetForward { get; }
        public ConfigEntry<float> OffsetRight { get; }

        public ConfigEntry<bool> CastShadows { get; }
        public ConfigEntry<bool> Flicker { get; }
        public ConfigEntry<float> FlickerAmount { get; }

        public ConfigEntry<bool> ShowToggleNotification { get; }

        public ConfigEntry<string> ItemNameMatch { get; }
        public ConfigEntry<string> ItemNameBlocklist { get; }
        public ConfigEntry<AttachPoint> Attach { get; }
        public ConfigEntry<float> Scale { get; }
        public ConfigEntry<float> RotationX { get; }
        public ConfigEntry<float> RotationY { get; }
        public ConfigEntry<float> RotationZ { get; }
        public ConfigEntry<bool> Verbose { get; }

        public LanternConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "1. General", "Enabled", true,
                "Master switch for the Lantern mod.");

            EnabledByDefault = cfg.Bind(
                "1. General", "EnabledByDefault", false,
                "If true, the lantern starts on when the game loads. Otherwise it must be toggled with the hotkey.");

            ToggleHotkey = cfg.Bind(
                "1. General", "ToggleHotkey", KeyCode.K,
                "Hotkey to toggle the lantern on/off in-game.");

            Intensity = cfg.Bind(
                "2. Light", "Intensity", 22f,
                new ConfigDescription(
                    "Light intensity in EV100 stops (logarithmic — like camera exposure). " +
                    "Reference: candle ≈ 10, oil lamp ≈ 16, hand torch ≈ 22 (the value the in-game equipped torch uses), " +
                    "bright daylight ≈ 24. Each +1 doubles the brightness. We use EV100 because the " +
                    "game itself uses EV100 for all its torches/lanterns; setting plain Candela was making " +
                    "everything ~40× dimmer than vanilla.",
                    new AcceptableValueRange<float>(0f, 30f)));

            Range = cfg.Bind(
                "2. Light", "Range", 13f,
                new ConfigDescription(
                    "Maximum distance (in meters) the lantern's light reaches. Game torch uses 13.",
                    new AcceptableValueRange<float>(1f, 100f)));

            ShapeRadius = cfg.Bind(
                "2. Light", "ShapeRadius", 3f,
                new ConfigDescription(
                    "Radius of the emitting sphere (meters). 0 = harsh point source. " +
                    "Higher values make the light softer and more diffuse. Game torch uses 3.",
                    new AcceptableValueRange<float>(0f, 10f)));

            AffectsVolumetric = cfg.Bind(
                "2. Light", "AffectsVolumetric", false,
                "If true, the light contributes to volumetric fog (visible 'god ray' cone in fog). " +
                "Game torch keeps this OFF to avoid noisy fog flicker. Turn on for dramatic indoor scenes.");

            ColorR = cfg.Bind(
                "2. Light", "ColorR", 1.0f,
                new ConfigDescription("Red channel of light color (0-1). Default warm orange = (1.0, 0.85, 0.6).", new AcceptableValueRange<float>(0f, 1f)));
            ColorG = cfg.Bind(
                "2. Light", "ColorG", 0.85f,
                new ConfigDescription("Green channel of light color (0-1).", new AcceptableValueRange<float>(0f, 1f)));
            ColorB = cfg.Bind(
                "2. Light", "ColorB", 0.6f,
                new ConfigDescription("Blue channel of light color (0-1).", new AcceptableValueRange<float>(0f, 1f)));

            OffsetUp = cfg.Bind(
                "3. Position", "OffsetUp", 0.3f,
                new ConfigDescription(
                    "Vertical offset above the attach bone (meters). Default 0.3 lifts the light just above " +
                    "the chest so the body mesh doesn't self-shadow it.",
                    new AcceptableValueRange<float>(-2f, 3f)));

            OffsetForward = cfg.Bind(
                "3. Position", "OffsetForward", 0.4f,
                new ConfigDescription(
                    "Forward offset from the attach bone (meters). Default 0.4 pushes the light out of the " +
                    "torso so the player's own body doesn't block it.",
                    new AcceptableValueRange<float>(-2f, 2f)));

            OffsetRight = cfg.Bind(
                "3. Position", "OffsetRight", 0.0f,
                new ConfigDescription(
                    "Sideways offset from the head (meters). Positive = right side.",
                    new AcceptableValueRange<float>(-2f, 2f)));

            CastShadows = cfg.Bind(
                "4. Quality", "CastShadows", false,
                "If true, the lantern casts dynamic shadows. Default OFF because the light origin is " +
                "near the player's body and shadow-casting causes self-occlusion (light can't escape " +
                "the player). Enable only if you've offset the light well clear of the body.");

            Flicker = cfg.Bind(
                "4. Quality", "Flicker", false,
                "If true, modulate intensity with Perlin noise to mimic a real flame. Default OFF — " +
                "the game's vanilla torches use VFX-driven flame visuals, not light-intensity wobble.");

            FlickerAmount = cfg.Bind(
                "4. Quality", "FlickerAmount", 0.5f,
                new ConfigDescription(
                    "Flicker amplitude in EV stops (since Intensity is EV100). 0.5 = ±0.5 stops " +
                    "(~30% brightness swing). Higher values look candle-like but get noisy fast.",
                    new AcceptableValueRange<float>(0f, 3f)));

            ShowToggleNotification = cfg.Bind(
                "5. UI", "ShowToggleNotification", true,
                "Show an on-screen notification when the toggle hotkey flips the lantern on or off.");

            ItemNameMatch = cfg.Bind(
                "6. Model", "ItemNameMatch", "",
                "Comma-separated list of case-insensitive substrings tried in order to find an in-game " +
                "ItemTemplate to spawn as a visible model accessory. " +
                "DEFAULT EMPTY: the game renders all assets via Awaken's DrakeRenderer (DOTS/ECS), " +
                "and those Drake components self-destroy when instantiated outside a baked SubScene — " +
                "so any model spawned here ends up as an invisible Transform+Collider shell anyway. " +
                "Leaving this empty avoids the wasted Addressable load + ECS-leak risk on every toggle. " +
                "If you enable it (e.g. 'wyrdlantern,lantern,torch') you'll get a hollow proxy; the LIGHT " +
                "will still work because that's our own HDRP Light, not a Drake asset.");

            ItemNameBlocklist = cfg.Bind(
                "6. Model", "ItemNameBlocklist", "drowned,schematics,recipe",
                "Comma-separated list of substrings; templates whose names contain any of these are skipped during auto-match. (E.g. 'Drowned Lantern' visually is a bag, not a lantern.)");

            Attach = cfg.Bind(
                "6. Model", "Attach", AttachPoint.Torso,
                "Which body transform the lantern follows. Torso = chest-mount (default), Hips = belt, Head = overhead, MainHand/OffHand = held in hand.");

            Scale = cfg.Bind(
                "6. Model", "Scale", 0.2f,
                new ConfigDescription(
                    "Uniform scale applied to the spawned model. PickablePrefabs are sized for floor placement and are way too big at scale 1.0; 0.2 (20%) tends to look right.",
                    new AcceptableValueRange<float>(0.05f, 5f)));

            RotationX = cfg.Bind(
                "6. Model", "RotationX", 0f,
                new ConfigDescription("Euler rotation X (degrees) applied to the model after attachment, in the attach bone's local space.", new AcceptableValueRange<float>(-180f, 180f)));
            RotationY = cfg.Bind(
                "6. Model", "RotationY", 0f,
                new ConfigDescription("Euler rotation Y.", new AcceptableValueRange<float>(-180f, 180f)));
            RotationZ = cfg.Bind(
                "6. Model", "RotationZ", 0f,
                new ConfigDescription("Euler rotation Z.", new AcceptableValueRange<float>(-180f, 180f)));

            Verbose = cfg.Bind(
                "7. Debug", "Verbose", false,
                "Write detailed setup/lookup info to BepInEx/LogOutput.log.");
        }
    }

    internal enum AttachPoint
    {
        Hips,
        Torso,
        Head,
        MainHand,
        OffHand,
    }
}
