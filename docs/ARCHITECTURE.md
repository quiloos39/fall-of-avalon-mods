# Tainted Grail (Fall of Avalon) — Architecture cheat sheet for modders

Code-anchored summary of how `TG.Main.dll` and friends fit together. Cite paths
are inside `refs/` decompilations; everything is `Awaken.TG.*` unless noted.
Read with `CLAUDE.md` — this file does not duplicate paths/build info.

Glossary: M = `Awaken.TG.Main.`, MVC = `Awaken.TG.MVC.`. Symbols are written
`Namespace.Type` so they grep cleanly.

---

## 1. Bootstrap & lifecycle

The game is **not** Unity-driven via `[RuntimeInitializeOnLoadMethod]`. Init is
pinned to the `ApplicationScene` GameObject via Unity's normal scene order.

Entry point: `M.Scenes.SceneConstructors.ApplicationScene` (`[DefaultExecutionOrder(0)]`).
- `Start()` → `InitAll()` (a `UniTaskVoid`) drives everything in order:
  1. `SetupJobWorkers()` — caps `JobsUtility.JobWorkerCount` at 8.
  2. `InitServicesCrucialForCloudConflict()` — calls `MVC.World.AssignServices(new MVC.Services())` (one-shot, throws if called twice). Registers `MVC.UnityUpdateProvider` and `M.SocialServices.SocialService` (Steam/GoG-aware).
  3. `InitLocalization()` — wires `M.Localization.ILocalizationManager.Current = new Babel.BabelManager()`.
  4. `InitCloud()` — Steam/GoG cloud sync, may show conflict UI (`M.Saving.Cloud.Conflicts.CloudConflictUI`).
  5. `InitializeServices()` — registers ~40 services: `M.Templates.TemplatesProvider`, `M.Assets.Modding.ModService`, `MVC.Domains.SceneService`, `M.Saving.LargeFiles.LargeFilesStorage`, `M.General.Configs.GameConstants`, `CommonReferences`, `M.Heroes.Stats.Tweaks.TweakSystem`, `MVC.IdStorage`, `M.Fights.Factions.FactionRegionsService`, `M.Heroes.Items.DroppedItemSpawner`, `M.AI.Barks.BarkSystem`, `M.Wyrdnessing.WyrdnessService`, `M.AI.Combat.CombatDirector`, etc.
  6. `uiInitializer.InitAfterServices()` — `M.UI.UIInitializer`.
  7. `InitializeWorld()` — `World.Add` for `M.Settings.SettingsMaster`, `M.Cameras.CameraStack.CameraStateStack`, `M.Cameras.GameCamera`, `MVC.UI.Handlers.States.UIStateStack`, `MVC.UI.GameUI`, `M.ActionLogs.ActionLog`. Then `M.Saving.DomainUtils.LoadAppDomains()`.
  8. `InitializeWorldBasedServices()` — `M.UI.Cursors.Cursor`, `M.UI.GlobalKeys` (added as `AlwaysPresentHandlers` element of `GameUI`), `M.Timing.RecurringActions`, `M.Timing.TimeQueue`, `Auto*Service`s, `M.Debugging.AI.DebugAI`, `Get<AudioCore>().Initialize()`.
  9. Sets `s_initCompleted = true` — `OnGUI`, `LateUpdate`, etc. are gated on this.

After step 5: every `World.Services.Get<T>()` for the registered services is non-null.
After step 7: `World.Only<MVC.UI.GameUI>()` is the singleton UI host; `MVC.UI.Handlers.States.UIStateStack.Instance` is non-null. `Hero` does **not** exist yet.

**Hero & gameplay creation:** `M.Scenes.SceneConstructors.GameplayConstructor.CreateGameplay()`
(or `RestoreGameplay()` for loads). Called from `MapScene.cs` / `M.UI.TitleScreen.Loading.LoadingTypes.NewGameLoading` / `FullLoading`. Order:
- `BaseInit()`: registers gameplay services (`PrefabPool`, `GameplayMemory`, `AutoSaving`, `CrimeService`, `FactionService.Init()`, `MapService`, `NpcRegistry`, `SessionStatsService`, etc.), then `World.Add(new HUD())`, `World.Add(new TutorialMaster())`.
- Adds `World.Add(new GlobalTime())`, `new GameRealTime()`, `new DeferredSystem()`, `new PlayerJournal()`.
- `Hero.Create(HeroTemplate)` → `World.Add(new Hero(template))`. **`Hero.Current` is set inside `Hero.OnInitialize` and `Hero.OnRestore`** (Hero.cs:840, 878). After this returns, `Hero.Current` and its main view `M.Heroes.Combat.VHeroController` are valid.
- `LateInit()` — `NpcGrid`, `AchievementTrackingService`, `AutoAchievementsService.SpawnMissing`.

