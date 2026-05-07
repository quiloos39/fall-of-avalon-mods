using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StationIndicator
{
    // Strategy:
    //   1. Harmony-patch LocationSpec.GetAttachments() with a postfix.
    //   2. When the framework calls GetAttachments() during Location creation, our postfix
    //      checks whether this spec is a crafting station that should have a marker.
    //      If so, it ensures a MarkerAttachment component exists on the GameObject (creating
    //      one if needed) wired to a wrapper that points at the same SimpleMarkerData_CraftingStash
    //      template the player stash uses — then appends that MarkerAttachment to the returned
    //      IEnumerable<IAttachmentSpec>.
    //   3. The Location framework then creates a LocationMarker element from our injected
    //      attachment and the icon shows up on the map / compass.
    //
    // We also keep a tiny periodic safety net (every 30s) for already-loaded specs the patch
    // missed — not because it can spawn LocationMarkers retroactively (it can't, the model is
    // past init), but because the wrapper-prototype cache needs *some* stash spec to have been
    // observed at least once before we can clone its wrapper. The Update tick is idle work
    // until the first stash is seen.
    internal class Scanner : MonoBehaviour
    {
        // Cached game types — looked up once.
        internal static Type LocationSpecType;
        internal static Type MarkerAttachmentType;
        internal static Type StartCraftingAttachmentType;
        internal static Type IAttachmentSpecType;
        internal static FieldInfo WrapperField;

        // The wrapper instance cloned from a stash spec — shared across all injected attachments.
        // Sharing is safe because the wrapper holds an Explicit reference to a SimpleMarkerData_CraftingStash
        // ScriptableObject template (read-only), not per-spec mutable state.
        internal static object WrapperPrototype;

        private float _nextScanAt;

        public static int InjectionsTotal;     // for verbose logging
        public static int SpecsSeenWithMarker; // diagnostic counter

        private void Update()
        {
            if (WrapperPrototype != null) return;            // already cached, no need to scan
            if (Time.realtimeSinceStartup < _nextScanAt) return;
            _nextScanAt = Time.realtimeSinceStartup + 30f;

            try
            {
                if (!ResolveTypes()) return;
                TryCacheWrapperPrototype();
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[StationIndicator] Update tick: {e.Message}"); }
        }

        public void ForceRescan()
        {
            _nextScanAt = 0f;
            WrapperPrototype = null; // re-cache on next tick
            Plugin.Log.LogInfo("[StationIndicator] manual rescan requested — wrapper prototype cleared, will refresh on next tick");
        }

        internal static bool ResolveTypes()
        {
            if (LocationSpecType != null) return true;
            LocationSpecType = ResolveType("Awaken.TG.Main.Locations.Setup.LocationSpec");
            MarkerAttachmentType = ResolveType("Awaken.TG.Main.Maps.Markers.MarkerAttachment");
            StartCraftingAttachmentType = ResolveType("Awaken.TG.Main.Locations.Actions.Attachments.StartCraftingAttachment");
            IAttachmentSpecType = ResolveType("Awaken.TG.Main.Locations.Attachments.IAttachmentSpec");
            if (MarkerAttachmentType != null)
            {
                WrapperField = MarkerAttachmentType.GetField("markerDataWrapper",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return LocationSpecType != null && MarkerAttachmentType != null;
        }

        internal static void TryCacheWrapperPrototype()
        {
            if (WrapperPrototype != null) return;
            if (LocationSpecType == null || MarkerAttachmentType == null) return;

            var specs = UnityEngine.Object.FindObjectsOfType(LocationSpecType) as Component[];
            if (specs == null) return;

            // Prefer the player stash (Spec_ChestPlayerStash). Fallback to any spec named *stash*.
            Component bestSrc = null;
            foreach (var spec in specs)
            {
                if (spec == null) continue;
                if (spec.gameObject.name.IndexOf("ChestPlayerStash", StringComparison.OrdinalIgnoreCase) >= 0)
                { bestSrc = spec; break; }
                if (bestSrc == null && spec.gameObject.name.IndexOf("stash", StringComparison.OrdinalIgnoreCase) >= 0)
                { bestSrc = spec; }
            }
            if (bestSrc == null) return;

            var ma = bestSrc.gameObject.GetComponent(MarkerAttachmentType);
            if (ma == null) return;
            var wrapper = WrapperField?.GetValue(ma);
            if (wrapper == null) return;
            WrapperPrototype = wrapper;
            Plugin.Log.LogInfo($"[StationIndicator] cached wrapper prototype from '{bestSrc.gameObject.name}' (wrapperType={wrapper.GetType().Name})");
        }

        internal static bool ShouldMark(GameObject go)
        {
            string n = go.name ?? "";
            string lname = n.ToLowerInvariant();

            if (Plugin.Cfg.MarkAllCraftingStations.Value && StartCraftingAttachmentType != null)
            {
                if (go.GetComponent(StartCraftingAttachmentType) != null) return true;
            }
            if (Plugin.Cfg.MarkBlacksmithing.Value
                && (lname.Contains("blacksmith") || lname.Contains("forge") || lname.Contains("anvil") || lname.Contains("smithing")))
                return true;
            if (Plugin.Cfg.MarkAlchemy.Value
                && (lname.Contains("alchemy") || lname.Contains("alchemist") || lname.Contains("cauldron")))
            {
                if (StartCraftingAttachmentType != null && go.GetComponent(StartCraftingAttachmentType) != null) return true;
            }
            string extra = Plugin.Cfg.ExtraNamePatterns.Value ?? "";
            if (!string.IsNullOrWhiteSpace(extra))
            {
                foreach (var p in extra.Split(','))
                {
                    var needle = p.Trim().ToLowerInvariant();
                    if (needle.Length > 0 && lname.Contains(needle)) return true;
                }
            }
            return false;
        }

        // Either fetch the existing MarkerAttachment on the GameObject, or AddComponent + wire one up.
        // Returns null if the wrapper prototype isn't cached yet.
        internal static Component EnsureMarkerAttachment(GameObject go)
        {
            var existing = go.GetComponent(MarkerAttachmentType);
            if (existing != null) { SpecsSeenWithMarker++; return existing; }

            if (WrapperPrototype == null)
            {
                // Try one last cache attempt.
                TryCacheWrapperPrototype();
                if (WrapperPrototype == null)
                {
                    if (Plugin.Cfg.Verbose.Value)
                        Plugin.Log.LogWarning($"[StationIndicator] '{go.name}' qualifies but no wrapper prototype yet (need a stash spec to have spawned at least once).");
                    return null;
                }
            }

            try
            {
                var ma = go.AddComponent(MarkerAttachmentType);
                WrapperField.SetValue(ma, WrapperPrototype);
                InjectionsTotal++;
                if (Plugin.Cfg.Verbose.Value)
                    Plugin.Log.LogInfo($"[StationIndicator] +marker on '{go.name}' (totalSinceLoad={InjectionsTotal})");
                return ma;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[StationIndicator] AddComponent failed on '{go.name}': {e.Message}");
                return null;
            }
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(fullName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }
    }

    // Postfix on LocationSpec.GetAttachments(). Fires once per Location creation. We append
    // our MarkerAttachment (creating the component first if needed) to the framework's
    // attachment list — the framework then spawns a properly-initialized LocationMarker for it.
    [HarmonyPatch]
    internal static class LocationSpec_GetAttachments_Patch
    {
        // Use TargetMethod so we can resolve LocationSpec lazily — Harmony attribute discovery
        // happens before our type cache is populated.
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("Awaken.TG.Main.Locations.Setup.LocationSpec");
            return t == null ? null : AccessTools.Method(t, "GetAttachments");
        }

        static void Postfix(Component __instance, ref System.Collections.IEnumerable __result)
        {
            try
            {
                if (!Scanner.ResolveTypes()) return;

                var go = __instance.gameObject;
                if (!Scanner.ShouldMark(go)) return;

                var ma = Scanner.EnsureMarkerAttachment(go);
                if (ma == null) return;

                // If __result already contains our MarkerAttachment, leave it alone.
                bool alreadyIn = false;
                foreach (var a in __result)
                {
                    if (ReferenceEquals(a, ma)) { alreadyIn = true; break; }
                }
                if (alreadyIn) return;

                // Concat ma into the IEnumerable. We materialise to a List of IAttachmentSpec because
                // the original may be a deferred enumerable that's not safe to enumerate twice.
                var spec = Scanner.IAttachmentSpecType;
                var listType = typeof(List<>).MakeGenericType(spec);
                var list = (System.Collections.IList)Activator.CreateInstance(listType);
                foreach (var a in __result) list.Add(a);
                list.Add(ma);

                // Cast list to IEnumerable<IAttachmentSpec> via the typed list (already implements it).
                __result = (System.Collections.IEnumerable)list;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[StationIndicator] Postfix on GetAttachments crashed for '{__instance?.gameObject?.name}': {e}");
            }
        }
    }
}
