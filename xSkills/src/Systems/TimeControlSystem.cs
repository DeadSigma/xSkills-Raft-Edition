using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using HMLLibrary;
using UnityEngine;
using UnityEngine.AzureSky;

namespace xSkills.Systems
{
    [Serializable]
    internal sealed class TimeControlSettings
    {
        public const float DefaultDaySeconds = 1200f;
        public const float DefaultSharkAttack = 30f;
        public const float DefaultSharkRespawn = 80f;

        public const float ModDaySeconds = 3000f;
        public const float ModPlantGrowthMultiplier = 2f;
        public const float ModSharkAttackSeconds = 60f;
        public const float ModSharkRespawnSeconds = 160f;
        public const float ModWeatherDurationMultiplier = 2f;
        public const float DefaultRainFrequencyPercent = 100f;
        public const float DefaultRainDurationMultiplier = 1f;
        public const float DefaultRainVariationPercent = 35f;
        public const float ModHungerDurationMultiplier = 2f;
        public const float ModThirstDurationMultiplier = 2f;

        public int FormatVersion = 5;
        public float DayLengthSeconds = ModDaySeconds;
        public float PlantGrowthMultiplier = ModPlantGrowthMultiplier;
        public float SharkAttackSeconds = ModSharkAttackSeconds;
        public float SharkRespawnSeconds = ModSharkRespawnSeconds;
        public float WeatherDurationMultiplier = ModWeatherDurationMultiplier;
        public float RainFrequencyPercent = DefaultRainFrequencyPercent;
        public float RainDurationMultiplier = DefaultRainDurationMultiplier;
        public float RainVariationPercent = DefaultRainVariationPercent;
        public float HungerDurationMultiplier = ModHungerDurationMultiplier;
        public float ThirstDurationMultiplier = ModThirstDurationMultiplier;
        public bool ClimateConsumption = true;

        public void Clamp()
        {
            DayLengthSeconds = Mathf.Clamp(Mathf.Round(DayLengthSeconds), 60f, 14400f);
            PlantGrowthMultiplier = ClampMultiplier(PlantGrowthMultiplier);
            SharkAttackSeconds = Mathf.Clamp(Mathf.Round(SharkAttackSeconds), 1f, 3600f);
            SharkRespawnSeconds = Mathf.Clamp(Mathf.Round(SharkRespawnSeconds), 1f, 3600f);
            WeatherDurationMultiplier = ClampMultiplier(WeatherDurationMultiplier);
            RainFrequencyPercent = Mathf.Clamp(Mathf.Round(RainFrequencyPercent), 0f, 400f);
            RainDurationMultiplier = Mathf.Clamp(Mathf.Round(RainDurationMultiplier * 20f) / 20f, 0.25f, 5f);
            RainVariationPercent = Mathf.Clamp(Mathf.Round(RainVariationPercent), 0f, 90f);
            HungerDurationMultiplier = ClampMultiplier(HungerDurationMultiplier);
            ThirstDurationMultiplier = ClampMultiplier(ThirstDurationMultiplier);
        }
        internal static float ClampMultiplier(float value)
        {
            return Mathf.Clamp(Mathf.Round(value * 20f) / 20f, 0.1f, 20f);
        }
    }

    // Интеграция исходного Time Overhaul без собственного меню и второго Mod
    internal sealed class TimeControlSystem
    {
        private const string HarmonyId = "xskills.time.integrated";
        internal static TimeControlSystem Instance { get; private set; }
        internal TimeControlSettings Settings { get; private set; }
        private Harmony harmony;
        private string settingsPath;
        private bool dirty;
        private float saveAt;
        private float nextWorldPoll;
        private float nextClimatePoll;
        private float hungerClimateFactor = 1f;
        private float thirstClimateFactor = 1f;
        private Network_Player trackedLocalPlayer;
        private readonly Dictionary<Stat_Consumable, int> consumables = new Dictionary<Stat_Consumable, int>();
        private readonly Dictionary<Stat_Bonus_Consumable, int> groups = new Dictionary<Stat_Bonus_Consumable, int>();

