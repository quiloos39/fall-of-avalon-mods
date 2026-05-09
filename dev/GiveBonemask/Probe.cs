using System.Linq;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Templates;
using Awaken.TG.MVC;

namespace GiveBonemask
{
    public static class Probe
    {
        public static string Run(string templateName, int amount)
        {
            var hero = Hero.Current;
            if (hero == null) return "no hero — load a save first";

            var tpl = World.Services.Get<TemplatesProvider>()
                .GetAllOfType<ItemTemplate>(TemplateTypeFlag.All)
                .FirstOrDefault(t => t.name == templateName);
            if (tpl == null) return $"template not found: {templateName}";

            tpl.ChangeQuantity(hero.Inventory, amount);
            return $"added {amount}x {tpl.name}";
        }
    }
}
