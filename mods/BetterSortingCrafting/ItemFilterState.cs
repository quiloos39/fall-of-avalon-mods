using System;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.List;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Panel.Tabs;
using Awaken.TG.Main.Heroes.Items;

namespace BetterSortingCrafting
{
    // Combined search + category filter for item lists (shop / bag / stash /
    // gems / transmog / equipment-choose — anywhere VCItemSorting attaches).
    // Crafting has its own FilterState; keeping them separate so a category
    // chosen in one context doesn't leak into the other.
    internal static class ItemFilterState
    {
        public static RecipeFilter CategoryFilter = RecipeFilter.All;
        public static string SearchText = string.Empty;

        public static void Reset()
        {
            CategoryFilter = RecipeFilter.All;
            SearchText = string.Empty;
        }

        private static bool IsActive =>
            !ReferenceEquals(CategoryFilter, RecipeFilter.All)
            || !string.IsNullOrEmpty(SearchText);

        public static bool Matches(Item item)
        {
            if (item == null) return false;

            if (!ReferenceEquals(CategoryFilter, RecipeFilter.All))
            {
                var template = item.Template;
                if (template == null || !CategoryFilter.Matches(template))
                    return false;
            }

            if (!string.IsNullOrEmpty(SearchText))
            {
                var name = item.DisplayName ?? string.Empty;
                if (name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            return true;
        }

        // Apply the combined predicate via OverrideFilter + Refresh — same
        // path InventorySearchBar uses. ItemsListUI consumes _filterOverride
        // inside Refresh, so we re-call this helper any time either input
        // changes (search keystroke or filter popup pick).
        public static void Apply(ItemsListUI listUI)
        {
            if (listUI == null) return;
            try
            {
                if (!IsActive)
                {
                    listUI.OverrideFilter(null);
                }
                else
                {
                    var tab = new ItemsTabType("BetterSortingItemFilter",
                        Matches, "", null, null);
                    listUI.OverrideFilter(tab);
                }
                listUI.Refresh();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] item filter apply failed: {e.GetBaseException().Message}");
            }
        }
    }
}
