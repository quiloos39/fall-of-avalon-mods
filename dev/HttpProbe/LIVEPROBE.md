# LiveProbe (HttpProbe) — runtime HTTP probe for Tainted Grail: Fall of Avalon

A BepInEx plugin that exposes a small HTTP server inside the running game so
you can introspect state, hot-load DLLs, run code on the game's main thread,
and manage long-running jobs — all over `localhost`.

- **Plugin GUID:** `com.user.httpprobe`
- **Default port:** `8989` on `127.0.0.1`
- **Wire format:** base64-wrapped (request bodies and responses)
- **Files:** [`Plugin.cs`](Plugin.cs) · [`HttpServer.cs`](HttpServer.cs) · [`Endpoints.cs`](Endpoints.cs) · [`JobRegistry.cs`](JobRegistry.cs) · [`MainThreadDispatcher.cs`](MainThreadDispatcher.cs) · [`HttpProbeConfig.cs`](HttpProbeConfig.cs)

---

## Why

Game modding cycles are slow when every change requires a recompile + restart.
LiveProbe lets you:

- Inspect game state from the outside (Hero coords, inventory, world models, ...)
- Push small experimental DLLs into the running game and call methods on them
- Add new HTTP endpoints to the running probe without restarting it
- Run background jobs (loops, watchers) and kill them remotely
- Enumerate every BepInEx plugin currently on disk

It is a probe / debug surface — not a production-facing feature.

---

## Install

The plugin is built and copied to `BepInEx/plugins/HttpProbe.dll` automatically
by the `<CopyToPlugins>` MSBuild target.

```bash
cd C:/Users/quilo/source/HttpProbe
dotnet build -c Release
```

Restart the game. Verify with:

```bash
curl -s http://127.0.0.1:8989/ | base64 -d
```

You should see a JSON object listing every registered route.

---

## Architecture

```
HTTP request                                                 HTTP response
     │                                                              ▲
     ▼                                                              │
┌────────────────┐    base64-decode    ┌──────────────┐    base64-encode
│ HttpListener   │───────────────────▶ │ Endpoint     │ ───────────────▶
│ (bg thread)    │                     │ handler      │
└────────────────┘                     └──────┬───────┘
                                              │  await MainThreadDispatcher.Run(...)
                                              ▼
                                     ┌─────────────────┐
                                     │ Unity main      │ ← can touch Hero, World, etc.
                                     │ thread queue    │
                                     └─────────────────┘
```

`HttpListener` runs on a background thread. Endpoint handlers are invoked on
worker `Task`s. Anything that touches Unity APIs or game state must be marshalled
to the main thread via `MainThreadDispatcher.Run(() => ...)`. The dispatcher is
a `MonoBehaviour` whose `Update()` drains a thread-safe queue.

---

## Configuration

Lives in `BepInEx/config/com.user.httpprobe.cfg` after the first run.

| Section | Key | Default | What |
|---|---|---|---|
| `1. General` | `Enabled` | `true` | Master switch. False = listener never starts. |
| `1. General` | `BindAddress` | `127.0.0.1` | `127.0.0.1` / `localhost` = local only. `+` or `*` = all interfaces (DANGEROUS without `AuthToken`). |
| `1. General` | `Port` | `8989` | TCP port. Anything > 1024 needs no special permissions on Windows. |
| `2. Security` | `AuthToken` | *empty* | If set, every request must send `X-Auth-Token: <value>` or get `401`. |
| `2. Security` | `RequireBase64` | `true` | If true, request body must be base64; response is base64-encoded. Set false for raw `curl` testing. |
| `2. Security` | `AllowDangerousEndpoints` | `true` | Gates `/eval`, `/update`, `/dlls/load`, `/dlls/exec`, `/jobs/spawn`. |
| `3. Debug` | `Verbose` | `true` | Log every request/response to BepInEx log. |

> ⚠ If you ever change `BindAddress` away from loopback, **set `AuthToken` to a long random string**. Otherwise anyone on your network can execute arbitrary code in your game process.

---

## Wire format — the base64 envelope

When `RequireBase64 = true` (default):

- **Request body** must be `base64( <inner content> )`.
- **Response body** is `base64( <inner content> )` and the response `Content-Type` is `application/base64`.
- The **inner content** is endpoint-specific:
  - For most POST endpoints, it's a JSON object.
  - Inside that JSON, fields like `dllBase64` are base64-encoded DLL bytes (single layer — the outer envelope already covers the JSON).

