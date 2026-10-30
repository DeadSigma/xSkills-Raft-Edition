using System;
using System.Collections.Generic;
using UnityEngine;

namespace xSkills.Skills
{
    internal static class FishingAbilityBalance
    {
        internal const int MaximumTiers = 10;
        private const string Prefix = "xskills_fishing_perk_v1_";

        private sealed class Defaults
        {
            internal int RequiredLevel;
            internal int MaxTier;
            internal int ValuesPerTier;
            internal int[] Values;
        }

        private static readonly Dictionary<string, Defaults> Original =
            new Dictionary<string, Defaults>(StringComparer.OrdinalIgnoreCase);

        private static Defaults Remember(AbilityData ability)
        {
            Defaults result;
            if (Original.TryGetValue(ability.Id, out result)) return result;
            result = new Defaults
            {
                RequiredLevel = ability.RequiredLevel,
                MaxTier = ability.MaxTier,
                ValuesPerTier = ability.ValuesPerTier,
                Values = (int[])ability.Values.Clone()
            };
            Original.Add(ability.Id, result);
            return result;
        }

        private static string Key(AbilityData ability, string suffix)
        {
            return Prefix + ability.Id + "_" + suffix;
        }

        internal static void Apply(SkillData skill)
        {
            if (skill == null || skill.Id != "fishing") return;
            foreach (AbilityData ability in skill.Abilities)
            {
                Defaults defaults = Remember(ability);
                ability.RequiredLevel = Mathf.Clamp(PlayerPrefs.GetInt(Key(ability, "min"),
                    defaults.RequiredLevel), 1, skill.MaxLevel);
                int tiers = Mathf.Clamp(PlayerPrefs.GetInt(Key(ability, "tiers"), defaults.MaxTier),
                    1, MaximumTiers);
                Resize(ability, tiers, defaults);
                for (int tier = 1; tier <= tiers; tier++)
                    for (int index = 0; index < defaults.ValuesPerTier; index++)
                    {
                        int at = (tier - 1) * defaults.ValuesPerTier + index;
                        int value = PlayerPrefs.GetInt(Key(ability, "v" + tier + "_" + index),
                            ability.Values[at]);
                        ability.Values[at] = Normalize(value);
                    }
                if (ability.PreviewTier > tiers) ability.PreviewTier = tiers;
            }
        }

        private static int Normalize(int value) { return Mathf.Clamp(value, 0, 1000); }

        private static void Resize(AbilityData ability, int tiers, Defaults defaults)
        {
            int[] oldValues = ability.Values;
            int oldTiers = ability.MaxTier;
            int[] values = new int[tiers * defaults.ValuesPerTier];
            for (int tier = 1; tier <= tiers; tier++)
                for (int index = 0; index < defaults.ValuesPerTier; index++)
                {
                    int at = (tier - 1) * defaults.ValuesPerTier + index;
                    if (tier <= oldTiers && at < oldValues.Length)
                        values[at] = oldValues[at];
                    else if (tier <= defaults.MaxTier)
                        values[at] = defaults.Values[at];
                    else
                    {
                        int last = defaults.Values[(defaults.MaxTier - 1) * defaults.ValuesPerTier + index];
                        int step = defaults.MaxTier >= 2
                            ? last - defaults.Values[(defaults.MaxTier - 2) * defaults.ValuesPerTier + index] : last;
                        values[at] = Normalize(last + step * (tier - defaults.MaxTier));
                    }
                }
            ability.MaxTier = tiers;
            ability.Values = values;
        }

        internal static void SetMinimum(AbilityData ability, int value)
        {
            if (ability == null) return;
            SkillData skill = SkillCatalog.Find("fishing");
            ability.RequiredLevel = Mathf.Clamp(value, 1, skill != null ? skill.MaxLevel : 25);
            PlayerPrefs.SetInt(Key(ability, "min"), ability.RequiredLevel);
            PlayerPrefs.Save();
            Revalidate();
        }

        internal static void SetTierCount(AbilityData ability, int count)
        {
            if (ability == null) return;
            Defaults defaults = Remember(ability);
            Resize(ability, Mathf.Clamp(count, 1, MaximumTiers), defaults);
            for (int tier = 1; tier <= ability.MaxTier; tier++)
                for (int index = 0; index < defaults.ValuesPerTier; index++)
                {
                    int at = (tier - 1) * defaults.ValuesPerTier + index;
                    ability.Values[at] = Normalize(PlayerPrefs.GetInt(
                        Key(ability, "v" + tier + "_" + index), ability.Values[at]));
                }
            if (ability.PreviewTier > ability.MaxTier) ability.PreviewTier = ability.MaxTier;
            PlayerPrefs.SetInt(Key(ability, "tiers"), ability.MaxTier);
            PlayerPrefs.Save();
            Revalidate();
        }

        internal static void SetValue(AbilityData ability, int tier, int index, int value)
        {
            if (ability == null || tier < 1 || tier > ability.MaxTier ||
                index < 0 || index >= ability.ValuesPerTier) return;
            ability.Values[(tier - 1) * ability.ValuesPerTier + index] = Normalize(value);
            PlayerPrefs.SetInt(Key(ability, "v" + tier + "_" + index), Normalize(value));
            PlayerPrefs.Save();
            SkillCatalog.NotifyChanged();
        }

        internal static void RestoreDefaults(AbilityData ability)
        {
            if (ability == null) return;
            Defaults defaults = Remember(ability);
            PlayerPrefs.DeleteKey(Key(ability, "min"));
            PlayerPrefs.DeleteKey(Key(ability, "tiers"));
            for (int tier = 1; tier <= MaximumTiers; tier++)
                for (int index = 0; index < defaults.ValuesPerTier; index++)
                    PlayerPrefs.DeleteKey(Key(ability, "v" + tier + "_" + index));
            ability.RequiredLevel = defaults.RequiredLevel;
            ability.MaxTier = defaults.MaxTier;
            ability.Values = (int[])defaults.Values.Clone();
            if (ability.PreviewTier > ability.MaxTier) ability.PreviewTier = ability.MaxTier;
            PlayerPrefs.Save();
            Revalidate();
        }

        private static void Revalidate()
        {
            SkillData skill = SkillCatalog.Find("fishing");
            if (skill != null) skill.RevalidateAbilityTiers();
            SkillCatalog.NotifyChanged();
        }

        internal static void Clear() { Original.Clear(); }
    }
}
