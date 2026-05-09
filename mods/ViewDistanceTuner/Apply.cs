using System;
using System.Collections.Generic;
using Awaken.TG.Graphics.Culling;
using Awaken.TG.MVC;
using Awaken.TG.MVC.Events;
using Awaken.TG.Main.Settings;
using Awaken.TG.Main.Settings.Graphics;
using Awaken.Utility.Graphics;
using UnityEngine;

namespace ViewDistanceTuner
{
    // Central re-apply logic. The underlying patches read Cfg.* every frame for fog and
    // vegetation knobs, so those are live. Camera-side knobs (layerCullDistances, farClipPlane)
    // and DistanceCuller.Ranges are one-shot, so we capture baselines on first apply and
    // recompute from baseline × current multiplier whenever the menu changes them.
    internal static class Apply
    {
        internal class CameraBaseline
        {
            public float[] LayerCullDistances;
            public float OriginalFarClipPlane;
        }

        // Camera ref → baseline. Cleaned of dead entries on each refresh.
        internal static readonly Dictionary<Camera, CameraBaseline> CameraBaselines = new Dictionary<Camera, CameraBaseline>();

        // Original DistanceCuller.Ranges. Captured the first time we touch the static array.
        internal static (float maxVolume, float distance)[] OriginalRanges;

        // Captures baseline (if first time we see this camera) then applies current multipliers.
        // Called from the ByLayerFarPlane.ApplyLayers postfix.
        internal static void CaptureAndApplyCamera(ByLayerFarPlane bl)
        {
            var cam = bl._camera;
            if (cam == null) return;
            if (!CameraBaselines.TryGetValue(cam, out var baseline))
            {
                baseline = new CameraBaseline
                {
                    LayerCullDistances = (float[])cam.layerCullDistances.Clone(),
                    OriginalFarClipPlane = cam.farClipPlane,
                };
                CameraBaselines[cam] = baseline;
            }
            ApplyCamera(cam, baseline);
        }

        private static void ApplyCamera(Camera cam, CameraBaseline baseline)
        {
            var cfg = Plugin.Cfg;
            if (cfg == null) return;
            float mul = cfg.LayerCullDistanceMultiplier.Value;
            float[] distances = (float[])baseline.LayerCullDistances.Clone();
            for (int i = 0; i < distances.Length; i++)
            {
                if (distances[i] > 0f) distances[i] *= mul;
            }
            cam.layerCullDistances = distances;
            float farClip = cfg.CameraFarClipPlane.Value;
            cam.farClipPlane = farClip > 0f ? farClip : baseline.OriginalFarClipPlane;
        }

        // Re-apply layer cull + far clip on every cached camera. Called from the menu when
        // either of those sliders moves.
        internal static void RefreshCameras()
        {
            List<Camera> dead = null;
            foreach (var kvp in CameraBaselines)
            {
                if (kvp.Key == null)
                {
                    (dead ??= new List<Camera>()).Add(kvp.Key);
                    continue;
                }
                ApplyCamera(kvp.Key, kvp.Value);
            }
            if (dead != null)
            {
                foreach (var k in dead) CameraBaselines.Remove(k);
            }
        }

        internal static void EnsureRangesBaseline()
        {
            if (OriginalRanges != null) return;
            try
            {
                var ranges = DistanceCuller.Ranges;
                if (ranges == null) return;
                OriginalRanges = new (float, float)[ranges.Length];
                Array.Copy(ranges, OriginalRanges, ranges.Length);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError($"EnsureRangesBaseline failed: {e}");
            }
        }

        // Reset DistanceCuller.Ranges from baseline × current multiplier, then trigger every
        // active culler to recompute its squared cull distances. Called at plugin Awake (once)
        // and from the menu when the slider moves.
        internal static void RefreshDistanceCuller()
        {
            EnsureRangesBaseline();
            if (OriginalRanges == null) return;
            float mul = Plugin.Cfg.DistanceCullerRangeMultiplier.Value;
            try
            {
                var ranges = DistanceCuller.Ranges;
                for (int i = 0; i < ranges.Length; i++)
                {
                    ranges[i] = (OriginalRanges[i].maxVolume, OriginalRanges[i].distance * mul);
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError($"RefreshDistanceCuller mutation failed: {e}");
                return;
            }
            // World might not be ready yet at plugin Awake — guard the trigger.
            try { World.Any<DistanceCullingSetting>()?.RefreshSetting(); } catch { /* World not ready */ }
        }

        // Trigger DistanceCullingSetting refresh: re-reads our patched BiasValue (so QualitySettings.lodBias
        // and HDRP camera lodBias both update) and notifies DistanceCullersService to recompute
        // _cullDistancesSq on every per-scene culler.
        internal static void RefreshLodBias()
        {
            try { World.Any<DistanceCullingSetting>()?.RefreshSetting(); } catch { }
        }

        // Fire SettingRefresh event on Shadows so ShadowsController.OnSettingChanged re-runs and
        // our postfix multiplies the new shadow distance.
        internal static void RefreshShadows()
        {
            try
            {
                var shadows = World.Any<Shadows>();
                if (shadows != null)
                {
                    ModelExtensions.Trigger(shadows, Setting.Events.SettingRefresh, shadows);
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError($"RefreshShadows failed: {e}");
            }
        }

        // Re-apply everything (used by menu Reset button).
        internal static void RefreshAll()
        {
            RefreshLodBias();
            RefreshShadows();
            RefreshDistanceCuller();
            RefreshCameras();
        }
    }
}
