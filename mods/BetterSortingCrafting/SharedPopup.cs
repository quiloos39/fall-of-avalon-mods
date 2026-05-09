using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Awaken.TG.Main.Crafting.HandCrafting.RecipeView;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.List;
using Awaken.TG.Main.UI.Components;
using Awaken.TG.Main.UI.Popup;
using Cysharp.Threading.Tasks;

namespace BetterSortingCrafting
{
    // Shared state for the moment between "user pressed sort/filter" and
    // "popup view is being initialised." Single-shot — consumed by the first
    // matching popup, then nulled.
    internal static class SpawnContext
    {
        public static Transform Anchor;
    }

    // A popup is ours if its target's owner is one of the consumers we wired
    // up: ItemsListUI (inventory/character sheet) or RecipeTabContents
    // (crafting). Used to guard the styling/anchor patches so we don't
    // restyle vanilla popups opened elsewhere in the game.
    internal static class PopupOwnership
    {
        public static bool IsOurs(VContextPopupUI v)
        {
            var owner = v?.Target?._owner;
            return owner is ItemsListUI || owner is RecipeTabContents;
        }
    }

    // Restyle the popup background, padding, and per-row look. Same styling
    // used for both inventory sort and crafting sort/filter popups — they all
    // route through ContextPopupUI.
    [HarmonyPatch(typeof(VContextPopupUI), "Refresh")]
    internal static class VContextPopupUI_Refresh_Patch
    {
        private static readonly Color BgColor   = new Color(0.06f, 0.06f, 0.07f, 0.96f);
        private static readonly Color RowNormal = new Color(1f, 1f, 1f, 0.10f);     // subtle grey base
        private static readonly Color RowHover  = new Color(1f, 0.92f, 0.55f, 0.22f); // warm hover tint
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
                Plugin.Log.LogError($"[BetterSorting] restyle failed: {e.GetBaseException().Message}");
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

                    // Disable ARButton's color transitions — they don't apply
                    // our colors reliably at runtime. We handle hover ourselves
                    // via HoverHighlight below.
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
                Plugin.Log.LogError($"[BetterSorting] deferred width apply failed: {e.GetBaseException().Message}");
            }
        }
    }

    // Override position + pivot — vanilla puts popup at Input.mousePosition.
    // We anchor to the prompt and decide grow direction based on whether the
    // popup fits above. Without this check a top-of-screen anchor (e.g. the
    // crafting filter prompt) makes the popup overflow the top edge.
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
                // 0=BL, 1=TL, 2=TR, 3=BR (world-space).

                var vlRect = __instance.verticalLayout != null
                    ? __instance.verticalLayout.transform as RectTransform
                    : null;

                // Force a layout pass so rect.height is settled before we
                // check overflow — Refresh built children but Unity may not
                // have laid them out yet.
                if (vlRect != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(vlRect);

                float popupHeight = vlRect != null ? vlRect.rect.height : 0f;
                float spaceAbove  = Screen.height - _corners[1].y;
                bool openDown = popupHeight > 0f
                    ? popupHeight > spaceAbove
                    : _corners[1].y < Screen.height * 0.5f;     // fallback when height not measured

                if (openDown)
                {
                    // Popup TL at anchor BL → grows down-right.
                    __instance.transform.position = _corners[0];
                    if (vlRect != null) vlRect.pivot = new Vector2(0f, 1f);
                }
                else
                {
                    // Popup BL at anchor TL → grows up-right (default).
                    __instance.transform.position = _corners[1];
                    if (vlRect != null) vlRect.pivot = new Vector2(0f, 0f);
                }

                SpawnContext.Anchor = null;     // consume
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] anchor positioning failed: {e.GetBaseException().Message}");
                SpawnContext.Anchor = null;
            }
        }
    }
}
