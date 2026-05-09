using System;
using System.Collections.Generic;
using System.Linq;
using Awaken.TG.Main.Character;
using Awaken.TG.Main.Crafting.Recipes;
using Awaken.TG.Main.General.StatTypes;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.Weapons;
using Awaken.TG.Main.Heroes.Stats;
using Newtonsoft.Json;

namespace BuildAdvisor
{
    public static class Probe
    {
        public static string Run()
        {
            try
            {
                var hero = Hero.Current;
                if (hero == null) return JsonConvert.SerializeObject(new { error = "Hero.Current is null (in main menu?)" });

                var rpg = hero.HeroRPGStats;
                var prof = hero.ProficiencyStats;
                var cs = hero.CharacterStats;
                var dev = hero.Development;
                var hs = hero.HeroStats;

                var dump = new Dictionary<string, object>
                {
                    ["level"] = SafeStat(cs?.Level),
                    ["xp"] = SafeStat(hs?.XP),
                    ["pointsAvailable"] = new
                    {
                        baseStat = SafeStat(hs?.BaseStatPoints),
                        talent = SafeStat(hs?.TalentPoints),
                        catalyst = SafeStat(hs?.CatalystTalentPoints),
                        wyrdShards = SafeStat(hs?.WyrdMemoryShards),
                        wyrdWhispers = SafeStat(hs?.WyrdWhispers),
                    },
                    ["attributes"] = new
                    {
                        Strength = SafeStat(rpg?.Strength),
                        Dexterity = SafeStat(rpg?.Dexterity),
                        Spirituality = SafeStat(rpg?.Spirituality),
                        Perception = SafeStat(rpg?.Perception),
                        Endurance = SafeStat(rpg?.Endurance),
                        Practicality = SafeStat(rpg?.Practicality),
                    },
                    ["proficiencies"] = new
                    {
                        OneHanded = SafeStat(prof?.OneHanded),
                        TwoHanded = SafeStat(prof?.TwoHanded),
                        Unarmed = SafeStat(prof?.Unarmed),
                        Shield = SafeStat(prof?.Shield),
                        Athletics = SafeStat(prof?.Athletics),
                        LightArmor = SafeStat(prof?.LightArmor),
                        MediumArmor = SafeStat(prof?.MediumArmor),
                        HeavyArmor = SafeStat(prof?.HeavyArmor),
                        Archery = SafeStat(prof?.Archery),
                        Evasion = SafeStat(prof?.Evasion),
                        Acrobatics = SafeStat(prof?.Acrobatics),
                        Sneak = SafeStat(prof?.Sneak),
                        Theft = SafeStat(prof?.Theft),
                        Magic = SafeStat(prof?.Magic),
                        Alchemy = SafeStat(prof?.Alchemy),
                        Cooking = SafeStat(prof?.Cooking),
                        Handcrafting = SafeStat(prof?.Handcrafting),
                    },
                    ["combatMultipliers"] = new
                    {
                        MeleeDmgMul = SafeStat(cs?.MeleeDamageMultiplier),
                        OneHandedDmgMul = SafeStat(cs?.OneHandedMeleeDamageMultiplier),
                        TwoHandedDmgMul = SafeStat(cs?.TwoHandedMeleeDamageMultiplier),
                        UnarmedDmgMul = SafeStat(cs?.UnarmedMeleeDamageMultiplier),
                        RangedDmgMul = SafeStat(cs?.RangedDamageMultiplier),
                        MagicStrength = SafeStat(cs?.MagicStrength),
                        Strength = SafeStat(cs?.Strength),
                        StrengthLinear = SafeStat(cs?.StrengthLinear),
                        BuffStrength = SafeStat(cs?.BuffStrength),
                        DebuffStrength = SafeStat(cs?.DebuffStrength),
                        Resistance = SafeStat(cs?.Resistance),
                        LifeSteal = SafeStat(cs?.LifeSteal),
                        IncomingDamage = SafeStat(cs?.IncomingDamage),
                    },
                    ["attackSpeed"] = new
                    {
                        AttackSpeed = SafeStat(hs?.AttackSpeed),
                        OneHandedLight = SafeStat(hs?.OneHandedLightAttackSpeed),
                        OneHandedHeavy = SafeStat(hs?.OneHandedHeavyAttackSpeed),
                        TwoHandedLight = SafeStat(hs?.TwoHandedLightAttackSpeed),
                        TwoHandedHeavy = SafeStat(hs?.TwoHandedHeavyAttackSpeed),
                    },
                    ["crit"] = new
                    {
                        CriticalChance = SafeStat(hs?.CriticalChance),
                        CriticalDamageMultiplier = SafeStat(hs?.CriticalDamageMultiplier),
                        WeakSpotDmgMul = SafeStat(hs?.WeakSpotDamageMultiplier),
                        SneakDmgMul = SafeStat(hs?.SneakDamageMultiplier),
                    },
                    ["resources"] = new
                    {
                        MaxHealth = SafeStat(hero.MaxHealth),
                        MaxStamina = SafeStat(cs?.MaxStamina),
                        MaxMana = SafeStat(cs?.MaxMana),
                        StaminaRegen = SafeStat(cs?.StaminaRegen),
                        ManaRegen = SafeStat(cs?.ManaRegen),
                    },
                    ["equipped"] = DumpEquipped(hero),
                    ["talents"] = DumpTalents(hero),
                    ["statEffects"] = DumpStatEffects(),
                    ["craftableWeapons"] = DumpCraftableWeapons(hero),
                };

                return JsonConvert.SerializeObject(dump, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message, stack = e.StackTrace });
            }
        }

