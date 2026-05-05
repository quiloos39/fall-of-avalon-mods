using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.List;
using Awaken.TG.Main.UI.Components;
using Awaken.TG.Main.UI.Popup;
using Cysharp.Threading.Tasks;

namespace SortDropdown
{
    // Shared state for the moment between "user pressed sort" and "popup view
    // is being initialised." Single-shot — consumed by the first matching
    // popup, then nulled.
    internal static class SpawnContext
    {
        public static Transform Anchor;
    }

    internal static class PopupOwnership
    {
        public static bool IsOurs(VContextPopupUI v) =>
            v?.Target?._owner is ItemsListUI;
    }

    // Replaces the keyboard-cycle behaviour of VCItemSorting.NextSorting with a
    // ContextPopupUI dropdown listing every available sort.
    [HarmonyPatch(typeof(VCItemSorting), nameof(VCItemSorting.NextSorting))]
    internal static class VCItemSorting_NextSorting_Patch
    {
        static bool Prefix(VCItemSorting __instance)
        {
            try
            {
                var sortings = __instance._sortings;
                var target = __instance.Target;
                if (sortings == null || sortings.Count <= 1 || target == null)
                    return true;     // fall back to vanilla cycle

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
                                Plugin.Log.LogError($"[SortDropdown] applying sort failed: {e.GetBaseException().Message}");
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
                Plugin.Log.LogError($"[SortDropdown] popup open failed: {e.GetBaseException().Message}");
                SpawnContext.Anchor = null;
                return true;     // fall back to vanilla
            }
        }
    }

    // Restyle the context popup (only when ours) — dark bg, no per-row boxes,
    // padded so the bg extends past the text.
    [HarmonyPatch(typeof(VContextPopupUI), "Refresh")]
    internal static class VContextPopupUI_Refresh_Patch
    {
        private static readonly Color BgColor = new Color(0.06f, 0.06f, 0.07f, 0.96f);

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
                Plugin.Log.LogError($"[SortDropdown] restyle failed: {e.GetBaseException().Message}");
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

                // Vanilla fitter is h=Unconstrained,v=PreferredSize — width is
                // locked. Switch to PreferredSize so the bg grows to fit the
                // longest option (combined with per-child preferredWidth set
                // in StyleOptions).
                var fitter = v.verticalLayout.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                    fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        // Per-row styling. Each row gets a subtle grey base so it reads as
        // a button, plus a brighter gold tint on hover. We drive both
        // directly via the wrapper Image and a custom HoverHighlight MB.
        private static readonly Color RowNormal = new Color(1f, 1f, 1f, 0.10f);    // subtle grey
        private static readonly Color RowHover = new Color(1f, 0.92f, 0.55f, 0.22f); // brighter warm tint
        private static readonly Color Hidden = new Color(0f, 0f, 0f, 0f);

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
                        wrapperImg.color = RowNormal;     // direct: stable grey base
                    }

                    // Disable ARButton's color transitions — they don't apply
                    // our colors reliably at runtime. We handle hover ourselves
                    // via HoverHighlight below.
                    btn.transitionType = (ARButton.TransitionType)0;

                    if (btn.hoverGraphic != null) btn.hoverGraphic.color = Hidden;
                    if (btn.selectedGraphic != null) btn.selectedGraphic.color = Hidden;
                    if (btn.pressGraphic != null) btn.pressGraphic.color = Hidden;
                    if (btn.additiveSelectedGraphic != null) btn.additiveSelectedGraphic.color = Hidden;
                    if (btn.disableGraphic != null) btn.disableGraphic.color = Hidden;

                    // Attach our own pointer-enter/exit handler so hovered row
                    // brightens. Idempotent — only adds if missing.
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

            // Defer width sizing by one frame — TMP's preferredWidth is unreliable
            // synchronously after .text is set (returns ~75% of settled value).
            // Mirrors the game's own pattern from VCTalentTreeTooltip:
            //   await UniTask.DelayFrame(1); rt.RebuildAllBelowInverse();
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
                Plugin.Log.LogError($"[SortDropdown] deferred width apply failed: {e.GetBaseException().Message}");
            }
        }
    }

    // Override pivot — vanilla picks pivot based on cursor position; our anchor
    // is at the bottom of the screen, so popup must grow up and to the right.
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
                    vlRect.pivot = new Vector2(0f, 0f);     // bottom-left → grows up-right
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[SortDropdown] pivot override failed: {e.GetBaseException().Message}");
            }
        }
    }

    // Override position — vanilla puts popup at Input.mousePosition. We anchor
    // to the sort prompt's top-left so it sits directly above the "SORT: X" line.
    [HarmonyPatch(typeof(VContextPopupUI), "SyncPosition")]
    internal static class VContextPopupUI_SyncPosition_Patch
    {
        static void Postfix(VContextPopupUI __instance)
        {
            try
            {
                if (!PopupOwnership.IsOurs(__instance)) return;
                var anchor = SpawnContext.Anchor;
                if (anchor == null) return;

                var anchorRect = anchor as RectTransform;
                if (anchorRect == null) { SpawnContext.Anchor = null; return; }

                var corners = new Vector3[4];
                anchorRect.GetWorldCorners(corners);
                // 0=BL, 1=TL, 2=TR, 3=BR. Pivot is BL of popup, so position at TL of anchor
                // makes popup sit immediately above the prompt, left-aligned.
                __instance.transform.position = corners[1];

                SpawnContext.Anchor = null;     // consume
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[SortDropdown] anchor positioning failed: {e.GetBaseException().Message}");
                SpawnContext.Anchor = null;
            }
        }
    }
}