When `RequireBase64 = false`, both request body and response are raw — JSON in/out, easy to test with vanilla `curl`. Useful for development; turn it back on for actual use.

### Round-trip example

```bash
# Build the inner JSON, base64-encode it, POST it.
PAYLOAD='{"name":"my-job","dllBase64":"<base64 of DLL bytes>","type":"MyMod.Probe"}'
echo -n "$PAYLOAD" | base64 -w0 \
  | curl -s -X POST --data-binary @- http://127.0.0.1:8989/jobs/spawn \
  | base64 -d
```

---

## Endpoint reference

All paths are case-insensitive. Endpoints marked **🛑 DANGER** are gated on
`AllowDangerousEndpoints` and execute arbitrary code if abused.

### Read-only

| Method | Path | Returns |
|---|---|---|
| `GET` | `/` | Server info, config flags, full route list |
| `GET` | `/status` | PID, working-set MB, GC MB, job counts, plugin folder |
| `GET` | `/routes` | Just the route list |
| `GET` | `/jobs` | All known jobs (status `Running` / `Completed` / `Failed` / `Killed`) |
| `GET` | `/jobs/{id}` | One job's full record |
| `GET` | `/dlls` | Files in `BepInEx/plugins/**/*.dll` (name, size, mtime) |
| `GET` | `/dlls/loaded` | Assemblies the probe has hot-loaded + their type lists |

### Job control

| Method | Path | Body | What |
|---|---|---|---|
| `DELETE` | `/jobs/{id}` | – | Cancel one job (its `CancellationToken` fires) |
| `DELETE` | `/jobs` | – | Cancel all running jobs |
| `POST` | `/jobs/cleanup` | – | Drop completed/failed/killed entries from the registry |
| `POST` | `/jobs/spawn` | 🛑 see below | Spawn a long-running job from a posted DLL |

### Code execution 🛑

| Method | Path | Body | What |
|---|---|---|---|
| `POST` | `/dlls/load` | `{ dllBase64? , path? }` | Load a DLL into the AppDomain. Returns its type list. |
| `POST` | `/dlls/exec` | `{ type, method, args[], onMainThread }` | Invoke a static method on a previously-loaded type |
| `POST` | `/eval` | `{ dllBase64, type, method?, args[], onMainThread? }` | One-shot: load + invoke + return result |
| `POST` | `/update` | `{ dllBase64, type?, method? }` | Self-update: load DLL whose `Install(HttpServer)` registers/unregisters routes |

---

## Common payloads

### `POST /eval` — one-shot DLL invocation

Inner JSON:

```json
{
  "dllBase64": "TVqQAAMAAAA…",
  "type": "MyMod.Probe",
  "method": "Run",
  "args": [42, "hello"],
  "onMainThread": true
}
```

The DLL must contain:

```csharp
namespace MyMod
{
    public static class Probe
    {
        // Signature: parameter types must match the JSON args.
        public static string Run(int x, string s) => $"{s}:{x}";
    }
}
```

Returns:

```json
{ "ok": true, "result": "hello:42", "assembly": "MyMod" }
```

### `POST /jobs/spawn` — long-running job

Inner JSON:

```json
{
  "name": "my-watcher",
  "dllBase64": "…",
  "type": "MyMod.Watcher",
  "method": "Run"
}
```

The DLL's `Run` must take a `CancellationToken` and return `Task<string>`:

```csharp
public static class Watcher
{
    public static async Task<string> Run(CancellationToken ct)
    {
        int n = 0;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);
            n++;
        }
        return $"ran {n} ticks";
    }
}
```

Spawning returns:

```json
{ "spawned": { "id": "1", "name": "my-watcher", "status": "Running", … } }
```

Then:

```bash
curl -s http://127.0.0.1:8989/jobs/1 | base64 -d
curl -s -X DELETE http://127.0.0.1:8989/jobs/1 | base64 -d
```

### `POST /update` — self-update (extend the route table live)

The posted DLL must export `HttpProbe.Extension.Module.Install(HttpProbe.HttpServer server)`:

