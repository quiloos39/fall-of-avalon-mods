# HarmonyTracer

Generic, runtime-configurable Harmony call tracer for Tainted Grail: Fall of Avalon.
A dev tool — point it at a class via regex, get `ENTER` / `EXIT` log lines for every
matching method call. Useful when you want to know *what the game is actually doing*
in a code path before you start patching it.

This is **not** a release mod. Don't ship it to Nexus. The whole point is to leave
it disabled by default and turn it on while you're investigating something.

## Install

1. Build: `dotnet build -c Release` from this directory.
2. The post-build step copies `HarmonyTracer.dll` to
   `…\Tainted Grail FoA\BepInEx\plugins\`. Done.
3. Run the game once to generate
   `…\BepInEx\config\com.user.harmonytracer.cfg`.
4. Edit the config (see below) and restart the game.

## Config

Lives at `BepInEx\config\com.user.harmonytracer.cfg` after first run.

| Key                | Default                                    | Meaning                                                                                  |
|--------------------|--------------------------------------------|------------------------------------------------------------------------------------------|
| `Enabled`          | `true`                                     | Master switch. Off = no patches at all.                                                  |
| `TraceTypes`       | `""` (empty)                               | Comma-sep regexes matching full type names. **Empty = nothing is traced** (safe default).|
| `TraceMethods`     | `.*`                                       | Regex on method names within matched types.                                              |
| `TraceAssemblies`  | `TG.Main,Awaken.Utility,Awaken.ECS`        | Assembly names to scan. Anything else is skipped.                                        |
| `LogArgs`          | `true`                                     | Log argument values on entry (truncated to `ArgValueMaxLength`).                         |
| `LogResult`        | `false`                                    | Log return values on exit.                                                               |
| `LogStackTrace`    | `false`                                    | Log a 5-frame stack on entry. Very noisy.                                                |
| `MaxPatches`       | `5000`                                     | Hard cap. Catches over-broad regexes before they patch the universe.                     |
| `ArgValueMaxLength`| `200`                                      | Per-argument string length cap.                                                          |

## Output format

Greppable, single-line per event:

```
[HarmonyTracer] ENTER Awaken.TG.Main.Crafting.Foo.Bar(arg1=42, arg2="hello")
[HarmonyTracer] EXIT  Awaken.TG.Main.Crafting.Foo.Bar => True
```

Find them in `…\BepInEx\LogOutput.log` (or the BepInEx console if you have one open).

## Example: trace BetterSortingCrafting's target area

To see every call into the crafting menu code (the area BetterSortingCrafting patches):

```ini
[2. Tracing]
TraceTypes = Awaken\.TG\.Main\.Crafting\..*
TraceMethods = .*
TraceAssemblies = TG.Main

[3. Output]
LogArgs = true
LogResult = false
```

Open the in-game crafting menu and watch `LogOutput.log`. You'll see method names
and their argument values fly by — that's your map of what to patch next.

## Caveats

- **Open generics and abstract methods are skipped** — Harmony can't patch those
  cleanly.
- **Native / `extern` methods are skipped** — no IL body to wrap.
- **Constructors are not traced.** `GetMethods()` doesn't return them; tracing
  ctors with Harmony needs special handling we haven't bothered with.
- **Performance: a wide regex on `TG.Main` will tank your framerate.** That's why
  every traced call has a string-allocating prefix. Use narrow regexes.
- **Log volume.** A few hundred patched methods in a hot UI loop can produce
  hundreds of MB of log per minute. Turn `Enabled = false` when you're done.
