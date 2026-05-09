using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ViewDistanceTuner
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.viewdistancetuner";
        public const string PluginName = "ViewDistanceTuner";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static ViewDistanceTunerConfig Cfg;

        private Harmony _harmony;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");

            Cfg = new ViewDistanceTunerConfig(Config);

            if (!Cfg.Enabled.Value)
            {
                Log.LogInfo($"{PluginName} disabled in config — patches not installed.");
                return;
            }

            // Capture DistanceCuller.Ranges baseline + apply multiplier BEFORE PatchAll so
            // the static array is already boosted when any DistanceCuller.Initialize runs.
            Apply.RefreshDistanceCuller();

            // Add the OnGUI menu component (handles its own F8 toggle in Update).
            gameObject.AddComponent<Menu>();

            try
            {
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo($"{PluginName} loaded. Press {Cfg.MenuHotkey.Value} for the tuning menu. " +
                            $"LOD ×{Cfg.LodBiasMultiplier.Value}, " +
                            $"shadows ×{Cfg.ShadowDistanceMultiplier.Value}, " +
                            $"layerCull ×{Cfg.LayerCullDistanceMultiplier.Value}, " +
                            $"farClip={Cfg.CameraFarClipPlane.Value}, " +
                            $"fogMFP ×{Cfg.FogMeanFreePathMultiplier.Value}, " +
                            $"fogHeight+{Cfg.FogMaximumHeightAdditive.Value}, " +
                            $"vegSpawn ×{Cfg.VegetationSpawnDistanceMultiplier.Value}, " +
                            $"vegBillboard ×{Cfg.VegetationBillboardDistanceMultiplier.Value}, " +
                            $"cullerRange ×{Cfg.DistanceCullerRangeMultiplier.Value}.");
                VerifyPatches();
            }
            catch (System.Exception e)
            {
                Log.LogError($"[VerifyPatches] {PluginGuid}: PatchAll FAILED: {e}");
            }
        }

        private void VerifyPatches()
        {
            try
            {
                var patched = _harmony?.GetPatchedMethods();
                int count = 0;
                if (patched != null)
                {
                    foreach (var m in patched)
                    {
                        count++;
                        Log.LogDebug($"[VerifyPatches]   - {m?.DeclaringType?.FullName}.{m?.Name}");
                    }
                }
                if (count == 0)
                    Log.LogWarning($"[VerifyPatches] {PluginGuid}: 0 method(s) patched");
                else
                    Log.LogInfo($"[VerifyPatches] {PluginGuid}: {count} method(s) patched");
            }
            catch (System.Exception e)
            {
                Log.LogError($"[VerifyPatches] {PluginGuid}: enumeration failed: {e}");
            }
        }

        public void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { /* nbd */ }
        }
    }
}
