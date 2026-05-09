using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.ResourceManagement.AsyncOperations;

using Awaken.TG.Assets;
using Awaken.TG.MVC;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Templates;
using Awaken.TG.Main.UI.HUD.AdvancedNotifications;
using Awaken.TG.Main.UI.HUD.AdvancedNotifications.MiddleScreen;
using Awaken.TG.Main.UI.HUD.AdvancedNotifications.MiddleScreen.FancyPanel;

namespace Lantern
{
    internal class LanternController : MonoBehaviour
    {
        public bool Active { get; private set; }

        // The empty parent we own — sits in world-space, position is driven manually each frame.
        private GameObject _root;
        // The world-prefab instance (the visible lantern model).
        private GameObject _modelInstance;
        // The light components.
        private Light _light;
        private HDAdditionalLightData _hdData;

        private float _baseIntensity;
        private Transform _attachedTo;
        private string _matchedTemplateName;

        // Prefab caching — async-loaded once per template, reused across toggles.
        private GameObject _cachedPrefab;
        private string _cachedPrefabKey;
        private bool _prefabLoadInFlight;
        private ARAssetReference _heldAssetReference;

        private void LateUpdate()
        {
            // LateUpdate runs after animation has posed the rig — best place to track bones.
            if (!Active) return;

            var hero = Hero.Current;
            if (hero == null) return;

            var attach = ResolveAttachTransform(hero);
            if (attach == null) return;

            if (_root == null || _attachedTo != attach)
            {
                Spawn(attach);
            }

            // Manually drive world-space transform from the attach bone, in case the prefab
            // has internal scripts (SceneSpec, etc.) that fight Unity parenting.
            FollowAttach(attach);
            ApplyOffsetAndRotation(attach);
            UpdateConfigBoundProperties();
            UpdateFlicker();
        }

        public void Toggle()
        {
            Active = !Active;
            VLog($"Toggle → {Active}");

            if (Active)
            {
                var hero = Hero.Current;
                var attach = hero != null ? ResolveAttachTransform(hero) : null;
                if (attach != null) Spawn(attach);
                Plugin.Log.LogInfo("[Lantern] ON");
            }
            else
            {
                Despawn();
                Plugin.Log.LogInfo("[Lantern] OFF");
            }

            if (Plugin.Cfg.ShowToggleNotification.Value)
            {
                ShowToggleNotification(Active);
            }
        }

        private Transform ResolveAttachTransform(Hero hero)
        {
            switch (Plugin.Cfg.Attach.Value)
            {
                case AttachPoint.Hips:     return hero.Hips;
                case AttachPoint.Torso:    return hero.Torso;
                case AttachPoint.Head:     return hero.Head;
                case AttachPoint.MainHand: return hero.MainHand;
                case AttachPoint.OffHand:  return hero.OffHand;
                default:                   return hero.Torso;
            }
        }

        private void Spawn(Transform attach)
        {
            Despawn();

            try
            {
                _root = new GameObject("Lantern_Plugin");
                _attachedTo = attach;
                FollowAttach(attach);

                _light = _root.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.color = ReadColor();
                _light.range = Plugin.Cfg.Range.Value;
                _light.shadows = Plugin.Cfg.CastShadows.Value ? LightShadows.Soft : LightShadows.None;
                _light.renderingLayerMask = unchecked((int)0xFFFFFFFFu);   // affect every rendering layer
                VLog("Light component created");

                _hdData = _root.AddComponent<HDAdditionalLightData>();
                // Profile copied from TG:FoA's vanilla equipped torch (PointLight_Fire under
                // Equipable_Weapon_1H_Club_T0_Torch). Diff vs naive setup:
                //   • lightUnit = Ev100  (Candela makes "Intensity 800" mean ~800 cd, way too dim)
                //   • shapeRadius > 0    (sphere-area light, soft glow instead of harsh point)
                //   • lightlayersMask = ~0u  (RenderingLayer1 alone misses some world geometry)
                //   • affectsVolumetric off (game torch keeps volumetric off — avoids fog noise)
                _hdData.range = Plugin.Cfg.Range.Value;
                _hdData.SetColor(ReadColor());
                ApplyLayerMaskAll(_hdData);
                _hdData.shapeRadius = Plugin.Cfg.ShapeRadius.Value;
                _hdData.affectsVolumetric = Plugin.Cfg.AffectsVolumetric.Value;
                _hdData.volumetricDimmer = Plugin.Cfg.AffectsVolumetric.Value ? 1f : 0f;
                _hdData.shadowDimmer = 0.8f;
                _hdData.fadeDistance = 256f;
                _hdData.shadowFadeDistance = 128f;
                _hdData.applyRangeAttenuation = true;
                _hdData.EnableShadows(Plugin.Cfg.CastShadows.Value);
                // SetIntensity(value, Ev100) — value here is the EV100 stops, NOT a candela count.
                // Game torch uses ~22 EV; range 0–30 covers candle (10) → daylight (24).
                SetIntensityEv100(_hdData, Plugin.Cfg.Intensity.Value);
                VLog($"HDAdditionalLightData created (EV={Plugin.Cfg.Intensity.Value}, range={Plugin.Cfg.Range.Value}, shapeRadius={Plugin.Cfg.ShapeRadius.Value})");

                _baseIntensity = Plugin.Cfg.Intensity.Value;

                EnsureModelLoadedAndAttached();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Lantern] Spawn failed: {e}");
                Despawn();
            }
        }

