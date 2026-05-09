using System;
using System.Collections.Generic;
using Awaken.TG.Main.Heroes;
using Newtonsoft.Json;

namespace BuildAdvisor
{
    // Dump descriptions for ALL talents in ALL tables (base + Wyrd + Sarras).
    public static class FullTalentProbe
    {
        public static string Run()
        {
            try
            {
                var hero = Hero.Current;
                if (hero == null) return "{}";
                var ht = hero.Talents;
                var trees = new List<object>();

                var tables = new List<Awaken.TG.Main.Heroes.Development.Talents.TalentTable>();
                tables.AddRange(ht.BaseTalentTables);
                if (ht.WyrdTalentTable != null) tables.Add(ht.WyrdTalentTable);
                if (ht.SarrasTalentTable != null) tables.Add(ht.SarrasTalentTable);

                foreach (var table in tables)
                {
                    var nodes = new List<object>();
                    foreach (var t in table.talents)
                    {
                        try
                        {
                            int max = 0; try { max = t.MaxLevel; } catch { }
                            var perRank = new List<string>();
                            for (int r = 1; r <= max; r++)
                            {
                                try { perRank.Add(t.Template.GetLevel(r).Description(t, r)); } catch { perRank.Add("?"); }
                            }
                            string subtreeEnum = "", subtreeDisplay = "";
                            try {
                                var bt = t.TalentTreeBranchType;
                                if (bt != null) { subtreeEnum = bt.EnumName; subtreeDisplay = bt.DisplayName; }
                            } catch { }
                            string nameLoc = "";
                            try { nameLoc = t.Template.Name; } catch { }
                            nodes.Add(new
                            {
                                name = nameLoc,
                                templateRef = t.Template?.name,
                                parentRef = t.Parent?.name,
                                subtreeEnum,
                                subtreeDisplay,
                                level = t.Level,
                                maxLevel = max,
                                descByRank = perRank,
                            });
                        }
                        catch { }
                    }
                    trees.Add(new
                    {
                        tree = table.TreeTemplate?.name,
                        pointsSpent = table.PointsSpent,
                        nodes,
                    });
                }
                return JsonConvert.SerializeObject(trees, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message });
            }
        }
    }
}