        private AzureSkyController trackedSky;
        private float originalDayCycle;
        private float originalProgression;
        private float appliedDaySeconds = -1f;
        private float lastSkyVerification;
        private bool dayOverridden;
        private WeatherManager trackedWeatherManager;
        private Weather trackedWeather;
        private float originalWeatherRemaining;
        private float weatherElapsed;
        private bool weatherOverridden;
        private float observedSharkSearch;
        private float observedSharkRespawn;

        private static readonly FieldInfo SharkSearchField = AccessTools.Field(typeof(AI_StateMachine_Shark), "searchBlockInterval");
        private static readonly FieldInfo SkyProgressionField = AccessTools.Field(typeof(AzureSkyController), "m_timeProgression");
        private static readonly FieldInfo WeatherChangingField = AccessTools.Field(typeof(WeatherManager), "changingWeather");

        internal static bool IsHostInWorld
        {
            get { return Instance != null && LoadSceneManager.IsGameSceneLoaded && Raft_Network.IsHost; }
        }

        internal static float CurrentHungerRate
        {
            get { return IsHostInWorld ? Instance.hungerClimateFactor : 1f; }
        }
        internal static float CurrentThirstRate
        {
            get { return IsHostInWorld ? Instance.thirstClimateFactor : 1f; }
        }
        internal float HungerFactor { get { return hungerClimateFactor; } }
        internal float ThirstFactor { get { return thirstClimateFactor; } }
        internal float VanillaDaySeconds { get { return originalDayCycle > 0f ? Mathf.Round(originalDayCycle * 60f) : TimeControlSettings.DefaultDaySeconds; } }
        internal float VanillaSharkAttack { get { return observedSharkSearch > 0f ? Mathf.Round(observedSharkSearch) : TimeControlSettings.DefaultSharkAttack; } }
        internal float VanillaSharkRespawn { get { return observedSharkRespawn > 0f ? Mathf.Round(observedSharkRespawn) : TimeControlSettings.DefaultSharkRespawn; } }

        internal static void Initialize()
        {
            if (Instance != null) return;
            TimeControlSystem system = new TimeControlSystem();
            system.Settings = new TimeControlSettings();
            system.settingsPath = Path.Combine(Application.persistentDataPath, "xSkills/time-settings.json");
            system.LoadSettings();
            Instance = system;
            try
            {
                system.harmony = new Harmony(HarmonyId);
                // Регистрация только собственных классов исключает повторные патчи DietSystem
                Type[] patchClasses = {
                    typeof(TimeControlDayPatch), typeof(TimeControlPlantPatch), typeof(TimeControlPlantSleepPatch),
                    typeof(TimeControlSharkAttackPatch), typeof(TimeControlSharkRespawnDefaultsPatch),
                    typeof(TimeControlSharkRespawnPatch), typeof(TimeControlWeatherPatch),
                    typeof(TimeControlPlayerStatsPatch), typeof(TimeControlConsumableLossPatch),
                    typeof(TimeControlConsumableRemovalPatch)
                };
                foreach (Type patch in patchClasses)
                    system.harmony.CreateClassProcessor(patch).Patch();
            }
            catch (Exception error)
            {
                Debug.LogError("[xSkills Time] Patch initialization: " + error);
                if (system.harmony != null) system.harmony.UnpatchAll(HarmonyId);
                system.harmony = null;
            }
        }

        internal static void Tick()
        {
            TimeControlSystem system = Instance;
            if (system == null) return;
            float now = Time.unscaledTime;
            if (system.dirty && now >= system.saveAt) system.SaveSettings();
            if (!IsHostInWorld)
            {
                if (system.trackedLocalPlayer != null || system.trackedWeatherManager != null || system.trackedSky != null)
                    system.ResetWorld();
                return;
            }
            if (now >= system.nextWorldPoll)
            {
                system.nextWorldPoll = now + 2f;
                Network_Player local = null;
                try { local = RAPI.GetLocalPlayer(); } catch (Exception) { }
                if (local != system.trackedLocalPlayer)
                {
                    system.trackedLocalPlayer = local;
                    system.consumables.Clear();
                    system.groups.Clear();
                    if (local != null) system.RegisterPlayerStats(local.Stats);
                }
                else if (local != null && system.groups.Count == 0)
                {
                    system.RegisterPlayerStats(local.Stats);
                }
            }
            if (now >= system.nextClimatePoll)
            {
                system.nextClimatePoll = now + 1f;
                system.UpdateClimateRates();
            }
        }

