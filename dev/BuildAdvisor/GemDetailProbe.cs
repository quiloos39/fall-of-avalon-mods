using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Awaken.TG.MVC;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.Gems;
using Awaken.TG.Main.Skills;
using Newtonsoft.Json;

namespace BuildAdvisor
{
    // Pulls effect details for every gem the user currently has (loose + attached).
    public static class GemDetailProbe
    {
        public static string Run()
        {
            try
            {
                var hero = Hero.Current;
                if (hero == null) return "{}";
                var output = new List<object>();

                // Loose gems in inventory
                foreach (var item in hero.HeroItems.Items)
                {
                    if (item == null) continue;
                    if (item.IsEquipped) continue;
                    var gu = item.TryGetElement<GemUnattached>();
                    if (gu == null) continue;
                    output.Add(DescribeGem(item, attached: null));
                }

                // Attached gems on equipped items
                foreach (var slot in new[] {
                    EquipmentSlotType.MainHand, EquipmentSlotType.OffHand,
                    EquipmentSlotType.Helmet, EquipmentSlotType.Cuirass,
                    EquipmentSlotType.Gauntlets, EquipmentSlotType.Greaves,
                    EquipmentSlotType.Boots, EquipmentSlotType.Back })
                {
                    Item parent = null;
                    try { parent = hero.HeroItems.ItemInSlots[slot]; } catch { }
                    if (parent == null) continue;
                    foreach (var ga in parent.Elements<GemAttached>())
                    {
                        output.Add(new
                        {
                            attachedTo = parent.DisplayName,
                            slot = slot.EnumName,
                            gem = DescribeAttachedGem(ga),
                        });
                    }
                }

                return JsonConvert.SerializeObject(output, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message, stack = e.StackTrace });
            }
        }

        static object DescribeGem(Item gemItem, object attached)
        {
            try
            {
                var gu = gemItem.TryGetElement<GemUnattached>();
                var skills = new List<object>();
                if (gu != null)
                {
                    foreach (var skRef in gu.SkillRefs)
                    {
                        skills.Add(CatalogProbe.DescribeSkillRef(skRef));
                    }
                }
                return new
                {
                    name = gemItem.DisplayName,
                    template = gemItem.Template?.name,
                    gemType = gu?.GemType.ToString(),
                    quantity = gemItem.Quantity,
                    skills,
                };
            }
            catch (Exception e) { return new { error = e.Message }; }
        }

        static object DescribeAttachedGem(GemAttached ga)
        {
            try
            {
                var skills = new List<object>();
                foreach (var skRef in ga.SkillRefs)
                {
                    skills.Add(CatalogProbe.DescribeSkillRef(skRef));
                }
                return new
                {
                    name = ga.DisplayName,
                    template = ga.Template?.name,
                    gemLevel = ga.GemLevel,
                    skills,
                };
            }
            catch (Exception e) { return new { error = e.Message }; }
        }
    }
}
