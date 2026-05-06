# Tainted Grail: Fall of Avalon — BepInEx mods

Repo of BepInEx plugins for **Tainted Grail: Fall of Avalon** (codename "Avalon"). Each mod is a standalone netstandard2.1 csproj with a `Plugin.cs` BepInEx entry point and Harmony patches.

## Paths

- **Source workspace:** `C:\Users\quilo\source` (this repo's working tree)
- **Game install:** `C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA`
  - Game DLLs: `<GameRoot>\Fall of Avalon_Data\Managed\` (`TG.Main.dll`, `Awaken.Utility.dll`, Unity)
  - BepInEx core: `<GameRoot>\BepInEx\core\`
  - **Plugin install target:** `<GameRoot>\BepInEx\plugins\`

The game path is hardcoded as `<GameRoot>` in every `.csproj` — if it ever changes, update all four.

## Build

```
dotnet build AutoLoot/AutoLoot.csproj
```

A `CopyToPlugins` post-build target auto-copies the built DLL to `<GameRoot>\BepInEx\plugins\`. **Do not manually copy DLLs** — the build already did it. Restart the game to pick up the new build.

`Krafs.Publicizer` exposes `TG.Main` internals at compile time.

## Mods in this repo

| Folder | Purpose |
|---|---|
| `AutoLoot` | Auto-loots nearby containers/corpses |
| `BetterSortingCrafting` | Combined sort dropdown + crafting filter for inventory/crafting UI |
| `SpoilsOfTheSlain` | Kill-based recipe unlock system |
| `StationIndicator` | Crafting-station indicator |

Released to Nexus (see `nexus.json`): AutoLoot (mod 150), SpoilsOfTheSlain (mod 148). The other two aren't on Nexus yet.

## Third-party mod references

`<ModName>-ref/` folders alongside this repo (e.g. `AvalonModManager-ref/`, `WyrdSight-ref/`) contain ILSpy-decompiled source of installed third-party mods, for reference only. They are **untracked** by git and not part of this repo — read them to understand how other mods patch the game, but do not modify or commit them. Refresh with `ilspycmd -p "<GameRoot>\BepInEx\plugins\<Mod>.dll" -o ../<Mod>-ref`.

## Release

See `RELEASING.md`. Short version: `./release.ps1 -Mod <Name> -ModVersion <x.y.z> -GameVersion <x.y.z>` bumps versions, builds, zips to `dist/`, commits + tags, pushes, and uploads to Nexus.

## Testing

No automated tests — the build references proprietary game DLLs that can't be vendored or run in CI. To verify a change: `dotnet build`, restart the game, exercise the feature in-game.
