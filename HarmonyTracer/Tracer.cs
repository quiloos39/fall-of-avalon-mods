using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using HarmonyLib;

namespace HarmonyTracer
{
    /// <summary>
    /// Generic Harmony-based call tracer. Driven entirely by config:
    ///   * scan assemblies in TraceAssemblies
    ///   * keep types whose full name matches any regex in TraceTypes
    ///   * keep methods whose name matches TraceMethods
    ///   * apply a Prefix (entry log) and Postfix (exit log) to each
    ///
    /// All logging goes through one static log source so the patches don't have
    /// to capture state. Every operation is wrapped in try/catch — a tracing
    /// tool that crashes the game it's tracing is worse than useless.
    /// </summary>
    internal static class Tracer
    {
        private static ManualLogSource _log;
        private static TracerConfig _cfg;
        private static Harmony _harmony;
        private static int _patchCount;

        // The Prefix/Postfix MethodInfos are resolved once and shared across every patch — Harmony
        // is fine with one HarmonyMethod patching N originals.
        private static HarmonyMethod _prefix;
        private static HarmonyMethod _postfix;

        public static int PatchCount => _patchCount;

        public static void Install(Harmony harmony, TracerConfig cfg, ManualLogSource log)
        {
            _harmony = harmony;
            _cfg = cfg;
            _log = log;
            _patchCount = 0;

            var typePatterns = SplitCsv(cfg.TraceTypes.Value)
                .Select(SafeCompile)
                .Where(r => r != null)
                .ToArray();

            if (typePatterns.Length == 0)
            {
                _log.LogInfo("[HarmonyTracer] TraceTypes is empty — no patches will be applied. " +
                    "Set TraceTypes in the config (e.g. 'Awaken\\.TG\\.Main\\.Crafting\\..*') and reload.");
                return;
            }

            Regex methodPattern;
            try { methodPattern = new Regex(cfg.TraceMethods.Value, RegexOptions.Compiled); }
            catch (Exception e)
            {
                _log.LogError($"[HarmonyTracer] TraceMethods regex invalid: {e.Message}. Aborting.");
                return;
            }

            var assemblyAllowlist = new HashSet<string>(
                SplitCsv(cfg.TraceAssemblies.Value),
                StringComparer.OrdinalIgnoreCase);

            _prefix = new HarmonyMethod(typeof(Tracer).GetMethod(nameof(EntryHook), BindingFlags.Static | BindingFlags.NonPublic));
            _postfix = new HarmonyMethod(typeof(Tracer).GetMethod(nameof(ExitHook), BindingFlags.Static | BindingFlags.NonPublic));

            var sw = Stopwatch.StartNew();
            int typesScanned = 0;
            int methodsConsidered = 0;
            int skipped = 0;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName;
                try { asmName = asm.GetName().Name; }
                catch { continue; }

                if (!assemblyAllowlist.Contains(asmName)) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(t => t != null).ToArray(); }
                catch (Exception e)
                {
                    _log.LogWarning($"[HarmonyTracer] Skipping assembly {asmName}: {e.GetType().Name}: {e.Message}");
                    continue;
                }

                foreach (var type in types)
                {
                    typesScanned++;
                    string fullName;
                    try { fullName = type.FullName; }
                    catch { continue; }
                    if (string.IsNullOrEmpty(fullName)) continue;

                    if (!typePatterns.Any(p => p.IsMatch(fullName))) continue;

                    // Generics are a pain — the closed forms aren't always present and Harmony
                    // can't patch open generics directly. Skip them rather than try to be clever.
                    if (type.IsGenericTypeDefinition) continue;
                    if (type.ContainsGenericParameters) continue;

                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(
                            BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.DeclaredOnly);
                    }
                    catch (Exception e)
                    {
                        _log.LogWarning($"[HarmonyTracer] GetMethods({fullName}) failed: {e.Message}");
                        continue;
                    }

