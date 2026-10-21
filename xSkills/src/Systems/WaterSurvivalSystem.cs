using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UltimateWater;
using HMLLibrary;
using xSkills.Services;
using xSkills.GUI;

namespace xSkills.Systems
{
    [DataContract]
    internal sealed class WaterProgressData
    {
        [DataMember] public int FatigueLevel;
        [DataMember] public float FatigueTrainingSeconds;
        [DataMember] public int LungLevel;
        [DataMember] public int DiveTraining;
        [DataMember(EmitDefaultValue = false)] public float OxygenTrainingSeconds;
    }

    internal static class WaterSurvivalSystem
    {
        private const string HarmonyId = "xskills.water.survival";
        private const int MaxLevel = 10;
        private static Harmony harmony;
        private static Network_Player localPlayer;
        private static PlayerStats stats;
        private static PersonController controller;
        private static Stat_Oxygen oxygen;
        private static float fatigue;
        private static float regenCooldown;
        private static float oxygenBeforeUpdate;
        
        
        private static float nextTankCheck;
        private static string activeTank;
        private static float tankBonus;
        private static float tankRegen;
        private static float flipperStaminaMultiplier = 1f;
        private static float nextFlipperCheck;
        private static float pendingSaveSeconds;
        private const int LastChancePressCount = 3;
        private const int LastChanceMaxBursts = 3;
        private const float LastChancePressWindow = 1.6f;
        private const float LastChanceLiftDuration = .80f;
        private const float LastChanceLiftSpeed = 7.5f;
        private const float LastChanceRestDelay = .45f;
        private const float LastChanceRecoverySeconds = 4f;
        private static readonly FieldInfo JumpInputField = AccessTools.Field(typeof(PersonController), "inputJump");
        private static int lastChanceBursts = LastChanceMaxBursts;
        private static int lastChancePresses;
        private static int lastChanceInputFrame = -1;
        private static float lastChanceLastPress;
        private static float lastChanceFirstPress;
        private static float lastChanceLiftUntil;
        private static float lastChanceRestUntil;
        private static float lastChanceTimeOnLand;
        private static WaterProgressData progression = new WaterProgressData();

        internal static int FatigueLevel { get { return progression.FatigueLevel; } }
        internal static int LungLevel { get { return progression.LungLevel; } }
        internal static float FatigueSeconds { get { return fatigue; } }
        internal static float BaseFatigueMaximum
        {
            get
            {
                WaterSurvivalSettings s = WaterSurvivalSettings.Current;
                return s == null ? 15f : s.InitialWaterSeconds + progression.FatigueLevel * s.WaterSecondsPerLevel;
            }
        }
        internal static float FatigueMaximum { get { return BaseFatigueMaximum * flipperStaminaMultiplier; } }
        internal static float FatigueEquipmentBonusSeconds { get { return Mathf.Max(0f, FatigueMaximum - BaseFatigueMaximum); } }
        internal static int FatigueRequired
        {
            get
            {
                WaterSurvivalSettings s = WaterSurvivalSettings.Current;
                if (s == null) return 150;
                return Mathf.CeilToInt(Mathf.Min(3600000f, s.TrainingSeconds * Mathf.Pow(s.WaterTrainingGrowth, progression.FatigueLevel)));
            }
        }
        internal static int OxygenTrainingRequired
        {
            get
            {
                WaterSurvivalSettings s = WaterSurvivalSettings.Current;
                if (s == null) return 150;
                return Mathf.CeilToInt(Mathf.Min(3600000f,
                    s.TrainingUnderwaterSeconds * Mathf.Pow(s.DiveTrainingGrowth, progression.LungLevel)));
            }
        }

