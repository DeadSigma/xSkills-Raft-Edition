using System;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using HMLLibrary;
using UnityEngine;
using xSkills.Skills;
using xSkills.Services;

namespace xSkills.Systems
{
    internal static class SurvivalPerkSystem
    {
        private const string HarmonyId = "xskills.survival.perks";
        private static readonly FieldInfo NormalHealthField =
            AccessTools.Field(typeof(Network_Entity), "stat_health");
        private static Harmony harmony;

        internal static void Initialize()
        {
            if (harmony != null) return;
            try
            {
                harmony = new Harmony(HarmonyId);
                MethodInfo damage = AccessTools.Method(typeof(PlayerStats), "Damage", new[]
                {
                    typeof(float), typeof(Vector3), typeof(Vector3), typeof(EntityType),
                    typeof(bool), typeof(SO_Buff)
                });
                MethodInfo regen = AccessTools.Method(typeof(Stat_Health), "Regenerate");
                MethodInfo ground = AccessTools.Method(typeof(PersonController), "GroundControll");
                MethodInfo hunger = AccessTools.Method(typeof(Stat_Consumable), "HandleValue");
                if (damage == null || regen == null || ground == null || hunger == null)
                    throw new MissingMethodException("Survival perk patch target missing");
                harmony.Patch(damage, prefix: new HarmonyMethod(typeof(SurvivalPerkSystem), nameof(BeforeDamage)));
                harmony.Patch(regen, prefix: new HarmonyMethod(typeof(SurvivalPerkSystem), nameof(BeforeRegeneration)),
                    postfix: new HarmonyMethod(typeof(SurvivalPerkSystem), nameof(AfterRegeneration)));
                harmony.Patch(ground, postfix: new HarmonyMethod(typeof(SurvivalPerkSystem), nameof(AfterGroundMovement)));
                harmony.Patch(hunger, prefix: new HarmonyMethod(typeof(SurvivalPerkSystem), nameof(BeforeHungerUpdate)),
                    postfix: new HarmonyMethod(typeof(SurvivalPerkSystem), nameof(AfterHungerUpdate)));
            }
            catch (Exception ex)
            {
                Debug.LogError("[xSkills] Survival perks initialization: " + ex);
                Dispose();
            }
        }

        internal static void Dispose()
        {
            if (harmony == null) return;
            harmony.UnpatchAll(HarmonyId);
            harmony = null;
        }

        private static int Tier(string abilityId)
        {
            if (!SkillProgressStorage.IsWorldBound) return 0;
            SkillData skill = SkillCatalog.Find("survival");
            AbilityData ability = skill != null ? skill.FindAbility(abilityId) : null;
            if (ability == null) return 0;
            int allowed = Mathf.Max(0, skill.Level - ability.RequiredLevel + 1);
            return Mathf.Clamp(ability.PreviewTier, 0, Mathf.Min(allowed, ability.MaxTier));
        }

        internal static float CurrentHealthBonusPercent
        {
            get
            {
                int tier = Tier("longlife");
                if (tier == 0) return 0f;
                SkillData survival = SkillCatalog.Find("survival");
                int level = survival != null ? survival.Level : 1;
                return Mathf.Min(Value("longlife", tier, 2),
                    Value("longlife", tier, 0) + level * Value("longlife", tier, 1));
            }
        }

        internal static float ExtraHealth(float baseMaximum)
        {
            if (baseMaximum <= 0f) return 0f;
            return baseMaximum * CurrentHealthBonusPercent / 100f;
        }

        internal static float? CurrentLastStandChancePercent
        {
            get
            {
                int tier = Tier("laststand");
                if (tier == 0) return null;
                Network_Player player = LocalPlayer();
                if (player == null) return null;
                Stat_Health health = NormalHealth(player.Stats);
                if (health == null) return null;
                return LastStandChance(health, player.Stats, tier) * 100f;
            }
        }

