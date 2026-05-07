using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    // Reverse the InjectKnownRecipes change. Walks every recipe in HeroRecipes.knownRecipes,
    // removes any whose UnityObject.name starts with "CraftAnything_". Vanilla recipes are
    // untouched (their names look like "Recipe_Handcrafting_*" instead).
    public static class UndoKnownRecipes
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var heroRecipesType = ResolveType("Awaken.TG.Main.Heroes.HeroRecipes");
                var elementMethod = hero?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Element" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var heroRecipes = elementMethod?.MakeGenericMethod(heroRecipesType).Invoke(hero, null);
                if (heroRecipes == null) { bag["err"] = "no HeroRecipes"; return JsonConvert.SerializeObject(bag, Formatting.Indented); }

                var knownF = heroRecipesType.GetField("knownRecipes", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                var hashSet = knownF?.GetValue(heroRecipes);
                if (hashSet == null) { bag["err"] = "no knownRecipes"; return JsonConvert.SerializeObject(bag, Formatting.Indented); }

                var hsType = hashSet.GetType();
                var countP = hsType.GetProperty("Count");
                var removeM = hsType.GetMethod("Remove", BindingFlags.Public | BindingFlags.Instance);

                int countBefore = (int)countP.GetValue(hashSet);

                // Snapshot the HashSet to a list so we can iterate-and-remove safely.
                var asEnum = hashSet as System.Collections.IEnumerable;
                var snapshot = new List<object>();
                foreach (var x in asEnum) snapshot.Add(x);

                int removed = 0;
                foreach (var recipe in snapshot)
                {
                    var name = (recipe as UnityEngine.Object)?.name ?? "";
                    if (!name.StartsWith("CraftAnything_")) continue;
                    try
                    {
                        var ok = removeM.Invoke(hashSet, new object[] { recipe });
                        if (ok is bool b && b) removed++;
                    }
                    catch { }
                }

                bag["count_before"] = countBefore;
                bag["removed"] = removed;
                bag["count_after"] = (int)countP.GetValue(hashSet);
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
