using Awaken.TG.Graphics.DayNightSystem;
using Awaken.TG.LeshyRenderer;
using Awaken.TG.MVC.UI;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Settings;
using Awaken.TG.Main.Settings.Controllers;
using Awaken.TG.Main.Settings.Graphics;
using Awaken.Utility.Graphics;
using HarmonyLib;
using UnityEngine;

namespace ViewDistanceTuner
{
    // Bumps DistanceCullingSetting.BiasValue past the engine's 1.0 cap. Read by both
    // QualitySettings.lodBias and the HDRP per-camera frame-settings lodBias.
    [HarmonyPatch(typeof(DistanceCullingSetting), nameof(DistanceCullingSetting.BiasValue), MethodType.Getter)]
    internal static class DistanceCullingSetting_BiasValue_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref float __result)
        {
            var cfg = Plugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value) return;
            float mul = cfg.LodBiasMultiplier.Value;
            if (mul == 1f) return;
            __result *= mul;
            if (cfg.Verbose.Value) Plugin.Log?.LogDebug($"[LOD] BiasValue -> {__result:F3}");
        }
    }

    // After the game writes maxShadowDistance from (slider * volume-authored max), apply our
    // multiplier on top. Lets shadows extend past the volume's authored cap.
    [HarmonyPatch(typeof(ShadowsController), "OnSettingChanged")]
    internal static class ShadowsController_OnSettingChanged_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ShadowsController __instance, Setting setting)
        {
            var cfg = Plugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value) return;
            float mul = cfg.ShadowDistanceMultiplier.Value;
            if (mul == 1f) return;
            if (__instance._shadows == null) return;
            var shadows = setting as Shadows;
            if (shadows == null || !shadows.ShadowsEnabled) return;
            float boosted = shadows.ShadowsDistance * __instance._originalShadowsDistance * mul;
            __instance._shadows.maxShadowDistance.value = boosted;
            if (cfg.Verbose.Value) Plugin.Log?.LogDebug($"[Shadows] maxShadowDistance -> {boosted:F1}");
        }
    }

    // ByLayerFarPlane stamps Camera.layerCullDistances at Start(). Capture the baseline (so
    // menu changes are live-tunable) and delegate to Apply.CaptureAndApplyCamera which also
    // handles the optional camera farClipPlane override.
    [HarmonyPatch(typeof(ByLayerFarPlane), "ApplyLayers")]
    internal static class ByLayerFarPlane_ApplyLayers_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ByLayerFarPlane __instance)
        {
            var cfg = Plugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value) return;
            Apply.CaptureAndApplyCamera(__instance);
            if (cfg.Verbose.Value && __instance._camera != null)
                Plugin.Log?.LogInfo($"[Cull] Captured baseline + applied multipliers on {__instance._camera.name}");
        }
    }

    // DayNightSystem.HandleFog runs every frame and writes meanFreePath / maximumHeight from
    // time-of-day curves. Postfix to nudge the values after each curve evaluation.
    [HarmonyPatch(typeof(DayNightSystem), "HandleFog")]
    internal static class DayNightSystem_HandleFog_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(DayNightSystem __instance)
        {
            var cfg = Plugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value) return;
            var fog = __instance._fog;
            if (fog == null) return;

            float meanMul = cfg.FogMeanFreePathMultiplier.Value;
            if (meanMul != 1f)
            {
                fog.meanFreePath.value *= meanMul;
            }

            float heightAdd = cfg.FogMaximumHeightAdditive.Value;
            if (heightAdd > 0f)
            {
                fog.maximumHeight.value += heightAdd;
            }
        }
    }

    // Defensive input gate for the menu — when open, intercept PlayerInput.AfterDelivery so no
    // UI events (clicks, key presses) reach _downActions/_heldActions. The UIState.Cursor push
    // in Menu.PushUIState should already gate input via IsMapInteractive, but mouse clicks on
    // the IMGUI window were still leaking through to attacks. This prefix is the hard backstop.
    [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.AfterDelivery))]
    internal static class PlayerInput_AfterDelivery_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(UIEventDelivery delivery, ref UIEventDelivery __result)
        {
            if (!Menu.IsOpen) return true; // continue to original
            __result = delivery;            // pass-through: don't consume the event
            return false;                   // skip original — no _downActions writes
        }
    }

    // LeshyQualityManager.SpawnDistance — read every frame to decide whether to spawn each
    // vegetation instance type. Engine already does (preset_value × clamp(BiasValue, 0..3));
    // we apply our multiplier on top of that result.
    [HarmonyPatch(typeof(LeshyQualityManager), nameof(LeshyQualityManager.SpawnDistance))]
    internal static class LeshyQualityManager_SpawnDistance_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref float __result)
        {
            var cfg = Plugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value) return;
            float mul = cfg.VegetationSpawnDistanceMultiplier.Value;
            if (mul == 1f) return;
            __result *= mul;
        }
    }

    // LeshyQualityManager.BillboardDistance — read every frame for billboard-vs-mesh swap.
    // Pure preset value, no engine bias applied; we just multiply.
    [HarmonyPatch(typeof(LeshyQualityManager), nameof(LeshyQualityManager.BillboardDistance))]
    internal static class LeshyQualityManager_BillboardDistance_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref float __result)
        {
            var cfg = Plugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value) return;
            float mul = cfg.VegetationBillboardDistanceMultiplier.Value;
            if (mul == 1f) return;
            __result *= mul;
        }
    }
}