        static object SafeStat(Stat s)
        {
            if (s == null) return null;
            try
            {
                return new { value = s.ModifiedValue, baseValue = s.BaseValue };
            }
            catch { return null; }
        }

        static object DumpEquipped(Hero hero)
        {
            var slots = new[]
            {
                EquipmentSlotType.MainHand, EquipmentSlotType.OffHand,
                EquipmentSlotType.AdditionalMainHand, EquipmentSlotType.AdditionalOffHand,
                EquipmentSlotType.Quiver,
                EquipmentSlotType.Helmet, EquipmentSlotType.Cuirass, EquipmentSlotType.Gauntlets,
                EquipmentSlotType.Greaves, EquipmentSlotType.Boots, EquipmentSlotType.Back,
                EquipmentSlotType.Amulet, EquipmentSlotType.Ring1, EquipmentSlotType.Ring2,
            };

            var result = new Dictionary<string, object>();
            foreach (var slot in slots)
            {
                Item item = null;
                try { item = hero.HeroItems.ItemInSlots[slot]; } catch { }
                if (item == null) continue;
                result[slot.EnumName] = DumpItem(item, includeArmor: true);
            }
            return result;
        }

        static object DumpItem(Item item, bool includeArmor)
        {
            try
            {
                var t = item.Template;
                var info = new Dictionary<string, object>
                {
                    ["name"] = item.DisplayName ?? t?.ItemName,
                    ["template"] = t?.name,
                    ["level"] = item.Level?.ModifiedInt ?? 0,
                };

                var weaponStats = item.TryGetElement<ItemStats>();
                if (weaponStats != null)
                {
                    if (weaponStats.BaseMinDmg != null && weaponStats.BaseMinDmg.ModifiedValue > 0)
                    {
                        info["minDmg"] = weaponStats.BaseMinDmg.ModifiedValue;
                        info["maxDmg"] = weaponStats.BaseMaxDmg?.ModifiedValue;
                    }
                    try { info["armor"] = weaponStats.Armor?.ModifiedValue; } catch { }
                    try { info["block"] = weaponStats.Block?.ModifiedValue; } catch { }
                    try { info["weight"] = weaponStats.Weight?.ModifiedValue; } catch { }
                }

                if (includeArmor)
                {
                    // Dump elements that look like passive bonuses (stat tweaks, gem/talent-equivalent)
                    var elementSummaries = new List<string>();
                    foreach (var el in item.AllElements())
                    {
                        try
                        {
                            var n = el.GetType().Name;
                            if (n.Contains("Tweak") || n.Contains("Effect") || n.Contains("Status") ||
                                n.Contains("Skill") || n.Contains("Bonus") || n.Contains("Gem"))
                            {
                                elementSummaries.Add(n);
                            }
                        }
                        catch { }
                    }
                    if (elementSummaries.Count > 0) info["elements"] = elementSummaries;
                }

                return info;
            }
            catch (Exception e) { return new { error = e.Message }; }
        }