        internal static float BaseBreathSeconds
        {
            get
            {
                WaterSurvivalSettings s = WaterSurvivalSettings.Current;
                if (s == null || !s.OxygenEnabled) return 0f;
                float baseOxygen = s.InitialOxygen + progression.LungLevel * s.OxygenPerLevel;
                return baseOxygen / EffectiveOxygenLoss;
            }
        }
        internal static float EffectiveBreathSeconds
        {
            get
            {
                WaterSurvivalSettings s = WaterSurvivalSettings.Current;
                if (s == null || !s.OxygenEnabled) return 0f;
                float netLoss = EffectiveOxygenLoss - tankRegen;
                return netLoss <= .001f ? float.PositiveInfinity : EffectiveOxygenMax / netLoss;
            }
        }
        internal static float OxygenEquipmentBonusSeconds
        {
            get
            {
                float total = EffectiveBreathSeconds;
                return float.IsPositiveInfinity(total) ? total : Mathf.Max(0f, total - BaseBreathSeconds);
            }
        }

        internal static string WaterTrainingText
        {
            get
            {
                if (!SkillProgressStorage.IsWorldBound) return "-";
                if (progression.FatigueLevel >= MaxLevel) return "MAX";
                return FormatSeconds((int)progression.FatigueTrainingSeconds) + "/" + FormatSeconds(FatigueRequired);
            }
        }
        internal static string OxygenTrainingText
        {
            get
            {
                if (!SkillProgressStorage.IsWorldBound) return "-";
                if (progression.LungLevel >= MaxLevel) return "MAX";
                return FormatSeconds((int)progression.OxygenTrainingSeconds) + "/" + FormatSeconds(OxygenTrainingRequired);
            }
        }

        internal static string WaterRemainingText
        {
            get
            {
                return progression.FatigueLevel >= MaxLevel ? "-" :
                    FormatSeconds(Mathf.CeilToInt(Mathf.Max(0f, FatigueRequired - progression.FatigueTrainingSeconds)));
            }
        }
        internal static string OxygenRemainingText
        {
            get
            {
                return progression.LungLevel >= MaxLevel ? "-" :
                    FormatSeconds(Mathf.CeilToInt(Mathf.Max(0f, OxygenTrainingRequired - progression.OxygenTrainingSeconds)));
            }
        }
        internal static string WaterLevelText { get { return progression.FatigueLevel + "/" + MaxLevel; } }
        internal static string LungLevelText { get { return progression.LungLevel + "/" + MaxLevel; } }
        internal static string FormatBreathSeconds()
        {
            float time = EffectiveBreathSeconds;
            if (float.IsPositiveInfinity(time)) return "∞";
            return FormatSeconds(Mathf.RoundToInt(time));
        }

        internal static string FormatSeconds(int seconds)
        {
            seconds = Math.Max(0, seconds);
            return (seconds / 60).ToString() + ":" + (seconds % 60).ToString("00");
        }

        internal static float EffectiveOxygenMax
        {
            get
            {
                WaterSurvivalSettings s = WaterSurvivalSettings.Current;
                if (s == null) return 300f;
                return s.InitialOxygen + progression.LungLevel * s.OxygenPerLevel + tankBonus;
            }
        }

        internal static float EffectiveOxygenLoss
        {
            get
            {
                WaterSurvivalSettings s = WaterSurvivalSettings.Current;
                float baseLoss = s == null ? 30f : Mathf.Max(1f,
                    s.OxygenLossPerSecond - progression.LungLevel * s.OxygenLossReduction);
                return Mathf.Max(0.1f, baseLoss * SurvivalPerkSystem.OxygenConsumptionMultiplier);
            }
        }

        internal static void Initialize()
        {
            WaterSurvivalSettings.Initialize();
            if (harmony != null) return;
            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.CreateClassProcessor(typeof(WaterSinkingPatch)).Patch();
                harmony.CreateClassProcessor(typeof(WaterOxygenPatch)).Patch();
            }
            catch (Exception ex)
            {
                Debug.LogError("[xSkills] Water patches: " + ex);
                if (harmony != null) harmony.UnpatchAll(HarmonyId);
                harmony = null;
            }
        }

