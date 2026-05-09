using System;
using System.Collections.Generic;
using System.Linq;
using Awaken.TG.Main.Character;
using Awaken.TG.Main.Fights.DamageInfo;
using Awaken.TG.Main.General.StatTypes;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.Weapons;
using Awaken.TG.Main.Heroes.Stats;
using Newtonsoft.Json;

namespace BuildAdvisor
{
    public static class DeepProbe
    {
        public static string Run()
        {
            try
            {
                var hero = Hero.Current;
                if (hero == null) return "{\"error\":\"no hero\"}";

                var result = new Dictionary<string, object>();

                // 1. Damage formula using actual game function
                var mainHand = hero.HeroItems.ItemInSlots[EquipmentSlotType.MainHand];
                if (mainHand != null)
                {
                    var stats = mainHand.TryGetElement<ItemStats>();
                    if (stats != null)
                    {
                        Damage.GetStatModifiers(hero, stats, out float multStatMod, out float linearStatMod);
                        var reqs = mainHand.TryGetElement<ItemStatsRequirements>();
                        int missing = reqs?.MissingRequirementPoints ?? 0;
                        bool reqsMet = reqs?.RequirementsMet ?? true;
                        float reqPenalty = ItemRequirementsUtils.GetDamageMultiplier(hero, mainHand);

                        float minBase = stats.BaseMinDmg.ModifiedValue;
                        float maxBase = stats.BaseMaxDmg.ModifiedValue;
                        float minFinal = (minBase + linearStatMod) * multStatMod;
                        float maxFinal = (maxBase + linearStatMod) * multStatMod;

                        result["damageBreakdown"] = new
                        {
                            weapon = mainHand.DisplayName,
                            template = mainHand.Template?.name,
                            level = mainHand.Level?.ModifiedInt ?? 0,
                            baseMin = minBase,
                            baseMax = maxBase,
                            multStatModifier = multStatMod,
                            linearStatModifier = linearStatMod,
                            reqsMet = reqsMet,
                            missingReqPoints = missing,
                            reqPenalty = reqPenalty,
                            heavyAttackMul = stats.HeavyAttackDamageMultiplier?.ModifiedValue,
                            armorPen = stats.ArmorPenetration?.ModifiedValue,
                            backStabMul = stats.BackStabDamageMultiplier?.ModifiedValue,
                            finalMinLight = minFinal,
                            finalMaxLight = maxFinal,
                            finalMinHeavy = minFinal * (stats.HeavyAttackDamageMultiplier?.ModifiedValue ?? 1f),
                            finalMaxHeavy = maxFinal * (stats.HeavyAttackDamageMultiplier?.ModifiedValue ?? 1f),
                        };

                        result["weaponRequirements"] = new
                        {
                            str = reqs?.StrengthRequired?.ModifiedValue,
                            dex = reqs?.DexterityRequired?.ModifiedValue,
                            end = reqs?.EnduranceRequired?.ModifiedValue,
                            spi = reqs?.SpiritualityRequired?.ModifiedValue,
                            per = reqs?.PerceptionRequired?.ModifiedValue,
                            pra = reqs?.PracticalityRequired?.ModifiedValue,
                        };
                    }
                }

                // 2. Full armor breakdown
                var armorSlots = new[]
                {
                    EquipmentSlotType.Helmet, EquipmentSlotType.Cuirass, EquipmentSlotType.Gauntlets,
                    EquipmentSlotType.Greaves, EquipmentSlotType.Boots, EquipmentSlotType.Back,
                };
                float totalArmor = 0f;
                float totalWeight = 0f;
                var armorDetails = new List<object>();
                foreach (var slot in armorSlots)
                {
                    var item = hero.HeroItems.ItemInSlots[slot];
                    if (item == null) continue;
                    var stats = item.TryGetElement<ItemStats>();
                    if (stats == null) continue;
                    float armor = stats.Armor?.ModifiedValue ?? 0f;
                    float weight = stats.Weight?.ModifiedValue ?? 0f;
                    float reduction = ItemRequirementsUtils.GetArmorReductionMultiplier(hero, item);
                    float effectiveArmor = armor * reduction;
                    totalArmor += effectiveArmor;
                    totalWeight += weight;
                    armorDetails.Add(new
                    {
                        slot = slot.EnumName,
                        name = item.DisplayName,
                        template = item.Template?.name,
                        armorBase = armor,
                        reductionMul = reduction,
                        armorEffective = effectiveArmor,
                        weight = weight,
                        level = item.Level?.ModifiedInt ?? 0,
                    });
                }
                result["armor"] = new
                {
                    total = totalArmor,
                    totalWeight = totalWeight,
                    pieces = armorDetails,
                    bestOffenseDamageBonus = totalArmor * 0.20f,
                };

                // 3. Hero current armor stat
                try
                {
                    float baseArmor = hero.AliveStats.Armor.ModifiedValue;
                    float armorMul = hero.AliveStats.ArmorMultiplier.ModifiedValue;
                    float realTotalArmor = hero.TotalArmor(Awaken.TG.Main.Fights.DamageInfo.DamageSubType.GenericPhysical);
                    float damageReduction = System.Math.Min(0.95f, realTotalArmor / 100f);
                    result["heroArmorStat"] = new
                    {
                        baseArmor,
                        armorMultiplier = armorMul,
                        totalArmor = realTotalArmor,
                        damageReductionPct = damageReduction * 100f,
                        incomingDamageMul = hero.CharacterStats.IncomingDamage.ModifiedValue,
                        meleeDmgMul = hero.CharacterStats.MeleeDamageMultiplier.ModifiedValue,
                    };
                }
                catch (Exception e) { result["armorErr"] = e.Message; }

                // 4. Test damage calculation with logging enabled
                try
                {
                    bool prevLog = RawDamageData.showCalculationLogs;
                    RawDamageData.showCalculationLogs = true;

                    if (mainHand != null)
                    {
                        var stats = mainHand.TryGetElement<ItemStats>();
                        Damage.GetStatModifiers(hero, stats, out float m, out float l);
                        var raw = new RawDamageData(stats.BaseMinDmg.ModifiedValue, m, l);
                        raw.AddMultModifier(0.0f); // simulate
                        raw.FinalCalculation(false);
                        result["rawCalcResult"] = new
                        {
                            uncalculated = stats.BaseMinDmg.ModifiedValue,
                            multMod = m,
                            linearMod = l,
                            finalLight = raw.CalculatedValue,
                        };
                    }

                    RawDamageData.showCalculationLogs = prevLog;
                }
                catch (Exception e)
                {
                    result["rawCalcError"] = e.Message;
                }

                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message, stack = e.StackTrace });
            }
        }
    }
}
