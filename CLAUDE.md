# Tainted Grail: Fall of Avalon — BepInEx mod workspace

Repo of BepInEx 5 plugins for **Tainted Grail: Fall of Avalon** (Awaken Studio / Questline). Each mod is a standalone netstandard2.1 csproj with a `Plugin.cs` BepInEx entry point and HarmonyX (`HarmonyLib`) patches. `Krafs.Publicizer` exposes `TG.Main` internals at compile time in mods that need it.

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
dotnet build BetterSortingCrafting/BetterSortingCrafting.csproj
```

A `CopyToPlugins` post-build target auto-copies the built DLL to `<GameRoot>\BepInEx\plugins\`. **Do not manually copy DLLs** — the build already did it. Restart the game to pick up the new build.

## Mods (released)

| Folder | Purpose | Nexus ID |
|---|---|---|
| `AutoLoot/` | Auto-loots nearby containers/corpses | 150 |
| `BetterSortingCrafting/` | Search bar + grouped category filters for inventory/crafting/shop/stash UIs | 151 |
| `SpoilsOfTheSlain/` | Kill-based recipe unlock system | 148 |
| `StationIndicator/` | Crafting-station indicator (also marks vendors on map) | (see `nexus.json`) |

## Dev tools (not released)

| Folder | Purpose |
|---|---|
| `HarmonyTracer/` | Generic runtime tracer — point a regex at any namespace/method via `…\BepInEx\config\com.user.harmonytracer.cfg` and it logs `[HarmonyTracer] ENTER … / EXIT … => …` for matching calls. Default `TraceTypes = ""` so it idles until configured. Replaces ad-hoc probes. |
| `HttpProbe/`, `LanternProbe/`, `QuestProbe/` | Older one-off probes — kept for reference; prefer `HarmonyTracer` for new investigations. |
| `Lantern/`, `QuestPath/`, `DialogExporter/` | Research/data-export tools (not full mods). |
| `game-lore/` | Output sink for exported game data (items / quests / readables). |

## Reference trees (`*-ref/`, all gitignored)

ILSpy-decompiled C# for grep-based investigation. Generated with `ilspycmd -p -o <name>-ref <Managed>\<name>.dll`.

**Game-logic DLLs** (decompiled from `<GameRoot>\Fall of Avalon_Data\Managed\`):
- `TG.Main-ref/` — main game logic, `Awaken.TG.*` namespaces (~32 MB; the big one)
- `Awaken.Utility-ref/`, `Awaken.ECS-ref/`, `Awaken.Kandra-ref/`, `Awaken.Babel-ref/`, `Awaken.Orchestrating-ref/`, `Awaken.PackageUtilities-ref/`, `Awaken.Tests-ref/` — Awaken Studio libraries
- `Assembly-CSharp-ref/`, `Assembly-CSharp-firstpass-ref/`, `VendorWrappers-ref/` — Unity glue
- Skip Unity engine and 3rd-party DLLs (DOTween, Sirenix, FMOD, Pathfinding, Heathen Steamworks, QFSW, Newtonsoft, etc.) — public docs available.

**Other community mods** (decompiled from `<GameRoot>\BepInEx\plugins\<Mod>.dll`, kept for cross-reference):
- `AvalonModManager-ref/`, `Better-UI-ref/`, `BetterMovement-ref/`, `BetterSummon-ref/`, `ImprovedInventory-ref/`, `MiningMultiplier-ref/`, `TPCO-ref/`, `WyrdSight-ref/`

**Refresh after a game patch:**
```pwsh
ilspycmd -p -o <Name>-ref "<GameRoot>\Fall of Avalon_Data\Managed\<Name>.dll"
```

## Asset extraction (`AssetRipped/`, gitignored)

UI/scene/ScriptableObject extraction via **AssetRipper 1.3.14** (installed at `C:\Tools\AssetRipper\1.3.14\`). 1.3.x is GUI-only — see `ASSETRIPPER_USAGE.md` for the export walkthrough. Use this when modding UI: decompiled C# tells you *what* code runs, AssetRipped tells you *which prefab* it's wired to. Empty until first GUI run.

## Live debugger (`DEBUGGER_SETUP.md`)

`boot.config` is pre-flagged for managed-debugger attach (`player-connection-debug=1`, `wait-for-managed-debugger=0`, originals backed up to `boot.config.original`). **Currently blocked:** no debug-enabled Mono build is published for Unity 6 yet (community forks max out at Unity 2022). Until one lands, use `HarmonyTracer` instead for runtime introspection. See `DEBUGGER_SETUP.md` for the full status, attach steps, and revert instructions.

## Investigating a game system — the 4 paths

1. **Grep `*-ref/`** — fastest for "where is X defined / who calls Y". Targeted reads after grep beat reading whole files.
2. **`HarmonyTracer/`** — set `TraceTypes` regex, launch game, exercise the feature, read `LogOutput.log`. Tells you *what fires when*, in which order, with what args. Compensates for the missing live debugger.
3. **`AssetRipped/`** (after running AssetRipper) — for UI/prefab questions. Decompiled C# manipulates GameObjects whose hierarchies live here.
4. **Game logs** at `…\LocalLow\Questline\Fall of Avalon\Logs\` — for init-order / asset-load issues that BepInEx's log doesn't surface.

## Release

See `RELEASING.md`. Short version: `./release.ps1 -Mod <Name> -ModVersion <x.y.z> -GameVersion <x.y.z>` bumps versions, builds, zips to `dist/`, commits + tags, pushes, and uploads to Nexus.

## Conventions

- Patches in `<System>Patches.cs` (e.g. `CraftingSortPatches.cs`).
- State-holding singletons in `<System>State.cs` (e.g. `FilterState.cs`).
- UI injection via separate `*Injector.cs` files (e.g. `CraftingSearchInjector.cs`) — keeps Harmony patches small and testable.
- BepInPlugin GUIDs use the `com.user.<modname>` pattern.

## Testing

No automated tests — the build references proprietary game DLLs that can't be vendored or run in CI. To verify a change: `dotnet build`, restart the game, exercise the feature in-game.