                    foreach (var method in methods)
                    {
                        methodsConsidered++;

                        if (_patchCount >= cfg.MaxPatches.Value)
                        {
                            _log.LogWarning($"[HarmonyTracer] Hit MaxPatches cap ({cfg.MaxPatches.Value}) — stopping. " +
                                "Tighten TraceTypes/TraceMethods or raise MaxPatches if intentional.");
                            goto Done;
                        }

                        if (!ShouldPatch(method, methodPattern)) { skipped++; continue; }

                        try
                        {
                            harmony.Patch(method, prefix: _prefix, postfix: _postfix);
                            _patchCount++;
                        }
                        catch (Exception e)
                        {
                            // Most failures here are "method has no body" / IL2CPP shenanigans / native
                            // method. Log once at debug-level density so the user can grep for them.
                            skipped++;
                            _log.LogDebug($"[HarmonyTracer] Patch failed: {fullName}.{method.Name}: {e.GetType().Name}: {e.Message}");
                        }
                    }
                }
            }

            Done:
            sw.Stop();
            _log.LogInfo($"[HarmonyTracer] Patched {_patchCount} method(s) " +
                $"({methodsConsidered} considered, {skipped} skipped) " +
                $"across {typesScanned} types in {sw.ElapsedMilliseconds} ms.");
        }

        private static bool ShouldPatch(MethodInfo m, Regex methodPattern)
        {
            if (m == null) return false;
            if (m.IsAbstract) return false;
            if (m.ContainsGenericParameters) return false;
            if (m.IsGenericMethodDefinition) return false;

            // Native / extern methods have no body for Harmony to wrap around.
            if ((m.MethodImplementationFlags & MethodImplAttributes.InternalCall) != 0) return false;
            if ((m.GetMethodImplementationFlags() & MethodImplAttributes.InternalCall) != 0) return false;
            if ((m.Attributes & MethodAttributes.PinvokeImpl) != 0) return false;

            // Property accessors / event accessors get traced too (they're just MethodInfos), and
            // that's intentional — but constructors are skipped because GetMethods doesn't return them
            // and we don't add them on purpose; ctors with Harmony need extra care.

            if (!methodPattern.IsMatch(m.Name)) return false;

            // Avoid patching ourselves. Harmony's own internals would also be a foot-gun.
            var declaringAsm = m.DeclaringType?.Assembly?.GetName().Name;
            if (declaringAsm == "HarmonyTracer" || declaringAsm == "0Harmony" || declaringAsm == "BepInEx") return false;

            return true;
        }

        public static void Uninstall()
        {
            try { _harmony?.UnpatchSelf(); }
            catch (Exception e) { _log?.LogWarning($"[HarmonyTracer] UnpatchSelf failed: {e.Message}"); }
        }

        // ---- Harmony hooks (must be public-or-non-public static; signature uses __originalMethod
        //      and __result so Harmony injects the live values without us writing per-method patches) ----

        private static void EntryHook(MethodBase __originalMethod, object[] __args)
        {
            if (_log == null || _cfg == null) return;
            try
            {
                var sb = new StringBuilder(128);
                sb.Append("[HarmonyTracer] ENTER ");
                AppendMethodName(sb, __originalMethod);
                sb.Append('(');
                if (_cfg.LogArgs.Value && __args != null && __args.Length > 0)
                {
                    var parameters = __originalMethod.GetParameters();
                    for (int i = 0; i < __args.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(i < parameters.Length ? parameters[i].Name : "arg" + i);
                        sb.Append('=');
                        sb.Append(Truncate(SafeFormat(__args[i]), _cfg.ArgValueMaxLength.Value));
                    }
                }
                sb.Append(')');
                _log.LogInfo(sb.ToString());

                if (_cfg.LogStackTrace.Value)
                {
                    // Skip frames: this method, the IL trampoline, and the patched prefix dispatcher.
                    var trace = new StackTrace(2, false);
                    int frames = Math.Min(5, trace.FrameCount);
                    var ssb = new StringBuilder("[HarmonyTracer]   stack:");
                    for (int i = 0; i < frames; i++)
                    {
                        var f = trace.GetFrame(i);
                        var m = f?.GetMethod();
                        if (m == null) continue;
                        ssb.Append("\n    at ").Append(m.DeclaringType?.FullName ?? "?").Append('.').Append(m.Name);
                    }
                    _log.LogInfo(ssb.ToString());
                }
            }
            catch (Exception e)
            {
                // Last-resort: never throw out of a Harmony hook. A throwing prefix would skip the
                // original method (Harmony interprets a thrown exception in a void-returning prefix
                // as "don't run original"), and we'd silently break the game.
                try { _log.LogError($"[HarmonyTracer] EntryHook crashed for {__originalMethod?.Name}: {e}"); } catch { }
            }
        }

        private static void ExitHook(MethodBase __originalMethod, object __result)
        {
            if (_log == null || _cfg == null) return;
            if (!_cfg.LogResult.Value) return;
            try
            {
                var sb = new StringBuilder(96);
                sb.Append("[HarmonyTracer] EXIT  ");
                AppendMethodName(sb, __originalMethod);
                sb.Append(" => ");
                sb.Append(Truncate(SafeFormat(__result), _cfg.ArgValueMaxLength.Value));
                _log.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                try { _log.LogError($"[HarmonyTracer] ExitHook crashed for {__originalMethod?.Name}: {e}"); } catch { }
            }
        }

        // ---- Helpers ----

        private static void AppendMethodName(StringBuilder sb, MethodBase m)
        {
            if (m == null) { sb.Append("?.?"); return; }
            sb.Append(m.DeclaringType?.FullName ?? "?");
            sb.Append('.');
            sb.Append(m.Name);
        }

        private static string SafeFormat(object o)
        {
            if (o == null) return "null";
            try { return o.ToString() ?? "null"; }
            catch (Exception e) { return "<ToString threw " + e.GetType().Name + ">"; }
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return "null";
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }

        private static IEnumerable<string> SplitCsv(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) yield break;
            foreach (var part in s.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) yield return trimmed;
            }
        }

        private static Regex SafeCompile(string pattern)
        {
            try { return new Regex(pattern, RegexOptions.Compiled); }
            catch (Exception e)
            {
                _log?.LogWarning($"[HarmonyTracer] Invalid regex '{pattern}': {e.Message}");
                return null;
            }
        }
    }
}
