using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Awaken.TG.MVC;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.Weapons;
using Awaken.TG.Main.Templates;
using Newtonsoft.Json;

namespace BuildAdvisor
{
    public static class ArmorProbe
    {
        public static string Run()
        {
            try
            {
                var tp = World.Services.Get<TemplatesProvider>();
                // Find all armor item templates that match "Knight" or "Paladin" or relevant heavy sets
                var allItems = tp.GetAllOfType<ItemTemplate>().ToList();
                // ONLY user's craftable armor (from probe earlier)
                var craftableTemplates = new[] {
                    "ItemTemplate_Armor_Light_T0_Torso_ShadowbladesLamellarArmor",
                    "ItemTemplate_Armor_Light_T0_Back_WarmCoat",
                    "ItemTemplate_Armor_Heavy_T3_Head_KnightsHelmet",
                    "ItemTemplate_Armor_Heavy_T3_Body_KnightsBreastplate",
                    "ItemTemplate_Armor_Heavy_T3_Arms_KnightsGauntlets",
                    "ItemTemplate_Armor_Heavy_T3_Legs_KnightsGreaves",
                    "ItemTemplate_Armor_Heavy_T3_Feet_KnightsBoots",
                    "ItemTemplate_Armor_Heavy_T3_Head_KnightsHelmet_Paladin",
                    "ItemTemplate_Armor_Heavy_T3_Body_KnightsBreastplate_Paladin",
                    "ItemTemplate_Armor_Heavy_T3_Arms_KnightsGauntlets_Paladin",
                    "ItemTemplate_Armor_Heavy_T3_Legs_KnightsGreaves_Paladin",
                    "ItemTemplate_Armor_Heavy_T3_Feet_KnightsBoots_Paladin",
                    "ItemTemplate_Armor_Medium_T2_Arms_LeatherGloves",
                    "ItemTemplate_Armor_Medium_T2_Body_LeatherJacket",
                    "ItemTemplate_Armor_Medium_T2_Feet_SturdyLeatherBoots",
                    "ItemTemplate_Armor_Medium_T2_Head_RevenantsCowl",
                    "ItemTemplate_Armor_Medium_T2_Body_RevenantsRibcage",
                    "ItemTemplate_Armor_Medium_T2_Head_FlayedDrownersConch",
                    "ItemTemplate_Armor_Medium_T2_Body_FlayedDrownersCarapace",
                    "ItemTemplate_Armor_Medium_T2_Arms_FlayedDrownersGloves",
                    "ItemTemplate_Armor_Medium_T2_Head_FesteringHelm",
                    "ItemTemplate_Armor_Medium_T2_Body_FesteringChestguard",
                    "ItemTemplate_Armor_Medium_T2_Arms_FesteringArmGuards",
                    "ItemTemplate_Armor_Light_T2_Body_CorpseEatersRags",
                    "ItemTemplate_Armor_Light_T2_Head_CorpseEatersCap",
                    "ItemTemplate_Armor_Medium_T4_Body_BloodsoakedVest",
                    "ItemTemplate_Armor_Medium_T4_Head_BloodsoakedHood",
                    "ItemTemplate_Armor_Medium_T4_Arms_BloodsoakedGloves",
                    "ItemTemplate_Armor_Medium_T4_Feet_BloodsoakedBoots",
                    "ItemTemplate_Armor_Medium_T4_Head_TornSinewMask",
                    "ItemTemplate_Armor_Medium_T4_Body_TornSinewHusk",
                    "ItemTemplate_Armor_Medium_T4_Arms_TornSinewGrips",
                    "ItemTemplate_Armor_Medium_T4_Head_FlamegorgedMaw",
                    "ItemTemplate_Armor_Medium_T4_Body_FlamegorgedBreastplate",
                    "ItemTemplate_Armor_Medium_T4_Arms_FlamegorgedGauntlets",
                    "ItemTemplate_Armor_Medium_T4_Head_HaymakersCrown",
                    "ItemTemplate_Armor_Medium_T4_Body_HaymakersBonecage",
                    "ItemTemplate_Armor_Medium_T4_Feet_HaymakersBoots",
                    "ItemTemplate_Armor_Medium_T4_Arms_HaymakersTalons",
                    "ItemTemplate_Armor_Light_T2_Back_BrocsBackpack",
                };
                var armorTargets = craftableTemplates;

                var results = new List<object>();
                foreach (var target in armorTargets)
                {
                    var t = allItems.FirstOrDefault(it => it.name == target);
                    if (t == null) {
                        results.Add(new { template = target, found = false });
                        continue;
                    }

                    // Get attachment from template via GetAttachment<ItemStatsAttachment>()
                    var statsAttach = t.GetAttachment<Awaken.TG.Main.Heroes.Items.Attachments.ItemStatsAttachment>();
                    float armor = 0f, weight = 0f, minDmg = 0f, maxDmg = 0f, block = 0f, armorGain = 0f;

                    if (statsAttach != null) {
                        armor = statsAttach.armor;
                        armorGain = statsAttach.armorGain;
                        minDmg = statsAttach.minDamage;
                        maxDmg = statsAttach.maxDamage;
                    }

                    weight = t.Weight;

                    var reqAttach = t.GetAttachment<Awaken.TG.Main.Heroes.Items.Attachments.ItemStatsRequirementsAttachment>();
                    int strReq = 0, dexReq = 0, praReq = 0, perReq = 0, endReq = 0, spiReq = 0;
                    if (reqAttach != null) {
                        var rs = reqAttach.GetType();
                        TryGetInt(reqAttach, rs, "strengthRequired", ref strReq);
                        TryGetInt(reqAttach, rs, "dexterityRequired", ref dexReq);
                        TryGetInt(reqAttach, rs, "practicalityRequired", ref praReq);
                        TryGetInt(reqAttach, rs, "perceptionRequired", ref perReq);
                        TryGetInt(reqAttach, rs, "enduranceRequired", ref endReq);
                        TryGetInt(reqAttach, rs, "spiritualityRequired", ref spiReq);
                    }

                    // Read EquipmentType properly via the property
                    string slot = "?";
                    try {
                        var equip = t.GetAttachment<Awaken.TG.Main.Heroes.Items.Attachments.ItemEquipSpec>();
                        if (equip != null && equip.EquipmentType != null) {
                            slot = equip.EquipmentType.EnumName;
                        }
                    } catch { }

                    results.Add(new {
                        template = target,
                        found = true,
                        name = t.ItemName,
                        slot,
                        armor, armorGain, weight, minDmg, maxDmg, block,
                        reqs = new { str = strReq, dex = dexReq, end = endReq, spi = spiReq, per = perReq, pra = praReq }
                    });
                }

                return JsonConvert.SerializeObject(results, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message, stack = e.StackTrace });
            }
        }

        static bool TryGetFloat(object obj, Type t, string fieldName, ref float val) {
            var f = t.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(float)) { val = (float)f.GetValue(obj); return true; }
            var p = t.GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.PropertyType == typeof(float)) { val = (float)p.GetValue(obj); return true; }
            return false;
        }
        static bool TryGetInt(object obj, Type t, string fieldName, ref int val) {
            var f = t.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(int)) { val = (int)f.GetValue(obj); return true; }
            var p = t.GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.PropertyType == typeof(int)) { val = (int)p.GetValue(obj); return true; }
            return false;
        }
    }
}
