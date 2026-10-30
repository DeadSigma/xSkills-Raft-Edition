using System;
using HMLLibrary;
using UnityEngine;
using UnityEngine.AzureSky;
using xSkills.Services;
using xSkills.Skills;

namespace xSkills.Systems
{
    internal static class SurvivalExperienceSystem
    {
        private static Guid trackedWorld;
        private static ulong trackedPlayer;
        private static float elapsedGameMinutes;
        private static float saveTimer;

        internal static void Tick(float deltaTime)
        {
            if (!SkillProgressStorage.IsWorldBound) return;
            try
            {
                if (!RAPI.IsCurrentSceneGame() || SaveAndLoad.IsGameLoading) return;
                Guid world = SaveAndLoad.WorldGuid;
                Network_Player player = RAPI.GetLocalPlayer();
                if (world == Guid.Empty || player == null || player.Stats == null || player.steamID == 0UL) return;

                if (trackedWorld != world || trackedPlayer != player.steamID)
                {
                    trackedWorld = world;
                    trackedPlayer = player.steamID;
                    elapsedGameMinutes = 0f;
                    saveTimer = 0f;
                }

                SkillData survival = SkillCatalog.Find("survival");
                if (survival == null || survival.Level >= survival.MaxLevel || player.Stats.IsDead) return;
                if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;

                AzureSkyController sky = ComponentManager<AzureSkyController>.Value;
                if (sky == null || sky.timeOfDay == null) return;
                float daySeconds = sky.timeOfDay.dayCycle * 60f;
                if (daySeconds <= 0f || float.IsNaN(daySeconds) || float.IsInfinity(daySeconds)) return;

                float interval = SkillExperienceSettings.IntervalMinutes("survival");
                float award = SkillExperienceSettings.Experience("survival");
                float elapsed = Mathf.Min(deltaTime, 1f);
                elapsedGameMinutes += elapsed * 1440f / daySeconds;
                saveTimer += elapsed;

                if (elapsedGameMinutes >= interval)
                {
                    int awards = Mathf.Min(100, Mathf.FloorToInt(elapsedGameMinutes / interval));
                    elapsedGameMinutes -= awards * interval;
                    survival.AddExperience(award * awards);
                    saveTimer = 0f;
                    SkillProgressStorage.MarkExternalChanged(false);
                }
                else if (saveTimer >= 10f)
                {
                    saveTimer = 0f;
                    SkillProgressStorage.MarkExternalChanged(false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Survival XP update failed: " + ex.Message);
            }
        }

        internal static float CollectProgress()
        {
            if (trackedWorld != SaveAndLoad.WorldGuid || trackedWorld == Guid.Empty) return 0f;
            return Mathf.Max(0f, elapsedGameMinutes);
        }

        internal static void RestoreProgress(float minutes)
        {
            trackedWorld = SaveAndLoad.WorldGuid;
            Network_Player player = null;
            try { player = RAPI.GetLocalPlayer(); } catch { }
            trackedPlayer = 0UL;
            if (player != null) trackedPlayer = player.steamID;
            elapsedGameMinutes = !float.IsNaN(minutes) && !float.IsInfinity(minutes)
                ? Mathf.Clamp(minutes, 0f, SkillExperienceSettings.IntervalMinutes("survival") - 0.0001f) : 0f;
            saveTimer = 0f;
        }

        internal static void FlushProgress()
        {
            if (trackedWorld != Guid.Empty && elapsedGameMinutes > 0f)
                SkillProgressStorage.MarkExternalChanged(false);
        }

        internal static void SettingsChanged()
        {
            elapsedGameMinutes = 0f;
            saveTimer = 0f;
            SkillProgressStorage.MarkExternalChanged(false);
        }

        internal static void Reset()
        {
            trackedWorld = Guid.Empty;
            trackedPlayer = 0UL;
            elapsedGameMinutes = 0f;
            saveTimer = 0f;
        }
    }
}
