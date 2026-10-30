using System;
using System.Collections.Generic;
using UnityEngine;
using xSkills.Systems;

namespace xSkills.Skills
{
    internal static class CookingAbilityBalance
    {
        internal const int MaximumTiers = 10;
        private const string Prefix = "xskills_cooking_perk_v1_";

        private sealed class Defaults
        {
            internal int MinLevel;
            internal int TierCount;
            internal int[] Values;
            internal int PerTier;
        }

        private static readonly Dictionary<string, Defaults> Original =
            new Dictionary<string, Defaults>(StringComparer.OrdinalIgnoreCase);

        internal static void RememberDefaults(SkillData skill)
        {
            if (skill == null || skill.Id != "cooking") return;
            foreach (AbilityData ability in skill.Abilities) Remember(ability);
        }

        internal static void Apply(SkillData skill)
        {
            if (skill == null || skill.Id != "cooking") return;
            foreach (AbilityData ability in skill.Abilities)
            {
                Defaults defaults = Remember(ability);
                ability.RequiredLevel = Mathf.Clamp(
                    PlayerPrefs.GetInt(Key(ability, "min"), ability.RequiredLevel), 1, skill.MaxLevel);
                int tiers = Mathf.Clamp(PlayerPrefs.GetInt(Key(ability, "tiers"), defaults.TierCount),
                    1, TierLimit(ability));
                Resize(ability, tiers, defaults);
                for (int tier = 1; tier <= tiers; tier++)
                    for (int index = 0; index < defaults.PerTier; index++)
                    {
                        int at = (tier - 1) * defaults.PerTier + index;
                        int stored = PlayerPrefs.GetInt(Key(ability, "v" + tier + "_" + index),
                            ability.Values[at]);
                        ability.Values[at] = NormalizeValue(ability, tier, index, stored);
                    }
                NormalizeRelatedValues(ability);
                if (ability.PreviewTier > tiers) ability.PreviewTier = tiers;
            }
        }

        internal static void SetMinimum(AbilityData ability, int value)
        {
            if (ability == null) return;
            SkillData skill = SkillCatalog.Find("cooking");
            ability.RequiredLevel = Mathf.Clamp(value, 1, skill != null ? skill.MaxLevel : 25);
            PlayerPrefs.SetInt(Key(ability, "min"), ability.RequiredLevel);
            PlayerPrefs.Save();
            RevalidateProgress(ability);
        }

        internal static void SetTierCount(AbilityData ability, int count)
        {
            if (ability == null) return;
            Resize(ability, Mathf.Clamp(count, 1, TierLimit(ability)), Remember(ability));
            for (int tier = 1; tier <= ability.MaxTier; tier++)
                for (int index = 0; index < ability.ValuesPerTier; index++)
                {
                    int at = (tier - 1) * ability.ValuesPerTier + index;
                    int value = PlayerPrefs.GetInt(Key(ability, "v" + tier + "_" + index), ability.Values[at]);
                    ability.Values[at] = NormalizeValue(ability, tier, index, value);
                }
            NormalizeRelatedValues(ability);
            if (ability.PreviewTier > ability.MaxTier) ability.PreviewTier = ability.MaxTier;
            PlayerPrefs.SetInt(Key(ability, "tiers"), ability.MaxTier);
            PlayerPrefs.Save();
            RevalidateProgress(ability);
        }

        internal static void SetValue(AbilityData ability, int tier, int index, int value)
        {
            if (ability == null || tier < 1 || tier > ability.MaxTier ||
                index < 0 || index >= ability.ValuesPerTier) return;
            int at = (tier - 1) * ability.ValuesPerTier + index;
            ability.Values[at] = NormalizeValue(ability, tier, index, value);
            PlayerPrefs.SetInt(Key(ability, "v" + tier + "_" + index), ability.Values[at]);
            if (ability.Id == "canteencook" && tier == 1 && ability.MaxTier >= 2 &&
                ability.Values[1] < ability.Values[0])
            {
                ability.Values[1] = ability.Values[0];
                PlayerPrefs.SetInt(Key(ability, "v2_0"), ability.Values[1]);
            }
            PlayerPrefs.Save();
            RefreshSystems(ability);
            SkillCatalog.NotifyChanged();
        }