        private static float LastStandChance(Stat_Health health, PlayerStats stats, int tier)
        {
            if (health == null || health.Max <= 0f || stats == null) return 0f;
            float bonusHealth = stats.stat_BonusHealth != null ? stats.stat_BonusHealth.Value : 0f;
            float bonusMax = stats.stat_BonusHealth != null ? stats.stat_BonusHealth.Max : 0f;
            float maxHealth = health.Max + bonusMax;
            if (maxHealth <= 0f) return 0f;
            return Mathf.Clamp01(((health.Value + bonusHealth) / maxHealth) *
                Mathf.Max(0, Value("laststand", tier, 0)) / 100f);
        }

        internal static float CurrentPhotosynthesisBonusPercent
        {
            get { return PhotosynthesisBonus(Tier("photosynthesis")) * 100f; }
        }

        private static float PhotosynthesisBonus(int tier)
        {
            if (tier <= 0) return 0f;
            float daylight = BodyTemperatureSystem.Daylight;
            if (daylight >= .2f && !BodyTemperatureSystem.IsShaded && !BodyTemperatureSystem.IsRaining)
                return Value("photosynthesis", tier, 0) * Mathf.Clamp01((daylight - .2f) / .8f) / 100f;
            if (daylight < .1f)
                return -Value("photosynthesis", tier, 1) * (1f - Mathf.Clamp01(daylight / .1f)) / 100f;
            return 0f;
        }

        internal static float OxygenConsumptionMultiplier
        {
            get
            {
                int tier = Tier("diver");
                return tier == 0 ? 1f : 1f - Mathf.Clamp(Value("diver", tier, 0), 0, 95) / 100f;
            }
        }

        private static int Value(string abilityId, int tier, int index)
        {
            SkillData skill = SkillCatalog.Find("survival");
            AbilityData ability = skill != null ? skill.FindAbility(abilityId) : null;
            if (ability == null || tier <= 0 || tier > ability.MaxTier ||
                index < 0 || index >= ability.ValuesPerTier) return 0;
            int at = (tier - 1) * ability.ValuesPerTier + index;
            return at >= 0 && at < ability.Values.Length ? ability.Values[at] : 0;
        }

        private static Network_Player LocalPlayer()
        {
            if (!SkillProgressStorage.IsWorldBound) return null;
            try
            {
                if (!RAPI.IsCurrentSceneGame() || SaveAndLoad.IsGameLoading) return null;
                Network_Player player = RAPI.GetLocalPlayer();
                return player != null && player.Stats != null && !player.Stats.IsDead ? player : null;
            }
            catch (Exception) { return null; }
        }

        private static Stat_Health NormalHealth(PlayerStats stats)
        {
            if (stats == null || NormalHealthField == null) return null;
            try { return NormalHealthField.GetValue(stats) as Stat_Health; }
            catch (Exception) { return null; }
        }

        private static void BeforeDamage(PlayerStats __instance, ref float damage, EntityType damageInflictorEntityType)
        {
            Network_Player player = LocalPlayer();
            if (player == null || player.Stats != __instance || damage <= 0f) return;
            if (damageInflictorEntityType == EntityType.FallDamage)
            {
                int fallTier = Tier("featherfall");
                if (fallTier > 0)
                {
                    damage = Mathf.Max(0f, damage - Mathf.Max(0, Value("featherfall", fallTier, 0)));
                    damage *= 1f - Mathf.Clamp(Value("featherfall", fallTier, 1), 0, 100) / 100f;
                }
            }

            int shieldTier = Tier("meatshield");
            if (shieldTier > 0 && __instance.stat_hunger != null)
            {
                float savedDamage = damage * Mathf.Clamp(Value("meatshield", shieldTier, 0), 0, 100) / 100f;
                float hungerCost = savedDamage * Mathf.Max(0, Value("meatshield", shieldTier, 1));
                Stat_Bonus_Consumable hunger = __instance.stat_hunger;
                if (hunger.Normal != null && hunger.Bonus != null &&
                    hunger.Normal.Value + hunger.Bonus.Value >= hungerCost && hungerCost > 0f)
                {
                    hunger.RemoveValue(hungerCost);
                    damage -= savedDamage;
                }
            }

            if (Tier("laststand") <= 0 || damage <= 0f) return;
            Stat_Health health = NormalHealth(__instance);
            if (health == null || health.Max <= 0f) return;
            float bonusHealth = __instance.stat_BonusHealth != null ? __instance.stat_BonusHealth.Value : 0f;
            float remaining = health.Value + bonusHealth;
            if (remaining <= .1f || damage < remaining) return;
            float chance = LastStandChance(health, __instance, Tier("laststand"));
            if (UnityEngine.Random.value < chance)
                damage = Mathf.Max(0f, remaining - .1f);
        }

