using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

using Awaken.TG.Main.Heroes;

namespace AutoLoot
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.autoloot";
        public const string PluginName = "Auto Loot";
        public const string PluginVersion = "1.0.1";

        internal static ManualLogSource Log;
        internal static AutoLootConfig Cfg;

        private Harmony _harmony;
        private AutoLooter _collector;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");

            Cfg = new AutoLootConfig(Config);

            _harmony = new Harmony(PluginGuid);

            _collector = gameObject.AddComponent<AutoLooter>();
            _collector.Active = Cfg.EnabledByDefault.Value;

            Log.LogInfo($"{PluginName} loaded. Auto-collect is {(_collector.Active ? "ON" : "OFF")} by default.");
        }

        public void Update()
        {
            if (!Cfg.Enabled.Value || Hero.Current == null) return;

            if (Input.GetKeyDown(Cfg.ToggleHotkey.Value))
            {
                _collector.Active = !_collector.Active;
                if (Cfg.ShowToggleNotification.Value)
                {
                    Notifications.ShowToggle(_collector.Active);
                }
                Log.LogInfo($"AutoLoot toggled: {_collector.Active}");
            }
        }

        public void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            if (_collector != null) Destroy(_collector);
            Log.LogInfo($"{PluginName} unloaded.");
        }
    }
}
