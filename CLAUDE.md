# Tainted Grail: Fall of Avalon — BepInEx mod workspace

Repo of BepInEx 5 plugins for **Tainted Grail: Fall of Avalon** (Awaken Studio / Questline). Each mod is a standalone netstandard2.1 csproj with a `Plugin.cs` BepInEx entry point and HarmonyX (`HarmonyLib`) patches. `Krafs.Publicizer` exposes `TG.Main` internals at compile time in mods that need it.

## Repo layout

```
mods/                         # released mods (4)
dev/                          # dev tools, probes, exporters (7)
refs/                         # gitignored — decompiled reference
  game/                       # TG.Main, Awaken.*, Assembly-CSharp, VendorWrappers
  mods/                       # AvalonModManager, WyrdSight, etc.
assets/                       # gitignored — AssetRipper output (~39 GB)
data/                         # exported game data (items / quests / readables)
docs/                         # all .md except this file
  ARCHITECTURE.md             # READ THIS FIRST when starting a new mod
  COOKBOOK.md                 # mod-side patterns (plugin skeleton, UI injection, etc.)
  POST_GAME_PATCH.md          # checklist after a Steam game update
  RELEASING.md
  LIVE_RELOAD.md
  DEBUGGER_SETUP.md
  ASSETRIPPER_USAGE.md
tools/                        # PowerShell scripts + watch lists
  dev-relaunch.ps1            # build → kill game → relaunch (or stage to ScriptEngine)
  release.ps1                 # bump version, build, zip, upload to Nexus
  extract-anchors.ps1         # auto-collect symbol anchors from mod source
  verify-anchors.ps1          # check anchors against refs/game/ after game patch
  symbol-anchors.json         # committed inventory (diffable)
  README.md
CLAUDE.md                     # this file (must stay at root for auto-load)
TaintedGrailMods.sln          # opens all 11 projects (mods/ + dev/)
nexus.json                    # mod_id + file_group_id per released mod
```

## Paths

