using System;
using System.Collections;
using Awaken.TG.Main.Utility.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BetterSortingCrafting
{
    // Sits on the ARButton container and forwards a pointer click to the
    // TMP_InputField's ActivateInputField. We can't subscribe to
    // ARButton.OnClick directly because the publicizer exposes both a
    // public field and event with the same name (CS0229 ambiguity), so
    // routing via IPointerClickHandler is the cleanest workaround. Unity's
    // standard EventSystem dispatches IPointerClickHandlers on all
    // components of a clicked GameObject, so this fires alongside ARButton's
    // built-in click handling without conflicting.
    internal class ActivateInputOnClick : MonoBehaviour, IPointerClickHandler
    {
        public TMP_InputField Target;

        public void OnPointerClick(PointerEventData _)
        {
            try { Target?.ActivateInputField(); }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] activate input failed: {e.GetBaseException().Message}");
            }
        }
    }

    // Sits next to a TMP_InputField and:
    //   1. Tracks focus, blocking the game's Rewired keyboard input while
    //      the field is selected so typing letters doesn't also fire game
    //      keybinds (this was the missing piece that made our v1 search bar
    //      crash/feel buggy).
    //   2. Debounces text changes to avoid running the (potentially expensive)
    //      filter+refresh on every keystroke.
    //
    // A static counter aggregates focus across multiple search bars (inventory
    // + crafting) so AnyFocused works correctly when both are present.
    internal class SearchInputFocusBlocker : MonoBehaviour
    {
        private const float DebounceSeconds = 0.2f;

        // Used by the PlayerInput patches to short-circuit input dispatch
        // while any search field is focused.
        private static int _focusedCount;
        public static bool AnyFocused => _focusedCount > 0;

        private TMP_InputField _input;
        private Action<string> _onSearchChanged;
        private bool _wasFocused;
        private Coroutine _debounce;
        private string _pendingText;

        public void Initialize(TMP_InputField input, Action<string> onSearchChanged)
        {
            _input = input;
            _onSearchChanged = onSearchChanged;
        }

        // Call from TMP_InputField.onValueChanged. Keeps the latest text and
        // restarts the debounce timer.
        public void OnSearchTextChanged(string text)
        {
            _pendingText = text ?? string.Empty;
            if (_debounce != null) StopCoroutine(_debounce);
            _debounce = StartCoroutine(DebounceCo());
        }

        private IEnumerator DebounceCo()
        {
            yield return new WaitForSecondsRealtime(DebounceSeconds);
            _debounce = null;
            try { _onSearchChanged?.Invoke(_pendingText ?? string.Empty); }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] search apply failed: {e.GetBaseException().Message}");
            }
        }

        private void Update()
        {
            if (_input == null) return;
            bool focused = _input.isFocused;
            if (focused == _wasFocused) return;

            _wasFocused = focused;
            if (focused)
            {
                _focusedCount++;
                try { RewiredHelper.BlockKeyboardInput(); } catch { }
            }
            else
            {
                _focusedCount = Mathf.Max(0, _focusedCount - 1);
                if (_focusedCount == 0)
                {
                    try { RewiredHelper.EnableKeyboardInput(); } catch { }
                }
            }
        }

        private void OnDisable()
        {
            // If we got disabled while focused (eg. screen closed mid-typing)
            // we still need to release the keyboard block.
            if (_wasFocused)
            {
                _focusedCount = Mathf.Max(0, _focusedCount - 1);
                if (_focusedCount == 0)
                {
                    try { RewiredHelper.EnableKeyboardInput(); } catch { }
                }
                _wasFocused = false;
            }
            if (_debounce != null)
            {
                StopCoroutine(_debounce);
                _debounce = null;
            }
        }
    }
}
