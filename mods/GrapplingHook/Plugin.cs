using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace GrapplingHook
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.grapplinghook";
        public const string PluginName = "Grappling Hook";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static GrapplingHookConfig Cfg;

        private GrapplingHookController _controller;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");

            Cfg = new GrapplingHookConfig(Config);

            if (!Cfg.Enabled.Value)
            {
                Log.LogInfo($"{PluginName} disabled in config.");
                return;
            }

            _controller = gameObject.AddComponent<GrapplingHookController>();
            Log.LogInfo($"{PluginName} loaded. HookKey={Cfg.HookKey.Value}, MaxRange={Cfg.MaxRange.Value}m, PullSpeed={Cfg.PullSpeed.Value}m/s.");
        }

        public void OnDestroy()
        {
            if (_controller != null) Destroy(_controller);
            Log?.LogInfo($"{PluginName} unloaded.");
        }
    }
}