        internal static void RestoreDefaults(AbilityData ability)
        {
            if (ability == null) return;
            Defaults defaults = Remember(ability);
            PlayerPrefs.DeleteKey(Key(ability, "min"));
            PlayerPrefs.DeleteKey(Key(ability, "tiers"));
            for (int tier = 1; tier <= MaximumTiers; tier++)
                for (int index = 0; index < defaults.PerTier; index++)
                    PlayerPrefs.DeleteKey(Key(ability, "v" + tier + "_" + index));

            if (ability.Id == "durablebowl")
            {
                PlayerPrefs.DeleteKey("xskills_cooking_bowls_v1_required");
                PlayerPrefs.DeleteKey("xskills_cooking_bowls_v1_multiplier");
                PlayerPrefs.DeleteKey("xskills_cooking_bowls_v1_durability");
            }
            else if (ability.Id == "canteencook")
            {
                PlayerPrefs.DeleteKey("xskills_canteencook_v1_required");
                PlayerPrefs.DeleteKey("xskills_canteencook_v1_limit");
                PlayerPrefs.DeleteKey("xskills_canteencook_v1_limit_v2");
            }

            ability.RequiredLevel = defaults.MinLevel;
            ability.MaxTier = defaults.TierCount;
            ability.Values = (int[])defaults.Values.Clone();
            if (ability.PreviewTier > ability.MaxTier) ability.PreviewTier = ability.MaxTier;
            PlayerPrefs.Save();
            RefreshSystems(ability);
            RevalidateProgress(ability);
        }

        internal static int TierLimit(AbilityData ability)
        {
            if (ability != null && (ability.Id == "durablebowl" || ability.Id == "canteencook"))
                return 2;
            return MaximumTiers;
        }

        internal static void Clear()
        {
            Original.Clear();
        }

        private static Defaults Remember(AbilityData ability)
        {
            Defaults defaults;
            if (Original.TryGetValue(ability.Id, out defaults)) return defaults;
            defaults = new Defaults
            {
                MinLevel = ability.RequiredLevel,
                TierCount = ability.MaxTier,
                Values = (int[])ability.Values.Clone(),
                PerTier = ability.ValuesPerTier
            };
            Original[ability.Id] = defaults;
            return defaults;
        }

        private static string Key(AbilityData ability, string suffix)
        {
            return Prefix + ability.Id + "_" + suffix;
        }

        private static void Resize(AbilityData ability, int tierCount, Defaults defaults)
        {
            int[] old = ability.Values;
            int oldTiers = ability.MaxTier;
            int[] result = new int[tierCount * defaults.PerTier];
            for (int tier = 1; tier <= tierCount; tier++)
                for (int index = 0; index < defaults.PerTier; index++)
                {
                    int at = (tier - 1) * defaults.PerTier + index;
                    if (tier <= oldTiers && at < old.Length)
                        result[at] = old[at];
                    else if (tier <= defaults.TierCount)
                        result[at] = defaults.Values[at];
                    else
                    {
                        int previous = defaults.Values[(defaults.TierCount - 1) * defaults.PerTier + index];
                        int step = defaults.TierCount > 1
                            ? previous - defaults.Values[(defaults.TierCount - 2) * defaults.PerTier + index]
                            : previous;
                        result[at] = Mathf.Clamp(previous + step * (tier - defaults.TierCount), 0, 10000);
                    }
                }
            ability.MaxTier = tierCount;
            ability.Values = result;
        }

        private static int NormalizeValue(AbilityData ability, int tier, int index, int value)
        {
            if (ability.Id == "durablebowl")
                return tier == 1 ? Mathf.Clamp(value, 1, 10) : 0;
            if (ability.Id == "canteencook")
            {
                int minimum = tier == 2 && ability.Values.Length > 0
                    ? Mathf.Clamp(ability.Values[0], 1, 10) : 1;
                return Mathf.Clamp(value, minimum, 10);
            }
            return Mathf.Clamp(value, 0, 10000);
        }

        private static void NormalizeRelatedValues(AbilityData ability)
        {
            if (ability.Id == "canteencook" && ability.MaxTier >= 2 &&
                ability.Values.Length > 1 && ability.Values[1] < ability.Values[0])
                ability.Values[1] = ability.Values[0];
        }

        private static void RefreshSystems(AbilityData ability)
        {
            if (ability.Id == "durablebowl") ReusableDishesSystem.OnPerkBalanceChanged();
        }

        private static void RevalidateProgress(AbilityData ability)
        {
            SkillData skill = SkillCatalog.Find("cooking");
            if (skill != null) skill.RevalidateAbilityTiers();
            RefreshSystems(ability);
            SkillCatalog.NotifyChanged();
        }
    }
}
