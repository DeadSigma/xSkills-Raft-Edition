using System;
using HarmonyLib;
using UnityEngine;
using HMLLibrary;
using xSkills.Skills;
using xSkills.Services;

namespace xSkills.Systems
{
    internal static class FishingIntegration
    {
        private const string HarmonyId = "xskills.fishing.integrated";
        private static Harmony harmony;
        internal static void Initialize()
        {
            if (harmony != null) return;
            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.CreateClassProcessor(typeof(FishingBiteTimePatch)).Patch();
                harmony.CreateClassProcessor(typeof(FishingCatchExperiencePatch)).Patch();
            }
            catch (Exception ex)
            {
                Debug.LogError("[xSkills] Fishing patches: " + ex);
                if (harmony != null) harmony.UnpatchAll(HarmonyId);
                harmony = null;
            }
        }

        internal static int FishingTier
        {
            get
            {
                SkillData skill = SkillCatalog.Find("fishing");
                AbilityData ability = skill != null ? skill.FindAbility("angler") : null;
                return ability != null ? ability.PreviewTier : 0;
            }
        }

        internal static float DelayMultiplier
        {
            get
            {
                WaterSurvivalSettings s = WaterSurvivalSettings.Current;
                if (s == null) return 1f;
                return Mathf.Max(.1f, s.FishingDelayMultiplier /
                    (1f + FishingTier * s.FishingSpeedPerRank));
            }
        }

        internal static void AwardCatch(Network_Player owner)
        {
            if (owner == null || !owner.IsLocalPlayer || !SkillProgressStorage.IsWorldBound) return;
            SkillData skill = SkillCatalog.Find("fishing");
            WaterSurvivalSettings s = WaterSurvivalSettings.Current;
            if (skill == null || s == null || skill.Level >= skill.MaxLevel) return;
            skill.AddExperience(s.FishingExperiencePerCatch);
        }

        internal static void Dispose()
        {
            if (harmony != null) harmony.UnpatchAll(HarmonyId);
            harmony = null;
        }
    }

    [HarmonyPatch(typeof(Bobber), "FishOnHook", new[] { typeof(float) })]
    internal static class FishingBiteTimePatch
    {
        private static void Prefix(ref float timeDelay)
        {
            if (!SkillProgressStorage.IsWorldBound) return;
            timeDelay = Mathf.Max(.1f, timeDelay * FishingIntegration.DelayMultiplier);
        }
    }

    [HarmonyPatch(typeof(FishingRod), "PullItemsFromSea")]
    internal static class FishingCatchExperiencePatch
    {
        private static void Postfix(FishingRod __instance, Network_Player ___playerNetwork, Bobber ___bobber)
        {
            if (__instance == null || ___bobber == null || ___bobber.FishIsOnHook) return;
            FishingIntegration.AwardCatch(___playerNetwork);
        }
    }
}
