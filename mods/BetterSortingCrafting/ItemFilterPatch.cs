using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.List;
using Awaken.TG.Main.Localization;
using Awaken.TG.Main.UI.Popup;
using Awaken.TG.Main.Utility;
using Awaken.TG.Utility;
using Awaken.Utility;

namespace BetterSortingCrafting
{
    // For lists with UseCategoryList=true (shop, bag, stash, gems, transmog,
    // equipment-choose) the vanilla VCItemSorting force-disables the filter
    // prompt — there's no way to narrow the view at all. Force-enable it and
    // route its keybind through the same multi-level category popup the
    // crafting station already has.
    [HarmonyPatch(typeof(VCItemSorting), "OnAttach")]
    internal static class VCItemSorting_OnAttach_FilterPatch
    {
        static void Postfix(VCItemSorting __instance)
        {
            try
            {
                if (!IsCategoryListContext(__instance)) return;

                // Per-attach reset so a previous open's filter doesn't carry
                // over into a fresh one. Clears search text too — the input
                // field is rebuilt empty by InventorySearchBar.Build, so the
                // static state has to match.
                ItemFilterState.Reset();
                InventorySearchBar.SyncFromState();

                __instance.ChangeFilterPromptState(active: true, visible: true);
                RefreshLabel(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] item-filter OnAttach failed: {e.GetBaseException().Message}");
            }
        }

        public static bool IsCategoryListContext(VCItemSorting vc)
        {
            var config = vc?.Target?.ItemsUI?.Config;
            return config != null && config.UseCategoryList;
        }

        public static void RefreshLabel(VCItemSorting vc)
        {
            try
            {
                var prompt = vc?._filterPrompt;
                prompt?.ChangeName(BuildLabel());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] item-filter label refresh failed: {e.GetBaseException().Message}");
            }
        }

        public static string BuildLabel()
        {
            var prefix = LocTerms.UIItemsChangeFilter.Translate()
                .ColoredText(ARColor.MainGrey).FontLight();
            var value = ItemFilterState.CategoryFilter.PromptLabel
                .Italic().ColoredText(ARColor.MainWhite).FontSemiBold();
            return prefix + " " + value;
        }
    }

    [HarmonyPatch(typeof(VCItemSorting), nameof(VCItemSorting.NextFilter))]
    internal static class VCItemSorting_NextFilter_Patch
    {
        static bool Prefix(VCItemSorting __instance)
        {
            try
            {
                if (!VCItemSorting_OnAttach_FilterPatch.IsCategoryListContext(__instance))
                    return true;     // fall through to vanilla cycle for inventory etc.

                AnchorAtFilterPrompt(__instance);
                ContextPopupUI.CreatePopup(__instance.Target, BuildTopLevelOptions(__instance));
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] item filter popup failed: {e.GetBaseException()}");
                SpawnContext.Anchor = null;
                return true;
            }
        }

        private static void AnchorAtFilterPrompt(VCItemSorting vc)
        {
            SpawnContext.Anchor = vc.filterPrompt != null
                ? vc.filterPrompt.transform
                : vc.transform;
        }

        private static List<ContextPopupOption> BuildTopLevelOptions(VCItemSorting vc)
        {
            var options = new List<ContextPopupOption>();
            int order = 0;

            options.Add(new ContextPopupOption(
                text: "All",
                color: ReferenceEquals(ItemFilterState.CategoryFilter, RecipeFilter.All)
                    ? VCRecipeSorting_NextSorting_Patch.Highlight
                    : Color.white,
                callback: () =>
                {
                    ItemFilterState.CategoryFilter = RecipeFilter.All;
                    ApplyAndRefreshLabel(vc);
                },
                enabled: true,
                sortingOrder: order++));

            foreach (var (groupLabel, filters) in RecipeFilter.Groups)
            {
                var capturedFilters = filters;
                var capturedGroup = groupLabel;
                bool isActiveGroup = ItemFilterState.CategoryFilter.Group == groupLabel;
                options.Add(new ContextPopupOption(
                    text: groupLabel + "  ▸",
                    color: isActiveGroup
                        ? VCRecipeSorting_NextSorting_Patch.Highlight
                        : Color.white,
                    callback: () => OpenSubFilter(vc, capturedGroup, capturedFilters),
                    enabled: true,
                    sortingOrder: order++));
            }

            return options;
        }

        private static void OpenSubFilter(VCItemSorting vc, string groupLabel, RecipeFilter[] filters)
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
                        color: ReferenceEquals(ItemFilterState.CategoryFilter, captured)
                            ? VCRecipeSorting_NextSorting_Patch.Highlight
                            : Color.white,
                        callback: () =>
                        {
                            ItemFilterState.CategoryFilter = captured;
                            ApplyAndRefreshLabel(vc);
                        },
                        enabled: true,
                        sortingOrder: order++));
                }
                AnchorAtFilterPrompt(vc);
                ContextPopupUI.CreatePopup(vc.Target, options);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] item sub-filter '{groupLabel}' failed: {e.GetBaseException()}");
                SpawnContext.Anchor = null;
            }
        }

        private static void ApplyAndRefreshLabel(VCItemSorting vc)
        {
            VCItemSorting_OnAttach_FilterPatch.RefreshLabel(vc);
            ItemFilterState.Apply(vc?.Target);
        }
    }
}
