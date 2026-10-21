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
        // За один полный игровой день начисляется 1 XP, как в оригинальном xSkills
        private const float SecondsBetweenAwards = 10f;

        private static Guid trackedWorld;
        private static float elapsedSeconds;
        private static float pendingExperience;

        internal static void Tick(float deltaTime)
        {
            if (!SkillProgressStorage.IsWorldBound)
            {
                Reset();
                return;
            }

            try
            {
                if (!RAPI.IsCurrentSceneGame() || SaveAndLoad.IsGameLoading)
                {
                    ResetAccumulation();
                    return;
                }

                Guid world = SaveAndLoad.WorldGuid;
                if (world == Guid.Empty)
                {
                    ResetAccumulation();
                    return;
                }

                if (trackedWorld != world)
                {
                    trackedWorld = world;
                    ResetAccumulation();
                }

                Network_Player player = RAPI.GetLocalPlayer();
                if (player == null || player.Stats == null || player.Stats.IsDead)
                {
                    ResetAccumulation();
                    return;
                }

                SkillData survival = SkillCatalog.Find("survival");
                if (survival == null || survival.Level >= survival.MaxLevel)
                {
                    ResetAccumulation();
                    return;
                }

                if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                    return;

                AzureSkyController sky = ComponentManager<AzureSkyController>.Value;
                if (sky == null || sky.timeOfDay == null)
                {
                    ResetAccumulation();
                    return;
                }

                // dayCycle задаётся в игровых минутах на полный день Raft
                float secondsPerGameDay = sky.timeOfDay.dayCycle * 60f;
                if (secondsPerGameDay < 1f || float.IsNaN(secondsPerGameDay) || float.IsInfinity(secondsPerGameDay))
                {
                    ResetAccumulation();
                    return;
                }

                float elapsed = Mathf.Min(deltaTime, 1f);
                pendingExperience += elapsed / secondsPerGameDay;
                elapsedSeconds += elapsed;

                if (elapsedSeconds < SecondsBetweenAwards) return;

                float earned = pendingExperience;
                ResetAccumulation();
                if (earned > 0f) survival.AddExperience(earned);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Survival XP update failed: " + ex.Message);
                ResetAccumulation();
            }
        }

        private static void ResetAccumulation()
        {
            elapsedSeconds = 0f;
            pendingExperience = 0f;
        }

        internal static void Reset()
        {
            trackedWorld = Guid.Empty;
            ResetAccumulation();
        }
    }
}
