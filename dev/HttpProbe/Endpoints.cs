using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HttpProbe
{
    internal static class Endpoints
    {
        // List of assemblies the server has hot-loaded via /dlls/load. Held strongly so Mono won't
        // unload them (.NET Framework / Mono can't unload an Assembly anyway, but we want the
        // reference for /dlls/loaded to introspect).
        private static readonly List<Assembly> LoadedAssemblies = new List<Assembly>();
        private static readonly object LoadedLock = new object();

        public static void RegisterAll(HttpServer server)
        {
            // Read-only / always-safe
            server.Register("GET", "/", Index);
            server.Register("GET", "/status", Status);
            server.Register("GET", "/routes", Routes);
            server.Register("GET", "/jobs", JobsList);
            server.Register("GET", "/jobs/{id}", JobGet);
            server.Register("DELETE", "/jobs/{id}", JobKill);
            server.Register("DELETE", "/jobs", JobKillAll);
            server.Register("POST", "/jobs/cleanup", JobCleanup);
            server.Register("GET", "/dlls", DllsList);
            server.Register("GET", "/dlls/loaded", DllsLoaded);

            // Side-effecting / dangerous (gated on AllowDangerousEndpoints)
            server.Register("POST", "/jobs/spawn", JobSpawnDll);
            server.Register("POST", "/dlls/load", DllsLoad);
            server.Register("POST", "/dlls/exec", DllsExec);
            server.Register("POST", "/eval", Eval);
            server.Register("POST", "/update", Update);
        }

        // ─── Read-only ────────────────────────────────────────────────────────────────

        private static Task<HttpResponse> Index(HttpRequest req, CancellationToken ct)
        {
            return Task.FromResult(HttpResponse.Json(new
            {
                name = Plugin.PluginName,
                version = Plugin.PluginVersion,
                base64Required = Plugin.Cfg.RequireBase64.Value,
                authRequired = !string.IsNullOrEmpty(Plugin.Cfg.AuthToken.Value),
                dangerousEndpointsAllowed = Plugin.Cfg.AllowDangerousEndpoints.Value,
                routes = Plugin.Server.ListAllRoutes(),
            }));
        }

        private static Task<HttpResponse> Status(HttpRequest req, CancellationToken ct)
        {
            return Task.FromResult(HttpResponse.Json(new
            {
                up = true,
                processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                workingSetMb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024),
                gcMb = GC.GetTotalMemory(false) / (1024 * 1024),
                jobsRunning = JobRegistry.List().Count(j => j.Status == JobStatus.Running),
                jobsTotal = JobRegistry.List().Count,
                loadedAssembliesCount = LoadedAssemblies.Count,
                pluginsFolder = Paths.PluginPath,
            }));
        }

        private static Task<HttpResponse> Routes(HttpRequest req, CancellationToken ct)
        {
            return Task.FromResult(HttpResponse.Json(Plugin.Server.ListAllRoutes()));
        }

        // ─── Jobs ────────────────────────────────────────────────────────────────────

        private static Task<HttpResponse> JobsList(HttpRequest req, CancellationToken ct)
        {
            return Task.FromResult(HttpResponse.Json(JobRegistry.List()));
        }

        private static Task<HttpResponse> JobGet(HttpRequest req, CancellationToken ct)
        {
            string id = req.Headers["X-Path-id"];
            var info = JobRegistry.Get(id);
            return Task.FromResult(info == null
                ? HttpResponse.Error($"No such job '{id}'", 404)
                : HttpResponse.Json(info));
        }

        private static Task<HttpResponse> JobKill(HttpRequest req, CancellationToken ct)
        {
            string id = req.Headers["X-Path-id"];
            bool ok = JobRegistry.Kill(id);
            return Task.FromResult(HttpResponse.Json(new { id, killed = ok }));
        }

        private static Task<HttpResponse> JobKillAll(HttpRequest req, CancellationToken ct)
        {
            JobRegistry.KillAll();
            return Task.FromResult(HttpResponse.Json(new { killed = "all" }));
        }

        private static Task<HttpResponse> JobCleanup(HttpRequest req, CancellationToken ct)
        {
            int n = JobRegistry.RemoveCompleted();
            return Task.FromResult(HttpResponse.Json(new { removed = n }));
        }

        // POST /jobs/spawn body: { name, dllBase64, type, method }
        // Spawns a long-running task that calls the static method (CancellationToken) -> Task<string>
        // exposed by the loaded DLL.
        private static Task<HttpResponse> JobSpawnDll(HttpRequest req, CancellationToken ct)
        {
            if (!Plugin.Cfg.AllowDangerousEndpoints.Value) return Task.FromResult(HttpResponse.Error("Dangerous endpoints disabled in config", 403));

            JObject body;
            try { body = JObject.Parse(req.DecodedBodyText); }
            catch (Exception e) { return Task.FromResult(HttpResponse.Error("Body must be base64(JSON)", 400, e)); }

            string name = body.Value<string>("name") ?? "spawned-job";
            string dllB64 = body.Value<string>("dllBase64");
            string typeName = body.Value<string>("type");
            string methodName = body.Value<string>("method") ?? "Run";

            if (string.IsNullOrEmpty(dllB64) || string.IsNullOrEmpty(typeName))
                return Task.FromResult(HttpResponse.Error("Need fields: dllBase64, type, [method, name]", 400));

            try
            {
                var asm = Assembly.Load(Convert.FromBase64String(dllB64));
                lock (LoadedLock) LoadedAssemblies.Add(asm);

                var t = asm.GetType(typeName, throwOnError: true);
                var m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (m == null) return Task.FromResult(HttpResponse.Error($"No public static method '{methodName}' on '{typeName}'", 404));

                var info = JobRegistry.Spawn(name, async cancel =>
                {
                    var result = m.Invoke(null, new object[] { cancel });
                    return await CoerceToStringAsync(result);
                });
                return Task.FromResult(HttpResponse.Json(new { spawned = info }));
            }
            catch (Exception e)
            {
                return Task.FromResult(HttpResponse.Error("Spawn failed", 500, e));
            }
        }

        // ─── DLLs ────────────────────────────────────────────────────────────────────

        private static Task<HttpResponse> DllsList(HttpRequest req, CancellationToken ct)
        {
            try
            {
                var entries = Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories)
                    .Select(p => new FileInfo(p))
                    .Select(fi => new
                    {
                        name = fi.Name,
                        relPath = fi.FullName.Substring(Paths.PluginPath.Length).TrimStart('\\', '/'),
                        sizeBytes = fi.Length,
                        lastModifiedUtc = fi.LastWriteTimeUtc,
                    })
                    .OrderBy(e => e.relPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return Task.FromResult(HttpResponse.Json(entries));
            }
            catch (Exception e) { return Task.FromResult(HttpResponse.Error("listing failed", 500, e)); }
        }

        private static Task<HttpResponse> DllsLoaded(HttpRequest req, CancellationToken ct)
        {
            lock (LoadedLock)
            {
                var data = LoadedAssemblies.Select(a => new
                {
                    name = a.GetName().Name,
                    version = a.GetName().Version?.ToString(),
                    location = TryGetLocation(a),
                    types = a.GetTypes().Select(t => t.FullName).ToList(),
                }).ToList();
                return Task.FromResult(HttpResponse.Json(data));
            }
        }

        // POST /dlls/load body: { dllBase64 }   OR   { path: "<plugins-relative path>" }
        // Loads the DLL into the current AppDomain and returns the type list.
        private static Task<HttpResponse> DllsLoad(HttpRequest req, CancellationToken ct)
        {
            if (!Plugin.Cfg.AllowDangerousEndpoints.Value) return Task.FromResult(HttpResponse.Error("Dangerous endpoints disabled in config", 403));

            JObject body;
            try { body = JObject.Parse(req.DecodedBodyText); }
            catch (Exception e) { return Task.FromResult(HttpResponse.Error("Body must be base64(JSON)", 400, e)); }

            string dllB64 = body.Value<string>("dllBase64");
            string path = body.Value<string>("path");

            try
            {
                Assembly asm;
                if (!string.IsNullOrEmpty(dllB64))
                {
                    asm = Assembly.Load(Convert.FromBase64String(dllB64));
                }
                else if (!string.IsNullOrEmpty(path))
                {
                    string full = Path.IsPathRooted(path) ? path : Path.Combine(Paths.PluginPath, path);
                    if (!File.Exists(full)) return Task.FromResult(HttpResponse.Error($"File not found: {full}", 404));
                    asm = Assembly.LoadFrom(full);
                }
                else return Task.FromResult(HttpResponse.Error("Need 'dllBase64' or 'path'", 400));

                lock (LoadedLock) LoadedAssemblies.Add(asm);

                return Task.FromResult(HttpResponse.Json(new
                {
                    loaded = true,
                    name = asm.GetName().Name,
                    version = asm.GetName().Version?.ToString(),
                    types = asm.GetTypes().Select(t => t.FullName).ToList(),
                }));
            }
            catch (Exception e)
            {
                return Task.FromResult(HttpResponse.Error("Load failed", 500, e));
            }
        }

        // POST /dlls/exec body: { type, method, [args], [onMainThread: true|false] }
        // Invokes a static method on a previously-loaded type and returns its result as JSON/string.
        private static async Task<HttpResponse> DllsExec(HttpRequest req, CancellationToken ct)
        {
            if (!Plugin.Cfg.AllowDangerousEndpoints.Value) return HttpResponse.Error("Dangerous endpoints disabled in config", 403);

            JObject body;
            try { body = JObject.Parse(req.DecodedBodyText); }
            catch (Exception e) { return HttpResponse.Error("Body must be base64(JSON)", 400, e); }

            string typeName = body.Value<string>("type");
            string methodName = body.Value<string>("method");
            bool onMain = body.Value<bool?>("onMainThread") ?? true;
            JArray argsJson = body["args"] as JArray;

            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName))
                return HttpResponse.Error("Need fields: type, method", 400);

            Type t = ResolveType(typeName);
            if (t == null) return HttpResponse.Error($"Type '{typeName}' not found in any loaded assembly", 404);

            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == methodName).ToList();
            if (methods.Count == 0) return HttpResponse.Error($"No public static method '{methodName}' on '{typeName}'", 404);

            // Pick the overload with matching argument count.
            int argc = argsJson?.Count ?? 0;
            var method = methods.FirstOrDefault(m => m.GetParameters().Length == argc);
            if (method == null) return HttpResponse.Error($"No overload of '{methodName}' takes {argc} args", 404);

            object[] args = new object[argc];
            var parms = method.GetParameters();
            for (int i = 0; i < argc; i++)
            {
                args[i] = argsJson[i].ToObject(parms[i].ParameterType);
            }

            try
            {
                object result = onMain
                    ? await MainThreadDispatcher.Run(() => method.Invoke(null, args))
                    : method.Invoke(null, args);
                string asString = await CoerceToStringAsync(result);
                return HttpResponse.Json(new { ok = true, result = asString });
            }
            catch (TargetInvocationException tie)
            {
                return HttpResponse.Error("Method threw", 500, tie.InnerException ?? tie);
            }
            catch (Exception e)
            {
                return HttpResponse.Error("Exec failed", 500, e);
            }
        }

        // POST /eval body: { dllBase64, type, [method=Run], [onMainThread=true], [args=[]] }
        // One-shot: load the DLL, invoke the method, return result. Doesn't persist the assembly
        // across calls — each /eval is fire-and-forget.
        private static async Task<HttpResponse> Eval(HttpRequest req, CancellationToken ct)
        {
            if (!Plugin.Cfg.AllowDangerousEndpoints.Value) return HttpResponse.Error("Dangerous endpoints disabled in config", 403);

            JObject body;
            try { body = JObject.Parse(req.DecodedBodyText); }
            catch (Exception e) { return HttpResponse.Error("Body must be base64(JSON)", 400, e); }

            string dllB64 = body.Value<string>("dllBase64");
            string typeName = body.Value<string>("type");
            string methodName = body.Value<string>("method") ?? "Run";
            bool onMain = body.Value<bool?>("onMainThread") ?? true;
            JArray argsJson = body["args"] as JArray;

            if (string.IsNullOrEmpty(dllB64) || string.IsNullOrEmpty(typeName))
                return HttpResponse.Error("Need fields: dllBase64, type", 400);

            try
            {
                var asm = Assembly.Load(Convert.FromBase64String(dllB64));
                lock (LoadedLock) LoadedAssemblies.Add(asm);

                var t = asm.GetType(typeName, throwOnError: true);
                int argc = argsJson?.Count ?? 0;
                var method = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == argc);
                if (method == null) return HttpResponse.Error($"No public static '{methodName}' with {argc} args on '{typeName}'", 404);

                object[] args = new object[argc];
                var parms = method.GetParameters();
                for (int i = 0; i < argc; i++) args[i] = argsJson[i].ToObject(parms[i].ParameterType);

                object result = onMain
                    ? await MainThreadDispatcher.Run(() => method.Invoke(null, args))
                    : method.Invoke(null, args);
                string asString = await CoerceToStringAsync(result);
                return HttpResponse.Json(new { ok = true, result = asString, assembly = asm.GetName().Name });
            }
            catch (TargetInvocationException tie)
            {
                return HttpResponse.Error("Method threw", 500, tie.InnerException ?? tie);
            }
            catch (Exception e)
            {
                return HttpResponse.Error("Eval failed", 500, e);
            }
        }

        // POST /update body: { dllBase64, [type=HttpProbe.Extension.Module], [method=Install] }
        // Hot-loads a DLL and calls a static Install(HttpServer server) method on it.
        // The Install method can register/unregister endpoints, monkey-patch existing ones,
        // or whatever it likes. This is "self-update" for the route table while the listener
        // keeps running. Replacing the BepInEx DLL on disk requires a game restart.
        private static async Task<HttpResponse> Update(HttpRequest req, CancellationToken ct)
        {
            if (!Plugin.Cfg.AllowDangerousEndpoints.Value) return HttpResponse.Error("Dangerous endpoints disabled in config", 403);

            JObject body;
            try { body = JObject.Parse(req.DecodedBodyText); }
            catch (Exception e) { return HttpResponse.Error("Body must be base64(JSON)", 400, e); }

            string dllB64 = body.Value<string>("dllBase64");
            string typeName = body.Value<string>("type") ?? "HttpProbe.Extension.Module";
            string methodName = body.Value<string>("method") ?? "Install";

            if (string.IsNullOrEmpty(dllB64))
                return HttpResponse.Error("Need 'dllBase64'", 400);

            try
            {
                var asm = Assembly.Load(Convert.FromBase64String(dllB64));
                lock (LoadedLock) LoadedAssemblies.Add(asm);

                var t = asm.GetType(typeName, throwOnError: false);
                if (t == null) return HttpResponse.Error($"Update DLL must contain type '{typeName}'", 400);

                var m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (m == null) return HttpResponse.Error($"Type '{typeName}' must have public static {methodName}(HttpServer)", 400);

                await MainThreadDispatcher.Run(() => { m.Invoke(null, new object[] { Plugin.Server }); return true; });

                return HttpResponse.Json(new
                {
                    updated = true,
                    assembly = asm.GetName().Name,
                    routesAfter = Plugin.Server.ListAllRoutes(),
                });
            }
            catch (TargetInvocationException tie)
            {
                return HttpResponse.Error("Update Install() threw", 500, tie.InnerException ?? tie);
            }
            catch (Exception e)
            {
                return HttpResponse.Error("Update failed", 500, e);
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────

        private static Type ResolveType(string fullName)
        {
            // Search loaded assemblies first (so user's hot-loaded DLLs win), then current AppDomain.
            lock (LoadedLock)
            {
                foreach (var a in LoadedAssemblies)
                {
                    var t = a.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
            }
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(fullName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }

        // Awaits Task / Task<T>, otherwise just stringifies. Returns "" for void/null.
        private static async Task<string> CoerceToStringAsync(object result)
        {
            if (result == null) return string.Empty;
            if (result is Task task)
            {
                await task;
                var resultProp = task.GetType().GetProperty("Result");
                if (resultProp == null) return string.Empty;
                object inner = resultProp.GetValue(task);
                return inner?.ToString() ?? string.Empty;
            }
            try { return JsonConvert.SerializeObject(result); }
            catch { return result.ToString(); }
        }

        private static string TryGetLocation(Assembly a)
        {
            try { return a.IsDynamic ? "(dynamic)" : a.Location; } catch { return null; }
        }
    }
}
