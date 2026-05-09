using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class IconExport
    {
        // Locate the stash MarkerAttachment, resolve its sprite, blit to a RenderTexture
        // (so we can read GPU-only textures), then EncodeToPNG and write to disk.
        public static string Run()
        {
            try
            {
                string outputPath = @"C:\Users\quilo\source\StationIndicator\stash-icon.png";
                string outputDir = Path.GetDirectoryName(outputPath);
                Directory.CreateDirectory(outputDir);

                // ── 1. Get a stash spec → MarkerAttachment → MarkerData.MarkerIcon (ShareableSpriteReference)
                var locSpecType = ResolveType("Awaken.TG.Main.Locations.Setup.LocationSpec");
                var maType = ResolveType("Awaken.TG.Main.Maps.Markers.MarkerAttachment");
                var allSpecs = UnityEngine.Object.FindObjectsOfType(locSpecType) as Component[];
                var stash = allSpecs?.FirstOrDefault(s => s != null && s.gameObject.name.IndexOf("ChestPlayerStash", StringComparison.OrdinalIgnoreCase) >= 0)
                         ?? allSpecs?.FirstOrDefault(s => s != null && s.gameObject.name.IndexOf("stash", StringComparison.OrdinalIgnoreCase) >= 0);
                if (stash == null) return "no stash spec in scene — need to be in a region where the player stash exists";

                var ma = stash.gameObject.GetComponent(maType);
                if (ma == null) return "stash has no MarkerAttachment";

                var markerDataProp = maType.GetProperty("MarkerData");
                var markerData = markerDataProp.GetValue(ma);
                var iconRefProp = markerData.GetType().GetProperty("MarkerIcon");
                var iconRef = iconRefProp.GetValue(markerData); // ShareableSpriteReference

                // ── 2. Drill into ShareableSpriteReference → arSpriteReference (ARAssetReference) → load Sprite
                var arRefField = iconRef.GetType().GetField("arSpriteReference", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                var arRef = arRefField?.GetValue(iconRef);
                if (arRef == null) return "iconRef.arSpriteReference is null";

                // Try ShareableSpriteReference's high-level API first.
                Sprite sprite = TryGetSpriteViaShareable(iconRef) ?? TryGetSpriteViaARRef(arRef);
                if (sprite == null) return "could not load Sprite from icon ref (see log for details)";

                // (diagnostic info goes back in the JSON return, not Plugin.Log — this DLL has no Plugin)

                // ── 3. Blit the sprite's region of its source texture into a temporary readable RenderTexture,
                //      then ReadPixels into a Texture2D, then EncodeToPNG.
                var tex = sprite.texture;
                if (tex == null) return "sprite.texture is null";

                int w = (int)sprite.rect.width;
                int h = (int)sprite.rect.height;

                // Allocate a temporary RT in linear space so we don't double-apply gamma.
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                var prevRT = RenderTexture.active;
                try
                {
                    // Blit the SUBRECT of the source texture into the full RT.
                    // We can't Blit a subrect directly with stock Graphics.Blit — we set up a UV scale/offset.
                    var scaleOffset = new Vector4(
                        sprite.rect.width / tex.width,         // scale.x
                        sprite.rect.height / tex.height,       // scale.y
                        sprite.rect.x / tex.width,             // offset.x
                        sprite.rect.y / tex.height);           // offset.y
                    Graphics.Blit(tex, rt,
                        new Vector2(scaleOffset.x, scaleOffset.y),
                        new Vector2(scaleOffset.z, scaleOffset.w));

                    RenderTexture.active = rt;
                    var readable = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false, linear: false);
                    readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    readable.Apply();

                    // Sprite atlas often has the source flipped / rotated. We export raw pixels;
                    // if it looks upside-down, we'd need to also handle SpritePackingRotation.
                    // EncodeToPNG lives in UnityEngine.ImageConversionModule — call via reflection
                    // so we don't need to add another DLL reference for a one-shot probe.
                    var imgConv = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                        .FirstOrDefault(t => t != null && t.FullName == "UnityEngine.ImageConversion");
                    var encodeM = imgConv?.GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Static);
                    if (encodeM == null) throw new Exception("UnityEngine.ImageConversion.EncodeToPNG not found");
                    byte[] png = (byte[])encodeM.Invoke(null, new object[] { readable });
                    File.WriteAllBytes(outputPath, png);
                    UnityEngine.Object.Destroy(readable);
                }
                finally
                {
                    RenderTexture.active = prevRT;
                    RenderTexture.ReleaseTemporary(rt);
                }

                long size = new FileInfo(outputPath).Length;
                return JsonConvert.SerializeObject(new
                {
                    ok = true,
                    outputPath,
                    sizeBytes = size,
                    spriteName = sprite.name,
                    rect = $"({sprite.rect.x},{sprite.rect.y},{sprite.rect.width}x{sprite.rect.height})",
                    sourceTexture = sprite.texture?.name,
                    sourceTexSize = $"{sprite.texture?.width}x{sprite.texture?.height}",
                }, Formatting.Indented);
            }
            catch (Exception e) { return "ERR: " + e; }
        }

        private static Sprite TryGetSpriteViaShareable(object shareable)
        {
            try
            {
                // ShareableSpriteReference may have a Get() / LoadAsset / LoadAssetSync method.
                var t = shareable.GetType();
                var loadM = t.GetMethods().FirstOrDefault(m => (m.Name == "LoadAsset" || m.Name == "Get" || m.Name == "LoadSprite")
                                                              && m.GetParameters().Length == 0 && !m.IsGenericMethodDefinition);
                if (loadM != null)
                {
                    var r = loadM.Invoke(shareable, null);
                    if (r is Sprite s) return s;
                    // Maybe it returned an async handle.
                    return ExtractSpriteFromHandle(r);
                }
            }
            catch { /* fall through */ }
            return null;
        }

        private static Sprite TryGetSpriteViaARRef(object arRef)
        {
            try
            {
                // ARAssetReference.LoadAsset<Sprite>() returns an async operation handle.
                var t = arRef.GetType();
                var loadM = t.GetMethods().FirstOrDefault(m => m.Name == "LoadAsset" && m.IsGenericMethod);
                if (loadM == null) return null;
                var generic = loadM.MakeGenericMethod(typeof(Sprite));
                var handle = generic.Invoke(arRef, null);
                return ExtractSpriteFromHandle(handle);
            }
            catch { /* fall through */ }
            return null;
        }

        private static Sprite ExtractSpriteFromHandle(object handle)
        {
            if (handle == null) return null;
            try
            {
                var ht = handle.GetType();
                // Try WaitForCompletion()
                var waitM = ht.GetMethod("WaitForCompletion");
                if (waitM != null)
                {
                    var r = waitM.Invoke(handle, null);
                    if (r is Sprite s) return s;
                }
                // Try .Result
                var resultProp = ht.GetProperty("Result");
                if (resultProp != null)
                {
                    var r = resultProp.GetValue(handle);
                    if (r is Sprite s2) return s2;
                }
            }
            catch { /* fall through */ }
            return null;
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
