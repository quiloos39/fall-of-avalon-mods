using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Dump all items currently in HeroItems.KnownItems, grouped by category, sorted by name.
    // Lets the user spot-check whether specific boss drops they expect are tracked.
    public static class KnownItemsList
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var heroItemsType = ResolveType("Awaken.TG.Main.Heroes.Items.HeroItems");
                var heroItems = hero?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0)
                    ?.MakeGenericMethod(heroItemsType).Invoke(hero, null);
                var known = heroItemsType.GetProperty("KnownItems")?.GetValue(heroItems) as HashSet<string>;
                bag["known_count"] = known?.Count ?? 0;

                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);

                var itemTemplate = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                var flagType = ResolveType("Awaken.TG.Main.Templates.TemplateTypeFlag");
                var flagRegular = Enum.Parse(flagType, "Regular");
                var getAllOfType = providerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

                var allItems = getAllOfType?.MakeGenericMethod(itemTemplate).Invoke(provider, new object[] { flagRegular }) as System.Collections.IEnumerable;
                var byGuid = new Dictionary<string, object>();
                foreach (var it in allItems ?? new object[0])
                {
                    var g = it?.GetType().GetProperty("GUID")?.GetValue(it) as string;
                    if (!string.IsNullOrEmpty(g)) byGuid[g] = it;
                }

                // Categorize using the same checks the UI uses (IsWeapon, IsArmor, etc.)
                var weapons = new List<string>();
                var armors = new List<string>();
                var jewelry = new List<string>();
                var shields = new List<string>();
                var ranged = new List<string>();
                var arrows = new List<string>();
                var consumables = new List<string>();
                var components = new List<string>();
                var other = new List<string>();
                int unnamed = 0;

                foreach (var guid in known ?? new HashSet<string>())
                {
                    if (!byGuid.TryGetValue(guid, out var t)) continue;

                    var name = t.GetType().GetProperty("ItemName")?.GetValue(t)?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) { unnamed++; continue; }

                    bool IsB(string p) { try { return (bool)(t.GetType().GetProperty(p)?.GetValue(t) ?? false); } catch { return false; } }

                    int tier = SafeTierValue(t);
                    var label = $"[{tier}] {name}";

                    if (IsB("IsArmor")) armors.Add(label);
                    else if (IsB("IsJewelry")) jewelry.Add(label);
                    else if (IsB("IsShield")) shields.Add(label);
                    else if (IsB("IsArrow")) arrows.Add(label);
                    else if (IsB("IsRanged")) ranged.Add(label);
                    else if (IsB("IsWeapon")) weapons.Add(label);
                    else if (IsB("IsConsumable")) consumables.Add(label);
                    else if (IsB("IsComponent")) components.Add(label);
                    else other.Add(label);
                }

                weapons.Sort(); armors.Sort(); jewelry.Sort(); shields.Sort();
                ranged.Sort(); arrows.Sort(); consumables.Sort(); components.Sort();

                bag["unnamed"] = unnamed;
                bag["weapons_count"] = weapons.Count;
                bag["weapons"] = weapons;
                bag["armors_count"] = armors.Count;
                bag["armors"] = armors;
                bag["shields"] = shields;
                bag["ranged"] = ranged;
                bag["arrows"] = arrows;
                bag["jewelry"] = jewelry;
                bag["consumables_count"] = consumables.Count;
                bag["components_count"] = components.Count;
                bag["other_count"] = other.Count;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static int SafeTierValue(object t)
        {
            try
            {
                var tags = t.GetType().GetProperty("Tags")?.GetValue(t) as System.Collections.IEnumerable;
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        var s = tag?.ToString();
                        if (s == null) continue;
                        if (s.Length == 10 && s.StartsWith("item:tier") && char.IsDigit(s[9]))
                            return s[9] - '0';
                    }
                }
            }
            catch { }
            return 0;
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
