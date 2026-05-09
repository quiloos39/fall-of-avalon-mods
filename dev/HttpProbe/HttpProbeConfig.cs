using BepInEx.Configuration;

namespace HttpProbe
{
    internal class HttpProbeConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<string> BindAddress { get; }
        public ConfigEntry<int> Port { get; }
        public ConfigEntry<string> AuthToken { get; }
        public ConfigEntry<bool> RequireBase64 { get; }
        public ConfigEntry<bool> AllowDangerousEndpoints { get; }
        public ConfigEntry<bool> Verbose { get; }

        public HttpProbeConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "1. General", "Enabled", true,
                "Master switch. If false, the HTTP server doesn't start at all.");

            BindAddress = cfg.Bind(
                "1. General", "BindAddress", "127.0.0.1",
                "Interface to bind on. 'localhost' / '127.0.0.1' = local machine only (recommended). " +
                "'+' or '*' = all interfaces (DANGEROUS — exposes the server to the network and anyone " +
                "with AuthToken can execute arbitrary code in your game process).");

            Port = cfg.Bind(
                "1. General", "Port", 8989,
                new ConfigDescription(
                    "TCP port to listen on.",
                    new AcceptableValueRange<int>(1, 65535)));

            AuthToken = cfg.Bind(
                "2. Security", "AuthToken", "",
                "If non-empty, every request must include 'X-Auth-Token: <value>' or be rejected with 401. " +
                "Strongly recommended if you ever bind to a non-loopback address.");

            RequireBase64 = cfg.Bind(
                "2. Security", "RequireBase64", true,
                "If true, every request body must be base64-encoded and every response is base64-encoded " +
                "(after decode/encode the inner content is JSON or raw bytes per the endpoint contract). " +
                "Set false for debugging via plain curl.");

            AllowDangerousEndpoints = cfg.Bind(
                "2. Security", "AllowDangerousEndpoints", true,
                "Gates /eval, /update, /dlls/exec, and similar code-execution endpoints. " +
                "Disable if you only want read-only introspection.");

            Verbose = cfg.Bind(
                "3. Debug", "Verbose", true,
                "Log every request/response to BepInEx/LogOutput.log.");
        }
    }
}
