# AssetRipper usage for Tainted Grail: Fall of Avalon

Companion to `refs/` decompiled DLLs. Where those give you the C#, AssetRipper
gives you the Unity assets the C# manipulates: prefab hierarchies (the actual
GameObject + Component layouts), ScriptableObject configs, scenes, and UI.

## Tool location

- Install dir: `C:\Tools\AssetRipper\1.3.14\`
- Executable: `C:\Tools\AssetRipper\1.3.14\AssetRipper.GUI.Free.exe`
- Version: **1.3.14** (built 2026-04-25)
- Source: <https://github.com/AssetRipper/AssetRipper/releases/tag/1.3.14>
- Download URL: <https://github.com/AssetRipper/AssetRipper/releases/download/1.3.14/AssetRipper_win_x64.zip>

## What this version supports (important)

AssetRipper 1.3.x ships **only as a web-UI app** in the public Free build —
running the exe spins up a local HTTP server and opens a browser. There is **no
batch CLI** (no `AssetRipper.CLI.exe`, no `<gameRoot> -o <outputDir>` syntax).
The available command-line flags are limited to launch options:

```
AssetRipper.GUI.Free [--headless] [--port <int>] [--log] [--log-path <path>]
                     [--local-web-file <file>...] [--version] [--help]
```

So the workflow below is GUI-driven. Plan ~10–30 minutes for a full export and
expect several GB of output.

## Initial extraction (first time, or after a game patch)

1. **Launch AssetRipper:**

   ```powershell
   & "C:\Tools\AssetRipper\1.3.14\AssetRipper.GUI.Free.exe"
   ```

   A browser tab opens at `http://localhost:<port>/`.

2. **Load the game:** click *Load Folder* (or drag-drop) and point at:

   ```
   C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\Fall of Avalon_Data
   ```

   AssetRipper indexes the bundles. This takes a minute or two.

3. **Configure export (optional but recommended):**
   - *Settings → Import Settings*: leave defaults; the IL2CPP/Mono detection
     handles itself.
   - *Settings → Export Settings*: enable "Bundled Assets Export Mode = Group
     by asset type" so prefabs/scenes/MonoBehaviours land in predictable
     folders.
   - *Settings → Script Settings*: "Decompiled" if you want C# stubs alongside
     the assets (you already have the full decompile in `refs/`, so "Disabled"
     is fine and faster).

4. **Export everything:** click *Export → Export all files* and pick:

   ```
   C:\Users\quilo\source\assets
   ```

   AssetRipper writes into `assets\ExportedProject\` (a Unity-project-like
   tree).  Expect 10–30 minutes and 2–6 GB.

5. **Close the browser tab** when the status pane shows *Export complete*.
   The exe will keep running until you `Ctrl-C` the PowerShell window or close
   it.

## Refreshing after a game patch

Steam updates `Fall of Avalon_Data`. To get fresh assets:

```powershell
# 1. Wipe the old export (optional but avoids stale files mixing in)
Remove-Item -Recurse -Force C:\Users\quilo\source\assets\*

# 2. Re-run the GUI flow above, exporting back to C:\Users\quilo\source\assets
& "C:\Tools\AssetRipper\1.3.14\AssetRipper.GUI.Free.exe"
```

If a future AssetRipper release (>1.3.14) adds a true headless CLI, replace
this section with the one-liner.

## Where things land

After a full export, the structure under `C:\Users\quilo\source\assets\`
is roughly:

```
assets\
  ExportedProject\
    Assets\
      PrefabInstance\          <- prefabs as .prefab YAML (the hierarchies)
      MonoBehaviour\           <- ScriptableObject configs (loot tables, item
                                  defs, recipe lists, UI configs, etc.)
      Scene\                   <- *.unity scenes
      Resources\               <- everything reachable via Resources.Load
      Texture2D\, Sprite\, ...  <- per-type asset folders
      Scripts\                 <- (only if you enabled "Decompiled" scripts)
    ProjectSettings\
    Packages\
  AuxiliaryFiles\              <- raw bundle dumps AssetRipper couldn't classify