        private void Despawn()
        {
            if (_modelInstance != null)
            {
                Destroy(_modelInstance);
                _modelInstance = null;
            }
            if (_root != null)
            {
                Destroy(_root);
                _root = null;
            }
            _light = null;
            _hdData = null;
            _attachedTo = null;
        }

        // Drive _root's world position+rotation from the attach bone every frame.
        // We use this instead of Unity parenting because some prefabs override their position
        // via game scripts (SceneSpec, IWithUnityRepresentation), which causes the visual to
        // "float" detached from the character.
        private void FollowAttach(Transform attach)
        {
            if (_root == null || attach == null) return;
            _root.transform.position = attach.position;
            _root.transform.rotation = attach.rotation;
        }

        // Translate offset (in attach bone's local axes) into world delta, then add rotation.
        private void ApplyOffsetAndRotation(Transform attach)
        {
            if (_root == null) return;

            Vector3 localOffset = new Vector3(
                Plugin.Cfg.OffsetRight.Value,
                Plugin.Cfg.OffsetUp.Value,
                Plugin.Cfg.OffsetForward.Value);

            // Add to world position using the attach bone's local axes so that "up = up relative
            // to the bone" no matter how the bone is oriented in world space.
            _root.transform.position = attach.position
                + attach.right   * localOffset.x
                + attach.up      * localOffset.y
                + attach.forward * localOffset.z;

            _root.transform.rotation = attach.rotation * Quaternion.Euler(
                Plugin.Cfg.RotationX.Value,
                Plugin.Cfg.RotationY.Value,
                Plugin.Cfg.RotationZ.Value);

            if (_modelInstance != null)
            {
                _modelInstance.transform.localPosition = Vector3.zero;
                _modelInstance.transform.localRotation = Quaternion.identity;
                float s = Plugin.Cfg.Scale.Value;
                _modelInstance.transform.localScale = new Vector3(s, s, s);
            }
        }

