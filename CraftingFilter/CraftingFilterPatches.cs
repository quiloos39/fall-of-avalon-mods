using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Awaken.TG.Main.Crafting.HandCrafting.RecipeView;
using Awaken.TG.Main.Crafting.Recipes;
using Awaken.TG.Main.Localization;
using Awaken.TG.Main.UI.ButtonSystem;
using Awaken.TG.Main.UI.Components;
using Awaken.TG.Main.UI.Popup;
using Awaken.TG.Main.Utility;
using Awaken.TG.Utility;
using Awaken.Utility;
using Cysharp.Threading.Tasks;

namespace CraftingFilter
{
    internal static class SpawnContext
    {
        public static Transform Anchor;
    }

    internal static class PopupOwnership
    {
        public static bool IsOurs(VContextPopupUI v) =>
            v?.Target?._owner is RecipeTabContents;
    }

    // Replace the keyboard-cycle behaviour of VCRecipeSorting.NextSorting
    // with a context popup of just sort options (filter has its own popup
    // bound to FilterItems below).
    [HarmonyPatch(typeof(VCRecipeSorting), nameof(VCRecipeSorting.NextSorting))]
    internal static class VCRecipeSorting_NextSorting_Patch
    {
        static bool Prefix(VCRecipeSorting __instance)
        {
            try
            {
                CustomSortings.EnsureBuilt();

                var tabContents = __instance.Target;
                var grid = tabContents?.RecipeGridUI;
                if (grid == null) return true;

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
                Plugin.Log.LogError($"[CraftingFilter] sort popup failed: {e.GetBaseException()}");
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
                            Plugin.Log.LogError($"[CraftingFilter] applying sort failed: {e.GetBaseException().Message}");
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

                // Idempotent — return if we already registered a filter prompt
                // for this VC. Belt-and-braces against double OnAttach (real
                // game shouldn't, but live-probe re-runs and hot-reloads can).
                if (FilterPromptRegistry.Has(__instance)) return;

                var prompts = tabContents.Element<Prompts>();
                if (prompts == null) return;

                // If a previous CraftingFilter assembly already added a filter
                // prompt to this Prompts element (eg. via hot-reload), discard
                // it before adding a fresh one — prevents stacking duplicates.
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
                Plugin.Log.LogError($"[CraftingFilter] filter prompt registration failed: {e.GetBaseException()}");
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
                Plugin.Log.LogError($"[CraftingFilter] alignment fix failed: {e.GetBaseException().Message}");
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
                Plugin.Log.LogError($"[CraftingFilter] discard existing prompts failed: {e.GetBaseException().Message}");
            }
        }

