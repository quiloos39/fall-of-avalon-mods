using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

using Awaken.TG.MVC;
using Awaken.TG.Main.Fights.Factions.Crimes;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Interactions;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.LootTables;
using Awaken.TG.Main.Locations;
using Awaken.TG.Main.Locations.Actions;
using Awaken.TG.Main.Locations.Pickables;
using Awaken.TG.Main.Locations.Regrowables;

namespace AutoLoot
{
    internal class AutoLooter : MonoBehaviour
    {
        public bool Active { get; set; }

        private float _nextScanAt;
        private int _scanCounter;

        // Cached scene-wide pickable specs. FindObjectsByType is the single most expensive
        // call in this mod; we refresh on (a) scene change, (b) timer, OR (c) hero movement.
        // The movement trigger keeps the cache fresh while exploring without polling needlessly when idle.
        private static readonly List<PickableSpecBase> SpecCache = new List<PickableSpecBase>(256);
        private float _nextSpecCacheRefresh;
        private Vector3 _heroPosAtLastRefresh;
        private bool _sceneChanged = true;

        // Reusable buffers — keep these alive between scans to avoid per-tick GC.
        private static readonly List<ItemSpawningDataRuntime> ContainerItemsBuffer = new List<ItemSpawningDataRuntime>(16);
        private static readonly List<Item> ContainerInvBuffer = new List<Item>(16);

        // Tracks how many items the current scan has already picked up. Used to enforce
        // MaxPickupsPerScan so a fat corpse doesn't spawn 15 notifications in one frame.
        private int _pickedThisScan;
        private int _budgetThisScan;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            _sceneChanged = true;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => _sceneChanged = true;
        private void OnActiveSceneChanged(Scene from, Scene to) => _sceneChanged = true;

        private void Update()
        {
            if (!Active) return;
            if (!Plugin.Cfg.Enabled.Value) return;
            if (Hero.Current == null) return;

            if (Time.unscaledTime < _nextScanAt) return;
            _nextScanAt = Time.unscaledTime + Plugin.Cfg.ScanInterval.Value;

            try
            {
                Scan();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AutoLoot] scan failed: {e}");
            }
        }

        private bool BudgetExhausted() => _budgetThisScan > 0 && _pickedThisScan >= _budgetThisScan;

