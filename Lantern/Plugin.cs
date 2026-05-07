using BepInEx;
using BepInEx.Logging;
using UnityEngine;

using Awaken.TG.Main.Heroes;

namespace Lantern
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.lantern";
        public const string PluginName = "Lantern";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static LanternConfig Cfg;

        private LanternController _controller;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");

            Cfg = new LanternConfig(Config);

            _controller = gameObject.AddComponent<LanternController>();
            if (Cfg.EnabledByDefault.Value)
            {
                _controller.Toggle();
            }

            Log.LogInfo($"{PluginName} loaded. Lantern is {(_controller.Active ? "ON" : "OFF")} by default. Press {Cfg.ToggleHotkey.Value} in-game to toggle.");
        }

        public void Update()
        {
            if (!Cfg.Enabled.Value || Hero.Current == null) return;

            if (Input.GetKeyDown(Cfg.ToggleHotkey.Value))
            {
                _controller.Toggle();
            }
        }

        public void OnDestroy()
        {
            if (_controller != null) Destroy(_controller);
            Log.LogInfo($"{PluginName} unloaded.");
        }
    }
}
