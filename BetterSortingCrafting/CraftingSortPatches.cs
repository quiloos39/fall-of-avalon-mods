using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Awaken.TG.Main.Crafting.HandCrafting;
using Awaken.TG.Main.Crafting.HandCrafting.RecipeView;
using Awaken.TG.Main.Crafting.Recipes;
using Awaken.TG.Main.Localization;
using Awaken.TG.Main.UI.ButtonSystem;
using Awaken.TG.Main.UI.Components;
using Awaken.TG.Main.UI.Popup;
using Awaken.TG.Main.Utility;
using Awaken.TG.Utility;
using Awaken.Utility;

namespace BetterSortingCrafting
{
    // Replace the keyboard-cycle behaviour of VCRecipeSorting.NextSorting
    // with a context popup of just sort options (filter has its own popup
    // bound to FilterItems below).
    [HarmonyPatch(typeof(VCRecipeSorting), nameof(VCRecipeSorting.NextSorting))]
    internal static class VCRecipeSorting_NextSorting_Patch
    {
        static bool Prefix(VCRecipeSorting __instance)
        {
            Plugin.Log.LogInfo($"[BetterSorting] NextSorting Prefix fired (instance={__instance?.name})");
            try
            {
                CustomSortings.EnsureBuilt();

                var tabContents = __instance.Target;
                var grid = tabContents?.RecipeGridUI;
                if (grid == null) { Plugin.Log.LogWarning("[BetterSorting] NextSorting: grid was null, falling through to vanilla"); return true; }

                FilterState.ActiveGrid = grid;
                SearchBar.Ensure(__instance);

                var options = BuildSortOptions(grid);
                SpawnContext.Anchor = __instance.sortPrompt != null
                    ? __instance.sortPrompt.transform
                    : __instance.transform;
                ContextPopupUI.CreatePopup(tabContents, options);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] crafting sort popup failed: {e.GetBaseException()}");
                SpawnContext.Anchor = null;
                return true;
            }
        }

        private static List<ContextPopupOption> BuildSortOptions(RecipeGridUI grid)
        {
            var options = new List<ContextPopupOption>();
            int order = 0;
            foreach (var (sorting, label) in EnumerateSorts())
            {
                var captured = sorting;
                var color = grid.CurrentSorting == sorting ? Highlight : Color.white;
                options.Add(new ContextPopupOption(
                    text: label,
                    color: color,
                    callback: () =>
                    {
                        try { grid.ChangeItemsComparer(captured); }
                        catch (Exception e)
                        {
                            Plugin.Log.LogError($"[BetterSorting] applying crafting sort failed: {e.GetBaseException().Message}");
                        }
                    },
                    enabled: true,
                    sortingOrder: order++));
            }
            return options;
        }

        private static IEnumerable<(RecipeSorting sorting, string label)> EnumerateSorts()
        {
            yield return (RecipeSorting.AlphabeticallyAscending,  "Name A→Z");
            yield return (RecipeSorting.AlphabeticallyDescending, "Name Z→A");
            yield return (RecipeSorting.ByPriceAscending,         "Price ↑");
            yield return (RecipeSorting.ByPriceDescending,        "Price ↓");
            if (CustomSortings.DamageDescending != null)
                yield return (CustomSortings.DamageDescending, "Damage ↓");
            if (CustomSortings.DamageAscending != null)
                yield return (CustomSortings.DamageAscending,  "Damage ↑");
            if (CustomSortings.ArmorDescending != null)
                yield return (CustomSortings.ArmorDescending,  "Armor ↓");
            if (CustomSortings.ArmorAscending != null)
                yield return (CustomSortings.ArmorAscending,   "Armor ↑");
        }