        private void Scan()
        {
            _scanCounter++;
            _pickedThisScan = 0;
            _budgetThisScan = Plugin.Cfg.MaxPickupsPerScan.Value;
            bool verbose = Plugin.Cfg.Verbose.Value;

            var hero = Hero.Current;
            var heroPos = hero.Coords;
            var radius = Plugin.Cfg.ScanRadius.Value;
            var radiusSq = radius * radius;

            int specsTotal = 0;
            int specsInRange = 0;
            int specsPicked = 0;
            int locsTotal = 0;
            int locsWithPick = 0;
            int locsInRange = 0;
            int locsPicked = 0;
            int regrowTotal = 0;
            int regrowInRange = 0;
            int regrowPicked = 0;
            int contTotal = 0;
            int contInRange = 0;
            int contItemsTaken = 0;

            // ─── Path 1: PickableSpec / MutablePickableSpec (static-scene pickables) ─────
            RefreshSpecCacheIfNeeded(heroPos, radius);
            specsTotal = SpecCache.Count;

            for (int i = 0; i < SpecCache.Count; i++)
            {
                if (BudgetExhausted()) break;

                var spec = SpecCache[i];
                if (spec == null) continue;

                var dx = spec.transform.position - heroPos;
                if (dx.sqrMagnitude > radiusSq) continue;
                specsInRange++;

                var pickable = spec.InteractableWithHero as Pickable;
                if (pickable == null || !pickable.IsValidAction) continue;

                var template = SafeGetTemplate(spec.ItemData, spec);
                if (template == null) continue;

                if (!PassesFilters(template, pickable.IsIllegal, $"spec '{spec.name}'", verbose)) continue;

                if (TryPickPickable(pickable, template, verbose))
                {
                    specsPicked++;
                    _pickedThisScan++;
                }
            }

            // ─── Path 2 + Path 4: Locations (single iteration handles both PickItemAction and SearchAction) ─
            try
            {
                var locations = World.All<Location>();
                var enumerator = locations.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    if (BudgetExhausted()) break;

                    var loc = enumerator.Current;
                    if (loc == null || loc.HasBeenDiscarded) continue;
                    locsTotal++;

                    // Distance check first — by far the most common reason a Location is irrelevant.
                    // This avoids two element lookups (PickItemAction + SearchAction) per far-away location.
                    var dx = loc.Coords - heroPos;
                    if (dx.sqrMagnitude > radiusSq) continue;

                    if (Plugin.Cfg.IgnoreLocked.Value)
                    {
                        var lockAction = loc.TryGetElement<LockAction>();
                        if (lockAction != null && lockAction.Locked)
                        {
                            if (verbose) Plugin.Log.LogInfo($"[AutoLoot] skip loc '{loc.DisplayName}' — locked");
                            continue;
                        }
                    }

                    var pick = loc.TryGetElement<PickItemAction>();
                    if (pick != null)
                    {
                        locsWithPick++;
                        locsInRange++;
                        if (TryHandlePickItemAction(pick, loc, verbose))
                        {
                            locsPicked++;
                            _pickedThisScan++;
                            if (BudgetExhausted()) break;
                        }
                    }

                    var search = loc.TryGetElement<SearchAction>();
                    if (search != null)
                    {
                        contTotal++;
                        if (!search.SearchAvailable) continue;
                        if (search.IsEmpty()) continue;
                        contInRange++;

                        bool searchIllegal = SafeContainerIsIllegal(search, loc);
                        if (Plugin.Cfg.IgnoreIllegal.Value && searchIllegal)
                        {
                            if (verbose) Plugin.Log.LogInfo($"[AutoLoot] skip container '{loc.DisplayName}' — illegal");
                            continue;
                        }

                        contItemsTaken += TakeFromContainer(search, loc, searchIllegal, verbose);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AutoLoot] Location scan failed: {e}");
            }

            // ─── Path 3: Regrowable (onions, herbs, mushrooms, vegetables, crops) ─────────
            try
            {
                var regrowables = Regrowable.s_regrowableById;
                if (regrowables != null)
                {
                    regrowTotal = regrowables.Count;
                    foreach (var kv in regrowables)
                    {
                        if (BudgetExhausted()) break;

                        var regrowable = kv.Value;
                        if (regrowable == null) continue;
                        if (!regrowable.IsValidAction) continue;

                        var dx = regrowable.Coords - heroPos;
                        if (dx.sqrMagnitude > radiusSq) continue;
                        regrowInRange++;

                        var template = regrowable.Template;
                        if (template == null) continue;

                        if (!PassesFilters(template, regrowable.IsIllegal, $"regrowable '{template.ItemName}'", verbose)) continue;

                        if (TryPickRegrowable(regrowable, template, verbose))
                        {
                            regrowPicked++;
                            _pickedThisScan++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AutoLoot] Regrowable scan failed: {e}");
            }

            if (verbose)
            {
                Plugin.Log.LogInfo(
                    $"[AutoLoot] scan #{_scanCounter} @ heroPos={Format(heroPos)} radius={radius}m " +
                    $"| specs total={specsTotal} inRange={specsInRange} picked={specsPicked} " +
                    $"| locs total={locsTotal} pickable={locsWithPick} inRange={locsInRange} picked={locsPicked} " +
                    $"| regrow total={regrowTotal} inRange={regrowInRange} picked={regrowPicked} " +
                    $"| containers total={contTotal} inRange={contInRange} itemsTaken={contItemsTaken} " +
                    $"| budget={(_budgetThisScan == 0 ? "∞" : _pickedThisScan + "/" + _budgetThisScan)}");
            }
        }

        private void RefreshSpecCacheIfNeeded(Vector3 heroPos, float scanRadius)
        {
            float interval = Plugin.Cfg.SpecCacheRefreshInterval.Value;
            // Force-refresh as soon as the hero has moved more than one scan-radius since the last refresh.
            // This keeps the cache fresh while exploring (where streaming might bring in new pickables)
            // without polling FindObjectsByType when standing still.
            float moveThresholdSq = scanRadius * scanRadius;
            bool movedFar = (heroPos - _heroPosAtLastRefresh).sqrMagnitude >= moveThresholdSq;

            if (!_sceneChanged && !movedFar && Time.unscaledTime < _nextSpecCacheRefresh) return;

            _sceneChanged = false;
            _nextSpecCacheRefresh = Time.unscaledTime + interval;
            _heroPosAtLastRefresh = heroPos;

            SpecCache.Clear();
            // FindObjectsByType allocates an array per call but Unity has type-indices internally,
            // so the cost is roughly O(matches) not O(scene size). Still expensive enough to cache.
            var pickables = UnityEngine.Object.FindObjectsByType<PickableSpec>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < pickables.Length; i++) SpecCache.Add(pickables[i]);
            var mutables = UnityEngine.Object.FindObjectsByType<MutablePickableSpec>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < mutables.Length; i++) SpecCache.Add(mutables[i]);
        }

