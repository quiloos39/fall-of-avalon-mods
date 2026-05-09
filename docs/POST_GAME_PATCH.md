# After a Tainted Grail patch — recovery checklist

Steam updated `Fall of Avalon`. This is what to run, in order, to find out if our mods still work and to ship a new build if they do.

Total time when nothing's broken: ~5 minutes. When a symbol moved: depends on how much moved.

## 1. Refresh `refs/game/` (decompile the new DLLs)

Compare DLL mtimes against the last decompile so you only redo what actually changed.

```pwsh
$managed = "C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\Fall of Avalon_Data\Managed"
$refsRoot = "C:\Users\quilo\source\refs\game"

# Show which DLLs were updated by Steam vs our last decompile.
Get-ChildItem $managed *.dll | Where-Object {
    $name = $_.BaseName
    $refDir = Join-Path $refsRoot $name
    -not (Test-Path $refDir) -or ($_.LastWriteTime -gt (Get-Item $refDir).LastWriteTime)
} | Select-Object Name, LastWriteTime
```

For each name in the list above (and at minimum `TG.Main.dll` after every patch):

```pwsh
$name = "TG.Main"   # repeat for each updated DLL
Remove-Item -Recurse -Force "C:\Users\quilo\source\refs\game\$name" -ErrorAction SilentlyContinue
ilspycmd -p -o "C:\Users\quilo\source\refs\game\$name" "$managed\$name.dll"
```

Game-logic DLLs we track (from `CLAUDE.md`): `TG.Main`, `Awaken.Utility`, `Awaken.ECS`, `Awaken.Kandra`, `Awaken.Babel`, `Awaken.Orchestrating`, `Awaken.PackageUtilities`, `Awaken.Tests`, `Assembly-CSharp`, `Assembly-CSharp-firstpass`, `VendorWrappers`. Skip Unity engine and 3rd-party — they have public docs.

## 2. Run the symbol-anchor verifier

```pwsh
pwsh -File C:\Users\quilo\source\tools\verify-anchors.ps1
```

Expected when healthy: `22 OK / 0 WARN / 0 FAIL` (count drifts as mods evolve).

- **All OK / WARN** → patches still resolve. Skip to step 4.
- **Any FAIL** → at least one game type or method our mods reference was renamed, removed, or moved. Continue with step 3.

## 3. Triage the FAILs

The verifier prints `<mod> <type>.<member>: not found` along with `(file, line)` of the patch. For each:

1. Open the cited mod source line.
2. Look up the original `<type>.<member>` in the **old** `refs/game/` if you still have a backup, or in `git log -p -- refs/game/<DllName>` if you committed the trees (we don't, by default).
3. Search the **new** `refs/game/` for the renamed/relocated symbol:
   ```pwsh
   Grep -r "void NewMethodName\b" refs/game/
   ```
   For methods that may have been split or whose signatures changed, also grep for the parameter or return-type pattern.
4. Update the patch (`[HarmonyPatch(typeof(...), nameof(...))]`) and any `Krafs.Publicizer` references.
5. Rebuild that mod: `.\tools\dev-relaunch.ps1 -Mod <Name>` and exercise the feature in-game.
6. Re-run `tools\extract-anchors.ps1` so the JSON inventory matches the new code.
7. Re-run `verify-anchors.ps1` until it's all OK.

If a method is genuinely gone (not renamed), you may need to find a different hook — `docs/ARCHITECTURE.md` lists the common ones by domain.

## 4. Refresh AssetRipper extraction (optional — only if you're modding UI)

UI work depends on prefab structure, which can change with the game. For pure-logic mods (AutoLoot, SpoilsOfTheSlain), skip this step.

```pwsh
Remove-Item -Recurse -Force C:\Users\quilo\source\assets\* -ErrorAction SilentlyContinue
& "C:\Tools\AssetRipper\1.3.14\AssetRipper.GUI.Free.exe"
# Then GUI flow per docs/ASSETRIPPER_USAGE.md
```

Takes 10–30 minutes; expect 2–6 GB.

## 5. Smoke-test in-game

Launch and watch `<GameRoot>\BepInEx\LogOutput.log` for the `[VerifyPatches]` lines:

```
[Info   :BetterSortingCrafting] [VerifyPatches] com.user.bettersortingcrafting: 13 method(s) patched
[Info   :StationIndicator]      [VerifyPatches] com.user.stationindicator: 1 method(s) patched
[Info   :SpoilsOfTheSlain]      [VerifyPatches] com.user.spoilsoftheslain: 8 method(s) patched
```

Counts should match the previous build. A `0 method(s) patched` warning or a `PatchAll FAILED` error means a Harmony patch silently lost its target — go back to step 3.

Then exercise each mod's main feature manually (no automated tests possible — proprietary game DLLs).

## 6. Confirm Steam didn't undo our edits

Steam's "Verify integrity" (and sometimes major patches) restores `boot.config` and `mono-2.0-bdwgc.dll`. Check:

```pwsh
$boot = "C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\Fall of Avalon_Data\boot.config"
Select-String -Path $boot -Pattern "player-connection-debug|wait-for-managed-debugger"
```

If those lines are missing, re-apply per `docs/DEBUGGER_SETUP.md`. The `boot.config.original` and `mono-2.0-bdwgc.dll.original` backups still exist (one-time, never overwritten).

ScriptEngine config (`<GameRoot>\BepInEx\config\BepInEx.Debug.ScriptEngine.cfg`) lives outside the game data dir — Steam never touches it.

## 7. Cut releases

Once everything is green:

```pwsh
.\tools\release.ps1 -Mod <Name> -ModVersion <x.y.z> -GameVersion <newGameVersion>
```

Bump `-ModVersion` even if no mod code changed — it signals to users that a fresh build was made against the new game version. Repeat per released mod that uses any patched code path. See `docs/RELEASING.md` for the full flow including manual `git commit + tag + push`.

Then update memory if the patch surfaced any new gotchas (renamed types, new lifecycle quirks) so future sessions have the context.

## At-a-glance time budget

| Step | Time when clean | Time when broken |
|---|---|---|
| 1. Decompile | 1–2 min | same |
| 2. verify-anchors | <30 s | same |
| 3. Triage FAILs | n/a | 5–60 min per fix |
| 4. AssetRipper (UI mods only) | 10–30 min | same |
| 5. In-game smoke | 5 min | as long as needed |
| 6. Verify boot.config not reset | <30 s | 1 min |
| 7. Release each affected mod | 1 min × N | same |
