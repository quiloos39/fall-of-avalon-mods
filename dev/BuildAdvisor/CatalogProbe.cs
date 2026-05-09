using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Awaken.TG.MVC;
using Awaken.TG.Main.General.StatTypes;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Stats;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.Attachments;
using Awaken.TG.Main.Heroes.Items.Gems;
using Awaken.TG.Main.Heroes.Items.Weapons;
using Awaken.TG.Main.Skills;
using Awaken.TG.Main.Templates;
using Newtonsoft.Json;

namespace BuildAdvisor
{
    // Returns the entire item catalog for the build advisor to munch on.
    public static class CatalogProbe
    {
        public static string Run()
        {
            try
            {
                var tp = World.Services.Get<TemplatesProvider>();
                var allItems = tp.GetAllOfType<ItemTemplate>(TemplateTypeFlag.All).ToList();
                // GUIDMap holds every registered template — enumerate it so we don't miss
                // entries registered under sub-types or with non-Regular template flags.
                try
                {
                    var guidMap = tp.GetType()
                        .GetField("GUIDMap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(tp);
                    if (guidMap is System.Collections.IDictionary dict)
                    {
                        foreach (var v in dict.Values)
                        {
                            if (v is ItemTemplate it && !allItems.Contains(it))
                            {
                                allItems.Add(it);
                            }
                        }
                    }
                }
                catch { }

                var weapons = new List<object>();
                var armors = new List<object>();
                var jewelry = new List<object>();
                var gems = new List<object>();

                foreach (var t in allItems)
                {
                    try
                    {
                        var n = t.name;
                        if (n == null) continue;
                        bool isWeap = n.Contains("_Weapon_");
                        bool isArm = n.Contains("_Armor_");
                        bool isJewel = n.Contains("_Jewelry_");
                        bool isGem = n.Contains("_Gem_");
                        if (!isWeap && !isArm && !isJewel && !isGem) continue;

                        var info = DescribeTemplate(t, includeEffects: isJewel || isGem || isArm || isWeap, includeStats: true);
                        if (isWeap) weapons.Add(info);
                        else if (isArm) armors.Add(info);
                        else if (isJewel) jewelry.Add(info);
                        else if (isGem) gems.Add(info);
                    }
                    catch { }
                }

                return JsonConvert.SerializeObject(new
                {
                    counts = new { weapons = weapons.Count, armors = armors.Count, jewelry = jewelry.Count, gems = gems.Count },
                    weapons,
                    armors,
                    jewelry,
                    gems,
                }, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message, stack = e.StackTrace });
            }
        }

        public static object DescribeTemplate(ItemTemplate t, bool includeEffects, bool includeStats)
        {
            var dict = new Dictionary<string, object>
            {
                ["template"] = t.name,
                ["name"] = t.ItemName,
                ["weight"] = t.Weight,
                ["tags"] = t.tags,
            };

            // Stats attachment
            if (includeStats)
            {
                var sa = t.GetAttachment<ItemStatsAttachment>();
                if (sa != null)
                {
                    var stats = new Dictionary<string, object>
                    {
                        ["armor"] = sa.armor,
                        ["armorGain"] = sa.armorGain,
                        ["minDmg"] = sa.minDamage,
                        ["maxDmg"] = sa.maxDamage,
                    };
                    // try other fields via reflection
                    TryAddFloat(sa, "minDamageGain", stats, "minDmgGain");
                    TryAddFloat(sa, "maxDamageGain", stats, "maxDmgGain");
                    TryAddFloat(sa, "block", stats, "block");
                    TryAddFloat(sa, "blockGain", stats, "blockGain");
                    TryAddFloat(sa, "criticalChance", stats, "critChance");
                    TryAddFloat(sa, "criticalDamage", stats, "critDmg");
                    TryAddFloat(sa, "weakSpotDamage", stats, "weakSpotDmg");
                    dict["stats"] = stats;
                }
            }

            // Slot
            try
            {
                var es = t.GetAttachment<ItemEquipSpec>();
                if (es != null && es.EquipmentType != null) dict["slot"] = es.EquipmentType.EnumName;
            }
            catch { }

            // Requirements
            try
            {
                var rs = t.GetAttachment<ItemStatsRequirementsAttachment>();
                if (rs != null)
                {
                    var rt = rs.GetType();
                    int strReq = 0, dexReq = 0, endReq = 0, spiReq = 0, perReq = 0, praReq = 0;
                    GetIntField(rs, rt, "strengthRequired", ref strReq);
                    GetIntField(rs, rt, "dexterityRequired", ref dexReq);
                    GetIntField(rs, rt, "enduranceRequired", ref endReq);
                    GetIntField(rs, rt, "spiritualityRequired", ref spiReq);
                    GetIntField(rs, rt, "perceptionRequired", ref perReq);
                    GetIntField(rs, rt, "practicalityRequired", ref praReq);
                    dict["reqs"] = new { str = strReq, dex = dexReq, end = endReq, spi = spiReq, per = perReq, pra = praReq };
                }
            }
            catch { }

