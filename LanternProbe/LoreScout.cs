using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class LoreScout
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // 1) Awaken.Babel — find the central localization manager / database
                var babelTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name?.StartsWith("Awaken.Babel") == true)
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .Where(t => t != null && !t.IsNested && !t.FullName.Contains("+<"))
                    .Take(50)
                    .Select(t => t.FullName).ToList();
                bag["babelTypes_sample"] = babelTypes;

                // Try to find a singleton / catalog
                foreach (var typeName in new[] {
                    "Awaken.Babel.Localization.LocalizationCatalog",
                    "Awaken.Babel.Localization.LocalizationManager",
                    "Awaken.Babel.Localization.Catalog",
                    "Awaken.Babel.Localization.Tables.LocalizationTable",
                    "Awaken.Babel.Babel",
                })
                {
                    var t = ResolveType(typeName);
                    if (t != null)
                    {
                        bag["found_" + typeName] = new
                        {
                            staticMembers = t.GetMembers(BindingFlags.Public | BindingFlags.Static).Take(20).Select(m => $"{m.MemberType} {m.Name}").ToList(),
                            instanceMethods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic).Take(15).Select(m => m.Name).ToList(),
                        };
                    }
                }

                // 2) Look for "LocString" / readable / dialog story types in TG.Main
                var tgQuery = new[] { "LocString", "Readable", "ItemReadable", "Note", "Letter", "Book",
                                      "Dialogue", "Dialog", "Bark", "VoiceOver",
                                      "StoryStep", "StoryGraph", "Conversation" };
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name?.StartsWith("TG.") == true || a.GetName().Name?.StartsWith("Awaken.") == true)
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .Where(t => t != null && !t.FullName.Contains("+<") && !t.FullName.Contains("d__"))
                    .ToList();
                var byKw = new Dictionary<string, List<string>>();
                foreach (var k in tgQuery) byKw[k] = new List<string>();
                foreach (var t in allTypes)
                {
                    foreach (var k in tgQuery)
                    {
                        if (t.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0
                         && byKw[k].Count < 12)
                            byKw[k].Add(t.FullName);
                    }
                }
                bag["typesByKeyword"] = byKw.Where(kv => kv.Value.Count > 0)
                    .ToDictionary(kv => kv.Key, kv => (object)kv.Value);

                // 3) Inspect LocString  — game's text wrapper
                var locStringType = ResolveType("Awaken.Babel.Localization.LocString")
                                ?? ResolveType("Awaken.TG.Main.Localization.LocString")
                                ?? allTypes.FirstOrDefault(t => t.Name == "LocString");
                if (locStringType != null)
                {
                    bag["LocString_type"] = locStringType.FullName;
                    bag["LocString_props"] = locStringType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Take(15).Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
                    bag["LocString_methods"] = locStringType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Take(15).Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})").ToList();
                }

                // 4) ItemReadable / ReadableSpec
                var readableT = allTypes.FirstOrDefault(t => t.Name == "Readable" || t.Name == "ItemReadable" || t.Name == "ReadableSpec");
                if (readableT != null) bag["readableExample"] = readableT.FullName;

                // 5) Find existing Babel string-table assets via Resources lookup
                var allSO = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>();
                bag["totalScriptableObjects"] = allSO.Length;

                var localizationSOs = allSO
                    .Where(so => so != null && (
                           so.GetType().FullName.Contains("Localization")
                        || so.GetType().FullName.Contains("Babel")
                        || so.GetType().FullName.Contains("StringTable")))
                    .GroupBy(so => so.GetType().FullName)
                    .OrderByDescending(g => g.Count())
                    .Select(g => $"{g.Key}  count={g.Count()}  sample={g.First().name}")
                    .Take(20).ToList();
                bag["localizationSOs"] = localizationSOs;
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
