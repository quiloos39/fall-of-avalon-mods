using System;
using Awaken.TG.MVC;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.List;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.Tabs;
using Awaken.TG.Main.Heroes.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterSortingCrafting
{
    // Search bar for any VItemsDefaultUI host (bag, shop, stash, gems,
    // transmog, equipment-choose). Same UX as the crafting bar (identical
    // visual, custom blinking caret). Writes the search text into
    // ItemFilterState so it composes with the category filter the popup
    // installs — both feed a single OverrideFilter on ItemsListUI.
    internal static class InventorySearchBar
    {
        private static GameObject _root;
        private static TMP_InputField _input;
        private static VItemsDefaultUI _attachedTo;
        private static SearchDebouncer _debouncer;

        public static bool IsFocused => _input != null && _input.isFocused;

        public static void Ensure(VItemsDefaultUI view)
        {
            try
            {
                if (view == null) return;
                if (_attachedTo == view && _root != null && _input != null) return;

                Destroy();

                var titleParent = view.titleParent;
                if (titleParent == null)
                {
                    Plugin.Log.LogWarning("[BetterSorting] inventory titleParent is null — bag search disabled");
                    return;
                }

                var font = FindAnyTmpFont();
                if (font == null)
                {
                    Plugin.Log.LogWarning("[BetterSorting] no TMP font found — bag search bar disabled");
                    return;
                }

                _attachedTo = view;
                _root = Build(titleParent.transform, font);
                if (_root != null)
                {
                    _root.transform.SetAsLastSibling();
                    Plugin.Log.LogInfo($"[BetterSorting] InventorySearchBar built under '{titleParent.name}'");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] InventorySearchBar.Ensure failed: {e.GetBaseException()}");
            }
        }

        public static void Destroy()
        {
            try
            {
                if (_root != null) UnityEngine.Object.Destroy(_root);
            }
            catch { }
            _root = null;
            _input = null;
            _attachedTo = null;
            _debouncer = null;
        }

        private static UnityEngine.TextCore.Text.FontAsset FindAnyTmpFont()
        {
            var anyText = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
            if (anyText != null && anyText.font != null) return anyText.font;
            var all = Resources.FindObjectsOfTypeAll<UnityEngine.TextCore.Text.FontAsset>();
            return all.Length > 0 ? all[0] : null;
        }

        private static GameObject Build(Transform parent, UnityEngine.TextCore.Text.FontAsset font)
        {
            var root = new GameObject("BetterSortingBagSearch", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            var rt = (RectTransform)root.transform;
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-12f, 0f);
            rt.sizeDelta = new Vector2(260f, 30f);
            rt.localScale = Vector3.one;

            var cg = root.GetComponent<CanvasGroup>();
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;

            var bg = root.GetComponent<Image>();
            bg.color = new Color(0.06f, 0.06f, 0.07f, 0.92f);

            // Standard TMP_InputField hierarchy (same as crafting bar).
            var inputGo = new GameObject("Input", typeof(RectTransform), typeof(Image));
            var inputRt = (RectTransform)inputGo.transform;
            inputRt.SetParent(rt, worldPositionStays: false);
            inputRt.anchorMin = Vector2.zero;
            inputRt.anchorMax = Vector2.one;
            inputRt.offsetMin = Vector2.zero;
            inputRt.offsetMax = Vector2.zero;
            var inputImg = inputGo.GetComponent<Image>();
            inputImg.color = new Color(0f, 0f, 0f, 0f);
            inputImg.raycastTarget = true;

            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var areaRt = (RectTransform)area.transform;
            areaRt.SetParent(inputRt, worldPositionStays: false);
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(8f, 4f);
            areaRt.offsetMax = new Vector2(-8f, -4f);

            var placeholder = MakeTmp("Placeholder", areaRt, font, "Search items...", 14, italic: true,
                color: new Color(1f, 1f, 1f, 0.45f));
            var textComp = MakeTmp("Text", areaRt, font, string.Empty, 14, italic: false,
                color: Color.white);

            var input = inputGo.AddComponent<TMP_InputField>();
            input.textViewport = areaRt;
            input.textComponent = textComp;
            input.placeholder = placeholder;
            input.fontAsset = font;
            input.pointSize = 14;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.targetGraphic = inputImg;
            input.text = string.Empty;
            input.caretColor = Color.white;
            input.caretWidth = 2;
            input.customCaretColor = true;
            input.selectionColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
            input.onValueChanged.AddListener(OnSearchChanged);
            input.onSubmit.AddListener(OnSearchSubmit);

            // Custom blinking caret — TMP's internal caret doesn't render
            // reliably in this game; mirror the crafting bar's solution.
            var caretGo = new GameObject("CustomCaret", typeof(RectTransform), typeof(Image));
            caretGo.transform.SetParent(areaRt, worldPositionStays: false);
            var caretRt = (RectTransform)caretGo.transform;
            caretRt.anchorMin = new Vector2(0f, 0f);
            caretRt.anchorMax = new Vector2(0f, 1f);
            caretRt.pivot = new Vector2(0f, 0.5f);
            caretRt.sizeDelta = new Vector2(2f, -4f);
            caretRt.anchoredPosition = Vector2.zero;
            var caretImg = caretGo.GetComponent<Image>();
            caretImg.color = new Color(1f, 1f, 1f, 0f);
            caretImg.raycastTarget = false;
            var blinker = caretGo.AddComponent<CustomCaretBlinker>();
            if (blinker != null)
            {
                blinker.Input = input;
                blinker.CaretImage = caretImg;
                blinker.CaretRt = caretRt;
                blinker.TextRt = (RectTransform)textComp.transform;
            }

            _debouncer = root.AddComponent<SearchDebouncer>();
            _input = input;
            return root;
        }

        private static TMP_Text MakeTmp(string name, RectTransform parent, UnityEngine.TextCore.Text.FontAsset font,
                                        string text, int size, bool italic, Color color)
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
            tmp.fontSize = size;
            tmp.text = text;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            if (italic) tmp.fontStyle = FontStyles.Italic;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void OnSearchChanged(string s)
        {
            ItemFilterState.SearchText = s ?? string.Empty;
            // Debouncer (added to root) coalesces fast typing into one filter
            // refresh per ~180ms — same path the crafting bar uses, but the
            // applied action is ApplyFilter rather than RefreshGrid.
            if (_debouncer != null) _debouncer.ScheduleAction(ApplyFilter);
            else ApplyFilter();
        }

        private static void OnSearchSubmit(string s)
        {
            ItemFilterState.SearchText = s ?? string.Empty;
            if (_debouncer != null) _debouncer.FireNowAction(ApplyFilter);
            else ApplyFilter();
        }

        // Resolve the active list and delegate filtering to ItemFilterState
        // so the search text composes with any category filter the popup
        // installed.
        public static void ApplyFilter()
        {
            try
            {
                if (_attachedTo == null) return;
                var itemsUI = ((View)_attachedTo).GenericTarget as ItemsUI;
                var listUI = itemsUI?.ItemsListUI;
                if (listUI == null) return;
                ItemFilterState.Apply(listUI);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] bag-search apply failed: {e.GetBaseException().Message}");
            }
        }

        // Sync the search input's visible text from ItemFilterState — used
        // when the filter popup resets state (e.g. on a fresh OnAttach).
        public static void SyncFromState()
        {
            try
            {
                if (_input == null) return;
                if (_input.text != ItemFilterState.SearchText)
                    _input.SetTextWithoutNotify(ItemFilterState.SearchText ?? string.Empty);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] bag-search sync failed: {e.GetBaseException().Message}");
            }
        }
    }

    // Postfix on VItemsDefaultUI.OnMount — runs every time the bag panel mounts.
    [HarmonyLib.HarmonyPatch(typeof(Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.VItemsDefaultUI), "OnMount")]
    internal static class VItemsDefaultUI_OnMount_Patch
    {
        static void Postfix(Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.VItemsDefaultUI __instance)
        {
            try { InventorySearchBar.Ensure(__instance); }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] bag-search OnMount postfix failed: {e.GetBaseException().Message}");
            }
        }
    }
}
