using System;
using HarmonyLib;
using UnityEngine;
using Awaken.TG.Main.Character.Features;
using Awaken.TG.Main.Fights.Factions.Crimes;
using Awaken.TG.Main.Fights.NPCs;

namespace GuardVariety
{
    // Postfixes NpcInitializer.InitClothesAndBodyFeatures, which runs once per NPC
    // visual-load (NpcInitializer.cs:122). At that point BodyFeatures is added,
    // Gender has been read from the prefab's NpcGenderMarker, and (for fresh
    // spawns) BlockRandomization is still false — the right window to drive the
    // existing NPCRandomConfigSO.RandomizeFeatures(features) pathway.
    //
    // For save-restored guards, BodyFeatures is rehydrated with
    // BlockRandomization=true, so without ForceRandomize we'd skip them entirely.
    // ForceRandomize=true (default) re-randomizes regardless; StableSeed=true
    // (default) seeds UnityEngine.Random by NPC identity so each guard rolls
    // the same face every time.
    [HarmonyPatch(typeof(NpcInitializer), "InitClothesAndBodyFeatures")]
    internal static class NpcInitializer_InitClothesAndBodyFeatures_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(NpcInitializer __instance)
        {
            try
            {
                if (Plugin.Cfg == null || !Plugin.Cfg.Enabled.Value) return;

                var npc = __instance?.Npc;
                if (npc == null) return;

                var template = npc.Template;
                if (template == null) return;

                var features = npc.Element<BodyFeatures>();

                // Tier 3 — feed the prefab registry from EVERY NPC (not just
                // guards) so female villagers / priests / merchants populate the
                // pool that guards later swap into. Done before the archetype
                // filter; safe to repeat (Register dedupes).
                if (features != null && Plugin.Cfg.SwapToFemalePrefab.Value)
                {
                    var addr = npc._visualPrefab?.Address;
                    if (!string.IsNullOrEmpty(addr) && features.Gender != Gender.None)
                    {
                        PrefabRegistry.Register(addr, features.Gender);
                    }
                }

                if (!IsTargetArchetype(template.CrimeReactionArchetype))
                {
                    LogSkip(npc, $"archetype={template.CrimeReactionArchetype}");
                    return;
                }
                if (npc.IsUnique && !Plugin.Cfg.RandomizeUniqueGuards.Value)
                {
                    LogSkip(npc, "unique=true and RandomizeUniqueGuards=false");
                    return;
                }

                if (features == null)
                {
                    LogSkip(npc, "no BodyFeatures element");
                    return;
                }

                bool wasBlocked = features.BlockRandomization;
                if (wasBlocked && !Plugin.Cfg.ForceRandomize.Value)
                {
                    LogSkip(npc, "BlockRandomization=true and ForceRandomize=false");
                    return;
                }

                // Gender-matched config so female-marked guards (including those
                // we swapped via Tier 3) get female blendshapes/hair instead of
                // masculine ones from Features_MaleRandom.
                var config = RandomConfigPool.GetForGender(features.Gender);
                if (config == null)
                {
                    if (Plugin.Cfg.Verbose.Value)
                        Plugin.Log.LogWarning($"[GuardVariety] No NPCRandomConfigSO loaded yet; skipping {SafeName(npc)}.");
                    return;
                }

                ApplyRandomization(npc, features, config, wasBlocked);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[GuardVariety] Postfix failed: {e.GetBaseException().Message}");
            }
        }

        private static void ApplyRandomization(NpcElement npc, BodyFeatures features, Awaken.TG.Main.Character.Features.Config.NPCRandomConfigSO config, bool wasBlocked)
        {
            // Required for our re-roll path: RandomizeFeatures only sets fields,
            // it doesn't touch BlockRandomization. BUT setters like features.SkinColor
            // call ChangeMutableFeature which, if IsShown==true (i.e. NPC already in
            // visual band), releases the old feature and spawns the new one. So we
            // need MutableSetterAsyncLock=false during the call (default state).

            UnityEngine.Random.State savedState = default;
            bool seeded = false;
            if (Plugin.Cfg.StableSeed.Value)
            {
                savedState = UnityEngine.Random.state;
                UnityEngine.Random.InitState(ComputeSeed(npc));
                seeded = true;
            }

            try
            {
                // Temporarily clear BlockRandomization so any internal guard inside
                // RandomizeFeatures (and downstream view code) doesn't short-circuit.
                features.BlockRandomization = false;
                config.RandomizeFeatures(features);
            }
            finally
            {
                if (seeded) UnityEngine.Random.state = savedState;
            }

            if (Plugin.Cfg.ClearBeardOnFemale.Value && features.Gender == Gender.Female)
            {
                features.Beard = null;
            }

            // Lock so any NpcRandomizer ViewComponent on the prefab won't re-roll.
            features.BlockRandomization = true;

            if (Plugin.Cfg.Verbose.Value)
            {
                Plugin.Log.LogInfo(
                    $"[GuardVariety] Randomized {SafeName(npc)} " +
                    $"(gender={features.Gender}, archetype={npc.Template.CrimeReactionArchetype}, " +
                    $"unique={npc.IsUnique}, wasBlocked={wasBlocked}, seed={(seeded ? ComputeSeed(npc).ToString() : "global")}) " +
                    $"using config '{config.name}' " +
                    $"=> skin={ColorString(features.SkinColor?.Color)}, " +
                    $"eyes={ColorString(features.Eyes?.Color)}, " +
                    $"hair={(features.Hair != null ? "set" : "null")}, " +
                    $"beard={(features.Beard != null ? "set" : "null")}");
            }
        }

        private static int ComputeSeed(NpcElement npc)
        {
            // Stable per-NPC seed. Combine template name + Model.ID so:
            //  - Different templates of guards differ from each other
            //  - Two instances of the same template (e.g. crowd guards) differ
            //  - Same NPC across loads gets the same face (ID is [Saved])
            string key = (npc.Template?.name ?? "?") + "|" + (npc.ID ?? "?");
            unchecked
            {
                int hash = 23;
                foreach (char c in key) hash = hash * 31 + c;
                return hash;
            }
        }

        private static string ColorString(Color? c) => c.HasValue ? $"#{ColorUtility.ToHtmlStringRGB(c.Value)}" : "null";

        private static bool IsTargetArchetype(CrimeReactionArchetype archetype)
        {
            if (archetype == CrimeReactionArchetype.Guard) return true;
            if (Plugin.Cfg.AlsoRandomizeDefenders.Value && archetype == CrimeReactionArchetype.Defender) return true;
            return false;
        }

        private static string SafeName(NpcElement npc)
        {
            try { return npc?.Name ?? npc?.Template?.name ?? "<unnamed>"; }
            catch { return "<error>"; }
        }

        private static void LogSkip(NpcElement npc, string reason)
        {
            if (Plugin.Cfg.Verbose.Value)
                Plugin.Log.LogInfo($"[GuardVariety] Skipped {SafeName(npc)} ({reason})");
        }
    }
}
