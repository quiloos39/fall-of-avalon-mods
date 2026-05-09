using System.Collections.Generic;
using System.Linq;
using Awaken.TG.Assets;
using Awaken.TG.Main.Stories.Quests.UI;
using TMPro;
using UnityEngine;

namespace QuestRegionGrouping
{
    internal class TypeGroup
    {
        public QuestType Type;
        public GameObject Header;
        public List<QuestUI> Quests = new List<QuestUI>();
    }

    internal class RegionGroup
    {
        public string Key;             // stable dictionary key (region.Name or "")
        public string DisplayName;     // localized for UI
        public SceneReference Region;
        public GameObject Header;
        public List<TypeGroup> Types = new List<TypeGroup>();
    }

    internal class GroupingState
    {
        public List<RegionGroup> Regions = new List<RegionGroup>();
        public List<Transform> OriginalSections = new List<Transform>();
    }

    // Reorders the quest log into Region -> Type two-level grouping.
    //
    // The vanilla flow (QuestLogUI.AfterViewSpawned + VQuestListUI):
    //   - VQuestListUI's `sectionsUI` holds 3 pre-built type-header transforms
    //     (Main, Side, Misc), parked under `sectionsInactiveParent`.
    //   - For each unique active QuestType, AfterViewSpawned calls
    //     `SetupQuestSection(type, n)` which reparents the matching header
    //     into `questParent` at sibling index n.
    //   - `[SpawnsView]` on QuestUI then appends each VQuestUI under questParent.
    //
    // Our postfix replaces that arrangement:
    //   - Deactivate the 3 originals (we'll reuse them as Instantiate templates).
    //   - For each region (sorted by display name, "Other" last):
    //       * Clone a region superheader (the localized region name, bold).
    //       * For each type within the region (Main, Side, Misc enum order):
    //           - Clone the matching type subheader (keeps the original "Main
    //             Quests" / "Side Quests" / "Misc Quests" text).
    //           - Re-sibling each VQuestUI in that (region, type) bucket.
    //
    // Why a postfix and not a prefix-replace: the vanilla method also wires up
    // prompts, listeners, and selection state. Letting it run and then reordering
    // children is cheaper than re-implementing the whole flow.
    internal static class RegionInjector
    {
        public const string CloneNamePrefix = "QuestRegionGrouping_";

        // Per-VQuestListUI state. Each panel-open spawns a fresh VQuestListUI;
        // we clean up entries whose key has been destroyed (Unity '==' against null
        // returns true for destroyed objects).
        private static readonly Dictionary<VQuestListUI, GroupingState> _state =
            new Dictionary<VQuestListUI, GroupingState>();

        public static GroupingState GetState(VQuestListUI listView)
        {
            return _state.TryGetValue(listView, out var s) ? s : null;
        }

