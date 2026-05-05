using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SpoilsOfTheSlain
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.spoilsoftheslain";
        public const string PluginName = "Spoils of the Slain";
        public const string PluginVersion = "0.10.0";

        internal static ManualLogSource Log;
        internal static SpoilsOfTheSlainConfig Cfg;

        private Harmony _harmony;
        private bool _backfillTriggered;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");

            Cfg = new SpoilsOfTheSlainConfig(Config);
            if (!Cfg.Enabled.Value)
            {
                Log.LogInfo($"{PluginName} disabled in config.");
                return;
            }

            // Per-class patch installation: a single bad target doesn't sink the rest.
            _harmony = new Harmony(PluginGuid);
            int ok = 0, fail = 0;
            foreach (var t in typeof(Plugin).Assembly.GetTypes())
            {
                if (!t.IsClass || t.GetCustomAttribute<HarmonyPatch>() == null) continue;
                try { _harmony.CreateClassProcessor(t).Patch(); ok++; }
                catch (System.Exception e)
                {
                    fail++;
                    Log.LogError($"[Patch] {t.Name} failed to install: {e.GetBaseException().Message}");
                }
            }
            Log.LogInfo($"{PluginName} loaded. {ok} patches installed, {fail} failed. " +
                        $"Stations: forge={Cfg.InjectIntoForge.Value} alchemy={Cfg.InjectIntoAlchemy.Value} cooking={Cfg.InjectIntoCooking.Value}");
        }

        // Single-shot startup driver: every frame, check if Hero.Current is loaded.
        // First time it is, run the backfill and stop checking. No periodic polling.
        public void Update()
        {
            if (Cfg == null || !Cfg.Enabled.Value) return;
            if (_backfillTriggered) return;
            try
            {
                if (StartupBackfill.TryRun()) _backfillTriggered = true;
            }
            catch (System.Exception e)
            {
                Log.LogError($"[Startup] backfill threw: {e.GetBaseException().Message}");
                _backfillTriggered = true;     // don't re-throw forever
            }
        }

        public void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { /* nbd */ }
        }
    }
}
