# AssetRipper usage for Tainted Grail: Fall of Avalon

Companion to `*-ref/` decompiled DLLs. Where those give you the C#, AssetRipper
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
     the assets (you already have the full decompile in `*-ref/`, so "Disabled"
     is fine and faster).

4. **Export everything:** click *Export → Export all files* and pick:

   ```
   C:\Users\quilo\source\AssetRipped
   ```

   AssetRipper writes into `AssetRipped\ExportedProject\` (a Unity-project-like
   tree).  Expect 10–30 minutes and 2–6 GB.

5. **Close the browser tab** when the status pane shows *Export complete*.
   The exe will keep running until you `Ctrl-C` the PowerShell window or close
   it.

## Refreshing after a game patch

Steam updates `Fall of Avalon_Data`. To get fresh assets:

```powershell
# 1. Wipe the old export (optional but avoids stale files mixing in)
Remove-Item -Recurse -Force C:\Users\quilo\source\AssetRipped\*

# 2. Re-run the GUI flow above, exporting back to C:\Users\quilo\source\AssetRipped
& "C:\Tools\AssetRipper\1.3.14\AssetRipper.GUI.Free.exe"
```

If a future AssetRipper release (>1.3.14) adds a true headless CLI, replace
this section with the one-liner.

## Where things land

After a full export, the structure under `C:\Users\quilo\source\AssetRipped\`
is roughly:

```
AssetRipped\
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
reference the script GUIDs you can cross-link with the `*-ref` decompile.

## Quick "find inventory UI" pointers

From `C:\Users\quilo\source\`:

```powershell
# Inventory-related prefabs
Get-ChildItem -Recurse AssetRipped\ExportedProject\Assets\PrefabInstance `
  -Filter "*Inventory*.prefab"

# Crafting UI prefabs
Get-ChildItem -Recurse AssetRipped\ExportedProject\Assets\PrefabInstance `
  -Filter "*Crafting*.prefab"

# Anything filter-related (popups, category lists)
Get-ChildItem -Recurse AssetRipped\ExportedProject\Assets `
  -Filter "*Filter*" -File
```

Or, with this repo's tooling, use Grep against the YAML directly:

```
# Find prefab files that reference a class you patched, e.g. CraftingItemUI
grep -r "CraftingItemUI" AssetRipped/ExportedProject/Assets/PrefabInstance
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
grep -r "m_Script" AssetRipped/ExportedProject/Assets/MonoBehaviour | grep -i category
```

then open the matching `.asset` files to see field values.

## Git

`AssetRipped/` is gitignored (top-level entry, alongside `*-ref/`) so the
multi-GB export never enters a commit.
