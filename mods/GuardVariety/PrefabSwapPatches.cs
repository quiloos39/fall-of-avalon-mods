using System;
using HarmonyLib;
using Awaken.TG.Assets;
using Awaken.TG.Main.Fights.Factions.Crimes;
using Awaken.TG.Main.Fights.NPCs;

namespace GuardVariety
{
    // Tier 3 — swap a guard's visualPrefab to a female humanoid prefab discovered
    // from the player's exploration history. Runs in NpcElement.InitFromAttachment
    // postfix, BEFORE LoadVisual fires, so the Addressables system loads the
    // alternate prefab in the first place.
    //
    // The swap decision is deterministic per NPC (seed = hash(template + Model.ID))
    // so a given guard either always swaps or never swaps, with the same female
    // prefab pick each load. Visual armor mismatches are accepted by design — the
    // user opted in.
    [HarmonyPatch(typeof(NpcElement), nameof(NpcElement.InitFromAttachment))]
    internal static class NpcElement_InitFromAttachment_PrefabSwap_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(NpcElement __instance, NpcAttachment spec, bool isRestored)
        {
            try
            {
                if (Plugin.Cfg == null || !Plugin.Cfg.Enabled.Value) return;
                if (!Plugin.Cfg.SwapToFemalePrefab.Value) return;

                var template = __instance?.Template;
                if (template == null) return;

                if (!IsTargetArchetype(template.CrimeReactionArchetype)) return;
                if (__instance.IsUnique && !Plugin.Cfg.RandomizeUniqueGuards.Value) return;

                int seed = ComputeSeed(__instance);

                // Probabilistic swap; deterministic from same seed used by
                // BodyFeatures randomization, so a guard with skin tone X also
                // gets the female-prefab decision based on the same identity.
                float roll = ((uint)seed % 10000u) / 10000f;
                if (roll >= Plugin.Cfg.SwapToFemaleProbability.Value) return;

                string femaleAddress = PrefabRegistry.PickFemale(seed);
                if (string.IsNullOrEmpty(femaleAddress))
                {
                    // The roll said we WOULD swap this guard but the registry
                    // has no female prefab yet. Verbose-only — once registry
                    // is populated this never fires.
                    if (Plugin.Cfg.Verbose.Value)
                        Plugin.Log.LogInfo(
                            $"[GuardVariety][Swap] {SafeName(__instance)} would swap, " +
                            $"but no Female prefab in registry yet " +
                            $"(have {PrefabRegistry.FemaleCount}F, {PrefabRegistry.MaleCount}M).");
                    return;
                }

                string originalAddress = __instance._visualPrefab?.Address ?? __instance._baseVisualPrefab?.Address;
                if (string.IsNullOrEmpty(originalAddress)) return;
                if (string.Equals(originalAddress, femaleAddress, StringComparison.Ordinal)) return; // already female

                // Replace _visualPrefab with our female pick. Clear the conditional
                // path so LateAssignVisualPrefab doesn't override us with a
                // story-flag-gated version of the original.
                __instance._visualPrefab = new ARAssetReference(femaleAddress);
                __instance._baseVisualPrefab = null;
                __instance._conditionalVisualPrefabs = null;

                if (Plugin.Cfg.Verbose.Value)
                    Plugin.Log.LogInfo(
                        $"[GuardVariety][Swap] {SafeName(__instance)} " +
                        $"({originalAddress}) -> female ({femaleAddress}) " +
                        $"isRestored={isRestored}, seed={seed}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[GuardVariety][Swap] failed: {e.GetBaseException().Message}");
            }
        }

        private static bool IsTargetArchetype(CrimeReactionArchetype archetype)
        {
            if (archetype == CrimeReactionArchetype.Guard) return true;
            if (Plugin.Cfg.AlsoRandomizeDefenders.Value && archetype == CrimeReactionArchetype.Defender) return true;
            return false;
        }

        private static int ComputeSeed(NpcElement npc)
        {
            // Match the seed used by GuardVarietyPatches so identity is stable.
            string key = (npc.Template?.name ?? "?") + "|" + (npc.ID ?? "?");
            unchecked
            {
                int hash = 23;
                foreach (char c in key) hash = hash * 31 + c;
                return hash;
            }
        }

        private static string SafeName(NpcElement npc)
        {
            try { return npc?.Template?.name ?? "<unnamed>"; }
            catch { return "<error>"; }
        }
    }
}
