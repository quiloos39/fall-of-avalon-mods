using System;
using Awaken.TG.Main.Crafting.HandCrafting.RecipeView;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CraftingFilter
{
    // A persistent TMP_InputField that floats above the sort prompt of the
    // active VCRecipeSorting. Built from scratch (no prefab) using a TMP font
    // discovered at runtime from any existing TMP_Text in the scene — which
    // there are plenty of in the crafting screen.
    internal static class SearchBar
    {
        private static GameObject _root;
        private static TMP_InputField _input;
        private static VCRecipeSorting _attachedTo;

        public static void Ensure(VCRecipeSorting ctx)
        {
            try
            {
                if (ctx == null) return;
                if (_attachedTo == ctx && _root != null && _input != null) return;

                Destroy();

                var font = FindAnyTmpFont();
                if (font == null)
                {
                    Plugin.Log.LogWarning("[CraftingFilter] no TMP font found — search bar disabled");
                    return;
                }

                _attachedTo = ctx;
                _root = Build(ctx.transform, font);
                SyncText();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[CraftingFilter] SearchBar.Ensure failed: {e.GetBaseException()}");
            }
        }

        public static void RequestFocus()
        {
            if (_input == null) return;
            try
            {
                EventSystem.current?.SetSelectedGameObject(_input.gameObject);
                _input.ActivateInputField();
                _input.Select();
            }
            catch { }
        }

        public static void SyncText()
        {
            if (_input == null) return;
            // Avoid feedback loop with onValueChanged.
            if (_input.text != FilterState.SearchText)
                _input.SetTextWithoutNotify(FilterState.SearchText ?? string.Empty);
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
        }

        private static UnityEngine.TextCore.Text.FontAsset FindAnyTmpFont()
        {
            // Cheap path: any active TMP_Text on screen will have a font.
            var anyText = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
            if (anyText != null && anyText.font != null) return anyText.font;
            // Fallback: search inactive too.
            var all = Resources.FindObjectsOfTypeAll<UnityEngine.TextCore.Text.FontAsset>();
            return all.Length > 0 ? all[0] : null;
        }

        private static GameObject Build(Transform parent, UnityEngine.TextCore.Text.FontAsset font)
        {
            // Root container — sits above the sort prompt, anchored bottom-left.
            var root = new GameObject("CraftingFilterSearch", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)root.transform;
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(0f, 8f);
            rt.sizeDelta = new Vector2(280f, 32f);

            var bg = root.GetComponent<Image>();
            bg.color = new Color(0.06f, 0.06f, 0.07f, 0.85f);

            // Text Area (RectMask2D + content/placeholder).
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var areaRt = (RectTransform)area.transform;
            areaRt.SetParent(rt, worldPositionStays: false);
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(8f, 4f);
            areaRt.offsetMax = new Vector2(-8f, -4f);

            var placeholder = MakeTmp("Placeholder", areaRt, font, "Search recipes...", 14, italic: true,
                color: new Color(1f, 1f, 1f, 0.45f));
            var textComp    = MakeTmp("Text",        areaRt, font, string.Empty,            14, italic: false,
                color: Color.white);

            // The TMP_InputField needs a CanvasRenderer-backed text component.
            var inputGo = new GameObject("Input", typeof(RectTransform));
            var inputRt = (RectTransform)inputGo.transform;
            inputRt.SetParent(rt, worldPositionStays: false);
            inputRt.anchorMin = Vector2.zero;
            inputRt.anchorMax = Vector2.one;
            inputRt.offsetMin = Vector2.zero;
            inputRt.offsetMax = Vector2.zero;

            var input = inputGo.AddComponent<TMP_InputField>();
            input.textViewport = areaRt;
            input.textComponent = textComp;
            input.placeholder = placeholder;
            input.fontAsset = font;
            input.pointSize = 14;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.text = FilterState.SearchText ?? string.Empty;
            input.onValueChanged.AddListener(OnSearchChanged);

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
            FilterState.SearchText = s ?? string.Empty;
            VCRecipeSorting_OnAttach_Patch.RefreshGrid();
        }
    }
}
