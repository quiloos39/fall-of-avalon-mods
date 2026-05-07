using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Audit the existing KnownItems set on the live save:
    //  - Size? (so we know how much is already tracked)
    //  - Key format? (GUID vs template name)
    //  - Who calls AddToKnownItems? (where in code path is it invoked)
    //  - Is there a corresponding container "Looted" / "Opened" flag?
    //  - Is there a per-NPC kill flag we can read?
    public static class KnownItemsAudit
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                bag["hero"] = hero != null;

                // Get HeroItems via Element<HeroItems>()
                var heroItemsType = ResolveType("Awaken.TG.Main.Heroes.Items.HeroItems");
                var elementMethod = hero?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var heroItems = elementMethod?.MakeGenericMethod(heroItemsType).Invoke(hero, null);
                bag["heroItems"] = heroItems != null;
                if (heroItems == null) return Json(bag);

                // 1. Read KnownItems
                var knownProp = heroItemsType.GetProperty("KnownItems", BindingFlags.Public | BindingFlags.Instance);
                var known = knownProp?.GetValue(heroItems) as System.Collections.Generic.HashSet<string>;
                if (known == null)
                {
                    // Try via field
                    var knownField = heroItemsType.GetField("<KnownItems>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                    known = knownField?.GetValue(heroItems) as System.Collections.Generic.HashSet<string>;
                }
                bag["knownItems_count"] = known?.Count ?? -1;
                bag["knownItems_sample"] = known?.Take(20).ToList();

                // 2. Sample key format - is it GUID-like (32 hex chars) or template-name-like?
                if (known != null && known.Count > 0)
                {
                    var first = known.First();
                    bag["sample_key"] = first;
                    bag["sample_key_length"] = first.Length;
                    bag["sample_key_looks_like_guid"] = System.Text.RegularExpressions.Regex.IsMatch(first, "^[0-9a-fA-F]{32}$");
                }

                // 3. Resolve a sample to its ItemTemplate to verify mapping
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);
                bag["provider"] = provider != null;

                // Try to look up a known template via the provider with a few common method names
                if (provider != null && known != null && known.Count > 0)
                {
                    var firstKey = known.First();
                    var providerMethods = providerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.Name.IndexOf("GetTemplate", StringComparison.OrdinalIgnoreCase) >= 0
                                 || m.Name.IndexOf("Resolve", StringComparison.OrdinalIgnoreCase) >= 0
                                 || m.Name.Equals("FromGuid", StringComparison.OrdinalIgnoreCase))
                        .Take(15).ToList();
                    bag["provider_lookup_methods"] = providerMethods.Select(m =>
                        $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})").ToList();
                }

                // 4. Find callers of AddToKnownItems by scanning method bodies for the metadata token
                //    Cheap version: find any method whose name suggests pickup/spawn and check if it
                //    references the KnownItems hashset.
                var addKnownM = heroItemsType.GetMethod("AddToKnownItems", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                bag["addToKnownItems_signature"] = addKnownM == null ? null :
                    $"{Pretty(addKnownM.ReturnType)} {addKnownM.Name}({string.Join(",", addKnownM.GetParameters().Select(p => Pretty(p.ParameterType)))})";

                // 5. Container Looted flag — find Container / LootedContainer types
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && (n.StartsWith("TG.") || n.StartsWith("Awaken.")); })
                    .SelectMany(SafeGetTypes).Where(t => t != null && !t.Name.Contains("<")).ToList();

                bag["container_types"] = allTypes.Where(t =>
                    t.Name.IndexOf("Container", StringComparison.OrdinalIgnoreCase) >= 0
                    && t.Namespace != null && t.Namespace.Contains("Awaken.TG"))
                    .Select(t => t.FullName).OrderBy(n => n).Take(40).ToList();

                bag["looted_types"] = allTypes.Where(t =>
                    t.Name.IndexOf("Looted", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("Opened", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(t => t.FullName).OrderBy(n => n).Take(20).ToList();

                // 6. ContainerInventory has Looted flag?
                var containerInv = ResolveType("Awaken.TG.Main.Locations.Containers.ContainerInventory");
                if (containerInv != null)
                {
                    bag["ContainerInventory_props_state"] = containerInv.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(p => p.Name.IndexOf("Looted", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.IndexOf("Opened", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.IndexOf("Open", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.IndexOf("Empty", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
                    bag["ContainerInventory_fields_state"] = containerInv.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(f => f.Name.IndexOf("Looted", StringComparison.OrdinalIgnoreCase) >= 0
                                 || f.Name.IndexOf("Opened", StringComparison.OrdinalIgnoreCase) >= 0
                                 || f.Name.IndexOf("Open", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();
                }

                // 7. NpcTemplate — does it have a loot table reference?
                var npcTemplate = ResolveType("Awaken.TG.Main.Fights.NPCs.NpcTemplate");
                if (npcTemplate != null)
                {
                    bag["NpcTemplate_lootRelated"] = npcTemplate.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(f => f.Name.IndexOf("Loot", StringComparison.OrdinalIgnoreCase) >= 0
                                 || f.Name.IndexOf("Drop", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").Take(15).ToList();
                    bag["NpcTemplate_lootProps"] = npcTemplate.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(p => p.Name.IndexOf("Loot", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.IndexOf("Drop", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(15).ToList();
                }

                // 8. Find a kill flag / how kills are persisted - look at LocationSpec / NpcSpec
                var locationSpec = ResolveType("Awaken.TG.Main.Locations.LocationSpec");
                if (locationSpec != null)
                {
                    bag["LocationSpec_killRelatedProps"] = locationSpec.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(p => p.Name.IndexOf("Kill", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.IndexOf("Dead", StringComparison.OrdinalIgnoreCase) >= 0
                                 || p.Name.IndexOf("Defeat", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").Take(10).ToList();
                }

                // 9. ALL world locations currently — can we enumerate them and read their states?
                var locationType = ResolveType("Awaken.TG.Main.Locations.Location");
                if (locationType != null && services != null)
                {
                    // Try World.All<Location>()
                    var worldAllGeneric = worldType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "All" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    if (worldAllGeneric != null)
                    {
                        try
                        {
                            var locs = worldAllGeneric.MakeGenericMethod(locationType).Invoke(null, null) as System.Collections.IEnumerable;
                            int count = 0;
                            foreach (var l in locs ?? new object[0]) count++;
                            bag["world_all_locations_count"] = count;
                        }
                        catch (Exception e) { bag["world_all_loc_err"] = e.Message; }
                    }
                }

                // 10. ItemSpawningDataRuntime constructor on Item — can we cheaply mark as discovered without spawning?
                var spawningDataRuntime = ResolveType("Awaken.TG.Main.Heroes.Items.LootTables.ItemSpawningDataRuntime");
                if (spawningDataRuntime != null)
                {
                    bag["SpawningDataRuntime_methods"] = spawningDataRuntime.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .Take(15).ToList();
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return Json(bag);
        }

        private static string Json(object o) => JsonConvert.SerializeObject(o, Formatting.Indented);

        private static IEnumerable<Type> SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch { return Array.Empty<Type>(); }
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