        private bool TryHandlePickItemAction(PickItemAction pick, Location loc, bool verbose)
        {
            var data = pick._itemSpawningData;
            var template = data?.ItemTemplate;
            if (template == null)
            {
                if (verbose) Plugin.Log.LogInfo($"[AutoLoot] loc '{loc.DisplayName}' has PickItemAction but no template");
                return false;
            }

            if (!PassesFilters(template, pick.IsIllegal, $"loc '{loc.DisplayName}'", verbose)) return false;

            return TryPickLocation(pick, loc, template, verbose);
        }

        private bool PassesFilters(ItemTemplate template, bool isIllegal, string label, bool verbose)
        {
            // Hard skips that always apply, regardless of always-collect category.
            if (Plugin.Cfg.IgnoreIllegal.Value && isIllegal)
            {
                if (verbose) Plugin.Log.LogInfo($"[AutoLoot] skip {label} '{template.ItemName}' — illegal");
                return false;
            }
            if (Plugin.Cfg.IgnoreHidden.Value && template.HiddenOnUI)
            {
                if (verbose) Plugin.Log.LogInfo($"[AutoLoot] skip {label} '{template.ItemName}' — hidden");
                return false;
            }
            if (Plugin.Cfg.IgnoreQuestItems.Value && template.IsQuestItem())
            {
                if (verbose) Plugin.Log.LogInfo($"[AutoLoot] skip {label} '{template.ItemName}' — quest item");
                return false;
            }

            // Always-collect category bypasses BOTH the readable filter and the value/weight check.
            string alwaysReason = AlwaysCollectReason(template);
            if (alwaysReason != null)
            {
                if (verbose) Plugin.Log.LogInfo($"[AutoLoot] pass {label} '{template.ItemName}' — always-collect ({alwaysReason})");
                return true;
            }

            // Outside always-collect: respect the readable skip.
            if (Plugin.Cfg.IgnoreReadables.Value && template.IsReadable)
            {
                if (verbose) Plugin.Log.LogInfo($"[AutoLoot] skip {label} '{template.ItemName}' — readable");
                return false;
            }

            int basePrice = template.BasePrice;
            float weight = template.Weight;
            float ratio = weight > 0f ? basePrice / weight : 0f;

            if (weight <= 0f)
            {
                if (basePrice < Plugin.Cfg.MinValueWhenWeightless.Value)
                {
                    if (verbose) Plugin.Log.LogInfo($"[AutoLoot] skip {label} '{template.ItemName}' — weightless price {basePrice} < min {Plugin.Cfg.MinValueWhenWeightless.Value}");
                    return false;
                }
            }
            else if (ratio < Plugin.Cfg.MinValuePerWeight.Value)
            {
                if (verbose) Plugin.Log.LogInfo($"[AutoLoot] skip {label} '{template.ItemName}' — price={basePrice} weight={weight:F2} ratio={ratio:F2} < min {Plugin.Cfg.MinValuePerWeight.Value}");
                return false;
            }

            if (verbose) Plugin.Log.LogInfo($"[AutoLoot] pass {label} '{template.ItemName}' price={basePrice} weight={weight:F2} ratio={ratio:F2}");
            return true;
        }

