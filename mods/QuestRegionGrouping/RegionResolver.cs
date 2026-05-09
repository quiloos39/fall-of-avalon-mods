using Awaken.TG.Assets;
using Awaken.TG.Main.Localization;
using Awaken.TG.Main.Stories.Quests;

namespace QuestRegionGrouping
{
    // Quest -> region scene -> localized region name.
    //
    // Quests don't carry a region directly. The path is:
    //   Quest -> Objective.MarkersData[i].OpenWorldScene
    //         -> ScenesCache.GetOpenWorldRegion(targetScene)
    //         -> SceneRegion.regionScene
    //         -> LocTerms.GetSceneName(scene)  (translates "Scene/<name>")
    //
    // We prefer the first active objective's region (matches what the game shows
    // on the quest description panel via VQuestDescriptionUI.TryToSetQuestSceneName);
    // fall back to any objective with a target so completed-objective quests still
    // get grouped sensibly.
    internal static class RegionResolver
    {
        // Stable dictionary key. Empty string == "Other" bucket.
        public static string RegionKey(SceneReference region)
        {
            return (region != null && region.IsSet) ? region.Name : string.Empty;
        }

        public static SceneReference GetRegion(Quest quest)
        {
            if (quest == null) return null;

            // Active objectives first.
            foreach (var obj in quest.ActiveObjectives)
            {
                var r = TryResolveFromMarkers(obj);
                if (r != null) return r;
            }

            // Then any objective.
            foreach (var obj in quest.Objectives.GetManagedEnumerator())
            {
                var r = TryResolveFromMarkers(obj);
                if (r != null) return r;
            }

            return null;
        }

        private static SceneReference TryResolveFromMarkers(Awaken.TG.Main.Stories.Quests.Objectives.Objective obj)
        {
            if (obj == null) return null;
            var markers = obj.MarkersData;
            if (markers == null) return null;
            for (int i = 0; i < markers.Length; i++)
            {
                var r = markers[i].OpenWorldScene;
                if (r != null && r.IsSet) return r;
            }
            return null;
        }

        public static string DisplayName(SceneReference region)
        {
            if (region == null || !region.IsSet) return "Other";
            try
            {
                var translated = LocTerms.GetSceneName(region);
                // Translation pipeline returns "Scene/<key>" verbatim if no entry exists.
                if (string.IsNullOrWhiteSpace(translated) || translated.StartsWith("Scene/"))
                    return region.Name;
                return translated;
            }
            catch
            {
                return region.Name ?? "Other";
            }
        }
    }
}