Mods loaded by BepInEx run very early — `Plugin.Awake` fires before `ApplicationScene.Start`. Anything touching `World.Services`, `Hero.Current`, or game models must defer (Harmony patch on `OnInitialize`/`OnFullyInitialized`, or listen to `M.Scenes.SceneLifetimeEvents.Events.AfterWorldInitialized`).

`s_initCompleted` reset for editor is `ApplicationScene.EDITOR_RuntimeReset()`. `World.EDITOR_RuntimeReset()` exists for the same reason but is only called in editor.

---

## 2. Core patterns

### Model / Element / View triad

**Live in `MVC/`. The patterns are unique to Awaken — recognise them on sight.**

- `MVC.Model` (abstract, `MVC/Model.cs`): every gameplay object inherits from this. Has `ID`, `CurrentDomain`, lifecycle hooks (`OnInitialize` / `OnFullyInitialized` / `OnDiscard` / `OnFullyDiscarded` / `OnRestore` / `OnAfterDeserialize`), and a static `Events` class with `BeforeFullyInitialized`, `AfterFullyInitialized`, `AfterChanged`, `BeforeDiscarded`, `BeingDiscarded`, `AfterDiscarded`. `[Saved]` (`Awaken.TG.Utility.Attributes.SavedAttribute`, `Awaken.Utility-ref/`) marks fields/properties for the binary serialiser.
- `MVC.Elements.Element` (abstract, `MVC/Elements/Element.cs`) and generic `Element<TParent>`: a `Model` whose ID is `parent.ID + ":" + element-id`. Elements are owned by their parent and discarded when the parent is. Add via `parent.AddElement<T>()` / `parent.AddElement(new T())`. `IElement<TParent>` exposes `ParentModel`. `Hero` exposes "elements" like `HeroItems`, `HealthElement`, `HeroStats`, `HeroWyrdNight`, `TimeDependent`, etc.
- `MVC.View` (abstract, MonoBehaviour, `MVC/View.cs`): GameObject-attached visual for a `Model`. Auto-spawned via `[SpawnsView(typeof(VFoo), isMainView: true)]` on the model class — see `MVC.Attributes.SpawnsView` and `World.SpawnViews`. `[NoPrefab]` skips the Resources lookup (component on a fresh GO). `[UsesPrefab("name")]` overrides the default prefab name. Default prefab path `Resources/Prefabs/MapViews/{TypeName}` (or override; `World.PrefabPath = "Prefabs/MapViews"`).
- `MVC.Presenter<TModel>` (`MVC/Presenter.cs`): for UI-toolkit (`UnityEngine.UIElements`) views — separate path from MonoBehaviour `View`. Bound via `World.BindPresenter`.
- `MVC.ViewComponent`: subviews/widgets — UI fragments inside a view. Examples: `M.Heroes.CharacterSheet.Items.Panel.List.VCItemSorting` (the sort prompt mounted on the bag UI).

**Naming convention:** `Foo` model, `VFoo` view, `VCFoo` view component, `PFoo` presenter, `RFoo` reusable. So when you see `VCRecipeSorting`, the model graph is `RecipeGridUI` → contains a `VCRecipeSorting` UI fragment.

### World — registry, queries, events

`MVC.World` is a static class (in `MVC/World.cs`). All models live there.
- `World.Add(model)` → registers, runs `OnInitialize`, spawns views, fires `BeforeFullyInitialized` / `AfterFullyInitialized`. `World.Restore(model)` is the deserialisation path.
- Lookups: `World.Only<T>()` (asserts exactly one), `World.Any<T>()`, `World.All<T>()` (returns `MVC.Elements.ModelsSet<T>` — struct-iterable, no GC). `World.ByID(string)` for direct lookup.
- Per-type events: `World.Events.ModelAdded<T>()`, `ModelInitialized<T>()`, `ModelFullyInitialized<T>()`, `ModelDiscarded<T>()`, plus `*AnyType` variants. These are the cleanest hook for "react when a model X spawns".
- Singletons by convention: many systems expose a static `.Current` (e.g. `Hero.Current`, `UIStateStack.Instance`). Prefer `World.Only<T>()` when unsure — it's the single source of truth.

