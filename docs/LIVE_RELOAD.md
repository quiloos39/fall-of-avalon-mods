# Live reload + dev iteration

## Research summary

| Tool | Status (May 2026) | BepInEx 5 | HarmonyX | Verdict |
|---|---|---|---|---|
| **BepInEx.Debug / ScriptEngine** | r11.1, **2026-01-01** | Yes (5.x required) | Plugin must `UnpatchSelf()` in `OnDestroy` | **Installed** |
| UnityHotReload (xiaoxiao921) | v1.0.1, 2025-11 | Yes | Side-steps unpatching | Skipped |
| BepInEx.GUI / DemystifyExceptions | adjacent debug tooling | n/a | n/a | Out of scope |
| Custom file-watcher + `Assembly.LoadFile` + re-PatchAll | n/a | duplicates ScriptEngine | duplicates ScriptEngine | Skipped |

**Decision: ScriptEngine.** It does exactly what we need (loads `*.dll` from `BepInEx\scripts`, F6 reloads them, FileSystemWatcher auto-reload on file changes), is two months old, and lists BepInEx 5.x as the minimum. Tainted Grail is Mono Unity 6 — out of scope of the well-known `BepInEx-Unity.IL2CPP` Unity 6 metadata bug — so the standard tooling applies. UnityHotReload was rejected because it requires referencing `UnityHotReload.dll` in every plugin csproj, prohibits adding/removing/changing fields between reloads, and needs explicit `LoadNewAssemblyVersion()` calls — far more invasive across our 11 projects than ScriptEngine's drop-in approach. A custom watcher would just reimplement ScriptEngine.

**Compat caveat with HarmonyX:** ScriptEngine itself does not unpatch on reload — each plugin has to clean up its own patches. `BetterSortingCrafting/Plugin.cs` already does (`OnDestroy { _harmony?.UnpatchSelf(); }`); other plugins follow the same pattern (verified by grepping `UnpatchSelf` / `OnDestroy` across the source root). If you add a new plugin without `UnpatchSelf()`, ScriptEngine reloads will leak old patches — symptom is duplicated log lines or stale behaviour after F6.

## Install record

- Source: `https://github.com/BepInEx/BepInEx.Debug/releases/download/r11.1/ScriptEngine_r11.1.zip`
- Installed `ScriptEngine.dll` to `<GameRoot>\BepInEx\plugins\`
- Created `<GameRoot>\BepInEx\scripts\` (empty — populated by `tools\dev-relaunch.ps1 -ScriptEngine`)
- Pre-seeded config at `<GameRoot>\BepInEx\config\BepInEx.Debug.ScriptEngine.cfg`:
  - `[AutoReload] EnableFileSystemWatcher = true` — auto-reload on file change (no hotkey needed)
  - `[AutoReload] AutoReloadDelay = 1` — 1 s after the file lands
  - `[General] ReloadKey = F11` — manual reload fallback. **Moved off the F6 default** because AutoLoot uses F6 as its toggle hotkey (`com.user.autoloot.cfg → ToggleHotkey = F6`).

## Fast loop (preferred)

Edit code, then run:

```powershell
.\tools\dev-relaunch.ps1 -Mod BetterSortingCrafting -ScriptEngine
```

This builds the project (post-build copies the DLL to `BepInEx\plugins\`), then stages a copy in `BepInEx\scripts\`. The FileSystemWatcher detects the file change after ~1 s and reloads automatically — no key press required. ScriptEngine destroys the old plugin instance, fires `OnDestroy` (which unpatches), then loads and `Awake()`s the fresh DLL.

Manual reload fallback: press **F11** in-game (changed from the F6 default to avoid the AutoLoot conflict).

Caveats: ScriptEngine cannot reload BepInEx infrastructure itself (changes to `BepInPlugin` GUID/metadata still need a relaunch), and any patches that target methods called during scene load won't re-run on already-loaded scenes. `World.AssignServices` is one-shot per app lifetime — mods that register services (none of ours currently do) will not work with reload and need a full relaunch.

## Slow loop (full relaunch fallback)

When ScriptEngine isn't enough — new plugin, GUID change, configuration entry change, or you just want a clean slate:

```powershell
.\tools\dev-relaunch.ps1                                    # build all, kill, relaunch via Steam
.\tools\dev-relaunch.ps1 -Mod StationIndicator              # build single project, relaunch
.\tools\dev-relaunch.ps1 -Mod StationIndicator -TailLog     # ...and tail BepInEx\LogOutput.log
.\tools\dev-relaunch.ps1 -NoBuild                           # just kill + relaunch (e.g. after VS build)
.\tools\dev-relaunch.ps1 -NoLaunch                          # kill only
```

The script:
- builds the solution or a single `<Mod>/<Mod>.csproj`
- gracefully closes via `CloseMainWindow`, then `Stop-Process -Force` after 8 s timeout
- launches `steam://rungameid/1466060`, falls back to the exe directly if Steam fails
- prints colourised status with elapsed times for build + total