```

The `.prefab` and `.unity` files are plain YAML — `Grep`-able, diff-able,
openable in any editor. `MonoBehaviour\*.asset` files are also YAML and
reference the script GUIDs you can cross-link with the `refs/` decompile.

## Quick "find inventory UI" pointers

From `C:\Users\quilo\source\`:

```powershell
# Inventory-related prefabs
Get-ChildItem -Recurse assets\ExportedProject\Assets\PrefabInstance `
  -Filter "*Inventory*.prefab"

# Crafting UI prefabs
Get-ChildItem -Recurse assets\ExportedProject\Assets\PrefabInstance `
  -Filter "*Crafting*.prefab"

# Anything filter-related (popups, category lists)
Get-ChildItem -Recurse assets\ExportedProject\Assets `
  -Filter "*Filter*" -File
```

Or, with this repo's tooling, use Grep against the YAML directly:

```
# Find prefab files that reference a class you patched, e.g. CraftingItemUI
grep -r "CraftingItemUI" assets/ExportedProject/Assets/PrefabInstance
```

Common BetterSortingCrafting-relevant searches:

- `*CraftingPanel*`, `*CraftingList*`, `*RecipeList*` — the panels you inject
  the search bar into
- `*Backpack*`, `*Bag*`, `*PlayerInventory*` — bag UI hierarchies
- `*Stash*`, `*Container*` — stash/storage UIs
- `*Filter*Popup*`, `*Category*Popup*` — the popup roots referenced from the
  filter button patches
- `*Tooltip*`, `*ItemSlot*` — slot prefabs whose layout drives sorting

For ScriptableObject configs (item categories, recipe groups, etc.) that the
game wires into the UI, search:

```
grep -r "m_Script" assets/ExportedProject/Assets/MonoBehaviour | grep -i category
```

then open the matching `.asset` files to see field values.

## Extracting 3D meshes (use AssetStudio, not AssetRipper)

AssetRipper Free 1.3.14 has **no Mesh Export Format option** — its CLI / GUI
both export meshes as Unity-native `.asset` (YAML), which Blender / Maya can't
open. "Static Mesh Separation" is a Premium-only feature. For 3D model
extraction (props, weapons, characters, environment), use **AssetStudio**
alongside.

### Tool: Razviar/AssetStudio fork

- Install dir: `C:\Tools\AssetStudio\v2.4.1\`
- CLI: `C:\Tools\AssetStudio\v2.4.1\AssetStudio.CLI.exe` (full-featured)
- GUI: `C:\Tools\AssetStudio\v2.4.1\AssetStudio.GUI.exe`
- Version: **2.4.1** (built 2025-11-27, AssetStudio core 1.36)
- Source: <https://github.com/Razviar/assetstudio>
- Download URL: <https://github.com/Razviar/assetstudio/releases/download/v2.4.1/AssetStudio-net8.0-win.zip>
- Runtime: needs .NET 8 (`Microsoft.NETCore.App` + `Microsoft.WindowsDesktop.App`)
  — both already present.
- Supports Unity 2.x through Unity 6 (6000.0–6000.4+), so `6000.0.64f1` is in range.

### CLI: extract every mesh in the game to OBJ

```pwsh
& "C:\Tools\AssetStudio\v2.4.1\AssetStudio.CLI.exe" `
    "C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\Fall of Avalon_Data" `
    "C:\Users\quilo\source\assets\meshes" `
    --game Normal `
    --unity_version 6000.0.64f1 `
    --types Mesh
```

