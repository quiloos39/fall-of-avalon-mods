using BepInEx.Configuration;
using UnityEngine;

namespace ViewDistanceTuner
{
    internal class ViewDistanceTunerConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<KeyCode> MenuHotkey { get; }
        public ConfigEntry<float> MenuWindowX { get; }
        public ConfigEntry<float> MenuWindowY { get; }

        public ConfigEntry<float> LodBiasMultiplier { get; }
        public ConfigEntry<float> ShadowDistanceMultiplier { get; }
        public ConfigEntry<float> LayerCullDistanceMultiplier { get; }
        public ConfigEntry<float> CameraFarClipPlane { get; }

        public ConfigEntry<float> FogMeanFreePathMultiplier { get; }
        public ConfigEntry<float> FogMaximumHeightAdditive { get; }

        public ConfigEntry<float> VegetationSpawnDistanceMultiplier { get; }
        public ConfigEntry<float> VegetationBillboardDistanceMultiplier { get; }
        public ConfigEntry<float> DistanceCullerRangeMultiplier { get; }

        public ConfigEntry<bool> Verbose { get; }

        public ViewDistanceTunerConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "1. General", "Enabled", true,
                "Master switch. Disables all patches when false (game restart required to take effect).");

            MenuHotkey = cfg.Bind(
                "1. General", "MenuHotkey", KeyCode.F8,
                "Toggles the in-game tuning menu. Set to None to disable. Cursor is unlocked while the menu is open.");

            MenuWindowX = cfg.Bind(
                "1. General", "MenuWindowX", 60f,
                "Saved menu X position (top-left corner). Updated when you drag the menu.");

            MenuWindowY = cfg.Bind(
                "1. General", "MenuWindowY", 60f,
                "Saved menu Y position (top-left corner). Updated when you drag the menu.");

            LodBiasMultiplier = cfg.Bind(
                "2. Geometry", "LodBiasMultiplier", 1.5f,
                new ConfigDescription(
                    "Multiplies the engine's LOD bias. Higher = LOD0 (full-detail meshes) holds farther from camera. " +
                    "Bypasses the in-game Distance Culling slider's 1.0 cap. " +
                    "1.0 = vanilla Ultra, 1.5 = ~50% farther, 2.0 = double, 3.0+ may cost a lot of GPU on dense scenes.",
                    new AcceptableValueRange<float>(0.5f, 5f)));

            CameraFarClipPlane = cfg.Bind(
                "2. Geometry", "CameraFarClipPlane", 0f,
                new ConfigDescription(
                    "If > 0, overrides the world camera's far clip plane (units = meters). " +
                    "Use this to render very distant terrain/skybox geometry that the engine clips. " +
                    "0 = leave the engine default (recommended unless you can see hard cutoff at the horizon).",
                    new AcceptableValueRange<float>(0f, 100000f)));

            LayerCullDistanceMultiplier = cfg.Bind(
                "2. Geometry", "LayerCullDistanceMultiplier", 2.0f,
                new ConfigDescription(
                    "Multiplies the per-layer cull distances configured by ByLayerFarPlane on the world camera. " +
                    "Several layers (props, vegetation, small detail) are culled at distances < camera far plane; " +
                    "this pushes them out without forcing the engine to render distant cull-priority objects across the board. " +
                    "Applied at scene start; restart or change scene after editing.",
                    new AcceptableValueRange<float>(0.5f, 10f)));

            ShadowDistanceMultiplier = cfg.Bind(
                "3. Shadows", "ShadowDistanceMultiplier", 2.0f,
                new ConfigDescription(
                    "Multiplies the HDRP shadow max distance after the in-game Shadows slider has applied. " +
                    "Higher = shadows visible farther = more GPU cost. 2.0 doubles the distance vs vanilla Ultra.",
                    new AcceptableValueRange<float>(0.5f, 10f)));

            FogMeanFreePathMultiplier = cfg.Bind(
                "4. Fog", "FogMeanFreePathMultiplier", 2.0f,
                new ConfigDescription(
                    "Multiplies HDRP volumetric fog 'mean free path' — the distance light travels through fog before being scattered. " +
                    "Higher = THINNER fog = see further through it. 1.0 = vanilla, 2.0 = roughly twice the visibility, 5.0+ removes most fog. " +
                    "Applied every frame so changes take effect immediately.",
                    new AcceptableValueRange<float>(0.1f, 20f)));

            FogMaximumHeightAdditive = cfg.Bind(
                "4. Fog", "FogMaximumHeightAdditive", 0f,
                new ConfigDescription(
                    "Adds (in meters) to the fog volume's maximum height ceiling. " +
                    "Use if mountains/distant ridgelines look hazy from below — raises the fog ceiling so they pop out of the fog layer. " +
                    "0 = no change.",
                    new AcceptableValueRange<float>(0f, 5000f)));

            VegetationSpawnDistanceMultiplier = cfg.Bind(
                "5. Vegetation", "VegetationSpawnDistanceMultiplier", 1.5f,
                new ConfigDescription(
                    "Multiplies the LeshyRenderer spawn distance for grass/plants/objects/trees/ivy on top of the in-game Vegetation preset. " +
                    "1.0 = vanilla Ultra preset (after the engine's BiasValue × 3 clamp), 1.5 = 50% farther grass/foliage, 2.0+ = quite expensive on dense scenes. " +
                    "Highest visible-impact knob for grass/foliage pop-in.",
                    new AcceptableValueRange<float>(0.5f, 5f)));

            VegetationBillboardDistanceMultiplier = cfg.Bind(
                "5. Vegetation", "VegetationBillboardDistanceMultiplier", 2.0f,
                new ConfigDescription(
                    "Multiplies the distance at which LeshyRenderer instances transition from full mesh to billboard imposter. " +
                    "Higher = more 3D foliage at distance instead of flat billboards. Costs GPU but kills the 'cardboard tree' look at mid-far range. " +
                    "Independent of spawn distance — billboards still draw past spawn cutoff.",
                    new AcceptableValueRange<float>(0.5f, 5f)));

            DistanceCullerRangeMultiplier = cfg.Bind(
                "6. PropCulling", "DistanceCullerRangeMultiplier", 2.0f,
                new ConfigDescription(
                    "Multiplies the 9-tier cull distance table the game uses to despawn props by bound size " +
                    "(small at 20m, medium at 50-200m, big at 800-1200m, huge at 5000m+). 2.0 doubles every tier. " +
                    "Fixes mid-range pop-in of barrels/crates/rocks/etc. that LOD bias doesn't touch. " +
                    "Applied once at plugin load — restart the game to retune.",
                    new AcceptableValueRange<float>(0.5f, 5f)));

            Verbose = cfg.Bind(
                "9. Debug", "Verbose", false,
                "Log every patch application (per-frame for fog — spammy). Off by default.");
        }
    }
}