        // Format mirrors vanilla VCItemSorting.RefreshPromptName: greyed-out
        // "Filter:" prefix, white italic-semibold value next to it.
        public static string BuildLabel()
        {
            var prefix = LocTerms.UIItemsChangeFilter.Translate()
                .ColoredText(ARColor.MainGrey).FontLight();
            var value = FilterState.WeaponFilter.Label()
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
                Plugin.Log.LogError($"[CraftingFilter] refresh label failed: {e.GetBaseException().Message}");
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

                bool weaponish = IsWeaponishTab(grid.CurrentType);
                var options = BuildFilterOptions(weaponish);

                SpawnContext.Anchor = FilterPromptRegistry.GetAnchor(source)
                    ?? (source.sortPrompt != null ? source.sortPrompt.transform : source.transform);

                ContextPopupUI.CreatePopup(tabContents, options);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CraftingFilter] filter popup failed: {e.GetBaseException()}");
                SpawnContext.Anchor = null;
            }
        }

        private static List<ContextPopupOption> BuildFilterOptions(bool weaponish)
        {
            var options = new List<ContextPopupOption>();
            int order = 0;

            if (weaponish)
            {
                foreach (var f in FilterState.AllValues)
                {
                    var captured = f;
                    var color = FilterState.WeaponFilter == f
                        ? VCRecipeSorting_NextSorting_Patch.Highlight
                        : Color.white;
                    options.Add(new ContextPopupOption(
                        text: f.Label(),
                        color: color,
                        callback: () =>
                        {
                            FilterState.WeaponFilter = captured;
                            RefreshLabel();
                            RefreshGrid();
                        },
                        enabled: true,
                        sortingOrder: order++));
                }
            }
            else
            {
                // No applicable filters on this tab — show a single hint row
                // so the user understands what happened. The row is enabled:
                // false (greyed-out, no-op).
                options.Add(new ContextPopupOption(
                    text: "(no filters for this tab)",
                    color: new Color(0.55f, 0.55f, 0.6f),
                    callback: () => { },
                    enabled: false,
                    sortingOrder: order++));
            }

            return options;
        }

        public static bool IsWeaponishTab(RecipeTabType ct)
        {
            return ct == RecipeTabType.All
                || ct == RecipeTabType.Weapon
                || ct == RecipeTabType.Arrows;
        }

        // Force the tab to drop its existing slots and rebuild from the
        // (now-filtered) AllRecipesOfCurrentType. RecipeGridUI.Refresh by
        // itself only re-sorts in place — the slots persist if force is
        // false, which is why filter changes appeared not to take effect.
        public static void RefreshGrid()
        {
            try
            {
                var grid = FilterState.ActiveGrid;
                if (grid == null) return;
                grid.CurrentTab?.Refresh(force: true, contentsChanged: true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CraftingFilter] refresh failed: {e.GetBaseException().Message}");
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

    // Apply our search + weapon-subtype filter on top of the tab's existing
    // CurrentType.Contains predicate.
    [HarmonyPatch(typeof(RecipeGridUI), nameof(RecipeGridUI.AllRecipesOfCurrentType), MethodType.Getter)]
    internal static class RecipeGridUI_AllRecipesOfCurrentType_Patch
    {
        static void Postfix(RecipeGridUI __instance, ref IEnumerable<IRecipe> __result)
        {
            try
            {
                if (FilterState.ActiveGrid != __instance) return;

                bool weaponApplies = FilterState.WeaponFilter != WeaponSubFilter.All
                    && VCRecipeSorting_OnAttach_Patch.IsWeaponishTab(__instance.CurrentType);

                bool searchApplies = !string.IsNullOrEmpty(FilterState.SearchText);

                if (!weaponApplies && !searchApplies) return;

                var inner = __result;
                __result = inner.Where(r =>
                {
                    if (r?.Outcome == null) return false;
                    if (searchApplies)
                    {
                        var name = r.OutcomeName() ?? string.Empty;
                        if (name.IndexOf(FilterState.SearchText, StringComparison.OrdinalIgnoreCase) < 0)
                            return false;
                    }
                    if (weaponApplies && !FilterState.WeaponFilter.Matches(r.Outcome))
                        return false;
                    return true;
                });
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CraftingFilter] filter postfix failed: {e.GetBaseException().Message}");
            }
        }
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

    // Restyle the popup — applies to both sort and filter popups since both
    // are owned by RecipeTabContents.
    [HarmonyPatch(typeof(VContextPopupUI), "Refresh")]
    internal static class VContextPopupUI_Refresh_Patch
    {
        private static readonly Color BgColor = new Color(0.06f, 0.06f, 0.07f, 0.96f);
        private static readonly Color RowNormal = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color RowHover  = new Color(1f, 0.92f, 0.55f, 0.22f);
        private static readonly Color Hidden    = new Color(0f, 0f, 0f, 0f);

        static void Postfix(VContextPopupUI __instance)
        {
            try
            {
                if (!PopupOwnership.IsOurs(__instance)) return;
                StyleRoot(__instance);
                StyleOptions(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CraftingFilter] restyle failed: {e.GetBaseException().Message}");
            }
        }

        private static void StyleRoot(VContextPopupUI v)
        {
            var t = v.actionsParent;
            int safety = 6;
            while (t != null && safety-- > 0)
            {
                var img = t.GetComponent<Image>();
                if (img != null)
                {
                    img.color = BgColor;
                    img.sprite = null;
                }
                if (t == v.transform) break;
                t = t.parent;
            }

            if (v.verticalLayout != null)
            {
                v.verticalLayout.padding = new RectOffset(28, 28, 14, 14);
                v.verticalLayout.spacing = 4f;
                v.verticalLayout.childForceExpandWidth = true;
                v.verticalLayout.childControlWidth = true;

                var fitter = v.verticalLayout.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                    fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        private static void StyleOptions(VContextPopupUI v)
        {
            if (v.actionsParent == null) return;

            for (int i = 0; i < v.actionsParent.childCount; i++)
            {
                var child = v.actionsParent.GetChild(i);
                if (child == null) continue;

                foreach (var btn in child.GetComponentsInChildren<ARButton>(true))
                {
                    var wrapperImg = btn.GetComponent<Image>();
                    if (wrapperImg != null)
                    {
                        btn.TargetGraphic = wrapperImg;
                        wrapperImg.raycastTarget = true;
                        wrapperImg.color = RowNormal;
                    }

                    btn.transitionType = (ARButton.TransitionType)0;

                    if (btn.hoverGraphic != null)            btn.hoverGraphic.color = Hidden;
                    if (btn.selectedGraphic != null)         btn.selectedGraphic.color = Hidden;
                    if (btn.pressGraphic != null)            btn.pressGraphic.color = Hidden;
                    if (btn.additiveSelectedGraphic != null) btn.additiveSelectedGraphic.color = Hidden;
                    if (btn.disableGraphic != null)          btn.disableGraphic.color = Hidden;

                    if (wrapperImg != null)
                    {
                        var hh = btn.GetComponent<HoverHighlight>() ?? btn.gameObject.AddComponent<HoverHighlight>();
                        hh.Target = wrapperImg;
                        hh.NormalColor = RowNormal;
                        hh.HoverColor = RowHover;
                    }
                }

                var tmp = child.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null)
                {
                    tmp.fontStyle |= FontStyles.SmallCaps;
                    tmp.alignment = TextAlignmentOptions.MidlineLeft;
                    tmp.raycastTarget = false;
                }
            }

            ApplyWidthsDeferred(v).Forget();
        }

        private static async UniTaskVoid ApplyWidthsDeferred(VContextPopupUI v)
        {
            await UniTask.DelayFrame(1);
            try
            {
                if (v == null || v.actionsParent == null) return;

                for (int i = 0; i < v.actionsParent.childCount; i++)
                {
                    var child = v.actionsParent.GetChild(i);
                    if (child == null) continue;
                    var tmp = child.GetComponentInChildren<TextMeshProUGUI>(true);
                    var le = child.GetComponent<LayoutElement>();
                    if (tmp == null || le == null) continue;

                    tmp.ForceMeshUpdate();
                    float w = tmp.preferredWidth + 16f;
                    le.preferredWidth = w;
                    le.minWidth = w;
                }

                var layoutRoot = v.verticalLayout != null
                    ? v.verticalLayout.transform as RectTransform
                    : v.actionsParent as RectTransform;
                if (layoutRoot != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(layoutRoot);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CraftingFilter] deferred width apply failed: {e.GetBaseException().Message}");
            }
        }
    }

    [HarmonyPatch(typeof(VContextPopupUI), "OnInitialize")]
    internal static class VContextPopupUI_OnInitialize_Patch
    {
        static void Postfix(VContextPopupUI __instance)
        {
            try
            {
                if (!PopupOwnership.IsOurs(__instance)) return;
                if (SpawnContext.Anchor == null) return;
                var vlRect = __instance.verticalLayout != null
                    ? __instance.verticalLayout.transform as RectTransform
                    : null;
                if (vlRect != null)
                    vlRect.pivot = new Vector2(0f, 0f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CraftingFilter] pivot override failed: {e.GetBaseException().Message}");
            }
        }
    }

    [HarmonyPatch(typeof(VContextPopupUI), "SyncPosition")]
    internal static class VContextPopupUI_SyncPosition_Patch
    {
        // Reused — GetWorldCorners writes into a caller-provided Vector3[4].
        // Single-threaded UI code, so a static buffer is fine.
        private static readonly Vector3[] _corners = new Vector3[4];

        static void Postfix(VContextPopupUI __instance)
        {
            try
            {
                if (!PopupOwnership.IsOurs(__instance)) return;
                var anchor = SpawnContext.Anchor;
                if (anchor == null) return;

                var anchorRect = anchor as RectTransform;
                if (anchorRect == null) { SpawnContext.Anchor = null; return; }

                anchorRect.GetWorldCorners(_corners);
                __instance.transform.position = _corners[1];

                SpawnContext.Anchor = null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CraftingFilter] anchor positioning failed: {e.GetBaseException().Message}");
                SpawnContext.Anchor = null;
            }
        }
    }
}
