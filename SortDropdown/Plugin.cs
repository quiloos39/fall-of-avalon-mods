using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace SortDropdown
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.sortdropdown";
        public const string PluginName = "SortDropdown";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{PluginName} loaded.");
        }

        public void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
        }
    }
}
