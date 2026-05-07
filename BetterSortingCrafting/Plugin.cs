using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace BetterSortingCrafting
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.bettersortingcrafting";
        public const string PluginName = "BetterSortingCrafting";
        public const string PluginVersion = "0.1.0";

        internal static SafeLog Log = new SafeLog();
        private Harmony _harmony;

        public void Awake()
        {
            Log.Bind(Logger);
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");
            _harmony = new Harmony(PluginGuid);
            try
            {
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo($"{PluginName} loaded.");
                VerifyPatches();
            }
            catch (System.Exception e)
            {
                Log.LogError($"[VerifyPatches] {PluginGuid}: PatchAll FAILED: {e}");
            }
        }

        // Surfaces silent patch failures: counts what actually landed and lists each
        // patched method at Debug level so we can sanity-check the targets.
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
        public void LogInfo(object o)    { try { _src?.LogInfo(o);    if (_src == null) UnityEngine.Debug.Log("[BetterSortingCrafting] " + o); } catch { } }
        public void LogWarning(object o) { try { _src?.LogWarning(o); if (_src == null) UnityEngine.Debug.LogWarning("[BetterSortingCrafting] " + o); } catch { } }
        public void LogError(object o)   { try { _src?.LogError(o);   if (_src == null) UnityEngine.Debug.LogError("[BetterSortingCrafting] " + o); } catch { } }
        public void LogDebug(object o)   { try { _src?.LogDebug(o);   /* skip Unity fallback — Debug-level chatter shouldn't pollute non-BepInEx logs */ } catch { } }
    }
}
