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

        private static Vector3? TryGetTrackedQuestObjectiveCoords(object hero)
        {
            try
            {
                if (_questTrackerType == null) return null;

                // Tracked quest lives at Hero.Element<QuestTracker>().ActiveQuest — NOT in
                // World.All<Quest>() (the global model registry doesn't index it).
                var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var tracker = elementGeneric?.MakeGenericMethod(_questTrackerType).Invoke(hero, null);
                if (tracker == null) return null;

                var activeQuest = _questTrackerType.GetProperty("ActiveQuest")?.GetValue(tracker);
                if (activeQuest == null) return null;

                if (Plugin.Cfg.Verbose.Value)
                    Plugin.Log.LogInfo($"[QuestPath] tracked active quest: '{activeQuest.GetType().GetProperty("DisplayName")?.GetValue(activeQuest)}'");

                var objsProp = activeQuest.GetType().GetProperty("ActiveObjectives") ?? activeQuest.GetType().GetProperty("Objectives");
                var objs = (objsProp?.GetValue(activeQuest) as System.Collections.IEnumerable)?.Cast<object>().ToList();
                if (objs == null || objs.Count == 0) return null;

                foreach (var objective in objs)
                {
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
