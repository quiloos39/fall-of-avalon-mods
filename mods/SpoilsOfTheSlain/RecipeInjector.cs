using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Awaken.TG.Main.Crafting.HandCrafting;
using Awaken.TG.Main.Crafting.AlchemyCrafting;
using Awaken.TG.Main.Crafting.Cooking;
using Awaken.TG.Main.Crafting.Recipes;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Templates;

namespace SpoilsOfTheSlain
{
    // Postfixes on each station's Recipes getter. Returns vanilla recipes unchanged plus
    // every recipe in our RecipeRegistry for that station. The registry is populated once
    // by StartupBackfill + appended to by Discovery / Kill hooks. No cache invalidation,
    // no per-call rebuild — O(N) in registry size, period.

    [HarmonyPatch(typeof(HandcraftingTemplate), nameof(HandcraftingTemplate.Recipes), MethodType.Getter)]
    internal static class HandcraftingTemplate_Recipes_Patch
    {
        static void Postfix(ref IEnumerable<IRecipe> __result)
        {
            if (!Plugin.Cfg.InjectIntoForge.Value) return;
            try
            {
                var ours = RecipeRegistry.GetAll(RecipeFactory.Station.Forge);
                __result = (__result ?? Enumerable.Empty<IRecipe>()).Concat(ours);
            }
            catch (Exception e) { Plugin.Log.LogError($"[Inject] Forge postfix crashed: {e}"); }
        }
    }

    [HarmonyPatch(typeof(AlchemyTemplate), nameof(AlchemyTemplate.Recipes), MethodType.Getter)]
    internal static class AlchemyTemplate_Recipes_Patch
    {
        static void Postfix(ref IEnumerable<IRecipe> __result)
        {
            if (!Plugin.Cfg.InjectIntoAlchemy.Value) return;
            try
            {
                var ours = RecipeRegistry.GetAll(RecipeFactory.Station.Alchemy);
                __result = (__result ?? Enumerable.Empty<IRecipe>()).Concat(ours);
            }
            catch (Exception e) { Plugin.Log.LogError($"[Inject] Alchemy postfix crashed: {e}"); }
        }
    }

    [HarmonyPatch(typeof(CookingTemplate), nameof(CookingTemplate.Recipes), MethodType.Getter)]
    internal static class CookingTemplate_Recipes_Patch
    {
        static void Postfix(ref IEnumerable<IRecipe> __result)
        {
            if (!Plugin.Cfg.InjectIntoCooking.Value) return;
            try
            {
                var ours = RecipeRegistry.GetAll(RecipeFactory.Station.Cooking);
                __result = (__result ?? Enumerable.Empty<IRecipe>()).Concat(ours);
            }
            catch (Exception e) { Plugin.Log.LogError($"[Inject] Cooking postfix crashed: {e}"); }
        }
    }

    // Patch HeroRecipes.IsLearned — non-generic on a non-generic class. Returns true for
    // recipes in our registry so the UI accepts them.
    [HarmonyPatch(typeof(Awaken.TG.Main.Heroes.HeroRecipes), nameof(Awaken.TG.Main.Heroes.HeroRecipes.IsLearned))]
    internal static class HeroRecipes_IsLearned_Patch
    {
        static void Postfix(IRecipe recipe, ref bool __result)
        {
            if (__result) return;     // vanilla learned recipes pass through
            try
            {
                if (recipe != null && RecipeRegistry.All.Contains(recipe))
                    __result = true;
            }
            catch { }
        }
    }

    // Tab classification — mirrors the game's IsWeapon/IsArmor inheritance check, with
    // tag-based routing for alchemy/cooking. Used by RecipeIssuer to decide which station
    // each item belongs to.
    internal static class StationFilter
    {
        public static bool IsCraftableAt(RecipeFactory.Station station, ItemTemplate t)
        {
            if (t == null) return false;
            try { if (t.HiddenOnUI) return false; } catch { }

            try
            {
                var n = t.ItemName;
                if (string.IsNullOrWhiteSpace(n)) return false;
            }
            catch { return false; }

            var tags = SafeTags(t);

            if (tags.Contains("item:tier0")) return false;
            if (tags.Contains("item:book")) return false;
            if (tags.Contains("item:recipe")) return false;
            if (tags.Contains("item:spell")) return false;
            if (tags.Contains("item:key")) return false;
            if (tags.Any(x => x.StartsWith("ingredients:"))) return false;
            if (tags.Any(x => x.StartsWith("craft:"))) return false;

            switch (station)
            {
                case RecipeFactory.Station.Forge:
                    try
                    {
                        if (t.IsWeapon || t.IsArmor || t.IsShield || t.IsRanged || t.IsArrow || t.IsJewelry)
                            return true;
                    } catch { }
                    return false;

                case RecipeFactory.Station.Alchemy:
                    if (tags.Contains("item:potion") || tags.Contains("item:potionHP")
                        || tags.Contains("item:potionMP") || tags.Contains("item:potionSP")
                        || tags.Contains("item:potionOther") || tags.Contains("item:weapongrease")
                        || tags.Any(x => x.StartsWith("potions:"))
                        || tags.Any(x => x.StartsWith("alchemy:")))
                        return true;
                    return false;

                case RecipeFactory.Station.Cooking:
                    if (tags.Contains("item:dish")) return true;
                    if (tags.Contains("item:foodstuff") && tags.Contains("item:edible")) return true;
                    return false;
            }
            return false;
        }

        private static List<string> SafeTags(ItemTemplate t)
        {
            try
            {
                var tags = t.Tags;
                if (tags == null) return new List<string>();
                return tags.Where(x => x != null).ToList();
            }
            catch { return new List<string>(); }
        }
    }
}
