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
        private const string LastStandSavePrefix = "xskills_laststand_ready_v1_";
        private const float LastStandCooldownDays = 3f;
        private const float LastStandRecoveredHealth = .2f;
        private static Harmony harmony;
        private static Stat_Consumable trackedStomach;
        private static Stat trackedStomachTarget;
        private static float baseStomachMax;
        private static float baseStomachTargetMax;
        private static float appliedStomachMax;
        private static float appliedStomachTargetMax;
        private static float appliedStomachBonus;
        private static float nextStomachCheck;

        internal static void Initialize()
        {
            if (harmony != null) return;
            harmony = new Harmony(HarmonyId);
            Patch("Damage", AccessTools.Method(typeof(PlayerStats), "Damage", new[]
            {
                typeof(float), typeof(Vector3), typeof(Vector3), typeof(EntityType),
                typeof(bool), typeof(SO_Buff)
            }), nameof(BeforeDamage), nameof(AfterDamage));
            Patch("Regenerate", AccessTools.Method(typeof(Stat_Health), "Regenerate"),
                nameof(BeforeRegeneration), nameof(AfterRegeneration));
            Patch("GroundControll", AccessTools.Method(typeof(PersonController), "GroundControll"),
                nameof(BeforeGroundMovement), nameof(AfterGroundMovement));
            Patch("LostPerSecond", AccessTools.PropertyGetter(typeof(Stat_Consumable), "LostPerSecond"),
                null, nameof(AfterHungerRate));
            Patch("PlayerStats.Update", AccessTools.Method(typeof(PlayerStats), "Update"),
                null, nameof(AfterStatsUpdate));
        }

        private static void Patch(string label, MethodBase target, string prefix, string postfix)
        {
            if (target == null)
            {
                Debug.LogWarning("[xSkills] Survival perk target not found: " + label);
                return;
            }
            try
            {
                harmony.Patch(target,
                    prefix == null ? null : new HarmonyMethod(typeof(SurvivalPerkSystem), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(SurvivalPerkSystem), postfix));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Survival perk patch failed (" + label + "): " + ex);
            }
        }

        internal static void Dispose()
        {
            RestoreStomachCapacity();
            if (harmony != null) harmony.UnpatchAll(HarmonyId);
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

        internal static float CurrentStomachBonus
        {
            get
            {
                int tier = Tier("hugestomach");
                return tier > 0 ? Mathf.Max(0, Value("hugestomach", tier, 0)) : 0f;
            }
        }

        internal static float? CurrentStomachCapacity
        {
            get
            {
                Network_Player player = LocalPlayer();
                return player != null && player.Stats.stat_hunger != null &&
                    player.Stats.stat_hunger.Normal != null
                    ? (float?)player.Stats.stat_hunger.Normal.Max : null;
            }
        }

        internal static float CurrentGroundSpeedBonusPercent
        {
            get
            {
                int scout = Tier("scout");
                int sprinter = Tier("sprinter");
                return (scout > 0 ? Mathf.Max(0, Value("scout", scout, 0)) : 0) +
                    (sprinter > 0 ? Mathf.Max(0, Value("sprinter", sprinter, 0)) : 0);
            }
        }

        internal static float CurrentSprinterHungerMultiplier
        {
            get
            {
                int tier = Tier("sprinter");
                return tier > 0 ? 1f + Mathf.Clamp(Value("sprinter", tier, 1), 0, 500) / 100f : 1f;
            }
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
                if (LastStandRemainingDays(player) > 0f) return 0f;
                return LastStandChance(health, player.Stats, tier) * 100f;
            }
        }

        internal static float? CurrentLastStandCooldownDays
        {
            get
            {
                if (Tier("laststand") <= 0) return null;
                Network_Player player = LocalPlayer();
                return player == null ? (float?)null : LastStandRemainingDays(player);
            }
        }

        private static float LastStandGameDay()
        {
            UnityEngine.AzureSky.AzureSkyController sky =
                ComponentManager<UnityEngine.AzureSky.AzureSkyController>.Value;
            float hour = sky != null && sky.timeOfDay != null
                ? Mathf.Clamp(sky.timeOfDay.hour, 0f, 24f) : 0f;
            return Mathf.Max(0, WorldManager.DayCounter) + hour / 24f;
        }

        private static string LastStandSaveKey(Network_Player player)
        {
            if (player == null || SaveAndLoad.WorldGuid == Guid.Empty) return null;
            return LastStandSavePrefix + SaveAndLoad.WorldGuid.ToString("N") + "_" + player.steamID;
        }

        private static float LastStandRemainingDays(Network_Player player)
        {
            string key = LastStandSaveKey(player);
            if (key == null) return 0f;
            return Mathf.Max(0f, PlayerPrefs.GetFloat(key, 0f) - LastStandGameDay());
        }

        private static void BeginLastStandCooldown(Network_Player player)
        {
            string key = LastStandSaveKey(player);
            if (key == null) return;
            PlayerPrefs.SetFloat(key, LastStandGameDay() + LastStandCooldownDays);
            PlayerPrefs.Save();
        }

        private static float LastStandChance(Stat_Health health, PlayerStats stats, int tier)
        {
            if (health == null || health.Max <= 0f || stats == null) return 0f;
            float bonusHealth = stats.stat_BonusHealth != null &&
                stats.stat_BonusHealth.statTarget != null
                    ? Mathf.Max(0f, stats.stat_BonusHealth.statTarget.Value) : 0f;
            float maxHealth = health.Max + bonusHealth;
            float healthRatio = Mathf.Clamp01((health.Value + bonusHealth) / maxHealth);
            float chanceAtStandardStrength = Mathf.Max(.1f, 1f - healthRatio);
            return Mathf.Clamp01(chanceAtStandardStrength *
                Mathf.Max(0, Value("laststand", tier, 0)) / 200f);
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
            return stats != null ? stats.stat_health : null;
        }

        private static void BeforeDamage(PlayerStats __instance, ref float damage,
            EntityType damageInflictorEntityType, out bool __state)
        {
            __state = false;
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

            int tier = Tier("laststand");
            if (tier <= 0 || damage <= 0f || LastStandRemainingDays(player) > 0f ||
                __instance.hasDeathPrevention || __instance.IsInvurnerable ||
                GameModeValueManager.IsPlayerInvurnerable) return;

            Stat_Health health = NormalHealth(__instance);
            if (health == null || health.Max <= 0f) return;
            float bonusHealth = __instance.stat_BonusHealth != null &&
                __instance.stat_BonusHealth.statTarget != null
                    ? Mathf.Max(0f, __instance.stat_BonusHealth.statTarget.Value) : 0f;
            float remaining = health.Value + bonusHealth;
            if (remaining <= .1f) return;

            // Проверяется урон после ванильного уменьшения бронёй
            float actualDamage = damage;
            if (damageInflictorEntityType == EntityType.Enemy ||
                damageInflictorEntityType == EntityType.Player)
            {
                ArmorHandler armor = __instance.GetComponent<ArmorHandler>();
                if (armor != null && armor.DamageReduction != 0f)
                    actualDamage = Mathf.FloorToInt(damage * (1f - armor.DamageReduction));
            }
            if (actualDamage < remaining || UnityEngine.Random.value >= LastStandChance(health, __instance, tier))
                return;

            // Удар оставляет минимум здоровья до применения восстановления
            damage = Mathf.Max(0f, remaining - .1f);
            __state = true;
            BeginLastStandCooldown(player);
        }

        private static void AfterDamage(PlayerStats __instance, bool __state)
        {
            if (!__state) return;
            Stat_Health health = NormalHealth(__instance);
            if (health != null && health.Max > 0f)
                health.Value = Mathf.Max(health.Value, health.Max * LastStandRecoveredHealth);
        }

        private static void BeforeRegeneration(Stat_Health __instance,
            ref float ___regenPerSecond, out float __state)
        {
            __state = ___regenPerSecond;
            int tier = Tier("photosynthesis");
            if (tier <= 0) return;
            Network_Player player = LocalPlayer();
            if (player == null || NormalHealth(player.Stats) != __instance) return;
            float multiplier = Mathf.Max(0f, 1f + PhotosynthesisBonus(tier));
            ___regenPerSecond = __state * multiplier;
        }

        private static void AfterRegeneration(ref float ___regenPerSecond, float __state)
        {
            ___regenPerSecond = __state;
        }

        private static void BeforeGroundMovement(PersonController __instance, out Vector2 __state)
        {
            __state = new Vector2(__instance.normalSpeed, __instance.sprintSpeed);
            Network_Player player = LocalPlayer();
            if (player == null || player.PersonController != __instance ||
                !__instance.IsMovementFree) return;
            float multiplier = 1f + CurrentGroundSpeedBonusPercent / 100f;
            if (multiplier <= 1f) return;
            __instance.normalSpeed = __state.x * multiplier;
            __instance.sprintSpeed = __state.y * multiplier;
        }

        private static void AfterGroundMovement(PersonController __instance, Vector2 __state)
        {
            __instance.normalSpeed = __state.x;
            __instance.sprintSpeed = __state.y;
        }

        private static void AfterHungerRate(Stat_Consumable __instance, ref float __result)
        {
            int tier = Tier("sprinter");
            if (tier <= 0 || __instance == null) return;
            Network_Player player = LocalPlayer();
            if (player == null || player.Stats.stat_hunger == null) return;
            Stat_Bonus_Consumable hunger = player.Stats.stat_hunger;
            if (hunger.normalConsumable != __instance &&
                hunger.bonusConsumable != __instance) return;
            __result *= 1f + Mathf.Clamp(Value("sprinter", tier, 1), 0, 500) / 100f;
        }

        private static void AfterStatsUpdate(PlayerStats __instance)
        {
            if (Time.unscaledTime < nextStomachCheck && trackedStomach != null) return;
            nextStomachCheck = Time.unscaledTime + .25f;
            Network_Player player = LocalPlayer();
            if (player == null || player.Stats != __instance)
            {
                if (trackedStomach != null && (player == null ||
                    player.Stats.stat_hunger == null ||
                    trackedStomach != player.Stats.stat_hunger.Normal))
                    RestoreStomachCapacity();
                return;
            }
            ApplyStomachCapacity(__instance);
        }

        private static void ApplyStomachCapacity(PlayerStats stats)
        {
            if (stats == null || stats.stat_hunger == null) return;
            Stat_Consumable normal = stats.stat_hunger.Normal as Stat_Consumable;
            if (normal == null) return;
            Stat target = normal.statTarget;
            if (trackedStomach != normal || trackedStomachTarget != target)
            {
                RestoreStomachCapacity();
                trackedStomach = normal;
                trackedStomachTarget = target;
                baseStomachMax = normal.Max;
                baseStomachTargetMax = target != null ? target.Max : 0f;
                appliedStomachMax = baseStomachMax;
                appliedStomachTargetMax = baseStomachTargetMax;
                appliedStomachBonus = 0f;
            }

            if (Mathf.Abs(normal.Max - appliedStomachMax) > .05f)
                baseStomachMax = Mathf.Max(1f, normal.Max - appliedStomachBonus);
            if (target != null && Mathf.Abs(target.Max - appliedStomachTargetMax) > .05f)
                baseStomachTargetMax = Mathf.Max(1f, target.Max - appliedStomachBonus);

            float bonus = CurrentStomachBonus;
            float maximum = Mathf.Max(1f, baseStomachMax + bonus);
            if (Mathf.Abs(normal.Max - maximum) > .001f) normal.SetMaxValue(maximum);
            if (normal.Value > maximum) normal.Value = maximum;
            appliedStomachMax = maximum;

            if (target != null)
            {
                float targetMaximum = Mathf.Max(1f, baseStomachTargetMax + bonus);
                if (Mathf.Abs(target.Max - targetMaximum) > .001f)
                    target.SetMaxValue(targetMaximum);
                if (target.Value > targetMaximum) target.Value = targetMaximum;
                appliedStomachTargetMax = targetMaximum;
            }
            appliedStomachBonus = bonus;
        }

        private static void RestoreStomachCapacity()
        {
            if (trackedStomach != null && Mathf.Abs(trackedStomach.Max - appliedStomachMax) < .05f)
            {
                trackedStomach.SetMaxValue(baseStomachMax);
                if (trackedStomach.Value > baseStomachMax)
                    trackedStomach.Value = baseStomachMax;
            }
            if (trackedStomachTarget != null &&
                Mathf.Abs(trackedStomachTarget.Max - appliedStomachTargetMax) < .05f)
            {
                trackedStomachTarget.SetMaxValue(baseStomachTargetMax);
                if (trackedStomachTarget.Value > baseStomachTargetMax)
                    trackedStomachTarget.Value = baseStomachTargetMax;
            }
            trackedStomach = null;
            trackedStomachTarget = null;
            baseStomachMax = baseStomachTargetMax = 0f;
            appliedStomachMax = appliedStomachTargetMax = appliedStomachBonus = 0f;
            nextStomachCheck = 0f;
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
