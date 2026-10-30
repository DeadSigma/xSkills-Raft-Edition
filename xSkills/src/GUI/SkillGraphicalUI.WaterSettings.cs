using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using xSkills.Services;
using xSkills.Systems;

namespace xSkills.GUI
{
    internal sealed partial class SkillGraphicalUI
    {
        private void BuildWaterSurvivalSettings(RectTransform panel, bool fatigue)
        {
            WaterSurvivalSettings s = WaterSurvivalSettings.Current;
            if (s == null) return;
            RectTransform list;
            BuildWaterScroll(panel, out list);
            float y = 4f;
            UIFactory.Label(list, "Water settings intro", Localizer.T(fatigue ?
                "settings-water-intro" : "settings-oxygen-intro"),
                20, y, 565, 72, 19, UIFactory.White, TextAnchor.UpperLeft);
            y += 80f;
            if (fatigue)
            {
                WaterToggle(list, "fatigue enabled", "settings-water-enabled", ref y,
                    () => s.FatigueEnabled, v => s.FatigueEnabled = v);
                WaterSlider(list, "base fatigue", "settings-water-base-seconds", ref y,
                    15f, 900f, s.InitialWaterSeconds, v => s.InitialWaterSeconds = v, Localizer.T("settings-time-second-symbol"), true);
                WaterSlider(list, "fatigue per level", "settings-water-bonus-per-level", ref y,
                    0f, 180f, s.WaterSecondsPerLevel, v => s.WaterSecondsPerLevel = v, Localizer.T("settings-time-second-symbol"), true);
                WaterSlider(list, "fatigue training", "settings-water-training", ref y,
                    15f, 1800f, s.TrainingSeconds, v => s.TrainingSeconds = v, Localizer.T("settings-time-second-symbol"), true);
                WaterSlider(list, "fatigue requirement", "settings-water-growth", ref y,
                    1f, 2f, s.WaterTrainingGrowth, v => s.WaterTrainingGrowth = v, "x", false);
                WaterSlider(list, "fatigue regen", "settings-water-regeneration", ref y,
                    .1f, 50f, s.FatigueRegenPerSecond, v => s.FatigueRegenPerSecond = v, Localizer.T("settings-water-unit-per-second"), false);
                WaterSlider(list, "fatigue cooldown", "settings-water-cooldown", ref y,
                    0f, 15f, s.FatigueRegenCooldown, v => s.FatigueRegenCooldown = v, Localizer.T("settings-time-second-symbol"), false);
            }
            else
            {
                WaterToggle(list, "oxygen enabled", "settings-oxygen-enabled", ref y,
                    () => s.OxygenEnabled, v => s.OxygenEnabled = v);
                WaterSlider(list, "oxygen base", "settings-oxygen-base", ref y,
                    30f, 1000f, s.InitialOxygen, v => s.InitialOxygen = v, "O₂", true);
                WaterSlider(list, "oxygen level", "settings-oxygen-per-level", ref y,
                    0f, 150f, s.OxygenPerLevel, v => s.OxygenPerLevel = v, "O₂", true);
                WaterSlider(list, "oxygen drain", "settings-oxygen-loss", ref y,
                    1f, 80f, s.OxygenLossPerSecond, v => s.OxygenLossPerSecond = v, Localizer.T("settings-oxygen-unit-per-second"), false);
                WaterSlider(list, "oxygen drain bonus", "settings-oxygen-loss-bonus", ref y,
                    0f, 5f, s.OxygenLossReduction, v => s.OxygenLossReduction = v, Localizer.T("settings-oxygen-unit-per-second"), false);
                WaterSlider(list, "oxygen training", "settings-oxygen-training-time", ref y,
                    15f, 1800f, s.TrainingUnderwaterSeconds, v => s.TrainingUnderwaterSeconds = v, Localizer.T("settings-time-second-symbol"), true);
                WaterSlider(list, "oxygen growth", "settings-oxygen-growth", ref y,
                    1f, 2f, s.DiveTrainingGrowth, v => s.DiveTrainingGrowth = v, "x", false);
                WaterSlider(list, "oxygen regen", "settings-oxygen-regeneration", ref y,
                    .05f, 2f, s.OxygenRegenMultiplier, v => s.OxygenRegenMultiplier = v, "x", false);
            }
            list.sizeDelta = new Vector2(612f, y + 35f);
        }

        private static void BuildWaterScroll(RectTransform panel, out RectTransform list)
        {
            RectTransform viewport = UIFactory.Rect(panel, "Water settings viewport", 8, 5, 625, 750);
            viewport.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            Image hitbox = UIFactory.Fill(viewport, "Water scroll background", 0f, 0f, 625f, 750f,
                new Color(1f, 1f, 1f, .001f));
            hitbox.raycastTarget = true;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = false;
            scroll.scrollSensitivity = 60f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            list = UIFactory.Rect(viewport, "Water settings content", 0, 0, 610, 1000);
            scroll.content = list;
            scroll.viewport = viewport;
            scroll.verticalNormalizedPosition = 1f;
        }

        private void WaterToggle(Transform parent, string name, string key, ref float y,
            Func<bool> read, Action<bool> apply)
        {
            UIFactory.Label(parent, name + " title", Localizer.T(key),
                20, y, 383, 48, 20, UIFactory.Gold);
            Text text = UIFactory.Label(parent, name + " value", "", 414, y, 180, 43,
                18, UIFactory.White, TextAnchor.MiddleCenter);
            Action refresh = () => text.text = Localizer.T(read() ? "settings-enabled" : "settings-disabled");
            refresh();
            UIFactory.Button(parent, name + " button", 414, y, 180, 47, () =>
            {
                apply(!read());
                refresh();
                WaterSurvivalSettings.Changed();
            });
            y += 70f;
        }

        private void WaterSlider(Transform parent, string name, string key, ref float y,
            float min, float max, float current, Action<float> apply, string unit, bool integer)
        {
            UIFactory.Label(parent, name + " label", Localizer.T(key),
                20, y, 565, 29, 20, UIFactory.Gold);
            InputField input = CreateTimeInput(parent, name + " input", 476, y + 29f, 115, 37);
            Slider slider = SettingsSlider(parent, name + " slider", 20, y + 35f,
                444, min, max, current, null, integer);
            Action<float> changed = value =>
            {
                float v = Mathf.Clamp(value, min, max);
                v = integer ? Mathf.Round(v) : Mathf.Round(v * 100f) / 100f;
                slider.SetValueWithoutNotify(v);
                apply(v);
                input.text = v.ToString(integer ? "0" : "0.##", CultureInfo.InvariantCulture);
                WaterSurvivalSettings.Changed();
            };
            input.text = current.ToString(integer ? "0" : "0.##", CultureInfo.InvariantCulture);
            slider.onValueChanged.AddListener(v => changed(v));
            input.onEndEdit.AddListener(raw =>
            {
                float parsed;
                if (float.TryParse(raw.Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                    changed(parsed);
                else input.text = slider.value.ToString(integer ? "0" : "0.##", CultureInfo.InvariantCulture);
            });
            UIFactory.Label(parent, name + " unit", unit, 517, y + 65f, 80, 18,
                14, UIFactory.Grey, TextAnchor.MiddleRight);
            y += 92f;
        }
    }
}
