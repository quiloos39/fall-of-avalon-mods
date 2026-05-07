using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // Simulates CraftAnything's recipe Build() pipeline directly via reflection so we can
    // validate the AddComponent-per-GameObject fix without restarting the game.
    //
    // 1) Confirm whether [DisallowMultipleComponent] is the actual cause: try AddComponent
    //    twice on the same GO and inspect the exception.
    // 2) Confirm that one-GO-per-component succeeds: try AddComponent on 10 fresh GOs.
    // 3) For each, verify TemplateReference(ITemplate) constructor works and the recipe's
    //    Outcome resolves back to the original ItemTemplate.
    public static class RecipeBuildSim
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var hcType = ResolveType("Awaken.TG.Main.Crafting.HandCrafting.HandcraftingRecipe");
                var templateRefType = ResolveType("Awaken.TG.Main.Templates.TemplateReference");
                var iTemplateType = ResolveType("Awaken.TG.Main.Templates.ITemplate");

                // 1) Check for DisallowMultipleComponent attribute on HandcraftingRecipe (and parents)
                var damc = hcType.GetCustomAttributes(true)
                    .Concat(hcType.BaseType?.GetCustomAttributes(true) ?? new object[0])
                    .Concat(hcType.BaseType?.BaseType?.GetCustomAttributes(true) ?? new object[0])
                    .Select(a => a.GetType().FullName).ToList();
                bag["attributes_walked"] = damc;
                bag["has_DisallowMultipleComponent"] = damc.Any(a => a.Contains("DisallowMultipleComponent"));

                // 2) Try AddComponent twice on same GO — see whether it actually throws
                var sameGo = new GameObject("ca_test_same") { hideFlags = HideFlags.HideAndDontSave };
                sameGo.SetActive(false);
                Component first = null, second = null;
                string firstErr = null, secondErr = null;
                try { first = sameGo.AddComponent(hcType); } catch (Exception e) { firstErr = e.GetBaseException().Message; }
                try { second = sameGo.AddComponent(hcType); } catch (Exception e) { secondErr = e.GetBaseException().Message; }
                bag["sameGo_first"] = first != null ? "ok" : ("err: " + firstErr);
                bag["sameGo_second"] = second != null ? "ok" : ("err: " + secondErr);
                UnityEngine.Object.Destroy(sameGo);

                // 3) Try AddComponent on 10 separate GOs (per-recipe pattern)
                int separateOk = 0, separateFail = 0;
                string firstSeparateErr = null;
                var hostParent = new GameObject("ca_test_parent") { hideFlags = HideFlags.HideAndDontSave };
                hostParent.SetActive(false);
                for (int i = 0; i < 10; i++)
                {
                    var go = new GameObject("ca_test_child_" + i) { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetParent(hostParent.transform, false);
                    go.SetActive(false);
                    try
                    {
                        var c = go.AddComponent(hcType);
                        if (c != null) separateOk++;
                        else separateFail++;
                    }
                    catch (Exception e)
                    {
                        separateFail++;
                        if (firstSeparateErr == null) firstSeparateErr = e.GetBaseException().Message;
                    }
                }
                bag["separateGo_ok"] = separateOk;
                bag["separateGo_fail"] = separateFail;
                bag["separateGo_firstErr"] = firstSeparateErr;

                // 4) End-to-end: build a real recipe + verify outcome resolves
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var services = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);

                var itemTemplate = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                var flagType = ResolveType("Awaken.TG.Main.Templates.TemplateTypeFlag");
                var flagRegular = Enum.Parse(flagType, "Regular");
                var getAllOfType = providerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                var allItems = getAllOfType?.MakeGenericMethod(itemTemplate).Invoke(provider, new object[] { flagRegular }) as System.Collections.IEnumerable;

                // Find a known weapon to use as test outcome
                object testWeapon = null;
                foreach (var it in allItems ?? new object[0])
                {
                    var tagsObj = it.GetType().GetProperty("Tags")?.GetValue(it) as System.Collections.IEnumerable;
                    var tags = new List<string>();
                    if (tagsObj != null) foreach (var t in tagsObj) tags.Add(t?.ToString());
                    if (tags.Contains("item:weapon") && tags.Any(x => x?.StartsWith("weapons:") == true))
                    {
                        var name = it.GetType().GetProperty("ItemName")?.GetValue(it)?.ToString();
                        if (!string.IsNullOrEmpty(name) && name.Length > 3)
                        {
                            testWeapon = it; break;
                        }
                    }
                }
                bag["testWeapon_found"] = testWeapon != null;
                if (testWeapon != null)
                {
                    bag["testWeapon_name"] = testWeapon.GetType().GetProperty("ItemName")?.GetValue(testWeapon)?.ToString();
                    bag["testWeapon_guid"] = testWeapon.GetType().GetProperty("GUID")?.GetValue(testWeapon)?.ToString();

                    // Try TemplateReference(ITemplate) constructor
                    object refInstance = null;
                    string refErr = null;
                    try
                    {
                        var ctor = templateRefType.GetConstructor(new Type[] { iTemplateType });
                        refInstance = ctor.Invoke(new object[] { testWeapon });
                    }
                    catch (Exception e) { refErr = e.GetBaseException().Message; }
                    bag["templateRef_ctor_ITemplate"] = refInstance != null ? "ok" : ("err: " + refErr);
                    if (refInstance != null)
                    {
                        bag["templateRef_GUID"] = refInstance.GetType().GetProperty("GUID")?.GetValue(refInstance)?.ToString();
                        bag["templateRef_IsSet"] = refInstance.GetType().GetProperty("IsSet")?.GetValue(refInstance)?.ToString();
                    }

                    // Build a full recipe via the same flow CraftAnything would use
                    int builtCount = 0;
                    string buildErr = null;
                    try
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            var go = new GameObject("ca_real_" + i) { hideFlags = HideFlags.HideAndDontSave };
                            go.transform.SetParent(hostParent.transform, false);
                            go.SetActive(false);
                            var recipe = go.AddComponent(hcType);
                            if (recipe == null) { buildErr = "AddComponent returned null on iter " + i; break; }
                            // Set outcome via reflection on `outcome` field
                            var baseRecipe = ResolveType("Awaken.TG.Main.Crafting.Recipes.BaseRecipe");
                            var outcomeF = baseRecipe.GetField("outcome", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                            outcomeF?.SetValue(recipe, refInstance);
                            // Verify Outcome resolves
                            var outcomeProp = baseRecipe.GetProperty("Outcome");
                            var resolved = outcomeProp?.GetValue(recipe);
                            if (resolved != null) builtCount++;
                            else { buildErr = "Outcome did not resolve on iter " + i; break; }
                        }
                    }
                    catch (Exception e) { buildErr = e.GetBaseException().Message; }
                    bag["full_build_ok_count"] = builtCount;
                    bag["full_build_err"] = buildErr;
                }

                UnityEngine.Object.Destroy(hostParent);
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
