using System;
using System.Collections.Generic;
using Awaken.TG.Main.Crafting.Recipes;
using Awaken.TG.Main.Heroes.Items;

namespace SpoilsOfTheSlain
{
    // The single source of truth for all recipes we've generated. No cache invalidation,
    // no rebuild logic — recipes are added once and never removed for the session. The
    // RecipeFactory.Build call is the only place new entries get created; everything else
    // reads from here.
    //
    // Indexed by:
    //   - station + outcome GUID → IRecipe (so we never duplicate per outcome)
    //   - flat HashSet<IRecipe>   → for IsLearned membership check (O(1))
    internal static class RecipeRegistry
    {
        private static readonly Dictionary<RecipeFactory.Station, Dictionary<string, IRecipe>> _byOutcome
            = new Dictionary<RecipeFactory.Station, Dictionary<string, IRecipe>>
            {
                [RecipeFactory.Station.Forge]   = new Dictionary<string, IRecipe>(512),
                [RecipeFactory.Station.Alchemy] = new Dictionary<string, IRecipe>(64),
                [RecipeFactory.Station.Cooking] = new Dictionary<string, IRecipe>(64),
            };

        // Membership set used by HeroRecipes.IsLearned postfix. Flat across all stations.
        public static readonly HashSet<IRecipe> All = new HashSet<IRecipe>();

        // Returns true if a NEW recipe was added (i.e. wasn't already registered for this
        // outcome at this station). Caller can use this to know whether to log/notify.
        public static bool TryAdd(RecipeFactory.Station station, ItemTemplate outcome, IRecipe recipe)
        {
            if (recipe == null || outcome == null) return false;
            string guid = null;
            try { guid = outcome.GUID; } catch { }
            if (string.IsNullOrEmpty(guid)) return false;

            var bucket = _byOutcome[station];
            if (bucket.ContainsKey(guid)) return false;     // already have one for this outcome
            bucket[guid] = recipe;
            All.Add(recipe);
            return true;
        }

        public static bool Contains(RecipeFactory.Station station, string outcomeGuid)
        {
            return _byOutcome[station].ContainsKey(outcomeGuid);
        }

        public static IEnumerable<IRecipe> GetAll(RecipeFactory.Station station)
        {
            return _byOutcome[station].Values;
        }

        public static int Count(RecipeFactory.Station station) => _byOutcome[station].Count;
    }
}
