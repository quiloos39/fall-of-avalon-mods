using System;
using Awaken.TG.MVC.UI.Handlers.States;
using BepInEx.Configuration;
using UnityEngine;

namespace ViewDistanceTuner
{
    // OnGUI tuning window. Toggled by Cfg.MenuHotkey (default F8). Cursor is unlocked while
    // the menu is open; restored when closed. The window is draggable; position is saved to
    // Cfg.MenuWindowX/Y. Each slider writes back to the live ConfigEntry and triggers the
    // appropriate Apply.* helper so changes take effect immediately.
    internal class Menu : MonoBehaviour
    {
        private const float WindowWidth = 500f;
        private const float WindowHeight = 720f;
        private const int WindowId = 0x4D454E55; // "MENU"

        // Static so Harmony patches in ViewDistancePatches.cs can gate game input cheaply.
        internal static bool IsOpen { get; private set; }

        private bool _open;
        private Rect _windowRect;
        private Vector2 _scroll;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _descStyle;
        private GUIStyle _windowStyle;
        private GUIStyle _buttonStyle;
        private Texture2D _bgDark;
        private Texture2D _bgPanel;
        private Texture2D _bgButton;
        private Texture2D _bgButtonHover;
        private bool _stylesReady;

        // Saved cursor state at the moment the menu was opened — restored on close.
        private CursorLockMode _savedLockState;
        private bool _savedCursorVisible;

        // The UIState we push onto UIStateStack while open. UIState.Cursor has
        // MapInteractive=false, which makes PlayerInput.ProcessLateUpdate zero LookInput /
        // MoveInput / MountMoveInput, freezing the camera and player. The game's cursor
        // service also reads IsMapInteractive and naturally shows the cursor for us.
        private UIState _pushedState;

        private void Awake()
        {
            var cfg = Plugin.Cfg;
            _windowRect = new Rect(cfg.MenuWindowX.Value, cfg.MenuWindowY.Value, WindowWidth, WindowHeight);
        }

        private void Update()
        {
            var cfg = Plugin.Cfg;
            if (cfg == null) return;
            var key = cfg.MenuHotkey.Value;
            if (key != KeyCode.None && Input.GetKeyDown(key))
            {
                Toggle();
            }

            // The game re-locks/hides the cursor every frame, so a one-shot Cursor.lockState
            // assignment at Toggle() gets stomped. Force-unlock each frame while the menu is
            // open so the user can actually click sliders without alt-tabbing or pressing ESC.
            if (_open)
            {
                if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
                if (!Cursor.visible) Cursor.visible = true;
            }
        }

        private void Toggle()
        {
            _open = !_open;
            IsOpen = _open;
            if (_open)
            {
                _savedLockState = Cursor.lockState;
                _savedCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                PushUIState();
                Plugin.Log?.LogDebug("[Menu] opened");
            }
            else
            {
                PopUIState();
                Cursor.lockState = _savedLockState;
                Cursor.visible = _savedCursorVisible;
                Plugin.Log?.LogDebug("[Menu] closed");
            }
        }

        // Push UIState.Cursor (MapInteractive=false) so PlayerInput.ProcessLateUpdate zeros
        // the look/move axes, freezing the player and camera. The game's Cursor service reads
        // the same state and shows the cursor on its own — no need to fight it. We use
        // UIStateStack.Instance as the owner because it always exists once the game is in-world.
        private void PushUIState()
        {
            try
            {
                var stack = UIStateStack.Instance;
                if (stack == null)
                {
                    Plugin.Log?.LogDebug("[Menu] PushUIState: UIStateStack.Instance null (likely title screen). Skipping.");
                    return;
                }
                _pushedState = UIState.Cursor;
                stack.PushState(_pushedState, stack);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"[Menu] PushUIState failed: {e.Message}");
                _pushedState = null;
            }
        }

