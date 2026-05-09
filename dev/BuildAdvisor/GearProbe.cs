using System;
using System.Collections.Generic;
using System.Linq;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.Gems;
using Awaken.TG.Main.Heroes.Items.Weapons;
using Awaken.TG.Main.Heroes.Skills;
using Awaken.TG.Main.Skills;
using Newtonsoft.Json;

namespace BuildAdvisor
{
    public static class GearProbe
    {
        public static string Run()
        {
            try
            {
                var hero = Hero.Current;
                if (hero == null) return JsonConvert.SerializeObject(new { error = "no hero" });

                var equipped = new Dictionary<string, object>();
                foreach (EquipmentSlotType slot in new[] {
                    EquipmentSlotType.MainHand, EquipmentSlotType.OffHand,
                    EquipmentSlotType.Helmet, EquipmentSlotType.Cuirass,
                    EquipmentSlotType.Gauntlets, EquipmentSlotType.Greaves,
                    EquipmentSlotType.Boots, EquipmentSlotType.Back,
                    EquipmentSlotType.Amulet, EquipmentSlotType.Ring1, EquipmentSlotType.Ring2,
                    EquipmentSlotType.Quiver,
                })
                {
                    Item item = null;
                    try { item = hero.HeroItems.ItemInSlots[slot]; } catch { }
                    if (item == null) continue;
                    equipped[slot.EnumName] = DescribeItem(item);
                }

                // Loose inventory: gems, weapons, armor not currently equipped
                var loose = new List<object>();
                foreach (var item in hero.HeroItems.Items)
                {
                    try
                    {
                        if (item == null) continue;
                        if (item.IsEquipped) continue;
                        var info = DescribeItem(item, briefSkills: true);
                        loose.Add(info);
                    }
                    catch { }
                }

                return JsonConvert.SerializeObject(new
                {
                    equipped,
                    looseCount = loose.Count,
                    loose,
                }, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message, stack = e.StackTrace });
            }
        }

        static object DescribeItem(Item item, bool briefSkills = false)
        {
            try
            {
                var info = new Dictionary<string, object>
                {
                    ["name"] = item.DisplayName ?? item.Template?.ItemName,
                    ["template"] = item.Template?.name,
                    ["level"] = item.Level?.ModifiedInt ?? 0,
                    ["quantity"] = item.Quantity,
                    ["isEquipped"] = item.IsEquipped,
                    ["tags"] = item.Template?.tags,
                };

                var stats = item.TryGetElement<ItemStats>();
                if (stats != null)
                {
                    var s = new Dictionary<string, object>();
                    try { if (stats.BaseMinDmg != null) { s["minDmg"] = stats.BaseMinDmg.ModifiedValue; s["maxDmg"] = stats.BaseMaxDmg.ModifiedValue; } } catch { }
                    try { s["armor"] = stats.Armor?.ModifiedValue ?? 0f; } catch { }
                    try { s["block"] = stats.Block?.ModifiedValue ?? 0f; } catch { }
                    try { s["weight"] = stats.Weight?.ModifiedValue ?? 0f; } catch { }
                    if (s.Count > 0) info["stats"] = s;
                }

                // gem slots and attached gems
                var itemGems = item.TryGetElement<ItemGems>();
                if (itemGems != null)
                {
                    info["gemSlots"] = new { available = itemGems.AvailableSlots, max = itemGems.MaxSlots };
                }

                var gemList = new List<object>();
                foreach (var g in item.Elements<GemAttached>())
                {
                    var skills = new List<string>();
                    foreach (var sk in g.Skills)
                    {
                        try { skills.Add(sk.Graph?.DebugName ?? sk.GetType().Name); } catch { }
                    }
                    gemList.Add(new
                    {
                        name = g.DisplayName,
                        template = g.Template?.name,
                        level = g.GemLevel,
                        skills,
                    });
                }
                if (gemList.Count > 0) info["attachedGems"] = gemList;

                // Item-intrinsic skills (ring/amulet bonuses, weapon skills)
                var skillsList = new List<string>();
                try
                {
                    foreach (var sk in item.Elements<Skill>())
                    {
                        try
                        {
                            var n = sk.Graph?.DebugName ?? sk.GetType().Name;
                            if (briefSkills && n != null && n.Length > 70) n = n.Substring(0, 70);
                            skillsList.Add(n);
                        }
                        catch { }
                    }
                }
                catch { }
                if (skillsList.Count > 0) info["intrinsicSkills"] = skillsList;

                // ItemEffects -> tweaks/effects (passive bonuses)
                var effects = item.TryGetElement<Awaken.TG.Main.Heroes.Items.Attachments.ItemEffects>();
                if (effects != null)
                {
                    var effectList = new List<string>();
                    try
                    {
                        foreach (var sk in effects.Skills)
                        {
                            try { effectList.Add(sk.Graph?.DebugName ?? sk.GetType().Name); } catch { }
                        }
                    }
                    catch { }
                    if (effectList.Count > 0) info["effects"] = effectList;
                }

                // GemUnattached: this item is itself a gem
                var gu = item.TryGetElement<GemUnattached>();
                if (gu != null)
                {
                    var gemSkills = new List<string>();
                    foreach (var skRef in gu.SkillRefs)
                    {
                        try {
                            var graphRef = skRef.skillGraphRef;
                            gemSkills.Add(graphRef.GUID ?? "?");
                        } catch (Exception ex) { gemSkills.Add("err: " + ex.Message); }
                    }
                    int skCount = 0; try { skCount = gu.SkillRefs.Count(); } catch { }
                    info["isGem"] = new { gemType = gu.GemType.ToString(), skillCount = skCount, skills = gemSkills };
                }

                return info;
            }
            catch (Exception e)
            {
                return new { error = e.Message, name = item?.DisplayName };
            }
        }
    }
}
