using BepInEx.Configuration;

namespace HarmonyTracer
{
    internal class TracerConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<string> TraceTypes { get; }
        public ConfigEntry<string> TraceMethods { get; }
        public ConfigEntry<string> TraceAssemblies { get; }
        public ConfigEntry<bool> LogArgs { get; }
        public ConfigEntry<bool> LogResult { get; }
        public ConfigEntry<bool> LogStackTrace { get; }
        public ConfigEntry<int> MaxPatches { get; }
        public ConfigEntry<int> ArgValueMaxLength { get; }

        public TracerConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "1. General", "Enabled", true,
                "Master switch. If false, no patches are applied.");

            // Empty default = trace nothing. Tracing every method in TG.Main would patch tens of
            // thousands of methods and the game would never finish loading, so the safe default
            // is "do nothing until the user opts in via regex".
            TraceTypes = cfg.Bind(
                "2. Tracing", "TraceTypes", "",
                "Comma-separated regexes matching full type names (e.g. 'Awaken\\.TG\\.Main\\.Crafting\\..*'). " +
                "Empty = trace nothing. Patterns are anchored implicitly via Regex.IsMatch (substring match).");

            TraceMethods = cfg.Bind(
                "2. Tracing", "TraceMethods", ".*",
                "Regex matching method names within types selected by TraceTypes. Default '.*' = all methods.");

            TraceAssemblies = cfg.Bind(
                "2. Tracing", "TraceAssemblies", "TG.Main,Awaken.Utility,Awaken.ECS",
                "Comma-separated assembly NAMES (no extension) to scan. Anything not in this list is skipped " +
                "outright — keeps Unity / mscorlib / 3rd party out of the search and out of the patcher.");

            LogArgs = cfg.Bind(
                "3. Output", "LogArgs", true,
                "Log argument values on entry via arg?.ToString() ?? \"null\" (truncated to ArgValueMaxLength).");

            LogResult = cfg.Bind(
                "3. Output", "LogResult", false,
                "Log return values on exit. Off by default — postfixes run for every void method too, but the " +
                "cost is the .ToString() of every return value of every traced call.");

            LogStackTrace = cfg.Bind(
                "3. Output", "LogStackTrace", false,
                "Log a 5-frame stack trace on entry. Useful for figuring out who called a hot method, " +
                "but very noisy.");

            MaxPatches = cfg.Bind(
                "4. Limits", "MaxPatches", 5000,
                new ConfigDescription(
                    "Hard cap on the number of methods patched. The patcher stops scanning once this many " +
                    "patches have been applied and logs a warning. Catches accidental over-broad regexes.",
                    new AcceptableValueRange<int>(1, 100000)));

            ArgValueMaxLength = cfg.Bind(
                "4. Limits", "ArgValueMaxLength", 200,
                new ConfigDescription(
                    "Per-argument string length cap when LogArgs=true. Long collections / huge objects get " +
                    "truncated to this many chars + '...'.",
                    new AcceptableValueRange<int>(16, 4096)));
        }
    }
}
