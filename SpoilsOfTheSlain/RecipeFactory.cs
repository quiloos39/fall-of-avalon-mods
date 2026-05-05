using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Awaken.TG.Main.Crafting.Recipes;
using Awaken.TG.Main.Crafting.HandCrafting;
using Awaken.TG.Main.Crafting.AlchemyCrafting;
using Awaken.TG.Main.Crafting.Cooking;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Templates;

namespace SpoilsOfTheSlain
{
    // Builds runtime BaseRecipe instances. Recipes are Unity ScriptableObjects, so we go
    // through ScriptableObject.CreateInstance, then poke fields directly. The ingredient
    // and outcome fields use TemplateReference (not raw ItemTemplate), so we have to set
    // those up correctly — TemplateReference holds a GUID and resolves on demand.
    internal static class RecipeFactory
    {
        // Cache reflected field handles on first use.
        private static FieldInfo _outcomeF;
        private static FieldInfo _ingredientsF;
        private static FieldInfo _quantityF;
        private static FieldInfo _isHiddenF;
        private static FieldInfo _itemCraftingDifficultyF;
        private static FieldInfo _statRequirementF;
        private static FieldInfo _statRequirementValueF;
        private static FieldInfo _disableItemLevelsF;
        private static FieldInfo _storyOnCreationF;

        private static FieldInfo _ingredientTemplateRefF;
        private static FieldInfo _ingredientCountF;

        private static FieldInfo _templateRefGuidF;
        private static PropertyInfo _templateMetadataP;
        private static FieldInfo _templateGuidF;

        // Reflection cache for setting GUID on the runtime recipe so the game can identify it.
        private static FieldInfo _recipeGuidBackingF;

