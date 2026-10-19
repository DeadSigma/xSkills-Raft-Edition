using HMLLibrary;
using UnityEngine;

namespace xSkills
{
    internal static class GameInputGuard
    {
        public static string LastBlockReason { get; private set; }

        public static bool CanToggle(bool menuAlreadyOpen)
        {
            LastBlockReason = null;

            if (KeybindInterface.AssigningKey)
                return Block("key assignment is active");
            if (ConsoleInputCapture.IsTyping())
                return Block("text input has focus");

            if (menuAlreadyOpen) return true;
            if (!RAPI.IsCurrentSceneGame())
                return Block("not in a game scene");
            if (RAPI.GetLocalPlayer() == null)
                return Block("local player unavailable");
            if (CanvasHelper.ActiveMenu != MenuType.None)
                return Block("vanilla menu active: " + CanvasHelper.ActiveMenu);

            // Курсор не используется как признак открытого меню - его состояние может изменяться другими модами
            return true;
        }

        private static bool Block(string reason)
        {
            LastBlockReason = reason;
            return false;
        }
    }
}
