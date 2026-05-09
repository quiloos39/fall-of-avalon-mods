using System;
using Awaken.TG.Main.Crafting.HandCrafting.RecipeView;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterSortingCrafting
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
        private static SearchDebouncer _debouncer;

        // True while the user has the search bar focused. PlayerInput patches
        // read this to suppress the game's keyboard/registered-input handling
        // so typing letters doesn't fire hotkeys.
        public static bool IsFocused => _input != null && _input.isFocused;

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
                    Plugin.Log.LogWarning("[BetterSorting] no TMP font found — search bar disabled");
                    return;
                }

                // Parent inside the crafting view's own UI tree, next to
                // PrimaryFontHeader (the "Available recipes" title). This
                // gives us the flex-row visual: title on the left, our bar
                // anchored to the right of the same row. Falls back to a
                // standalone overlay canvas only if the in-game parent
                // can't be located.
                Transform parent = ResolveCraftingHeaderParent(ctx);
                bool inGameParent = parent != null;
                if (parent == null) parent = EnsureOwnCanvas().transform;

                _attachedTo = ctx;
                _root = Build(parent, font);
                if (_root != null)
                {
                    _root.transform.SetAsLastSibling();

                    // AddComponent<T>() can return null when T comes from an
                    // assembly loaded via Assembly.Load(byte[]) (which is what
                    // /eval does). The bar is now parented inside the game's
                    // Content node, so its lifecycle follows the parent — when
                    // crafting closes, Content + our bar both die naturally.
                    // Watcher is only useful for the standalone-canvas fallback,
                    // and a null result is no longer fatal.
                    var watcher = _root.AddComponent<SearchBarLifetimeWatcher>();
                    if (watcher != null) watcher.Ctx = ctx;

                    Plugin.Log.LogInfo(
                        $"[BetterSorting] SearchBar built (inGameParent={inGameParent}, parent='{(parent != null ? parent.name : "<null>")}', watcher={(watcher != null ? "ok" : "null")}, activeInHierarchy={_root.activeInHierarchy})");
                }
                SyncText();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] SearchBar.Ensure failed: {e.GetBaseException()}");
            }
        }

        // Walk from the VCRecipeSorting up through the MVC view chain to
        // VRecipeGridUI/Content — the "row" that holds PrimaryFontHeader
        // (the title) and List (the recipe grid). That's where we want the
        // bar to live so it sits inline with the title.
        private static Transform ResolveCraftingHeaderParent(VCRecipeSorting ctx)
        {
            try
            {
                var grid = ctx?.Target?.RecipeGridUI;
                if (grid == null) { Plugin.Log.LogWarning("[BetterSorting] resolve: grid == null"); return null; }
                var gridView = grid.View<Awaken.TG.Main.Crafting.HandCrafting.RecipeView.VRecipeGridUI>();
                if (gridView == null) { Plugin.Log.LogWarning("[BetterSorting] resolve: VRecipeGridUI view not found"); return null; }
                var content = gridView.transform.Find("Content");
                if (content == null) Plugin.Log.LogWarning($"[BetterSorting] resolve: 'Content' not found under '{gridView.name}'");
                return content;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[BetterSorting] resolve threw: {e.GetBaseException().Message}");
                return null;
            }
        }

        private static GameObject _ownCanvasGo;
        private static Canvas EnsureOwnCanvas()
        {
            if (_ownCanvasGo != null)
            {
                var existing = _ownCanvasGo.GetComponent<Canvas>();
                if (existing != null) return existing;
            }
            _ownCanvasGo = new GameObject(
                "BetterSortingCraftingCanvas",
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_ownCanvasGo);
            var canvas = _ownCanvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = _ownCanvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
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
            _debouncer = null;
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
            // Anchor to the top-right of the parent rect (VRecipeGridUI/Content),
            // mirroring BetterUI's pattern: title left-aligned, search right-aligned,
            // both in the same horizontal row. Pivot top-right + small padding off
            // the right edge.
            var root = new GameObject("BetterSortingSearch", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            var rt = (RectTransform)root.transform;
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-8f, -2f);     // 8px from right, 2px from top
            rt.sizeDelta = new Vector2(260f, 30f);
            rt.localScale = Vector3.one;

            var cg = root.GetComponent<CanvasGroup>();
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;

            var bg = root.GetComponent<Image>();
            bg.color = new Color(0.06f, 0.06f, 0.07f, 0.92f);

            // Standard Unity TMP_InputField hierarchy:
            //   root
            //     Input (TMP_InputField + transparent Image for raycast)
            //       Text Area (RectMask2D)
            //         Placeholder (TMP)
            //         Text (TMP)
            // The caret GameObject TMP_InputField creates at runtime is parented
            // to the Input transform; positioning math expects Text Area to
            // be a child of Input. Sibling layout left the caret invisible.
            var inputGo = new GameObject("Input", typeof(RectTransform), typeof(Image));
            var inputRt = (RectTransform)inputGo.transform;
            inputRt.SetParent(rt, worldPositionStays: false);
            inputRt.anchorMin = Vector2.zero;
            inputRt.anchorMax = Vector2.one;
            inputRt.offsetMin = Vector2.zero;
            inputRt.offsetMax = Vector2.zero;
            var inputImg = inputGo.GetComponent<Image>();
            inputImg.color = new Color(0f, 0f, 0f, 0f);     // fully transparent — only here for raycasts
            inputImg.raycastTarget = true;

            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var areaRt = (RectTransform)area.transform;
            areaRt.SetParent(inputRt, worldPositionStays: false);
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(8f, 4f);
            areaRt.offsetMax = new Vector2(-8f, -4f);

            var placeholder = MakeTmp("Placeholder", areaRt, font, "Search recipes...", 14, italic: true,
                color: new Color(1f, 1f, 1f, 0.45f));
            var textComp    = MakeTmp("Text",        areaRt, font, string.Empty,            14, italic: false,
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
            input.text = FilterState.SearchText ?? string.Empty;
            input.caretColor = Color.white;
            input.caretWidth = 2;
            input.customCaretColor = true;
            input.selectionColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
            input.onValueChanged.AddListener(OnSearchChanged);
            input.onSubmit.AddListener(OnSearchSubmit);

            // Custom blinking caret. TMP_InputField's built-in caret never
            // got created on focus in this game's UI environment (TMP
            // CaretGameObject creation path appears to no-op silently). We
            // draw our own — a 2-px-wide Image child of Text Area, blinked
            // by CustomCaretBlinker, positioned at preferredWidth of the
            // typed text.
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
            FilterState.SearchText = s ?? string.Empty;
            // Debounce: each keystroke rebuilds every recipe slot (force=true,
            // contentsChanged=true), so per-keystroke refresh stutters badly on
            // large tabs. Schedule one refresh after typing pauses.
            if (_debouncer != null) _debouncer.Schedule();
            else VCRecipeSorting_OnAttach_Patch.RefreshGrid();
        }

        private static void OnSearchSubmit(string s)
        {
            FilterState.SearchText = s ?? string.Empty;
            if (_debouncer != null) _debouncer.FireNow();
            else VCRecipeSorting_OnAttach_Patch.RefreshGrid();
        }
    }

    // While any of our input fields are focused, suppress the game's Rewired
    // keyboard input. Without this, typing letters while the search bar is
    // focused fires hotkeys (I = inventory toggle, etc.) before TMP_InputField
    // has a chance to consume them. Mirrors BetterUI's pattern.
    internal static class RewiredInputBlocker
    {
        private static bool _blocked;

        public static void Tick()
        {
            bool shouldBlock = SearchBar.IsFocused || InventorySearchBar.IsFocused;
            if (shouldBlock == _blocked) return;
            _blocked = shouldBlock;
            try
            {
                if (shouldBlock) Awaken.TG.Main.Utility.UI.RewiredHelper.BlockKeyboardInput();
                else Awaken.TG.Main.Utility.UI.RewiredHelper.EnableKeyboardInput();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[BetterSorting] Rewired toggle failed: {e.GetBaseException().Message}");
            }
        }
    }

    // Custom caret because TMP_InputField's internal caret rendering doesn't
    // produce a visible cursor in this UI environment. Toggles a 2-px-wide
    // Image's alpha at TMP's standard blink rate (0.85s period) and
    // repositions it after the typed text's last character.
    internal class CustomCaretBlinker : UnityEngine.MonoBehaviour
    {
        public TMPro.TMP_InputField Input;
        public UnityEngine.UI.Image CaretImage;
        public RectTransform CaretRt;
        public RectTransform TextRt;

        private float _t;

        private void Update()
        {
            // Toggle Rewired input blocking based on whether any of our search
            // bars are focused. Cheap (a single bool comparison when state
            // hasn't changed) — fine to call every frame.
            RewiredInputBlocker.Tick();

            if (Input == null || CaretImage == null) return;

            if (!Input.isFocused)
            {
                if (CaretImage.color.a > 0f)
                    CaretImage.color = new UnityEngine.Color(1f, 1f, 1f, 0f);
                _t = 0f;
                return;
            }

            _t += UnityEngine.Time.unscaledDeltaTime;
            float a = ((_t % 0.85f) < 0.425f) ? 1f : 0f;
            if (CaretImage.color.a != a)
                CaretImage.color = new UnityEngine.Color(1f, 1f, 1f, a);

            var tc = Input.textComponent;
            if (tc != null && CaretRt != null)
            {
                CaretRt.anchoredPosition = new UnityEngine.Vector2(tc.preferredWidth + 1f, 0f);
            }
        }
    }

    // Bar lives on a Canvas that outlives the crafting screen. This watcher
    // destroys the bar when its associated VCRecipeSorting goes away (crafting
    // closed or tab swapped) so it doesn't linger over the HUD.
    internal class SearchBarLifetimeWatcher : UnityEngine.MonoBehaviour
    {
        public VCRecipeSorting Ctx;

        private void Update()
        {
            if (Ctx == null || !Ctx.gameObject || !Ctx.gameObject.activeInHierarchy)
            {
                UnityEngine.Object.Destroy(gameObject);
            }
        }
    }

    // Coalesces rapid OnSearchChanged calls into a single fire ~180ms after
    // the last keystroke. Lives on the SearchBar root, so it dies with the
    // GameObject. The action to fire is supplied per-call by the caller —
    // crafting bar runs RefreshGrid, bag bar runs ApplyFilter, etc.
    internal class SearchDebouncer : UnityEngine.MonoBehaviour
    {
        private const float DelaySeconds = 0.18f;
        private float _fireAt = -1f;
        private System.Action _pending;

        // Crafting bar's legacy entry point — defaults the action to RefreshGrid.
        public void Schedule() => ScheduleAction(VCRecipeSorting_OnAttach_Patch.RefreshGrid);

        public void FireNow() => FireNowAction(VCRecipeSorting_OnAttach_Patch.RefreshGrid);

        public void ScheduleAction(System.Action action)
        {
            _pending = action;
            _fireAt = UnityEngine.Time.unscaledTime + DelaySeconds;
        }

        public void FireNowAction(System.Action action)
        {
            _pending = null;
            _fireAt = -1f;
            try { action?.Invoke(); }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] debounced fire-now failed: {e.GetBaseException().Message}");
            }
        }

        private void Update()
        {
            if (_fireAt < 0f) return;
            if (UnityEngine.Time.unscaledTime < _fireAt) return;
            _fireAt = -1f;
            var action = _pending;
            _pending = null;
            try { action?.Invoke(); }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] debounced fire failed: {e.GetBaseException().Message}");
            }
        }
    }
}