        internal static void SettingsChanged()
        {
            if (Instance == null) return;
            Instance.Settings.Clamp();
            Instance.dirty = true;
            Instance.saveAt = Time.unscaledTime + 1.5f;
            Instance.nextClimatePoll = 0f;
        }

        internal static void Dispose()
        {
            TimeControlSystem system = Instance;
            if (system == null) return;
            system.ResetWorld();
            if (system.dirty) system.SaveSettings();
            if (system.harmony != null) system.harmony.UnpatchAll(HarmonyId);
            Instance = null;
        }

        private void ResetWorld()
        {
            RestoreDay();
            RestoreWeather();
            trackedLocalPlayer = null;
            consumables.Clear();
            groups.Clear();
            hungerClimateFactor = thirstClimateFactor = 1f;
            observedSharkSearch = observedSharkRespawn = 0f;
            nextWorldPoll = nextClimatePoll = 0f;
        }

        private void UpdateClimateRates()
        {
            hungerClimateFactor = 1f;
            thirstClimateFactor = 1f;
            if (!Settings.ClimateConsumption) return;
            if (!BodyTemperatureSystem.HasPlayer) return;
            float t = BodyTemperatureSystem.OutsideTemperature;
            float humidity = BodyTemperatureSystem.AirHumidity;
            float sun = BodyTemperatureSystem.IsShaded || BodyTemperatureSystem.IsRaining ? 0f : BodyTemperatureSystem.Daylight;
            float humidHeat = Mathf.Clamp01((humidity - 65f) / 30f);
            float hot = Mathf.Clamp01((t - 29f) / 9f);
            // Жажда зависит от жары, влажности и солнечной нагрузки
            thirstClimateFactor = 1f + hot * (0.26f + 0.20f * humidHeat) + hot * sun * 0.15f;
            // Холод усиливает голод, жара умеренно снижает аппетит
            float cool = Mathf.Clamp01((27f - t) / 8f);
            hungerClimateFactor = Mathf.Clamp(1f + cool * (0.10f + 0.08f * BodyTemperatureSystem.Wetness / 100f) - hot * 0.08f, 0.92f, 1.18f);
        }

        internal void RegisterPlayerStats(PlayerStats stats)
        {
            if (stats == null) return;
            RegisterGroup(stats.stat_hunger, 1);
            RegisterGroup(stats.stat_thirst, 2);
        }

        private void RegisterGroup(Stat_Bonus_Consumable group, int kind)
        {
            if (group == null) return;
            groups[group] = kind;
            Stat_Consumable normal = group.Normal as Stat_Consumable;
            Stat_Consumable bonus = group.Bonus as Stat_Consumable;
            if (normal != null) consumables[normal] = kind;
            if (bonus != null) consumables[bonus] = kind;
        }

        internal float GetDuration(Stat_Consumable stat)
        {
            if (stat == null) return 1f;
            int kind;
            return consumables.TryGetValue(stat, out kind) ? GetDurationByKind(kind) : 1f;
        }
        internal float GetDuration(Stat_Bonus_Consumable group)
        {
            if (group == null) return 1f;
            int kind;
            return groups.TryGetValue(group, out kind) ? GetDurationByKind(kind) : 1f;
        }
        private float GetDurationByKind(int kind)
        {
            if (kind == 1) return Mathf.Max(0.1f, Settings.HungerDurationMultiplier) / hungerClimateFactor;
            if (kind == 2) return Mathf.Max(0.1f, Settings.ThirstDurationMultiplier) / thirstClimateFactor;
            return 1f;
        }

