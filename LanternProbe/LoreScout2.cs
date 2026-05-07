using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class LoreScout2
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // 1) BabelManager — find singleton + dump API
                var bm = ResolveType("Awaken.Babel.BabelManager");
                if (bm != null)
                {
                    bag["BabelManager_static"] = bm.GetMembers(BindingFlags.Public | BindingFlags.Static).Take(20).Select(m => $"{m.MemberType} {m.Name}").ToList();
                    bag["BabelManager_instance"] = bm.GetMembers(BindingFlags.Public | BindingFlags.Instance).Take(30).Select(m => $"{m.MemberType} {m.Name}").ToList();

                    // Find live instance
                    var bmLive = UnityEngine.Object.FindObjectsOfType(bm) as Component[];
                    bag["BabelManager_liveCount"] = bmLive?.Length ?? 0;
                }

                // 2) PreloadedBabelProvider / StreamingBabelProvider - actual text store
                foreach (var name in new[] { "Awaken.Babel.PreloadedBabelProvider", "Awaken.Babel.StreamingBabelProvider" })
                {
                    var t = ResolveType(name);
                    if (t == null) continue;
                    bag[t.Name + "_members"] = t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .Where(m => m.MemberType == MemberTypes.Method || m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field)
                        .Take(30).Select(m => $"{m.MemberType} {m.Name}").ToList();
                }

                // 3) Find a real LocString example — pick a quest objective's name LocString
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero != null)
                {
                    var trackerType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTracker");
                    var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    var tracker = elementGeneric.MakeGenericMethod(trackerType).Invoke(hero, null);
                    var activeQuest = trackerType.GetProperty("ActiveQuest").GetValue(tracker);
                    if (activeQuest != null)
                    {
                        var descProp = activeQuest.GetType().GetProperty("Description");
                        var desc = descProp?.GetValue(activeQuest);
                        bag["sample_quest_description_type"] = desc?.GetType().FullName;
                        if (desc != null)
                        {
                            // String? Or LocString?
                            bag["sample_quest_description_string"] = desc?.ToString();
                        }
                    }
                }

                // 4) Find ALL ItemTemplates that are readable — those have book/note content
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var servicesProp = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static);
                var services = servicesProp?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);

                var itemTemplateType = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                var getAllOfType = providerType?.GetMethods()
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethod);
                var allItems = (getAllOfType?.MakeGenericMethod(itemTemplateType).Invoke(provider, null) as System.Collections.IEnumerable)?.Cast<object>().ToList();

                if (allItems != null)
                {
                    bag["totalItemTemplates"] = allItems.Count;
                    var readables = allItems
                        .Where(t =>
                        {
                            try { return (bool?)t.GetType().GetProperty("IsReadable")?.GetValue(t) == true; }
                            catch { return false; }
                        })
                        .ToList();
                    bag["readableItemCount"] = readables.Count;

                    if (readables.Count > 0)
                    {
                        // Inspect ItemTemplate.IsReadable items for their text fields/properties
                        var sample = readables[0];
                        bag["readableSample_name"] = sample.GetType().GetProperty("ItemName")?.GetValue(sample)?.ToString();
                        var readableProps = sample.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .Where(p => p.Name.IndexOf("read", StringComparison.OrdinalIgnoreCase) >= 0
                                     || p.Name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0
                                     || p.Name.IndexOf("content", StringComparison.OrdinalIgnoreCase) >= 0
                                     || p.Name.IndexOf("description", StringComparison.OrdinalIgnoreCase) >= 0
                                     || p.Name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0
                                     || p.Name.IndexOf("page", StringComparison.OrdinalIgnoreCase) >= 0)
                            .Take(30).Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
                        bag["readableSample_textProps"] = readableProps;

                        var readableFields = sample.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .Where(f => f.Name.IndexOf("read", StringComparison.OrdinalIgnoreCase) >= 0
                                     || f.Name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0
                                     || f.Name.IndexOf("content", StringComparison.OrdinalIgnoreCase) >= 0
                                     || f.Name.IndexOf("description", StringComparison.OrdinalIgnoreCase) >= 0
                                     || f.Name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0
                                     || f.Name.IndexOf("page", StringComparison.OrdinalIgnoreCase) >= 0)
                            .Take(30).Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();
                        bag["readableSample_textFields"] = readableFields;

                        // List first 10 readable item names
                        bag["readableItems_first10"] = readables.Take(10).Select(t => t.GetType().GetProperty("ItemName")?.GetValue(t)?.ToString()).ToList();
                    }
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }

        private static string Pretty(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.Name;
            return t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(g => g.Name)) + ">";
        }
    }
}
