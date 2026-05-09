using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace GuardVariety
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.guardvariety";
        public const string PluginName = "GuardVariety";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static GuardVarietyConfig Cfg;

        private Harmony _harmony;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");

            Cfg = new GuardVarietyConfig(Config);

            if (!Cfg.Enabled.Value)
            {
                Log.LogInfo($"{PluginName} disabled in config — patches not installed.");
                return;
            }

            // Tier 3: warm the prefab registry from disk before any NPCs spawn.
            try { PrefabRegistry.Load(); }
            catch (System.Exception e) { Log.LogWarning($"PrefabRegistry.Load failed: {e.GetBaseException().Message}"); }

            try
            {
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
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
