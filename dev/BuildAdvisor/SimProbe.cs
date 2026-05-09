using System;
using System.Collections.Generic;
using Awaken.TG.Main.Fights.DamageInfo;
using Awaken.TG.Main.General.StatTypes;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.Weapons;
using Awaken.TG.Main.Heroes.Stats;
using Newtonsoft.Json;

namespace BuildAdvisor
{
    public static class SimProbe
    {
        public static string Run()
        {
            try
            {
                var hero = Hero.Current;
                if (hero == null) return "{\"error\":\"no hero\"}";

                var mainHand = hero.HeroItems.ItemInSlots[EquipmentSlotType.MainHand];
                if (mainHand == null) return "{\"error\":\"no main hand\"}";
                var stats = mainHand.TryGetElement<ItemStats>();
                if (stats == null) return "{\"error\":\"no item stats\"}";

                // Save current
                float oldStr = hero.HeroRPGStats.Strength.BaseValue;
                float oldDex = hero.HeroRPGStats.Dexterity.BaseValue;
                float oldEnd = hero.HeroRPGStats.Endurance.BaseValue;

                var result = new Dictionary<string, object>();

                // Snapshot CURRENT
                Damage.GetStatModifiers(hero, stats, out float curMul, out float curLin);
                result["current"] = Snapshot(hero, stats, curMul, curLin);

                // Apply TANK build stats: STR 20, DEX 6, END 21
                hero.HeroRPGStats.Strength.SetTo(20f);
                hero.HeroRPGStats.Dexterity.SetTo(6f);
                hero.HeroRPGStats.Endurance.SetTo(21f);

                // Re-read with tank stats
                Damage.GetStatModifiers(hero, stats, out float newMul, out float newLin);
                result["tankStatsOnly"] = Snapshot(hero, stats, newMul, newLin);

                // Restore
                hero.HeroRPGStats.Strength.SetTo(oldStr);
                hero.HeroRPGStats.Dexterity.SetTo(oldDex);
                hero.HeroRPGStats.Endurance.SetTo(oldEnd);

                // Verify restoration
                Damage.GetStatModifiers(hero, stats, out float restoredMul, out float restoredLin);
                result["restored"] = Snapshot(hero, stats, restoredMul, restoredLin);
                result["restoredOk"] = Math.Abs(restoredMul - curMul) < 0.001f;

                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message, stack = e.StackTrace });
            }
        }

        static object Snapshot(Hero h, ItemStats s, float multMod, float linearMod)
        {
            float wpnMin = s.BaseMinDmg.ModifiedValue;
            float wpnMax = s.BaseMaxDmg.ModifiedValue;
            float heavyMul = s.HeavyAttackDamageMultiplier?.ModifiedValue ?? 1f;
            float minLight = (wpnMin + linearMod) * multMod;
            float maxLight = (wpnMax + linearMod) * multMod;
            float minHeavy = minLight * heavyMul;
            float maxHeavy = maxLight * heavyMul;
            float weaponSpeedHeavy = h.HeroStats.TwoHandedHeavyAttackSpeed?.ModifiedValue ?? 0.9f;
            float weaponSpeedLight = h.HeroStats.TwoHandedLightAttackSpeed?.ModifiedValue ?? 1.0f;
            float globalAS = h.HeroStats.AttackSpeed.ModifiedValue;
            float effLight = weaponSpeedLight + globalAS - 1f;
            float effHeavy = weaponSpeedHeavy + globalAS - 1f;

            return new
            {
                str_rpg = h.HeroRPGStats.Strength.ModifiedValue,
                dex_rpg = h.HeroRPGStats.Dexterity.ModifiedValue,
                end_rpg = h.HeroRPGStats.Endurance.ModifiedValue,
                charStrength = h.CharacterStats.Strength.ModifiedValue,
                meleeDmgMul = h.CharacterStats.MeleeDamageMultiplier.ModifiedValue,
                twoHandedDmgMul = h.CharacterStats.TwoHandedMeleeDamageMultiplier.ModifiedValue,
                attackSpeed = globalAS,
                multStatMod = multMod,
                linearStatMod = linearMod,
                weaponBaseMin = wpnMin,
                weaponBaseMax = wpnMax,
                heavyAttackMul = heavyMul,
                effLightSpeed = effLight,
                effHeavySpeed = effHeavy,
                lightDmgMin = minLight,
                lightDmgMax = maxLight,
                heavyDmgMin = minHeavy,
                heavyDmgMax = maxHeavy,
                lightDPS = (minLight + maxLight) * 0.5f * effLight,
                heavyDPS = (minHeavy + maxHeavy) * 0.5f * effHeavy,
                maxHealth = h.MaxHealth.ModifiedValue,
                maxStamina = h.CharacterStats.MaxStamina.ModifiedValue,
            };
        }
    }
}
