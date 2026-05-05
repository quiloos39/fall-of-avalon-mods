using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace CraftingFilter
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.craftingfilter";
        public const string PluginName = "CraftingFilter";
        public const string PluginVersion = "0.1.0";

        internal static SafeLog Log = new SafeLog();
        private Harmony _harmony;

        public void Awake()
        {
            Log.Bind(Logger);
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

    // Wrapper around ManualLogSource that's null-safe — hot-loading via the
    // probe bypasses BepInEx's plugin lifecycle, so the underlying source is
    // unbound; we still want catch handlers to not throw secondary NREs.
    internal class SafeLog
    {
        private ManualLogSource _src;
        public void Bind(ManualLogSource src) => _src = src;
        public void LogInfo(object o)    { try { _src?.LogInfo(o);    if (_src == null) UnityEngine.Debug.Log("[CraftingFilter] " + o); } catch { } }
        public void LogWarning(object o) { try { _src?.LogWarning(o); if (_src == null) UnityEngine.Debug.LogWarning("[CraftingFilter] " + o); } catch { } }
        public void LogError(object o)   { try { _src?.LogError(o);   if (_src == null) UnityEngine.Debug.LogError("[CraftingFilter] " + o); } catch { } }
    }
}
