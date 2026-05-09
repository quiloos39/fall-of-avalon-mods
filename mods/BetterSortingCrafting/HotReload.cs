using System;
using System.Linq;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BetterSortingCrafting
{
    // Entrypoint invoked by HttpProbe's /eval endpoint when hot-loading a new
    // build into the running game without a restart. Steps:
    //   1. Bind the new Plugin.Log so its messages appear in BepInEx LogOutput.
    //   2. UnpatchAll on PluginGuid — drops every Harmony patch owned by us
    //      (including ones from the previous assembly load).
    //   3. Destroy any GameObjects we created last time so the new code starts
    //      clean (search-bar canvas, search-bar root).
    //   4. PatchAll on this (new) assembly to wire up the latest patches.
    public static class HotReload
    {
        public static string Reload()
        {
            try
            {
                // Bind a fresh log source so logs from this hot-loaded assembly
                // land in LogOutput.log instead of falling through to Debug.Log.
                var src = BepInEx.Logging.Logger.CreateLogSource("BetterSortingCrafting[hot]");
                Plugin.Log.Bind(src);

                Plugin.Log.LogInfo("HotReload: starting reload");

                var harmony = new Harmony(Plugin.PluginGuid);
                harmony.UnpatchSelf();

                int destroyed = DestroyStaleGameObjects();
                Plugin.Log.LogInfo($"HotReload: unpatched old harmony, destroyed {destroyed} stale GameObject(s)");

                harmony.PatchAll(typeof(HotReload).Assembly);
                Plugin.Log.LogInfo("HotReload: applied patches from new assembly");

                // OnAttach already fired for any open VCRecipeSorting before our
                // new patches landed, so trigger SearchBar.Ensure manually for
                // every live instance — otherwise the bar won't show up until
                // the user closes/reopens crafting.
                int reattached = 0;
                foreach (var vc in Resources.FindObjectsOfTypeAll<Awaken.TG.Main.Crafting.HandCrafting.RecipeView.VCRecipeSorting>())
                {
                    if (vc == null) continue;
                    if (!vc.gameObject) continue;
                    if (!vc.gameObject.scene.IsValid()) continue;
                    if (!vc.gameObject.activeInHierarchy) continue;
                    try
                    {
                        var grid = vc.Target?.RecipeGridUI;
                        if (grid != null) FilterState.ActiveGrid = grid;
                        SearchBar.Ensure(vc);
                        reattached++;
                    }
                    catch (Exception inner)
                    {
                        Plugin.Log.LogWarning($"HotReload: re-ensure on {vc.name} failed: {inner.GetBaseException().Message}");
                    }
                }
                Plugin.Log.LogInfo($"HotReload: re-ensured SearchBar on {reattached} live VCRecipeSorting instance(s)");

                var sb = new StringBuilder();
                int n = 0;
                foreach (var m in Harmony.GetAllPatchedMethods())
                {
                    var info = Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    if (!info.Owners.Contains(Plugin.PluginGuid)) continue;
                    n++;
                    sb.AppendLine($"  {m.DeclaringType?.FullName}.{m.Name}");
                }
                Plugin.Log.LogInfo($"HotReload: {n} methods patched by {Plugin.PluginGuid}");
                return $"ok; {n} methods patched\n{sb}";
            }
            catch (Exception e)
            {
                var msg = $"HotReload failed: {e.GetBaseException()}";
                try { Plugin.Log.LogError(msg); } catch { }
                return msg;
            }
        }

        // Dumps the search bar's hierarchy + the input field's runtime state
        // (focus, caret config, etc.) so we can diagnose why the caret isn't
        // visible. Call this AFTER clicking on the bar to focus it.
        public static string DumpSearchBar()
        {
            try
            {
                var sb = new StringBuilder();
                // Scan by TMP_InputField too — our bar's input has a known
                // placeholder text. This catches it even if the GameObject
                // somehow got renamed or its parent moved.
                var allInputs = Resources.FindObjectsOfTypeAll<TMPro.TMP_InputField>();
                var sbInputs = new StringBuilder();
                sbInputs.AppendLine($"All TMP_InputField in scene: {allInputs.Length}");
                foreach (var i in allInputs)
                {
                    if (i == null) continue;
                    sbInputs.AppendLine($"  '{i.name}' parent='{(i.transform.parent != null ? i.transform.parent.name : "<root>")}' active={i.gameObject.activeInHierarchy} placeholder='{(i.placeholder != null ? (i.placeholder as TMPro.TMP_Text)?.text : "<null>")}'");
                }

                var allGos = Resources.FindObjectsOfTypeAll<GameObject>()
                    .Where(g => g != null && g.name == "BetterSortingSearch")
                    .ToList();
                var sb0 = new StringBuilder();
                sb0.AppendLine($"Found {allGos.Count} GameObject(s) named 'BetterSortingSearch':");
                foreach (var g in allGos)
                    sb0.AppendLine($"  hideFlags={g.hideFlags} sceneValid={g.scene.IsValid()} sceneName='{g.scene.name}' active={g.activeInHierarchy} parent='{(g.transform.parent != null ? g.transform.parent.name : "<root>")}'");

                var bar = allGos.FirstOrDefault(g => g.activeInHierarchy && g.scene.IsValid());
                if (bar == null) { sb0.AppendLine("(no usable instance)"); return sb0.ToString() + "\n" + sbInputs.ToString(); }
                var sb_pre = sb0.ToString() + "\n" + sbInputs.ToString();

                sb.Append(sb_pre);
                sb.AppendLine("=== Hierarchy ===");
                Walk(bar.transform, 0, sb);

                var input = bar.GetComponentInChildren<TMPro.TMP_InputField>(includeInactive: true);
                if (input != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("=== TMP_InputField state ===");
                    sb.AppendLine($"  isFocused={input.isFocused}");
                    sb.AppendLine($"  interactable={input.interactable}");
                    sb.AppendLine($"  readOnly={input.readOnly}");
                    sb.AppendLine($"  customCaretColor={input.customCaretColor} caretColor={input.caretColor}");
                    sb.AppendLine($"  caretWidth={input.caretWidth}  caretBlinkRate={input.caretBlinkRate}");
                    sb.AppendLine($"  selectionColor={input.selectionColor}");
                    sb.AppendLine($"  caretPosition={input.caretPosition} text='{input.text}'");
                    sb.AppendLine($"  shouldHideMobileInput={input.shouldHideMobileInput}");
                }

                sb.AppendLine();
                sb.AppendLine("=== EventSystem ===");
                var es = UnityEngine.EventSystems.EventSystem.current;
                sb.AppendLine($"  current={(es != null ? es.name : "<null>")}");
                if (es != null)
                {
                    sb.AppendLine($"  selectedGameObject='{(es.currentSelectedGameObject != null ? es.currentSelectedGameObject.name : "<null>")}'");
                    sb.AppendLine($"  sendNavigationEvents={es.sendNavigationEvents}");
                }

                return sb.ToString();
            }
            catch (Exception e)
            {
                return $"DumpSearchBar failed: {e.GetBaseException()}";
            }
        }

        // Force-focus the bar's input, then walk its full tree to find the
        // TMP-created "Caret" GameObject. If it exists, dump its rect/colors.
        // If it doesn't, TMP_InputField never created one on focus.
        public static string ProbeCaret()
        {
            try
            {
                var bar = Resources.FindObjectsOfTypeAll<GameObject>()
                    .FirstOrDefault(g => g != null && g.name == "BetterSortingSearch" && g.activeInHierarchy);
                if (bar == null) return "No active 'BetterSortingSearch' GameObject.";

                var input = bar.GetComponentInChildren<TMPro.TMP_InputField>(includeInactive: true);
                if (input == null) return "No TMP_InputField inside bar.";

                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(input.gameObject);
                input.ActivateInputField();
                input.Select();

                var sb = new StringBuilder();
                sb.AppendLine($"isFocused={input.isFocused}");
                sb.AppendLine($"caretPosition={input.caretPosition}");
                sb.AppendLine($"text='{input.text}'");

                sb.AppendLine("--- Tree ---");
                Walk(bar.transform, 0, sb);

                // Specifically hunt for caret-related children.
                sb.AppendLine("--- Caret hunt ---");
                foreach (Transform t in bar.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (t == null) continue;
                    if (t.name.IndexOf("caret", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var rt = t as RectTransform;
                    sb.AppendLine($"  '{t.name}' active={t.gameObject.activeInHierarchy} parent='{t.parent?.name}' size={(rt != null ? rt.rect.size.ToString() : "?")} pos={(rt != null ? rt.anchoredPosition.ToString() : "?")}");
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c == null) continue;
                        var n = c.GetType().Name;
                        if (n == "Transform" || n == "RectTransform") continue;
                        sb.Append("    ").AppendLine(n);
                    }
                }
                return sb.ToString();
            }
            catch (Exception e) { return $"ProbeCaret threw: {e.GetBaseException()}"; }
        }

        // Dump every TMP_InputField in scene with full config — to compare
        // a working one (e.g. HttpProbe's IOBar) against ours.
        public static string DumpAllInputs()
        {
            try
            {
                var sb = new StringBuilder();
                var inputs = Resources.FindObjectsOfTypeAll<TMPro.TMP_InputField>();
                sb.AppendLine($"Inputs: {inputs.Length}");
                foreach (var i in inputs)
                {
                    if (i == null) continue;
                    sb.AppendLine($"--- '{i.name}' ---");
                    sb.AppendLine($"  active={i.gameObject.activeInHierarchy} interactable={i.interactable}");
                    sb.AppendLine($"  customCaretColor={i.customCaretColor} caretColor={i.caretColor} caretWidth={i.caretWidth} caretBlinkRate={i.caretBlinkRate}");
                    sb.AppendLine($"  pointSize={i.pointSize} fontAsset={(i.fontAsset != null ? i.fontAsset.name : "<null>")}");
                    sb.AppendLine($"  textViewport='{(i.textViewport != null ? i.textViewport.name : "<null>")}' textComponent='{(i.textComponent != null ? i.textComponent.name : "<null>")}'");
                    var tt = i.textComponent as TMPro.TextMeshProUGUI;
                    if (tt != null) sb.AppendLine($"    textComp.fontSize={tt.fontSize} alignment={tt.alignment} color={tt.color} raycastTarget={tt.raycastTarget} extraPadding={tt.extraPadding}");
                    sb.AppendLine($"  parent='{(i.transform.parent != null ? i.transform.parent.name : "<root>")}'");
                    var children = new System.Text.StringBuilder();
                    foreach (Transform c in i.transform) children.Append(c.name + ",");
                    sb.AppendLine($"  inputGO children: [{children}]");
                }
                return sb.ToString();
            }
            catch (Exception e) { return $"DumpAllInputs threw: {e.GetBaseException()}"; }
        }

        // Find any ARInputField prefabs/instances loaded in the game so we
        // can clone one as the search bar (gives us the game's native styling
        // and a working caret automatically).
        public static string FindARInputFields()
        {
            try
            {
                var fields = Resources.FindObjectsOfTypeAll<Awaken.TG.Main.UI.Components.ARInputField>();
                var sb = new StringBuilder();
                sb.AppendLine($"ARInputField count: {fields.Length}");
                foreach (var f in fields)
                {
                    if (f == null) continue;
                    var path = "<unparented>";
                    var t = f.transform;
                    if (t != null)
                    {
                        var parts = new System.Collections.Generic.List<string>();
                        for (var x = t; x != null; x = x.parent) parts.Insert(0, x.name);
                        path = string.Join("/", parts);
                    }
                    sb.AppendLine($"  '{f.name}' active={f.gameObject.activeInHierarchy} sceneValid={f.gameObject.scene.IsValid()} sceneName='{f.gameObject.scene.name}' path={path}");
                }
                return sb.ToString();
            }
            catch (Exception e) { return $"FindARInputFields threw: {e.GetBaseException()}"; }
        }

        // Force-build, then force-focus, then dump caret state.
        public static string FocusAndDump()
        {
            var build = ForceBuildBar();
            var bar = Resources.FindObjectsOfTypeAll<GameObject>()
                .FirstOrDefault(g => g != null && g.name == "BetterSortingSearch" && g.activeInHierarchy);
            if (bar == null) return $"FocusAndDump: bar not found.\n{build}";
            var input = bar.GetComponentInChildren<TMPro.TMP_InputField>(includeInactive: true);
            if (input == null) return $"FocusAndDump: input not found.\n{build}";

            // Force focus.
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(input.gameObject);
            input.ActivateInputField();
            input.Select();

            var sb = new StringBuilder();
            sb.AppendLine(build);
            sb.AppendLine($"input.isFocused={input.isFocused}");
            sb.AppendLine($"input.interactable={input.interactable}");
            sb.AppendLine($"input.readOnly={input.readOnly}");
            sb.AppendLine($"input.caretColor={input.caretColor} customCaretColor={input.customCaretColor} caretWidth={input.caretWidth} caretBlinkRate={input.caretBlinkRate}");
            sb.AppendLine($"input.selectionColor={input.selectionColor}");
            sb.AppendLine($"input.text='{input.text}' caretPosition={input.caretPosition}");
            sb.AppendLine($"EventSystem.current={(UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.name : "<null>")}");
            sb.AppendLine($"EventSystem.selected='{(UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject?.name ?? "<null>")}'");
            return sb.ToString();
        }

        // Force-creates a search bar on whatever live VCRecipeSorting exists,
        // bypassing the static idempotency check (which thinks a bar exists
        // because _root points to a destroyed GameObject from a tab cycle).
        public static string ForceBuildBar()
        {
            try
            {
                var vc = Resources.FindObjectsOfTypeAll<Awaken.TG.Main.Crafting.HandCrafting.RecipeView.VCRecipeSorting>()
                    .FirstOrDefault(v => v != null && v.gameObject != null && v.gameObject.scene.IsValid() && v.gameObject.activeInHierarchy);
                if (vc == null) return "No live VCRecipeSorting.";

                // Reset SearchBar statics so Ensure rebuilds.
                SearchBar.Destroy();
                SearchBar.Ensure(vc);

                var bar = Resources.FindObjectsOfTypeAll<GameObject>()
                    .FirstOrDefault(g => g != null && g.name == "BetterSortingSearch");
                if (bar == null) return "Build appeared to succeed but no GameObject 'BetterSortingSearch' is in scene afterwards.";
                return $"Built. parent='{bar.transform.parent?.name}' active={bar.activeInHierarchy} sceneValid={bar.scene.IsValid()} sceneName='{bar.scene.name}'";
            }
            catch (Exception e)
            {
                return $"ForceBuildBar threw: {e.GetBaseException()}";
            }
        }

        // Walks the VRecipeGridUI's transform tree and dumps layout-relevant
        // info for each node — sizes, layout groups, masks, what kind of
        // content it has. Used to find the right parent for the search bar.
        public static string DumpRecipeGridTree()
        {
            try
            {
                var grid = Resources.FindObjectsOfTypeAll<Awaken.TG.Main.Crafting.HandCrafting.RecipeView.VRecipeGridUI>()
                    .FirstOrDefault(v => v != null && v.gameObject != null && v.gameObject.scene.IsValid() && v.gameObject.activeInHierarchy);
                if (grid == null) return "No active VRecipeGridUI in scene.";

                var sb = new StringBuilder();
                sb.AppendLine($"VRecipeGridUI tree:");
                Walk(grid.transform, 0, sb);
                return sb.ToString();
            }
            catch (Exception e)
            {
                return $"DumpRecipeGridTree failed: {e.GetBaseException()}";
            }
        }

        private static void Walk(Transform t, int depth, StringBuilder sb)
        {
            if (t == null) return;
            var indent = new string(' ', depth * 2);
            var rt = t as RectTransform;
            string size = rt != null ? $"size=({rt.rect.width:F0}x{rt.rect.height:F0})" : "";
            var components = new System.Collections.Generic.List<string>();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var n = c.GetType().Name;
                if (n == "RectTransform" || n == "Transform") continue;
                if (n == "CanvasRenderer") continue;
                components.Add(n);
            }
            sb.AppendLine($"{indent}{t.name} {size} [{string.Join(",", components)}]");
            foreach (Transform child in t)
                Walk(child, depth + 1, sb);
        }

        // Diagnostic — counts VCRecipeSorting instances, our GameObjects,
        // and dumps their state. Invoke via /eval to see what's actually
        // in the scene without needing the user to do anything.
        public static string Diagnose()
        {
            try
            {
                var sb = new StringBuilder();

                var vcs = Resources.FindObjectsOfTypeAll<Awaken.TG.Main.Crafting.HandCrafting.RecipeView.VCRecipeSorting>()
                    .Where(v => v != null && v.gameObject != null && v.gameObject.scene.IsValid())
                    .ToList();
                sb.AppendLine($"Total VCRecipeSorting in scene: {vcs.Count}");
                int activeIdx = 0;
                foreach (var vc in vcs)
                {
                    sb.AppendLine($"  [{activeIdx++}] '{vc.name}' active={vc.gameObject.activeInHierarchy}");
                    sb.AppendLine($"      path: {Path(vc.transform)}");
                }

                var ourGos = Resources.FindObjectsOfTypeAll<GameObject>()
                    .Where(g => g != null && g.scene.IsValid() &&
                                (g.name == "BetterSortingSearch" || g.name == "BetterSortingCraftingCanvas"))
                    .ToList();
                sb.AppendLine($"Our GameObjects in scene: {ourGos.Count}");
                foreach (var go in ourGos)
                {
                    var rt = go.transform as RectTransform;
                    sb.AppendLine($"  '{go.name}' active={go.activeInHierarchy} parent='{(go.transform.parent != null ? go.transform.parent.name : "<root>")}'");
                    if (rt != null) sb.AppendLine($"      anchoredPos={rt.anchoredPosition} sizeDelta={rt.sizeDelta} scale={rt.localScale}");
                    var c = go.GetComponent<Canvas>();
                    if (c != null) sb.AppendLine($"      Canvas: renderMode={c.renderMode} sortingOrder={c.sortingOrder} enabled={c.enabled}");
                }

                // If a live VC exists, force-create the bar right now.
                var live = vcs.FirstOrDefault(v => v.gameObject.activeInHierarchy);
                if (live != null)
                {
                    sb.AppendLine("Forcing SearchBar.Ensure on live VC...");
                    try
                    {
                        var grid = live.Target?.RecipeGridUI;
                        if (grid != null) FilterState.ActiveGrid = grid;
                        SearchBar.Ensure(live);
                        sb.AppendLine("Ensure call returned without throwing.");
                    }
                    catch (Exception e)
                    {
                        sb.AppendLine($"Ensure threw: {e.GetBaseException()}");
                    }
                }
                else
                {
                    sb.AppendLine("(No active VC — user is not in a crafting screen.)");
                    sb.AppendLine("Building a STANDALONE test bar at top-center of screen so we can see if the Canvas approach renders at all.");
                    BuildStandaloneTestBar();
                }

                return sb.ToString();
            }
            catch (Exception e)
            {
                return $"Diagnose failed: {e.GetBaseException()}";
            }
        }

        private static string Path(Transform t)
        {
            if (t == null) return "<null>";
            var parts = new System.Collections.Generic.List<string>();
            for (var x = t; x != null; x = x.parent) parts.Insert(0, x.name);
            return string.Join("/", parts);
        }

        private static void BuildStandaloneTestBar()
        {
            // Sanity check — make a screen-overlay Canvas with a bright box so
            // we can prove the rendering path works regardless of game UI.
            var go = new GameObject(
                "BetterSortingTestBar",
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            var box = new GameObject("Box", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            box.transform.SetParent(go.transform, false);
            var rt = (RectTransform)box.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -50f);
            rt.sizeDelta = new Vector2(420f, 60f);
            var img = box.GetComponent<UnityEngine.UI.Image>();
            img.color = new Color(1f, 0.2f, 0.2f, 0.95f);     // bright red
        }

        private static int DestroyStaleGameObjects()
        {
            int count = 0;
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in all)
            {
                if (go == null) continue;
                if (!go.scene.IsValid()) continue;     // skip prefabs/assets
                if (go.name != "BetterSortingSearch"
                    && go.name != "BetterSortingCraftingCanvas"
                    && go.name != "BetterSortingTestBar")
                    continue;
                try { UnityEngine.Object.DestroyImmediate(go); count++; }
                catch { }
            }
            return count;
        }
    }
}