        private void EnsureModelLoadedAndAttached()
        {
            string keys = Plugin.Cfg.ItemNameMatch.Value;
            if (string.IsNullOrWhiteSpace(keys))
            {
                VLog("ItemNameMatch is empty — light only, no model.");
                return;
            }

            if (_cachedPrefab != null && _cachedPrefabKey == keys)
            {
                VLog("Using cached prefab.");
                AttachCachedModel();
                return;
            }

            if (_prefabLoadInFlight) return;
            _prefabLoadInFlight = true;

            try
            {
                var provider = World.Services?.Get<TemplatesProvider>();
                if (provider == null)
                {
                    Plugin.Log.LogWarning("[Lantern] TemplatesProvider not available yet — model will attach later when world is ready.");
                    _prefabLoadInFlight = false;
                    return;
                }

                ItemTemplate template = FindBestTemplate(provider, keys);
                if (template == null)
                {
                    Plugin.Log.LogWarning($"[Lantern] No ItemTemplate matched any of [{keys}] (after blocklist filter). Light only, no model.");
                    _prefabLoadInFlight = false;
                    return;
                }

                _matchedTemplateName = template.ItemName;
                Plugin.Log.LogInfo($"[Lantern] Using ItemTemplate '{template.ItemName}' as accessory model.");
                _heldAssetReference = template.PickablePrefab.Get();
                if (_heldAssetReference == null || !_heldAssetReference.IsSet)
                {
                    Plugin.Log.LogWarning($"[Lantern] '{template.ItemName}' PickablePrefab.Get() returned no asset.");
                    _prefabLoadInFlight = false;
                    return;
                }

                var handle = _heldAssetReference.LoadAsset<GameObject>();
                handle.OnComplete(OnPrefabLoaded);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Lantern] EnsureModelLoadedAndAttached failed: {e}");
                _prefabLoadInFlight = false;
            }
        }

        private static ItemTemplate FindBestTemplate(TemplatesProvider provider, string keys)
        {
            string[] needles = keys.Split(',')
                .Select(s => s.Trim().ToLowerInvariant())
                .Where(s => s.Length > 0)
                .ToArray();

            string[] blocked = (Plugin.Cfg.ItemNameBlocklist.Value ?? "")
                .Split(',')
                .Select(s => s.Trim().ToLowerInvariant())
                .Where(s => s.Length > 0)
                .ToArray();

            var allItems = provider.GetAllOfType<ItemTemplate>().ToList();

            if (Plugin.Cfg.Verbose.Value)
            {
                var allMatches = allItems
                    .Where(t => t != null && !string.IsNullOrEmpty(t.ItemName))
                    .Where(t => needles.Any(n => t.ItemName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(t => $"'{t.ItemName}' (prefab={(t.PickablePrefab?.IsSet == true ? "yes" : "NO")})")
                    .ToList();
                Plugin.Log.LogInfo($"[Lantern] Candidate templates matching {{{keys}}}: {(allMatches.Count == 0 ? "(none)" : string.Join(", ", allMatches))}");
            }

            // Try each needle in order, pick the first non-blocked template with a valid prefab.
            foreach (var needle in needles)
            {
                foreach (var t in allItems)
                {
                    if (t == null || string.IsNullOrEmpty(t.ItemName)) continue;
                    if (t.PickablePrefab == null || !t.PickablePrefab.IsSet) continue;

                    string lname = t.ItemName.ToLowerInvariant();
                    if (lname.IndexOf(needle, StringComparison.Ordinal) < 0) continue;
                    if (blocked.Any(b => lname.IndexOf(b, StringComparison.Ordinal) >= 0)) continue;

                    return t;
                }
            }
            return null;
        }

        private void OnPrefabLoaded(ARAsyncOperationHandle<GameObject> handle)
        {
            _prefabLoadInFlight = false;

            try
            {
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                {
                    Plugin.Log.LogWarning($"[Lantern] Prefab failed to load (status={handle.Status}). Light only, no model.");
                    return;
                }

                _cachedPrefab = handle.Result;
                _cachedPrefabKey = Plugin.Cfg.ItemNameMatch.Value;
                VLog($"Prefab loaded: '{_matchedTemplateName}' (children: {handle.Result.transform.childCount})");

                if (Active && _root != null) AttachCachedModel();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Lantern] OnPrefabLoaded failed: {e}");
            }
        }

        private void AttachCachedModel()
        {
            if (_cachedPrefab == null || _root == null) return;

            try
            {
                if (_modelInstance != null) Destroy(_modelInstance);

                _modelInstance = UnityEngine.Object.Instantiate(_cachedPrefab, _root.transform);
                _modelInstance.name = "Lantern_PluginModel";
                _modelInstance.transform.localPosition = Vector3.zero;
                _modelInstance.transform.localRotation = Quaternion.identity;

                StripGameLogic(_modelInstance);

                float s = Plugin.Cfg.Scale.Value;
                _modelInstance.transform.localScale = new Vector3(s, s, s);

                if (Plugin.Cfg.Verbose.Value)
                {
                    var bounds = ComputeBounds(_modelInstance);
                    Plugin.Log.LogInfo($"[Lantern] Model instantiated. Local bounds size={bounds.size:F3}, center={bounds.center:F3}, scale={s}");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Lantern] AttachCachedModel failed: {e}");
            }
        }

        // Disable colliders, freeze rigidbodies, and turn off any MonoBehaviour from the game's
        // own pickable / interaction code that would otherwise try to pin the model in world
        // space or generate UI prompts.
        private static void StripGameLogic(GameObject root)
        {
            foreach (var col in root.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                col.enabled = false;
            }
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            // Disable any MonoBehaviour whose type name suggests it touches pickup / interaction
            // / scene-spec systems. Keep mesh renderers and other visual scripts.
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (mb == null) continue;
                var typeName = mb.GetType().Name;
                if (typeName.Contains("Pickable") ||
                    typeName.Contains("Pickup") ||
                    typeName.Contains("Interactable") ||
                    typeName.Contains("SceneSpec") ||
                    typeName.Contains("Interaction"))
                {
                    mb.enabled = false;
                }
            }
        }

        private static Bounds ComputeBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        private void UpdateConfigBoundProperties()
        {
            if (_light == null || _hdData == null) return;

            var color = ReadColor();
            _light.color = color;
            _hdData.SetColor(color);

            float intensity = Plugin.Cfg.Intensity.Value;
            if (!Mathf.Approximately(_baseIntensity, intensity))
            {
                _baseIntensity = intensity;
                SetIntensityEv100(_hdData, intensity);
            }

            float range = Plugin.Cfg.Range.Value;
            _light.range = range;
            _hdData.range = range;

            // Shape / volumetric drift fixes for live config tweaking.
            _hdData.shapeRadius = Plugin.Cfg.ShapeRadius.Value;
            _hdData.affectsVolumetric = Plugin.Cfg.AffectsVolumetric.Value;
            _hdData.volumetricDimmer = Plugin.Cfg.AffectsVolumetric.Value ? 1f : 0f;

            bool shadows = Plugin.Cfg.CastShadows.Value;
            _light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            _hdData.EnableShadows(shadows);
        }

        private void UpdateFlicker()
        {
            if (_hdData == null || !Plugin.Cfg.Flicker.Value) return;

            float amount = Plugin.Cfg.FlickerAmount.Value;
            float t = Time.unscaledTime;
            // For Ev100 (logarithmic stops), additive noise is more intuitive than multiplicative.
            // ±amount EV swings produce a "flame" feel without the runaway explosion of cd math.
            float noise = Mathf.PerlinNoise(t * 5f, 0f) - 0.5f
                        + (Mathf.PerlinNoise(t * 13f, 100f) - 0.5f) * 0.5f;
            SetIntensityEv100(_hdData, _baseIntensity + noise * amount);
        }

        // SetIntensity(float, LightUnit) — done via reflection because TG ships an HDRP build
        // where the LightUnit enum isn't reachable from a netstandard2.1 mod assembly. We pin
        // it lazily on the first call and reuse.
        private static System.Reflection.MethodInfo s_setIntensityWithUnit;
        private static object s_ev100;
        private static void SetIntensityEv100(HDAdditionalLightData hd, float ev)
        {
            try
            {
                if (s_setIntensityWithUnit == null)
                {
                    var hdType = typeof(HDAdditionalLightData);
                    foreach (var m in hdType.GetMethods())
                    {
                        if (m.Name != "SetIntensity") continue;
                        var ps = m.GetParameters();
                        if (ps.Length == 2 && ps[0].ParameterType == typeof(float) && ps[1].ParameterType.IsEnum)
                        {
                            s_setIntensityWithUnit = m;
                            s_ev100 = System.Enum.Parse(ps[1].ParameterType, "Ev100", ignoreCase: true);
                            break;
                        }
                    }
                }
                if (s_setIntensityWithUnit != null)
                {
                    s_setIntensityWithUnit.Invoke(hd, new object[] { ev, s_ev100 });
                    return;
                }
                // Fallback: single-arg SetIntensity uses whatever the current lightUnit is.
                hd.SetIntensity(ev);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[Lantern] SetIntensityEv100: {e.Message}"); }
        }

        // HDRP `lightlayersMask` is enum-typed (RenderingLayerMask). The exact type may differ
        // across HDRP versions, so reflect on it and assign every-bits-set as the underlying
        // integer. This is what the game's torch effectively uses (RenderingLayer1 was enough
        // for the torch but +everything is safe and matches the user's "make it obvious" setup).
        private static void ApplyLayerMaskAll(HDAdditionalLightData hd)
        {
            try
            {
                var prop = typeof(HDAdditionalLightData).GetProperty("lightlayersMask");
                if (prop != null && prop.CanWrite)
                {
                    object val = prop.PropertyType.IsEnum
                        ? System.Enum.ToObject(prop.PropertyType, 0xFFFFFFFFu)
                        : (object)0xFFFFFFFFu;
                    prop.SetValue(hd, val);
                    return;
                }
                var field = typeof(HDAdditionalLightData).GetField("lightlayersMask",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    object val = field.FieldType.IsEnum
                        ? System.Enum.ToObject(field.FieldType, 0xFFFFFFFFu)
                        : (object)0xFFFFFFFFu;
                    field.SetValue(hd, val);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[Lantern] ApplyLayerMaskAll: {e.Message}"); }
        }

        private Color ReadColor()
        {
            return new Color(
                Plugin.Cfg.ColorR.Value,
                Plugin.Cfg.ColorG.Value,
                Plugin.Cfg.ColorB.Value,
                1f);
        }

        private void ShowToggleNotification(bool isOn)
        {
            string text = isOn ? "Lantern: ON" : "Lantern: OFF";
            try
            {
                NotificationUtils.PushExplicitly<LowerMiddleScreenNotificationBuffer, LowerInfoNotification>(
                    new LowerInfoNotification(text, typeof(VLowerInfoNotification)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Lantern] Failed to push notification ({e.GetType().Name}): {e.Message}");
            }
        }

        private static void VLog(string msg)
        {
            if (Plugin.Cfg.Verbose.Value) Plugin.Log.LogInfo($"[Lantern] {msg}");
        }

        private void OnDestroy()
        {
            Despawn();
            if (_heldAssetReference != null)
            {
                try { _heldAssetReference.ReleaseAsset(); } catch { /* nbd */ }
                _heldAssetReference = null;
            }
        }
    }
}
