using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Walk the same filter chain CraftAnything's RecipeInjectionCache uses, count drops
    // at each stage, and show samples of rejected items. Tells us whether the filter is
    // wrong, the dedup against vanilla is too aggressive, or the recipe factory is silently
    // failing to build outcomes.
    public static class InjectionDiag
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Resolve services
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);

                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var heroItemsType = ResolveType("Awaken.TG.Main.Heroes.Items.HeroItems");
                var heroItems = hero?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0)
                    ?.MakeGenericMethod(heroItemsType).Invoke(hero, null);
                var known = heroItemsType.GetProperty("KnownItems")?.GetValue(heroItems) as HashSet<string>;
                bag["known_count"] = known?.Count ?? 0;

                // GetAllOfType<ItemTemplate>(TemplateTypeFlag.Regular)
                var itemTemplate = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                var flagType = ResolveType("Awaken.TG.Main.Templates.TemplateTypeFlag");
                var flagRegular = Enum.Parse(flagType, "Regular");
                var getAllOfType = providerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                var allItemsRaw = getAllOfType?.MakeGenericMethod(itemTemplate).Invoke(provider, new object[] { flagRegular }) as System.Collections.IEnumerable;
                var byGuid = new Dictionary<string, object>();
                foreach (var it in allItemsRaw ?? new object[0])
                {
                    var g = it?.GetType().GetProperty("GUID", BindingFlags.Public | BindingFlags.Instance)?.GetValue(it) as string;
                    if (!string.IsNullOrEmpty(g)) byGuid[g] = it;
                }
                bag["byGuid_count"] = byGuid.Count;

                // Vanilla forge outcome GUIDs — same as what CraftAnything excludes
                var hcTemplateType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.HandcraftingTemplate");
                var existingForgeGuids = new HashSet<string>();
                int vanillaForgeRecipes = 0;
                foreach (var ct in (getAllOfType?.MakeGenericMethod(hcTemplateType).Invoke(provider, new object[] { flagRegular }) as System.Collections.IEnumerable) ?? new object[0])
                {
                    var recipes = ct.GetType().GetProperty("Recipes")?.GetValue(ct) as System.Collections.IEnumerable;
                    foreach (var r in recipes ?? new object[0])
                    {
                        vanillaForgeRecipes++;
                        var outcome = r.GetType().GetProperty("Outcome")?.GetValue(r);
                        var g = outcome?.GetType().GetProperty("GUID", BindingFlags.Public | BindingFlags.Instance)?.GetValue(outcome) as string;
                        if (!string.IsNullOrEmpty(g)) existingForgeGuids.Add(g);
                    }
                }
                bag["vanilla_forge_recipes"] = vanillaForgeRecipes;
                bag["vanilla_forge_unique_outcomes"] = existingForgeGuids.Count;

                // Walk known items, apply filter chain
                int notInTemplates = 0;     // GUID in KnownItems but no template found
                int hiddenOnUI = 0;
                int rejectedNonCraft = 0;   // book/recipe/spell/key/ingredients/craft
                int rejectedNotForge = 0;
                int rejectedQuest = 0;
                int rejectedDuplicate = 0;
                int passed = 0;
                var passedSamples = new List<string>();
                var rejectedNotForgeSamples = new List<string>();

                foreach (var guid in known ?? new HashSet<string>())
                {
                    if (!byGuid.TryGetValue(guid, out var t)) { notInTemplates++; continue; }
                    var tags = GetTags(t);
                    var name = t.GetType().GetProperty("ItemName")?.GetValue(t)?.ToString() ?? "?";

                    bool hidden = false;
                    try { hidden = (bool)(t.GetType().GetProperty("HiddenOnUI")?.GetValue(t) ?? false); } catch { }
                    if (hidden) { hiddenOnUI++; continue; }

                    if (tags.Contains("item:book") || tags.Contains("item:recipe") || tags.Contains("item:spell")
                        || tags.Contains("item:key") || tags.Any(x => x.StartsWith("ingredients:"))
                        || tags.Any(x => x.StartsWith("craft:"))) { rejectedNonCraft++; continue; }

                    if (!IsForge(tags))
                    {
                        rejectedNotForge++;
                        if (rejectedNotForgeSamples.Count < 8 && !string.IsNullOrEmpty(name))
                            rejectedNotForgeSamples.Add($"{name}  tags=[{string.Join(",", tags)}]");
                        continue;
                    }

                    // Quest filter (ExcludeQuestItems = true)
                    bool cannotBeDropped = false;
                    try { cannotBeDropped = (bool)(t.GetType().GetProperty("CannotBeDropped")?.GetValue(t) ?? false); } catch { }
                    if (cannotBeDropped || tags.Any(x => x.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0))
                    { rejectedQuest++; continue; }

                    if (existingForgeGuids.Contains(guid)) { rejectedDuplicate++; continue; }

                    passed++;
                    if (passedSamples.Count < 12 && !string.IsNullOrEmpty(name))
                        passedSamples.Add($"{name}  tags=[{string.Join(",", tags)}]");
                }

                bag["notInTemplates"] = notInTemplates;
                bag["hiddenOnUI"] = hiddenOnUI;
                bag["rejectedNonCraft"] = rejectedNonCraft;
                bag["rejectedNotForge"] = rejectedNotForge;
                bag["rejectedQuest"] = rejectedQuest;
                bag["rejectedDuplicate"] = rejectedDuplicate;
                bag["passed_forge"] = passed;
                bag["passed_samples"] = passedSamples;
                bag["rejected_notForge_samples"] = rejectedNotForgeSamples;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static List<string> GetTags(object t)
        {
            try
            {
                var tags = t.GetType().GetProperty("Tags", BindingFlags.Public | BindingFlags.Instance)?.GetValue(t) as System.Collections.IEnumerable;
                if (tags == null) return new List<string>();
                var list = new List<string>();
                foreach (var x in tags) if (x != null) list.Add(x.ToString());
                return list;
            }
            catch { return new List<string>(); }
        }

        private static bool IsForge(List<string> tags)
        {
            if (tags.Contains("item:weapon")) return true;
            if (tags.Any(x => x.StartsWith("weapons:"))) return true;
            if (tags.Any(x => x.StartsWith("armors:"))) return true;
            if (tags.Any(x => x == "type:bow" || x == "type:sword" || x == "type:dagger"
                           || x == "type:axe" || x == "type:mace" || x == "type:spear"
                           || x == "type:shield" || x == "type:hammer")) return true;
            if (tags.Contains("item:amulet")) return true;
            if (tags.Contains("item:ring")) return true;
            if (tags.Contains("item:bow")) return true;
            return false;
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
