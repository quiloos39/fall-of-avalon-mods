using System;
using System.Collections.Generic;
using System.Linq;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Development.Talents;
using Newtonsoft.Json;

namespace BuildAdvisor
{
    public static class TalentDescProbe
    {
        // Returns descriptions for a curated set of high-priority talents the user
        // might consider next, plus parents of the current taken talents.
        public static string Run()
        {
            try
            {
                var hero = Hero.Current;
                if (hero == null) return "{}";

                // Templates we want full details for
                var wanted = new HashSet<string>(StringComparer.Ordinal)
                {
                    // PRA armor branch (untaken / partially taken)
                    "PRA_Talent_EnduranceTraining",
                    "PRA_Talent_IncreaseArmorMultiplier",
                    "PRA_Talent_IncreaseArmorFlat",
                    "PRA_Talent_EnduranceIncreasesDamage",
                    "PRA_Talent_HedgehogKnight",
                    "PRA_Talent_ToughenedByPain",
                    // PRA status branch
                    "PRA_Talent_ExploitationOfAfflictions",
                    "PRA_Talent_EnemiesWithStatusTakeMoreDamage",
                    // STR two-handed branch
                    "STR_Talent_TwoHandedHeavyHitsAreCheaper",
                    "STR_Talent_TwoHandedBonusDamageOnHeavyAttack",
                    "STR_Talent_TwoHandedHeavyHitsChanceToStun",
                    "STR_Talent_TwoHandedHeavyHitsBonusDMGWhenEnemyBelowStatThreshold",
                    "STR_Talent_HeavyAttackTwoHandedAOE",
                    // STR general
                    "STR_Talent_IncreasePhysicalDMG",
                    "STR_Talent_IncreaseStatByWeaponStat",
                    "STR_Talent_StaggeredEnemiesTakeMoreDamage",
                    "STR_Talent_BelowHPGainDMGBoost",
                    // PER crit branch
                    "PER_Talent_ExtraCritChance",
                    "PER_Talent_ExtraCritDamage",
                    "PER_Talent_ConsecutiveHitsCritChance",
                    "PER_Talent_CritsApplyDamageReduction",
                    "PER_Talent_CritRestoreStamina",
                    // END
                    "END_Talent_MaxHealth",
                    "END_Talent_KillsIncreaseDamage",
                    "END_Talent_IncreaseDamageForPointOfMissingHealth",
                    "END_Talent_TakingDamageHealsStamina",
                    "END_Talent_OnKillRestoreMaxStaminaMultiplier",
                };

                var results = new List<object>();
                foreach (var table in hero.Talents.BaseTalentTables)
                {
                    foreach (var t in table.talents)
                    {
                        try
                        {
                            var n = t.Template?.name;
                            if (n == null) continue;
                            // Match either exact name or a partial match on key tokens
                            // Always include zero-level talents in PRA Armor / END Health/Stamina / STR / PER for full survey
                            bool match = wanted.Contains(n);
                            if (!match && t.Level == 0)
                            {
                                if (n.StartsWith("PRA_Talent_") || n.StartsWith("END_Talent_") ||
                                    n.StartsWith("STR_Talent_") || n.StartsWith("PER_Talent_"))
                                {
                                    match = true;
                                }
                            }
                            if (!match)
                            {
                                if (n.Contains("EnduranceTraining") || n.Contains("ToughenedByPain") ||
                                    n.Contains("Furious") || n.Contains("FirstBlood") ||
                                    n.Contains("Cruelty") || n.Contains("Carnage") ||
                                    n.Contains("Culling") || n.Contains("FuryWithoutFatigue") ||
                                    n.Contains("UnstoppableImpact") || n.Contains("Executioner") ||
                                    n.Contains("DeepWounds") || n.Contains("GoForTheEyes") ||
                                    n.Contains("ExtraLucky") || n.Contains("RespiteInCruelty") ||
                                    n.Contains("WeaponsOfGreatHeft") || n.Contains("GodlyPhysique") ||
                                    n.Contains("LastStand") || n.Contains("RecklessFrenzy") ||
                                    n.Contains("DeadlyFury") || n.Contains("BloodthirstHeavy") ||
                                    n.Contains("StackingPeril") || n.Contains("RisingScourge"))
                                {
                                    match = true;
                                }
                            }
                            if (!match) continue;

                            var template = t.Template;
                            int max = t.MaxLevel;
                            var perRank = new List<string>();
                            for (int r = 1; r <= max; r++)
                            {
                                try { perRank.Add(template.GetLevel(r).Description(t, r)); } catch { perRank.Add("?"); }
                            }
                            string nameLoc = "";
                            try { nameLoc = template.Name; } catch { }
                            results.Add(new
                            {
                                templateRef = n,
                                name = nameLoc,
                                level = t.Level,
                                maxLevel = max,
                                parentRef = t.Parent?.name,
                                descByRank = perRank,
                            });
                        }
                        catch { }
                    }
                }
                return JsonConvert.SerializeObject(results, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message, stack = e.StackTrace });
            }
        }
    }
}
