using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace QuestPath
{
    // Resolves the current destination on demand. Two sources, in priority order:
    //   1. Compass.CustomMarkerLocation — the marker the player drops on the world map
    //      (vanilla feature: open map → middle-click / R3 to drop pin).
    //   2. The currently-tracked Quest's first active Objective's first IGrounded target.
    // Either may not exist; returns null if there's nothing to point at.
    //
    // Uses reflection throughout so we don't need a hard reference to Awaken types we don't
    // strictly need — keeps the mod resilient to game updates renaming internals.
    internal static class DestinationProvider
    {
        private static Type _heroType;
        private static Type _compassType;
        private static Type _questType;
        private static Type _worldType;
        private static Type _questTrackerType;
        private static Type _markerDataType;            // Objective+MarkerData (nested struct)
        private static Type _locationReferenceType;     // Awaken.TG.Main.Locations.LocationReference

        // Public entry point. Returns the current destination world coords + a tag describing source,
        // or (null, null) if neither source has a usable target.
        public static (Vector3? coords, string source) Resolve()
        {
            Init();

            object hero = _heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (hero == null) return (null, null);

            // 1) Custom map marker (Compass.CustomMarkerLocation)
            if (Plugin.Cfg.UseCustomMarker.Value)
            {
                var coords = TryGetCompassCustomMarkerCoords(hero);
                if (coords.HasValue) return (coords, "custom map marker");
            }

            // 2) Tracked quest's first active objective target
            if (Plugin.Cfg.UseTrackedQuest.Value)
            {
                var coords = TryGetTrackedQuestObjectiveCoords(hero);
                if (coords.HasValue) return (coords, "tracked quest");
            }

            return (null, null);
        }

        private static void Init()
        {
            if (_heroType != null) return;
            _heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
            _compassType = ResolveType("Awaken.TG.Main.Maps.Compasses.Compass");
            _questType = ResolveType("Awaken.TG.Main.Stories.Quests.Quest");
            _worldType = ResolveType("Awaken.TG.MVC.World");
            _questTrackerType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTracker");
            _locationReferenceType = ResolveType("Awaken.TG.Main.Locations.LocationReference");
            _markerDataType = ResolveType("Awaken.TG.Main.Stories.Quests.Objectives.Objective+MarkerData");
        }

        private static Vector3? TryGetCompassCustomMarkerCoords(object hero)
        {
            try
            {
                // Hero contains a Compass element. Use Element<Compass>() generic.
                if (_compassType == null) return null;
                var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                if (elementGeneric == null) return null;
                var compass = elementGeneric.MakeGenericMethod(_compassType).Invoke(hero, null);
                if (compass == null) return null;

                // Check both CustomMarkerLocation and SpyglassMarkerLocation — return whichever is set.
                foreach (var locProp in new[] { "CustomMarkerLocation", "SpyglassMarkerLocation" })
                {
                    var prop = _compassType.GetProperty(locProp);
                    var loc = prop?.GetValue(compass);
                    if (loc == null) continue;
                    var coords = TryGetCoords(loc);
                    if (coords.HasValue) return coords;
                }
            }
            catch (Exception e)
            {
                if (Plugin.Cfg.Verbose.Value) Plugin.Log.LogWarning($"[QuestPath] custom marker resolve failed: {e.Message}");
            }
            return null;
        }

        // One-shot diagnostic flags — log a given trace line at most once until the relevant
        // input changes. Without this, every Update tick floods the log.
        private static string _lastLoggedQuestName;
        private static int _lastLoggedObjectiveCount = -1;

        private static Vector3? TryGetTrackedQuestObjectiveCoords(object hero)
        {
            try
            {
                if (_questTrackerType == null) { Plugin.Log.LogWarning("[QuestPath] _questTrackerType is null"); return null; }

                // Tracked quest lives at Hero.Element<QuestTracker>().ActiveQuest — NOT in
                // World.All<Quest>() (the global model registry doesn't index it).
                var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var tracker = elementGeneric?.MakeGenericMethod(_questTrackerType).Invoke(hero, null);
                if (tracker == null) { LogOnce(ref _lastLoggedQuestName, "<no-tracker>", "[QuestPath] no QuestTracker on hero"); return null; }

                var activeQuest = _questTrackerType.GetProperty("ActiveQuest")?.GetValue(tracker);
                if (activeQuest == null) { LogOnce(ref _lastLoggedQuestName, "<no-quest>", "[QuestPath] no ActiveQuest tracked — pin a quest in the journal"); return null; }

                var displayName = activeQuest.GetType().GetProperty("DisplayName")?.GetValue(activeQuest)?.ToString() ?? "<unnamed>";
                LogOnce(ref _lastLoggedQuestName, displayName, $"[QuestPath] tracked active quest: '{displayName}'");

                var objsProp = activeQuest.GetType().GetProperty("ActiveObjectives") ?? activeQuest.GetType().GetProperty("Objectives");
                var objs = (objsProp?.GetValue(activeQuest) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                if (objs == null || objs.Count == 0)
                {
                    LogOnceCount(ref _lastLoggedObjectiveCount, 0, "[QuestPath] active quest has 0 objectives");
                    return null;
                }
                LogOnceCount(ref _lastLoggedObjectiveCount, objs.Count, $"[QuestPath] active quest has {objs.Count} objective(s)");

                foreach (var objective in objs)
                {
                    // Step A (preferred when RevealHiddenMarkers=true):
                    // Iterate Objective.MarkersData[] ourselves and call MarkerData.LocationReference
                    // .MatchingLocations(null) directly. The vanilla GetAllActiveTargets() filters by
                    // MarkerData.MarkerVisible, which gates targets behind story flags
                    // (e.g. 'find the quarantined house' is invisible until you've talked to NPC X).
                    // We don't care about that gate — the coords exist, surface them.
                    if (Plugin.Cfg.RevealHiddenMarkers.Value)
                    {
                        var bypassed = TryGetMarkerBypassCoords(objective);
                        if (bypassed.HasValue)
                        {
                            if (Plugin.Cfg.Verbose.Value)
                                Plugin.Log.LogInfo($"[QuestPath] resolved target via marker-bypass: {bypassed.Value}");
                            return bypassed;
                        }
                    }

                    // Step B (fallback): vanilla GetAllActiveTargets — respects MarkerVisible gate.
                    // Objective.GetAllActiveTargets(IGrounded, SceneReference) → List<IGrounded>
                    //
                    // IMPORTANT: passing null as the SceneReference returns a fallback that can
                    // be a STALE target from a different quest entirely (we observed it returning
                    // an old NPC's coords across quest changes). Always prefer the objective's
                    // own MainTargetScene first; only fall back if that returns nothing.
                    var getAllTargetsM = objective.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "GetAllActiveTargets" && m.GetParameters().Length == 2);
                    if (getAllTargetsM == null) continue;

                    var mainScene      = objective.GetType().GetProperty("MainTargetScene")?.GetValue(objective);
                    var openWorldScene = objective.GetType().GetProperty("MainTargetOpenWorldScene")?.GetValue(objective);

                    // Try in order: MainTargetScene → OpenWorldScene → null (last resort).
                    foreach (var sceneArg in new[] { mainScene, openWorldScene, null })
                    {
                        object targets;
                        try { targets = getAllTargetsM.Invoke(objective, new object[] { hero, sceneArg }); }
                        catch (Exception e)
                        {
                            if (Plugin.Cfg.Verbose.Value)
                                Plugin.Log.LogInfo($"[QuestPath] GetAllActiveTargets({(sceneArg == null ? "null" : "scene")}) threw on '{objective.GetType().GetProperty("Name")?.GetValue(objective)}': {(e.InnerException ?? e).Message}");
                            continue;
                        }

                        if (targets is System.Collections.IEnumerable list)
                        {
                            foreach (var t in list)
                            {
                                var coords = TryGetCoords(t);
                                if (coords.HasValue)
                                {
                                    if (Plugin.Cfg.Verbose.Value)
                                        Plugin.Log.LogInfo($"[QuestPath] resolved target via {(sceneArg == null ? "fallback null" : "objective scene")}: {coords.Value}");
                                    return coords;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (Plugin.Cfg.Verbose.Value) Plugin.Log.LogWarning($"[QuestPath] tracked-quest resolve failed: {e.Message}");
            }
            return null;
        }

        // Read Objective.MarkersData[], grab each MarkerData.LocationReference, and resolve
        // matching loaded Locations directly via LocationReference.MatchingLocations(null).
        // This skips the MarkerVisible / RelatedStoryFlag gate that the vanilla GetAllActiveTargets
        // applies. Returns the first non-sentinel coord found, or null.
        //
        // Caveat: only finds targets in the currently-loaded scene set. If the location isn't
        // streamed in (target is in another region), no Location matches and we get null —
        // that's fine, the caller falls through to the vanilla path which can route to a portal.
        private static string _lastLoggedBypassObjective;

        private static Vector3? TryGetMarkerBypassCoords(object objective)
        {
            var objName = objective.GetType().GetProperty("Name")?.GetValue(objective)?.ToString() ?? "<unnamed>";
            try
            {
                var markersProp = objective.GetType().GetProperty("MarkersData");
                if (markersProp == null) { LogOnce(ref _lastLoggedBypassObjective, objName + ":no-markersdata", $"[QuestPath:bypass] '{objName}' has no MarkersData property"); return null; }
                var markersArr = markersProp.GetValue(objective) as Array;
                if (markersArr == null || markersArr.Length == 0) { LogOnce(ref _lastLoggedBypassObjective, objName + ":empty", $"[QuestPath:bypass] '{objName}' has 0 markers"); return null; }

                if (_markerDataType == null) { Plugin.Log.LogWarning("[QuestPath:bypass] _markerDataType lookup failed (Objective+MarkerData)"); return null; }
                if (_locationReferenceType == null) { Plugin.Log.LogWarning("[QuestPath:bypass] _locationReferenceType lookup failed"); return null; }
                var locRefProp = _markerDataType.GetProperty("LocationReference");
                var matchingM = _locationReferenceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "MatchingLocations" && m.GetParameters().Length == 1);
                if (locRefProp == null || matchingM == null) { Plugin.Log.LogWarning("[QuestPath:bypass] LocationReference / MatchingLocations not resolvable"); return null; }

                int totalMatches = 0;
                for (int i = 0; i < markersArr.Length; i++)
                {
                    var marker = markersArr.GetValue(i);
                    if (marker == null) continue;
                    var locRef = locRefProp.GetValue(marker);
                    if (locRef == null) continue;

                    object matches;
                    try { matches = matchingM.Invoke(locRef, new object[] { null }); }
                    catch (Exception e)
                    {
                        Plugin.Log.LogInfo($"[QuestPath:bypass] MatchingLocations threw on marker[{i}] of '{objName}': {(e.InnerException ?? e).Message}");
                        continue;
                    }

                    if (matches is System.Collections.IEnumerable list)
                    {
                        int matchCount = 0;
                        foreach (var loc in list)
                        {
                            matchCount++; totalMatches++;
                            var coords = TryGetCoords(loc);
                            if (coords.HasValue)
                            {
                                Plugin.Log.LogInfo($"[QuestPath:bypass] HIT '{objName}' marker[{i}] → loc {loc?.GetType().Name} @ {coords.Value}");
                                return coords;
                            }
                        }
                        if (matchCount == 0)
                            LogOnce(ref _lastLoggedBypassObjective, objName + $":m{i}:0", $"[QuestPath:bypass] '{objName}' marker[{i}] matched 0 loaded locations (target scene not streamed?)");
                    }
                }
                LogOnce(ref _lastLoggedBypassObjective, objName + ":nohit:" + totalMatches, $"[QuestPath:bypass] '{objName}' had {markersArr.Length} marker(s) but no usable coords (totalMatched={totalMatches})");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[QuestPath:bypass] marker-bypass failed on '{objName}': {e.Message}");
            }
            return null;
        }

        // Log a string-keyed message once per distinct key. Resets only when key changes.
        private static void LogOnce(ref string slot, string key, string msg)
        {
            if (slot == key) return;
            slot = key;
            Plugin.Log.LogInfo(msg);
        }
        private static void LogOnceCount(ref int slot, int key, string msg)
        {
            if (slot == key) return;
            slot = key;
            Plugin.Log.LogInfo(msg);
        }

        // Pulls a Vector3 out of any IGrounded / Location / model with a Coords / Position property.
        // Filters out the game's "no target" sentinel (-5000,-5000,-5000) and any other coord
        // that's clearly off-map.
        private static Vector3? TryGetCoords(object obj)
        {
            if (obj == null) return null;
            try
            {
                foreach (var pname in new[] { "Coords", "Position", "WorldPosition" })
                {
                    var p = obj.GetType().GetProperty(pname, BindingFlags.Public | BindingFlags.Instance);
                    if (p == null) continue;
                    var v = p.GetValue(obj);
                    if (v is Vector3 v3)
                    {
                        if (IsSentinelCoord(v3)) continue;     // skip sentinels, try next prop / next caller-arg
                        return v3;
                    }
                }
            }
            catch { }
            return null;
        }

        // The game uses (-5000, -5000, -5000) as a placeholder when an objective doesn't yet
        // have a resolvable target (quest just started, target NPC not spawned yet, etc.). These
        // would lead the path to render under the world. Filter anything with all three axes
        // pinned at large negative values, plus a generous "any coord beyond reasonable game world".
        private static bool IsSentinelCoord(Vector3 v)
        {
            const float SentinelMag = 4500f;       // a hair below the canonical 5000
            const float WorldMaxMag = 100000f;     // game world is bounded; beyond this is junk
            if (Mathf.Abs(v.x) >= SentinelMag && Mathf.Abs(v.y) >= SentinelMag && Mathf.Abs(v.z) >= SentinelMag)
                return true;
            if (Mathf.Abs(v.x) >= WorldMaxMag || Mathf.Abs(v.y) >= WorldMaxMag || Mathf.Abs(v.z) >= WorldMaxMag)
                return true;
            return false;
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
