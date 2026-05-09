# Mod cookbook — Tainted Grail (Fall of Avalon)

How *our mods* are structured against the game. Companion to
`docs/ARCHITECTURE.md` (which describes the game). Paths are repo-relative
(root `C:\Users\quilo\source`); `file:line` cites point at concrete
precedents — copy-adapt rather than re-derive.

## Contents

1. [File layout](#1-file-layout-convention)
2. [Minimum-viable plugin](#2-minimum-viable-plugin-skeleton)
3. [Harmony patch idioms](#3-harmony-patch-idioms)
4. [Lifecycle & timing](#4-lifecycle-and-timing)
5. [UI injection](#5-ui-injection-the-big-pattern)
6. [Config](#6-config-bepinexconfiguration)
7. [Hotkey policy](#7-hotkey-policy)
8. [Defensive coding](#8-defensive-coding)
9. [Live-reload safety](#9-live-reload-safety)
10. [Logging](#10-logging)
11. [Anti-patterns from third-party mods](#11-anti-patterns-from-third-party-mods)

---

## 1. File layout convention

BetterSortingCrafting is the canonical heavyweight split (`mods/BetterSortingCrafting/`):

| File | Role |
|---|---|
| `Plugin.cs` | `[BepInPlugin]`, `Awake`/`OnDestroy`, `PatchAll`, `[VerifyPatches]` log line. Nothing else. |
| `<Area>Patches.cs` | All `[HarmonyPatch]` types for one game system. E.g. `CraftingSortPatches.cs`, `InventorySortPatch.cs`, `PlayerInputPatches.cs`. |
| `<Area>State.cs` | Static singletons holding live mod state shared across patches. E.g. `FilterState.cs`. |
| `<Area>Injector.cs` | UI-injection helpers — see `CraftingSearchInjector.cs:17`, `InventorySearchInjector.cs:21`. The Harmony patch becomes a 5-line postfix that calls `Injector.Inject(__instance)`. |
| `*MonoBehaviour.cs` | Self-contained Components attached at runtime (e.g. `SearchInputFocusBlocker.cs:42`, `SearchInputFocusBlocker.cs:18` for `ActivateInputOnClick`). |

**When to split:**

- Two `[HarmonyPatch]` blocks in one file → move them to `*Patches.cs`.
- Patch body grew past ~30 lines → split into `*Injector.cs` / `*Helper.cs`.
- Two patches share state → it belongs in `*State.cs`, not as `private static` duplicated in each.
- Spawning custom GameObjects/Components → own `MonoBehaviour` file.

Lighter mods (AutoLoot, StationIndicator) collapse state and patches into
1-2 files; `Plugin.cs` stays minimal. SpoilsOfTheSlain is the most file-split
(`Catalog.cs`, `RecipeFactory.cs`, `RecipeRegistry.cs`, `RecipeIssuer.cs`,
`RecipeInjector.cs`, `StartupBackfill.cs`, `KillUnlock.cs`, `DiscoveryPatches.cs`).

---

## 2. Minimum-viable plugin skeleton

```csharp
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin {
    public const string PluginGuid = "com.user.<modname>";
    internal static ManualLogSource Log;
    private Harmony _harmony;
    public void Awake() {
        Log = Logger;
        _harmony = new Harmony(PluginGuid);
        try { _harmony.PatchAll(typeof(Plugin).Assembly); VerifyPatches(); }
        catch (System.Exception e) { Log.LogError($"[VerifyPatches] {PluginGuid}: PatchAll FAILED: {e}"); }
    }
    public void OnDestroy() { try { _harmony?.UnpatchSelf(); } catch { } }
}
```

Full canonical version: `mods/BetterSortingCrafting/Plugin.cs:7-65`. The
`VerifyPatches()` body (`mods/BetterSortingCrafting/Plugin.cs:36-59`)
enumerates `_harmony.GetPatchedMethods()` and logs `N method(s) patched` or
`0 method(s) patched`. **Always include it** — silent patch failures (renamed
game methods, conflicting mods) only surface in this line. First thing to
grep `LogOutput.log` for when a mod looks inactive (CLAUDE.md:144).

GUID: `com.user.<modname>` (`mods/AutoLoot/Plugin.cs:17`,
`mods/SpoilsOfTheSlain/Plugin.cs:12`). `OnDestroy` MUST `UnpatchSelf()` —
ScriptEngine reload otherwise leaves stale patches alive (§9).

---

## 3. Harmony patch idioms

### Postfix with `__instance` / `__result`

Most common. Inject UI or mutate state on the way out. 5-line postfix
(`mods/BetterSortingCrafting/InventorySearchInjector.cs:286-297`).
`ref __result` mutates the return; `__args` for raw access (rare).

### Getter postfix — `MethodType.Getter`

```csharp
[HarmonyPatch(typeof(HandcraftingTemplate), nameof(HandcraftingTemplate.Recipes), MethodType.Getter)]
static void Postfix(ref IEnumerable<IRecipe> __result) {
    __result = (__result ?? Enumerable.Empty<IRecipe>()).Concat(ours);
}
```

(`mods/SpoilsOfTheSlain/RecipeInjector.cs:20-33`,
`mods/BetterSortingCrafting/CraftingSortPatches.cs:375`).

### Prefix with `return false` to skip the original

Swap behaviour entirely. The sort-cycle hijack:

```csharp
static bool Prefix(VCRecipeSorting __instance) {
    try { /* build popup, ContextPopupUI.CreatePopup(...) */ return false; }
    catch (Exception e) { Plugin.Log.LogError(...); return true; }
}
```

(`mods/BetterSortingCrafting/CraftingSortPatches.cs:22-50`,
`mods/BetterSortingCrafting/InventorySortPatch.cs:13-79`,
`mods/BetterSortingCrafting/PlayerInputPatches.cs:11-21` for the 1-line
input-gating prefix).

**Gotcha:** a thrown exception in a `void`-returning prefix is silently
interpreted as "skip original" — always wrap the body and return `true`
on error to fall back to vanilla.

### Per-class `harmony.CreateClassProcessor(t).Patch()`

Use over `PatchAll` when one bad target shouldn't sink the rest, or you want
explicit `ok`/`fail` counters:

```csharp
foreach (var t in typeof(Plugin).Assembly.GetTypes()) {
    if (!t.IsClass || t.GetCustomAttribute<HarmonyPatch>() == null) continue;
    try { _harmony.CreateClassProcessor(t).Patch(); ok++; }
    catch (System.Exception e) { fail++; Log.LogError(...); }
}
```

(`mods/SpoilsOfTheSlain/Plugin.cs:39-50`). Otherwise `PatchAll` is fine
(AutoLoot, BetterSortingCrafting, StationIndicator).

### String-typed game references — `AccessTools.TypeByName`

When a type isn't directly referenceable, resolve via `TargetMethod()`:

```csharp
[HarmonyPatch]
static class LocationSpec_GetAttachments_Patch {
    static MethodBase TargetMethod() {
        var t = AccessTools.TypeByName("Awaken.TG.Main.Locations.Setup.LocationSpec");
        return t == null ? null : AccessTools.Method(t, "GetAttachments");
    }
    static void Postfix(Component __instance, ref IEnumerable __result) { ... }
}
```

(`mods/StationIndicator/Scanner.cs:187-234`,
`mods/StationIndicator/Scanner.cs:69-79` for the static type cache).
**`tools/verify-anchors.ps1` recognises this only when `TypeByName(...)` and
`Method(t, "literal")` appear together** (`tools/extract-anchors.ps1:116, 236-256`)
— defeats the static check otherwise.

### `Krafs.Publicizer` — preferred over reflection caches

Two `.csproj` lines (every released mod):

```xml
<PackageReference Include="Krafs.Publicizer" Version="2.2.1" PrivateAssets="all" />
<Publicize Include="TG.Main" />
```

(`mods/BetterSortingCrafting/BetterSortingCrafting.csproj:22-28`,
`mods/AutoLoot/AutoLoot.csproj:22-28`). Trade-off: couples to internals
(rename = compile error, not runtime null). Upside: concise
`__instance.field = x` over `AccessTools.Field(...).SetValue(...)`.
Build catches post-game-patch breakage immediately.

---

## 4. Lifecycle and timing

`Plugin.Awake` fires *before* `ApplicationScene.Start` (ARCHITECTURE.md:39).
`World`, `Services`, `Hero.Current` all null at Awake. Strategies:

1. **Single-shot Update poll** — flag-guarded; runs the first frame
   `Hero.Current` is non-null, then never again
   (`mods/SpoilsOfTheSlain/Plugin.cs:88-101`,
   `mods/SpoilsOfTheSlain/StartupBackfill.cs:23-34`).
2. **Patch `Hero.OnInitialize` AND `Hero.OnRestore`** — separate paths for new
   game vs load (ARCHITECTURE.md:226-227, 252); patching only one silently
   misses half the cases. *No example yet in released mods.*
3. **Patch `Hero.OnFullyInitialized`** when "everything wired" matters more
   than "earliest possible" (ARCHITECTURE.md:254). *No example yet.*
4. **`SceneLifetimeEvents.Events.AfterWorldInitialized`** via `EventSystem`
   for one-shot global setup (ARCHITECTURE.md:243). *No example yet.*
5. **Per-frame null checks** — bail when null. Hotkey toggles only, not setup
   (`mods/AutoLoot/Plugin.cs:44`).

**Gotcha:** `World.AssignServices` is one-shot (ARCHITECTURE.md:269) — second
call throws. Don't register your own `IService`; use static singletons in
`*State.cs` (`mods/BetterSortingCrafting/FilterState.cs:13`).

---

## 5. UI injection (the big pattern)

End-to-end BetterSortingCrafting walk-through.

**5a. Postfix the panel.** `VItemsDefaultUI.OnMount` fires every panel open
(ARCHITECTURE.md:182, 253). 5-line postfix delegates to the injector
(`mods/BetterSortingCrafting/InventorySearchInjector.cs:286-297`,
`mods/BetterSortingCrafting/CraftingSearchInjector.cs:180-191`).

**5b. Why a separate `*Injector.cs`.** Patch stays small and reads as "what
fires when". Injector owns GameObject lifetime, idempotency tracking, font
lookup, prefab cloning, re-entry handling.
`Dictionary<VItemsDefaultUI, TMP_InputField>` for multi-view safety
(`mods/BetterSortingCrafting/InventorySearchInjector.cs:25-26`).

**5c. Idempotency + hot-reload child cleanup.** `OnMount` fires every open;
without de-dup you stack one bar per open. Two layers:

```csharp
private static readonly HashSet<VRecipeGridUI> _injected = new HashSet<VRecipeGridUI>();
public static void Inject(VRecipeGridUI view) {
    if (_injected.Contains(view)) return;
    ClearOldContainers(parent);   // hot-reload safety
    Build(view);
    _injected.Add(view);
}
```

(`mods/BetterSortingCrafting/CraftingSearchInjector.cs:21-41`,
`mods/BetterSortingCrafting/InventorySearchInjector.cs:33-53, 242-250`).
`ClearOldContainers` walks children and `DestroyImmediate`s anything named
`BetterSorting_*` — survives ScriptEngine reload, where old GameObjects
outlive old static maps.

**5d. The `OverrideFilter` reentrancy guard.** The crucial gotcha.
`ItemsListUI.Refresh` clears `_filterOverride` every call (ARCHITECTURE.md:182).
Filters need a `Refresh` postfix that re-applies — but `OverrideFilter`+`Refresh`
recurses. Fix:

```csharp
private static bool _isReapplying;
public static void ReapplyAfterRefresh(ItemsListUI list) {
    if (_isReapplying) return;
    try {
        _isReapplying = true;
        list.OverrideFilter(BuildSearchTab(text));
        list.Refresh();
    } finally { _isReapplying = false; }
}
```

(`mods/BetterSortingCrafting/InventorySearchInjector.cs:30, 176-197`).
Better-UI uses the same flag with the same name
(`refs/mods/Better-UI/Better_UI.Patches/ItemSearchFilterPatch.cs:79-99`) —
convergent design.

**5e. Prefab references.** Find a sibling and copy its attributes.
`VCRecipeSorting.OnAttach` postfix mirrors `sortPrompt`'s anchor +
`ContentSizeFitter` to align a new filter prompt
(`mods/BetterSortingCrafting/CraftingSortPatches.cs:161-196`).
StationIndicator clones a `MarkerAttachment.markerDataWrapper` from an
already-spawned stash spec (`mods/StationIndicator/Scanner.cs:81-107`) —
**the stash must spawn first or injection no-ops silently**. Plan for the
"no prototype yet" case.

For new clickable widgets, use the game's `ARButton` so input dispatch
recognises the hit (ARCHITECTURE.md:178,
`mods/BetterSortingCrafting/InventorySearchInjector.cs:76`). Plain `Button`
won't trigger hover/select.

**5f. Search input + keybind suppression.** Typing letters in TMP would also
fire game keybinds (R/F/etc.). Three pieces:

- `MonoBehaviour` watching `TMP_InputField.isFocused`, edge-toggling
  `RewiredHelper.BlockKeyboardInput()`/`EnableKeyboardInput()`
  (`mods/BetterSortingCrafting/SearchInputFocusBlocker.cs:42-103`).
- Static `_focusedCount` aggregate; multiple bars all inc/dec it
  (`mods/BetterSortingCrafting/SearchInputFocusBlocker.cs:48-49`).
- `PlayerInput.HandleKeyboard`/`HandleRegisteredPlayerInputs` prefix with
  `return !SearchInputFocusBlocker.AnyFocused`
  (`mods/BetterSortingCrafting/PlayerInputPatches.cs:11-21`).

BetterSortingCrafting and Better-UI converge on this technique
(`refs/mods/Better-UI/Better_UI.Patches/ItemSearchFilterPatch.cs:54-70`).

---

## 6. Config (`BepInEx.Configuration`)

Pattern: dedicated `<Mod>Config` class wrapping `ConfigFile`, constructed in
`Plugin.Awake`:

```csharp
Cfg = new SpoilsOfTheSlainConfig(Config);
```

(`mods/SpoilsOfTheSlain/Plugin.cs:27`, `mods/AutoLoot/Plugin.cs:32`,
`mods/StationIndicator/Plugin.cs:26`). One entry per shape:

```csharp
ToggleHotkey = cfg.Bind("1. General", "ToggleHotkey", KeyCode.F6,
    "Hotkey to toggle auto-collect on/off in-game.");

ScanInterval = cfg.Bind("2. Scanning", "ScanInterval", 0.5f,
    new ConfigDescription("How often (in seconds) to scan...",
        new AcceptableValueRange<float>(0.1f, 5f)));

Enabled = cfg.Bind("1. General", "Enabled", true, "Master switch.");

TraceTypes = cfg.Bind("2. Tracing", "TraceTypes", "",
    "Comma-separated regexes...");
```

(`mods/AutoLoot/AutoLootConfig.cs:47-67`,
`mods/StationIndicator/StationIndicatorConfig.cs:21-33`,
`dev/HarmonyTracer/TracerConfig.cs:26-38`).

Sections use a `1. General` / `2. Scanning` numeric prefix to keep the
generated `*.cfg` ordered (`mods/AutoLoot/AutoLootConfig.cs:39-135`).

**Gotcha:** runtime config edits work for trivial bools (`Cfg.Enabled.Value`
read every frame), but settings governing *what gets registered* (e.g.
SpoilsOfTheSlain's `InjectIntoForge`) — the patch already ran. **Defer the
choice to the use site, not `OnSettingChanged`.** `RecipeInjector` reads
`Plugin.Cfg.InjectIntoForge.Value` inside the postfix
(`mods/SpoilsOfTheSlain/RecipeInjector.cs:25`), so toggling at runtime
works without re-patching.

---

## 7. Hotkey policy

**F6 is owned by AutoLoot** (`mods/AutoLoot/AutoLootConfig.cs:48`,
CLAUDE.md:145). Don't bind F6 in any new mod. Before picking a default,
grep `<GameRoot>\BepInEx\config\*.cfg` for `= F6` to spot collisions.
ScriptEngine's `ReloadKey` is on F11 (CLAUDE.md:100).

| Type | Use case | Trade-off |
|---|---|---|
| `ConfigEntry<KeyCode>` (Unity) | Single key, no modifiers | Simple. `mods/AutoLoot/AutoLootConfig.cs:10`, `mods/StationIndicator/StationIndicatorConfig.cs:31`. |
| `ConfigEntry<KeyboardShortcut>` (BepInEx) | Modifier+key combos like `Ctrl+Shift+L` | Flexible; needs `using BepInEx.Configuration;`. *No example yet.* |

**Prefer `KeyboardShortcut` for new code** — modifier support sidesteps the
F6-collision space. Read with `Input.GetKeyDown(Cfg.ToggleHotkey.Value)` from
`Plugin.Update` (`mods/AutoLoot/Plugin.cs:46`,
`mods/StationIndicator/Plugin.cs:84-91`).

---

## 8. Defensive coding

1. **`Awake` body wrapped in try/catch** + `Log.LogError` — one bad patch type
   shouldn't kill the plugin (`mods/BetterSortingCrafting/Plugin.cs:22-32`,
   `mods/StationIndicator/Plugin.cs:40-49`).
2. **Per-patch try/catch** when installing — HarmonyTracer pattern, bad ones
   logged + skipped (`dev/HarmonyTracer/Tracer.cs:136-148`,
   `mods/SpoilsOfTheSlain/Plugin.cs:42-46`).
3. **Per-hook try/catch.** A throwing prefix is silently destructive —
   Harmony reads the exception in a `void` prefix as "don't run original" and
   the game breaks unobservably (`dev/HarmonyTracer/Tracer.cs:232-238`).
   Always `try { ... return false; } catch { return true; }` for prefixes
   (`mods/BetterSortingCrafting/CraftingSortPatches.cs:25-50`,
   `mods/BetterSortingCrafting/InventorySortPatch.cs:24-79`).
4. **`?.` everywhere** — game state nulls at unexpected times
   (`mods/SpoilsOfTheSlain/StartupBackfill.cs:27-32`,
   `mods/StationIndicator/Scanner.cs:103-104`).
5. **Never call into game systems from a static initialiser.** Static ctors
   of Harmony patch classes run during `PatchAll` → `Plugin.Awake` → before
   `World.AssignServices` (ARCHITECTURE.md:39).
6. **Skip generics, abstracts, bodyless methods when patching by reflection**
   — Harmony can't wrap them (`dev/HarmonyTracer/Tracer.cs:160-181`).
7. **`SafeLog` wrapper** for hot-reload paths bypassing the BepInEx plugin
   lifecycle — `Logger` is unbound; falls back to `UnityEngine.Debug.Log`
   so catch handlers don't throw secondary NREs
   (`mods/BetterSortingCrafting/Plugin.cs:70-78`).

---

## 9. Live-reload safety

ScriptEngine reloads by destroying the old `Plugin` and creating a new one
(CLAUDE.md:98-106, `docs/LIVE_RELOAD.md`).

**Works:**

- Pure Harmony patches with `_harmony?.UnpatchSelf()` in `OnDestroy` (every
  released mod). New assembly re-patches with a fresh `Harmony` instance.
- Static fields in `*State.cs` — gone with the old assembly, reborn empty
  in the new. Often what you want.

**Does NOT work:**

- `World.Services.Register(...)` — `AssignServices` is one-shot
  (ARCHITECTURE.md:269); second call throws.
- Persistent GameObjects without cleanup — old `BetterSorting_InventorySearch`
  outlives the old assembly → duplicates. Mitigation: name with predictable
  prefix and `DestroyImmediate` matching children on init
  (`mods/BetterSortingCrafting/InventorySearchInjector.cs:242-250`).
- BepInPlugin GUID/version changes — BepInEx caches metadata; needs relaunch.

**Convention: write `OnDestroy` as the inverse of `Awake`.** AutoLoot also
destroys its `MonoBehaviour` (`mods/AutoLoot/Plugin.cs:60`); StationIndicator
relies on the host gameObject's implicit destruction. When in doubt, mirror.

---

## 10. Logging

| Level | Use | Example |
|---|---|---|
| `LogInfo` | One-shot init facts. `<mod> v0.1.0 loading...`, `[VerifyPatches]` count line, `Backfill complete` summary. | `mods/SpoilsOfTheSlain/StartupBackfill.cs:71-75` |
| `LogDebug` | Per-call traces, the per-method list under `[VerifyPatches]`. Off in default BepInEx config. | `mods/BetterSortingCrafting/Plugin.cs:47` |
| `LogWarning` | Unexpected-but-not-fatal — wrapper not yet cached, `0 method(s) patched`, missing prefab. | `mods/BetterSortingCrafting/Plugin.cs:51`, `mods/StationIndicator/Scanner.cs:152` |
| `LogError` | Things broke. Always with `e.GetBaseException().Message` or full `e`. | `mods/BetterSortingCrafting/CraftingSortPatches.cs:46` |

`[VerifyPatches] <guid>: N method(s) patched` is **the single most useful
diagnostic** — `0 patched` / `PatchAll FAILED` jumps out grepping
`LogOutput.log`. Tag log lines with a bracket-prefix matching the mod
(`[BetterSorting]`, `[StationIndicator]`, `[AutoLoot]`) for multi-mod
greppability (`mods/BetterSortingCrafting/CraftingSortPatches.cs:46`,
`mods/StationIndicator/Scanner.cs:106`).

---

## 11. Anti-patterns from third-party mods

Patterns observed in `refs/mods/` we **don't** use:

1. **Heavy reflection caching of game internals.** TPCO resolves three
   properties + a heuristic-named field at static-init via `AccessTools.*`
   (`refs/mods/TPCO/TPCO.Patches/DodgePatches.cs:14-20`). A renamed field
   becomes a runtime null with no compile error. Our approach: `Krafs.Publicizer`
   makes the same fields directly addressable; renames break the build
   immediately.

2. **Per-mod re-implementation of UI injection helpers.** Better-UI ships its
   own `SearchInputFocusBlocker` + `_isReapplyingFilter` + per-view
   dictionary triple
   (`refs/mods/Better-UI/Better_UI.Patches/ItemSearchFilterPatch.cs:178-188`).
   So does BetterSortingCrafting (`InventorySearchInjector.cs:25-30`). They
   mutually clobber each other if both are installed (each thinks it owns
   `OverrideFilter`). Two mods on the same `OverrideFilter` pipeline = race;
   compose better by targeting *different* injection points.

3. **`MyPluginInfo.cs` + `PluginConsts.cs` proliferation.** ImprovedInventory
   maintains separate `MyPluginInfo`, `PluginConsts`, and per-feature
   `Patch()`/`Unpatch()` static methods called from `Awake`
   (`refs/mods/ImprovedInventory/ImprovedInventory/Plugin.cs:49-79`). Fine
   for a complex multi-feature mod, overkill for ours — we keep
   `PluginGuid`/`Name`/`Version` as `const` on `Plugin` directly.

4. **No `[VerifyPatches]` log line.** None of the third-party mods surface
   patch counts; the only feedback when one fails is "feature X doesn't work".
   Always log the count (CLAUDE.md:144).