        internal static void Tick(float dt)
        {
            WaterSurvivalSettings.Tick();
            if (!SkillProgressStorage.IsWorldBound || !LoadSceneManager.IsGameSceneLoaded)
            {
                ClearPlayer();
                return;
            }
            Network_Player player = null;
            try { player = RAPI.GetLocalPlayer(); } catch { }
            if (player == null || player.Stats == null) { ClearPlayer(); return; }
            if (player != localPlayer)
            {
                localPlayer = player;
                stats = player.Stats;
                controller = stats.GetComponent<PersonController>();
                oxygen = stats.stat_oxygen;
                flipperStaminaMultiplier = 1f;
                fatigue = BaseFatigueMaximum;
                nextFlipperCheck = 0f;
                ResetLastChance();

                tankBonus = tankRegen = 0f;
                nextTankCheck = 0f;
            }
            if (controller == null || stats == null || stats.IsDead) return;
            dt = Mathf.Clamp(dt, 0f, .15f);
            WaterSurvivalSettings s = WaterSurvivalSettings.Current;
            if (s == null) return;
            if (Time.unscaledTime >= nextFlipperCheck)
            {
                nextFlipperCheck = Time.unscaledTime + .5f;
                RefreshFlipperStamina();
            }
            float max = FatigueMaximum;
            fatigue = Mathf.Clamp(fatigue, 0f, max);
            bool inWater = controller.controllerType == ControllerType.Water;
            if (inWater)
                lastChanceTimeOnLand = 0f;
            else
            {
                lastChanceTimeOnLand += dt;
                if (lastChanceTimeOnLand >= LastChanceRecoverySeconds && fatigue >= max * .35f &&
                    lastChanceBursts < LastChanceMaxBursts)
                    ResetLastChance();
            }
            if (s.FatigueEnabled && inWater)
            {
                float beforeFatigue = fatigue;
                fatigue = Mathf.Max(0f, fatigue - dt);
                regenCooldown = s.FatigueRegenCooldown;
                if (controller.moving && beforeFatigue > 0f && progression.FatigueLevel < MaxLevel)
                {
                    progression.FatigueTrainingSeconds += dt;
                    pendingSaveSeconds += dt;
                    int required = FatigueRequired;
                    if (progression.FatigueTrainingSeconds >= required)
                    {
                        progression.FatigueTrainingSeconds -= required;
                        progression.FatigueLevel++;
                        fatigue += s.WaterSecondsPerLevel * flipperStaminaMultiplier;
                        max = FatigueMaximum;
                        if (progression.FatigueLevel >= MaxLevel) progression.FatigueTrainingSeconds = 0f;
                        SkillProgressStorage.MarkExternalChanged(true);
                    }
                }
            }
            else
            {
                regenCooldown = Mathf.Max(0f, regenCooldown - dt);
                if (regenCooldown <= 0f)
                    fatigue = Mathf.Min(max, fatigue + s.FatigueRegenPerSecond * dt);
            }
            WaterStaminaHud.Refresh(fatigue, max, s.FatigueEnabled &&
                (inWater || fatigue < max - .25f));
            if (pendingSaveSeconds >= 5f)
            {
                pendingSaveSeconds = 0f;
                SkillProgressStorage.MarkExternalChanged(false);
            }
            if (Time.unscaledTime >= nextTankCheck)
            {
                nextTankCheck = Time.unscaledTime + 1f;
                RefreshTank();
            }
        }

        private static void RefreshFlipperStamina()
        {
            float flippers = MovementStatDisplay.ReadEquippedFlippersMultiplier(localPlayer);
            float next = 1f + Mathf.Clamp((Mathf.Max(1f, flippers) - 1f) * .5f, 0f, .8f);
            if (Mathf.Abs(next - flipperStaminaMultiplier) < .001f) return;
            float oldMax = FatigueMaximum;
            flipperStaminaMultiplier = next;
            float newMax = FatigueMaximum;
            fatigue = oldMax > .001f ? Mathf.Clamp01(fatigue / oldMax) * newMax : newMax;
        }

