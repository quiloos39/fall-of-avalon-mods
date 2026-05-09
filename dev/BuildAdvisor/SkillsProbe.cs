using System;
using System.Collections.Generic;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Skills;
using Awaken.TG.Main.Skills;
using Newtonsoft.Json;

namespace BuildAdvisor
{
    // Dump CharacterSkills (separate from talents): learned, equipped.
    public static class SkillsProbe
    {
        public static string Run()
        {
            try
            {
                var hero = Hero.Current;
                if (hero == null) return "{}";

                var characterSkills = hero.Element<CharacterSkills>();
                var learned = new List<object>();
                if (characterSkills != null)
                {
                    foreach (var skill in characterSkills.AllSkills())
                    {
                        try
                        {
                            learned.Add(new
                            {
                                graphName = skill.Graph?.DebugName,
                                displayName = skill.DisplayName,
                                isLearned = skill.IsLearned,
                                isEquipped = skill.IsEquipped,
                                tags = skill.Tags,
                            });
                        }
                        catch { }
                    }
                }
                return JsonConvert.SerializeObject(new { count = learned.Count, learned }, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message, stack = e.StackTrace });
            }
        }
    }
}
