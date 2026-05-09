using Awaken.TG.Main.Stories.Quests.UI;
using HarmonyLib;

namespace QuestRegionGrouping
{
    // Postfix the moment QuestLogUI has finished spawning its views and
    // populating QuestUI elements. By now `vQuestListUI` and every VQuestUI
    // are mounted under questParent in vanilla (type-grouped) order; we
    // reorder them into Region -> Type buckets.
    [HarmonyPatch(typeof(QuestLogUI), "AfterViewSpawned")]
    internal static class QuestLogUI_AfterViewSpawned_Patch
    {
        static void Postfix(QuestLogUI __instance, VQuestLogUI view)
        {
            try
            {
                var listView = __instance?.View<VQuestListUI>();
                if (listView == null) return;
                RegionInjector.Inject(__instance, listView);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[QuestRegionGrouping] AfterViewSpawned postfix failed: {e}");
            }
        }
    }

    // Postfix every tab switch and the initial SelectFirstTab. The vanilla
    // method sets each QuestUI's IsVisible AND re-activates the original
    // section transforms when listType==All; we re-suppress those originals
    // and toggle our cloned region/type headers based on whether any quest
    // under them is currently visible.
    [HarmonyPatch(typeof(VQuestListUI), "ChangeListVisibility")]
    internal static class VQuestListUI_ChangeListVisibility_Patch
    {
        static void Postfix(VQuestListUI __instance, QuestListType listType)
        {
            try
            {
                if (__instance == null) return;
                RegionInjector.ApplyVisibility(__instance, listType);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[QuestRegionGrouping] ChangeListVisibility postfix failed: {e}");
            }
        }
    }
}
