using HarmonyLib;
using Awaken.TG.Main.Heroes;

namespace BetterSortingCrafting
{
    // While any search field has focus, suppress the game's keyboard /
    // registered-input dispatch so typing letters doesn't also fire game
    // keybinds (R for sort, F for filter, etc.). Mirrors the technique used
    // by Better UI — without these prefixes, every keystroke in the search
    // box doubles as a game action.
    [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.HandleKeyboard))]
    internal static class PlayerInput_HandleKeyboard_Patch
    {
        static bool Prefix() => !SearchInputFocusBlocker.AnyFocused;
    }

    [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.HandleRegisteredPlayerInputs))]
    internal static class PlayerInput_HandleRegisteredPlayerInputs_Patch
    {
        static bool Prefix() => !SearchInputFocusBlocker.AnyFocused;
    }
}