        private static void RefreshTank()
        {
            string found = null;
            if (localPlayer != null && localPlayer.Inventory != null)
                foreach (Slot slot in localPlayer.Inventory.equipSlots)
                {
                    if (slot == null || !slot.active || !slot.HasValidItemInstance()) continue;
                    string name = slot.itemInstance.UniqueName;
                    if (name == "OxygenBottle" || name == "Oxygen_Canister" || name == "Oxygen_Tank")
                    { found = name; break; }
                }
            if (found == activeTank) return;
            activeTank = found;
            switch (found)
            {
                case "OxygenBottle": tankBonus = 200f; tankRegen = 1.5f; break;
                case "Oxygen_Canister": tankBonus = 500f; tankRegen = 2.5f; break;
                case "Oxygen_Tank": tankBonus = 1200f; tankRegen = 4f; break;
                default: tankBonus = 0f; tankRegen = 0f; break;
            }
        }

        internal static int LastChancePressesRemaining
        {
            get
            {
                return lastChanceBursts > 0 && controller != null && IsExhausted(controller)
                    ? LastChancePressCount - lastChancePresses : 0;
            }
        }

        private static void ResetLastChance()
        {
            lastChanceBursts = LastChanceMaxBursts;
            lastChancePresses = 0;
            lastChanceInputFrame = -1;
            lastChanceLastPress = 0f;
            lastChanceFirstPress = 0f;
            lastChanceLiftUntil = 0f;
            lastChanceRestUntil = 0f;
            lastChanceTimeOnLand = 0f;
        }

        private static bool WasJumpPressed(PersonController pc)
        {
            try
            {
                if (JumpInputField != null)
                {
                    InputActionReference reference = JumpInputField.GetValue(pc) as InputActionReference;
                    CustomInputConfig config = SimpleMonoBehaviourSingleton<CustomInputConfig>.Instance;
                    if (reference != null && reference.action != null && config != null)
                        return config.WasPressedThisFrame(reference.action, "Jump");
                }
            }
            catch (Exception) { }
            return Input.GetKeyDown(KeyCode.Space);
        }

        internal static void ApplyLastChance(PersonController pc, ref Vector3 moveDirection)
        {
            if (!IsExhausted(pc)) return;
            float now = Time.unscaledTime;
            if (pc.IsMovementFree && CanvasHelper.ActiveMenu == MenuType.None &&
                lastChanceBursts > 0 && now >= lastChanceRestUntil &&
                lastChanceInputFrame != Time.frameCount)
            {
                lastChanceInputFrame = Time.frameCount;
                if (WasJumpPressed(pc))
                {
                    if (lastChancePresses == 0 || now - lastChanceLastPress > .95f ||
                        now - lastChanceFirstPress > LastChancePressWindow)
                    {
                        lastChancePresses = 0;
                        lastChanceFirstPress = now;
                    }
                    lastChancePresses++;
                    lastChanceLastPress = now;
                    if (lastChancePresses >= LastChancePressCount)
                    {
                        lastChancePresses = 0;
                        lastChanceBursts--;
                        lastChanceLiftUntil = now + LastChanceLiftDuration;
                        lastChanceRestUntil = lastChanceLiftUntil + LastChanceRestDelay;
                    }
                }
            }
            moveDirection.y = now < lastChanceLiftUntil && pc.IsMovementFree
                ? LastChanceLiftSpeed
                : pc.SubmersionState == SubmersionState.Full ? -3f : -14f;
        }

        internal static bool IsExhausted(PersonController pc)
        {
            WaterSurvivalSettings s = WaterSurvivalSettings.Current;
            return s != null && s.FatigueEnabled && pc != null && pc == controller &&
                controller.controllerType == ControllerType.Water && fatigue <= 0f;
        }

