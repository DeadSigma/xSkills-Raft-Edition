using System;
using UnityEngine;

namespace xSkills.Skills
{
    internal static class SkillExperienceSettings
    {
        private const string Prefix = "xskills_skill_experience_v1_";
        internal const float DefaultSurvivalIntervalMinutes = 1440f;
        internal const float DefaultSurvivalExperience = 1f;
        internal const float MinIntervalMinutes = 1f;
        internal const float MaxIntervalMinutes = 10080f;
        internal const float MinExperience = 0.1f;
        internal const float MaxExperience = 100f;

        internal static float IntervalMinutes(string skillId)
        {
            float fallback = DefaultSurvivalIntervalMinutes;
            float value = PlayerPrefs.GetFloat(Prefix + skillId + "_interval", fallback);
            return Valid(value) ? Mathf.Clamp(Mathf.Round(value), MinIntervalMinutes, MaxIntervalMinutes) : fallback;
        }

        internal static float Experience(string skillId)
        {
            float fallback = DefaultSurvivalExperience;
            float value = PlayerPrefs.GetFloat(Prefix + skillId + "_amount", fallback);
            return Valid(value) ? Mathf.Clamp(Mathf.Round(value * 10f) / 10f, MinExperience, MaxExperience) : fallback;
        }

        internal static void SetIntervalMinutes(string skillId, float minutes)
        {
            if (!Valid(minutes)) return;
            PlayerPrefs.SetFloat(Prefix + skillId + "_interval",
                Mathf.Clamp(Mathf.Round(minutes), MinIntervalMinutes, MaxIntervalMinutes));
            PlayerPrefs.Save();
        }

        internal static void SetExperience(string skillId, float amount)
        {
            if (!Valid(amount)) return;
            PlayerPrefs.SetFloat(Prefix + skillId + "_amount",
                Mathf.Clamp(Mathf.Round(amount * 10f) / 10f, MinExperience, MaxExperience));
            PlayerPrefs.Save();
        }

        internal static void RestoreDefaults(string skillId)
        {
            PlayerPrefs.DeleteKey(Prefix + skillId + "_interval");
            PlayerPrefs.DeleteKey(Prefix + skillId + "_amount");
            PlayerPrefs.Save();
        }

        private static bool Valid(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
