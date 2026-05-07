using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class ReadableProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);

                var itemTemplateType = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                // Pick the no-arg GetAllOfType<T>() overload specifically.
                var getAllOfType = providerType?.GetMethods()
                    .Where(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                    .FirstOrDefault();
                bag["getAllOfType_signature"] = getAllOfType == null ? "null" :
                    $"args=[{string.Join(",", getAllOfType.GetParameters().Select(p => p.ParameterType.Name))}]";
                var allItems = getAllOfType?.MakeGenericMethod(itemTemplateType).Invoke(provider, new object[0]) as System.Collections.IEnumerable;

                var readables = new List<object>();
                int totalItems = 0;
                foreach (var t in allItems ?? new object[0])
                {
                    totalItems++;
                    bool isReadable = false;
                    try { isReadable = (bool?)t.GetType().GetProperty("IsReadable")?.GetValue(t) == true; } catch { }
                    if (isReadable) readables.Add(t);
                }
                bag["totalItems"] = totalItems;
                bag["readableCount"] = readables.Count;

                if (readables.Count == 0) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                // Inspect first readable in detail
                var first = readables[0];
                bag["sampleReadable_name"] = first.GetType().GetProperty("ItemName")?.GetValue(first)?.ToString();

                // Look for any "readable", "text", "content", "page", "body", "note" properties/fields
                var allProps = first.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(p => Regex(p.Name, "(read|text|content|page|body|note|description)"))
                    .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
                var allFields = first.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(f => Regex(f.Name, "(read|text|content|page|body|note|description)"))
                    .Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();
                bag["sampleReadable_textProps"] = allProps;
                bag["sampleReadable_textFields"] = allFields;

                // Also list ALL components attached to its prefab (if accessible)
                bag["sampleReadable_first10_names"] = readables.Take(10).Select(r => r.GetType().GetProperty("ItemName")?.GetValue(r)?.ToString()).ToList();

                // Now check a known-readable spec: Look for an ItemTemplateAttachment for readables
                var attTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && n.StartsWith("TG."); })
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .Where(t => t != null && (t.Name.Contains("Readable") || t.Name.Contains("ReadAct") || t.Name.Contains("Read")))
                    .Select(t => t.FullName)
                    .Take(30).ToList();
                bag["readableTypes"] = attTypes;

                // Inspect ItemTemplate for any "ReadableContentRef" / similar
                var allItemProps = itemTemplateType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => Regex(p.Name, "(read|text|content|page|body|note|description|story|book)"))
                    .Select(p => $"{Pretty(p.PropertyType)} {p.Name}").ToList();
                var allItemFields = itemTemplateType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(f => Regex(f.Name, "(read|text|content|page|body|note|description|story|book)"))
                    .Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();
                bag["ItemTemplate_textProps"] = allItemProps;
                bag["ItemTemplate_textFields"] = allItemFields;
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static bool Regex(string s, string pat) => System.Text.RegularExpressions.Regex.IsMatch(s ?? "", pat, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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
