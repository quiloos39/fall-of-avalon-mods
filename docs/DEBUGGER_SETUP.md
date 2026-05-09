# Tainted Grail: Fall of Avalon - Unity Debugger Attach

How to attach a managed (Mono) debugger (dnSpy or Rider) to the running game so
you can set breakpoints in game code and your BepInEx plugins.

## Detected versions

| What | Version |
|---|---|
| Unity engine | **6000.0.64f1** (Unity 6) |
| Mono runtime | `MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll`, 7,830,952 bytes |
| Closest published debug Mono | **Unity 2022.3.41f1-mbe** (in `notsapinho/dnSpy-Unity-mono-unity2022.xx` fork) |

### Mismatch and what was actually done

Nobody has published a debug-enabled Mono build for Unity 6 (6000.x) at the
time of writing. The actively maintained `dnSpyEx/dnSpy-Unity-mono` repo has no
releases and goes only up to Unity 2020.x in its branches; community forks
(`Neoshrimp`, `notsapinho`, `Funny-ppt`, `VitaminStack`) cover up to Unity
2022.x. Dropping a Unity 2022 mono into a Unity 6 game is very likely to crash
on launch, so **the original Mono DLL was NOT replaced**.

Instead, the boot.config was edited to enable Unity's built-in managed debug
listener. This is the safest approach — fully reversible, no binary changes.
Whether the stock Unity 6 release-build mono has the debugger agent compiled
in is uncertain; if it does, you can attach directly. If it doesn't, you have
two fallback paths documented below.

## What changed on disk

### Backups created (both before any modification)

```
C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\Fall of Avalon_Data\boot.config.original
C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll.original
```

The mono backup is unused right now (we did not swap), but it's there in case
you decide to try a Unity 2022 build or compile your own.

### File modified

`C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\Fall of Avalon_Data\boot.config`

Two lines appended:

```
player-connection-debug=1
wait-for-managed-debugger=0
```

- `player-connection-debug=1` — enables the Unity Player connection / managed
  debugger listener on startup.
- `wait-for-managed-debugger=0` — does NOT block game startup waiting for a
  debugger. Set to `1` if you want the game to pause until the debugger is
  attached (useful for breakpoints that fire very early).

The original boot.config contents are preserved; only those two lines were
added at the end.

## Try this first: attach to the stock runtime

1. Launch Tainted Grail: Fall of Avalon normally (Steam).
2. Open dnSpy / dnSpyEx (64-bit build).
3. Drag your plugin DLLs from `BepInEx\plugins\` into dnSpy so symbols load.
4. **Debug -> Start Debugging...** (or Ctrl+F5 -> "Continue Debugging").
5. In the dialog, set **Debug engine** to `Unity (Connect)`.
6. Address: `127.0.0.1`, Port: `55555`.
7. Press **OK**.

If dnSpy connects and your breakpoints bind, you're done.

If it fails with "could not connect" / "no debugger listening":
- The stock Unity 6 release-build Mono almost certainly does NOT have the
  debugger agent. You need a debug-enabled `mono-2.0-bdwgc.dll`. Continue with
  the fallback section below.

### Rider

1. **Run -> Attach to Unity Process...** (the standalone Unity attach action).
2. Pick `Fall of Avalon` from the list, or use **Attach to Remote Process** ->
   `127.0.0.1:55555` if it doesn't auto-discover.

If Rider does not see the process, it's the same root cause: the stock mono
isn't broadcasting the debugger UDP probe / isn't listening. Use the fallback.

## Fallback: get a debug-enabled mono-2.0-bdwgc.dll

You need ONE of these, in rough order of effort:

### Option A — DIY build (most reliable)

Clone `dnSpyEx/dnSpy-Unity-mono`, find the Unity 6000.0.64 mono source from
Unity's Mono GitHub mirror (`Unity-Technologies/mono`, branch
`unity-6000.0/mbe` or similar), apply the dnSpyEx patches, and build with
the included `umpatcher` tool. Output goes to `win64/mono-2.0-bdwgc.dll`.

References:
- https://github.com/dnSpyEx/dnSpy-Unity-mono
- https://github.com/Unity-Technologies/mono

### Option B — try a close Unity 2022.x build (risky)

Download a Unity 2022.3.41f1-mbe release (or the closest you can find) from
one of the community forks below. Drop `win64\mono-2.0-bdwgc.dll` into
`MonoBleedingEdge\EmbedRuntime\` of the game. **Likely to crash the game**
because of mono ABI changes between Unity 2022 and Unity 6, but it's worth a
shot since the backup is in place.

- https://github.com/notsapinho/dnSpy-Unity-mono-unity2022.xx
- https://github.com/VitaminStack/dnSpy-Unity-mono-unity2022.3.62f2

### Option C — wait

Watch for a Unity-6-compatible build to appear in `dnSpyEx/dnSpy-Unity-mono`,
its forks, or as a BepInEx community drop. Until then, you're stuck with
log-based debugging or `UnityExplorer`.

### After dropping in a debug-enabled DLL

1. Make sure it's at:
   `C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll`
2. Optionally (instead of, or in addition to, the boot.config edits) set the
   environment variable for the launching shell:

```powershell
$env:DNSPY_UNITY_DBG2 = '--debugger-agent=transport=dt_socket,server=y,address=127.0.0.1:55555,suspend=n,no-hide-debugger'
& 'C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\Fall of Avalon.exe'
```

The dnSpy-patched mono reads `DNSPY_UNITY_DBG2` (.NET 4.x / mono-bdwgc) at
startup. Without it, the patched build still listens on `127.0.0.1:55555` by
default. Use `suspend=y` to block at startup until the debugger attaches.

3. Attach with dnSpy/Rider exactly as in "Try this first" above.

## Reverting (do this for normal play)

The patched mono adds a small startup overhead and a mono port listener; you
probably want to revert when not actively debugging.

```powershell
# 1. Restore original mono (only matters if you swapped it)
$mbe = 'C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\MonoBleedingEdge\EmbedRuntime'
Copy-Item -Force "$mbe\mono-2.0-bdwgc.dll.original" "$mbe\mono-2.0-bdwgc.dll"

# 2. Restore original boot.config
$boot = 'C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\Fall of Avalon_Data\boot.config'
Copy-Item -Force "$boot.original" $boot
```

Or remove just the two appended lines from `boot.config`:

```powershell
$boot = 'C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\Fall of Avalon_Data\boot.config'
(Get-Content $boot) | Where-Object { $_ -notmatch '^(player-connection-debug|wait-for-managed-debugger)\s*=' } | Set-Content $boot
```

## Caveats

- **Steam will revalidate** the swapped mono DLL on "Verify integrity of game
  files" and silently restore Tainted Grail's original. Re-swap after any
  validation. boot.config is also restored on validation, so you'll need to
  re-apply the edits too.
- The patched mono adds a small (~5-10 ms) startup hit and a TCP listener on
  127.0.0.1:55555. Don't ship a save with debugger flags on if you care about
  micro-frame-time consistency.
- BepInEx 5 is already installed at `BepInEx\plugins\`; the `winhttp.dll` /
  `doorstop_config.ini` chain is intact and is unaffected by these changes.
- This game has no anti-cheat, so attaching a debugger is safe. Don't touch
  any of this on a multiplayer Unity title with EAC/BattlEye.
