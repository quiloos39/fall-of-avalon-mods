using System;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.List;
using Awaken.TG.Main.Heroes.Items;

namespace BetterSortingCrafting
{
    // Value-per-weight ratio sortings. Built via ItemsSorting's publicized
    // ctor — the game ships ByPrice / ByWeight but no combined ratio, which
    // is the metric you actually want when deciding what to drop while
    // overencumbered.
    internal static class CustomItemSortings
    {
        public static ItemsSorting ByValuePerWeightDescending { get; private set; }
        public static ItemsSorting ByValuePerWeightAscending  { get; private set; }

        private static bool _built;

        public static void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            try
            {
                ItemsSorting.Comparer cmp = (Item x, Item y) =>
                    ItemsSorting.Compare(RatioOf(x), RatioOf(y));

                ByValuePerWeightDescending = New("ByValuePerWeightDescending", cmp,
                    reverse: false, label: "Value/Weight ↓");
                ByValuePerWeightAscending  = New("ByValuePerWeightAscending",  cmp,
                    reverse: true,  label: "Value/Weight ↑");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] custom item sorting build failed: {e.GetBaseException()}");
            }
        }

        // Use ExactPrice (float) rather than Price (rounded int) so cheap items
        // don't tie. Weightless items get +∞ — they sort to the top of the
        // descending list, which matches intuition: a free-to-carry item is
        // never the right thing to drop when overencumbered.
        private static float RatioOf(Item item)
        {
            if (item == null) return 0f;
            float weight = item.Weight;
            if (weight <= 0f) return float.PositiveInfinity;
            return item.ExactPrice / weight;
        }

        private static ItemsSorting New(string enumName, ItemsSorting.Comparer cmp, bool reverse, string label)
        {
            // The nameID arg is a loc id that won't resolve for our custom
            // labels, so pass empty and let LocString.ToString fall through to
            // the Fallback we set right after.
            var sorting = new ItemsSorting(enumName, cmp, reverse, nameID: string.Empty);
            try
            {
                sorting._name?.SetFallback(label, overrideExisting: true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[BetterSorting] could not set loc fallback: {e.Message}");
            }
            return sorting;
        }
    }
}