        static object DumpTalents(Hero hero)
        {
            try
            {
                var ht = hero.Talents;
                if (ht == null) return null;
                var trees = new List<object>();
                var tables = new List<Awaken.TG.Main.Heroes.Development.Talents.TalentTable>();
                tables.AddRange(ht.BaseTalentTables);
                if (ht.WyrdTalentTable != null) tables.Add(ht.WyrdTalentTable);
                if (ht.SarrasTalentTable != null) tables.Add(ht.SarrasTalentTable);

                foreach (var table in tables)
                {
                    try
                    {
                        var all = new List<object>();
                        foreach (var t in table.talents)
                        {
                            int level = 0, max = 0;
                            try { level = t.Level; } catch { }
                            try { max = t.MaxLevel; } catch { }
                            string descRank1 = "";
                            try { descRank1 = t.Template?.GetLevel(1).Description(t, 1) ?? ""; } catch { }
                            string subtreeEnum = "";
                            string subtreeDisplay = "";
                            try {
                                var bt = t.TalentTreeBranchType;
                                if (bt != null) {
                                    subtreeEnum = bt.EnumName;
                                    subtreeDisplay = bt.DisplayName;
                                }
                            } catch { }
                            all.Add(new
                            {
                                name = t.Template?.Name ?? t.Template?.name,
                                templateRef = t.Template?.name,
                                parentRef = t.Parent?.name,
                                subtreeEnum,
                                subtreeDisplay,
                                level = level,
                                maxLevel = max,
                            });
                        }
                        trees.Add(new
                        {
                            tree = table.TreeTemplate?.name,
                            pointsSpent = table.PointsSpent,
                            allTalents = all,
                        });
                    }
                    catch { }
                }
                return trees;
            }
            catch (Exception e) { return new { error = e.Message }; }
        }

        static object DumpStatEffects()
        {
            try
            {
                var gc = Awaken.TG.MVC.World.Services.Get<Awaken.TG.Main.General.Configs.GameConstants>();
                var rpgEffects = new List<object>();
                foreach (var p in gc.rpgHeroStats)
                {
                    foreach (var fx in p.Effects)
                    {
                        rpgEffects.Add(new
                        {
                            stat = p.RPGStat?.EnumName,
                            innate = p.InnateStatLevel,
                            affects = fx.StatEffected?.EnumName,
                            perLevel = fx.BaseEffectStrength,
                            opType = fx.EffectType?.ToString(),
                        });
                    }
                }
                var profEffects = new List<object>();
                foreach (var p in gc.proficiencyParams)
                {
                    foreach (var fx in p.Effects)
                    {
                        profEffects.Add(new
                        {
                            prof = p.HeroStat?.EnumName,
                            affects = fx.StatEffected?.EnumName,
                            perLevel = fx.BaseEffectStrength,
                            opType = fx.EffectType?.ToString(),
                        });
                    }
                }
                return new { rpg = rpgEffects, proficiencies = profEffects };
            }
            catch (Exception e) { return new { error = e.Message, stack = e.StackTrace }; }
        }

        static object DumpCraftableWeapons(Hero hero)
        {
            try
            {
                var heroRecipes = hero.Element<HeroRecipes>();
                if (heroRecipes == null) return new { error = "no HeroRecipes element" };

                var list = new List<object>();
                foreach (var recipe in heroRecipes.knownRecipes)
                {
                    try
                    {
                        var outcome = recipe.Outcome;
                        if (outcome == null) continue;

                        var prof = recipe.ProficiencyStat;
                        bool isWeaponish = prof != null && (
                            prof == ProfStatType.OneHanded || prof == ProfStatType.TwoHanded ||
                            prof == ProfStatType.Archery || prof == ProfStatType.Magic ||
                            prof == ProfStatType.Handcrafting);

                        // tag-based filter as fallback — handcrafting can produce non-weapons
                        var tags = outcome.tags ?? Array.Empty<string>();
                        bool tagMatchesWeapon = tags.Any(tag =>
                            tag != null && (tag.Contains("weapon") || tag.Contains("Weapon") ||
                                            tag.Contains("sword") || tag.Contains("axe") ||
                                            tag.Contains("dagger") || tag.Contains("bow") ||
                                            tag.Contains("staff") || tag.Contains("hammer") ||
                                            tag.Contains("mace") || tag.Contains("spear") ||
                                            tag.Contains("halberd") || tag.Contains("crossbow")));

                        if (!isWeaponish && !tagMatchesWeapon) continue;

                        var req = recipe.StatRequirement;
                        list.Add(new
                        {
                            name = outcome.ItemName,
                            template = outcome.name,
                            recipeType = recipe.GetType().Name,
                            proficiency = prof?.EnumName,
                            bonusStat = recipe.BonusLevelStat?.EnumName,
                            statRequirement = req.statType?.EnumName,
                            statRequirementValue = req.value,
                            craftingDifficulty = recipe.ItemCraftingDifficulty,
                            levelBonus = outcome.LevelBonus,
                            tags = tags,
                        });
                    }
                    catch { }
                }

                return list;
            }
            catch (Exception e) { return new { error = e.Message, stack = e.StackTrace }; }
        }
    }
}
