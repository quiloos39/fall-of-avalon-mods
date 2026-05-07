using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // Bypass the Harmony-on-generics issue entirely. Find every recipe under our
    // CraftAnything_Recipes host GameObject and add it directly to
    // HeroRecipes.knownRecipes — that's the HashSet KnownRecipe(recipe) checks.
    // Our generated recipes are reference-unique MonoBehaviours, so HashSet.Contains
    // works on default reference equality.
    //
    // Save concern: knownRecipes is serialized. Our recipes have synthetic GUIDs
    // ("ca_*") that won't resolve on load — the deserializer drops them silently,
    // so save integrity is preserved. Worst case: next session starts without the
    // injection until forge is opened again (which regenerates everything).
    public static class InjectKnownRecipes
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Find the host GameObject (uses HideFlags so Find() can't see it; use FindObjectsOfTypeAll)
                var hostGOs = UnityEngine.Resources.FindObjectsOfTypeAll<GameObject>();
                var host = hostGOs.FirstOrDefault(g => g.name == "CraftAnything_Recipes");
                bag["host_found"] = host != null;
                if (host == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                bag["host_childCount"] = host.transform.childCount;

                // Find HeroRecipes
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var heroRecipesType = ResolveType("Awaken.TG.Main.Heroes.HeroRecipes");
                var elementMethod = hero?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var heroRecipes = elementMethod?.MakeGenericMethod(heroRecipesType).Invoke(hero, null);
                bag["heroRecipes_found"] = heroRecipes != null;
                if (heroRecipes == null) return JsonConvert.SerializeObject(bag, Formatting.Indented);

                // List ALL fields on HeroRecipes for diagnosis
                bag["HeroRecipes_all_fields"] = heroRecipesType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Select(f => $"{f.FieldType.Name} {f.Name} ({(f.IsStatic ? "static" : "instance")})").ToList();

                // Get the knownRecipes HashSet via reflection
                var knownF = heroRecipesType.GetField("knownRecipes", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                bag["knownF_found"] = knownF != null;
                if (knownF == null)
                {
                    // Try inherited
                    knownF = heroRecipesType.GetField("knownRecipes", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    bag["knownF_found_via_flatten"] = knownF != null;
                }
                var knownRaw = knownF?.GetValue(heroRecipes);
                bag["knownRaw_null"] = knownRaw == null;
                bag["knownRaw_type"] = knownRaw?.GetType().FullName;

                // Use the HashSet directly via reflection (non-generic ICollection isn't impl'd)
                var hashSet = knownRaw;
                var hsType = hashSet.GetType();
                var addM = hsType.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
                var countP = hsType.GetProperty("Count");

                int countBefore = (int)countP.GetValue(hashSet);
                bag["knownRecipes_count_before"] = countBefore;

                int added = 0;
                int skipped = 0;
                var sampleAdded = new List<string>();
                for (int i = 0; i < host.transform.childCount; i++)
                {
                    var child = host.transform.GetChild(i);
                    var recipe = child.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name.EndsWith("Recipe"));
                    if (recipe == null) continue;
                    try
                    {
                        var result = addM.Invoke(hashSet, new object[] { recipe });
                        if (result is bool b && b) { added++; if (sampleAdded.Count < 5) sampleAdded.Add(child.name); }
                        else skipped++;
                    }
                    catch (Exception e)
                    {
                        if (!bag.ContainsKey("first_add_err"))
                            bag["first_add_err"] = e.GetBaseException().Message;
                        skipped++;
                    }
                }

                bag["added"] = added;
                bag["skipped"] = skipped;
                bag["sample_added"] = sampleAdded;
                bag["knownRecipes_count_after"] = (int)countP.GetValue(hashSet);
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
