using HMLLibrary;
using HarmonyLib;
using UnityEngine;
using FMODUnity;
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
                harmony.CreateClassProcessor(typeof(NotebookForecastArrowsPatch)).Patch();
                harmony.CreateClassProcessor(typeof(NotebookForecastVanillaBookmarkPatch)).Patch();
                harmony.CreateClassProcessor(typeof(NotebookForecastVanillaPagePatch)).Patch();
                harmony.CreateClassProcessor(typeof(NotebookForecastSetPagePatch)).Patch();
                harmony.CreateClassProcessor(typeof(NotebookForecastPageActivationPatch)).Patch();
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
            return skill != null && skill.Level >= 2 && ability != null && ability.PreviewTier > 0;
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

        internal static void OverrideVanillaArrows(NoteBookUI target,
            GameObject left, GameObject right, GameObject leftConsole, GameObject rightConsole)
        {
            if (attachedBook == target && attachedView != null)
                attachedView.OverrideVanillaArrows(left, right, leftConsole, rightConsole);
        }

        internal static void OnVanillaNavigation(NoteBookUI target)
        {
            if (attachedBook == target && attachedView != null)
                attachedView.OnVanillaNavigation();
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
        private static bool Prefix(NoteBookUI __instance, StudioEventEmitter ___soundemitter_FlipPage)
        {
            NotebookForecastUI view = __instance.GetComponent<NotebookForecastUI>();
            if (view == null || !view.HandleVanillaPageFlip(-1)) return true;
            if (___soundemitter_FlipPage != null) ___soundemitter_FlipPage.Play();
            return false;
        }
    }

    [HarmonyPatch(typeof(NoteBookUI), "FlipPageRight")]
    internal static class NotebookForecastRightPatch
    {
        private static bool Prefix(NoteBookUI __instance, StudioEventEmitter ___soundemitter_FlipPage)
        {
            NotebookForecastUI view = __instance.GetComponent<NotebookForecastUI>();
            if (view == null || !view.HandleVanillaPageFlip(1)) return true;
            if (___soundemitter_FlipPage != null) ___soundemitter_FlipPage.Play();
            return false;
        }
    }

    [HarmonyPatch(typeof(NoteBookUI), "Update")]
    internal static class NotebookForecastArrowsPatch
    {
        private static void Postfix(NoteBookUI __instance,
            GameObject ___leftPageFlipImage, GameObject ___rightPageFlipImage,
            GameObject ___leftPageFlipImageConsole, GameObject ___rightPageFlipImageConsole)
        {
            NotebookForecastSystem.OverrideVanillaArrows(__instance,
                ___leftPageFlipImage, ___rightPageFlipImage,
                ___leftPageFlipImageConsole, ___rightPageFlipImageConsole);
        }
    }
}

namespace xSkills.Systems
{
    [HarmonyPatch(typeof(NoteBookUI), "FlipToPageNetwork")]
    internal static class NotebookForecastVanillaBookmarkPatch
    {
        private static void Prefix(NoteBookUI __instance)
        {
            NotebookForecastSystem.OnVanillaNavigation(__instance);
        }
    }

    [HarmonyPatch(typeof(NoteBookUI), "FlipToPageLocally")]
    internal static class NotebookForecastVanillaPagePatch
    {
        private static void Prefix(NoteBookUI __instance)
        {
            NotebookForecastSystem.OnVanillaNavigation(__instance);
        }
    }

    [HarmonyPatch(typeof(NoteBookUI), "SetPageIndex")]
    internal static class NotebookForecastSetPagePatch
    {
        private static void Prefix(NoteBookUI __instance)
        {
            NotebookForecastSystem.OnVanillaNavigation(__instance);
        }
    }
}

namespace xSkills.Systems
{
    [HarmonyPatch(typeof(NoteBookPage), "SetPageActive")]
    internal static class NotebookForecastPageActivationPatch
    {
        private static void Postfix(NoteBookPage __instance, bool value)
        {
            if (!value) return;
            NoteBookUI notebook = __instance.GetComponentInParent<NoteBookUI>();
            if (notebook != null) NotebookForecastSystem.OnVanillaNavigation(notebook);
        }
    }
}
