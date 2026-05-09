using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class BabelStaticHunt
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var bmType = ResolveType("Awaken.Babel.BabelManager");
                var iBabelProvider = ResolveType("Awaken.Babel.IBabelProvider");
                var preloadedType = ResolveType("Awaken.Babel.PreloadedBabelProvider");
                var streamingType = ResolveType("Awaken.Babel.StreamingBabelProvider");

                var hits = new List<string>();
                var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => { var n = a.GetName().Name; return n != null && (n.StartsWith("TG.") || n.StartsWith("Awaken.")); });

                foreach (var asm in assemblies)
                {
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

                    foreach (var t in types.Where(t => t != null))
                    {
                        // Static fields of any type in Babel namespace, OR any field referencing BabelManager / IBabelProvider
                        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                        {
                            if (f.FieldType == bmType || f.FieldType == iBabelProvider
                                || f.FieldType == preloadedType || f.FieldType == streamingType
                                || (f.FieldType.Namespace != null && f.FieldType.Namespace.StartsWith("Awaken.Babel")))
                            {
                                try
                                {
                                    var v = f.GetValue(null);
                                    hits.Add($"{t.FullName}.{f.Name}  type={Pretty(f.FieldType)}  value={(v == null ? "null" : v.GetType().Name)}");
                                }
                                catch { }
                            }
                        }
                    }
                }
                bag["babelStaticFields"] = hits.Take(40).ToList();
                bag["babelStaticFieldsTotal"] = hits.Count;

                // Try every translate-related thing on LocString
                var locString = ResolveType("Awaken.TG.Main.Localization.LocString");
                if (locString != null)
                {
                    var allMethods = locString.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    bag["LocString_static_methods"] = allMethods.Take(20).Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})").ToList();
                    var allFields = locString.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    bag["LocString_static_fields"] = allFields.Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();
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