        private static void BeforeRegeneration(Stat_Health __instance, out float __state)
        {
            __state = __instance != null ? __instance.Value : 0f;
        }

        private static void AfterRegeneration(Stat_Health __instance, float __state)
        {
            int tier = Tier("photosynthesis");
            if (tier <= 0 || __instance == null) return;
            Network_Player player = LocalPlayer();
            if (player == null || NormalHealth(player.Stats) != __instance) return;
            float regenerated = __instance.Value - __state;
            if (regenerated <= 0f) return;

            float bonus = PhotosynthesisBonus(tier);
            if (Mathf.Abs(bonus) > .0001f)
                __instance.Value = Mathf.Clamp(__state + regenerated * (1f + bonus), 0f, __instance.Max);
        }

        private static void AfterGroundMovement(PersonController __instance, ref Vector3 ___moveDirection)
        {
            Network_Player player = LocalPlayer();
            if (player == null || player.PersonController != __instance) return;
            int scoutTier = Tier("scout");
            float bonus = scoutTier > 0 ? Value("scout", scoutTier, 0) / 100f : 0f;
            int sprinterTier = Tier("sprinter");
            if (sprinterTier > 0) bonus += Value("sprinter", sprinterTier, 0) / 100f;
            if (bonus <= 0f) return;
            ___moveDirection.x *= 1f + bonus;
            ___moveDirection.z *= 1f + bonus;
        }

        private static void BeforeHungerUpdate(Stat_Consumable __instance, out float __state)
        {
            __state = __instance != null ? __instance.Value : 0f;
        }

        private static void AfterHungerUpdate(Stat_Consumable __instance, float __state)
        {
            int tier = Tier("sprinter");
            if (tier <= 0 || __instance == null) return;
            Network_Player player = LocalPlayer();
            if (player == null || player.Stats.stat_hunger == null) return;
            Stat_Bonus_Consumable hunger = player.Stats.stat_hunger;
            if (hunger.normalConsumable != __instance &&
                hunger.bonusConsumable != __instance) return;
            float consumed = __state - __instance.Value;
            if (consumed <= 0f) return;
            __instance.Value = Mathf.Max(0f, __instance.Value - consumed *
                Mathf.Clamp(Value("sprinter", tier, 1), 0, 500) / 100f);
        }

        internal static string DescribeAbility(AbilityData ability, string description)
        {
            if (ability == null || string.IsNullOrEmpty(description) || ability.ValuesPerTier <= 0)
                return description;

            int currentTier = Mathf.Clamp(ability.PreviewTier, 0, ability.MaxTier);
            object[] args = new object[Mathf.Max(10, ability.ValuesPerTier)];
            for (int i = 0; i < args.Length; i++) args[i] = "";

            // Суффикс процента добавляется к каждой цифре внутри её цветового тега
            string format = Regex.Replace(description, @"\{(\d+)\}%", match =>
            {
                int index;
                if (!int.TryParse(match.Groups[1].Value, out index) || index >= ability.ValuesPerTier)
                    return match.Value;
                return "{" + index + "}";
            });
            for (int i = 0; i < ability.ValuesPerTier; i++)
            {
                bool percent = Regex.IsMatch(description, @"\{" + i + @"\}%");
                string suffix = percent ? "%" : "";
                string value = "";
                if (currentTier > 0)
                    value = "<color=#feae34>" +
                        ability.Values[(currentTier - 1) * ability.ValuesPerTier + i] + suffix + "</color>";
                if (currentTier < ability.MaxTier)
                {
                    if (value.Length > 0) value += " → ";
                    value += "<color=#7ac62f>" +
                        ability.Values[currentTier * ability.ValuesPerTier + i] + suffix + "</color>";
                }
                args[i] = value;
            }
            try { return string.Format(format, args); }
            catch (FormatException) { return description; }
        }
    }
}
