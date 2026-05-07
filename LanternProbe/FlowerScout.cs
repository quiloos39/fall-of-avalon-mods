using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class FlowerScout
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // 1) Loaded SPRITES that look flower/petal/daffodil/etc. (UI icons we could blit onto quads)
                var sprites = Resources.FindObjectsOfTypeAll<Sprite>()
                    .Where(s => s != null && !string.IsNullOrEmpty(s.name))
                    .Where(s => Regex(s.name, "(flower|daffodil|marigold|petal|bloom|rose|lily|chamomile|dandelion|herb|yellow)"))
                    .Select(s => $"{s.name}  ({(int)s.rect.width}x{(int)s.rect.height})  tex={s.texture?.name}")
                    .Distinct().Take(40).ToList();
                bag["sprites_flowerLike"] = sprites;

                // 2) Loaded TEXTURES (raw 2D textures) with similar names
                var texs = Resources.FindObjectsOfTypeAll<Texture2D>()
                    .Where(t => t != null && !string.IsNullOrEmpty(t.name))
                    .Where(t => Regex(t.name, "(flower|daffodil|marigold|petal|bloom|rose|lily|chamomile|dandelion|herb|yellow)"))
                    .Select(t => $"{t.name}  ({t.width}x{t.height})")
                    .Distinct().Take(40).ToList();
                bag["textures_flowerLike"] = texs;

                // 3) Loaded MESHES with flower-like names
                var meshes = Resources.FindObjectsOfTypeAll<Mesh>()
                    .Where(m => m != null && !string.IsNullOrEmpty(m.name))
                    .Where(m => Regex(m.name, "(flower|daffodil|marigold|petal|bloom|rose|lily|chamomile|dandelion|herb|yellow)"))
                    .Select(m => $"{m.name}  vertices={m.vertexCount}")
                    .Distinct().Take(40).ToList();
                bag["meshes_flowerLike"] = meshes;

                // 4) Live scene Renderers near the player whose mesh contains "flower"-ish names
                var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
                var hero = heroType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                Vector3 playerPos = hero != null ? (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero) : Vector3.zero;

                var liveFlowers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>()
                    .Where(r => r != null)
                    .Where(r =>
                    {
                        var mn = r.name?.ToLowerInvariant() ?? "";
                        var mesh = r.GetComponent<MeshFilter>()?.sharedMesh?.name?.ToLowerInvariant() ?? "";
                        var mat = r.sharedMaterial?.name?.ToLowerInvariant() ?? "";
                        return Regex(mn + " " + mesh + " " + mat, "(flower|daffodil|marigold|petal|bloom|chamomile|dandelion)");
                    })
                    .Select(r => new
                    {
                        go = r.name,
                        mesh = r.GetComponent<MeshFilter>()?.sharedMesh?.name,
                        material = r.sharedMaterial?.name,
                        distance = Vector3.Distance(r.transform.position, playerPos),
                    })
                    .OrderBy(x => x.distance)
                    .Take(15)
                    .ToList();
                bag["liveFlowerMeshRenderers"] = liveFlowers;

                // 5) Item templates (game's alchemy ingredients) with flower names
                var providerType = ResolveType("Awaken.TG.Main.Templates.TemplatesProvider");
                var worldType = ResolveType("Awaken.TG.MVC.World");
                var servicesProp = worldType?.GetProperty("Services", BindingFlags.Public | BindingFlags.Static);
                var services = servicesProp?.GetValue(null);
                var getMethod = services?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethod && m.GetParameters().Length == 0);
                var provider = getMethod?.MakeGenericMethod(providerType).Invoke(services, null);

                var itemTemplateType = ResolveType("Awaken.TG.Main.Heroes.Items.ItemTemplate");
                var getAllOfType = providerType?.GetMethods()
                    .FirstOrDefault(m => m.Name == "GetAllOfType" && m.IsGenericMethod);
                var allItems = (getAllOfType?.MakeGenericMethod(itemTemplateType).Invoke(provider, null) as System.Collections.IEnumerable)?.Cast<object>().ToList();

                if (allItems != null)
                {
                    var flowerItems = allItems
                        .Select(t => new { t, name = (t.GetType().GetProperty("ItemName")?.GetValue(t) as string) ?? "" })
                        .Where(x => Regex(x.name, "(flower|daffodil|marigold|petal|bloom|rose|lily|chamomile|dandelion|herb)"))
                        .Select(x => new
                        {
                            name = x.name,
                            type = x.t.GetType().Name,
                            hasPickablePrefab = x.t.GetType().GetProperty("PickablePrefab")?.GetValue(x.t) != null,
                        })
                        .Distinct().Take(20).ToList();
                    bag["itemTemplates_flowerLike"] = flowerItems;
                }

                // 6) Sample one live flower's components so we know if it's Drake or a real renderer
                if (liveFlowers.Count > 0)
                {
                    var name = liveFlowers[0].go;
                    var sampleR = UnityEngine.Object.FindObjectsOfType<MeshRenderer>().FirstOrDefault(r => r != null && r.name == name);
                    if (sampleR != null)
                    {
                        bag["sampleFlowerComponents"] = sampleR.gameObject.GetComponents<Component>()
                            .Where(c => c != null).Select(c => c.GetType().FullName).ToList();
                        // Also walk parent for context
                        if (sampleR.transform.parent != null)
                            bag["sampleFlowerParent"] = sampleR.transform.parent.name + " components=" + string.Join(",", sampleR.transform.parent.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name));
                    }
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }

            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        private static bool Regex(string s, string pat)
        {
            try { return System.Text.RegularExpressions.Regex.IsMatch(s ?? "", pat, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
            catch { return false; }
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
