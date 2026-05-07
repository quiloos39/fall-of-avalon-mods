using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // Walks every loaded template / quest / objective / dialog graph in the game's runtime
    // memory, translates every embedded LocString via the static LocString.GetTranslation(id),
    // and writes organized text files to game-lore/.
    //
    // What gets written:
    //   game-lore/items/<itemName>.txt           — name + flavor + description for every ItemTemplate
    //   game-lore/items.csv                      — index of all items
    //   game-lore/readables/<bookName>.txt       — readable items with their content
    //   game-lore/quests/<questName>.txt         — name + description + objectives
    //   game-lore/locstring-by-id.txt            — every LocString ID we encountered with its translation
    //   game-lore/_summary.txt                   — counts + warnings
    public static class LoreExporter
    {
        public static string Run()
        {
            var summary = new StringBuilder();
            try
            {
                string outRoot = @"C:\Users\quilo\source\game-lore";
                Directory.CreateDirectory(outRoot);
                Directory.CreateDirectory(Path.Combine(outRoot, "items"));
                Directory.CreateDirectory(Path.Combine(outRoot, "readables"));
                Directory.CreateDirectory(Path.Combine(outRoot, "quests"));

                // We don't need GetTranslation — every LocString instance has its own Translate()
                // method, which calls into BabelManager internally. We just call it on each
                // LocString we find on a template.
                MethodInfo translateM = null;
                var allLocStrings = new Dictionary<string, string>(); // id → translation

                // ── ItemTemplates ──────────────────────────────────────────────────────
                int itemCount = ExportItems(outRoot, translateM, allLocStrings, summary);

                // ── Quests (whatever's loaded — the active quest at minimum) ───────────
                int questCount = ExportQuests(outRoot, translateM, allLocStrings, summary);

                // ── Master locstring index ─────────────────────────────────────────────
                using (var sw = new StreamWriter(Path.Combine(outRoot, "locstring-by-id.txt"), false, Encoding.UTF8))
                {
                    sw.WriteLine($"# All LocString IDs seen during export ({allLocStrings.Count} entries)");
                    sw.WriteLine();
                    foreach (var kv in allLocStrings.OrderBy(k => k.Key))
                    {
                        sw.WriteLine($"[{kv.Key}]");
                        sw.WriteLine(kv.Value ?? "");
                        sw.WriteLine();
                    }
                }

                File.WriteAllText(Path.Combine(outRoot, "_summary.txt"),
                    $"Exported {itemCount} items, {questCount} quests, {allLocStrings.Count} unique LocStrings.\n\n" + summary.ToString(),
                    Encoding.UTF8);

                return JsonConvert.SerializeObject(new
                {
                    outputRoot = outRoot,
                    items = itemCount,
                    quests = questCount,
                    uniqueLocStrings = allLocStrings.Count,
                });
            }
            catch (Exception e) { return "ERR: " + e; }
        }

        private static int ExportItems(string outRoot, MethodInfo translateM, Dictionary<string, string> bag, StringBuilder summary)
        {
            try
            {
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);
                var itemTemplateType = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");

                // The generic method has multiple overloads — pick the no-arg one explicitly.
                var getAllMs = providerType.GetMethods().Where(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition).ToList();
                summary.AppendLine($"GetAllOfType overloads: {getAllMs.Count}");
                foreach (var m in getAllMs) summary.AppendLine($"  {m.Name}<T>({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})");

                var noArgGetAll = getAllMs.FirstOrDefault(m => m.GetParameters().Length == 0);
                System.Collections.IEnumerable allItems = null;
                if (noArgGetAll != null)
                {
                    allItems = noArgGetAll.MakeGenericMethod(itemTemplateType).Invoke(provider, new object[0]) as System.Collections.IEnumerable;
                }
                else
                {
                    // Try the 1-arg version with default — it might want a bool
                    var oneArg = getAllMs.FirstOrDefault(m => m.GetParameters().Length == 1);
                    if (oneArg != null)
                    {
                        var p = oneArg.GetParameters()[0];
                        object def = p.HasDefaultValue ? p.DefaultValue
                                    : (p.ParameterType == typeof(bool) ? (object)false
                                    : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null));
                        allItems = oneArg.MakeGenericMethod(itemTemplateType).Invoke(provider, new object[] { def }) as System.Collections.IEnumerable;
                    }
                }

                if (allItems == null) { summary.AppendLine("[items] no GetAllOfType overload worked"); return 0; }

                int n = 0;
                int readables = 0;
                using (var idx = new StreamWriter(Path.Combine(outRoot, "items.csv"), false, Encoding.UTF8))
                {
                    idx.WriteLine("name,tier,isReadable,price,weight,filename");
                    foreach (var t in allItems)
                    {
                        try
                        {
                            string itemName = (t.GetType().GetProperty("ItemName")?.GetValue(t) as string) ?? "";
                            string filename = SafeFilename(itemName.Length > 0 ? itemName : t.GetType().Name + "_" + n);

                            // Pull LocString IDs for itemName, description, flavor
                            var nameLs = t.GetType().GetField("itemName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(t);
                            var descLs = t.GetType().GetField("description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(t);
                            var flavorLs = t.GetType().GetField("flavor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(t);

                            string nameTr = TranslateLs(nameLs, translateM, bag);
                            string descTr = TranslateLs(descLs, translateM, bag);
                            string flavorTr = TranslateLs(flavorLs, translateM, bag);

                            bool isReadable = (bool?)t.GetType().GetProperty("IsReadable")?.GetValue(t) == true;
                            float weight = (float?)t.GetType().GetProperty("Weight")?.GetValue(t) ?? 0f;
                            int price = (int?)t.GetType().GetProperty("BasePrice")?.GetValue(t) ?? 0;

                            // Write per-item file under items/ (or readables/ if marked readable)
                            string subDir = isReadable ? "readables" : "items";
                            string outPath = Path.Combine(outRoot, subDir, filename + ".txt");

                            var sb = new StringBuilder();
                            sb.AppendLine("# " + (string.IsNullOrEmpty(nameTr) ? itemName : nameTr));
                            sb.AppendLine();
                            if (!string.IsNullOrWhiteSpace(flavorTr)) { sb.AppendLine("> " + flavorTr.Replace("\n", "\n> ")); sb.AppendLine(); }
                            if (!string.IsNullOrWhiteSpace(descTr))   { sb.AppendLine(descTr); sb.AppendLine(); }
                            sb.AppendLine($"---");
                            sb.AppendLine($"price={price}  weight={weight}  readable={isReadable}");
                            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);

                            idx.WriteLine($"\"{Csv(nameTr)}\",,{isReadable},{price},{weight},\"{Csv(filename)}\"");
                            n++;
                            if (isReadable) readables++;
                        }
                        catch (Exception e) { summary.AppendLine($"[item err] {e.Message}"); }
                    }
                }
                summary.AppendLine($"[items] exported {n} items ({readables} readable)");
                return n;
            }
            catch (Exception e) { summary.AppendLine($"[items] EXC: {e}"); return 0; }
        }

        private static int ExportQuests(string outRoot, MethodInfo translateM, Dictionary<string, string> bag, StringBuilder summary)
        {
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero == null) { summary.AppendLine("[quests] no hero"); return 0; }

                // QuestTracker has the active quest. World.All<Quest>() doesn't reliably enumerate
                // them, so we fall back to whatever is in the tracker. Plus iterate quest TEMPLATES
                // via TemplatesProvider — those have descriptions even for quests not yet started.
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);

                var questTemplateType = ResolveType("Awaken.TG.Main.Stories.Quests.QuestTemplate")
                                     ?? ResolveType("Awaken.TG.Main.Stories.Quests.QuestTemplateBase");
                if (questTemplateType == null) { summary.AppendLine("[quests] QuestTemplate type not found"); return 0; }

                var noArgGetAll = providerType.GetMethods()
                    .Where(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                    .FirstOrDefault();
                System.Collections.IEnumerable allQuests = null;
                if (noArgGetAll != null)
                    allQuests = noArgGetAll.MakeGenericMethod(questTemplateType).Invoke(provider, new object[0]) as System.Collections.IEnumerable;

                if (allQuests == null) { summary.AppendLine("[quests] couldn't iterate"); return 0; }

                int n = 0;
                foreach (var q in allQuests)
                {
                    try
                    {
                        string qName = q.GetType().GetProperty("name")?.GetValue(q)?.ToString()
                                    ?? q.GetType().Name + "_" + n;
                        string filename = SafeFilename(qName);

                        // Pull title + description LocStrings
                        var titleLs = q.GetType().GetField("title", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(q)
                                   ?? q.GetType().GetField("displayName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(q)
                                   ?? q.GetType().GetField("questName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(q);
                        var descLs = q.GetType().GetField("description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(q);

                        string titleTr = TranslateLs(titleLs, translateM, bag);
                        string descTr = TranslateLs(descLs, translateM, bag);

                        var sb = new StringBuilder();
                        sb.AppendLine("# " + (string.IsNullOrEmpty(titleTr) ? qName : titleTr));
                        sb.AppendLine();
                        if (!string.IsNullOrWhiteSpace(descTr)) { sb.AppendLine(descTr); sb.AppendLine(); }

                        // Walk all fields for any LocString-like nested objects (objectives, dialog hints)
                        foreach (var f in q.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            try
                            {
                                var v = f.GetValue(q);
                                if (v == null) continue;
                                if (v.GetType().Name == "LocString" || v.GetType().Name == "LightLocString" || v.GetType().Name == "OptionalLocString")
                                {
                                    var tr = TranslateLs(v, translateM, bag);
                                    if (!string.IsNullOrWhiteSpace(tr) && tr != titleTr && tr != descTr)
                                        sb.AppendLine($"### {f.Name}\n{tr}\n");
                                }
                            }
                            catch { }
                        }

                        File.WriteAllText(Path.Combine(outRoot, "quests", filename + ".txt"), sb.ToString(), Encoding.UTF8);
                        n++;
                    }
                    catch (Exception e) { summary.AppendLine($"[quest err] {e.Message}"); }
                }

                summary.AppendLine($"[quests] exported {n} quest templates");
                return n;
            }
            catch (Exception e) { summary.AppendLine($"[quests] EXC: {e}"); return 0; }
        }

        // Pull a translation out of any LocString-like object by calling its Translate() method.
        // Caches by FinalId so we don't re-translate the same string a thousand times.
        private static string TranslateLs(object ls, MethodInfo _, Dictionary<string, string> bag)
        {
            if (ls == null) return null;
            try
            {
                // Some game fields are wrapper structs: { bool toggled; LocString locString; }.
                // Drill into the inner LocString if present.
                var inner = ls;
                var locStringField = ls.GetType().GetField("locString", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (locStringField != null)
                {
                    var v = locStringField.GetValue(ls);
                    if (v != null) inner = v;
                }

                // Read FinalId for cache-key purposes (so we don't re-translate the same id).
                string id = null;
                try { id = inner.GetType().GetProperty("FinalId")?.GetValue(inner) as string; } catch { }
                if (string.IsNullOrEmpty(id))
                {
                    try { id = inner.GetType().GetField("ID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(inner) as string; } catch { }
                }
                if (!string.IsNullOrEmpty(id) && bag.TryGetValue(id, out var cached)) return cached;

                // Call inner.Translate() — instance method, no args.
                var trM = inner.GetType().GetMethod("Translate", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (trM == null) return null;
                string result = trM.Invoke(inner, null) as string;
                if (!string.IsNullOrEmpty(id)) bag[id] = result;
                return result;
            }
            catch { return null; }
        }

        private static string SafeFilename(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "_unnamed";
            var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).Distinct().ToArray();
            foreach (var c in invalid) s = s.Replace(c, '_');
            if (s.Length > 100) s = s.Substring(0, 100);
            return s;
        }

        private static string Csv(string s) => (s ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ");

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