        internal static float GetPlantGrowthDeltaTime(Plant plant)
        {
            return Time.deltaTime * GetPlantGrowthSpeed(plant);
        }
        internal static float GetPlantGrowthSpeed(Plant plant)
        {
            if (!IsHostInWorld || plant == null) return 1f;
            return 1f / Mathf.Max(0.1f, Instance.Settings.PlantGrowthMultiplier);
        }

        internal void CaptureSharkSearchInterval(AI_StateMachine_Shark shark)
        {
            if (observedSharkSearch > 0f || shark == null || SharkSearchField == null) return;
            object raw = SharkSearchField.GetValue(shark);
            if (raw is float && (float)raw > 0f) observedSharkSearch = (float)raw;
        }
        internal void CaptureSharkRespawnDelay(AI_State_Decay_Shark state)
        {
            if (observedSharkRespawn == 0f && state != null && state.decayRespawnTime > 0f)
                observedSharkRespawn = state.decayRespawnTime;
        }

        internal void ApplyDayLength(AzureSkyController controller)
        {
            if (controller == null || controller.timeOfDay == null || SkyProgressionField == null) return;
            if (trackedSky != controller)
            {
                RestoreDay();
                trackedSky = controller;
                originalDayCycle = controller.timeOfDay.dayCycle;
                originalProgression = (float)SkyProgressionField.GetValue(controller);
                appliedDaySeconds = -1f;
                dayOverridden = false;
                lastSkyVerification = 0f;
            }
            float seconds = Settings.DayLengthSeconds;
            if (Mathf.Approximately(seconds, appliedDaySeconds) &&
                Time.unscaledTime < lastSkyVerification) return;
            float minutes = seconds / 60f;
            float progression = 0.4f / minutes;
            if (!Mathf.Approximately(controller.timeOfDay.dayCycle, minutes))
                controller.timeOfDay.dayCycle = minutes;
            float previous = (float)SkyProgressionField.GetValue(controller);
            if (!Mathf.Approximately(previous, progression)) SkyProgressionField.SetValue(controller, progression);
            dayOverridden = true;
            appliedDaySeconds = seconds;
            lastSkyVerification = Time.unscaledTime + 3f;
        }
        private void RestoreDay()
        {
            if (dayOverridden && trackedSky != null && trackedSky.timeOfDay != null && SkyProgressionField != null)
            {
                trackedSky.timeOfDay.dayCycle = originalDayCycle;
                SkyProgressionField.SetValue(trackedSky, originalProgression);
            }
            trackedSky = null;
            dayOverridden = false;
            appliedDaySeconds = -1f;
            originalDayCycle = 0f;
        }

        internal void ApplyWeather(WeatherManager manager)
        {
            if (manager == null || GameModeValueManager.GetCurrentGameModeValue().gameMode == GameMode.Creative) return;
            float multiplier = Settings.WeatherDurationMultiplier;
            if (Mathf.Approximately(multiplier, 1f) && !weatherOverridden && trackedWeatherManager == null)
                return;
            if (WeatherChangingField != null && (bool)WeatherChangingField.GetValue(manager)) return;
            Weather current = manager.GetCurrentWeather();
            if (current == null) return;
            if (manager != trackedWeatherManager || trackedWeather != current)
            {
                trackedWeatherManager = manager;
                trackedWeather = current;
                originalWeatherRemaining = manager.WeatherTimer;
                weatherElapsed = 0f;
                weatherOverridden = false;
            }
            else weatherElapsed += Time.deltaTime;
            if (Mathf.Approximately(multiplier, 1f) && !weatherOverridden)
            {
                trackedWeatherManager = null;
                trackedWeather = null;
                return;
            }
            manager.WeatherTimer = Mathf.Max(0f, originalWeatherRemaining * multiplier - weatherElapsed);
            weatherOverridden = !Mathf.Approximately(multiplier, 1f);
        }
        private void RestoreWeather()
        {
            if (weatherOverridden && trackedWeatherManager != null && trackedWeatherManager.GetCurrentWeather() == trackedWeather)
                trackedWeatherManager.WeatherTimer = Mathf.Max(0f, originalWeatherRemaining - weatherElapsed);
            trackedWeatherManager = null;
            trackedWeather = null;
            weatherOverridden = false;
        }

