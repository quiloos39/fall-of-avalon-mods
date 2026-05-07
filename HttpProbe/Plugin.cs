using BepInEx;
using BepInEx.Logging;

namespace HttpProbe
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.user.httpprobe";
        public const string PluginName = "HttpProbe";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static HttpProbeConfig Cfg;
        internal static HttpServer Server;

        private MainThreadDispatcher _dispatcher;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading...");

            Cfg = new HttpProbeConfig(Config);
            if (!Cfg.Enabled.Value)
            {
                Log.LogInfo($"{PluginName} disabled in config — server not started.");
                return;
            }

            // Marshal-to-main-thread component so endpoint handlers can touch Unity / game state safely.
            _dispatcher = gameObject.AddComponent<MainThreadDispatcher>();

            Server = new HttpServer();
            Endpoints.RegisterAll(Server);

            try
            {
                Server.Start(Cfg.BindAddress.Value, Cfg.Port.Value);
            }
            catch (System.Exception e)
            {
                Log.LogError($"[HttpProbe] Failed to start listener on {Cfg.BindAddress.Value}:{Cfg.Port.Value}: {e}");
                Log.LogError("[HttpProbe] On Windows, binding to non-loopback addresses requires running the game as administrator OR registering a URL ACL. " +
                    "For 127.0.0.1 + a port > 1024, no special permissions are needed.");
            }
        }

        public void OnDestroy()
        {
            try { Server?.Stop(); } catch { /* nbd */ }
            JobRegistry.KillAll();
            Log.LogInfo($"{PluginName} unloaded.");
        }
    }
}
