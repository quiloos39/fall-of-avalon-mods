using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.List;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.Tabs;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.UI.Components;

namespace BetterSortingCrafting
{
    // Injects a search bar into the bag's "Bag: <type>" header. Filtering
    // goes through the game's official ItemsListUI.OverrideFilter pipeline
    // (an ItemsTabType whose Contains predicate matches the search). After
    // each Refresh the override is auto-cleared by the game, so the postfix
    // patch below re-applies it — this is what gives "tab persistence":
    // switching tabs preserves the active search.
    internal static class InventorySearchInjector
    {
        // Active search bars. Keyed by view so multiple inventory screens
        // (rare in this game, but defensive) don't stomp each other.
        private static readonly Dictionary<VItemsDefaultUI, TMP_InputField> _fields  = new Dictionary<VItemsDefaultUI, TMP_InputField>();
        private static readonly Dictionary<VItemsDefaultUI, string>         _texts   = new Dictionary<VItemsDefaultUI, string>();

        // Reentrancy guard — the postfix on Refresh applies the filter and
        // calls Refresh again, which would recurse without this.
        private static bool _isReapplying;

        public static void Inject(VItemsDefaultUI view)
        {
            if (view == null || view.titleParent == null) return;

            // Hot-reload + repeated OnMount safety: nuke any existing search
            // containers under titleParent before adding a fresh one.
            ClearOldContainers(view.titleParent.transform);
            _fields.Remove(view);
            _texts.Remove(view);

            try
            {
                var input = Build(view);
                if (input == null) return;
                _fields[view] = input;
                _texts[view]  = string.Empty;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] inventory search inject failed: {e.GetBaseException()}");
            }
        }

        private static TMP_InputField Build(VItemsDefaultUI view)
        {
            // Container — anchored top-right of titleParent, sits next to "Bag:"
            var container = new GameObject("BetterSorting_InventorySearch",
                typeof(RectTransform), typeof(Image));
            container.transform.SetParent(view.titleParent.transform, worldPositionStays: false);

            var rt = (RectTransform)container.transform;
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-20f, 0f);
            rt.sizeDelta = new Vector2(220f, 32f);

            var bg = container.GetComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

            // ARButton on the container — the game's native button. Its hover
            // state plus our custom click handling on TMP_InputField below give
            // the bar correct visual feedback and make clicks land on a
            // Selectable the game's input dispatcher recognises.
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

            // Input field GameObject — child of container so the ARButton
            // remains the click hit-target while the input field handles
            // keystrokes once selected.
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

            var font = view.titleLabel != null ? view.titleLabel.font : FindAnyTmpFont();
            if (font == null)
            {
                Plugin.Log.LogWarning("[BetterSorting] no TMP font available for inventory search bar");
                return null;
            }

            var placeholder = MakeTmp("Placeholder", areaRt, font, "Search items...", italic: true,
                color: new Color(0.5f, 0.5f, 0.5f, 0.8f));
            var textComp    = MakeTmp("Text",        areaRt, font, string.Empty,        italic: false,
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

            // Pointer-click on the container → activate input. See
            // ActivateInputOnClick for why this isn't ARButton.OnClick.
            var clickForwarder = container.AddComponent<ActivateInputOnClick>();
            clickForwarder.Target = input;

            var blocker = container.AddComponent<SearchInputFocusBlocker>();
            blocker.Initialize(input, text => OnSearchTextChanged(view, text));
            input.onValueChanged.AddListener(text => blocker.OnSearchTextChanged(text));

            return input;
        }

        private static void OnSearchTextChanged(VItemsDefaultUI view, string text)
        {
            _texts[view] = text ?? string.Empty;
            ApplyFilter(view, _texts[view]);
        }

        public static void ApplyFilter(VItemsDefaultUI view, string searchText)
        {
            try
            {
                var itemsUI = ResolveItemsUI(view);
                var list = itemsUI?.ItemsListUI;
                if (list == null) return;

                if (string.IsNullOrWhiteSpace(searchText))
                    list.OverrideFilter(null);
                else
                    list.OverrideFilter(BuildSearchTab(searchText));

                list.Refresh();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] inventory ApplyFilter failed: {e.GetBaseException().Message}");
            }
        }

        // After every Refresh the game nulls _filterOverride. Re-apply ours
        // if a search is active so filter persists across tab switches.
        public static void ReapplyAfterRefresh(ItemsListUI list)
        {
            if (_isReapplying) return;
            try
            {
                var view = ResolveView(list);
                if (view == null) return;
                if (!_texts.TryGetValue(view, out var text) || string.IsNullOrWhiteSpace(text)) return;

                _isReapplying = true;
                list.OverrideFilter(BuildSearchTab(text));
                list.Refresh();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] inventory reapply failed: {e.GetBaseException().Message}");
            }
            finally
            {
                _isReapplying = false;
            }
        }

        private static ItemsTabType BuildSearchTab(string searchText)
        {
            return new ItemsTabType(
                "BetterSorting_Search",
                (Item item) => MatchesSearch(item, searchText),
                titleID: string.Empty,
                subTabs: null,
                gridException: null);
        }

        private static bool MatchesSearch(Item item, string searchText)
        {
            if (item == null || string.IsNullOrEmpty(searchText)) return true;
            var name = item.DisplayName ?? string.Empty;
            if (name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            var desc = item.Template?.Description ?? string.Empty;
            if (desc.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static ItemsUI ResolveItemsUI(VItemsDefaultUI view)
        {
            try { return view?.GenericTarget as ItemsUI; }
            catch { return null; }
        }

        // Map an ItemsListUI back to the VItemsDefaultUI that owns it.
        private static VItemsDefaultUI ResolveView(ItemsListUI list)
        {
            try
            {
                var itemsUI = list?.ParentModel;
                if (itemsUI == null) return null;
                foreach (var pair in _fields)
                {
                    if (pair.Key == null) continue;
                    if (ResolveItemsUI(pair.Key) == itemsUI) return pair.Key;
                }
                return null;
            }
            catch { return null; }
        }

        private static void ClearOldContainers(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child != null && child.name.StartsWith("BetterSorting_InventorySearch"))
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

    // Hooks ------------------------------------------------------------------

    [HarmonyPatch(typeof(VItemsDefaultUI), "OnMount")]
    internal static class VItemsDefaultUI_OnMount_Patch
    {
        static void Postfix(VItemsDefaultUI __instance)
        {
            try { InventorySearchInjector.Inject(__instance); }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] OnMount inject failed: {e.GetBaseException().Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ItemsListUI), nameof(ItemsListUI.Refresh))]
    internal static class ItemsListUI_Refresh_Patch
    {
        static void Postfix(ItemsListUI __instance)
        {
            InventorySearchInjector.ReapplyAfterRefresh(__instance);
        }
    }
}
