using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LanternProbe
{
    public static class Drama
    {
        // Make the light DRAMATICALLY visible — bright cyan, all rendering layers, 50m range,
        // far above the head so it can't be hidden inside the body.
        public static string MakeItObvious()
        {
            try
            {
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "Lantern.LanternController");
                if (ctrl == null) return "no controller";

                var t = ctrl.GetType();
                var pluginType = ResolveType("Lantern.Plugin");
                var cfg = pluginType.GetField("Cfg", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

                // Cyan = (0, 1, 1). Sticks out hard against any natural environment.
                SetCfg(cfg, "ColorR", 0.0f);
                SetCfg(cfg, "ColorG", 1.0f);
                SetCfg(cfg, "ColorB", 1.0f);
                // Crank distance and offset.
                SetCfg(cfg, "Range", 50f);
                SetCfg(cfg, "OffsetUp", 2.0f);          // 2m above the chest = floats over head
                SetCfg(cfg, "OffsetForward", 0f);
                SetCfg(cfg, "OffsetRight", 0f);
                SetCfg(cfg, "Flicker", false);          // no flicker so it's a constant beacon
                SetCfg(cfg, "CastShadows", false);

                // Force-toggle off+on so a fresh _root is built.
                t.GetField("_cachedPrefab", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                t.GetField("_cachedPrefabKey", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ctrl, null);
                var activeProp = t.GetProperty("Active");
                var toggle = t.GetMethod("Toggle", BindingFlags.Public | BindingFlags.Instance);
                if ((bool)activeProp.GetValue(ctrl)) toggle.Invoke(ctrl, null);
                toggle.Invoke(ctrl, null);

                // Now find the just-spawned root and reconfigure its HD light data:
                //   • lightUnit = Ev100, EV ≈ 22.3 (matches torch internal nits ~633k)
                //   • shapeRadius = 0.5 (point-ish, easy to spot)
                //   • lightlayersMask = uint.MaxValue (light EVERY rendering layer)
                //   • range = 50
                var root = UnityEngine.Object.FindObjectsOfType<Transform>()
                    .FirstOrDefault(x => x != null && x.name == "Lantern_Plugin" && x.parent == null);
                if (root == null) return "Lantern_Plugin GO not found after toggle";

                var light = root.GetComponent<Light>();
                if (light != null)
                {
                    light.color = Color.cyan;
                    light.range = 50f;
                    light.shadows = LightShadows.None;
                    light.intensity = 1_000_000f;       // extreme nits, will be reset by SetIntensity below
                    var rl = typeof(Light).GetProperty("renderingLayerMask");
                    if (rl != null) rl.SetValue(light, unchecked((int)uint.MaxValue));
                }

                var hd = root.GetComponents<Component>().FirstOrDefault(c =>
                    c != null && c.GetType().FullName == "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData");
                if (hd == null) return "HDAdditionalLightData missing";
                var hdType = hd.GetType();

                TrySetEnum(hd, "lightUnit", "Ev100");
                var setLightUnit = hdType.GetMethod("SetLightUnit", BindingFlags.Public | BindingFlags.Instance);
                if (setLightUnit != null)
                {
                    var pt = setLightUnit.GetParameters()[0].ParameterType;
                    setLightUnit.Invoke(hd, new[] { Enum.Parse(pt, "Ev100", ignoreCase: true) });
                }
                TrySet(hd, "luxAtDistance", 18f);
                TrySet(hd, "shapeRadius", 0.5f);
                TrySet(hd, "shapeWidth", 0.5f);
                TrySet(hd, "shapeHeight", 0.5f);
                TrySet(hd, "affectsVolumetric", true);   // ON = visible "god ray" cone in fog
                TrySet(hd, "shadowDimmer", 1f);
                TrySet(hd, "volumetricDimmer", 1f);
                TrySet(hd, "fadeDistance", 1024f);
                TrySet(hd, "shadowFadeDistance", 256f);
                TrySet(hd, "applyRangeAttenuation", true);
                TrySet(hd, "lightlayersMask", uint.MaxValue);   // ALL render layers
                TrySet(hd, "range", 50f);

                // Set the EV.
                var setIntensityWithUnit = hdType.GetMethods()
                    .FirstOrDefault(m => m.Name == "SetIntensity" && m.GetParameters().Length == 2);
                if (setIntensityWithUnit != null)
                {
                    var unitParam = setIntensityWithUnit.GetParameters()[1].ParameterType;
                    setIntensityWithUnit.Invoke(hd, new object[] { 24f, Enum.Parse(unitParam, "Ev100", ignoreCase: true) });
                }
                else
                {
                    hdType.GetMethod("SetIntensity", new[] { typeof(float) })?.Invoke(hd, new object[] { 24f });
                }

                hdType.GetMethod("SetColor", new[] { typeof(Color) })?.Invoke(hd, new object[] { Color.cyan });

                return "Drama applied: CYAN, range=50, EV=24, shapeRadius=0.5, OffsetUp=2m, ALL render layers, volumetric ON. Look up.";
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
                try { p.SetValue(obj, ConvertIfNeeded(value, p.PropertyType)); return; } catch { }
            }
            var f = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) { try { f.SetValue(obj, ConvertIfNeeded(value, f.FieldType)); } catch { } }
        }

        private static void TrySetEnum(object obj, string memberName, string enumName)
        {
            var t = obj.GetType();
            var p = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.PropertyType.IsEnum)
            {
                try { p.SetValue(obj, Enum.Parse(p.PropertyType, enumName, ignoreCase: true)); return; } catch { }
            }
            var f = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType.IsEnum)
            {
                try { f.SetValue(obj, Enum.Parse(f.FieldType, enumName, ignoreCase: true)); } catch { }
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
