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

            ResourceLoader.Configure(this);
            SoundPlayer.Initialize();
            BodyTemperatureSystem.Initialize();
            ui = new SkillGraphicalUI();
            ui.Initialize();
            notifications = SkillNotifications.Create();
            try { NativeSkillHotkey.Install(); }
            catch (Exception ex) { Debug.LogError("[xSkills] Hotkey initialization: " + ex); }
        }

        public void Update()
        {
            NativeSkillHotkey.Tick();
            BodyTemperatureSystem.Tick(Time.deltaTime, Time.unscaledDeltaTime);
            if (ui == null) return;
            if (NativeSkillHotkey.WasPressed())
            {
                if (GameInputGuard.CanToggle(ui.IsOpen))
                {
                    NativeSkillHotkey.MarkHandled();
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
            if (ui != null) ui.Dispose();
            ui = null;
            if (notifications != null) notifications.Dispose();
            notifications = null;
            NativeSkillHotkey.Uninstall();
            BodyTemperatureSystem.Dispose();
            SoundPlayer.Shutdown();
            Localizer.Reset();
            ResourceLoader.Clear();
            SkillCatalog.Clear();
            AdminCommands.Active = false;
        }
    }
}
