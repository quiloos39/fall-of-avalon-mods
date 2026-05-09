using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace QuestPath
{
    // One-shot live introspection of the player's tracked quest. Loaded via HttpProbe /eval
    // (Assembly.Load(byte[])) into the running game, executed once on the main thread, returns a
    // formatted string. Doesn't touch QuestPathController state — read-only.
    //
    // Uses reflection rather than compile-time TG.Main types so it can be re-shipped via /eval
    // even if game internals shift between Steam updates.
    public static class Probe
    {
        public static string Run()
        {
            var sb = new StringBuilder();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                if (heroType == null) return "ERR: Hero type not found";
                var hero = heroType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero == null) return "ERR: Hero.Current is null (loading screen / title)";

                Vector3 pp = (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero);
                sb.AppendLine($"hero.Coords = {pp}");

                var qtType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTracker");
                var elementGen = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var tracker = elementGen?.MakeGenericMethod(qtType).Invoke(hero, null);
                if (tracker == null) return sb + "\nERR: no QuestTracker on hero";

                var activeQuest = qtType.GetProperty("ActiveQuest")?.GetValue(tracker);
                if (activeQuest == null)
                {
                    sb.AppendLine("ActiveQuest = NULL  (no quest is pinned/tracked)");
                    var allQuestsProp = qtType.GetProperty("Quests") ?? qtType.GetProperty("AllQuests");
                    var qs = allQuestsProp?.GetValue(tracker) as IEnumerable;
                    if (qs != null)
                    {
                        sb.AppendLine("Available quests on tracker:");
                        foreach (var q in qs.Cast<object>().Take(20))
                            sb.AppendLine($"  - '{q.GetType().GetProperty("DisplayName")?.GetValue(q)}' state={q.GetType().GetProperty("State")?.GetValue(q)}");
                    }
                    return sb.ToString();
                }

                sb.AppendLine($"ActiveQuest: '{activeQuest.GetType().GetProperty("DisplayName")?.GetValue(activeQuest)}'");
                sb.AppendLine($"  Description: {Truncate(activeQuest.GetType().GetProperty("Description")?.GetValue(activeQuest)?.ToString(), 200)}");

                var objsProp = activeQuest.GetType().GetProperty("ActiveObjectives") ?? activeQuest.GetType().GetProperty("Objectives");
                var objs = (objsProp?.GetValue(activeQuest) as IEnumerable)?.Cast<object>().ToList();
                sb.AppendLine($"  ActiveObjectives.Count = {objs?.Count ?? -1}");
                if (objs == null || objs.Count == 0) return sb.ToString();

                var locRefType = ResolveType("Awaken.TG.Main.Locations.LocationReference");
                var matchingM = locRefType?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "MatchingLocations" && m.GetParameters().Length == 1);

                int oi = 0;
                foreach (var objective in objs)
                {
                    sb.AppendLine($"\n  [{oi++}] '{objective.GetType().GetProperty("Name")?.GetValue(objective)}'  state={objective.GetType().GetProperty("State")?.GetValue(objective)}");
                    sb.AppendLine($"      Description: {Truncate(objective.GetType().GetProperty("Description")?.GetValue(objective)?.ToString(), 200)}");
                    try
                    {
                        var trackerDesc = objective.GetType().GetMethod("GetQuestTrackerDescription")?.Invoke(objective, null);
                        sb.AppendLine($"      TrackerText: {Truncate(trackerDesc?.ToString(), 200)}");
                    }
                    catch (Exception e) { sb.AppendLine($"      TrackerText threw: {(e.InnerException ?? e).Message}"); }

                    var markers = objective.GetType().GetProperty("MarkersData")?.GetValue(objective) as Array;
                    sb.AppendLine($"      MarkersData.Length = {markers?.Length ?? -1}");
                    if (markers == null || markers.Length == 0) continue;

                    for (int i = 0; i < markers.Length; i++)
                    {
                        var m = markers.GetValue(i);
                        var mt = m.GetType();
                        bool storyGated = (bool)mt.GetProperty("IsMarkerRelatedToStory").GetValue(m);
                        var flag = mt.GetProperty("RelatedStoryFlag").GetValue(m);
                        bool flagVal = false;
                        try { var get = flag?.GetType().GetMethod("Get", Type.EmptyTypes); if (get != null) flagVal = (bool)get.Invoke(flag, null); } catch { }
                        bool visible = (bool)mt.GetProperty("MarkerVisible").GetValue(m);
                        var targetScene = mt.GetProperty("TargetScene").GetValue(m);
                        var sceneName = targetScene?.GetType().GetProperty("Name")?.GetValue(targetScene)?.ToString() ?? "<null>";
                        bool sceneIsSet = false;
                        try { sceneIsSet = (bool)(targetScene?.GetType().GetProperty("IsSet")?.GetValue(targetScene) ?? false); } catch { }
                        var locRef = mt.GetProperty("LocationReference").GetValue(m);
                        sb.AppendLine($"        marker[{i}]  storyGated={storyGated}  flag.Get()={flagVal}  Visible={visible}  TargetScene='{sceneName}' (IsSet={sceneIsSet})");
                        sb.AppendLine($"          LocationReference: {locRef?.ToString() ?? "<null>"}");
                        if (locRef == null || matchingM == null) continue;
                        try
                        {
                            var matches = matchingM.Invoke(locRef, new object[] { null }) as IEnumerable;
                            int hit = 0;
                            if (matches != null)
                            {
                                foreach (var loc in matches)
                                {
                                    hit++;
                                    if (hit > 5) { sb.AppendLine("            ... (truncated)"); break; }
                                    var coordsObj = loc?.GetType().GetProperty("Coords")?.GetValue(loc);
                                    sb.AppendLine($"            match: {loc?.GetType().Name}  Coords={coordsObj}  Template={loc?.GetType().GetProperty("Template")?.GetValue(loc)?.GetType().Name}");
                                }
                            }
                            sb.AppendLine($"          MatchingLocations(null) returned {hit} hit(s)");
                        }
                        catch (Exception e) { sb.AppendLine($"          MatchingLocations threw: {(e.InnerException ?? e).Message}"); }
                    }
                }

                // Also report current scene + how many Location models are in World right now
                try
                {
                    var sceneSvcType = ResolveType("Awaken.TG.Main.Scenes.SceneService");
                    var worldType = ResolveType("Awaken.TG.MVC.World");
                    var servicesProp = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static);
                    var services = servicesProp?.GetValue(null);
                    if (services != null && sceneSvcType != null)
                    {
                        var getGen = services.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(mm => mm.Name == "Get" && mm.IsGenericMethod && mm.GetParameters().Length == 0);
                        var svc = getGen?.MakeGenericMethod(sceneSvcType).Invoke(services, null);
                        sb.AppendLine($"\nSceneService.MainSceneRef: {svc?.GetType().GetProperty("MainSceneRef")?.GetValue(svc)?.ToString()}");
                        sb.AppendLine($"SceneService.IsOpenWorld: {svc?.GetType().GetProperty("IsOpenWorld")?.GetValue(svc)}");
                    }
                    // Count World.All<Location>()
                    var locType = ResolveType("Awaken.TG.Main.Locations.Location");
                    var allGen = worldType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(mm => mm.Name == "All" && mm.IsGenericMethod && mm.GetParameters().Length == 0);
                    if (locType != null && allGen != null)
                    {
                        var locs = allGen.MakeGenericMethod(locType).Invoke(null, null) as IEnumerable;
                        int n = 0; foreach (var _ in locs ?? Enumerable.Empty<object>()) n++;
                        sb.AppendLine($"World.All<Location>() count = {n}");
                    }
                }
                catch (Exception e) { sb.AppendLine($"scene/loc count probe threw: {(e.InnerException ?? e).Message}"); }
            }
            catch (Exception e)
            {
                sb.AppendLine($"PROBE FAILED: {e}");
            }
            return sb.ToString();
        }

        // Add `qty` of an ItemTemplate whose asset name exactly equals `assetName`.
        public static string GiveItemExact(string assetName, int qty)
        {
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero == null) return "ERR: Hero.Current is null";
                var inventory = heroType.GetProperty("Inventory", BindingFlags.Public | BindingFlags.Instance)?.GetValue(hero);
                if (inventory == null) return "ERR: Hero.Inventory is null";

                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var tpType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var getGen = services?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var tp = getGen?.MakeGenericMethod(tpType).Invoke(services, null);
                var itemTemplateType = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                var getAllNonGen = tp.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && !m.IsGenericMethod && m.GetParameters().Length == 2);
                var flagEnumType = getAllNonGen.GetParameters()[1].ParameterType;
                var regularFlag = Enum.Parse(flagEnumType, "Regular");
                var allTemplates = getAllNonGen.Invoke(tp, new object[] { itemTemplateType, regularFlag }) as IEnumerable;

                object chosen = null;
                foreach (var t in allTemplates)
                {
                    if ((t as UnityEngine.Object)?.name == assetName) { chosen = t; break; }
                }
                if (chosen == null) return $"ERR: no ItemTemplate with asset name == '{assetName}'";

                // ChangeQuantity is an extension method on ItemUtils — static, takes (ItemTemplate, IInventory, int, ...)
                var itemUtilsType = ResolveType("Awaken.TG.Main.Heroes.Items.ItemUtils");
                var changeQty = itemUtilsType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "ChangeQuantity"
                        && m.GetParameters().Length >= 3
                        && m.GetParameters()[0].ParameterType.Name == "ItemTemplate");
                if (changeQty == null) return "ERR: ItemUtils.ChangeQuantity not found";
                var parms = changeQty.GetParameters();
                var callArgs = new object[parms.Length];
                callArgs[0] = chosen; callArgs[1] = inventory; callArgs[2] = qty;
                for (int i = 3; i < parms.Length; i++) callArgs[i] = parms[i].HasDefaultValue ? parms[i].DefaultValue : null;
                changeQty.Invoke(null, callArgs);
                return $"Added {qty} × '{assetName}' to hero inventory.";
            }
            catch (Exception e) { return $"FAILED: {e}"; }
        }

        // Add `qty` of any ItemTemplate whose itemName.IdOverride or asset name contains
        // `nameSubstring` (case-insensitive). Logs candidates if ambiguous; only adds when unique.
        public static string GiveItem(string nameSubstring, int qty)
        {
            var sb = new StringBuilder();
            try
            {
                if (string.IsNullOrWhiteSpace(nameSubstring)) return "ERR: nameSubstring required";
                if (qty == 0) return "ERR: qty must be non-zero";

                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero == null) return "ERR: Hero.Current is null";
                var inventory = heroType.GetProperty("Inventory", BindingFlags.Public | BindingFlags.Instance)?.GetValue(hero);
                if (inventory == null) return "ERR: Hero.Inventory is null";

                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var tpType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var getGen = services?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var tp = getGen?.MakeGenericMethod(tpType).Invoke(services, null);
                if (tp == null) return "ERR: TemplatesProvider unresolved";

                var itemTemplateType = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                // Non-generic overload: GetAllOfType(Type, TemplateTypeFlag = Regular). Easier to call via reflection.
                var getAllNonGen = tp.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && !m.IsGenericMethod && m.GetParameters().Length == 2);
                if (getAllNonGen == null) return "ERR: GetAllOfType(Type, TemplateTypeFlag) not found";
                var flagEnumType = getAllNonGen.GetParameters()[1].ParameterType;
                var regularFlag = Enum.Parse(flagEnumType, "Regular");
                var allTemplates = getAllNonGen.Invoke(tp, new object[] { itemTemplateType, regularFlag }) as IEnumerable;
                if (allTemplates == null) return "ERR: GetAllOfType returned null";

                string needle = nameSubstring.ToLowerInvariant();
                var matches = new System.Collections.Generic.List<object>();
                foreach (var t in allTemplates)
                {
                    if (t == null) continue;
                    string assetName = (t as UnityEngine.Object)?.name ?? "";
                    string idOverride = "";
                    try
                    {
                        var itemName = t.GetType().GetField("itemName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(t)
                                    ?? t.GetType().GetProperty("itemName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(t);
                        idOverride = itemName?.GetType().GetField("IdOverride")?.GetValue(itemName)?.ToString()
                                  ?? itemName?.GetType().GetProperty("IdOverride")?.GetValue(itemName)?.ToString()
                                  ?? "";
                    }
                    catch { }
                    if ((assetName.ToLowerInvariant().Contains(needle)) || (idOverride.ToLowerInvariant().Contains(needle)))
                        matches.Add(t);
                }

                sb.AppendLine($"Searched ItemTemplates for '{nameSubstring}' → {matches.Count} match(es)");
                int show = Math.Min(matches.Count, 30);
                for (int i = 0; i < show; i++)
                {
                    var t = matches[i];
                    string assetName = (t as UnityEngine.Object)?.name ?? "<unnamed>";
                    string idOverride = "";
                    try
                    {
                        var itemName = t.GetType().GetField("itemName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(t)
                                    ?? t.GetType().GetProperty("itemName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(t);
                        idOverride = itemName?.GetType().GetField("IdOverride")?.GetValue(itemName)?.ToString()
                                  ?? itemName?.GetType().GetProperty("IdOverride")?.GetValue(itemName)?.ToString()
                                  ?? "";
                    }
                    catch { }
                    sb.AppendLine($"  [{i}] asset='{assetName}'  IdOverride='{idOverride}'");
                }
                if (matches.Count > show) sb.AppendLine($"  ... ({matches.Count - show} more)");

                if (matches.Count == 0) return sb + "\nNo match — try a different substring.";
                if (matches.Count > 1) return sb + "\nMultiple matches — narrow the substring (or call GiveItemExact).";

                var chosen = matches[0];
                var changeQty = chosen.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "ChangeQuantity" && m.GetParameters().Length == 2);
                if (changeQty == null) return sb + "\nERR: ChangeQuantity method not found on ItemTemplate";
                changeQty.Invoke(chosen, new object[] { inventory, qty });
                sb.AppendLine($"\nAdded {qty} × '{(chosen as UnityEngine.Object)?.name}' to hero inventory.");
            }
            catch (Exception e) { sb.AppendLine($"FAILED: {e}"); }
            return sb.ToString();
        }

        private static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "<null>";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
