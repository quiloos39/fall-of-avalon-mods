using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace QuestRegionGrouping
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.questregiongrouping";
        public const string PluginName = "QuestRegionGrouping";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");

            try
            {
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo($"{PluginName} loaded.");
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