- **Source workspace:** `C:\Users\quilo\source` (this repo)
- **Game install:** `C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA` — referenced as `<GameRoot>` in csprojs
  - Game DLLs: `<GameRoot>\Fall of Avalon_Data\Managed\` (`TG.Main.dll`, `Awaken.*.dll`, `Assembly-CSharp.dll`, Unity)
  - BepInEx core: `<GameRoot>\BepInEx\core\`
  - **Plugin install target:** `<GameRoot>\BepInEx\plugins\` (post-build copies land here)
  - BepInEx logs: `<GameRoot>\BepInEx\LogOutput.log`
- **Game runtime logs (per-session):** `C:\Users\quilo\AppData\LocalLow\Questline\Fall of Avalon\Logs\<date>_<time>.txt` — game's own logger, distinct from BepInEx's. Useful for init order, asset load failures, and errors not surfaced by BepInEx.
- **Unity engine version:** 6000.0.64f1 (Unity 6)

## Build

```pwsh
dotnet build mods/BetterSortingCrafting/BetterSortingCrafting.csproj
# or whole solution:
dotnet build TaintedGrailMods.sln
```

A `CopyToPlugins` post-build target auto-copies the built DLL to `<GameRoot>\BepInEx\plugins\`. **Do not manually copy DLLs** — the build already did it. Restart the game (or use `tools\dev-relaunch.ps1 -ScriptEngine`) to pick up the new build.

## Mods (released)

| Folder | Purpose | Nexus ID |
|---|---|---|
| `mods/AutoLoot/` | Auto-loots nearby containers/corpses | 150 |
| `mods/BetterSortingCrafting/` | Search bar + grouped category filters for inventory/crafting/shop/stash UIs | 151 |
| `mods/SpoilsOfTheSlain/` | Kill- and dialogue-based recipe unlock; also unlocks discovered relics (gems) at the forge. Backfill: pre-mod inventory + currently-loaded NPCs (the game has no global "talked-to" registry, so loaded NPCs are a coarse approximation) | 148 |
| `mods/StationIndicator/` | Crafting-station indicator (also marks vendors on map) | (see `nexus.json`) |
| `mods/ViewDistanceTuner/` | LOD/shadow/layer-cull/fog/far-clip multipliers — push render distance past Ultra cap | (local, unreleased) |
| `mods/GuardVariety/` | Randomizes city-guard appearance (skin/hair/eyes/beard/blendshapes) on every spawn via the existing `NPCRandomConfigSO` pipeline; strips helmets; probabilistically swaps a percentage of guards to female humanoid prefabs discovered passively during exploration (registry persists to `BepInEx/config/com.user.guardvariety.prefabs.txt`) | (local, unreleased) |

## Dev tools (not released)

| Folder | Purpose |
|---|---|
| `dev/HarmonyTracer/` | Generic runtime tracer — point a regex at any namespace/method via `…\BepInEx\config\com.user.harmonytracer.cfg` and it logs `[HarmonyTracer] ENTER … / EXIT … => …` for matching calls. Default `TraceTypes = ""` so it idles until configured. Replaces ad-hoc probes. |
| `dev/HttpProbe/`, `dev/LanternProbe/`, `dev/QuestProbe/` | Older one-off probes — kept for reference; prefer `HarmonyTracer` for new investigations. |
| `dev/Lantern/`, `dev/QuestPath/`, `dev/DialogExporter/` | Research/data-export tools (not full mods). |
| `data/` | Exported game data sink (items / quests / readables). |

## Reference trees (`refs/`, gitignored)

ILSpy-decompiled C# for grep-based investigation. Generated with `ilspycmd -p -o refs/game/<Name> <Managed>\<Name>.dll`.

**Game-logic DLLs** (decompiled from `<GameRoot>\Fall of Avalon_Data\Managed\`):
- `refs/game/TG.Main/` — main game logic, `Awaken.TG.*` namespaces (~32 MB; the big one)
- `refs/game/Awaken.Utility/`, `refs/game/Awaken.ECS/`, `refs/game/Awaken.Kandra/`, `refs/game/Awaken.Babel/`, `refs/game/Awaken.Orchestrating/`, `refs/game/Awaken.PackageUtilities/`, `refs/game/Awaken.Tests/` — Awaken Studio libraries
- `refs/game/Assembly-CSharp/`, `refs/game/Assembly-CSharp-firstpass/`, `refs/game/VendorWrappers/` — Unity glue
- Skip Unity engine and 3rd-party DLLs (DOTween, Sirenix, FMOD, Pathfinding, Heathen Steamworks, QFSW, Newtonsoft, etc.) — public docs available.

**Other community mods** (decompiled from `<GameRoot>\BepInEx\plugins\<Mod>.dll`, kept for cross-reference):
- `refs/mods/AvalonModManager/`, `refs/mods/Better-UI/`, `refs/mods/BetterMovement/`, `refs/mods/BetterSummon/`, `refs/mods/ImprovedInventory/`, `refs/mods/MiningMultiplier/`, `refs/mods/TPCO/`, `refs/mods/WyrdSight/`

**Refresh after a game patch:**
```pwsh
ilspycmd -p -o refs/game/<Name> "<GameRoot>\Fall of Avalon_Data\Managed\<Name>.dll"
```

## Asset extraction (`assets/`, gitignored)

UI/scene/ScriptableObject extraction via **AssetRipper 1.3.14** (installed at `C:\Tools\AssetRipper\1.3.14\`). 1.3.x is GUI-only — see `docs/ASSETRIPPER_USAGE.md` for the export walkthrough. Use this when modding UI: decompiled C# tells you *what* code runs, `assets/` tells you *which prefab* it's wired to.

## Live debugger (`docs/DEBUGGER_SETUP.md`)

`boot.config` is pre-flagged for managed-debugger attach (`player-connection-debug=1`, `wait-for-managed-debugger=0`, originals backed up to `boot.config.original`). **Currently blocked:** no debug-enabled Mono build is published for Unity 6 yet (community forks max out at Unity 2022). Until one lands, use `dev/HarmonyTracer/` instead for runtime introspection. See `docs/DEBUGGER_SETUP.md` for the full status, attach steps, and revert instructions.

## Live reload (`docs/LIVE_RELOAD.md`)

ScriptEngine (BepInEx.Debug r11.1) is installed in `<GameRoot>\BepInEx\plugins\` and pre-configured with `EnableFileSystemWatcher = true`, `ReloadKey = F11` (off the F6 default to avoid the AutoLoot toggle conflict). Fastest dev loop:

```pwsh
.\tools\dev-relaunch.ps1 -Mod BetterSortingCrafting -ScriptEngine
```

Build + stage in ~600 ms, then ScriptEngine auto-reloads the running game ~1 s later. No keypress needed; F11 is the manual fallback.

## Investigating a game system — the 4 paths

1. **Grep `refs/game/`** — fastest for "where is X defined / who calls Y". Targeted reads after grep beat reading whole files.
2. **`dev/HarmonyTracer/`** — set `TraceTypes` regex, launch game, exercise the feature, read `LogOutput.log`. Tells you *what fires when*, in which order, with what args. Compensates for the missing live debugger.
3. **`assets/`** — for UI/prefab questions. Decompiled C# manipulates GameObjects whose hierarchies live here (run AssetRipper to populate; see `docs/ASSETRIPPER_USAGE.md`).
4. **Game logs** at `…\LocalLow\Questline\Fall of Avalon\Logs\` — for init-order / asset-load issues that BepInEx's log doesn't surface.

### Architecture & cookbook reference

`docs/ARCHITECTURE.md` — code-anchored cheat sheet for `TG.Main` (bootstrap order, Model/Element/View/Service patterns, domain map, UI injection points, common Harmony hooks, observed pitfalls). **Read this first** when starting a new mod — it short-circuits the 30+ minutes of re-deriving how `World`, `Services`, `Hero.Current`, `UIStateStack`, `EventSystem`, and `[Saved]` fit together.

`docs/COOKBOOK.md` — code-anchored reference of the *mod-side* patterns we use here (file layout, plugin skeleton, Harmony idioms, lifecycle/timing, UI injection, config, hotkeys, defensive coding, live-reload safety, logging). Skim for the closest precedent and copy/adapt.

## Symbol-anchor verification (`tools/`)

Every Harmony patch in this repo declares a dependency on a game-side type/method. After every game patch, regenerate `refs/game/` and run:

```pwsh
pwsh tools/extract-anchors.ps1   # re-scan mod source
pwsh tools/verify-anchors.ps1    # check each anchor exists in current refs
```

Exit code 1 if any FAIL — suitable for pre-release sanity check. See `tools/README.md`.

For the full post-Steam-update workflow (decompile → verify → triage → smoke-test → release), see `docs/POST_GAME_PATCH.md`.

## Release

See `docs/RELEASING.md`. Short version:
```pwsh
.\tools\release.ps1 -Mod <Name> -ModVersion <x.y.z> -GameVersion <x.y.z>
```
Bumps versions, builds, zips to `dist/`, uploads to Nexus. You manually commit/tag/push.

## Conventions

- Patches in `<System>Patches.cs` (e.g. `mods/BetterSortingCrafting/CraftingSortPatches.cs`).
- State-holding singletons in `<System>State.cs` (e.g. `FilterState.cs`).
- UI injection via separate `*Injector.cs` files (e.g. `CraftingSearchInjector.cs`) — keeps Harmony patches small and testable.
- BepInPlugin GUIDs use the `com.user.<modname>` pattern.
- Every released mod's `Plugin.cs` logs `[VerifyPatches] <guid>: N method(s) patched` (or `PatchAll FAILED: …`) on game launch. Read `LogOutput.log` first when a mod seems inactive — `0 patched` or a `FAILED` line tells you immediately whether the patch landed.
- Hotkey policy: AutoLoot owns **F6** (its toggle). Don't bind F6 in any new mod or dev tool. Check `<GameRoot>\BepInEx\config\*.cfg` before picking a default key.

## Testing

No automated tests — the build references proprietary game DLLs that can't be vendored or run in CI. To verify a change: `dotnet build`, restart the game (or live-reload), exercise the feature in-game.