        private void LoadSettings()
        {
            try
            {
                string legacy = Path.Combine(Application.persistentDataPath, "TimeOverhaul/settings.json");
                bool currentExists = File.Exists(settingsPath);
                string path = currentExists ? settingsPath : legacy;
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path);
                TimeControlSettings loaded = JsonUtility.FromJson<TimeControlSettings>(json);
                if (loaded == null) return;
                if (loaded.FormatVersion == 4 || (currentExists && loaded.FormatVersion >= 4))
                {
                    Settings = loaded;
                }
                else
                {
                    // Старый файл v2/v3 не содержит всех современных параметров
                    Settings.DayLengthSeconds = loaded.DayLengthSeconds;
                    Settings.SharkAttackSeconds = loaded.SharkAttackSeconds;
                    Settings.SharkRespawnSeconds = loaded.SharkRespawnSeconds;
                    Settings.PlantGrowthMultiplier = loaded.FormatVersion >= 3 ? loaded.PlantGrowthMultiplier : 1f;
                    Settings.WeatherDurationMultiplier = loaded.FormatVersion >= 3 ? loaded.WeatherDurationMultiplier : 1f;
                    Settings.HungerDurationMultiplier = loaded.FormatVersion >= 4 ? loaded.HungerDurationMultiplier : 1f;
                    Settings.ThirstDurationMultiplier = loaded.FormatVersion >= 4 ? loaded.ThirstDurationMultiplier : 1f;
                }
                if (!json.Contains("\"ClimateConsumption\"")) Settings.ClimateConsumption = true;
                if (!json.Contains("\"RainFrequencyPercent\"")) Settings.RainFrequencyPercent = TimeControlSettings.DefaultRainFrequencyPercent;
                if (!json.Contains("\"RainDurationMultiplier\"")) Settings.RainDurationMultiplier = TimeControlSettings.DefaultRainDurationMultiplier;
                if (!json.Contains("\"RainVariationPercent\"")) Settings.RainVariationPercent = TimeControlSettings.DefaultRainVariationPercent;
                Settings.FormatVersion = 5;
                Settings.Clamp();
                if (!currentExists) { dirty = true; saveAt = 0f; }
            }
            catch (Exception ex) { Debug.LogWarning("[xSkills Time] Settings load: " + ex.Message); }
        }
        private void SaveSettings()
        {
            try
            {
                Settings.Clamp();
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllText(settingsPath, JsonUtility.ToJson(Settings, true));
                dirty = false;
            }
            catch (Exception ex)
            {
                saveAt = Time.unscaledTime + 5f;
                Debug.LogWarning("[xSkills Time] Settings save: " + ex.Message);
            }
        }
    }

[HarmonyPatch(typeof(AzureSkyController), "Update")]
internal static class TimeControlDayPatch
{
    private static void Prefix(AzureSkyController __instance)
    {
        if (TimeControlSystem.IsHostInWorld)
            TimeControlSystem.Instance.ApplyDayLength(__instance);
    }
}

[HarmonyPatch(typeof(Plant), "Grow")]
internal static class TimeControlPlantPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> code = new List<CodeInstruction>(instructions);
        MethodInfo original = AccessTools.PropertyGetter(typeof(Time), "deltaTime");
        MethodInfo replacement = AccessTools.Method(typeof(TimeControlSystem), "GetPlantGrowthDeltaTime");
        List<int> indexes = new List<int>();
        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].Calls(original))
                indexes.Add(i);
        }
        if (indexes.Count == 2 && replacement != null)
        {
            int index = indexes[1];
            CodeInstruction loadPlant = new CodeInstruction(OpCodes.Ldarg_0);
            loadPlant.labels.AddRange(code[index].labels);
            code[index].labels.Clear();
            code.Insert(index, loadPlant);
            code[index + 1].operand = replacement;
        }
        else
        {
            Debug.LogWarning("[xSkills Time] Plant.Grow layout changed - patch skipped");
        }
        return code;
    }
}

