using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace StationIndicator
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.stationindicator";
        public const string PluginName = "StationIndicator";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static StationIndicatorConfig Cfg;

        private Scanner _scanner;
        private Harmony _harmony;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");

            Cfg = new StationIndicatorConfig(Config);

            if (!Cfg.Enabled.Value)
            {
                Log.LogInfo($"{PluginName} disabled in config — patches not installed.");
                return;
            }

            // The Scanner MonoBehaviour caches the wrapper prototype + serves as the
            // hotkey listener. The actual marker injection happens via the Harmony patch
            // on LocationSpec.GetAttachments (in Scanner.cs).
            _scanner = gameObject.AddComponent<Scanner>();

            try
            {
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo($"{PluginName} loaded. Harmony patches installed.");
            }
            catch (System.Exception e)
            {
                Log.LogError($"{PluginName} failed to install Harmony patches: {e}");
            }
        }

        public void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { /* nbd */ }
        }

        public void Update()
        {
            if (Cfg == null) return;
            if (Cfg.RescanHotkey.Value != KeyCode.None && Input.GetKeyDown(Cfg.RescanHotkey.Value))
            {
                if (_scanner != null) _scanner.ForceRescan();
            }
        }
    }
}
