using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using Awaken.TG.MVC;
using Awaken.TG.Main.Locations;
using Awaken.TG.Main.Fights.NPCs;
using Awaken.TG.Main.Fights.NPCs.Presences;
using Awaken.TG.Main.Heroes;

namespace LanternProbe
{
    // Why are these fae here? Each NpcElement has an NpcPresence (or its
    // ParentModel does) that tells us whether it's permanently present, gated
    // on a story flag, daytime/nighttime, or spawned by a spawner. Dump the
    // relevant props for every loaded fae.
    public static class FaePresenceProbe
    {
        public static string Run()
        {
            var bag = new Dictionary<string, object>();
            try
            {
                var hero = Hero.Current;
                bag["heroPos"] = hero == null ? null : F(hero.Coords);

                var rows = new List<object>();
                foreach (var npc in World.All<NpcElement>())
                {
                    string locTname = SafeStr(() => (npc.ParentModel?.Template as UnityEngine.Object)?.name) ?? "";
                    string npcTname = SafeStr(() => (npc.Template as UnityEngine.Object)?.name) ?? "";
                    if (!(locTname.ToLowerInvariant().Contains("fae") || npcTname.ToLowerInvariant().Contains("fae"))) continue;

                    Vector3? coords = null; try { coords = npc.Coords; } catch { }

                    var presence = npc.NpcPresence;
                    var presenceProps = new Dictionary<string, string>();
                    if (presence != null)
                    {
                        DumpScalars(presence, presenceProps);
                        var parent = presence.ParentModel;
                        if (parent != null)
                        {
                            presenceProps["_parentModelType"] = parent.GetType().FullName;
                            presenceProps["_parentModelDebugName"] = SafeStr(() => parent.DebugName);
                        }
                        var pTemplate = presence.Template;
                        if (pTemplate != null)
                        {
                            presenceProps["_template_name"] = SafeStr(() => (pTemplate as UnityEngine.Object)?.name);
                        }
                    }

                    var locProps = new Dictionary<string, string>();
                    if (npc.ParentModel is Location loc)
                    {
                        locProps["debugName"] = SafeStr(() => loc.DebugName);
                        DumpScalars(loc, locProps, takeOnly: new[] { "Spec", "MainView", "IsVisualLoaded", "IsMainViewLoaded" });
                        var attachments = new List<string>();
                        try
                        {
                            var components = loc.Spec?.gameObject?.GetComponents<Component>();
                            if (components != null)
                                foreach (var c in components)
                                    if (c != null) attachments.Add(c.GetType().Name);
                        }
                        catch { }
                        locProps["spec_components"] = string.Join(",", attachments);
                    }

                    rows.Add(new
                    {
                        locTemplate = locTname,
                        npcTemplate = npcTname,
                        coords = coords.HasValue ? F(coords.Value) : null,
                        hasPresence = presence != null,
                        presence_type = presence?.GetType().FullName,
                        presence_props = presenceProps,
                        loc_props = locProps,
                    });
                }
                bag["fae"] = rows;

                // Also list NpcPresences that template-match fae but are NOT currently producing an NpcElement.
                // That tells us how many "spawn slots" there are for fae across the loaded map.
                var presenceAll = new List<object>();
                foreach (var p in World.All<NpcPresence>())
                {
                    string ptname = SafeStr(() => (p.Template as UnityEngine.Object)?.name) ?? "";
                    string parentName = SafeStr(() => (p.ParentModel?.Template as UnityEngine.Object)?.name) ?? "";
                    if (!(ptname.ToLowerInvariant().Contains("fae") || parentName.ToLowerInvariant().Contains("fae"))) continue;
                    presenceAll.Add(new
                    {
                        presence_template = ptname,
                        location_template = parentName,
                        parent_pos = SafeStr(() => p.ParentModel != null ? F(p.ParentModel.Coords) : null),
                        available = SafeBool(() => p.Available),
                        attached = SafeBool(() => p.Attached),
                    });
                }
                bag["fae_presences"] = presenceAll;
            }
            catch (Exception e)
            {
                bag["err"] = e.GetBaseException().Message;
                bag["stack"] = e.StackTrace;
            }
            return JsonConvert.SerializeObject(bag, Formatting.Indented);
        }

        static void DumpScalars(object obj, Dictionary<string, string> into, string[] takeOnly = null)
        {
            if (obj == null) return;
            foreach (var p in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (takeOnly != null && !takeOnly.Contains(p.Name)) continue;
                object v;
                try { v = p.GetValue(obj); } catch { continue; }
                if (v == null) { into[p.Name] = "null"; continue; }
                var t = v.GetType();
                if (v is string s) { into[p.Name] = Trim(s, 200); continue; }
                if (v is bool || v is int || v is long || v is float || v is double || v is decimal || t.IsEnum)
                {
                    into[p.Name] = v.ToString(); continue;
                }
                if (v is Vector3 v3) { into[p.Name] = $"({v3.x:F1},{v3.y:F1},{v3.z:F1})"; continue; }
                if (t.IsClass)
                {
                    var ts = v.ToString();
                    if (ts != t.FullName && ts != t.Name) into[p.Name] = Trim(ts, 200);
                }
            }
        }

        static string Trim(string s, int max) => s == null ? null : (s.Length <= max ? s : s.Substring(0, max) + "…");
        static string F(Vector3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";
        static string SafeStr(Func<string> f) { try { return f(); } catch { return null; } }
        static bool? SafeBool(Func<bool> f) { try { return f(); } catch { return null; } }
    }
}
