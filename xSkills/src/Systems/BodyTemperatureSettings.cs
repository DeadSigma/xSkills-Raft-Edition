using UnityEngine;

namespace xSkills.Systems
{
    internal sealed class BodyTemperatureSettings
    {
        private const string Prefix = "xskills_temperature_v1_";
        private static BodyTemperatureSettings current;
        private static bool dirty;
        private static float saveAt;

        public bool ColdEnabled = true;
        public float ColdSlowStart = 35f;
        public float ColdMinimumSpeed = 0.60f;
        public float ColdDamageStart = 33f;
        public float ColdBaseDamage = 2f;
        public float ColdDamagePerDegree = 2f;

        public bool HeatEnabled = true;
        public float HeatSlowStart = 38f;
        public float HeatMinimumSpeed = 0.90f;
        public float HeatDamageStart = 40f;
        public float HeatBaseDamage = 2.4f;
        public float HeatDamagePerDegree = 5.6f;

        public bool DamageEnabled = true;
        public float DamageInterval = 2f;
        public bool NauseaEnabled = true;
        public bool NauseaDistortionEnabled = true;
        public float NauseaStart = 39f;
        public float NauseaStrength = 1f;

        public static BodyTemperatureSettings Current
        {
            get { return current ?? (current = Load()); }
        }

        private static bool Bool(string key, bool fallback)
        {
            return PlayerPrefs.GetInt(Prefix + key, fallback ? 1 : 0) != 0;
        }

        private static float Float(string key, float fallback, float min, float max)
        {
            float value = PlayerPrefs.GetFloat(Prefix + key, fallback);
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
        }

        private static BodyTemperatureSettings Load()
        {
            var s = new BodyTemperatureSettings();
            s.ColdEnabled = Bool("cold_enabled", s.ColdEnabled);
            s.ColdSlowStart = Float("cold_slow", s.ColdSlowStart, 32f, 36.4f);
            s.ColdMinimumSpeed = Float("cold_speed", s.ColdMinimumSpeed, 0.3f, 1f);
            s.ColdDamageStart = Float("cold_damage_start", s.ColdDamageStart, 31f, 35f);
            s.ColdBaseDamage = Float("cold_damage_base", s.ColdBaseDamage, 0f, 30f);
            s.ColdDamagePerDegree = Float("cold_damage_slope", s.ColdDamagePerDegree, 0f, 25f);
            s.HeatEnabled = Bool("heat_enabled", s.HeatEnabled);
            s.HeatSlowStart = Float("heat_slow", s.HeatSlowStart, 37f, 40f);
            s.HeatMinimumSpeed = Float("heat_speed", s.HeatMinimumSpeed, 0.5f, 1f);
            s.HeatDamageStart = Float("heat_damage_start", s.HeatDamageStart, 38f, 41f);
            s.HeatBaseDamage = Float("heat_damage_base", s.HeatBaseDamage, 0f, 30f);
            s.HeatDamagePerDegree = Float("heat_damage_slope", s.HeatDamagePerDegree, 0f, 30f);
            s.DamageEnabled = Bool("damage_enabled", s.DamageEnabled);
            s.DamageInterval = Float("damage_interval", s.DamageInterval, .5f, 10f);
            s.NauseaEnabled = Bool("nausea_enabled", s.NauseaEnabled);
            s.NauseaDistortionEnabled = Bool("nausea_distortion", s.NauseaDistortionEnabled);
            s.NauseaStart = Float("nausea_start", s.NauseaStart, 37f, 40.5f);
            s.NauseaStrength = Float("nausea_strength", s.NauseaStrength, 0f, 2f);
            MigrateDamageBalance(s);
            return s;
        }

