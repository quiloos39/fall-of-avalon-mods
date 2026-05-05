using System;
using System.Linq;
using HarmonyLib;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Combat;
using Awaken.TG.Main.Locations.Containers;

namespace SpoilsOfTheSlain
{
    // Discovery hooks: every time the world reveals an item to the player (drop, container,
    // NPC corpse), we make sure the item is in HeroItems.KnownItems AND has a recipe issued
    // for it. This closes the AutoCollect gap (items the player saw but didn't pick up) and
    // also keeps the recipe pool fresh as new items become known mid-session.
    internal static class DiscoveryHelper
    {
        public static void OnItemDiscovered(ItemTemplate template, string source)
        {
            if (template == null) return;
            try
            {
                var hero = Hero.Current;
                if (hero == null) return;
                var heroItems = hero.Element<HeroItems>();
                if (heroItems == null) return;

                int before = heroItems.KnownItems?.Count ?? 0;
                heroItems.AddToKnownItems(template);

                // If this is genuinely new, also issue a recipe right away so the player
                // doesn't need to revisit the forge "after the rebuild" — recipe is there
                // the moment they next open the station.
                bool isNew = (heroItems.KnownItems?.Count ?? 0) > before;
                RecipeIssuer.Issue(template, announce: true);     // idempotent, no-op for already-known items

                if (isNew && Plugin.Cfg.Verbose.Value)
                    Plugin.Log.LogInfo($"[Discovery] +{template.ItemName ?? template.name} via {source}");
            }
            catch (Exception e)
            {
                if (Plugin.Cfg.Verbose.Value)
                    Plugin.Log.LogWarning($"[Discovery] {source} hook failed: {e.GetBaseException().Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NPCItemDroppedElement), nameof(NPCItemDroppedElement.OnFullyInitialized))]
    internal static class NPCItemDroppedElement_OnFullyInitialized_Patch
    {
        static void Postfix(NPCItemDroppedElement __instance)
        {
            if (!Plugin.Cfg.ExpandDiscoveryOnNpcDrop.Value) return;
            DiscoveryHelper.OnItemDiscovered(__instance?.DroppedItem?.Template, "NpcDrop");
        }
    }

    [HarmonyPatch(typeof(ContainerInventory), nameof(ContainerInventory.Add))]
    internal static class ContainerInventory_Add_Patch
    {
        static void Postfix(Item __result)
        {
            if (!Plugin.Cfg.ExpandDiscoveryOnContainer.Value) return;
            DiscoveryHelper.OnItemDiscovered(__result?.Template, "Container");
        }
    }

    [HarmonyPatch(typeof(DroppedItemSpawner), nameof(DroppedItemSpawner.OnItemDropped))]
    internal static class DroppedItemSpawner_OnItemDropped_Patch
    {
        static void Postfix(DroppedItemData data)
        {
            if (!Plugin.Cfg.ExpandDiscoveryOnDrop.Value) return;
            DiscoveryHelper.OnItemDiscovered(data.item?.Template, "DropSpawn");
        }
    }
}