[HarmonyPatch(typeof(Plant), "IncrementGrowTimer")]
internal static class TimeControlPlantSleepPatch
{
    private static void Prefix(Plant __instance, ref float amount)
    {
        amount *= TimeControlSystem.GetPlantGrowthSpeed(__instance);
    }
}

[HarmonyPatch(typeof(AI_StateMachine_Shark), "get_SearchBlockInterval")]
internal static class TimeControlSharkAttackPatch
{
    private static void Postfix(AI_StateMachine_Shark __instance, ref float __result)
    {
        if (!TimeControlSystem.IsHostInWorld)
            return;
        TimeControlSystem.Instance.CaptureSharkSearchInterval(__instance);
        float difficultyMultiplier =
            GameModeValueManager.GetCurrentGameModeValue().sharkVariables.attackRateMultiplier;
        __result = TimeControlSystem.Instance.Settings.SharkAttackSeconds /
            Mathf.Max(0.01f, difficultyMultiplier);
    }
}

[HarmonyPatch(typeof(AI_State_Decay_Shark), "Start")]
internal static class TimeControlSharkRespawnDefaultsPatch
{
    private static void Postfix(AI_State_Decay_Shark __instance)
    {
        if (TimeControlSystem.IsHostInWorld)
            TimeControlSystem.Instance.CaptureSharkRespawnDelay(__instance);
    }
}

[HarmonyPatch(typeof(Network_Host_Entities), "CreateShark")]
internal static class TimeControlSharkRespawnPatch
{
    private static bool Prefix(Network_Host_Entities __instance, float timeDelay,
        AI_NetworkBehaviourType type, ref IEnumerator __result)
    {
        if (!TimeControlSystem.IsHostInWorld)
            return true;
        __result = DynamicRespawn(__instance, timeDelay, type);
        return false;
    }

    private static IEnumerator DynamicRespawn(Network_Host_Entities host, float originalDelay,
        AI_NetworkBehaviourType type)
    {
        float remaining = Mathf.Max(0f, originalDelay);
        while (remaining > 0f)
        {
            float speed = 1f;
            if (TimeControlSystem.IsHostInWorld)
                speed = Mathf.Max(0.01f, originalDelay) /
                    Mathf.Max(1f, TimeControlSystem.Instance.Settings.SharkRespawnSeconds);
            remaining -= Time.deltaTime * speed;
            yield return null;
        }
        if (host != null && Raft_Network.IsHost)
            host.CreateAINetworkBehaviour(type, host.GetSharkSpawnPosition(), null);
    }
}

[HarmonyPatch(typeof(WeatherManager), "Update")]
internal static class TimeControlWeatherPatch
{
    private static void Prefix(WeatherManager __instance)
    {
        if (TimeControlSystem.IsHostInWorld)
            TimeControlSystem.Instance.ApplyWeather(__instance);
    }
}

[HarmonyPatch(typeof(PlayerStats), "Start")]
internal static class TimeControlPlayerStatsPatch
{
    private static void Postfix(PlayerStats __instance)
    {
        if (!TimeControlSystem.IsHostInWorld || __instance == null) return;
        Network_Player player = __instance.GetComponent<Network_Player>();
        if (player != null && player.IsLocalPlayer)
            TimeControlSystem.Instance.RegisterPlayerStats(__instance);
    }
}

[HarmonyPatch(typeof(Stat_Consumable), "get_LostPerSecond")]
internal static class TimeControlConsumableLossPatch
{
    private static void Postfix(Stat_Consumable __instance, ref float __result)
    {
        if (!TimeControlSystem.IsHostInWorld)
            return;

        float duration = TimeControlSystem.Instance.GetDuration(__instance);
        __result /= duration;
    }
}

[HarmonyPatch(typeof(Stat_Bonus_Consumable), "RemoveValue")]
internal static class TimeControlConsumableRemovalPatch
{
    private static void Prefix(Stat_Bonus_Consumable __instance, ref float amount)
    {
        if (!TimeControlSystem.IsHostInWorld)
            return;

        float duration = TimeControlSystem.Instance.GetDuration(__instance);
        amount /= duration;
    }
}

}
