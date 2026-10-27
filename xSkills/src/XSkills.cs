using System;
using UnityEngine;
using HMLLibrary;
using RaftModLoader;
using xSkills.GUI;
using xSkills.Skills;

using xSkills.Services;
using xSkills.Systems;
namespace xSkills
{
    public sealed class XSkills : Mod
    {
        private SkillGraphicalUI ui;
        private SkillNotifications notifications;


        public void Start()
        {
            AdminCommands.Active = true;
            SkillCatalog.Create();
            SkillProgressStorage.Initialize();

            ResourceLoader.Configure(this);
            SoundPlayer.Initialize();
            BodyTemperatureSystem.Initialize();
            SurvivalPerkSystem.Initialize();
            StrongBackSystem.Initialize();
            NotebookForecastSystem.Initialize();
            TimeControlSystem.Initialize();
            WaterSurvivalSystem.Initialize();
            FishingIntegration.Initialize();
            ui = new SkillGraphicalUI();
            ui.Initialize();
            notifications = SkillNotifications.Create();
            try { NativeSkillHotkey.Install(); }
            catch (Exception ex) { Debug.LogError("[xSkills] Hotkey initialization: " + ex); }
            // HUD запускается отдельно и не блокирует загрузку навыков
            try { TemperatureHud.Initialize(); }
            catch (Exception ex) { Debug.LogError("[xSkills] HUD initialization: " + ex); }
        }

        public void Update()
        {
            SkillProgressStorage.Tick();
            StrongBackSystem.Tick();
            SurvivalExperienceSystem.Tick(Time.deltaTime);
            WaterSurvivalSystem.Tick(Time.deltaTime);
            NativeSkillHotkey.Tick();
            BodyTemperatureSystem.Tick(Time.deltaTime, Time.unscaledDeltaTime);
            TemperatureHud.Tick(ui != null && ui.IsOpen);
            TimeControlSystem.Tick();
            NotebookForecastSystem.Tick();
            if (ui == null) return;
            if (NativeSkillHotkey.WasPressed())
            {
                if (GameInputGuard.CanToggle(ui.IsOpen))
                {
                    NativeSkillHotkey.MarkHandled();
                    if (TemperatureHud.IsEditing) TemperatureHud.EndEditing();
                    ui.Toggle();
                }
            }
            else if (ui.IsOpen && Input.GetKeyDown(KeyCode.Escape) && !ConsoleInputCapture.IsTyping() && !ui.TryClosePopup())
                ui.Close();
            ui.Tick(Time.unscaledDeltaTime);
        }

        [ConsoleCommand("level", "Manage skill levels")]
        public static string LevelCommand(string[] args)
        {
            return ExecuteCommand("level", args, AdminCommands.Level);
        }

        [ConsoleCommand("exp", "Manage skill experience")]
        public static string ExperienceCommand(string[] args)
        {
            return ExecuteCommand("exp", args, AdminCommands.Exp);
        }

        [ConsoleCommand("tier", "Manage ability tiers")]
        public static string TierCommand(string[] args)
        {
            return ExecuteCommand("tier", args, AdminCommands.Tier);
        }

        [ConsoleCommand("unlearnpoints", "Manage unlearn points")]
        public static string UnlearnPointsCommand(string[] args)
        {
            return ExecuteCommand("unlearnpoints", args, AdminCommands.UnlearnPoints);
        }

        [ConsoleCommand("bodytemp", "Set local body temperature (31-41 C)")]
        public static string BodyTemperatureCommand(string[] args)
        {
            return ExecuteCommand("bodytemp", args, BodyTemperatureSystem.Command);
        }

        private static string ExecuteCommand(string name, string[] args, Func<string[], string> handler)
        {
            try
            {
                SkillProgressStorage.EnsureLoaded();
                if (name != "bodytemp" && !SkillProgressStorage.IsWorldBound)
                    return "xSkills: мир ещё не загружен, повтори команду после загрузки";
                return handler(args);
            }
            catch (Exception ex)
            {
                Debug.LogError("[xSkills] Command " + name + " failed: " + ex);
                return "xSkills command error";
            }
        }

        public void OnModUnload()
        {
            TemperatureHud.Dispose();
            if (ui != null) ui.Dispose();
            ui = null;
            if (notifications != null) notifications.Dispose();
            notifications = null;
            NativeSkillHotkey.Uninstall();
            NotebookForecastSystem.Dispose();
            FishingIntegration.Dispose();
            SkillProgressStorage.Dispose();
            SurvivalExperienceSystem.Reset();
            WaterSurvivalSystem.Dispose();
            TimeControlSystem.Dispose();
            BodyTemperatureSystem.Dispose();
            SurvivalPerkSystem.Dispose();
            StrongBackSystem.Dispose();
            SoundPlayer.Shutdown();
            Localizer.Reset();
            ResourceLoader.Clear();
            SkillCatalog.Clear();
            AdminCommands.Active = false;
        }
    }
}
