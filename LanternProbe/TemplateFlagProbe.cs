using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LanternProbe
{
    public static class TemplateFlagProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Find the TemplateTypeFlag enum
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(SafeGetTypes).Where(t => t != null && !t.Name.Contains("<")).ToList();

                var flagType = allTypes.FirstOrDefault(t => t.Name == "TemplateTypeFlag");
                if (flagType == null) { bag["err"] = "TemplateTypeFlag not found"; return JsonConvert.SerializeObject(bag, Formatting.Indented); }

                bag["TemplateTypeFlag_full"] = flagType.FullName;
                bag["TemplateTypeFlag_isEnum"] = flagType.IsEnum;
                bag["TemplateTypeFlag_underlyingType"] = flagType.IsEnum ? Enum.GetUnderlyingType(flagType).Name : null;
                bag["TemplateTypeFlag_values"] = flagType.IsEnum
                    ? Enum.GetNames(flagType).Select(n => $"{n} = {Convert.ToInt64(Enum.Parse(flagType, n))}").ToList()
                    : null;

                // Try invoking GetAllOfType<ItemTemplate>(TemplateTypeFlag.All) (or similar broad value)
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);

                var itemTemplate = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                var getAllOfType = providerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

                if (getAllOfType != null && flagType.IsEnum)
                {
                    // Try every named value to find one that returns the most items
                    foreach (var name in Enum.GetNames(flagType))
                    {
                        var flagVal = Enum.Parse(flagType, name);
                        try
                        {
                            var result = getAllOfType.MakeGenericMethod(itemTemplate).Invoke(provider, new[] { flagVal }) as System.Collections.IEnumerable;
                            int n = 0;
                            string firstName = null;
                            if (result != null)
                            {
                                foreach (var x in result)
                                {
                                    if (n == 0)
                                    {
                                        firstName = x?.GetType().GetProperty("ItemName")?.GetValue(x)?.ToString()
                                                 ?? (x as UnityEngine.Object)?.name;
                                    }
                                    n++;
                                    if (n > 10000) break;
                                }
                            }
                            bag[$"flag_{name}_count"] = n;
                            if (firstName != null) bag[$"flag_{name}_firstItem"] = firstName;
                        }
                        catch (Exception e) { bag[$"flag_{name}_err"] = e.GetBaseException().Message; }
                    }
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

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
    }
}
