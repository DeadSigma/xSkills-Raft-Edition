using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using HMLLibrary;
using UnityEngine;
using xSkills.Skills;
using xSkills.Systems;

namespace xSkills.Services
{
    internal static class SkillProgressStorage
    {
        private const string SavePrefix = "xSkills.Progress.v1.";
        private const float PollInterval = 0.75f;
        private const float ExperienceSaveDelay = 1.5f;

        private static bool initialized;
        private static bool restoring;
        private static bool dirty;
        private static float nextPoll;
        private static float saveAt;
        private static Network_Player currentPlayer;
        private static Guid currentWorld;
        private static string saveKey;
        private static string savePath;
        private static int revision;

        [DataContract]
        private sealed class SavedAbility
        {
            [DataMember(Order = 1)] public string Id;
            [DataMember(Order = 2)] public int Tier;
        }

        [DataContract]
        private sealed class SavedSkill
        {
            [DataMember(Order = 1)] public string Id;
            [DataMember(Order = 2)] public int Level;
            [DataMember(Order = 3)] public float Experience;
            [DataMember(Order = 4)] public SavedAbility[] Abilities;
        }

        [DataContract]
        private sealed class SavedProgress
        {
            [DataMember(Order = 1)] public int Version = 1;
            [DataMember(Order = 2)] public int Revision;
            [DataMember(Order = 3)] public int UnlearnPoints;
            [DataMember(Order = 4)] public SavedSkill[] Skills;
            [DataMember(Order = 5, EmitDefaultValue = false)] public WaterProgressData Water;
            [DataMember(Order = 6, EmitDefaultValue = false)] public float SurvivalElapsedMinutes;
        }

        private static readonly DataContractJsonSerializer ProgressSerializer =
            new DataContractJsonSerializer(typeof(SavedProgress));

        internal static bool IsWorldBound { get { return saveKey != null; } }

        internal static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            SkillCatalog.Changed += OnChanged;
            SkillCatalog.ExperienceGained += OnExperienceGained;
            SkillCatalog.LevelGained += OnLevelGained;
        }

        internal static void Tick()
        {
            if (!initialized) return;
            if (Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + PollInterval;
                EnsureLoaded();
            }
            if (dirty && saveKey != null && Time.unscaledTime >= saveAt)
                Save();
        }

        internal static void EnsureLoaded()
        {
            if (!initialized || restoring) return;

            bool inWorld;
            try { inWorld = RAPI.IsCurrentSceneGame(); }
            catch { return; }

            if (!inWorld)
            {
                if (saveKey != null) Detach();
                return;
            }

            Network_Player player;
            Guid world;
            try
            {
                if (SaveAndLoad.IsGameLoading) return;
                world = SaveAndLoad.WorldGuid;
                player = RAPI.GetLocalPlayer();
            }
            catch { return; }

            if (world == Guid.Empty || player == null || player.Stats == null) return;

            ulong numericId = player.steamID;
            if (numericId == 0UL) return;
            string id = numericId.ToString(CultureInfo.InvariantCulture);
            string key = SavePrefix + world.ToString("N") + "_" + id;
            if (currentPlayer == player && currentWorld == world && saveKey == key) return;

            if (saveKey != null) Detach();
            currentPlayer = player;
            currentWorld = world;
            saveKey = key;
            savePath = Path.Combine(Application.persistentDataPath, "xSkills/progress/" +
                world.ToString("N") + "_" + id + ".json");
            Restore();
        }

