using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LanternProbe
{
    public static class MimicTorch
    {
        // Find our Lantern_Plugin GameObject's Light + HDAdditionalLightData and reconfigure
        // them to match the game torch's settings as closely as possible. All via reflection
        // so we don't need a hard reference to HDRP.
        public static string ApplyTorchLightProfile()
        {
            try
            {
                // Make sure lantern is on first.
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");
                if (ctrl == null) return "no controller";

                var t = ctrl.GetType();
                var activeProp = t.GetProperty("Active");
                var toggle = t.GetMethod("Toggle", BindingFlags.Public | BindingFlags.Instance);
                if (!(bool)activeProp.GetValue(ctrl)) toggle.Invoke(ctrl, null);

                // Update config so the next ConfigBound update doesn't undo our changes.
                var pluginType = ResolveType("Lantern.Plugin");
                var cfg = pluginType.GetField("Cfg", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                SetCfg(cfg, "ItemNameMatch", "");
                SetCfg(cfg, "Intensity", 633337f);   // raw HDRP-internal value
                SetCfg(cfg, "Range", 13f);
                SetCfg(cfg, "OffsetForward", 0.4f);
                SetCfg(cfg, "OffsetUp", 0.3f);
                SetCfg(cfg, "CastShadows", true);
                SetCfg(cfg, "Flicker", false);        // game torch handles flicker via VFX, not light intensity

                // Find our root GameObject (must be active = toggle just ran)
                var root = UnityEngine.Object.FindObjectsOfType<Transform>()
                    .FirstOrDefault(x => x != null && x.name == "Lantern_Plugin" && x.parent == null);
                if (root == null) return "Lantern_Plugin GO not found after toggle";

                var light = root.GetComponent<Light>();
                if (light != null)
                {
                    // Mirror torch's basic Light properties.
                    light.color = Color.white;
                    light.range = 13f;
                    light.shadows = LightShadows.Soft;
                    light.intensity = 633337f;
                }

                var hdComp = root.GetComponents<Component>().FirstOrDefault(c =>
                    c != null && c.GetType().FullName == "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData");
                if (hdComp == null) return "HDAdditionalLightData not found on root — toggle off+on first";

                var hdType = hdComp.GetType();

                // Set lightUnit = Ev100. Try property setter first; if it's an enum-typed property,
                // we need to construct the enum value by name.
                TrySetEnum(hdComp, "lightUnit", "Ev100");

                // Some HDRP versions require SetLightUnit(LightUnit). Try that too.
                var setLightUnit = hdType.GetMethod("SetLightUnit", BindingFlags.Public | BindingFlags.Instance);
                if (setLightUnit != null)
                {
                    var paramType = setLightUnit.GetParameters()[0].ParameterType;
                    object enumVal = Enum.Parse(paramType, "Ev100", ignoreCase: true);
                    setLightUnit.Invoke(hdComp, new[] { enumVal });
                }

                TrySet(hdComp, "luxAtDistance", 18f);
                TrySet(hdComp, "shapeRadius", 3f);
                TrySet(hdComp, "shapeWidth", 0.5f);
                TrySet(hdComp, "shapeHeight", 0.5f);
                TrySet(hdComp, "affectsVolumetric", false);
                TrySet(hdComp, "shadowDimmer", 0.8f);
                TrySet(hdComp, "volumetricDimmer", 0f);
                TrySet(hdComp, "volumetricShadowDimmer", 0f);
                TrySet(hdComp, "fadeDistance", 256f);
                TrySet(hdComp, "shadowFadeDistance", 128f);
                TrySet(hdComp, "applyRangeAttenuation", true);
                TrySet(hdComp, "useScreenSpaceShadows", false);
                TrySet(hdComp, "interactsWithSky", false);

                // Set the rendering layer mask to RenderingLayer1 (matches torch).
                // RenderingLayerMask is an enum-flags type (uint underneath) in HDRP. Value 1 = first bit.
                TrySet(hdComp, "lightlayersMask", 1u);
                // Also update the Light's renderingLayerMask to be safe
                if (light != null)
                {
                    var lightRenderLayer = typeof(Light).GetProperty("renderingLayerMask");
                    if (lightRenderLayer != null) lightRenderLayer.SetValue(light, 1);
                }

                // The torch shows internal intensity=633337 nits with lightUnit=Ev100. The
                // user-facing EV value we need to set is something around 16–18 (NOT 633337,
                // which HDRP would interpret as EV → 2^633337 = Infinity).
                // Bypass SetIntensity completely and write the underlying internal nits via the
                // setter that takes (intensity, unit) explicitly. If unavailable, just set
                // lightUnit=Lumen first, set intensity in lumen-equivalents, then leave the unit alone.
                var setIntensityWithUnit = hdType.GetMethods()
                    .FirstOrDefault(m => m.Name == "SetIntensity" && m.GetParameters().Length == 2);
                if (setIntensityWithUnit != null)
                {
                    var unitParam = setIntensityWithUnit.GetParameters()[1].ParameterType;
                    var ev100Val = Enum.Parse(unitParam, "Ev100", ignoreCase: true);
                    setIntensityWithUnit.Invoke(hdComp, new object[] { 22.3f, ev100Val });  // ~633k nits
                }
                else
                {
                    // Fallback: SetIntensity(float) interprets value in current lightUnit.
                    var setIntensity = hdType.GetMethod("SetIntensity", new[] { typeof(float) });
                    setIntensity?.Invoke(hdComp, new object[] { 22.3f });
                }

                // SetColor too.
                var setColor = hdType.GetMethod("SetColor", new[] { typeof(Color) });
                setColor?.Invoke(hdComp, new object[] { Color.white });

                return "applied: lightUnit=Ev100 luxAtDistance=18 shapeRadius=3 affectsVolumetric=false volumetricDimmer=0 fadeDistance=256 lightlayersMask=1 intensity=633337 color=white range=13";
            }
            catch (Exception e) { return "ERR: " + e; }
        }

        private static void SetCfg(object cfg, string propName, object value)
        {
            var entry = cfg.GetType().GetProperty(propName).GetValue(cfg);
            entry.GetType().GetProperty("Value").SetValue(entry, value);
        }

        private static void TrySet(object obj, string memberName, object value)
        {
            var t = obj.GetType();
            var p = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                try { p.SetValue(obj, ConvertIfNeeded(value, p.PropertyType)); return; }
                catch (Exception e) { Console.WriteLine($"set prop {memberName}: {e.Message}"); }
            }
            var f = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                try { f.SetValue(obj, ConvertIfNeeded(value, f.FieldType)); }
                catch (Exception e) { Console.WriteLine($"set field {memberName}: {e.Message}"); }
            }
        }

        private static void TrySetEnum(object obj, string memberName, string enumName)
        {
            var t = obj.GetType();
            var p = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.PropertyType.IsEnum)
            {
                var val = Enum.Parse(p.PropertyType, enumName, ignoreCase: true);
                try { p.SetValue(obj, val); return; }
                catch { }
            }
            var f = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType.IsEnum)
            {
                var val = Enum.Parse(f.FieldType, enumName, ignoreCase: true);
                try { f.SetValue(obj, val); } catch { }
            }
        }

        private static object ConvertIfNeeded(object v, Type target)
        {
            if (v == null) return null;
            if (target.IsAssignableFrom(v.GetType())) return v;
            if (target.IsEnum) return Enum.ToObject(target, Convert.ChangeType(v, Enum.GetUnderlyingType(target)));
            return Convert.ChangeType(v, target);
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(fullName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