This writes **13,315 OBJ files (7.2 GB)** into `assets\meshes\Mesh\`, including
LOD levels (`Mesh_FrozenHuman_03_LOD0.obj`, `LOD1`, `LOD2`). Sample breakdown
from a real run: 244 weapon meshes (`Mesh_Weapon_*`), 1,058 architecture meshes
(`Mesh_Arch_*`), 205 large meshes (>5 MB), 1,876 small meshes (<10 KB). Full
run takes ~10–15 minutes.

### Gotchas (each one bit me on the test run)

1. **Unity version detection returns `0.0.0`** for TG's bundles. You **must**
   pass `--unity_version 6000.0.64f1` explicitly. Without it,
   `EndOfStreamException` fires on every Transform parse and 0 meshes export.

2. **Don't extract from a single bundle.** Meshes use cross-bundle PPtr
   references (the scene bundle holds GameObjects pointing into separate
   mesh bundles). The herohome scene bundle alone gave 0 meshes; pointing at
   the full `Fall of Avalon_Data\` resolves all references.

3. **Don't pipe the output.** Bash's `head` / `Select-Object -First` closes
   the pipe and AssetStudio gets `SIGPIPE`. Redirect to a file instead:
   `… --types Mesh > assets\meshes\_extraction.log 2>&1`.

4. **`--game Normal`** is required — TG isn't an encrypted Hoyoverse / Mihoyo
   game so `Normal` is the right value (the dropdown lists ~30 game-specific
   formats; pick `Normal` for any Mono Unity game without obfuscation).

5. **OBJ has no rigging.** Static props (weapons, environment, items) come
   through perfectly. Skinned meshes (characters, NPCs) extract as static
   T-pose geometry — bones, weights, blend shapes are lost. The CLI in
   v2.4.1 does **not** have an FBX flag despite shipping
   `AssetStudio.FBXWrapper.dll`. For rigged FBX of characters, use the GUI
   path below.

### GUI path: rigged FBX with skeleton + animations

When you need bones/animations (character mods that reskin or repose):

1. `& "C:\Tools\AssetStudio\v2.4.1\AssetStudio.GUI.exe"`
2. **File → Load folder** → point at `<game>\Fall of Avalon_Data` (whole folder,
   not a single bundle — same cross-reference reason).
3. **Options → Specify Unity version** → `6000.0.64f1` (same gotcha as the CLI).
4. *Asset List* tab → filter Type column = `Mesh`, or use *Scene Hierarchy*
   tab to find a specific character GameObject.
5. **Model → Export selected objects (split)** for FBX with rig + animations,
   or *Export → Selected assets* for raw mesh.

### TG-specific caveat: Awaken.Kandra (~3.7% miss rate)

Awaken Studio has a custom skinned-mesh DLL (`Awaken.Kandra.dll`, see
`refs/game/Awaken.Kandra/`). Real-run error breakdown:

- **490 `System.IO.EndOfStreamException`** — meshes whose binary layout
  AssetStudio's stock parser can't read. Strongly suspected to be
  Kandra-formatted skinned meshes. ~3.7% of attempts.
- **137 `Submesh topology is lines or points`** — not real meshes (debug
  lines, particle effects, trail renderers). Skipping is correct. ~1%.

Most character meshes (e.g. `Mesh_FrozenHuman_03_LOD0/1/2`) DID export, so the
mainline path works for static geometry. If a specific character comes out
empty in the output, it's the Kandra path — inspect `refs/game/Awaken.Kandra/`
for storage layout, and consider a custom extractor or the GUI's Model export
path which may handle it differently.

### Refreshing after a game patch

Same as AssetRipper — just re-run with a fresh output dir:

```pwsh
Remove-Item -Recurse -Force C:\Users\quilo\source\assets\meshes
mkdir C:\Users\quilo\source\assets\meshes
& "C:\Tools\AssetStudio\v2.4.1\AssetStudio.CLI.exe" `
    "C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\Fall of Avalon_Data" `
    "C:\Users\quilo\source\assets\meshes" `
    --game Normal --unity_version 6000.0.64f1 --types Mesh `
    > C:\Users\quilo\source\assets\meshes\_extraction.log 2>&1
```

## Git

`assets/` is gitignored (top-level entry, alongside `refs/`) so the
multi-GB export never enters a commit.