        private void PopUIState()
        {
            if (_pushedState == null) return;
            try
            {
                UIStateStack.Instance?.RemoveState(_pushedState);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"[Menu] PopUIState failed: {e.Message}");
            }
            finally
            {
                _pushedState = null;
            }
        }

        private void OnDestroy()
        {
            if (_open)
            {
                // Pop our UIState + restore cursor state if the plugin gets unloaded mid-menu
                // (e.g., ScriptEngine reload). Otherwise the player would be input-frozen
                // until next scene transition.
                PopUIState();
                Cursor.lockState = _savedLockState;
                Cursor.visible = _savedCursorVisible;
                _open = false;
                IsOpen = false;
            }
            // Free dynamically-allocated textures so we don't leak across reloads.
            if (_bgDark != null) Destroy(_bgDark);
            if (_bgPanel != null) Destroy(_bgPanel);
            if (_bgButton != null) Destroy(_bgButton);
            if (_bgButtonHover != null) Destroy(_bgButtonHover);
        }

        // 1×1 textures used as solid backgrounds for window / button styles. Cheap; one each.
        private static Texture2D MakeColorTexture(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            // Solid-color backgrounds. Dark gray window with slight tint, mid-gray buttons that
            // brighten on hover so they're obviously clickable against the dark backdrop.
            _bgDark        = MakeColorTexture(new Color(0.10f, 0.10f, 0.12f, 0.96f));
            _bgPanel       = MakeColorTexture(new Color(0.15f, 0.15f, 0.18f, 1.00f));
            _bgButton      = MakeColorTexture(new Color(0.22f, 0.22f, 0.26f, 1.00f));
            _bgButtonHover = MakeColorTexture(new Color(0.32f, 0.32f, 0.38f, 1.00f));

            var textColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            var dimColor  = new Color(0.72f, 0.72f, 0.74f, 1f);
            var headColor = new Color(0.85f, 0.93f, 1.00f, 1f); // slight blue tint for section headers

            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                normal   = { background = _bgDark, textColor = textColor },
                onNormal = { background = _bgDark, textColor = textColor },
                focused  = { background = _bgDark, textColor = textColor },
                onFocused= { background = _bgDark, textColor = textColor },
                active   = { background = _bgDark, textColor = textColor },
                onActive = { background = _bgDark, textColor = textColor },
                hover    = { background = _bgDark, textColor = textColor },
                onHover  = { background = _bgDark, textColor = textColor },
                border = new RectOffset(8, 8, 24, 8),
                padding = new RectOffset(12, 12, 28, 12),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                normal   = { background = _bgButton,      textColor = textColor },
                hover    = { background = _bgButtonHover, textColor = Color.white },
                active   = { background = _bgButtonHover, textColor = Color.white },
                focused  = { background = _bgButton,      textColor = textColor },
                padding = new RectOffset(10, 10, 6, 6),
                fontStyle = FontStyle.Bold,
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 14,
                normal = { textColor = headColor },
            };
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = textColor },
            };
            _valueStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = textColor },
            };
            _descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = dimColor },
            };
            _stylesReady = true;
        }

        private void OnGUI()
        {
            if (!_open) return;
            EnsureStyles();
            _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow,
                $"ViewDistanceTuner v{Plugin.PluginVersion} — {Plugin.Cfg.MenuHotkey.Value} to close",
                _windowStyle);

            // Persist drag position when the user lets go of the mouse.
            if (Event.current.type == EventType.MouseUp)
            {
                Plugin.Cfg.MenuWindowX.Value = _windowRect.x;
                Plugin.Cfg.MenuWindowY.Value = _windowRect.y;
            }
        }

        private void DrawWindow(int id)
        {
            var cfg = Plugin.Cfg;
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(WindowWidth - 20), GUILayout.Height(WindowHeight - 50));

            GUILayout.Label("World detail", _headerStyle);
            DrawSlider(
                "Object detail at distance",
                "How soon faraway objects switch to lower-detail meshes. Higher = sharper trees/buildings/rocks from further away. 1.0 = vanilla Ultra; 2.0 doubles the high-detail range.",
                cfg.LodBiasMultiplier, 0.5f, 5f, "F2", Apply.RefreshLodBias);
            DrawSlider(
                "Distant object visibility",
                "Pushes out the cutoff where the engine stops drawing per-layer scenery (props, lights, decorations). Higher = fewer 'objects appear as you walk' surprises.",
                cfg.LayerCullDistanceMultiplier, 0.5f, 10f, "F2", Apply.RefreshCameras);
            DrawSlider(
                "Absolute view distance (meters, 0 = off)",
                "Raw far-clip override for the world camera. Leave at 0 unless you can see a hard cutoff at the horizon — then bump to 10000+. Most users won't need this.",
                cfg.CameraFarClipPlane, 0f, 100000f, "F0", Apply.RefreshCameras);

            GUILayout.Space(8f);
            GUILayout.Label("Shadows", _headerStyle);
            DrawSlider(
                "Shadow draw distance",
                "How far shadows from sun/moon are rendered. Higher = distant trees and cliffs cast shadows instead of looking flat-lit. Costs GPU.",
                cfg.ShadowDistanceMultiplier, 0.5f, 10f, "F2", Apply.RefreshShadows);

            GUILayout.Space(8f);
            GUILayout.Label("Fog & atmosphere", _headerStyle);
            DrawSlider(
                "Fog visibility (higher = clearer)",
                "How far you can see through volumetric fog. 1.0 = vanilla moody fog, 2.0 = much clearer distance, 5.0+ removes most fog. Distant ridges become visible.",
                cfg.FogMeanFreePathMultiplier, 0.1f, 20f, "F2", null);
            DrawSlider(
                "Fog ceiling height (+meters)",
                "Lifts the top of the fog layer. Use if mountain peaks look hazy when you look up at them. 0 = no change.",
                cfg.FogMaximumHeightAdditive, 0f, 5000f, "F0", null);

            GUILayout.Space(8f);
            GUILayout.Label("Vegetation & trees", _headerStyle);
            DrawSlider(
                "Grass / foliage draw distance",
                "How far grass blades, plants, and small foliage spawn into view. The biggest fix for grass and bushes 'growing in' as you walk. 1.0 = vanilla preset.",
                cfg.VegetationSpawnDistanceMultiplier, 0.5f, 5f, "F2", null);
            DrawSlider(
                "3D tree distance (vs flat billboards)",
                "How far trees stay as full 3D meshes before switching to flat 2D imposters. Higher = no 'cardboard tree' look at mid-far distance.",
                cfg.VegetationBillboardDistanceMultiplier, 0.5f, 5f, "F2", null);

            GUILayout.Space(8f);
            GUILayout.Label("Props & details", _headerStyle);
            DrawSlider(
                "Prop draw distance (rocks/crates/barrels)",
                "Pushes out all 9 cull tiers for mid-size props. Fixes the classic 'object appears out of thin air' effect when approaching a camp or village.",
                cfg.DistanceCullerRangeMultiplier, 0.5f, 5f, "F2", Apply.RefreshDistanceCuller);

            GUILayout.Space(12f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset to defaults", _buttonStyle))
            {
                ResetToDefaults();
            }
            GUILayout.Space(8f);
            if (GUILayout.Button("Close", _buttonStyle))
            {
                Toggle();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Tip: drag the title bar to move. Settings save automatically and apply live. Higher values = better visuals but more GPU work.", _descStyle);

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, WindowWidth, 20));
        }

        // Title (bold) + value readout, slider, then a smaller muted description line.
        // Calls onChange only when the slider actually moves so we don't spam setting-refresh
        // events every frame.
        private void DrawSlider(string title, string description, ConfigEntry<float> entry, float min, float max, string fmt, Action onChange)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, _labelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(entry.Value.ToString(fmt), _valueStyle, GUILayout.Width(70));
            GUILayout.EndHorizontal();
            float old = entry.Value;
            float val = GUILayout.HorizontalSlider(entry.Value, min, max);
            if (Mathf.Abs(val - old) > 1e-5f)
            {
                entry.Value = val;
                onChange?.Invoke();
            }
            GUILayout.Label(description, _descStyle);
            GUILayout.Space(4f);
        }

        private static void ResetToDefaults()
        {
            var cfg = Plugin.Cfg;
            cfg.LodBiasMultiplier.Value = 1.5f;
            cfg.ShadowDistanceMultiplier.Value = 2.0f;
            cfg.LayerCullDistanceMultiplier.Value = 2.0f;
            cfg.CameraFarClipPlane.Value = 0f;
            cfg.FogMeanFreePathMultiplier.Value = 2.0f;
            cfg.FogMaximumHeightAdditive.Value = 0f;
            cfg.VegetationSpawnDistanceMultiplier.Value = 1.5f;
            cfg.VegetationBillboardDistanceMultiplier.Value = 2.0f;
            cfg.DistanceCullerRangeMultiplier.Value = 2.0f;
            Apply.RefreshAll();
        }
    }
}