            // Gem slots (defined on the template?) — get from a runtime attachment
            try
            {
                var gemAtt = t.GetAttachmentRaw("ItemGemsAttachment") ?? t.GetAttachmentRaw("ItemGemSlotsAttachment");
                if (gemAtt != null)
                {
                    var gat = gemAtt.GetType();
                    int avail = 0, max = 0;
                    GetIntField(gemAtt, gat, "availableSlots", ref avail);
                    GetIntField(gemAtt, gat, "maxSlots", ref max);
                    dict["gemSlots"] = new { available = avail, max };
                }
            }
            catch { }

            // Item effects via spec
            if (includeEffects)
            {
                var fxList = new List<object>();
                try
                {
                    var spec = t.GetAttachment<ItemEffectsSpec>();
                    if (spec != null)
                    {
                        foreach (var skRef in spec.Skills)
                        {
                            fxList.Add(DescribeSkillRef(skRef));
                        }
                    }
                }
                catch (Exception e) { fxList.Add(new { error = e.Message }); }

                if (fxList.Count > 0) dict["effects"] = fxList;

                // Gem-specific: dump SkillRefs from attachments
                if (t.name.Contains("_Gem_"))
                {
                    var gemSkills = new List<object>();
                    try
                    {
                        // GemAttachment holds SkillRefs
                        var gemAtt = t.GetAttachmentRaw("GemAttachment");
                        if (gemAtt != null)
                        {
                            var rt = gemAtt.GetType();
                            var f = rt.GetField("skillRefs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
                                    rt.GetField("_skillRefs", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (f != null)
                            {
                                if (f.GetValue(gemAtt) is List<SkillReference> refs)
                                {
                                    foreach (var r in refs) gemSkills.Add(DescribeSkillRef(r));
                                }
                            }
                            // also try a property
                            var p = rt.GetProperty("SkillRefs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (p != null && gemSkills.Count == 0)
                            {
                                if (p.GetValue(gemAtt) is IEnumerable<SkillReference> refs2)
                                {
                                    foreach (var r in refs2) gemSkills.Add(DescribeSkillRef(r));
                                }
                            }
                        }
                    }
                    catch (Exception e) { gemSkills.Add(new { error = e.Message }); }
                    if (gemSkills.Count > 0) dict["gemSkills"] = gemSkills;
                }
            }

            return dict;
        }

        public static object DescribeSkillRef(SkillReference skRef)
        {
            try
            {
                var d = new Dictionary<string, object>
                {
                    ["graphGuid"] = skRef.skillGraphRef.GUID,
                };
                try
                {
                    var graph = skRef.SkillGraph(null);
                    d["graphName"] = graph?.DebugName ?? graph?.GetType().Name;
                }
                catch { }
                if (skRef.variables != null && skRef.variables.Count > 0)
                {
                    d["variables"] = skRef.variables.Select(v => new { v.name, v.value }).ToList();
                }
                if (skRef.enums != null && skRef.enums.Count > 0)
                {
                    d["enums"] = skRef.enums.Select(en => new
                    {
                        en.name,
                        statType = SafeStatTypeName(en),
                    }).ToList();
                }
                return d;
            }
            catch (Exception e) { return new { error = e.Message }; }
        }

        static string SafeStatTypeName(SkillRichEnum en)
        {
            try
            {
                var st = en.enumReference?.EnumAs<StatType>();
                return st?.EnumName;
            }
            catch { return null; }
        }

        static void TryAddFloat(object obj, string field, Dictionary<string, object> into, string asKey)
        {
            try
            {
                var t = obj.GetType();
                var f = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && (f.FieldType == typeof(float) || f.FieldType == typeof(int)))
                {
                    into[asKey] = f.GetValue(obj);
                    return;
                }
                var p = t.GetProperty(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && (p.PropertyType == typeof(float) || p.PropertyType == typeof(int)))
                {
                    into[asKey] = p.GetValue(obj);
                }
            }
            catch { }
        }

        static void GetIntField(object obj, Type t, string field, ref int val)
        {
            var f = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(int)) { val = (int)f.GetValue(obj); }
        }
    }

    // Helper extension: GetAttachmentRaw allows looking up any attachment by class short-name
    internal static class TemplateAttachmentExt
    {
        public static object GetAttachmentRaw(this ItemTemplate template, string typeShortName)
        {
            // ItemTemplate has GetAttachments() returning IEnumerable<IAttachment>
            try
            {
                var miGet = template.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetAttachments" && !m.IsGenericMethod);
                if (miGet != null)
                {
                    if (miGet.Invoke(template, null) is System.Collections.IEnumerable list)
                    {
                        foreach (var a in list)
                        {
                            if (a == null) continue;
                            if (a.GetType().Name == typeShortName) return a;
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