### Services — DI for non-`Model` systems

`MVC.Services` (`MVC/Services.cs`) is a typed dictionary by `RuntimeTypeHandle`. Things implementing `IService` register here.
- `World.Services.Register(new T())` adds; throws if duplicate.
- `World.Services.Get<T>()` resolves (throws), `TryGet<T>(out)` non-throwing.
- `WaitFor<T>()` is `UniTask`-based polling — useful for mods loading early.
- `IDomainBoundService` (lifecycle = a `Domain`) and `M.Saving.SerializedService` are the two layered specialisations. Domain-bound services are dropped on `World.DropDomain`.

### Domains — scope/lifetime tags

`MVC.Domains.Domain` (struct). Static instances: `Domain.Main` (modal), `.Globals`, `.TitleScreen`, `.SaveSlot` (modal), `.Gameplay`, `.MetaData`. Each `Model.DefaultDomain` declares which scope it belongs to. `World.DropDomain(domain)` discards every model whose `CurrentDomain.IsChildOf(target)` — used when leaving a save slot or unloading a scene. **Modal** domains (`Main`, `SaveSlot`) gate input.

`MVC.Domains.SceneService` exposes `MainDomain`, `MainSceneRef`, `AdditiveSceneRef`, `ActiveDomain`. `Domain.Scene(sceneRef)` and `Domain.CurrentScene()` are convenience factories.

### Event bus

`MVC.Events.EventSystem` (registered as a service in `World.AssignServices`). Event objects are statics on the publisher class (`Hero.Events.LevelUp`, `Item.Events.Equipped`, `M.Timing.GameRealTime.Events.GameTimeChanged`, `MVC.UI.Handlers.States.UIStateStack.Events.UIStateChanged`, `M.Scenes.SceneLifetimeEvents.Events.*`). `Event<TSource, TPayload>` is the type.

API surface (extension methods in `MVC.ModelExtensions`):
- `model.ListenTo(SomeClass.Events.Foo, payload => ..., owner)` — owner = `IListenerOwner` whose discard auto-unsubscribes.
- `World.EventSystem.ListenTo(targetPattern, evt, owner, callback)` for explicit wildcard. `EventSystem.PatternForModel(model)` = `model.ID`. Wildcard `"*"` matches any source.
- `ModelExtensions.Trigger(source, evt, payload)` to publish.
- `LimitedListenTo` (charges), `ModalListenTo` (survives discard), `LimitedEventListener`.

`HookableEvent<TModel, TValue>` (`MVC/Events/HookableEvent.cs`) is `Event<TModel, HookResult<TModel, TValue>>` — gives subscribers a chance to mutate or veto the payload (used for damage hooks, etc.).

**Lifetime story:** if you supply an `IListenerOwner` (any Model, View, Presenter, Service implements it), the listener auto-removes when the owner discards. Without an owner, the listener leaks unless you call `RemoveAllListenersOwnedBy` or the listener returns `ShouldBeDisposed`.

### Save/load

Custom binary serialiser, **not** Newtonsoft (Newtonsoft is only for cloud/save-slot metadata).

- Mark `[Saved]` on a field/property — `Awaken.TG.Utility.Attributes.SavedAttribute` (in `Awaken.Utility-ref/`). Constructors take a default value. Inherited.
- `MVC.Serialization.SaveWriter` / `SaveReader` are the binary streams. Models override `Serialize(SaveWriter)` / `Deserialize(in ushort name, SaveReader)`. Names are `ushort` IDs from `M.Saving.JsonTypeName`.
- Each Model has `TypeForSerialization` (a `ushort`) — registry in `MVC.Serialization.ModelTyper`.
- Drivers: `M.Saving.SaveSystem.Serialize(allModels, domain, stream)`, `M.Saving.LoadSystem`, top-level `M.Saving.LoadSave`. Save gates per-domain. `M.Saving.AutoSaving` is a service.
- `IsNotSaved` virtual on `Model` excludes from saves. `MarkedNotSaved` is a runtime override.

### ECS

`Awaken.ECS-ref/` (Unity DOTS). Used in **isolated** rendering / mass-simulation systems only (~32 imports across `TG.Main`):
- `Awaken.ECS.DrakeRenderer.*` — instanced rendering (mesh/material variants)
- `Awaken.ECS.MedusaRenderer`, `LeshyRenderer` — terrain/foliage
- `Awaken.ECS.Critters`, `Awaken.ECS.Flocks` — ambient creatures
- `Awaken.ECS.Mipmaps.Systems` — texture LOD

