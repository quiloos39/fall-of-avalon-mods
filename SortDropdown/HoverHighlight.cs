using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SortDropdown
{
    // Lightweight pointer enter/exit handler that swaps a target Image's color
    // between a normal and hover state. We use this instead of relying on
    // ARButton's color transitions — those don't apply reliably when we change
    // the colors at runtime (MarkToRefresh schedules but doesn't always commit).
    internal class HoverHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Target;
        public Color NormalColor;
        public Color HoverColor;

        public void OnPointerEnter(PointerEventData _)
        {
            if (Target != null) Target.color = HoverColor;
        }

        public void OnPointerExit(PointerEventData _)
        {
            if (Target != null) Target.color = NormalColor;
        }
    }
}
