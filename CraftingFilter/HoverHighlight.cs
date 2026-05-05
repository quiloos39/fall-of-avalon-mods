using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CraftingFilter
{
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
