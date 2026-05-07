using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace QuestPath
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.questpath";
        public const string PluginName = "QuestPath";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static QuestPathConfig Cfg;

        private QuestPathController _controller;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");

            Cfg = new QuestPathConfig(Config);

            if (!Cfg.Enabled.Value)
            {
                Log.LogInfo($"{PluginName} disabled in config — controller not started.");
                return;
            }

            _controller = gameObject.AddComponent<QuestPathController>();
            Log.LogInfo($"{PluginName} loaded. Press {Cfg.ToggleHotkey.Value} to toggle path display.");
        }
    }
}
