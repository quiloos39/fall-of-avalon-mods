using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Awaken.TG.Main.Crafting.HandCrafting.RecipeView;
using Awaken.TG.Main.UI.Components;

namespace BetterSortingCrafting
{
    // Mirror of InventorySearchInjector for the crafting screen. Filters
    // route through the existing crafting filter pipeline (FilterState.SearchText
    // + RecipeGridUI.AllRecipesOfCurrentType postfix in CraftingSortPatches.cs)
    // — we just feed it a search string from the input field and call
    // RefreshGrid to make the grid pull fresh items.
    internal static class CraftingSearchInjector
    {
        // Track which views we've already injected — SetHeaderTabName fires
        // every tab switch, so without this we'd build a new bar each time.
        private static readonly HashSet<VRecipeGridUI> _injected = new HashSet<VRecipeGridUI>();

        public static void Inject(VRecipeGridUI view)
        {
            if (view == null || view.headerNameText == null) return;
            if (_injected.Contains(view)) return;

            // Hot-reload safety: nuke any old containers from a previous load.
            var parent = view.headerNameText.transform.parent;
            if (parent != null) ClearOldContainers(parent);

            try
            {
                Build(view);
                _injected.Add(view);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] crafting search inject failed: {e.GetBaseException()}");
            }
        }

        private static void Build(VRecipeGridUI view)
        {
            var headerRT = (RectTransform)view.headerNameText.transform;
            var parent   = headerRT.parent != null ? headerRT.parent : headerRT;

            // Container — anchored to the right of the header row.
            var container = new GameObject("BetterSorting_CraftingSearch",
                typeof(RectTransform), typeof(Image));
            container.transform.SetParent(parent, worldPositionStays: false);

            var rt = (RectTransform)container.transform;
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-20f, 0f);
            rt.sizeDelta = new Vector2(220f, 32f);

            var bg = container.GetComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

            var btn = container.AddComponent<ARButton>();
            btn.TargetGraphic = bg;
            btn.NormalColor   = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            btn.HoverColor    = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            btn.SelectedColor = new Color(0.3f, 0.3f, 0.3f, 0.8f);
            btn.PressColor    = new Color(0.4f, 0.4f, 0.4f, 0.8f);
            btn.transitionType = ARButton.TransitionType.Color;
            var btnNav = ((Selectable)btn).navigation;
            btnNav.mode = Navigation.Mode.None;
            ((Selectable)btn).navigation = btnNav;

            var inputGo = new GameObject("Input", typeof(RectTransform));
            inputGo.transform.SetParent(container.transform, worldPositionStays: false);
            var inputRt = (RectTransform)inputGo.transform;
            inputRt.anchorMin = Vector2.zero;
            inputRt.anchorMax = Vector2.one;
            inputRt.offsetMin = new Vector2(8f, 2f);
            inputRt.offsetMax = new Vector2(-8f, -2f);

            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var areaRt = (RectTransform)area.transform;
            areaRt.SetParent(inputRt, worldPositionStays: false);
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = Vector2.zero;
            areaRt.offsetMax = Vector2.zero;

            var font = view.headerNameText.font ?? FindAnyTmpFont();
            if (font == null)
            {
                Plugin.Log.LogWarning("[BetterSorting] no TMP font available for crafting search bar");
                return;
            }

            var placeholder = MakeTmp("Placeholder", areaRt, font, "Search recipes...", italic: true,
                color: new Color(0.5f, 0.5f, 0.5f, 0.8f));
            var textComp    = MakeTmp("Text",        areaRt, font, string.Empty,            italic: false,
                color: Color.white);

            var input = inputGo.AddComponent<TMP_InputField>();
            input.textViewport = areaRt;
            input.textComponent = textComp;
            input.placeholder = placeholder;
            input.fontAsset = font;
            input.pointSize = 14;
            input.characterLimit = 50;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.caretColor = Color.white;
            input.caretWidth = 2;
            input.customCaretColor = true;
            input.selectionColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
            var inputNav = ((Selectable)input).navigation;
            inputNav.mode = Navigation.Mode.None;
            ((Selectable)input).navigation = inputNav;

            var clickForwarder = container.AddComponent<ActivateInputOnClick>();
            clickForwarder.Target = input;

            var blocker = container.AddComponent<SearchInputFocusBlocker>();
            blocker.Initialize(input, OnSearchTextChanged);
            input.onValueChanged.AddListener(text => blocker.OnSearchTextChanged(text));
        }

        private static void OnSearchTextChanged(string text)
        {
            // Plug into the existing crafting filter pipeline. The
            // RecipeGridUI.AllRecipesOfCurrentType postfix already applies
            // FilterState.SearchText to the recipe enumeration.
            FilterState.SearchText = text ?? string.Empty;
            VCRecipeSorting_OnAttach_Patch.RefreshGrid();
        }

        private static void ClearOldContainers(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child != null && child.name.StartsWith("BetterSorting_CraftingSearch"))
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        private static UnityEngine.TextCore.Text.FontAsset FindAnyTmpFont()
        {
            var anyText = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
            if (anyText != null && anyText.font != null) return anyText.font;
            var all = Resources.FindObjectsOfTypeAll<UnityEngine.TextCore.Text.FontAsset>();
            return all.Length > 0 ? all[0] : null;
        }

        private static TMP_Text MakeTmp(string name, RectTransform parent, UnityEngine.TextCore.Text.FontAsset font,
                                        string text, bool italic, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = 14f;
            tmp.text = text;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            if (italic) tmp.fontStyle = FontStyles.Italic;
            tmp.raycastTarget = false;
            return tmp;
        }
    }

    // Hook --------------------------------------------------------------------

    [HarmonyPatch(typeof(VRecipeGridUI), nameof(VRecipeGridUI.SetHeaderTabName))]
    internal static class VRecipeGridUI_SetHeaderTabName_Patch
    {
        static void Postfix(VRecipeGridUI __instance)
        {
            try { CraftingSearchInjector.Inject(__instance); }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] crafting search hook failed: {e.GetBaseException().Message}");
            }
        }
    }
}
