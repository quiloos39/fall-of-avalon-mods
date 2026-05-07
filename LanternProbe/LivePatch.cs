using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;

namespace LanternProbe
{
    // Apply the KnownRecipe Harmony patch live, without restarting. We target the inherited
    // method on each concrete derived class (Handcrafting, Alchemy, etc.) so the open-generic
    // resolution issue is bypassed — we work with closed-generic instantiations.
    //
    // Postfix: returns true for any recipe whose UnityObject.name starts with "CraftAnything_".
    // That's the same identifier CraftAnything's RecipeFactory tags its recipes with.
    public static class LivePatch
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var harmony = new Harmony("livepatch.craftanything.knownrecipe");
                var postfix = typeof(LivePatch).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.Public);
                bag["postfix_found"] = postfix != null;

                // The trick: walk up the inheritance chain on a CONCRETE derived class
                // (e.g. Handcrafting). At each step BaseType returns the closed-generic
                // RecipeCrafting<HandcraftingTemplate>, NOT the open generic RecipeCrafting<T>.
                // GetMethod on that closed type returns a closed-generic MethodInfo, which
                // Harmony CAN patch.
                var concreteCraftingTypes = new[]
                {
                    "Awaken.TG.Main.Crafting.HandCrafting.Handcrafting",
                    "Awaken.TG.Main.Crafting.AlchemyCrafting.Alchemy",
                    "Awaken.TG.Main.Crafting.Cooking.RecipeCooking",
                };

                var patched = new List<string>();
                var failed = new List<string>();
                foreach (var name in concreteCraftingTypes)
                {
                    var t = AccessTools.TypeByName(name);
                    if (t == null) { failed.Add($"{name}: type not found"); continue; }

                    // Find the BaseType chain that's closed-generic. The closed RecipeCrafting<T>
                    // base of e.g. Handcrafting is RecipeCrafting<HandcraftingTemplate>. Walking
                    // up base types stays in closed-generic territory.
                    Type closedBase = t.BaseType;
                    MethodInfo target = null;
                    while (closedBase != null)
                    {
                        // GetMethod (without DeclaredOnly) walks up — but on a closed type
                        // returns the MethodInfo bound to that closed type.
                        target = closedBase.GetMethod("KnownRecipe",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (target != null && target.DeclaringType.IsGenericType
                            && !target.DeclaringType.IsGenericTypeDefinition) break;
                        closedBase = closedBase.BaseType;
                    }

                    if (target == null) { failed.Add($"{name}: KnownRecipe method not found"); continue; }

                    try
                    {
                        harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                        patched.Add($"{name}: patched on {target.DeclaringType.FullName}");
                    }
                    catch (Exception e)
                    {
                        failed.Add($"{name}: patch failed: {e.GetBaseException().Message} | declType={target.DeclaringType.FullName} isGenDef={target.DeclaringType.IsGenericTypeDefinition}");
                    }
                }

                bag["patched"] = patched;
                bag["failed"] = failed;

                // Sanity check — call the patched method directly on a real Handcrafting instance
                // if one exists in the world. (Probably not unless forge is open.)
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        // The postfix: any recipe whose name starts with "CraftAnything_" → known.
        // We use name-prefix detection instead of a HashSet because the probe DLL doesn't
        // share static state with CraftAnything — but the Unity Object name persists.
        public static void Postfix(object recipe, ref bool __result)
        {
            if (__result) return;
            try
            {
                var name = (recipe as UnityEngine.Object)?.name;
                if (!string.IsNullOrEmpty(name) && name.StartsWith("CraftAnything_"))
                    __result = true;
            }
            catch { }
        }
    }
}
