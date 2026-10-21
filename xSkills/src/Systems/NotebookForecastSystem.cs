using HMLLibrary;
using HarmonyLib;
using UnityEngine;
using xSkills.GUI;
using xSkills.Skills;

namespace xSkills.Systems
{
    internal static class NotebookForecastSystem
    {
        private const float PollInterval = 0.75f;
        private const string HarmonyId = "xskills.notebookforecast";
        private static Harmony harmony;
        private static float nextPoll;
        private static NoteBookUI attachedBook;
        private static NotebookForecastUI attachedView;

        internal static void Initialize()
        {
            if (harmony != null) return;
            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.CreateClassProcessor(typeof(NotebookForecastLeftPatch)).Patch();
                harmony.CreateClassProcessor(typeof(NotebookForecastRightPatch)).Patch();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[xSkills] Notebook forecast page patches: " + ex.Message);
                if (harmony != null) harmony.UnpatchAll(HarmonyId);
                harmony = null;
            }
        }

        internal static bool IsUnlocked()
        {
            SkillData skill = SkillCatalog.Find("survival");
            AbilityData ability = skill == null ? null : skill.FindAbility("forecast");
            return skill != null && ability != null && skill.Level >= ability.RequiredLevel && ability.PreviewTier > 0;
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + PollInterval;
            NoteBookUI notebook = null;
            if (RAPI.IsCurrentSceneGame() && Raft_Network.WorldHasBeenRecieved)
            {
                Network_Player player = RAPI.GetLocalPlayer();
                if (player != null) notebook = player.NoteBookUI;
            }
            if (notebook != attachedBook || attachedView == null)
            {
                ClearView();
                if (notebook != null)
                {
                    attachedBook = notebook;
                    attachedView = notebook.GetComponent<NotebookForecastUI>();
                    if (attachedView == null)
                        attachedView = notebook.gameObject.AddComponent<NotebookForecastUI>();
                    attachedView.Bind(notebook);
                }
            }
            if (attachedView != null) attachedView.Sync(IsUnlocked());
        }

        private static void ClearView()
        {
            if (attachedView != null) UnityEngine.Object.Destroy(attachedView);
            attachedView = null;
            attachedBook = null;
        }

        internal static void Dispose()
        {
            ClearView();
            if (harmony != null) harmony.UnpatchAll(HarmonyId);
            harmony = null;
            nextPoll = 0f;
        }
    }

    [HarmonyPatch(typeof(NoteBookUI), "FlipPageLeft")]
    internal static class NotebookForecastLeftPatch
    {
        private static bool Prefix(NoteBookUI __instance)
        {
            NotebookForecastUI view = __instance.GetComponent<NotebookForecastUI>();
            return view == null || !view.HandleVanillaPageFlip(-1);
        }
    }

    [HarmonyPatch(typeof(NoteBookUI), "FlipPageRight")]
    internal static class NotebookForecastRightPatch
    {
        private static bool Prefix(NoteBookUI __instance)
        {
            NotebookForecastUI view = __instance.GetComponent<NotebookForecastUI>();
            return view == null || !view.HandleVanillaPageFlip(1);
        }
    }
}
