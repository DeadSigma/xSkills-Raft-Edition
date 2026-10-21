using System;
using System.Collections.Generic;
using UnityEngine;

namespace xSkills.Skills
{
    internal static class SurvivalAbilityBalance
    {
        internal const int MaximumTiers = 10;
        private const string Prefix = "xskills_survival_perk_v1_";
        private sealed class Defaults
        {
            internal int MinLevel;
            internal int TierCount;
            internal int[] Values;
            internal int PerTier;
        }

        private static readonly Dictionary<string, Defaults> Original =
            new Dictionary<string, Defaults>(StringComparer.OrdinalIgnoreCase);

        internal static void Apply(SkillData skill)
        {
            if (skill == null || skill.Id != "survival") return;
            foreach (AbilityData ability in skill.Abilities)
            {
                Defaults def = Remember(ability);
                int min = Mathf.Clamp(PlayerPrefs.GetInt(Key(ability, "min"), def.MinLevel), 1, skill.MaxLevel);
                int tiers = Mathf.Clamp(PlayerPrefs.GetInt(Key(ability, "tiers"), def.TierCount), 1, MaximumTiers);
                ability.RequiredLevel = min;
                Resize(ability, tiers, def);
                for (int tier = 1; tier <= tiers; tier++)
                    for (int value = 0; value < def.PerTier; value++)
                    {
                        int index = (tier - 1) * def.PerTier + value;
                        ability.Values[index] = NormalizeValue(ability, value,
                            PlayerPrefs.GetInt(Key(ability, "v" + tier + "_" + value), ability.Values[index]));
                    }
                if (ability.PreviewTier > tiers) ability.PreviewTier = tiers;
            }
        }

        private static Defaults Remember(AbilityData ability)
        {
            Defaults def;
            if (Original.TryGetValue(ability.Id, out def)) return def;
            def = new Defaults
            {
                MinLevel = ability.RequiredLevel,
                TierCount = ability.MaxTier,
                Values = (int[])ability.Values.Clone(),
                PerTier = ability.ValuesPerTier
            };
            Original[ability.Id] = def;
            return def;
        }

        private static string Key(AbilityData ability, string suffix) { return Prefix + ability.Id + "_" + suffix; }

        private static void Resize(AbilityData ability, int tierCount, Defaults def)
        {
            int[] old = ability.Values;
            int oldTiers = ability.MaxTier;
            int[] resized = new int[tierCount * def.PerTier];
            for (int tier = 1; tier <= tierCount; tier++)
                for (int value = 0; value < def.PerTier; value++)
                {
                    int index = (tier - 1) * def.PerTier + value;
                    if (tier <= oldTiers && index < old.Length)
                        resized[index] = old[index];
                    else if (tier <= def.TierCount)
                        resized[index] = def.Values[index];
                    else
                    {
                        int last = def.Values[(def.TierCount - 1) * def.PerTier + value];
                        int step = def.TierCount >= 2
                            ? last - def.Values[(def.TierCount - 2) * def.PerTier + value]
                            : last;
                        resized[index] = Mathf.Clamp(last + step * (tier - def.TierCount), -10000, 10000);
                        resized[index] = NormalizeValue(ability, value, resized[index]);
                    }
                }
            ability.MaxTier = tierCount;
            ability.Values = resized;
        }

        internal static void SetMinimum(AbilityData ability, int value)
        {
            if (ability == null) return;
            SkillData survival = SkillCatalog.Find("survival");
            ability.RequiredLevel = Mathf.Clamp(value, 1, survival != null ? survival.MaxLevel : 25);
            PlayerPrefs.SetInt(Key(ability, "min"), ability.RequiredLevel);
            PlayerPrefs.Save();
            RevalidateProgress();
        }

        internal static void SetTierCount(AbilityData ability, int value)
        {
            if (ability == null) return;
            int previousTier = ability.PreviewTier;
            Resize(ability, Mathf.Clamp(value, 1, MaximumTiers), Remember(ability));
            for (int tier = 1; tier <= ability.MaxTier; tier++)
                for (int index = 0; index < ability.ValuesPerTier; index++)
                {
                    int at = (tier - 1) * ability.ValuesPerTier + index;
                    ability.Values[at] = NormalizeValue(ability, index,
                        PlayerPrefs.GetInt(Key(ability, "v" + tier + "_" + index), ability.Values[at]));
                }
            if (ability.PreviewTier > ability.MaxTier) ability.PreviewTier = ability.MaxTier;
            PlayerPrefs.SetInt(Key(ability, "tiers"), ability.MaxTier);
            PlayerPrefs.Save();
            RevalidateProgress(previousTier != ability.PreviewTier);
        }

        internal static void SetValue(AbilityData ability, int tier, int index, int value)
        {
            if (ability == null || tier < 1 || tier > ability.MaxTier || index < 0 || index >= ability.ValuesPerTier) return;
            int selected = NormalizeValue(ability, index, value);
            ability.Values[(tier - 1) * ability.ValuesPerTier + index] = selected;
            PlayerPrefs.SetInt(Key(ability, "v" + tier + "_" + index), selected);
            PlayerPrefs.Save();
        }

        internal static void RestoreDefaults(AbilityData ability)
        {
            if (ability == null) return;
            Defaults def = Remember(ability);
            int previousTier = ability.PreviewTier;
            PlayerPrefs.DeleteKey(Key(ability, "min"));
            PlayerPrefs.DeleteKey(Key(ability, "tiers"));
            for (int tier = 1; tier <= MaximumTiers; tier++)
                for (int value = 0; value < def.PerTier; value++)
                    PlayerPrefs.DeleteKey(Key(ability, "v" + tier + "_" + value));
            ability.RequiredLevel = def.MinLevel;
            ability.MaxTier = def.TierCount;
            ability.Values = (int[])def.Values.Clone();
            if (ability.PreviewTier > ability.MaxTier) ability.PreviewTier = ability.MaxTier;
            PlayerPrefs.Save();
            RevalidateProgress(previousTier != ability.PreviewTier);
        }

        // Пределы соответствуют фактически используемым игровым формулам
        private static int NormalizeValue(AbilityData ability, int index, int value)
        {
            if (ability.Id == "forecast") return Mathf.Clamp(value, 1, 30);
            if (ability.Id == "diver") return Mathf.Clamp(value, 0, 95);
            if (ability.Id == "featherfall" && index == 1) return Mathf.Clamp(value, 0, 100);
            if (ability.Id == "meatshield" && index == 0) return Mathf.Clamp(value, 0, 100);
            if (ability.Id == "sprinter" && index == 1) return Mathf.Clamp(value, 0, 500);
            return Mathf.Clamp(value, 0, 10000);
        }

        private static void RevalidateProgress(bool changed = false)
        {
            SkillData skill = SkillCatalog.Find("survival");
            if (skill != null && (skill.RevalidateAbilityTiers() || changed))
                SkillCatalog.NotifyChanged();
        }

        internal static void Clear() { Original.Clear(); }
    }
}