        internal static readonly Color Highlight = new Color(1f, 0.85f, 0.4f);
    }

    // Register a separate filter prompt bound to the FilterItems keybind.
    // Mirrors vanilla VCItemSorting which exposes both a sort prompt and a
    // filter prompt — keeps the two popups split per user feedback.
    [HarmonyPatch(typeof(VCRecipeSorting), "OnAttach")]
    internal static class VCRecipeSorting_OnAttach_Patch
    {
        static void Postfix(VCRecipeSorting __instance)
        {
            try
            {
                var tabContents = __instance.Target;
                if (tabContents == null) return;

                // Make the search bar visible as soon as crafting attaches —
                // matches BetterUI's pattern of injecting on OnMount, instead
                // of waiting for a sort/filter keypress.
                var grid = tabContents.RecipeGridUI;
                if (grid != null) FilterState.ActiveGrid = grid;
                SearchBar.Ensure(__instance);

                // Idempotent — return if we already registered a filter prompt
                // for this VC. Belt-and-braces against double OnAttach (real
                // game shouldn't, but live-probe re-runs and hot-reloads can).
                if (FilterPromptRegistry.Has(__instance)) return;

                var prompts = tabContents.Element<Prompts>();
                if (prompts == null) return;

                // If a previous BetterSortingCrafting assembly already added a
                // filter prompt to this Prompts element (eg. via hot-reload),
                // discard it before adding a fresh one — prevents duplicates.
                DiscardExistingFilterPrompts(prompts);

                var filterPrompt = Prompt.Tap(
                    KeyBindings.UI.Items.FilterItems,
                    BuildLabel(),
                    () => OpenFilterPopup(__instance)
                ).AddAudio();

                // Parent under sortPrompt's parent (SortingSlot — a
                // VerticalLayoutGroup) so it stacks below the sort prompt
                // with matching alignment. Default AddPrompt(.., owner, ..)
                // would parent it under PromptsHost (= vc.transform itself,
                // a sibling of SortingSlot), which centers it instead.
                Transform host = null;
                if (__instance.sortPrompt != null)
                    host = __instance.sortPrompt.transform.parent;

                if (host != null)
                    prompts.AddPrompt(filterPrompt, tabContents, host, active: true, visible: true);
                else
                    prompts.AddPrompt(filterPrompt, tabContents, active: true, visible: true);

                FilterPromptRegistry.Set(__instance, filterPrompt);

                // The VGenericPromptUI prefab ships with
                // ContentSizeFitter.horizontalFit = PreferredSize, which
                // auto-shrinks the rect to fit its text — so a short
                // "Filter: All weapons" ends up narrower than the long
                // sortPrompt and visually mis-centers under the parent
                // VerticalLayoutGroup. The vanilla sortPrompt has H set
                // to Unconstrained on its prefab override; mirror that so
                // the VLG's childForceExpandWidth can stretch us to full
                // width and align our left edge with the sort prompt.
                AlignWithSortPrompt(filterPrompt, __instance.sortPrompt);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] filter prompt registration failed: {e.GetBaseException()}");
            }
        }

        private static void AlignWithSortPrompt(Prompt filterPrompt, Component sortPromptComp)
        {
            try
            {
                var view = filterPrompt?.MainView as Component;
                if (view == null) return;

                // 1) Disable horizontal content-fit so VLG's force-expand wins.
                var fitter = view.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                    fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

                // 2) Mirror sortPrompt's anchor/pivot/sizeDelta exactly so
                // both rects are positioned identically along the X axis.
                var rt = view.transform as RectTransform;
                var srt = sortPromptComp != null ? sortPromptComp.transform as RectTransform : null;
                if (rt != null && srt != null)
                {
                    rt.anchorMin = srt.anchorMin;
                    rt.anchorMax = srt.anchorMax;
                    rt.pivot = srt.pivot;
                    rt.sizeDelta = srt.sizeDelta;
                    // Don't touch anchoredPosition — VLG sets Y for stacking.
                }

                // 3) Force the parent layout to re-run now so the change is
                // visible immediately (otherwise it lags a frame).
                var slot = view.transform.parent as RectTransform;
                if (slot != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(slot);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] alignment fix failed: {e.GetBaseException().Message}");
            }
        }

        private static void DiscardExistingFilterPrompts(Prompts prompts)
        {
            try
            {
                var existing = new List<Prompt>();
                foreach (var p in prompts.Elements<Prompt>().GetManagedEnumerator())
                {
                    if (p != null && p.IsKeyBindedTo(KeyBindings.UI.Items.FilterItems))
                        existing.Add(p);
                }
                foreach (var p in existing) p.Discard();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] discard existing prompts failed: {e.GetBaseException().Message}");
            }
        }

        // Format mirrors vanilla VCItemSorting.RefreshPromptName: greyed-out
        // "Filter:" prefix, white italic-semibold value next to it. The value
        // is "Group / Label" for grouped filters, just "Label" for All.
        public static string BuildLabel()
        {
            var prefix = LocTerms.UIItemsChangeFilter.Translate()
                .ColoredText(ARColor.MainGrey).FontLight();
            var value = FilterState.ItemFilter.PromptLabel
                .Italic().ColoredText(ARColor.MainWhite).FontSemiBold();
            return prefix + " " + value;
        }

        // Refresh the visible "[R] FILTER: <value>" prompt to reflect
        // the current FilterState. Call after every filter change.
        public static void RefreshLabel()
        {
            try
            {
                var prompt = FilterPromptRegistry.GetPrompt();
                prompt?.ChangeName(BuildLabel());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] refresh label failed: {e.GetBaseException().Message}");
            }
        }

        public static void OpenFilterPopup(VCRecipeSorting source)
        {
            try
            {
                var tabContents = source.Target;
                var grid = tabContents?.RecipeGridUI;
                if (grid == null) return;

                FilterState.ActiveGrid = grid;
                SearchBar.Ensure(source);

                AnchorAtSortPrompt(source);
                ContextPopupUI.CreatePopup(tabContents, BuildTopLevelFilterOptions(tabContents, source));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] filter popup failed: {e.GetBaseException()}");
                SpawnContext.Anchor = null;
            }
        }

        private static void AnchorAtSortPrompt(VCRecipeSorting source)
        {
            SpawnContext.Anchor = FilterPromptRegistry.GetAnchor(source)
                ?? (source.sortPrompt != null ? source.sortPrompt.transform : source.transform);
        }

        // Top-level popup: All + each category opens a sub-popup. The active
        // filter's category gets highlighted so the user can see where they
        // last narrowed.
        private static List<ContextPopupOption> BuildTopLevelFilterOptions(RecipeTabContents tabContents, VCRecipeSorting source)
        {
            var options = new List<ContextPopupOption>();
            int order = 0;

            options.Add(new ContextPopupOption(
                text: "All",
                color: ReferenceEquals(FilterState.ItemFilter, RecipeFilter.All)
                    ? VCRecipeSorting_NextSorting_Patch.Highlight
                    : Color.white,
                callback: () =>
                {
                    FilterState.ItemFilter = RecipeFilter.All;
                    RefreshLabel();
                    RefreshGrid();
                },
                enabled: true,
                sortingOrder: order++));

            foreach (var (groupLabel, filters) in RecipeFilter.Groups)
            {
                var capturedFilters = filters;
                var capturedGroup = groupLabel;
                bool isActiveGroup = FilterState.ItemFilter.Group == groupLabel;
                options.Add(new ContextPopupOption(
                    text: groupLabel + "  ▸",
                    color: isActiveGroup ? VCRecipeSorting_NextSorting_Patch.Highlight : Color.white,
                    callback: () => OpenSubFilterPopup(tabContents, source, capturedGroup, capturedFilters),
                    enabled: true,
                    sortingOrder: order++));
            }

            return options;
        }

        private static void OpenSubFilterPopup(RecipeTabContents tabContents, VCRecipeSorting source, string groupLabel, RecipeFilter[] filters)
        {
            try
            {
                var options = new List<ContextPopupOption>();
                int order = 0;
                foreach (var f in filters)
                {
                    var captured = f;
                    options.Add(new ContextPopupOption(
                        text: captured.Label,
                        color: ReferenceEquals(FilterState.ItemFilter, captured)
                            ? VCRecipeSorting_NextSorting_Patch.Highlight
                            : Color.white,
                        callback: () =>
                        {
                            FilterState.ItemFilter = captured;
                            RefreshLabel();
                            RefreshGrid();
                        },
                        enabled: true,
                        sortingOrder: order++));
                }
                AnchorAtSortPrompt(source);
                ContextPopupUI.CreatePopup(tabContents, options);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] sub-filter popup '{groupLabel}' failed: {e.GetBaseException()}");
                SpawnContext.Anchor = null;
            }
        }

        // Filter refresh that mirrors how vanilla sort works: never touch the
        // RecipeSlot collection, just trigger ContentsUpdated. Vanilla sort
        // is fast because it RefreshIndex's existing slots and lets the
        // recycler reorder _models — no Object.Instantiate, no Discard.
        //
        // We piggy-back on that same path: contentsChanged=true makes
        // VRecipeTabContents.SortChildren run, and our postfix on SortChildren
        // (below) prunes the recycler's _models list to matching slots only.
        // The recycler then retargets its small pool of view GameObjects to
        // the smaller _models — same cost shape as a sort change.
        public static void RefreshGrid()
        {
            try
            {
                var grid = FilterState.ActiveGrid;
                if (grid == null) return;
                grid.CurrentTab?.Refresh(force: false, contentsChanged: true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] refresh failed: {e.GetBaseException().Message}");
            }
        }
    }

    // Postfix on SortChildren — runs after vanilla re-indexes all slots and
    // OrderChangedRefresh sorts _models by Index. We rebuild _models from
    // tab.Elements<RecipeSlot>() in the freshly-assigned Index order, but
    // skip non-matching slots. The slots themselves stay as Element children
    // (so widening the filter just re-includes them, no respawn needed).
    [HarmonyPatch(typeof(Awaken.TG.Main.Crafting.HandCrafting.RecipeView.VRecipeTabContents), "SortChildren")]
    internal static class VRecipeTabContents_SortChildren_Patch
    {
        static void Postfix(Awaken.TG.Main.Crafting.HandCrafting.RecipeView.VRecipeTabContents __instance)
        {
            try
            {
                var tab = __instance?.Target;
                if (tab == null) return;
                var grid = tab.RecipeGridUI;
                if (grid == null) return;

                var listUI = __instance.recipeListUI;
                if (listUI == null) return;
                var rcm = listUI.GridManager;
                if (rcm == null) return;

                bool itemFilterActive = !ReferenceEquals(FilterState.ItemFilter, RecipeFilter.All);
                bool searchApplies = !string.IsNullOrEmpty(FilterState.SearchText);

                var ordered = new List<RecipeSlot>();
                foreach (var slot in tab.Elements<RecipeSlot>().GetManagedEnumerator())
                    if (slot != null) ordered.Add(slot);
                ordered.Sort((a, b) => a.Index.CompareTo(b.Index));

                rcm._models.Clear();
                foreach (var slot in ordered)
                {
                    if (itemFilterActive || searchApplies)
                    {
                        var recipe = slot.Recipe;
                        if (recipe?.Outcome == null) continue;

                        if (searchApplies)
                        {
                            var name = recipe.OutcomeName() ?? string.Empty;
                            if (name.IndexOf(FilterState.SearchText, StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                        }
                        if (itemFilterActive && !FilterState.ItemFilter.Matches(recipe.Outcome))
                            continue;
                    }
                    rcm._models.Add(slot);
                }

                rcm.Resize();
                rcm.SetDirty();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] sort/filter postfix failed: {e.GetBaseException().Message}");
            }
        }
    }

    // Tracks the filter Prompt instance per VCRecipeSorting so we can find
    // its visual host transform for popup anchoring.
    internal static class FilterPromptRegistry
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VCRecipeSorting, Prompt> _map
            = new System.Runtime.CompilerServices.ConditionalWeakTable<VCRecipeSorting, Prompt>();

        // Tracks the most recently registered prompt so RefreshLabel can find
        // it without needing the VC. Cleared when no instances are alive.
        private static Prompt _last;

        public static void Set(VCRecipeSorting vc, Prompt p)
        {
            _map.Remove(vc);
            _map.Add(vc, p);
            _last = p;
        }

        public static bool Has(VCRecipeSorting vc) =>
            vc != null && _map.TryGetValue(vc, out _);

        public static Prompt GetPrompt() => _last;

        public static Transform GetAnchor(VCRecipeSorting vc)
        {
            if (vc == null) return null;
            if (!_map.TryGetValue(vc, out var prompt) || prompt == null) return null;
            // Prompt's main view is the VGenericPromptUI we spawned — pull
            // its transform if available.
            try
            {
                var view = prompt.MainView as Component;
                return view != null ? view.transform : null;
            }
            catch { return null; }
        }
    }

    // While the user has the search bar focused, suppress the game's keyboard
    // and registered-input handlers so typing letters into the field doesn't
    // fire hotkeys. Mirrors BetterUI's pattern for VItemsDefaultUI search.
    [HarmonyPatch(typeof(Awaken.TG.Main.Heroes.PlayerInput), "HandleKeyboard")]
    internal static class PlayerInput_HandleKeyboard_Patch
    {
        static bool Prefix() => !SearchBar.IsFocused && !InventorySearchBar.IsFocused;
    }

    [HarmonyPatch(typeof(Awaken.TG.Main.Heroes.PlayerInput), "HandleRegisteredPlayerInputs")]
    internal static class PlayerInput_HandleRegistered_Patch
    {
        static bool Prefix() => !SearchBar.IsFocused && !InventorySearchBar.IsFocused;
    }

    [HarmonyPatch(typeof(RecipeGridUI), "OnFullyInitialized")]
    internal static class RecipeGridUI_OnFullyInitialized_Patch
    {
        static void Postfix(RecipeGridUI __instance)
        {
            FilterState.ActiveGrid = __instance;
            FilterState.Reset();
        }
    }
}
