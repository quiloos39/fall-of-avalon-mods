using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class QuestPathLook
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                // Force the controller ON if it isn't.
                var ctrl = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && mb.GetType().FullName == "QuestPath.QuestPathController");
                if (ctrl == null) return "no controller";

                bag["activeBefore"] = ctrl.GetType().GetProperty("Active")?.GetValue(ctrl);

                var t = ctrl.GetType();
                var line = t.GetField("_line", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ctrl) as LineRenderer;
                var lineMat = t.GetField("_lineMaterial", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ctrl) as Material;
                var currentPath = t.GetField("_currentPath", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ctrl);
                var lineHost = t.GetField("_lineHost", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ctrl) as GameObject;
                var beamMat = t.GetField("_beamMaterial", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ctrl) as Material;
                var discMat = t.GetField("_discMaterial", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ctrl) as Material;
                var discMesh = t.GetField("_discMesh", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ctrl) as GameObject;

                bag["lineHost"] = lineHost == null ? "null" : $"{lineHost.name} active={lineHost.activeInHierarchy}";
                bag["line"] = line == null ? "null" : new
                {
                    enabled = line.enabled,
                    positionCount = line.positionCount,
                    startWidth = line.startWidth,
                    startColor = $"({line.startColor.r:F2},{line.startColor.g:F2},{line.startColor.b:F2})",
                };
                bag["lineMaterial"] = lineMat == null ? "null" : new
                {
                    shader = lineMat.shader?.name,
                    color = $"({lineMat.color.r:F2},{lineMat.color.g:F2},{lineMat.color.b:F2})",
                    mainTexture = lineMat.mainTexture?.name + $" ({lineMat.mainTexture?.width}x{lineMat.mainTexture?.height})",
                    mainTextureScale = $"({lineMat.mainTextureScale.x:F2},{lineMat.mainTextureScale.y:F2})",
                    mainTextureOffset = $"({lineMat.mainTextureOffset.x:F3},{lineMat.mainTextureOffset.y:F3})",
                    hasEmissive = lineMat.HasProperty("_EmissionColor"),
                    emissionColor = lineMat.HasProperty("_EmissionColor") ? lineMat.GetColor("_EmissionColor").ToString() : "n/a",
                    surfaceType = lineMat.HasProperty("_SurfaceType") ? lineMat.GetFloat("_SurfaceType") : -1f,
                };
                bag["beamMaterial"] = beamMat == null ? "null" : new
                {
                    shader = beamMat.shader?.name,
                    mainTexture = beamMat.mainTexture?.name + $" ({beamMat.mainTexture?.width}x{beamMat.mainTexture?.height})",
                };
                bag["discMaterial"] = discMat == null ? "null" : new
                {
                    shader = discMat.shader?.name,
                    mainTexture = discMat.mainTexture?.name + $" ({discMat.mainTexture?.width}x{discMat.mainTexture?.height})",
                };
                bag["discMeshActive"] = discMesh != null && discMesh.activeInHierarchy;
                bag["currentPathCorners"] = (currentPath as System.Collections.IList)?.Count ?? -1;

                // Show live destination + tracker quest name
                var providerType = ResolveType("QuestPath.DestinationProvider");
                var resolveM = providerType?.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
                bag["DestinationProvider.Resolve"] = resolveM?.Invoke(null, null)?.ToString();

                // Sanity: list shaders present that look HDRP/unlit-related
                var shaders = new[] { "HDRP/Unlit", "HDRP/Lit", "Universal Render Pipeline/Unlit", "Unlit/Transparent", "Unlit/Color" };
                bag["shadersFound"] = shaders.Where(s => Shader.Find(s) != null).ToList();
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
