using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HarmonyTracer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.harmonytracer";
        public const string PluginName = "HarmonyTracer";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static TracerConfig Cfg;

        private Harmony _harmony;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");

            Cfg = new TracerConfig(Config);

            if (!Cfg.Enabled.Value)
            {
                Log.LogInfo($"{PluginName} disabled in config — patches not installed.");
                return;
            }

            try
            {
                _harmony = new Harmony(PluginGuid);
                Tracer.Install(_harmony, Cfg, Log);
                Log.LogInfo($"{PluginName} ready ({Tracer.PatchCount} patch(es) active).");
            }
            catch (Exception e)
            {
                Log.LogError($"{PluginName} failed to install patches: {e}");
            }
        }

        public void OnDestroy()
        {
            try { Tracer.Uninstall(); } catch { /* nbd */ }
        }
    }
}