        private static bool _initted;
        private static void Init()
        {
            if (_initted) return;

            var baseRecipe = typeof(BaseRecipe);
            _outcomeF = baseRecipe.GetField("outcome", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            _ingredientsF = baseRecipe.GetField("ingredients", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            _quantityF = baseRecipe.GetField("quantity", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            _isHiddenF = baseRecipe.GetField("isHidden", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            _itemCraftingDifficultyF = baseRecipe.GetField("itemCraftingDifficulty", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            _statRequirementF = baseRecipe.GetField("statRequirement", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            _statRequirementValueF = baseRecipe.GetField("statRequirementValue", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            _disableItemLevelsF = baseRecipe.GetField("disableItemLevels", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            _storyOnCreationF = baseRecipe.GetField("storyOnCreation", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            var ingredient = typeof(Ingredient);
            _ingredientTemplateRefF = ingredient.GetField("templateReference", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            _ingredientCountF = ingredient.GetField("count", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            var templateRef = typeof(TemplateReference);
            // TemplateReference's GUID storage — it's typically just a string field. Try common names.
            _templateRefGuidF = templateRef.GetField("_guid", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?? templateRef.GetField("guid", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?? templateRef.GetField("GUID", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            // The game's templates expose Metadata which typically has a GUID. Item templates
            // also have a direct GUID property/field. Try multiple resolution paths.
            _templateMetadataP = typeof(ItemTemplate).GetProperty("Metadata", BindingFlags.Public | BindingFlags.Instance);
            _templateGuidF = typeof(ItemTemplate).GetField("<GUID>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

            // BaseRecipe inherits from Template which has its own GUID — find the backing field.
            _recipeGuidBackingF = typeof(Template).GetField("<GUID>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                              ?? typeof(Template).GetField("guid", BindingFlags.NonPublic | BindingFlags.Instance);

            _initted = true;
        }

        // Resolve an ItemTemplate's GUID. Reflects on the runtime instance via the public
        // GUID property — works regardless of how the property is implemented (auto vs manual).
        // ItemTemplate implements ITemplate so the property exists.
        public static string GetGuid(object template)
        {
            if (template == null) return null;
            try
            {
                var guidProp = template.GetType().GetProperty("GUID", BindingFlags.Public | BindingFlags.Instance);
                if (guidProp != null) return guidProp.GetValue(template) as string;
            }
            catch { }
            return null;
        }

        // Build a TemplateReference for a given template. Use the (ITemplate) constructor —
        // it derives the GUID itself, so we don't have to fish for backing fields.
        private static TemplateReference MakeRef(ItemTemplate template)
        {
            if (template == null) return null;
            try
            {
                return new TemplateReference(template);
            }
            catch (Exception e)
            {
                // Fallback: if (ITemplate) constructor doesn't work for this build, use (string guid).
                try
                {
                    var guid = GetGuid(template);
                    if (string.IsNullOrEmpty(guid)) return null;
                    return new TemplateReference(guid);
                }
                catch (Exception e2)
                {
                    Plugin.Log.LogWarning($"[RecipeFactory] MakeRef failed for {SafeName(template)}: {e.GetBaseException().Message} (fallback also: {e2.GetBaseException().Message})");
                    return null;
                }
            }
        }

        private static string SafeName(ItemTemplate t)
        {
            try { return t?.ItemName ?? t?.name; } catch { return "?"; }
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }

        public enum Station { Forge, Alchemy, Cooking }

        // Recipes are MonoBehaviours (Template : MonoBehaviour) → AddComponent. Most recipe
        // classes are decorated [DisallowMultipleComponent], so we can't pack many onto one
        // GameObject — each recipe needs its own. We park them as children of a single
        // hidden parent for tidy DontDestroyOnLoad management.
        private static GameObject _hostParent;
        private static GameObject GetHostParent()
        {
            if (_hostParent != null) return _hostParent;
            _hostParent = new GameObject("SpoilsOfTheSlain_Recipes") { hideFlags = HideFlags.HideAndDontSave };
            _hostParent.SetActive(false);     // Behaviours on inactive objects don't run Update
            UnityEngine.Object.DontDestroyOnLoad(_hostParent);
            return _hostParent;
        }

        private static GameObject MakeChildHost(string label)
        {
            var parent = GetHostParent();
            var go = new GameObject(label) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(parent.transform, false);
            go.SetActive(false);
            return go;
        }

        // Counters surfaced in logs so we know how many recipes succeed vs. silently fail.
        public static int BuildSuccess;
        public static int BuildFailNoRef;
        public static int BuildFailException;

        public static IRecipe Build(Station station, ItemTemplate outcome, IList<(ItemTemplate template, int count)> ingredients, int statReqValue = 0)
        {
            Init();
            if (outcome == null) { BuildFailNoRef++; return null; }
            var outcomeRef = MakeRef(outcome);
            if (outcomeRef == null) { BuildFailNoRef++; return null; }

            try
            {
                var host = MakeChildHost("ca_" + SafeName(outcome));
                BaseRecipe recipe;
                switch (station)
                {
                    case Station.Alchemy: recipe = host.AddComponent<AlchemyRecipe>(); break;
                    case Station.Cooking: recipe = host.AddComponent<CookingRecipe>(); break;
                    default:              recipe = host.AddComponent<HandcraftingRecipe>(); break;
                }
                if (recipe == null) { BuildFailException++; return null; }
                return BuildBody(recipe, station, outcome, outcomeRef, ingredients, statReqValue);
            }
            catch (Exception e)
            {
                BuildFailException++;
                if (Plugin.Cfg.Verbose.Value)
                    Plugin.Log.LogWarning($"[RecipeFactory] Build failed for {SafeName(outcome)}: {e.GetBaseException().Message}");
                return null;
            }
        }

        private static IRecipe BuildBody(BaseRecipe recipe, Station station, ItemTemplate outcome, TemplateReference outcomeRef, IList<(ItemTemplate template, int count)> ingredients, int statReqValue)
        {

            // Tag the recipe so we can identify our own injections later (e.g. for debugging
            // or de-duping). The "ca:" prefix marks it as ours.
            string ourGuid = "ca_" + (GetGuid(outcome) ?? Guid.NewGuid().ToString("N"));
            try { _recipeGuidBackingF?.SetValue(recipe, ourGuid); } catch { }

            recipe.name = "SpoilsOfTheSlain_" + (outcome.ItemName ?? outcome.name ?? "?");

            try { _outcomeF?.SetValue(recipe, outcomeRef); } catch { }
            try { _quantityF?.SetValue(recipe, 1); } catch { }
            try { _isHiddenF?.SetValue(recipe, false); } catch { }
            try { _itemCraftingDifficultyF?.SetValue(recipe, 0f); } catch { }
            try { _disableItemLevelsF?.SetValue(recipe, false); } catch { }
            try { _statRequirementValueF?.SetValue(recipe, statReqValue); } catch { }

            // statRequirement must be a non-null RichEnumReference — vanilla recipes have
            // an empty (None) reference, never null. Without it, the StatRequirement getter
            // throws null ref and the UI fails to render the recipe.
            try
            {
                var richEnumRefType = ResolveType("Awaken.TG.Main.Utility.RichEnums.RichEnumReference");
                if (richEnumRefType != null && _statRequirementF != null)
                {
                    var emptyRef = Activator.CreateInstance(richEnumRefType);
                    _statRequirementF.SetValue(recipe, emptyRef);
                }
            }
            catch { }

            // storyOnCreation also expects a non-null TemplateReference — empty is fine.
            try
            {
                if (_storyOnCreationF != null)
                {
                    var emptyRef = (TemplateReference)Activator.CreateInstance(typeof(TemplateReference));
                    _storyOnCreationF.SetValue(recipe, emptyRef);
                }
            }
            catch { }

            // Build the Ingredient[] — every entry is itself a TemplateReference + count pair.
            var ings = new List<Ingredient>();
            foreach (var (t, c) in ingredients ?? new List<(ItemTemplate, int)>())
            {
                if (t == null || c <= 0) continue;
                var ing = new Ingredient();
                var refTo = MakeRef(t);
                if (refTo == null) continue;
                try { _ingredientTemplateRefF?.SetValue(ing, refTo); } catch { }
                try { _ingredientCountF?.SetValue(ing, c); } catch { }
                ings.Add(ing);
            }
            try { _ingredientsF?.SetValue(recipe, ings.ToArray()); } catch { }

            BuildSuccess++;
            return recipe;
        }
    }
}
