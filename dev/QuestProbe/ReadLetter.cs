using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;

namespace QuestProbe
{
    public static class ReadLetter
    {
        public static string Get()
        {
            try
            {
                Type heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                object hero = heroType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (hero == null) return "{\"error\":\"no Hero.Current\"}";

                Type itemsType = ResolveType("Awaken.TG.Main.Heroes.Items.HeroItems")
                              ?? ResolveType("Awaken.TG.Main.Heroes.HeroItems");
                var elementGeneric = hero.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .First(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                object items = elementGeneric.MakeGenericMethod(itemsType).Invoke(hero, null);

                object collection = null;
                foreach (var pname in new[] { "Items", "AllItems", "OwnedItems", "Inventory" })
                {
                    var p = items.GetType().GetProperty(pname);
                    if (p == null) continue;
                    collection = p.GetValue(items);
                    if (collection != null) break;
                }

                object letter = null;
                foreach (var item in ForceEnumerate(collection))
                {
                    string id = ReadStr(item, "Id") ?? ReadStr(item, "ID");
                    if (id == "Item:31767") { letter = item; break; }
                }
                if (letter == null) return "{\"error\":\"Item:31767 not found in inventory\"}";

                var sb = new StringBuilder();
                sb.Append("{\"item\":{");
                sb.Append("\"id\":").Append(J(ReadStr(letter, "ID")));
                sb.Append(",\"name\":").Append(J(ReadStr(letter, "DisplayName") ?? ReadStr(letter, "Name")));
                sb.Append(",\"template\":").Append(J(ReadStr(letter, "Template")));
                sb.Append("}");

                // Try Element<ItemRead>() to get the readable
                Type itemReadType = ResolveType("Awaken.TG.Main.Heroes.Items.Attachments.ItemRead");
                if (itemReadType != null)
                {
                    var itemElGeneric = letter.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                    object itemRead = null;
                    try { itemRead = itemElGeneric?.MakeGenericMethod(itemReadType).Invoke(letter, null); } catch { }
                    sb.Append(",\"itemRead\":").Append(DumpEverything(itemRead));
                }

                // Also dump all elements on the item — readable might live elsewhere
                sb.Append(",\"allElements\":[");
                bool firstE = true;
                var elementsProp = letter.GetType().GetProperty("Elements") ?? letter.GetType().GetProperty("AllElements");
                if (elementsProp?.GetValue(letter) is IEnumerable elements)
                {
                    foreach (var el in elements)
                    {
                        if (el == null) continue;
                        if (!firstE) sb.Append(",");
                        firstE = false;
                        sb.Append("{\"type\":").Append(J(el.GetType().FullName));
                        sb.Append(",\"props\":").Append(DumpProps(el));
                        sb.Append("}");
                    }
                }
                sb.Append("]");

                // Spec (template) may have the actual text content
                object template = letter.GetType().GetProperty("Template")?.GetValue(letter);
                if (template != null)
                {
                    sb.Append(",\"templateProps\":").Append(DumpProps(template));
                }
                sb.Append("}");
                return sb.ToString();
            }
            catch (Exception e)
            {
                var inner = (e is TargetInvocationException tie && tie.InnerException != null) ? tie.InnerException : e;
                return "{\"error\":" + J(inner.GetType().Name + ": " + inner.Message) + "}";
            }
        }

        private static string DumpEverything(object obj)
        {
            if (obj == null) return "null";
            var sb = new StringBuilder("{");
            sb.Append("\"type\":").Append(J(obj.GetType().FullName));
            sb.Append(",\"props\":").Append(DumpProps(obj));
            sb.Append(",\"fields\":").Append(DumpFields(obj));
            sb.Append("}");
            return sb.ToString();
        }

        private static string DumpProps(object obj)
        {
            if (obj == null) return "{}";
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var p in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                string val;
                try { val = StringifyAny(p.GetValue(obj)); }
                catch (Exception e) { val = "<<" + (e.InnerException ?? e).GetType().Name + ">>"; }
                if (val == null) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append(J(p.Name)).Append(":").Append(J(val));
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string DumpFields(object obj)
        {
            if (obj == null) return "{}";
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var f in obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic).OrderBy(f => f.Name))
            {
                if (f.Name.StartsWith("<")) continue;  // backing fields for auto-properties
                string val;
                try { val = StringifyAny(f.GetValue(obj)); }
                catch (Exception e) { val = "<<" + (e.InnerException ?? e).GetType().Name + ">>"; }
                if (val == null) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append(J(f.Name)).Append(":").Append(J(val));
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string StringifyAny(object v)
        {
            if (v == null) return "null";
            if (v is string s) return s.Length > 4000 ? s.Substring(0, 4000) + "…" : s;
            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum || v is decimal) return v.ToString();
            // Try to pull localized text out of LocString-like wrappers
            string loc = TryLocString(v);
            if (loc != null) return loc;
            string ts;
            try { ts = v.ToString(); } catch { ts = "<<ToString threw>>"; }
            if (ts == t.FullName || ts == t.Name) return null;  // useless default
            return ts.Length > 4000 ? ts.Substring(0, 4000) + "…" : ts;
        }

        private static string TryLocString(object loc)
        {
            if (loc == null) return null;
            string typeName = loc.GetType().Name;
            if (!typeName.Contains("LocString") && !typeName.Contains("Localized")) return null;
            foreach (var mname in new[] { "Translate", "GetLocalizedString", "GetLocalized", "Localize" })
            {
                var m = loc.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(mm => mm.Name == mname && mm.GetParameters().Length == 0);
                if (m != null) { try { var r = m.Invoke(loc, null); if (r != null) return "[loc] " + r; } catch { } }
            }
            foreach (var pname in new[] { "Translation", "LocalizedString", "Text", "Value" })
            {
                var p = loc.GetType().GetProperty(pname);
                if (p != null) { try { var r = p.GetValue(loc); if (r != null) return "[loc] " + r; } catch { } }
            }
            string id = ReadStr(loc, "ID") ?? ReadStr(loc, "Id");
            return id != null ? "[unresolved loc, key=" + id + "]" : null;
        }

        private static System.Collections.Generic.List<object> ForceEnumerate(object collection)
        {
            if (collection is IEnumerable plain)
            { try { return plain.Cast<object>().ToList(); } catch { } }
            var ge = collection.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetEnumerator" && m.GetParameters().Length == 0);
            if (ge != null)
            {
                try
                {
                    var en = ge.Invoke(collection, null);
                    if (en == null) return null;
                    var moveNext = en.GetType().GetMethod("MoveNext");
                    var current = en.GetType().GetProperty("Current");
                    var list = new System.Collections.Generic.List<object>();
                    while ((bool)moveNext.Invoke(en, null)) list.Add(current.GetValue(en));
                    return list;
                }
                catch { }
            }
            return null;
        }

        private static string ReadStr(object obj, string prop)
        { try { return obj?.GetType().GetProperty(prop)?.GetValue(obj)?.ToString(); } catch { return null; } }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }

        private static string J(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.AppendFormat("\\u{0:X4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }
    }
}
