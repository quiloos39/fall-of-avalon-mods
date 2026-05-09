# Symbol-anchor diff tools

Catch a game patch breaking our mods *before* a user does.

The mods in this repo (`BetterSortingCrafting`, `SpoilsOfTheSlain`,
`StationIndicator`, ...) hook game-side types and methods via Harmony. Renames
or removals on the game side silently break those hooks - the mod just stops
working at runtime, sometimes only on a specific code path. The two scripts
here turn that runtime failure into a checkable contract.

- [`extract-anchors.ps1`](extract-anchors.ps1) - scrapes every mod project for
  the game-side symbols we patch and writes them to
  [`symbol-anchors.json`](symbol-anchors.json) (committed; diffable).
- [`verify-anchors.ps1`](verify-anchors.ps1) - reads `symbol-anchors.json` and
  confirms every type / member still exists in the decompiled game source
  (`refs/game/`).

`symbol-anchors.json` is the **inventory** - a sorted list of `(mod, file,
line, type, member, kind)` tuples extracted from the source. After a game
patch, regenerate the `refs/game/` trees from the new game DLLs and re-run the
verifier. Anything that disappears or got renamed shows up as a FAIL.

## Recognised patch patterns

`extract-anchors.ps1` is a regex parser, not Roslyn. It recognises:

| Pattern | What gets recorded |
| --- | --- |
| `[HarmonyPatch(typeof(X), nameof(X.Y))]` | type=X, member=Y, kind=method |
| `[HarmonyPatch(typeof(X), "Y")]` | type=X, member=Y, kind=method |
| `[HarmonyPatch(typeof(X), nameof(X.Y), MethodType.Getter)]` | same as above; getter/setter modifier ignored - the underlying property name is what we verify |
| `[HarmonyPatch(typeof(X))]` | type=X, kind=type (whole-class patch) |
| `[HarmonyPatch("Awaken.TG.Foo", "Method")]` | string-namespace style |
| `AccessTools.Method(typeof(X), "Y")` / `nameof(...)` | type=X, member=Y, kind=method |
| `AccessTools.Field(typeof(X), "Y")` | kind=field |
| `AccessTools.PropertyGetter(typeof(X), "Y")` / `PropertySetter(...)` | kind=method |
| `AccessTools.Method(t, "Y")` paired with `AccessTools.TypeByName("Awaken.TG.X")` earlier in the file | type resolved from the string name |

A bare `[HarmonyPatch]` (used with `TargetMethod()`) is intentionally ignored -
it resolves at runtime and there's nothing static to verify. All other lines
that look like `[HarmonyPatch...` but don't match a known shape are surfaced
in the `_todo` array of `symbol-anchors.json` so they can be triaged.

## Usage

### Refresh the anchor inventory

Run after editing any mod's patches:

```pwsh
pwsh -File tools\extract-anchors.ps1
```

Output: `tools\symbol-anchors.json` (overwritten). Diff it - that's the change
to the game-side surface area your mods depend on. Commit the JSON.

### Verify against current game source

```pwsh
# pretty colour report
pwsh -File tools\verify-anchors.ps1

# include OK lines (otherwise only WARN/FAIL print)
pwsh -File tools\verify-anchors.ps1 -Verbose

# machine-readable
pwsh -File tools\verify-anchors.ps1 -Json
```

Exit code: `0` if every anchor is OK or WARN, `1` if any FAIL. Suitable for
CI / pre-release checks.

Status meanings:

- **OK** - the type and member were both found.
- **WARN** - the verifier was unsure but didn't conclude broken. Common
  reasons:
  - short type name resolves to multiple `refs/game/` files (consider switching
    the patch to a fully-qualified `typeof(Awaken.TG.Foo.Bar)`)
  - the member name has multiple declarations (overloads or partial classes)
- **FAIL** - the type or member could not be found in any `refs/game/` tree.
  Either the game patch removed/renamed it, or the decompiled trees are out of
  date and need re-running.

## Recommended workflow after a Tainted Grail patch

1. Update the game via Steam.

2. Re-decompile the game DLLs into `refs/game/` (these folders are gitignored).
   Repeat the line below for each DLL listed in `CLAUDE.md` (`TG.Main.dll`,
   `Awaken.Utility.dll`, etc.):

    ```pwsh
    $managed = "C:\Program Files (x86)\Steam\steamapps\common\Tainted Grail FoA\Fall of Avalon_Data\Managed"
    Remove-Item -Recurse -Force "refs/game/TG.Main" -ErrorAction SilentlyContinue
    ilspycmd -p -o refs/game/TG.Main "$managed\TG.Main.dll"
    ```

3. Run the verifier:

    ```pwsh
    pwsh -File tools\verify-anchors.ps1
    ```

4. If anything FAILs: open the anchor file (`mod`, `file`, `line` columns
   point you straight at the patch), look up the new name/shape in the
   refreshed `refs/game/` tree, and patch the mod accordingly. After fixing, run
   `extract-anchors.ps1` again so the JSON inventory matches the new code.

## Why a regex parser, not Roslyn?

Roslyn would catch slightly more (e.g. multi-line attributes spread over
several `[`...`]`), but it's heavyweight to set up for a 200-line PS script,
and the patch attributes in this repo all fit on a single line. Anything the
regex pass can't recognise is surfaced as `_todo` in `symbol-anchors.json`,
so we don't silently lose anchors.