        private static string AlwaysCollectReason(ItemTemplate template)
        {
            if (Plugin.Cfg.AlwaysFood.Value && (template.IsPlainFood || template.IsDish || template.IsFish)) return "food";
            if (Plugin.Cfg.AlwaysDrinkable.Value && (template.IsAlcohol || template.IsPotion)) return "drinkable";
            if (Plugin.Cfg.AlwaysCrafting.Value && template.IsCrafting) return "crafting";
            if (Plugin.Cfg.AlwaysReadable.Value && template.IsReadable) return "readable";
            if (Plugin.Cfg.AlwaysWeightless.Value && template.Weight <= 0f) return "weightless";
            if (Plugin.Cfg.AlwaysValueless.Value && template.BasePrice <= 0) return "valueless";
            return null;
        }

        private static bool TryPickPickable(Pickable pickable, ItemTemplate template, bool verbose)
        {
            try
            {
                if (template.IsReadable)
                {
                    return PickPickableSilent(pickable, template, verbose);
                }
                if (verbose) Plugin.Log.LogInfo($"[AutoLoot] picking up Pickable '{template.ItemName}'");
                bool ok = pickable.StartInteraction(Hero.Current, pickable);
                if (!ok && verbose) Plugin.Log.LogWarning($"[AutoLoot] Pickable.StartInteraction returned false for '{template.ItemName}'");
                return ok;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AutoLoot] Pickable pickup failed for '{template?.ItemName}': {e}");
                return false;
            }
        }

        private static bool PickPickableSilent(Pickable pickable, ItemTemplate template, bool verbose)
        {
            if (verbose) Plugin.Log.LogInfo($"[AutoLoot] silently picking up readable Pickable '{template.ItemName}'");
            var item = new Item(pickable._itemData);
            World.Add(item);
            CommitCrime.Theft(item, pickable);
            Hero.Current.Inventory.Add(item);
            pickable.NotifyPicked();
            return true;
        }

        private int TakeFromContainer(SearchAction search, Location location, bool searchIllegal, bool verbose)
        {
            var hero = Hero.Current;
            int taken = 0;

            try
            {
                // Snapshot into reusable buffers so MoveItem mutations don't invalidate enumerators.
                ContainerItemsBuffer.Clear();
                var inside = search._itemsInsideContainer;
                if (inside != null)
                {
                    for (int i = 0; i < inside.Count; i++) ContainerItemsBuffer.Add(inside[i]);
                }

                for (int i = 0; i < ContainerItemsBuffer.Count; i++)
                {
                    if (BudgetExhausted()) break;
                    var data = ContainerItemsBuffer[i];
                    var template = data?.ItemTemplate;
                    if (template == null) continue;

                    if (!PassesFilters(template, searchIllegal, $"container '{location.DisplayName}' item", verbose)) continue;

                    try
                    {
                        if (verbose) Plugin.Log.LogInfo($"[AutoLoot] taking from container '{location.DisplayName}': '{template.ItemName}' x{data.quantity}");
                        search.MoveItem(hero.Inventory, template, data.quantity);
                        taken += data.quantity;
                        _pickedThisScan++;
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError($"[AutoLoot] container MoveItem failed for '{template?.ItemName}': {e}");
                    }
                }

                var inv = location.Inventory;
                if (inv != null && !BudgetExhausted())
                {
                    ContainerInvBuffer.Clear();
                    foreach (var item in inv.Items) ContainerInvBuffer.Add(item);

                    for (int i = 0; i < ContainerInvBuffer.Count; i++)
                    {
                        if (BudgetExhausted()) break;
                        var item = ContainerInvBuffer[i];
                        if (item == null || item.HasBeenDiscarded) continue;
                        var template = item.Template;
                        if (template == null) continue;

                        if (!PassesFilters(template, searchIllegal, $"container '{location.DisplayName}' inv-item", verbose)) continue;

                        try
                        {
                            if (verbose) Plugin.Log.LogInfo($"[AutoLoot] taking from container inv '{location.DisplayName}': '{template.ItemName}' x{item.Quantity}");
                            search.MoveItem(hero.Inventory, template, item.Quantity);
                            taken += item.Quantity;
                            _pickedThisScan++;
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogError($"[AutoLoot] container MoveItem (inv) failed for '{template?.ItemName}': {e}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AutoLoot] TakeFromContainer failed: {e}");
            }
            finally
            {
                ContainerItemsBuffer.Clear();
                ContainerInvBuffer.Clear();
            }

            return taken;
        }

        // SearchAction.IsIllegal accesses _itemsInsideContainer[0] without checking length,
        // which throws when the list is empty (most NPC corpses store worn gear in Location.Inventory).
        // We sidestep that by inspecting the lists ourselves and calling Crime.Theft against a real
        // Item or ItemSpawningDataRuntime that we know exists.
        private static bool SafeContainerIsIllegal(SearchAction search, Location location)
        {
            try
            {
                var inside = search._itemsInsideContainer;
                if (inside != null && inside.Count > 0)
                {
                    using (var crime = Crime.Theft(inside[0], location))
                    {
                        return crime.IsCrime();
                    }
                }

                var inv = location.Inventory;
                if (inv != null)
                {
                    foreach (var item in inv.Items)
                    {
                        if (item == null || item.HasBeenDiscarded || item.Template == null) continue;
                        using (var crime = Crime.Theft(item, location))
                        {
                            return crime.IsCrime();
                        }
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[AutoLoot] SafeContainerIsIllegal fell through ({e.GetType().Name}: {e.Message}); treating '{location.DisplayName}' as legal.");
                return false;
            }
        }

        private static bool TryPickRegrowable(Regrowable regrowable, ItemTemplate template, bool verbose)
        {
            try
            {
                if (verbose) Plugin.Log.LogInfo($"[AutoLoot] picking up Regrowable '{template.ItemName}'");
                bool ok = regrowable.StartInteraction(Hero.Current, regrowable);
                if (!ok && verbose) Plugin.Log.LogWarning($"[AutoLoot] Regrowable.StartInteraction returned false for '{template.ItemName}'");
                return ok;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AutoLoot] Regrowable pickup failed for '{template?.ItemName}': {e}");
                return false;
            }
        }

        private static bool TryPickLocation(PickItemAction action, Location location, ItemTemplate template, bool verbose)
        {
            try
            {
                if (template.IsReadable)
                {
                    return PickLocationSilent(action, location, template, verbose);
                }
                if (verbose) Plugin.Log.LogInfo($"[AutoLoot] picking up Location '{template.ItemName}'");
                bool ok = action.StartInteraction(Hero.Current, location);
                if (!ok && verbose) Plugin.Log.LogWarning($"[AutoLoot] PickItemAction.StartInteraction returned false for '{template.ItemName}'");
                return ok;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[AutoLoot] Location pickup failed for '{template?.ItemName}': {e}");
                return false;
            }
        }

        private static bool PickLocationSilent(PickItemAction action, Location location, ItemTemplate template, bool verbose)
        {
            if (verbose) Plugin.Log.LogInfo($"[AutoLoot] silently picking up readable Location '{template.ItemName}'");
            var hero = Hero.Current;
            var item = new Item(action._itemSpawningData);
            World.Add(item);
            CommitCrime.Theft(item, location);
            hero.Inventory.Add(item);
            ModelExtensions.Trigger(location, PickItemAction.Events.ItemPicked, new PickItemAction.ItemPickedData(location, item, hero));
            if (action._destroyedOnInteract)
            {
                ((IInteractableWithHero)location).DestroyInteraction();
            }
            return true;
        }

        private static ItemTemplate SafeGetTemplate(ItemSpawningData data, object debugTarget)
        {
            try { return data?.ItemTemplate(debugTarget); }
            catch { return null; }
        }

        private static string Format(Vector3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";
    }
}