```csharp
namespace HttpProbe.Extension
{
    using System.Threading.Tasks;
    using HttpProbe;

    public static class Module
    {
        public static void Install(HttpServer server)
        {
            server.Register("GET", "/hero/coords", async (req, ct) =>
            {
                var v = await MainThreadDispatcher.Run(() =>
                    Awaken.TG.Main.Heroes.Hero.Current?.Coords);
                return HttpResponse.Json(v);
            });

            server.Register("DELETE", "/hero/coords", (req, ct) =>
            {
                server.Unregister("GET", "/hero/coords");
                return Task.FromResult(HttpResponse.Json(new { removed = true }));
            });
        }
    }
}
```

The listener never restarts. The new routes are live the moment `Install` returns.

### `POST /dlls/load` — load a DLL by path or bytes

Inner JSON (one of):

```json
{ "path": "MyMod.dll" }
```

```json
{ "dllBase64": "…" }
```

Returns the loaded assembly's name, version, and full type list.

### `POST /dlls/exec` — call a method on a previously-loaded type

```json
{
  "type": "MyMod.Probe",
  "method": "GetHeroPos",
  "args": [],
  "onMainThread": true
}
```

`onMainThread = true` (default) marshals the call onto Unity's main thread, so
it's safe to touch `Hero.Current`, `World.All<T>()`, etc.

---

## Curl recipes

```bash
# Server info + route list
curl -s http://127.0.0.1:8989/ | base64 -d | jq

# Memory snapshot
curl -s http://127.0.0.1:8989/status | base64 -d | jq

# All installed plugins on disk
curl -s http://127.0.0.1:8989/dlls | base64 -d | jq

# Hot-loaded assemblies + their types
curl -s http://127.0.0.1:8989/dlls/loaded | base64 -d | jq '.[].name'

# Spawn a job from a local DLL file
DLL_B64=$(base64 -w0 < ./MyJob.dll)
INNER=$(printf '{"name":"my-job","dllBase64":"%s","type":"MyMod.Watcher"}' "$DLL_B64")
echo -n "$INNER" | base64 -w0 \
  | curl -s -X POST --data-binary @- http://127.0.0.1:8989/jobs/spawn \
  | base64 -d | jq

# Kill that job
curl -s -X DELETE http://127.0.0.1:8989/jobs/1 | base64 -d

# Kill everything
curl -s -X DELETE http://127.0.0.1:8989/jobs | base64 -d
```

If you set `RequireBase64 = false`, drop the `base64 -d` decoding from the
response and `base64 -w0` encoding from the request body — JSON straight in/out.

---

## Authoring DLLs that the probe will load

### Reference setup (csproj)

Your tooling DLL needs to reference **the exact same assemblies** the probe sees
at runtime, so `Assembly.Load(byte[])` doesn't choke on missing types:

```xml
<ItemGroup>
  <PackageReference Include="Krafs.Publicizer" Version="2.2.1" PrivateAssets="all" />
</ItemGroup>
<ItemGroup>
  <Publicize Include="TG.Main" />
</ItemGroup>
<ItemGroup>
  <Reference Include="TG.Main"     HintPath="$(GameManaged)\TG.Main.dll" Private="false" />
  <Reference Include="UnityEngine" HintPath="$(GameManaged)\UnityEngine.dll" Private="false" />
  <Reference Include="UnityEngine.CoreModule" HintPath="$(GameManaged)\UnityEngine.CoreModule.dll" Private="false" />
  <Reference Include="HttpProbe"   HintPath="$(PluginsTarget)\HttpProbe.dll" Private="false" />
</ItemGroup>
```

The `HttpProbe` reference exposes `HttpServer`, `HttpRequest`, `HttpResponse`,
`MainThreadDispatcher`, and `JobRegistry` to your DLL.

### Conventions

- For `/eval` and `/dlls/exec`: any `public static` method works, parameter
  types must JSON-deserialize from the `args` array.
- For `/jobs/spawn`: signature is `public static Task<string> Run(CancellationToken ct)`.
- For `/update`: signature is `public static void Install(HttpProbe.HttpServer server)`.
- Always touch game state on the main thread:
  ```csharp
  var coords = await MainThreadDispatcher.Run(() => Hero.Current.Coords);
  ```
- Cancellation tokens are honoured: respect `ct.ThrowIfCancellationRequested()`
  or pass `ct` to `Task.Delay`/`Task.Run` so kills are responsive.

---

## Job lifecycle

```
spawn ─▶ Running ─┬─▶ Completed     (Run returned a value)
                  ├─▶ Failed        (Run threw)
                  └─▶ Killed        (DELETE /jobs/{id})
```