        internal static void BeforeOxygen(Stat_Oxygen stat)
        {
            WaterSurvivalSettings s = WaterSurvivalSettings.Current;
            if (s == null || !s.OxygenEnabled || stat != oxygen || !SkillProgressStorage.IsWorldBound) return;
            float max = EffectiveOxygenMax;
            float old = stat.Max;
            if (Mathf.Abs(max - old) > .01f)
            {
                float percent = old > 0f ? Mathf.Clamp01(stat.Value / old) : 1f;
                stat.SetMaxValue(max);
                stat.Value = Mathf.Clamp(percent * max, 0f, max);
            }
            stat.SetOxygenLostPerSecond(EffectiveOxygenLoss);
            oxygenBeforeUpdate = stat.Value;
        }

        internal static void AfterOxygen(Stat_Oxygen stat)
        {
            WaterSurvivalSettings s = WaterSurvivalSettings.Current;
            if (s == null || !s.OxygenEnabled || stat != oxygen || controller == null || !SkillProgressStorage.IsWorldBound) return;
            if (tankRegen > 0f) stat.Value = Mathf.Min(stat.Max, stat.Value + tankRegen * Time.deltaTime);
            else if (controller.SubmersionState != SubmersionState.Full)
            {
                float gained = stat.Value - oxygenBeforeUpdate;
                if (gained > 0f) stat.Value = Mathf.Min(stat.Max, oxygenBeforeUpdate + gained * s.OxygenRegenMultiplier);
            }
            // Засчитывается время под водой, пока остаётся запас кислорода
            if (stat.Max > 0f && !stats.IsDead && controller.SubmersionState == SubmersionState.Full &&
                stat.Value > .01f && progression.LungLevel < MaxLevel)
            {
                float dt = Mathf.Clamp(Time.deltaTime, 0f, .15f);
                if (dt > 0f)
                {
                    progression.OxygenTrainingSeconds += dt;
                    pendingSaveSeconds += dt;
                    int required = OxygenTrainingRequired;
                    if (progression.OxygenTrainingSeconds >= required)
                    {
                        progression.OxygenTrainingSeconds -= required;
                        progression.LungLevel++;
                        if (progression.LungLevel >= MaxLevel) progression.OxygenTrainingSeconds = 0f;
                        SkillProgressStorage.MarkExternalChanged(true);
                    }
                }
            }

        }


#pragma warning disable 0649
        [Serializable]
        private sealed class LegacyLung { public int LungLevel; public int SignificantDivesCounter; }
        [Serializable]
        private sealed class LegacyFatigue { public int FatigueLevel; public int FatigueActivityCounter; }
        [Serializable]
        private sealed class LegacySave
        {
            public LegacyLung LungData;
            public LegacyFatigue FatigueData;
        }
#pragma warning restore 0649

