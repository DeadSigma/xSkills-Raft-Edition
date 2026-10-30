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

        internal static float DelayMultiplier
        {
            get
            {
                WaterSurvivalSettings s = WaterSurvivalSettings.Current;
                if (s == null) return 1f;
                SkillData skill = SkillCatalog.Find("fishing");
                AbilityData angler = skill != null ? skill.FindAbility("angler") : null;
                float bonus = 0f;
                if (angler != null && angler.PreviewTier > 0 && angler.ValuesPerTier > 0)
                {
                    int tier = Mathf.Clamp(angler.PreviewTier, 1, angler.MaxTier);
                    int index = (tier - 1) * angler.ValuesPerTier;
                    if (index >= 0 && index < angler.Values.Length)
                        bonus = Mathf.Max(0f, angler.Values[index]) / 100f;
                }
                // Сохранён коэффициент старых настроек рыбалки
                float strength = s.FishingSpeedPerRank / .2f;
                return Mathf.Max(.1f, s.FishingDelayMultiplier / (1f + bonus * strength));
            }
        }

        internal static void AwardCatch(Network_Player owner)
        {
            if (owner == null || !owner.IsLocalPlayer || !SkillProgressStorage.IsWorldBound) return;
            SkillData skill = SkillCatalog.Find("fishing");
            if (skill == null || skill.Level >= skill.MaxLevel) return;
            skill.AddExperience(Fishing.ExperiencePerCatch);
        }

        internal static void Dispose()
        {
            if (harmony != null) harmony.UnpatchAll(HarmonyId);
            harmony = null;
            FishingAbilityBalance.Clear();
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
