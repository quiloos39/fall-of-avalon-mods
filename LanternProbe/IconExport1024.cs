using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LanternProbe
{
    public static class IconExport1024
    {
        // Same as IconExport but blits into a 1024×1024 RT with bilinear filtering for an
        // upscale of the 256-source sprite.
        public static string Run()
        {
            try
            {
                string outputPath = @"C:\Users\quilo\source\StationIndicator\stash-icon-1024.png";
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                var iMarkerTemplate = ResolveType("Awaken.TG.Main.Maps.Markers.IMarkerDataTemplate");
                var allSO = Resources.FindObjectsOfTypeAll<ScriptableObject>();
                var t = allSO.FirstOrDefault(so => so != null && iMarkerTemplate.IsAssignableFrom(so.GetType()) && so.name == "SimpleMarkerData_CraftingStash");
                if (t == null) return "SimpleMarkerData_CraftingStash template not found";

                var md = t.GetType().GetProperty("Get").GetValue(t);
                var iconRef = md.GetType().GetField("defaultIcon", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(md);
                var arRef = iconRef.GetType().GetField("arSpriteReference", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(iconRef);
                var loadM = arRef.GetType().GetMethods().First(m => m.Name == "LoadAsset" && m.IsGenericMethod);
                var handle = loadM.MakeGenericMethod(typeof(Sprite)).Invoke(arRef, null);
                var sprite = handle.GetType().GetMethod("WaitForCompletion").Invoke(handle, null) as Sprite;
                if (sprite == null) return "sprite load failed";

                int dst = 1024;
                int srcW = (int)sprite.rect.width;
                int srcH = (int)sprite.rect.height;
                var tex = sprite.texture;

                // Force bilinear on the source so the GPU upscales smoothly. We restore the
                // previous filter mode after the blit so we don't leak state into the game.
                FilterMode prevFilter = tex.filterMode;
                tex.filterMode = FilterMode.Bilinear;

                var rt = RenderTexture.GetTemporary(dst, dst, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                var prevRT = RenderTexture.active;
                try
                {
                    Graphics.Blit(tex, rt,
                        new Vector2(sprite.rect.width / tex.width, sprite.rect.height / tex.height),
                        new Vector2(sprite.rect.x / tex.width, sprite.rect.y / tex.height));

                    RenderTexture.active = rt;
                    var readable = new Texture2D(dst, dst, TextureFormat.RGBA32, false, false);
                    readable.ReadPixels(new Rect(0, 0, dst, dst), 0, 0);
                    readable.Apply();

                    var imgConv = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                        .First(tt => tt != null && tt.FullName == "UnityEngine.ImageConversion");
                    var encodeM = imgConv.GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Static);
                    byte[] png = (byte[])encodeM.Invoke(null, new object[] { readable });
                    File.WriteAllBytes(outputPath, png);
                    UnityEngine.Object.Destroy(readable);
                }
                finally
                {
                    RenderTexture.active = prevRT;
                    RenderTexture.ReleaseTemporary(rt);
                    tex.filterMode = prevFilter;
                }

                return Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    ok = true,
                    outputPath,
                    sizeBytes = new FileInfo(outputPath).Length,
                    spriteName = sprite.name,
                    sourceDim = $"{srcW}x{srcH}",
                    outputDim = $"{dst}x{dst}",
                    upscaleFactor = (float)dst / srcW,
                });
            }
            catch (Exception e) { return "ERR: " + e; }
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
