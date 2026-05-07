using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.List;
using Awaken.TG.Main.UI.Popup;

namespace BetterSortingCrafting
{
    // Replaces the keyboard-cycle behaviour of VCItemSorting.NextSorting (used
    // by the inventory / character sheet) with a ContextPopupUI dropdown listing
    // every available sort.
    [HarmonyPatch(typeof(VCItemSorting), nameof(VCItemSorting.NextSorting))]
    internal static class VCItemSorting_NextSorting_Patch
    {
        // We augment _sortings with extras the game ships but never lists
        // (ByPriceAscending, ByWeightAscending) plus our own ValuePerWeight
        // sortings. Track which lists we've augmented so we only do it once
        // per unique list instance — _sortings is typically aliased to one
        // of the static lists (BaseComparers / WeaponComparers / ArmorComparers),
        // so a single mutation propagates to every VC that shares it.
        private static readonly HashSet<List<ItemsSorting>> _augmented = new HashSet<List<ItemsSorting>>();

        static bool Prefix(VCItemSorting __instance)
        {
            try
            {
                var sortings = __instance._sortings;
                var target = __instance.Target;
                if (sortings == null || sortings.Count <= 1 || target == null)
                    return true;     // fall back to vanilla cycle

                CustomItemSortings.EnsureBuilt();
                EnsureExtras(sortings);

                var current = __instance._currentSorting;
                var options = new List<ContextPopupOption>(sortings.Count);
                for (int i = 0; i < sortings.Count; i++)
                {
                    int idx = i;     // closure capture
                    var sorting = sortings[i];
                    var label = !string.IsNullOrEmpty(sorting.Name) ? sorting.Name : sorting.EnumName;
                    var color = (i == current) ? new Color(1f, 0.85f, 0.4f) : Color.white;

                    options.Add(new ContextPopupOption(
                        text: label,
                        color: color,
                        callback: () =>
                        {
                            try
                            {
                                __instance._currentSorting = idx;
                                __instance.RefreshSortingPrompt(sortings[idx]);
                                __instance.Target.Sort(sortings[idx]);
                            }
                            catch (Exception e)
                            {
                                Plugin.Log.LogError($"[BetterSorting] applying sort failed: {e.GetBaseException().Message}");
                            }
                        },
                        enabled: true,
                        sortingOrder: i));
                }

                // Anchor the upcoming popup to the sort prompt instead of the cursor.
                SpawnContext.Anchor = __instance.sortPrompt != null
                    ? __instance.sortPrompt.transform
                    : __instance.transform;

                ContextPopupUI.CreatePopup(target, options);
                return false;     // skip vanilla cycle
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] inventory popup open failed: {e.GetBaseException().Message}");
                SpawnContext.Anchor = null;
                return true;     // fall back to vanilla
            }
        }

        private static void EnsureExtras(List<ItemsSorting> list)
        {
            if (!_augmented.Add(list)) return;     // already augmented
            try
            {
                InsertAfter(list, ItemsSorting.ByPriceDescending,  ItemsSorting.ByPriceAscending);
                InsertAfter(list, ItemsSorting.ByWeightDescending, ItemsSorting.ByWeightAscending);
                // Slot value-per-weight after the (now-present) ByWeightAscending,
                // descending first so the "what to keep" ordering is on top.
                InsertAfter(list, ItemsSorting.ByWeightAscending,                   CustomItemSortings.ByValuePerWeightDescending);
                InsertAfter(list, CustomItemSortings.ByValuePerWeightDescending,   CustomItemSortings.ByValuePerWeightAscending);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] augmenting sortings failed: {e.GetBaseException().Message}");
            }
        }

        private static void InsertAfter(List<ItemsSorting> list, ItemsSorting after, ItemsSorting toInsert)
        {
            if (toInsert == null || after == null) return;
            if (list.Contains(toInsert)) return;
            int idx = list.IndexOf(after);
            if (idx >= 0) list.Insert(idx + 1, toInsert);
            else list.Add(toInsert);
        }
    }

}