        public static void Inject(QuestLogUI questLogUI, VQuestListUI listView)
        {
            try
            {
                ReapStale();

                var sections = listView.sectionsUI;
                if (sections == null || sections.Count == 0)
                {
                    Plugin.Log.LogWarning("[QuestRegionGrouping] sectionsUI is empty; skipping injection.");
                    return;
                }

                Transform sampleHeader = sections[0].SectionTransform;
                if (sampleHeader == null)
                {
                    Plugin.Log.LogWarning("[QuestRegionGrouping] No template section transform; skipping injection.");
                    return;
                }

                Transform questParent = listView.questParent;
                if (questParent == null)
                {
                    Plugin.Log.LogWarning("[QuestRegionGrouping] questParent is null; skipping injection.");
                    return;
                }

                var state = new GroupingState();

                // Hide originals; we clone instead. Track them so the visibility
                // postfix can re-deactivate after vanilla SetQuestSectionActive(true).
                foreach (var s in sections)
                {
                    var t = s.SectionTransform;
                    if (t == null) continue;
                    state.OriginalSections.Add(t);
                    t.gameObject.SetActive(false);
                }

                // Bucket by region key.
                var regionMap = new Dictionary<string, RegionGroup>();
                foreach (var q in questLogUI.AllQuests)
                {
                    if (q?.QuestData == null) continue;
                    var region = RegionResolver.GetRegion(q.QuestData);
                    var key = RegionResolver.RegionKey(region);

                    if (!regionMap.TryGetValue(key, out var rg))
                    {
                        rg = new RegionGroup
                        {
                            Key = key,
                            Region = region,
                            DisplayName = RegionResolver.DisplayName(region),
                        };
                        regionMap[key] = rg;
                    }

                    var type = q.QuestType;
                    var tg = rg.Types.FirstOrDefault(t => t.Type == type);
                    if (tg == null)
                    {
                        tg = new TypeGroup { Type = type };
                        rg.Types.Add(tg);
                    }
                    tg.Quests.Add(q);
                }

                if (regionMap.Count == 0) return;

                // Region order: alphabetical by display name, "Other" (empty key) last.
                var sortedRegions = regionMap.Values
                    .OrderBy(rg => string.IsNullOrEmpty(rg.Key) ? 1 : 0)
                    .ThenBy(rg => rg.DisplayName, System.StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int siblingIdx = 0;
                foreach (var rg in sortedRegions)
                {
                    rg.Header = CloneHeader(
                        sampleHeader,
                        questParent,
                        $"{CloneNamePrefix}Region_{rg.DisplayName}",
                        text: $"<b>{rg.DisplayName.ToUpperInvariant()}</b>");
                    rg.Header.transform.SetSiblingIndex(siblingIdx++);

                    // Type order: enum value (Main < Side < Challenge < Achievement < Misc).
                    rg.Types = rg.Types.OrderBy(t => (int)t.Type).ToList();

                    foreach (var tg in rg.Types)
                    {
                        Transform typeTemplate = null;
                        foreach (var s in sections)
                        {
                            if (s.QuestType == tg.Type) { typeTemplate = s.SectionTransform; break; }
                        }
                        if (typeTemplate == null) typeTemplate = sampleHeader;

                        // Keep the cloned type header's original text ("Main Quests",
                        // "Side Quests", "Misc Quests") — passing null leaves it alone.
                        tg.Header = CloneHeader(
                            typeTemplate,
                            questParent,
                            $"{CloneNamePrefix}Type_{rg.DisplayName}_{tg.Type}",
                            text: null);
                        tg.Header.transform.SetSiblingIndex(siblingIdx++);

                        foreach (var q in tg.Quests)
                        {
                            var view = q.View<VQuestUI>();
                            if (view == null) continue;
                            view.transform.SetSiblingIndex(siblingIdx++);
                        }
                    }

                    state.Regions.Add(rg);
                }

                _state[listView] = state;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[QuestRegionGrouping] Inject failed: {e}");
            }
        }

        public static void ApplyVisibility(VQuestListUI listView, QuestListType listType)
        {
            try
            {
                var state = GetState(listView);
                if (state == null) return;

                // Vanilla SetQuestSectionActive(true) re-activates originals when the
                // listType==All tab is clicked. Re-suppress them.
                foreach (var t in state.OriginalSections)
                {
                    if (t != null) t.gameObject.SetActive(false);
                }

                bool hideAllSections = listType == QuestListType.Completed
                                       || listType == QuestListType.Failed;

                foreach (var rg in state.Regions)
                {
                    bool anyTypeVisible = false;
                    foreach (var tg in rg.Types)
                    {
                        bool anyQuestVisible = false;
                        if (!hideAllSections)
                        {
                            foreach (var q in tg.Quests)
                            {
                                if (q == null) continue;
                                if (IsQuestVisible(q))
                                {
                                    anyQuestVisible = true;
                                    break;
                                }
                            }
                        }
                        if (tg.Header != null) tg.Header.SetActive(anyQuestVisible);
                        anyTypeVisible |= anyQuestVisible;
                    }
                    if (rg.Header != null) rg.Header.SetActive(anyTypeVisible);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[QuestRegionGrouping] ApplyVisibility failed: {e}");
            }
        }

        private static bool IsQuestVisible(QuestUI q)
        {
            // Guard against the view being torn down mid-frame.
            try { return q.IsVisible; }
            catch { return false; }
        }

        private static GameObject CloneHeader(Transform template, Transform parent, string name, string text)
        {
            var go = Object.Instantiate(template.gameObject, parent);
            go.name = name;
            go.SetActive(true);
            if (text != null)
            {
                var label = go.GetComponentInChildren<TMP_Text>(includeInactive: true);
                if (label != null) label.text = text;
            }
            return go;
        }

        private static void ReapStale()
        {
            List<VQuestListUI> dead = null;
            foreach (var key in _state.Keys)
            {
                if (key == null)
                {
                    if (dead == null) dead = new List<VQuestListUI>();
                    dead.Add(key);
                }
            }
            if (dead != null)
            {
                foreach (var k in dead) _state.Remove(k);
            }
        }
    }
}
