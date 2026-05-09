using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // Reach into the live CraftAnything DLL (already loaded as a BepInEx plugin) and
    // invoke its RecipeFactory.Build() directly so we see the actual exception that's
    // making 228/229 recipes fail.
    public static class InvokeBuild
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Find the CraftAnything assembly + the RecipeFactory type
                var caAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "CraftAnything");
                bag["caAsm_found"] = caAsm != null;
                if (caAsm == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                var factoryType = caAsm.GetType("CraftAnything.RecipeFactory");
                bag["factoryType_found"] = factoryType != null;

                var stationType = factoryType.GetNestedType("Station");
                bag["stationType_found"] = stationType != null;

                // Get the Build method — public static
                var buildMethods = factoryType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name == "Build")
                    .Select(m => $"{m.ReturnType.Name} {m.Name}({string.Join(",", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})").ToList();
                bag["build_overloads"] = buildMethods;

                var build = factoryType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Build");
                bag["build_found"] = build != null;
                if (build == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                // Find a known weapon to build a recipe for
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getServiceMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getServiceMethod?.MakeGenericMethod(providerType).Invoke(services, null);

                var itemTemplate = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                var flagType = ResolveType("Awaken.TG.Main.Templates.TemplateTypeFlag");
                var flagRegular = Enum.Parse(flagType, "Regular");
                var getAllOfType = providerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                var allItems = getAllOfType?.MakeGenericMethod(itemTemplate).Invoke(provider, new object[] { flagRegular }) as System.Collections.IEnumerable;

                object weapon = null;
                foreach (var it in allItems ?? new object[0])
                {
                    var name = it.GetType().GetProperty("ItemName")?.GetValue(it)?.ToString();
                    if (string.IsNullOrEmpty(name) || name.Length < 4) continue;
                    var tagsObj = it.GetType().GetProperty("Tags")?.GetValue(it) as System.Collections.IEnumerable;
                    var tags = new List<string>();
                    if (tagsObj != null) foreach (var t in tagsObj) tags.Add(t?.ToString());
                    if (tags.Contains("item:weapon")) { weapon = it; break; }
                }
                bag["weapon_name"] = weapon?.GetType().GetProperty("ItemName")?.GetValue(weapon)?.ToString();

                // Build's signature: Build(Station, ItemTemplate, IList<(ItemTemplate, int)>, int)
                // We need to construct an empty IList<ValueTuple<ItemTemplate, int>>.
                var tupleType = typeof(ValueTuple<,>).MakeGenericType(itemTemplate, typeof(int));
                var listType = typeof(List<>).MakeGenericType(tupleType);
                var emptyList = Activator.CreateInstance(listType);

                var stationForge = Enum.Parse(stationType, "Forge");

                // Try to build 3 recipes — capture exception text per attempt
                var attempts = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        var result = build.Invoke(null, new object[] { stationForge, weapon, emptyList, 0 });
                        attempts.Add($"#{i}: ok = {result != null}, type={result?.GetType().Name}");
                    }
                    catch (Exception e)
                    {
                        var inner = e.GetBaseException();
                        attempts.Add($"#{i}: EXC {inner.GetType().Name}: {inner.Message}\n  stack first 200: {inner.StackTrace?.Substring(0, Math.Min(400, inner.StackTrace.Length))}");
                    }
                }
                bag["attempts"] = attempts;

                // After invocation, peek at static counters
                var bsField = factoryType.GetField("BuildSuccess", BindingFlags.Public | BindingFlags.Static);
                var bnField = factoryType.GetField("BuildFailNoRef", BindingFlags.Public | BindingFlags.Static);
                var beField = factoryType.GetField("BuildFailException", BindingFlags.Public | BindingFlags.Static);
                bag["BuildSuccess_after"] = bsField?.GetValue(null);
                bag["BuildFailNoRef_after"] = bnField?.GetValue(null);
                bag["BuildFailException_after"] = beField?.GetValue(null);
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
    }
}
