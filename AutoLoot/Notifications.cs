using System;

using Awaken.TG.Main.UI.HUD.AdvancedNotifications;
using Awaken.TG.Main.UI.HUD.AdvancedNotifications.MiddleScreen;
using Awaken.TG.Main.UI.HUD.AdvancedNotifications.MiddleScreen.FancyPanel;

namespace AutoLoot
{
    internal static class Notifications
    {
        public static void ShowToggle(bool enabled)
        {
            string text = enabled ? "Auto Loot: ON" : "Auto Loot: OFF";
            try
            {
                NotificationUtils.PushExplicitly<LowerMiddleScreenNotificationBuffer, LowerInfoNotification>(
                    new LowerInfoNotification(text, typeof(VLowerInfoNotification)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Failed to push toggle notification ({e.GetType().Name}): {e.Message}");
            }
        }
    }
}