        private static string Serialize(SavedProgress progress)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                ProgressSerializer.WriteObject(stream, progress);
                return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
            }
        }

        private static SavedProgress Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                using (MemoryStream stream = new MemoryStream(bytes, false))
                {
                    SavedProgress result = ProgressSerializer.ReadObject(stream) as SavedProgress;
                    return result != null && result.Version == 1 &&
                        result.Skills != null && result.Skills.Length > 0 ? result : null;
                }
            }
            catch { return null; }
        }

        private static SavedProgress ReadFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return Parse(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Could not read skill progress: " + ex.Message);
                return null;
            }
        }

        private static void Restore()
        {
            restoring = true;
            dirty = false;
            revision = 0;
            bool migratedWater = false;
            try
            {
                SavedProgress progress = null;
                SavedProgress fromFile = ReadFile(savePath);
                SavedProgress fromBackup = ReadFile(savePath + ".bak");
                SavedProgress fromPrefs = Parse(PlayerPrefs.GetString(saveKey, string.Empty));
                if (fromPrefs != null) progress = fromPrefs;
                if (fromBackup != null && (progress == null || fromBackup.Revision > progress.Revision))
                    progress = fromBackup;
                if (fromFile != null && (progress == null || fromFile.Revision >= progress.Revision))
                    progress = fromFile;

                if (progress == null && !saveKey.EndsWith("_0", StringComparison.Ordinal))
                    progress = Parse(PlayerPrefs.GetString(SavePrefix + currentWorld.ToString("N") + "_0", string.Empty));

                List<SkillData> skills = SkillCatalog.Create();
                for (int i = 0; i < skills.Count; i++) skills[i].Reset();
                SkillCatalog.SetUnlearnPoints(0);
                if (progress != null)
                {
                    revision = Math.Max(0, progress.Revision);
                    SkillCatalog.SetUnlearnPoints(Math.Max(0, progress.UnlearnPoints));
                    for (int i = 0; i < progress.Skills.Length; i++)
                    {
                        SavedSkill stored = progress.Skills[i];
                        if (stored == null || string.IsNullOrEmpty(stored.Id)) continue;
                        SkillData skill = SkillCatalog.Find(stored.Id);
                        if (skill == null) continue;
                        Dictionary<string, int> tiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        if (stored.Abilities != null)
                        {
                            for (int j = 0; j < stored.Abilities.Length; j++)
                            {
                                SavedAbility ability = stored.Abilities[j];
                                if (ability != null && !string.IsNullOrEmpty(ability.Id))
                                    tiers[ability.Id] = ability.Tier;
                            }
                        }
                        skill.RestoreProgress(stored.Level, stored.Experience, tiers);
                    }
                }
                WaterProgressData water = progress != null ? progress.Water : null;
                if (water == null)
                {
                    water = WaterSurvivalSystem.TryImportBetterRaft();
                    migratedWater = water != null;
                }
                WaterSurvivalSystem.RestoreProgress(water);
                SurvivalExperienceSystem.RestoreProgress(progress != null ? progress.SurvivalElapsedMinutes : 0f);
                SkillCatalog.NotifyChanged();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Skill progress load failed: " + ex);
            }
            finally
            {
                restoring = false;
                if (migratedWater)
                {
                    dirty = true;
                    saveAt = Time.unscaledTime + .5f;
                }
            }
        }

        private static void OnChanged()
        {
            MarkDirty(true);
        }

        private static void OnExperienceGained(SkillData skill, float amount)
        {
            MarkDirty(false);
        }

        private static void OnLevelGained(SkillData skill, int level)
        {
            MarkDirty(true);
        }

        internal static void MarkExternalChanged(bool immediate)
        {
            MarkDirty(immediate);
        }

        private static void MarkDirty(bool immediate)
        {
            if (restoring || saveKey == null) return;
            if (!dirty) saveAt = Time.unscaledTime + ExperienceSaveDelay;
            dirty = true;
            if (immediate) Save();
        }

        private static SavedProgress Collect()
        {
            List<SkillData> skills = SkillCatalog.Create();
            SavedProgress progress = new SavedProgress();
            progress.Revision = revision + 1;
            progress.UnlearnPoints = SkillCatalog.UnlearnPoints;
            progress.Water = WaterSurvivalSystem.CollectProgress();
            progress.SurvivalElapsedMinutes = SurvivalExperienceSystem.CollectProgress();
            progress.Skills = new SavedSkill[skills.Count];
            for (int i = 0; i < skills.Count; i++)
            {
                SkillData skill = skills[i];
                SavedSkill entry = new SavedSkill();
                entry.Id = skill.Id;
                entry.Level = skill.Level;
                entry.Experience = skill.Experience;
                entry.Abilities = new SavedAbility[skill.Abilities.Count];
                for (int j = 0; j < skill.Abilities.Count; j++)
                {
                    AbilityData ability = skill.Abilities[j];
                    entry.Abilities[j] = new SavedAbility { Id = ability.Id, Tier = ability.PreviewTier };
                }
                progress.Skills[i] = entry;
            }
            return progress;
        }

        private static string SavedSurvival(SavedProgress progress)
        {
            if (progress == null || progress.Skills == null) return "missing";
            for (int i = 0; i < progress.Skills.Length; i++)
            {
                SavedSkill skill = progress.Skills[i];
                if (skill == null || !string.Equals(skill.Id, "survival", StringComparison.OrdinalIgnoreCase))
                    continue;
                int tier = 0;
                if (skill.Abilities != null)
                    for (int j = 0; j < skill.Abilities.Length; j++)
                    {
                        SavedAbility ability = skill.Abilities[j];
                        if (ability != null && string.Equals(ability.Id, "forecast", StringComparison.OrdinalIgnoreCase))
                        { tier = ability.Tier; break; }
                    }
                return skill.Level + "/" + tier;
            }
            return "no-survival";
        }

        private static void Save()
        {
            if (saveKey == null || !dirty) return;
            try
            {
                SavedProgress progress = Collect();
                string json = Serialize(progress);
                SavedProgress roundtrip = Parse(json);
                if (roundtrip == null || roundtrip.Skills.Length != progress.Skills.Length ||
                    SavedSurvival(roundtrip) != SavedSurvival(progress) ||
                    roundtrip.Water == null ||
                    roundtrip.Water.FatigueLevel != progress.Water.FatigueLevel ||
                    roundtrip.Water.LungLevel != progress.Water.LungLevel ||
                    Math.Abs(roundtrip.Water.OxygenTrainingSeconds - progress.Water.OxygenTrainingSeconds) > .01f ||
                    Math.Abs(roundtrip.Water.FatigueTrainingSeconds - progress.Water.FatigueTrainingSeconds) > .01f ||
                    Math.Abs(roundtrip.SurvivalElapsedMinutes - progress.SurvivalElapsedMinutes) > .01f)
                    throw new InvalidOperationException("Skill progress JSON failed round-trip validation");

                bool written = false;
                try
                {
                    PlayerPrefs.SetString(saveKey, json);
                    PlayerPrefs.Save();
                    written = true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[xSkills] PlayerPrefs skill save failed: " + ex.Message);
                }

                try
                {
                    string directory = Path.GetDirectoryName(savePath);
                    Directory.CreateDirectory(directory);
                    string temporary = savePath + ".tmp";
                    File.WriteAllText(temporary, json, Encoding.UTF8);
                    if (File.Exists(savePath))
                    {
                        try { File.Replace(temporary, savePath, savePath + ".bak"); }
                        catch (PlatformNotSupportedException)
                        {
                            File.Copy(savePath, savePath + ".bak", true);
                            File.Copy(temporary, savePath, true);
                            File.Delete(temporary);
                        }
                    }
                    else File.Move(temporary, savePath);
                    written = true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[xSkills] File skill save failed: " + ex.Message);
                }

                if (!written) throw new IOException("No skill progress storage is writable");
                revision = progress.Revision;
                dirty = false;
            }
            catch (Exception ex)
            {
                dirty = true;
                saveAt = Time.unscaledTime + 5f;
                Debug.LogWarning("[xSkills] Skill progress save failed: " + ex.Message);
            }
        }

        private static void Detach()
        {
            if (saveKey == null) return;
            WaterSurvivalSystem.FlushProgress();
            SurvivalExperienceSystem.FlushProgress();
            Save();
            saveKey = null;
            savePath = null;
            currentPlayer = null;
            currentWorld = Guid.Empty;
            dirty = false;
            restoring = true;
            try
            {
                List<SkillData> skills = SkillCatalog.Create();
                for (int i = 0; i < skills.Count; i++) skills[i].Reset();
                SkillCatalog.SetUnlearnPoints(0);
                WaterSurvivalSystem.RestoreProgress(null);
                SurvivalExperienceSystem.Reset();
                SkillCatalog.NotifyChanged();
            }
            finally { restoring = false; }
        }

        internal static void Dispose()
        {
            if (!initialized) return;
            WaterSurvivalSystem.FlushProgress();
            SurvivalExperienceSystem.FlushProgress();
            Save();
            SkillCatalog.Changed -= OnChanged;
            SkillCatalog.ExperienceGained -= OnExperienceGained;
            SkillCatalog.LevelGained -= OnLevelGained;
            saveKey = null;
            savePath = null;
            currentPlayer = null;
            currentWorld = Guid.Empty;
            dirty = false;
            restoring = false;
            initialized = false;
            nextPoll = 0f;
        }
    }
}
