using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class IconCatalog
    {
        // 1) Find every SimpleMarkerDataTemplate / DiscoveryMarkerDataTemplate / MarkerDataTemplate
        //    asset loaded in memory (Resources.FindObjectsOfTypeAll catches assets in Addressable
        //    cache and editor pool). 2) For each, dump name + the underlying icon's sprite/texture name.
        public static string ListMarkerTemplates()
        {
            try
            {
                var iMarkerTemplate = ResolveType("Awaken.TG.Main.Maps.Markers.IMarkerDataTemplate");
                if (iMarkerTemplate == null) return "IMarkerDataTemplate type missing";

                var allSO = Resources.FindObjectsOfTypeAll<ScriptableObject>();
                var templates = allSO.Where(so => so != null && iMarkerTemplate.IsAssignableFrom(so.GetType())).ToList();

                var rows = new List<object>();
                foreach (var t in templates)
                {
                    var bag = new Dictionary<string, object>
                    {
                        ["templateName"] = t.name,
                        ["templateType"] = t.GetType().Name,
                    };

                    // Pull MarkerData via the "Get" property (defined on MarkerDataTemplate<T>).
                    object md = null;
                    try
                    {
                        var getProp = t.GetType().GetProperty("Get");
                        md = getProp?.GetValue(t);
                    }
                    catch { }
                    if (md == null) { rows.Add(bag); continue; }

                    bag["markerDataType"] = md.GetType().Name;

                    // Read MarkerData's defaultIcon ShareableSpriteReference → arSpriteReference → asset GUID
                    foreach (var iconFieldName in new[] { "defaultIcon", "undiscoveredMarkerIcon", "inactiveMarkerIcon" })
                    {
                        var f = md.GetType().GetField(iconFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f == null) continue;
                        var iconRef = f.GetValue(md);
                        if (iconRef == null) { bag[iconFieldName] = null; continue; }

                        var arRefField = iconRef.GetType().GetField("arSpriteReference", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var arRef = arRefField?.GetValue(iconRef);

                        string guid = null;
                        try
                        {
                            var guidProp = arRef?.GetType().GetProperty("AssetGUID");
                            guid = guidProp?.GetValue(arRef) as string;
                        }
                        catch { }

                        // Try resolving the Sprite to learn its name (won't always succeed without load).
                        string spriteName = null;
                        try
                        {
                            var loadM = arRef?.GetType().GetMethods().FirstOrDefault(m => m.Name == "LoadAsset" && m.IsGenericMethod);
                            if (loadM != null)
                            {
                                var handle = loadM.MakeGenericMethod(typeof(Sprite)).Invoke(arRef, null);
                                var waitM = handle?.GetType().GetMethod("WaitForCompletion");
                                var s = waitM?.Invoke(handle, null) as Sprite;
                                spriteName = s?.name;
                            }
                        }
                        catch { }

                        bag[iconFieldName] = new { guid, spriteName };
                    }

                    // Visibility flags
                    foreach (var fn in new[] { "visibleOnMap", "visibleOnMapUnderFogOfWar", "compassMarkerType" })
                    {
                        var f = md.GetType().GetField(fn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f != null) try { bag[fn] = f.GetValue(md)?.ToString(); } catch { }
                    }

                    rows.Add(bag);
                }

                return JsonConvert.SerializeObject(new
                {
                    totalTemplates = rows.Count,
                    templates = rows.OrderBy(r => ((Dictionary<string, object>)r)["templateName"]).ToList(),
                }, Formatting.Indented);
            }
            catch (Exception e) { return "ERR: " + e; }
        }

        // Export the icon for a named template to disk. Caller supplies name + outputPath.
        public static string ExportTemplateIcon(string templateName, string outputPath)
        {
            try
            {
                if (string.IsNullOrEmpty(templateName)) return "templateName required";
                if (string.IsNullOrEmpty(outputPath)) return "outputPath required";

                var iMarkerTemplate = ResolveType("Awaken.TG.Main.Maps.Markers.IMarkerDataTemplate");
                var allSO = Resources.FindObjectsOfTypeAll<ScriptableObject>();
                var t = allSO.FirstOrDefault(so => so != null && iMarkerTemplate.IsAssignableFrom(so.GetType()) && so.name == templateName);
                if (t == null) return $"template '{templateName}' not found";

                var getProp = t.GetType().GetProperty("Get");
                var md = getProp?.GetValue(t);
                if (md == null) return "template.Get returned null";

                var iconF = md.GetType().GetField("defaultIcon", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var iconRef = iconF?.GetValue(md);
                var arRefField = iconRef?.GetType().GetField("arSpriteReference", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var arRef = arRefField?.GetValue(iconRef);
                if (arRef == null) return "no arSpriteReference";

                var loadM = arRef.GetType().GetMethods().FirstOrDefault(m => m.Name == "LoadAsset" && m.IsGenericMethod);
                var handle = loadM.MakeGenericMethod(typeof(Sprite)).Invoke(arRef, null);
                var waitM = handle.GetType().GetMethod("WaitForCompletion");
                var sprite = waitM?.Invoke(handle, null) as Sprite;
                if (sprite == null) return "sprite load failed";

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                int w = (int)sprite.rect.width;
                int h = (int)sprite.rect.height;
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                var prevRT = RenderTexture.active;
                try
                {
                    var tex = sprite.texture;
                    Graphics.Blit(tex, rt,
                        new Vector2(sprite.rect.width / tex.width, sprite.rect.height / tex.height),
                        new Vector2(sprite.rect.x / tex.width, sprite.rect.y / tex.height));
                    RenderTexture.active = rt;
                    var readable = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                    readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    readable.Apply();

                    var imgConv = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                        .FirstOrDefault(tt => tt != null && tt.FullName == "UnityEngine.ImageConversion");
                    var encodeM = imgConv.GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Static);
                    byte[] png = (byte[])encodeM.Invoke(null, new object[] { readable });
                    File.WriteAllBytes(outputPath, png);
                    UnityEngine.Object.Destroy(readable);
                }
                finally { RenderTexture.active = prevRT; RenderTexture.ReleaseTemporary(rt); }

                return JsonConvert.SerializeObject(new { ok = true, path = outputPath, sizeBytes = new FileInfo(outputPath).Length, spriteName = sprite.name, dim = $"{w}x{h}" });
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