- Running jobs are tracked in an in-memory `JobRegistry`.
- `KillAll()` fires on plugin unload (game shutdown).
- Completed entries linger in the registry for inspection. `POST /jobs/cleanup`
  removes them.

---

## Security

| If you have… | Risk | Mitigation |
|---|---|---|
| `BindAddress = 127.0.0.1`, `AuthToken = ""` | Trusted local-only | None needed. Other apps on the same machine could connect, but they could already inject DLLs anyway. |
| `BindAddress = +`, `AuthToken = ""` | **Anyone on your LAN can execute arbitrary code in your game process.** | Set a long random `AuthToken` immediately. Or revert to loopback. |
| `AllowDangerousEndpoints = false` | Read-only introspection only — no code paths that load assemblies or invoke methods | Use this when you only want telemetry. |

`AuthToken` is checked via constant-time `string.Equals` — fine against
casual sniffing, not against a determined attacker. **This is a debug tool**;
treat it as one.

---

## Known limits and caveats

1. **Hot-loaded DLLs cannot be unloaded.** .NET Framework / Mono have no
   `AssemblyLoadContext`. Every `/eval`, `/dlls/load`, `/jobs/spawn`, and
   `/update` call accumulates an `Assembly` in the AppDomain. For dev/probe use
   this is fine; don't run `/eval` in a tight loop with fresh bytes for hours.

2. **The plugin's own DLL on disk can't be replaced live** — Windows holds the
   file open while loaded. To upgrade `HttpProbe.dll` itself you must close the
   game, replace the file, restart. The `/update` endpoint does in-memory
   extension only; it doesn't touch the on-disk file.

3. **`Process.WorkingSet64` reports 0** under Mono/Unity for the current
   process. The `/status` endpoint exposes it anyway because the constant `0`
   is itself diagnostically useful. The `gcMb` field (managed-heap size from
   `GC.GetTotalMemory`) is accurate.

4. **`HttpListener` URL ACLs (Windows)**: binding to `127.0.0.1` + a port > 1024
   needs no special permissions. Binding to `+` or a public IP requires
   running the game as administrator OR registering the prefix once with:
   ```
   netsh http add urlacl url=http://+:8989/ user=Everyone
   ```
   The plugin logs a hint if startup fails this way.

5. **Verbose logging** is on by default. Each request produces 2 log lines
   (`-> METHOD PATH (NB)` and `<- STATUS (NB CT)`). Turn off in steady-state
   use to avoid bloating BepInEx log buffers.

6. **`Verbose = true` on AutoCollect + HttpProbe + WyrdSight** can stack into
   significant log-buffer memory in long sessions. See the AutoCollect section
   in the parent README — same applies here.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `connection refused` | Server didn't start | Check BepInEx log for `[HttpProbe]` lines; `Enabled` may be false in config |
| `401 Unauthorized` | `AuthToken` is set but request didn't include `X-Auth-Token` | Add header `-H "X-Auth-Token: <yourtoken>"` |
| `400 Body is not valid base64` | `RequireBase64 = true` and you sent raw bytes | Either base64-encode the body or set `RequireBase64 = false` |
| `404 No route for METHOD PATH` | Path typo / handler isn't registered | `curl /` to see the full route list |
| `403 Dangerous endpoints disabled in config` | `AllowDangerousEndpoints = false` | Flip it true (config-only, no rebuild) |
| `500 Method threw` with `TargetInvocationException` | Your DLL's method threw — body contains the inner exception | Read `body.exception` for the stack trace |
| Game freezes briefly when calling `/eval` | Method is doing slow work on the main thread | Set `"onMainThread": false` if game-state isn't needed, or move to `/jobs/spawn` |
| Server registers routes but `curl` hangs | Firewall is blocking | Loopback is normally allowed; check Windows Defender Firewall for the game's `.exe` |

---

## Roadmap (things I haven't built but easy adds)

- Streaming responses (Server-Sent Events) for long jobs that want to push log
  lines back to the client.
- WebSocket upgrade for bidirectional sessions.
- Built-in `GET /game/hero` returning common Hero state — currently you have
  to write a small DLL and `/eval` it.
- Roslyn-based `/eval` that takes C# source instead of pre-compiled bytes.
  (Requires bundling Microsoft.CodeAnalysis.* — adds ~10 MB to the plugin.)
- Per-route auth scopes so you can hand out a read-only token separately
  from a code-execution token.
- Optional disk persistence so registered routes survive a restart.