        internal static WaterProgressData TryImportBetterRaft()
        {
            string world = SaveAndLoad.CurrentGameFileName;
            if (string.IsNullOrEmpty(world)) return null;
            foreach (char invalid in Path.GetInvalidFileNameChars()) world = world.Replace(invalid, '_');
            string modRoot = Path.Combine(Application.dataPath, "..", "Mods");
            try { if (!string.IsNullOrEmpty(HLib.path_modsFolder)) modRoot = HLib.path_modsFolder; }
            catch { }
            string path = Path.Combine(modRoot, "ModData", "BetterRaft", world + "_progress.json");
            if (!File.Exists(path)) return null;
            try
            {
                LegacySave legacy = JsonUtility.FromJson<LegacySave>(File.ReadAllText(path));
                if (legacy == null || (legacy.LungData == null && legacy.FatigueData == null)) return null;
                WaterProgressData imported = new WaterProgressData();
                if (legacy.FatigueData != null)
                {
                    imported.FatigueLevel = legacy.FatigueData.FatigueLevel;
                    imported.FatigueTrainingSeconds = legacy.FatigueData.FatigueActivityCounter;
                }
                if (legacy.LungData != null)
                {
                    imported.LungLevel = legacy.LungData.LungLevel;
                    // Старые зачтённые погружения переводятся в долю новой тренировки
                    int oldDives = Math.Max(0, legacy.LungData.SignificantDivesCounter % 10);
                    imported.OxygenTrainingSeconds = oldDives * 15f;
                    imported.DiveTraining = 0;
                }
                Debug.Log("[xSkills] Imported BetterRaft swim and lung progression");
                return imported;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] BetterRaft progress import: " + ex.Message);
                return null;
            }
        }

        internal static void FlushProgress()
        {
            if (pendingSaveSeconds > 0f)
            {
                pendingSaveSeconds = 0f;
                SkillProgressStorage.MarkExternalChanged(false);
            }
        }

        internal static WaterProgressData CollectProgress()
        {
            return new WaterProgressData
            {
                FatigueLevel = progression.FatigueLevel,
                FatigueTrainingSeconds = progression.FatigueTrainingSeconds,
                LungLevel = progression.LungLevel,
                DiveTraining = 0,
                OxygenTrainingSeconds = progression.OxygenTrainingSeconds
            };
        }

        internal static void RestoreProgress(WaterProgressData saved)
        {
            WaterStaminaHud.Dispose();
            progression = saved ?? new WaterProgressData();
            progression.FatigueLevel = Mathf.Clamp(progression.FatigueLevel, 0, MaxLevel);
            progression.LungLevel = Mathf.Clamp(progression.LungLevel, 0, MaxLevel);
            progression.FatigueTrainingSeconds = Mathf.Clamp(progression.FatigueTrainingSeconds, 0f, FatigueRequired);
            if (progression.OxygenTrainingSeconds <= 0f && progression.DiveTraining > 0)
            {
                WaterSurvivalSettings s = WaterSurvivalSettings.Current;
                float oldDives = s != null ? Mathf.Max(1f, s.DivesToLevel *
                    Mathf.Pow(s.DiveTrainingGrowth, progression.LungLevel)) : 10f;
                progression.OxygenTrainingSeconds = Mathf.Clamp01(progression.DiveTraining / oldDives) * OxygenTrainingRequired;
            }
            progression.DiveTraining = 0;
            progression.OxygenTrainingSeconds = Mathf.Clamp(progression.OxygenTrainingSeconds,
                0f, OxygenTrainingRequired);
            if (progression.LungLevel >= MaxLevel) progression.OxygenTrainingSeconds = 0f;
            if (progression.FatigueLevel >= MaxLevel) progression.FatigueTrainingSeconds = 0f;
            pendingSaveSeconds = 0f;
            fatigue = FatigueMaximum;
            ClearPlayer();
        }

        private static void ClearPlayer()
        {
            WaterStaminaHud.Dispose();
            localPlayer = null;
            stats = null;
            controller = null;
            oxygen = null;
            activeTank = null;
            tankBonus = tankRegen = 0f;
            flipperStaminaMultiplier = 1f;
            nextFlipperCheck = 0f;
            ResetLastChance();

        }

        internal static void Dispose()
        {
            if (harmony != null) harmony.UnpatchAll(HarmonyId);
            harmony = null;
            ClearPlayer();
            WaterSurvivalSettings.Dispose();
            progression = new WaterProgressData();
        }
    }

    [HarmonyPatch(typeof(PersonController), "WaterControll")]
    [HarmonyPriority(Priority.Last)]
    internal static class WaterSinkingPatch
    {
        private static void Postfix(PersonController __instance, ref Vector3 ___moveDirection)
        {
            WaterSurvivalSystem.ApplyLastChance(__instance, ref ___moveDirection);
        }
    }

    [HarmonyPatch(typeof(Stat_Oxygen), "Update")]
    internal static class WaterOxygenPatch
    {
        private static void Prefix(Stat_Oxygen __instance) { WaterSurvivalSystem.BeforeOxygen(__instance); }
        private static void Postfix(Stat_Oxygen __instance) { WaterSurvivalSystem.AfterOxygen(__instance); }
    }
}
