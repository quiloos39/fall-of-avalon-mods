using System;
using Awaken.TG.Main.Crafting.HandCrafting.RecipeView;
using Awaken.TG.Main.Crafting.Recipes;
using Awaken.TG.Main.Localization;

namespace BetterSortingCrafting
{
    // Build new RecipeSorting instances for damage and armor by reusing the
    // game's own (publicized) private constructor. The instances are stored
    // in static fields so they're safe to feed into RecipeGridUI.ChangeItemsComparer
    // — that path doesn't care whether the sorting was declared at module init
    // by the game or later by us.
    internal static class CustomSortings
    {
        public static RecipeSorting DamageDescending { get; private set; }
        public static RecipeSorting DamageAscending  { get; private set; }
        public static RecipeSorting ArmorDescending  { get; private set; }
        public static RecipeSorting ArmorAscending   { get; private set; }

        private static bool _built;

        public static void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            try
            {
                DamageDescending = New("BetterSortingDamageDescending", DamageCmp,
                    reverse: false, label: "Damage ↓");
                DamageAscending  = New("BetterSortingDamageAscending",  DamageCmp,
                    reverse: true,  label: "Damage ↑");
                ArmorDescending  = New("BetterSortingArmorDescending",  ArmorCmp,
                    reverse: false, label: "Armor ↓");
                ArmorAscending   = New("BetterSortingArmorAscending",   ArmorCmp,
                    reverse: true,  label: "Armor ↑");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[BetterSorting] custom sorting build failed: {e.GetBaseException()}");
            }
        }

        // Game's Compare helpers reverse the operands (y.CompareTo(x)) so
        // _reverse:false ends up descending and _reverse:true ascending. Mirror
        // that behaviour here so our sorts behave consistently with the existing
        // ones (e.g. ByPrice — reverse:false is descending).
        private static int DamageCmp(IRecipe x, IRecipe y)
        {
            return StatsLookup.MaxDamage(y.Outcome).CompareTo(StatsLookup.MaxDamage(x.Outcome));
        }

        private static int ArmorCmp(IRecipe x, IRecipe y)
        {
            return StatsLookup.Armor(y.Outcome).CompareTo(StatsLookup.Armor(x.Outcome));
        }

        private static RecipeSorting New(string enumName,
                                         Func<IRecipe, IRecipe, int> cmp,
                                         bool reverse,
                                         string label)
        {
            // RecipeSorting's ctor is private and takes a private delegate type
            // (`Comparer`). Krafs.Publicizer exposes both. We bind the lambda
            // by going through the delegate type explicitly.
            var comparer = (RecipeSorting.Comparer)((x, y) => cmp(x, y));
            // The nameID arg is a loc id that won't resolve for our custom
            // labels, so pass empty and let LocString.ToString fall through to
            // the Fallback we set right after.
            var sorting = new RecipeSorting(enumName, comparer, reverse,
                                            nameID: string.Empty,
                                            localeCheck: false);
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
