using System;
using System.Collections.Generic;
using Awaken.TG.Main.Fights.DamageInfo;
using Awaken.TG.Main.General.StatTypes;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.Weapons;
using Awaken.TG.Main.Heroes.Stats;
using Awaken.TG.Main.Heroes.Stats.Tweaks;
using Newtonsoft.Json;

namespace BuildAdvisor
{
    // Compare current build against several what-if scenarios.
    // Uses temporary tweaks to MeleeDamageMultiplier / IncomingDamage / etc., then restores.
    public static class DpsProbe
    {
        public static string Run()
        {
            try
            {
                var hero = Hero.Current;
                if (hero == null) return "{}";

                var main = hero.HeroItems.ItemInSlots[EquipmentSlotType.MainHand];
                if (main == null) return JsonConvert.SerializeObject(new { error = "no mainhand" });
                var itemStats = main.TryGetElement<ItemStats>();
                if (itemStats == null) return JsonConvert.SerializeObject(new { error = "no itemStats" });

                var results = new List<object>();

                // Baseline
                results.Add(Scenario(hero, itemStats, "baseline (current)", null));

                // Scenario A: +20% via Exploitation of Afflictions 1→3 (always-on with poison/bleed weapon)
                results.Add(Scenario(hero, itemStats, "+ExploitationOfAfflictions max (vs status'd)", h =>
                {
                    return new[] {
                        // Exploitation actually applies as IncomingDamage on target. Simulate as MeleeDamageMul.
                        new StatTweak(h.CharacterStats.MeleeDamageMultiplier, 0.20f, TweakPriority.AddPreMultiply, OperationType.Add, h)
                    };
                }));

                // Scenario B: +20% via First Blood 0→2 at full stamina
                results.Add(Scenario(hero, itemStats, "+FirstBlood 0→2 at full stamina", h =>
                {
                    return new[] {
                        new StatTweak(h.CharacterStats.MeleeDamageMultiplier, 0.20f, TweakPriority.AddPreMultiply, OperationType.Add, h)
                    };
                }));

                // Scenario C: BOTH talents stacked
                results.Add(Scenario(hero, itemStats, "+Exploitation +FirstBlood (both)", h =>
                {
                    return new[] {
                        new StatTweak(h.CharacterStats.MeleeDamageMultiplier, 0.20f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                        new StatTweak(h.CharacterStats.MeleeDamageMultiplier, 0.20f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                    };
                }));

                // Scenario D: +4 PER stat points (CritChance +0.04, CritDmg +0.20)
                results.Add(Scenario(hero, itemStats, "+4 PER stat points (crit)", h =>
                {
                    return new[] {
                        new StatTweak(h.HeroStats.CriticalChance, 0.04f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                        new StatTweak(h.HeroStats.CriticalDamageMultiplier, 0.20f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                    };
                }));

                // Scenario E: +4 END (HP, no direct DPS)
                results.Add(Scenario(hero, itemStats, "+4 END stat points", h =>
                {
                    return new[] {
                        new StatTweak(h.AliveStats.MaxHealth, 32f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                    };
                }));

                // Scenario F: 2 PER + 2 END mix
                results.Add(Scenario(hero, itemStats, "+2 PER +2 END mix", h =>
                {
                    return new[] {
                        new StatTweak(h.HeroStats.CriticalChance, 0.02f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                        new StatTweak(h.HeroStats.CriticalDamageMultiplier, 0.10f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                        new StatTweak(h.AliveStats.MaxHealth, 16f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                    };
                }));

                // Scenario G: ALL combined — both talents + 2 PER + 2 END
                results.Add(Scenario(hero, itemStats, "FULL: talents + 2 PER + 2 END", h =>
                {
                    return new[] {
                        new StatTweak(h.CharacterStats.MeleeDamageMultiplier, 0.20f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                        new StatTweak(h.CharacterStats.MeleeDamageMultiplier, 0.20f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                        new StatTweak(h.HeroStats.CriticalChance, 0.02f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                        new StatTweak(h.HeroStats.CriticalDamageMultiplier, 0.10f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                        new StatTweak(h.AliveStats.MaxHealth, 16f, TweakPriority.AddPreMultiply, OperationType.Add, h),
                    };
                }));

                return JsonConvert.SerializeObject(results, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message, stack = e.StackTrace });
            }
        }

        static object Scenario(Hero hero, ItemStats itemStats, string name, Func<Hero, StatTweak[]> tweaker)
        {
            var tweaks = tweaker?.Invoke(hero);

            try
            {
                Damage.GetStatModifiers(hero, itemStats, out float multMod, out float linMod);

                float wpnMin = itemStats.BaseMinDmg.ModifiedValue;
                float wpnMax = itemStats.BaseMaxDmg.ModifiedValue;
                float heavyMul = itemStats.HeavyAttackDamageMultiplier?.ModifiedValue ?? 1f;
                float lightMin = (wpnMin + linMod) * multMod;
                float lightMax = (wpnMax + linMod) * multMod;
                float heavyMin = lightMin * heavyMul;
                float heavyMax = lightMax * heavyMul;
                float globalAS = hero.HeroStats.AttackSpeed.ModifiedValue;
                float lSpd = (hero.HeroStats.TwoHandedLightAttackSpeed?.ModifiedValue ?? 1f) + globalAS - 1f;
                float hSpd = (hero.HeroStats.TwoHandedHeavyAttackSpeed?.ModifiedValue ?? 0.9f) + globalAS - 1f;

                float crit = hero.HeroStats.CriticalChance.ModifiedValue;
                float critMul = 1f + hero.HeroStats.CriticalDamageMultiplier.ModifiedValue;

                // Effective per-swing avg accounting for crits
                float lightAvg = (lightMin + lightMax) * 0.5f;
                float heavyAvg = (heavyMin + heavyMax) * 0.5f;
                float effLight = lightAvg * (1f - crit + crit * critMul);
                float effHeavy = heavyAvg * (1f - crit + crit * critMul);

                // Dual-wield: each blade hits separately. multiplier x2.
                float lightDPS = effLight * lSpd * 2f;
                float heavyDPS = effHeavy * hSpd * 2f;

                float armor = hero.TotalArmor(DamageSubType.GenericPhysical);
                float dr = Math.Min(armor / 100f, 0.95f);
                float incomingDamage = hero.CharacterStats.IncomingDamage.ModifiedValue;
                float effHP = hero.MaxHealth.ModifiedValue / Math.Max(0.01f, (1f - dr) * incomingDamage);

                return new
                {
                    name,
                    multStatMod = multMod,
                    meleeDmgMul = hero.CharacterStats.MeleeDamageMultiplier.ModifiedValue,
                    crit, critMul = critMul - 1f,
                    perSwing = new { lightAvg, heavyAvg, effLight, effHeavy },
                    dps = new { light = lightDPS, heavy = heavyDPS, dualWielded = true },
                    survival = new { maxHP = hero.MaxHealth.ModifiedValue, armor, dr, incomingDamage, effHP },
                };
            }
            finally
            {
                if (tweaks != null) foreach (var t in tweaks) try { t?.Discard(); } catch { }
            }
        }
    }
}