        private static void MigrateDamageBalance(BodyTemperatureSettings s)
        {
            const string versionKey = Prefix + "damage_balance_version";
            if (PlayerPrefs.GetInt(versionKey, 1) >= 2) return;

            if (PlayerPrefs.HasKey(Prefix + "cold_damage_base"))
            {
                s.ColdBaseDamage *= 0.4f;
                PlayerPrefs.SetFloat(Prefix + "cold_damage_base", s.ColdBaseDamage);
            }
            if (PlayerPrefs.HasKey(Prefix + "cold_damage_slope"))
            {
                s.ColdDamagePerDegree *= 0.4f;
                PlayerPrefs.SetFloat(Prefix + "cold_damage_slope", s.ColdDamagePerDegree);
            }
            if (PlayerPrefs.HasKey(Prefix + "heat_damage_base"))
            {
                s.HeatBaseDamage *= 0.4f;
                PlayerPrefs.SetFloat(Prefix + "heat_damage_base", s.HeatBaseDamage);
            }
            if (PlayerPrefs.HasKey(Prefix + "heat_damage_slope"))
            {
                s.HeatDamagePerDegree *= 0.4f;
                PlayerPrefs.SetFloat(Prefix + "heat_damage_slope", s.HeatDamagePerDegree);
            }
            PlayerPrefs.SetInt(versionKey, 2);
            PlayerPrefs.Save();
        }

        public static void Changed()
        {
            var s = Current;
            PlayerPrefs.SetInt(Prefix + "cold_enabled", s.ColdEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "cold_slow", s.ColdSlowStart);
            PlayerPrefs.SetFloat(Prefix + "cold_speed", s.ColdMinimumSpeed);
            PlayerPrefs.SetFloat(Prefix + "cold_damage_start", s.ColdDamageStart);
            PlayerPrefs.SetFloat(Prefix + "cold_damage_base", s.ColdBaseDamage);
            PlayerPrefs.SetFloat(Prefix + "cold_damage_slope", s.ColdDamagePerDegree);
            PlayerPrefs.SetInt(Prefix + "heat_enabled", s.HeatEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "heat_slow", s.HeatSlowStart);
            PlayerPrefs.SetFloat(Prefix + "heat_speed", s.HeatMinimumSpeed);
            PlayerPrefs.SetFloat(Prefix + "heat_damage_start", s.HeatDamageStart);
            PlayerPrefs.SetFloat(Prefix + "heat_damage_base", s.HeatBaseDamage);
            PlayerPrefs.SetFloat(Prefix + "heat_damage_slope", s.HeatDamagePerDegree);
            PlayerPrefs.SetInt(Prefix + "damage_enabled", s.DamageEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "damage_interval", s.DamageInterval);
            PlayerPrefs.SetInt(Prefix + "nausea_enabled", s.NauseaEnabled ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "nausea_distortion", s.NauseaDistortionEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "nausea_start", s.NauseaStart);
            PlayerPrefs.SetFloat(Prefix + "nausea_strength", s.NauseaStrength);
            dirty = true;
            saveAt = Time.unscaledTime + 1.5f;
        }

        public static void Tick()
        {
            if (dirty && Time.unscaledTime >= saveAt) Flush();
        }

        public static void Flush()
        {
            if (!dirty) return;
            PlayerPrefs.Save();
            dirty = false;
        }

        public static void ResetCold()
        {
            var s = Current;
            var defaults = new BodyTemperatureSettings();
            s.ColdEnabled = defaults.ColdEnabled;
            s.ColdSlowStart = defaults.ColdSlowStart;
            s.ColdMinimumSpeed = defaults.ColdMinimumSpeed;
            s.ColdDamageStart = defaults.ColdDamageStart;
            s.ColdBaseDamage = defaults.ColdBaseDamage;
            s.ColdDamagePerDegree = defaults.ColdDamagePerDegree;
            s.DamageInterval = defaults.DamageInterval;
            s.DamageEnabled = defaults.DamageEnabled;
            Changed();
        }

        public static void ResetHeat()
        {
            var s = Current;
            var defaults = new BodyTemperatureSettings();
            s.HeatEnabled = defaults.HeatEnabled;
            s.HeatSlowStart = defaults.HeatSlowStart;
            s.HeatMinimumSpeed = defaults.HeatMinimumSpeed;
            s.HeatDamageStart = defaults.HeatDamageStart;
            s.HeatBaseDamage = defaults.HeatBaseDamage;
            s.HeatDamagePerDegree = defaults.HeatDamagePerDegree;
            s.DamageEnabled = defaults.DamageEnabled;
            s.DamageInterval = defaults.DamageInterval;
            s.NauseaEnabled = defaults.NauseaEnabled;
            s.NauseaDistortionEnabled = defaults.NauseaDistortionEnabled;
            s.NauseaStart = defaults.NauseaStart;
            s.NauseaStrength = defaults.NauseaStrength;
            Changed();
        }
    }
}