Game logic, NPCs, items, and stats are pure Models. **You do not need ECS knowledge to mod gameplay.**

---

## 3. Domain map

| Domain | Namespace | Key types |
|---|---|---|
| Hero / character | `M.Heroes` | `Hero` (with `Hero.Current` static), `HeroTemplate`, `PlayerInput` (`Element<GameUI>`) |
| Hero stats | `M.Heroes.Stats`, `M.Heroes.Stats.Tweaks` | `HeroStats`, `CharacterStats`, `Stat`, `Tweak`, `TweakSystem`, `TweakSelector` |
| NPCs | `M.Fights.NPCs` | `NpcElement` (Element of `Location`!), `AliveStats`, `HealthElement`, `Corpse` |
| Items (model) | `M.Heroes.Items` | `Item`, `ItemTemplate` (Template scriptable), `HeroItems` (inventory element of Hero), `ItemStats`, `ItemEquip`, `EquipmentSlotType`, `ItemSlot` |
| Item attachments | `M.Heroes.Items.Attachments` | `ItemAudio`, `ItemSkillsInvoker`, `ItemEffects` (all `Element<Item>`) |
| Item tooltips | `M.Heroes.Items.Tooltips` | `ItemTooltipUI`, `VCItemTooltipUI`, `VCItemBaseTooltipUI` |
| Inventory UI | `M.Heroes.CharacterSheet.Items.Panel`, `.Panel.List`, `.Panel.Tabs`, `.Bag` | `ItemsUI`, `VItemsDefaultUI`, `ItemsListUI`, `VBaseItemsListUI`, `ItemsListElementUI`, `ItemsTabType`, `ItemsSorting`, `VCItemSorting`, `BagUI` |
| Crafting | `M.Crafting`, `M.Crafting.HandCrafting`, `.AlchemyCrafting`, `.Cooking`, `.Fireplace`, `.Recipes`, `.HandCrafting.RecipeView` | `Handcrafting`, `HandcraftingRecipe`, `HandcraftingTemplate`, `RecipeCrafting`, `RecipeGridUI`, `VCRecipeSorting` |
| Recipes | `M.Heroes` (HeroRecipes) + `M.Crafting.Recipes` | `HeroRecipes` (Element of Hero, holds learned set), `BaseRecipe`, `IRecipe` |
| Storage / stash | `M.Heroes.Storage` | `HeroStorage`, `HeroStorageUI`, `HeroStorageTabUI`, `HeroStorageAttachment` |
| Containers | `M.Locations.Containers` | `ContainerElement`, `ContainerInventory` (also patched by AutoLoot) |
| Vendors / shops | `M.Locations.Shops`, `.UI` | `Shop` (Element), `ShopAttachment`, `ShopTemplate`, `IMerchant`, `ShopUI` |
| Locations | `M.Locations`, `.Attachments`, `.Attachments.Elements`, `.Pickables` | `Location` (sealed `Model`), `LocationCreator`, `LocationSpec`, `Pickable`, `SearchAction` |
| Quests / objectives | `M.Stories.Quests`, `.Objectives`, `.Templates`, `.UI` | `Quest`, `Objective`, `QuestTracker`, `QuestTemplate`, `QuestState`, `QuestLogUI` |
| Stories / dialog | `M.Stories`, `.Choices`, `.Steps`, `.Runtime`, `.Conditions` | `Story` (`IUIStateSource`), `StoryConfig`, `StoryBookmark` |
| Time / day-night | `M.Timing`, `.ARTime` | `GlobalTime`, `GameRealTime`, `TimeDependent` (Element pattern), `RecurringActions`, `TimeQueue`, `ARDateTime`, `ARTimeSpan` |
| Maps / markers | `M.Maps.Markers`, `.Compasses` | `CompassMarker`, `HeroMarker`, `DiscoveryMarker`, `Compass`, `MapService` |
| Factions / crime | `M.Fights.Factions`, `.Crimes` | `Faction`, `FactionContainer`, `FactionService`, `FactionProvider`, `Crime`, `CrimeService` |
| Save / load | `M.Saving`, `.Cloud`, `.SaveSlots`, `.Models`, `.LargeFiles` | `LoadSave`, `SaveSystem`, `LoadSystem`, `AutoSaving`, `SaveSlot`, `CloudService` |
| Title screen | `M.UI.TitleScreen`, `.Loading`, `.PatchNotes` | `TitleScreenUI`, `LoadingScreenUI`, `M.UI.TitleScreen.Loading.LoadingTypes.*` |
| Memories / facts | `M.Memories`, `.Journal` | `Memory`, `GameplayMemory`, `PrefMemory` (player prefs), `ContextualFacts`, `PlayerJournal` |
| Templates registry | `M.Templates` | `Template` (MonoBehaviour base), `ITemplate`, `TemplateService`, `TemplatesProvider` |
| HUD | `M.UI.HUD`, `.AdvancedNotifications` | `HUD`, `HUDState`, advanced-notification subsystems |
| UI state | `MVC.UI.Handlers.States` | `UIStateStack` (push/pop modal layers), `UIState`, `IUIStateSource` |
| Popups | `M.UI.Popup` | `PopupUI`, `ContextPopupUI`, `VContextPopupUI`, `ContextPopupOption`, `SpawnContext` |
| Settings | `M.Settings`, `.Audio`, `.Graphics`, `.Gameplay`, `.Controls`, `.Accessibility` | `SettingsMaster`, `Setting<T>`, plus per-area settings models |
| Cameras | `M.Cameras`, `.CameraStack`, `.Controllers` | `GameCamera`, `CameraStateStack`, camera controllers |
| Audio | `M.AudioSystem`, `.Biomes`, `.Notifications` | `AudioCore`, `AudioManager`, `FMODManager`, `BusGroup`, `AudioGroup`, `AudioBiome` |
| AI / combat behaviour | `M.AI.Combat`, `.Behaviours.*` | `CombatDirector`, behaviour state machines |
| Wyrdness | `M.Wyrdnessing` | `WyrdnessService`, `HeroWyrdNight`, `WyrdnessReactor` |
| New game+ | `M.NewGamePlus` | `NewGamePlusSystem`, `NewGamePlusUtils` |
| Scene lifecycle | `M.Scenes`, `.SceneConstructors` | `SceneLifetimeEvents` (heavy hook target), `MapScene`, `GameplayConstructor`, `ApplicationScene` |
| Mod loading | `M.Assets.Modding` | `ModService` (game's own mod registry, separate from BepInEx) |
| Skills | `M.Skills`, `.Cooldowns`, `.Passives`, `.Units` | `Skill`, `SkillsLearned`, `Cooldown` |
| Action log | `M.ActionLogs` | `ActionLog` (the chat-style HUD log; `World.Only<ActionLog>()`) |

---

## 4. UI architecture

Two parallel UI stacks coexist:

1. **MonoBehaviour / uGUI** views — most gameplay UI. `MVC.View` subclasses, prefab in `Resources/Prefabs/MapViews/<ViewName>`. `[SpawnsView]` on the model auto-instantiates and binds via `World.SpawnView`. `View.Mount(parent)` re-parents and registers with `Focus`.
2. **UI Toolkit (`UnityEngine.UIElements`)** — `MVC.Presenter<TModel>` subclasses with `VisualElement Parent / Content`, bound via `World.BindPresenter`. Used for things like character creator, popups.

### Opening a panel

Typical pattern: instantiate a Model that implements `IUIStateSource` (e.g. `M.Heroes.CharacterSheet.CharacterSheetUI`, `M.UI.TitleScreen.TitleScreenUI`, `M.Stories.Story`). Adding to `World` triggers `[SpawnsView]` → the View prefab loads. The Model's `UIState` property is read by `MVC.UI.Handlers.States.UIStateStack`, which pushes a `UIState` (modal flag, pause-time, hidden HUD elements). Discarding the model pops the state.

`UIStateStack.Instance` (`Domain.Globals`) lives for whole app lifetime. `UIState.ModalState(...)`, `UIState.WithPauseTime()`, etc. compose flags. `IUIStateSource.UIState` is queried by the stack on add/refresh.

### Input dispatch & focus

- `M.Heroes.PlayerInput` is `Element<GameUI>`. `HandleKeyboard()` and `HandleRegisteredPlayerInputs()` are the per-frame dispatch — Better-UI and BetterSortingCrafting both prefix-patch these to suppress game input while a search box has focus.
- `MVC.UI.Handlers.Focuses.Focus` (a Model, looked up by `World.Any<Focus>()`) tracks the focused View. Views auto-register on `Mount`.
- `MVC.UI.Handlers.Selections.Selection` (Model) tracks logical selection (controller-friendly). Fires `Selection.Events.SelectionChanged`.
- `M.UI.GlobalKeys` (a Service) wraps `AlwaysPresentHandlers` (Element of `GameUI`) to provide app-wide bindings.
- `M.UI.ButtonSystem.ARButton` is the game's button — implements its own hover/select states with `TargetGraphic` + colour fields. **Search bars / new clickable widgets must use `ARButton`** (or at least a `Selectable`) so the input dispatcher recognises them.

### The bag UI (search/filter injection example)

`VItemsDefaultUI` (`M.Heroes.CharacterSheet.Items.Panel.VItemsDefaultUI`) is the bag's main view. Its `OnMount` is the standard injection point (BetterSortingCrafting, ImprovedInventory). Filtering goes through `M.Heroes.CharacterSheet.Items.Panel.List.ItemsListUI.OverrideFilter(ItemsTabType)` — the predicate-bearing tab type also doubles as a runtime filter. After every `ItemsListUI.Refresh` the override is **cleared by the game** — so postfix-patching `Refresh` and re-applying is the canonical way to make filters persist across tab/refresh.

`ItemsTabType` is enum-of-instances (`All`, `Recent`, `OneHanded`, `Magic`, `Arrows`, `Ranged`, …) each carrying a `Func<Item, bool>` predicate. Crafting equivalent: `M.Crafting.HandCrafting.RecipeView.RecipeGridUI`, sorting via `VCRecipeSorting`.

### Popup pattern

`M.UI.Popup.ContextPopupUI.CreatePopup(target, options)` is the dropdown popup. Set `M.UI.Popup.SpawnContext.Anchor = someTransform` first to anchor instead of cursor. `ContextPopupOption(text, color, callback, enabled, sortingOrder)` holds entries. BetterSortingCrafting uses this to replace the keyboard-cycle behaviour on `VCItemSorting.NextSorting` and `VCRecipeSorting.NextSorting`.

### Prefab references

Code-side: `Resources.Load<GameObject>(World.PrefabPath + "/" + name)` — but most game UI uses Unity Addressables (`M.Assets.ARAssetReference`) for non-`Resources/` prefabs. Cross-reference with AssetRipper output (see `CLAUDE.md`). `World.ExtractPrefab(type, "Prefabs/MapViews")` is the standard view-prefab resolver.

---

## 5. Common modding hooks

Grouped by domain. Method names are intended for Harmony patches; default access is unspecified — many require `Krafs.Publicizer`.

**Inventory / items:**
- `M.Heroes.CharacterSheet.Items.Panel.VItemsDefaultUI.OnMount` — re-fired on every open; idempotent injection required (BetterSortingCrafting).
- `M.Heroes.CharacterSheet.Items.Panel.List.ItemsListUI.Refresh` — clears `OverrideFilter`; postfix to re-apply.
- `M.Heroes.CharacterSheet.Items.Panel.List.ItemsListUI.RefreshItemsCount` / `HandelNavigation` — used by ImprovedInventory.
- `M.Heroes.CharacterSheet.Items.Panel.List.VCItemSorting.NextSorting` — keyboard-cycle entry; prefix to swap for popup.
- `M.Heroes.CharacterSheet.Items.Panel.List.ItemsSorting.Compare` — sort comparator; ImprovedInventory patches.
- `M.Heroes.CharacterSheet.Items.Panel.Slot.ItemSlotUI.Setup` — per-cell visual; multiple mods patch.
- `M.Heroes.Items.Item.OnInitialize` — every item, on creation.
- `M.Heroes.Items.HeroItems.Add` — inventory pickup hook (Element-level events: `Hero.Events.PickedUpEquippable`).
- `M.Locations.Containers.ContainerInventory.Add` — container drop (SpoilsOfTheSlain).
- `M.Heroes.Items.DroppedItemSpawner.OnItemDropped` — world drop (SpoilsOfTheSlain).

**Crafting:**
- `M.Crafting.HandCrafting.HandcraftingTemplate.Recipes` (getter) / `AlchemyTemplate.Recipes` / `CookingTemplate.Recipes` — recipe list; postfix to inject recipes (SpoilsOfTheSlain).
- `M.Heroes.HeroRecipes.IsLearned` — gate recipe visibility per-Hero.
- `M.Crafting.HandCrafting.RecipeView.VCRecipeSorting.NextSorting` / `OnAttach` — sort cycle.
- `M.Crafting.HandCrafting.RecipeView.RecipeGridUI.AllRecipesOfCurrentType` (getter) / `OnFullyInitialized` — grid populate.

**UI / input:**
- `M.Heroes.PlayerInput.HandleKeyboard` / `HandleRegisteredPlayerInputs` — gate input (TextField focus suppression).
- `M.UI.Popup.VContextPopupUI.Refresh` / `OnInitialize` / `SyncPosition` — popup positioning.
- `M.UI.TitleScreen.TitleScreenUI.OnInitialize` — first-screen mod hooks (WyrdSight).
- `MVC.UI.Handlers.States.UIStateStack.PushState` — observe modal layer changes.

**Hero & combat:**
- `M.Heroes.Hero.OnInitialize` — fires on new game.
- `M.Heroes.Hero.OnRestore` — fires on load (StationIndicator hooks here for save-load init). Mods that need post-load setup should patch both.
- `M.Heroes.Hero.OnFullyInitialized` — after elements added (WyrdSight).
- `Hero.Events.LevelUp`, `Hero.Events.HeroLanded`, `Hero.Events.WalkedThroughPortal`, `Hero.Events.FastTraveled`, `Hero.Events.HeroAttacked`, etc. — listen via `EventSystem`.
- `M.Character.HealthElement` — universal hit handling.
- `M.Locations.Attachments.Elements.DeathElement.OnDeath` — death hook (SpoilsOfTheSlain).
- `M.Fights.NPCs.NpcElement.OnFullyInitialized` — every NPC spawn.

**Locations & world:**
- `M.Locations.Setup.LocationSpec.GetAttachments` (postfix) — inject `IAttachmentSpec` into a Location's spawn pipeline (StationIndicator does this to add map markers).
- `M.Locations.Pickables.Pickable.OnPrefabLoaded` (WyrdSight glow inject).
- `M.Locations.Actions.SearchAction.OnInitialize` / `OnRestore`.
- `M.Locations.Actions.PickItemAction.OnInitialize`.
- `M.Locations.Actions.LootInteractAction.OnLocationFullyInitialized`.

**Time & lifecycle:**
- `M.Timing.GameRealTime.Events.GameTimeChanged` / `DayBegan` / `NightBegan`.
- `M.Wyrdnessing.WyrdNightControllerBase.OnNightChange`.
- `M.Scenes.SceneLifetimeEvents.Events.AfterWorldInitialized` / `AfterSceneFullyInitialized` — best post-load hook for global mod setup.
- `M.UI.TitleScreen.Loading.LoadingScreenUI.OnTitleScreenLoaded`.

**Tooltips / UI fragments:**
- `M.Heroes.Items.Tooltips.Components.ItemTooltipFooterComponent.SetupCounters`.
- `M.Heroes.CharacterSheet.Items.Equipment.VEquipmentUI.SetArmorWeightBar`.
- Item descriptors: `M.Heroes.Items.ItemDescriptionElement.Setup`.

**Patch gotchas:**
- `Hero.OnRestore` and `Hero.OnInitialize` are **separate paths** — mods needing a hero callback must patch both (StationIndicator does).
- `OnMount` fires on every open (after `OnInitialize` on first open); injections need de-dup guards.
- `[SpawnsView]` views fire after `OnInitialize` but **before** `OnFullyInitialized`. For "everything is wired" guarantees prefer `OnFullyInitialized` or the `World.Events.ModelFullyInitialized<T>()` listener.
- `World.All<T>` returns a struct enumerator that walks `ModelsByType` — safe during normal frames; **not** safe to call during a domain drop (will throw — `s_verifyInvalid` flag).
- Some `Refresh` methods recurse (`OverrideFilter` → `Refresh` → cleared → patch reapplies → `Refresh` → …). Use a static reentrancy guard in postfixes (BetterSortingCrafting's `_isReapplying`).

---

## 6. Pitfalls observed in source

- `Hero.Current` is set in `OnInitialize` / `OnRestore`, not the constructor — accessing it before `Hero.Create` returns yields null.
- `MVC.World` static state means `EDITOR_RuntimeReset()` exists; in-game it's a one-shot. Domain switches use `DropDomain` instead.
- `ModelsByType.InitCapacity` (`World.cs:351`) pre-allocates large bucket sizes (`Item` = 1700, `Location` = 3100, `AttackGeneric` = 3000) — **don't add models in tight loops without thought**, and avoid `World.All<T>().ToList()` for hot types.
- `IsBeingInitialized` is true during `OnInitialize` — calling `Discard()` while in this state defers via `DiscardAfterInit` (`Model.cs:222`). Mods triggering Discard from a hook should be aware.
- `Element.GenericParentModel` becomes null after `OnFullyDiscarded` (`Element.cs:67`). Listeners running late see a half-broken Element.
- Listener cleanup is **owner-driven** (not subscription-driven). Plain lambdas with no `IListenerOwner` leak. Use `model.ListenTo(...)` with `this` as owner from inside a `Model`/`View`/`Presenter`.
- `World.Events.IsAddedEventRelevant(type)` is checked before firing — if no one ever subscribed for that type, the event is **never raised**, so polling-based mods don't see it. Subscribe defensively with `World.Events.ModelAddedAnyType` if you need to observe everything.
- `World.AssignServices` throws on second call — important for hot-reload scenarios. Use `World.EDITOR_RuntimeReset()` in editor, but production restart is required in-game.
- Crime/Faction services are **gameplay-domain bound** (added in `GameplayConstructor.BaseInit`) — patches that touch them at title-screen time will null-deref.
- Save format: `[Saved]` reads the *current* type's reflection. Renaming a saved field breaks back-compat — mods adding state to existing models should use `IDStorage`/separate dictionary keyed by `Model.ID`, not new `[Saved]` fields.
- `ContextPopupUI.CreatePopup` consumes `SpawnContext.Anchor` — if you set it but throw before calling `CreatePopup`, **reset it to null** in your catch (BetterSortingCrafting does).
- `View.Mount` re-parents and calls `OnMount` — patches on `OnMount` see fresh state every time the panel is opened, not just first init.
- `Awaken.ECS` collisions: a few `M.*` files pull `Awaken.ECS.DrakeRenderer.Systems` etc. directly — Mono main-thread assumption may not hold inside ECS systems, so don't synchronously access Models from there.

---

## 7. Useful greps

```
# Find a model class hierarchy
grep -rln "class \w\+ : Model\b" refs/game/TG.Main/Awaken.TG.Main.<area>/

# Where is a service registered?
grep -rnE "World\.Services\.Register\(.*\bMyServiceName\b" refs/game/TG.Main/

# What events does this model fire?
grep -nE "public static readonly Event<" refs/game/TG.Main/Awaken.TG.Main.Heroes/Hero.cs

# Find an event's listeners / triggers
grep -rn "Hero\.Events\.LevelUp" refs/game/TG.Main/

# Find a UI view's prefab name (view name = prefab name unless [UsesPrefab])
grep -rln "class \w\+UI : Model\b" refs/game/TG.Main/

# Find SpawnsView declarations to see attached views
grep -rn "\[SpawnsView" refs/game/TG.Main/Awaken.TG.Main.<area>/

# Find Save attribute usage on a system
grep -rn "\[Saved" refs/game/TG.Main/Awaken.TG.Main.<area>/
```

---

## 8. Open questions

- **Mod loading order via `M.Assets.Modding.ModService`** — there is a "ModService" registered alongside BepInEx but I didn't decode whether it's used at runtime for anything beyond loose-asset packs. Worth a closer look if a mod needs to ship Addressables.
- **Quest hooks for "objective just changed state"** — `M.Stories.Quests.Objectives.Objective` has lifecycle methods but I didn't enumerate the events fired on transition. Likely on `Objective` or `QuestState`.
- **`MVC.UI.Universal`** — referenced from `World.cs` (`VModalBlocker` mentioned in `SpawnsView`), unread namespace. Likely contains the modal-layer dim/blur logic.
- **`M.Saving.CustomSerializers`** — what types need bespoke serialisation? Affects mod-added `[Saved]` fields of unusual types.
- **`MVC.Relations`** — `RelationStore` and `Relation` exist on every Model (`Model.cs:59-63`). Used for cross-model references that survive serialisation. Untouched here.
- **`M.Assets.ARAssetReference` lifecycle** — when does an addressable load/release? Affects mods loading custom prefabs.
- **`M.RemoteEvents`** — exists at namespace level but unread. Possibly Steam-cloud event payloads vs. a generic event-streaming layer.
- **Tween/animation entry points** — DOTween is in use (`SetTweensCapacity(500, 50)` in `ApplicationScene`); didn't map who hooks `View` mount→tween chains.
- **HotReload status** — `BetterSortingCrafting/HotReload.cs` is gone in this branch; whether the workspace still supports any in-process reload is unclear.
