using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using UnityEngine;

namespace xSkills.Systems
{
    [DataContract]
    internal sealed class WaterSurvivalSettings
    {
        [DataMember] public float InitialWaterSeconds = 150f;
        [DataMember] public float WaterSecondsPerLevel = 20f;
        [DataMember] public float TrainingSeconds = 150f;
        [DataMember] public float WaterTrainingGrowth = 1.25f;
        [DataMember] public float FatigueRegenPerSecond = 10f;
        [DataMember] public float FatigueRegenCooldown = 3f;
        [DataMember] public float InitialOxygen = 200f;
        [DataMember] public float OxygenPerLevel = 15f;
        [DataMember] public float OxygenLossPerSecond = 30f;
        [DataMember] public float OxygenLossReduction = .5f;
        [DataMember] public float DivesToLevel = 10f;
        [DataMember] public float TrainingUnderwaterSeconds = 150f;
        [DataMember] public int SettingsVersion = 2;
        [DataMember] public float DiveTrainingGrowth = 1.2f;
        [DataMember] public float OxygenRegenMultiplier = .5f;
        [DataMember] public float FishingDelayMultiplier = 1.5f;
        [DataMember] public float FishingSpeedPerRank = .2f;
        [DataMember] public float FishingExperiencePerCatch = 12f;
        [DataMember] public bool FatigueEnabled = true;
        [DataMember] public bool OxygenEnabled = true;

        internal static WaterSurvivalSettings Current { get; private set; }
        private static bool dirty;
        private static float saveAt;
        private static string path;
        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(WaterSurvivalSettings));

        internal static void Initialize()
        {
            if (Current != null) return;
            path = Path.Combine(Application.persistentDataPath, "xSkills/water-settings.json");
            Current = new WaterSurvivalSettings();
            Current = Read(path) ?? Read(path + ".bak") ?? Current;
            if (Current.SettingsVersion < 2)
            {
                // Старая настройка количества погружений переводится в секунды
                Current.TrainingUnderwaterSeconds = Mathf.Clamp(
                    Mathf.Max(1f, Current.DivesToLevel) * 15f, 15f, 3600f);
                Current.SettingsVersion = 2;
                dirty = true;
                saveAt = Time.unscaledTime + 1.5f;
            }
            Current.Clamp();
        }

        private static WaterSurvivalSettings Read(string filename)
        {
            if (!File.Exists(filename)) return null;
            try
            {
                using (FileStream stream = File.OpenRead(filename))
                    return Serializer.ReadObject(stream) as WaterSurvivalSettings;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Water settings load: " + ex.Message);
                return null;
            }
        }

        internal void Clamp()
        {
            InitialWaterSeconds = Mathf.Clamp(InitialWaterSeconds, 15f, 3600f);
            WaterSecondsPerLevel = Mathf.Clamp(WaterSecondsPerLevel, 0f, 300f);
            TrainingSeconds = Mathf.Clamp(TrainingSeconds, 15f, 3600f);
            WaterTrainingGrowth = Mathf.Clamp(WaterTrainingGrowth, 1f, 3f);
            FatigueRegenPerSecond = Mathf.Clamp(FatigueRegenPerSecond, .1f, 120f);
            FatigueRegenCooldown = Mathf.Clamp(FatigueRegenCooldown, 0f, 30f);
            InitialOxygen = Mathf.Clamp(InitialOxygen, 30f, 3000f);
            OxygenPerLevel = Mathf.Clamp(OxygenPerLevel, 0f, 300f);
            OxygenLossPerSecond = Mathf.Clamp(OxygenLossPerSecond, 1f, 200f);
            OxygenLossReduction = Mathf.Clamp(OxygenLossReduction, 0f, 10f);
            DivesToLevel = Mathf.Clamp(DivesToLevel, 1f, 100f);
            TrainingUnderwaterSeconds = Mathf.Clamp(TrainingUnderwaterSeconds, 15f, 3600f);
            DiveTrainingGrowth = Mathf.Clamp(DiveTrainingGrowth, 1f, 3f);
            OxygenRegenMultiplier = Mathf.Clamp(OxygenRegenMultiplier, .05f, 2f);
            FishingDelayMultiplier = Mathf.Clamp(FishingDelayMultiplier, .2f, 5f);
            FishingSpeedPerRank = Mathf.Clamp(FishingSpeedPerRank, 0f, 1f);
            FishingExperiencePerCatch = Mathf.Clamp(FishingExperiencePerCatch, 0f, 1000f);
        }

        internal static void Changed()
        {
            if (Current == null) return;
            Current.Clamp();
            dirty = true;
            saveAt = Time.unscaledTime + 1.5f;
        }

        internal static void Tick()
        {
            if (dirty && Time.unscaledTime >= saveAt) Save();
        }

        private static void Save()
        {
            if (!dirty || Current == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                using (FileStream stream = File.Create(tmp)) Serializer.WriteObject(stream, Current);
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, path + ".bak"); }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(path, path + ".bak", true);
                        File.Copy(tmp, path, true);
                        File.Delete(tmp);
                    }
                }
                else File.Move(tmp, path);
                dirty = false;
            }
            catch (Exception ex)
            {
                saveAt = Time.unscaledTime + 5f;
                Debug.LogWarning("[xSkills] Water settings save: " + ex.Message);
            }
        }

        internal static void Dispose()
        {
            if (dirty) Save();
            Current = null;
            dirty = false;
            path = null;
        }
    }
}
