using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LanternProbe
{
    // Finds the live VContextPopupUI in the scene (must be open!) and dumps
    // its full GameObject hierarchy with sizes/components. Used to understand
    // why our restyled popup's bg doesn't extend past the text.
    public static class PopupHierarchyProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var popupType = ResolveType("Awaken.TG.Main.UI.Popup.VContextPopupUI");
                bag["popupType_found"] = popupType != null;
                if (popupType == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                var popups = UnityEngine.Object.FindObjectsOfType(popupType, includeInactive: true);
                bag["popup_instances"] = popups?.Length ?? 0;
                if (popups == null || popups.Length == 0)
                {
                    bag["hint"] = "No popup currently in scene. Open the sort popup in-game (press F in inventory), then re-run.";
                    return JsonConvert.SerializeObject(bag, Formatting.Indented);
                }

                var hierarchies = new List<object>();
                foreach (var obj in popups)
                {
                    var mb = obj as MonoBehaviour;
                    if (mb == null) continue;
                    var sb = new StringBuilder();
                    DumpNode(mb.transform, 0, sb);
                    hierarchies.Add(new
                    {
                        name = mb.gameObject.name,
                        hierarchy = sb.ToString().Split('\n')
                    });
                }
                bag["popups"] = hierarchies;
            }
            catch (Exception e)
            {
                bag["err"] = e.GetBaseException().Message;
                bag["stack"] = e.StackTrace;
            }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static void DumpNode(Transform t, int depth, StringBuilder sb)
        {
            if (t == null || depth > 10) return;
            var rt = t as RectTransform;
            string size = rt != null
                ? $"size=({rt.rect.width:F0}x{rt.rect.height:F0}) sd=({rt.sizeDelta.x:F0},{rt.sizeDelta.y:F0}) anchorMin=({rt.anchorMin.x:F2},{rt.anchorMin.y:F2}) anchorMax=({rt.anchorMax.x:F2},{rt.anchorMax.y:F2}) pivot=({rt.pivot.x:F2},{rt.pivot.y:F2})"
                : "no-rect";

            var img = t.GetComponent<Image>();
            string imgInfo = img != null
                ? $" Image[en={img.enabled},alpha={img.color.a:F2},sprite={(img.sprite != null ? img.sprite.name : "null")},rt={img.raycastTarget}]"
                : "";

            var rawImg = t.GetComponent<RawImage>();
            string rawInfo = rawImg != null ? $" RawImage[en={rawImg.enabled}]" : "";

            var tmp = t.GetComponent<TextMeshProUGUI>();
            string tmpInfo = "";
            if (tmp != null)
            {
                float gpvWide = tmp.GetPreferredValues(tmp.text, float.MaxValue, float.MaxValue).x;
                tmpInfo = $" TMP[en={tmp.enabled},text='{(tmp.text?.Length > 24 ? tmp.text.Substring(0, 24) + "…" : tmp.text)}',pref=({tmp.preferredWidth:F0}x{tmp.preferredHeight:F0}),gpv={gpvWide:F0},autoSz={tmp.enableAutoSizing},fontSz={tmp.fontSize:F0},minSz={tmp.fontSizeMin:F0},maxSz={tmp.fontSizeMax:F0}]";
            }

            var fitter = t.GetComponent<ContentSizeFitter>();
            string fitInfo = fitter != null ? $" Fitter[h={fitter.horizontalFit},v={fitter.verticalFit}]" : "";

            var layout = t.GetComponent<HorizontalOrVerticalLayoutGroup>();
            string layoutInfo = layout != null
                ? $" {layout.GetType().Name}[forceX={layout.childForceExpandWidth},ctrlX={layout.childControlWidth},forceY={layout.childForceExpandHeight},ctrlY={layout.childControlHeight},pad=({layout.padding.left},{layout.padding.right},{layout.padding.top},{layout.padding.bottom})]"
                : "";

            var le = t.GetComponent<LayoutElement>();
            string leInfo = le != null ? $" LE[minW={le.minWidth:F0},prefW={le.preferredWidth:F0},flexW={le.flexibleWidth:F0}]" : "";

            // ARButton state — dump targetGraphic identity + every state's color.
            string btnInfo = "";
            var arButtonType = ResolveType("Awaken.TG.Main.UI.Components.ARButton");
            if (arButtonType != null)
            {
                var btn = t.GetComponent(arButtonType);
                if (btn != null)
                {
                    var f = arButtonType.GetField("targetGraphic", BindingFlags.Public | BindingFlags.Instance);
                    var tg = f?.GetValue(btn) as Graphic;
                    var tgName = tg != null ? tg.gameObject.name : "null";
                    var tgAlpha = tg != null ? tg.color.a : -1f;
                    var hgF = arButtonType.GetField("hoverGraphic", BindingFlags.Public | BindingFlags.Instance);
                    var hg = hgF?.GetValue(btn) as Graphic;
                    var hgName = hg != null ? hg.gameObject.name : "null";
                    var nc = (Color?)arButtonType.GetField("normalColor", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.GetValue(btn);
                    var hc = (Color?)arButtonType.GetField("hoverColor", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.GetValue(btn);
                    var ttF = arButtonType.GetField("transitionType", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    var tt = ttF?.GetValue(btn);
                    btnInfo = $" ARBtn[targetGfx='{tgName}'@a={tgAlpha:F2}, hoverGfx='{hgName}', tt={tt}, normCol={nc?.a:F2}, hovCol={hc?.a:F2}]";
                }
            }

            sb.AppendLine($"{new string(' ', depth * 2)}- {t.name} {size}{imgInfo}{rawInfo}{tmpInfo}{fitInfo}{layoutInfo}{leInfo}{btnInfo}");
            for (int i = 0; i < t.childCount; i++) DumpNode(t.GetChild(i), depth + 1, sb);
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
