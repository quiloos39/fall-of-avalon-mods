using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace LanternProbe
{
    public static class TestMarker2
    {
        // Inspect Location class for "AddElement" / "Init" / "Refresh" methods, dump the element
        // workflow so we know how to register a manually-spawned LocationMarker.
        public static string Inspect()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var locType = ResolveType("Awaken.TG.Main.Locations.Location");
                var modelType = ResolveType("Awaken.TG.MVC.IModel");
                var elementType = ResolveType("Awaken.TG.MVC.Elements.IElement");

                bag["Location_methods"] = locType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name.IndexOf("element", StringComparison.OrdinalIgnoreCase) >= 0
                             || m.Name.IndexOf("attach", StringComparison.OrdinalIgnoreCase) >= 0
                             || m.Name.IndexOf("refresh", StringComparison.OrdinalIgnoreCase) >= 0
                             || m.Name.IndexOf("add", StringComparison.OrdinalIgnoreCase) >= 0
                             || m.Name == "OnInitialize")
                    .Take(30)
                    .Select(m => $"{(m.IsPublic ? "" : "internal ")}{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                    .ToList();

                // Walk Location's base classes for Element-related methods.
                var classChain = new List<object>();
                for (var t = locType; t != null && t != typeof(object); t = t.BaseType)
                {
                    var ms = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Where(m => m.Name.Contains("Add") || m.Name.Contains("Element") || m.Name.Contains("AttachTo") || m.Name.Contains("Initialize"))
                        .Take(20)
                        .Select(m => $"{(m.IsPublic ? "" : "internal ")}{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .ToList();
                    if (ms.Count > 0) classChain.Add(new { type = t.FullName, members = ms });
                }
                bag["LocationClassChain"] = classChain;

                // The Element type pattern
                var elementBase = ResolveType("Awaken.TG.MVC.Elements.Element");
                if (elementBase != null)
                {
                    bag["ElementBase_methods"] = elementBase.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Where(m => m.Name.Contains("Initialize") || m.Name.Contains("Add") || m.Name.Contains("Attach"))
                        .Take(20)
                        .Select(m => $"{Pretty(m.ReturnType)} {m.Name}({string.Join(",", m.GetParameters().Select(p => Pretty(p.ParameterType)))})")
                        .ToList();
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        // Final check: list ALL elements on the forge model via the generic Elements<T>() method.
        public static string CheckLocationMarkerProperly()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var locSpecType = ResolveType("Awaken.TG.Main.Locations.Setup.LocationSpec");
                var allSpecs = UnityEngine.Object.FindObjectsOfType(locSpecType) as Component[];
                var forge = allSpecs?.FirstOrDefault(s => s != null && s.gameObject.name.IndexOf("Blacksmith", StringComparison.OrdinalIgnoreCase) >= 0);
                if (forge == null) return "no forge";
                var modelProp = locSpecType.GetProperty("Model");
                var model = modelProp?.GetValue(forge);
                if (model == null) return "no model";

                // Use TryGetElement(Type) — non-generic overload — to look for LocationMarker.
                var locationMarkerType = ResolveType("Awaken.TG.Main.Maps.Markers.LocationMarker");
                var tryGetByType = model.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "TryGetElement" && m.GetParameters().Length == 1
                                                                   && m.GetParameters()[0].ParameterType == typeof(Type));
                object locationMarker = tryGetByType?.Invoke(model, new object[] { locationMarkerType });
                bag["hasLocationMarker"] = locationMarker != null;
                if (locationMarker != null)
                {
                    var iconProp = locationMarker.GetType().GetProperty("Icon");
                    var icon = iconProp?.GetValue(locationMarker);
                    bag["locationMarkerIcon"] = icon?.ToString();
                    var posProp = locationMarker.GetType().GetProperty("Position");
                    bag["locationMarkerPos"] = posProp?.GetValue(locationMarker)?.ToString();
                }

                // Also list ModelElements raw if accessible.
                var meProp = model.GetType().GetProperty("ModelElements", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                var me = meProp?.GetValue(model);
                if (me != null)
                {
                    var meType = me.GetType();
                    bag["ModelElementsType"] = meType.FullName;
                    bag["ModelElementsFields"] = meType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(f => $"{Pretty(f.FieldType)} {f.Name}").ToList();
                }
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        // Try the simplest fix: call Model.RevalidateElements() on the forge so the Location
        // re-scans its attachments and spawns a LocationMarker for the freshly-added MarkerAttachment.
        public static string Revalidate()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var locSpecType = ResolveType("Awaken.TG.Main.Locations.Setup.LocationSpec");
                var allSpecs = UnityEngine.Object.FindObjectsOfType(locSpecType) as Component[];
                var forge = allSpecs?.FirstOrDefault(s => s != null && s.gameObject.name.IndexOf("Blacksmith", StringComparison.OrdinalIgnoreCase) >= 0);
                if (forge == null) return "no forge spec";

                var modelProp = locSpecType.GetProperty("Model");
                var model = modelProp?.GetValue(forge);
                if (model == null) return "model null";

                bag["modelType"] = model.GetType().FullName;

                var elementsProp = model.GetType().GetProperty("Elements");
                var elementsBefore = (elementsProp?.GetValue(model) as System.Collections.IEnumerable)?.Cast<object>().Select(e => e.GetType().Name).ToList();
                bag["elementsBefore"] = elementsBefore;

                var revalidateM = model.GetType().GetMethod("RevalidateElements", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (revalidateM == null) return "RevalidateElements not found";
                revalidateM.Invoke(model, null);

                var elementsAfter = (elementsProp?.GetValue(model) as System.Collections.IEnumerable)?.Cast<object>().Select(e => e.GetType().Name).ToList();
                bag["elementsAfter"] = elementsAfter;

                bag["addedLocationMarker"] = elementsAfter?.Contains("LocationMarker") == true;
                bag["note"] = "Open your map and look for a stash icon at the forge position (-857.3, 135.7, -3059.5)";
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        // Now: call MarkerAttachment.SpawnElement() on the forge's attachment, then try to attach
        // the resulting Element to the forge Location model via whatever method we can find.
        public static string SpawnAndAttach()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var locSpecType = ResolveType("Awaken.TG.Main.Locations.Setup.LocationSpec");
                var maType = ResolveType("Awaken.TG.Main.Maps.Markers.MarkerAttachment");
                var allSpecs = UnityEngine.Object.FindObjectsOfType(locSpecType) as Component[];
                var forge = allSpecs?.FirstOrDefault(s => s != null && s.gameObject.name.IndexOf("Blacksmith", StringComparison.OrdinalIgnoreCase) >= 0);
                if (forge == null) return "no forge spec";

                var ma = forge.gameObject.GetComponent(maType);
                if (ma == null) return "forge has no MarkerAttachment (call TestMarker.Run first)";

                var spawnElementM = maType.GetMethod("SpawnElement", BindingFlags.Public | BindingFlags.Instance);
                if (spawnElementM == null) return "no SpawnElement method on MarkerAttachment";

                var element = spawnElementM.Invoke(ma, null);
                bag["spawnedElementType"] = element?.GetType().FullName;
                if (element == null) return JsonConvert.SerializeObject(bag);

                var modelProp = locSpecType.GetProperty("Model");
                var model = modelProp?.GetValue(forge);
                bag["modelType"] = model?.GetType().FullName;
                if (model == null) return JsonConvert.SerializeObject(bag);

                // Find an "AddElement" method on the model.
                var addElementM = model.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                    .Where(m => m.Name.Contains("AddElement") || m.Name == "Add")
                    .Where(m => m.GetParameters().Length == 1)
                    .ToList();
                bag["candidateAddMethods"] = addElementM
                    .Select(m => $"{(m.IsPublic ? "" : "internal ")}{Pretty(m.ReturnType)} {m.Name}({Pretty(m.GetParameters()[0].ParameterType)})")
                    .ToList();

                // Try the first one that takes the element type or its interface.
                MethodInfo bestAdd = null;
                foreach (var m in addElementM)
                {
                    var p = m.GetParameters()[0].ParameterType;
                    if (p.IsAssignableFrom(element.GetType())) { bestAdd = m; break; }
                }
                if (bestAdd != null)
                {
                    try
                    {
                        var added = bestAdd.Invoke(model, new[] { element });
                        bag["addedVia"] = bestAdd.Name;
                        bag["addReturned"] = added?.ToString() ?? "void/null";
                    }
                    catch (Exception e) { bag["addError"] = e.InnerException?.ToString() ?? e.ToString(); }
                }
                else bag["addElementMethod"] = "(none compatible found)";

                // Re-check elements
                var elementsProp = model.GetType().GetProperty("Elements");
                var elements = elementsProp?.GetValue(model) as System.Collections.IEnumerable;
                bag["elementsAfter"] = elements == null ? null : elements.Cast<object>().Select(e => e.GetType().Name).ToList();
            }
            catch (Exception e) { bag["err"] = e.ToString(); }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
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

        private static string Pretty(Type t)
        {
            if (t == null) return "?";
            if (!t.IsGenericType) return t.Name;
            return t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(g => g.Name)) + ">";
        }
    }
}
