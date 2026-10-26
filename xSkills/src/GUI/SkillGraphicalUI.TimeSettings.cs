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
        private Text climateRatesText;
        private float nextClimateRatesRefresh;

        private void UpdateClimateRateLabel()
        {
            if (page != "_settings" || climateRatesText == null ||
                shownSettingsSection < 0 || shownSettingsSection >= settingsSectionIds.Count ||
                settingsSectionIds[shownSettingsSection] != "survival" ||
                Time.unscaledTime < nextClimateRatesRefresh) return;
            nextClimateRatesRefresh = Time.unscaledTime + 1f;
            TimeControlSystem control = TimeControlSystem.Instance;
            if (control == null) return;
            climateRatesText.text = Localizer.F("settings-survival-current-rates",
                control.HungerFactor.ToString("0.00", CultureInfo.InvariantCulture),
                control.ThirstFactor.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private int timeWorldSubPage;

        private void BuildTimeWorldSettings(RectTransform panel)
        {
            if (!Raft_Network.IsHost) return;
            string[] tabs = { "settings-timeworld-tab-time", "settings-rain-title" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                float x = 12f + i * 313f;
                UIFactory.Image(panel, "Time and world tab background " + i, "elements",
                    "button_idle", x, 3, 306, 43);
                UIFactory.Label(panel, "Time and world tab name " + i, Localizer.T(tabs[i]),
                    x + 4, 6, 298, 37, 17, timeWorldSubPage == i ? UIFactory.Gold : UIFactory.White,
                    TextAnchor.MiddleCenter);
                UIFactory.Button(panel, "Time and world tab button " + i, x, 3, 306, 43, () =>
                {
                    if (timeWorldSubPage == index) return;
                    timeWorldSubPage = index;
                    ShowSettingsSection(shownSettingsSection, true);
                });
            }

            RectTransform subsection = UIFactory.Rect(panel, "Time and world subsection", 0, 51, 645, 720);
            if (timeWorldSubPage == 0) BuildTimeSettings(subsection);
            else BuildRainSettings(subsection);
        }

        private void BuildTimeSettings(RectTransform panel)
        {
            TimeControlSystem control = TimeControlSystem.Instance;
            if (control == null || !Raft_Network.IsHost) return;
            UIFactory.Label(panel, "Time intro", Localizer.T("settings-timeworld-intro"),
                22, 5, 585, 49, 18, UIFactory.White, TextAnchor.UpperLeft);
            TimeSetting(panel, "day", "settings-time-day", 60, 60f, 14400f,
                control.Settings.DayLengthSeconds, control.VanillaDaySeconds, TimeControlSettings.ModDaySeconds,
                v => control.Settings.DayLengthSeconds = v, false);
            TimeSetting(panel, "plants", "settings-time-plants", 174, .1f, 20f,
                control.Settings.PlantGrowthMultiplier, 1f, TimeControlSettings.ModPlantGrowthMultiplier,
                v => control.Settings.PlantGrowthMultiplier = v, true);
            TimeSetting(panel, "sharkattack", "settings-time-shark-attack", 288, 1f, 3600f,
                control.Settings.SharkAttackSeconds, control.VanillaSharkAttack, TimeControlSettings.ModSharkAttackSeconds,
                v => control.Settings.SharkAttackSeconds = v, false);
            TimeSetting(panel, "sharkrespawn", "settings-time-shark-respawn", 402, 1f, 3600f,
                control.Settings.SharkRespawnSeconds, control.VanillaSharkRespawn, TimeControlSettings.ModSharkRespawnSeconds,
                v => control.Settings.SharkRespawnSeconds = v, false);
            TimeSetting(panel, "weather", "settings-time-weather", 516, .1f, 20f,
                control.Settings.WeatherDurationMultiplier, 1f, TimeControlSettings.ModWeatherDurationMultiplier,
                v => control.Settings.WeatherDurationMultiplier = v, true);
            UIFactory.Label(panel, "Time hint", Localizer.T("settings-timeworld-hint"),
                22, 642, 585, 65, 17, UIFactory.Grey, TextAnchor.UpperLeft);
        }

        private void BuildRainSettings(RectTransform panel)
        {
            TimeControlSystem control = TimeControlSystem.Instance;
            if (control == null || !Raft_Network.IsHost) return;
            UIFactory.Label(panel, "Rain settings intro", Localizer.T("settings-rain-intro"),
                22, 4, 590, 65, 19, UIFactory.White, TextAnchor.UpperLeft);
            RainSetting(panel, "frequency", "settings-rain-frequency", "settings-rain-frequency-hint",
                75, 0f, 400f, control.Settings.RainFrequencyPercent, 100f,
                value => control.Settings.RainFrequencyPercent = value, false);
            RainSetting(panel, "duration", "settings-rain-duration", "settings-rain-duration-hint",
                250, .25f, 5f, control.Settings.RainDurationMultiplier, 1f,
                value => control.Settings.RainDurationMultiplier = value, true);
            RainSetting(panel, "variation", "settings-rain-variation", "settings-rain-variation-hint",
                425, 0f, 90f, control.Settings.RainVariationPercent, 35f,
                value => control.Settings.RainVariationPercent = value, false);
            UIFactory.Label(panel, "Rain settings note", Localizer.T("settings-rain-note"),
                22, 610, 592, 95, 17, UIFactory.Grey, TextAnchor.UpperLeft);
        }

        private void RainSetting(RectTransform panel, string id, string key, string hintKey,
            float y, float minimum, float maximum, float value, float reset,
            Action<float> apply, bool multiplier)
        {
            UIFactory.Label(panel, "Rain " + id + " title", Localizer.T(key),
                22, y, 590, 33, 23, UIFactory.Gold);
            InputField edit = CreateTimeInput(panel, "Rain " + id + " value", 511, y + 41, 100, 36);
            Slider slider = SettingsSlider(panel, "Rain " + id + " slider", 22, y + 43, 480,
                minimum, maximum, value, null, !multiplier);
            Func<float, string> display = v => multiplier
                ? "x" + v.ToString("0.##", CultureInfo.InvariantCulture)
                : Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture) + "%";
            Action<float> applyValue = next =>
            {
                float selected = Mathf.Clamp(next, minimum, maximum);
                selected = multiplier ? Mathf.Round(selected * 20f) / 20f : Mathf.Round(selected);
                slider.SetValueWithoutNotify(selected);
                edit.text = display(selected);
                apply(selected);
                TimeControlSystem.SettingsChanged();
                RainForecastSchedule.SettingsChanged();
            };
            edit.text = display(value);
            slider.onValueChanged.AddListener(v => applyValue(v));
            edit.onEndEdit.AddListener(raw =>
            {
                float parsed;
                string normalized = (raw ?? string.Empty).Replace("x", "").Replace("X", "")
                    .Replace("%", "").Replace(',', '.').Trim();
                if (float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
                    !float.IsNaN(parsed) && !float.IsInfinity(parsed)) applyValue(parsed);
                else edit.text = display(slider.value);
            });
            UIFactory.Label(panel, "Rain " + id + " hint", Localizer.T(hintKey),
                22, y + 90, 405, 60, 17, UIFactory.Grey, TextAnchor.UpperLeft);
            TimePresetButton(panel, "Rain " + id + " reset", Localizer.T("settings-rain-reset"),
                462, y + 92, 150, () => applyValue(reset));
        }

        private int survivalSubPage;

        private void BuildSurvivalSettings(RectTransform panel)
        {
            if (!Raft_Network.IsHost) return;
            climateRatesText = null;
            string[] tabs = { "settings-survival-tab-nutrition", "settings-survival-tab-cold", "settings-survival-tab-heat" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                float x = 12f + i * 208f;
                UIFactory.Image(panel, "Survival tab background " + i, "elements",
                    "button_idle", x, 3, 203, 43);
                UIFactory.Label(panel, "Survival tab name " + i, Localizer.T(tabs[i]),
                    x + 4, 6, 195, 37, 17, survivalSubPage == i ? UIFactory.Gold : UIFactory.White,
                    TextAnchor.MiddleCenter);
                UIFactory.Button(panel, "Survival tab button " + i, x, 3, 203, 43, () =>
                {
                    if (survivalSubPage == index) return;
                    survivalSubPage = index;
                    ShowSettingsSection(shownSettingsSection, true);
                });
            }
            RectTransform content = UIFactory.Rect(panel, "Survival subsection", 0, 51, 645, 720);
            if (survivalSubPage == 0) BuildNutritionSettings(content);
            else BuildTemperatureSettings(content, survivalSubPage == 1);
        }

        private void BuildNutritionSettings(RectTransform panel)
        {
            TimeControlSystem control = TimeControlSystem.Instance;
            if (control == null || !Raft_Network.IsHost) return;
            UIFactory.Label(panel, "Survival intro", Localizer.T("settings-survival-intro"),
                22, 14, 585, 70, 19, UIFactory.White, TextAnchor.UpperLeft);
            TimeSetting(panel, "hunger", "settings-time-hunger", 110, .1f, 20f,
                control.Settings.HungerDurationMultiplier, 1f, TimeControlSettings.ModHungerDurationMultiplier,
                v => control.Settings.HungerDurationMultiplier = v, true);
            TimeSetting(panel, "thirst", "settings-time-thirst", 235, .1f, 20f,
                control.Settings.ThirstDurationMultiplier, 1f, TimeControlSettings.ModThirstDurationMultiplier,
                v => control.Settings.ThirstDurationMultiplier = v, true);

            Text climateLabel = UIFactory.Label(panel, "Climate consumption state", "", 22, 397,
                590, 35, 23, UIFactory.Gold);
            Action updateText = () => climateLabel.text = Localizer.T("settings-climate-consumption") + ": " +
                Localizer.T(control.Settings.ClimateConsumption ? "settings-enabled" : "settings-disabled");
            updateText();
            SettingsButton(panel, "Climate consumption toggle", Localizer.T("settings-climate-toggle"), 22, 448, 280,
                () => { control.Settings.ClimateConsumption = !control.Settings.ClimateConsumption;
                    TimeControlSystem.SettingsChanged(); updateText(); });

            UIFactory.Label(panel, "Climate summary", Localizer.T("settings-survival-climate-description"),
                22, 532, 590, 98, 18, UIFactory.White, TextAnchor.UpperLeft);
            climateRatesText = UIFactory.Label(panel, "Climate rates", Localizer.F("settings-survival-current-rates",
                control.HungerFactor.ToString("0.00", CultureInfo.InvariantCulture),
                control.ThirstFactor.ToString("0.00", CultureInfo.InvariantCulture)),
                22, 646, 590, 56, 18, UIFactory.Grey, TextAnchor.UpperLeft);
        }

        private void TimeSetting(RectTransform panel, string name, string titleKey, float y,
            float minimum, float maximum, float value, float vanilla, float modPreset,
            Action<float> apply, bool multiplier)
        {
            string id = "Integrated " + name;
            UIFactory.Label(panel, id + " label", Localizer.T(titleKey),
                22, y, 590, 30, 23, UIFactory.Gold);
            InputField edit = CreateTimeInput(panel, id + " value", 511, y + 34, 100, 35);
            edit.text = value.ToString(multiplier ? "0.##" : "0", CultureInfo.InvariantCulture);
            Slider slider = SettingsSlider(panel, id + " slider", 22, y + 38, 480,
                minimum, maximum, value, null, !multiplier);
            Func<float, float> normalize = v => multiplier ? TimeControlSettings.ClampMultiplier(v) : Mathf.Round(v);
            Action<float> applyAndStore = v =>
            {
                float selected = normalize(Mathf.Clamp(v, minimum, maximum));
                slider.SetValueWithoutNotify(selected);
                apply(selected);
                edit.text = selected.ToString(multiplier ? "0.##" : "0", CultureInfo.InvariantCulture);
                TimeControlSystem.SettingsChanged();
            };
            slider.onValueChanged.AddListener(v => applyAndStore(v));
            edit.onEndEdit.AddListener(raw =>
            {
                float number;
                if (float.TryParse(raw.Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out number) && !float.IsNaN(number) && !float.IsInfinity(number))
                    applyAndStore(number);
                else
                    edit.text = slider.value.ToString(multiplier ? "0.##" : "0", CultureInfo.InvariantCulture);
            });
            UIFactory.Label(panel, id + " unit", Localizer.T(multiplier ? "settings-time-multiplier-hint" : "settings-time-seconds-hint"),
                22, y + 79, 265, 28, 15, UIFactory.Grey);
            string vanillaValue = FormatTimePreset(vanilla, multiplier);
            string modValue = FormatTimePreset(modPreset, multiplier);
            TimePresetButton(panel, id + " vanilla", Localizer.F("settings-time-reset-vanilla", vanillaValue),
                293, y + 78, 151, () => applyAndStore(vanilla));
            TimePresetButton(panel, id + " mod", Localizer.F("settings-time-reset-mod", modValue),
                457, y + 78, 151, () => applyAndStore(modPreset));
        }

        private static string FormatTimePreset(float value, bool multiplier)
        {
            return multiplier
                ? "x" + value.ToString("0.##", CultureInfo.InvariantCulture)
                : Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture) + Localizer.T("settings-time-second-symbol");
        }

        private static void TimePresetButton(Transform parent, string name, string title,
            float x, float y, float width, Action clicked)
        {
            const float height = 34f;
            UIFactory.Image(parent, name + " background", "elements", "button_idle", x, y, width, height);
            UIFactory.Label(parent, name + " label", title, x + 5f, y + 1f,
                width - 10f, height - 2f, 17, UIFactory.Gold, TextAnchor.MiddleCenter);
            UIFactory.Button(parent, name + " click", x, y, width, height, clicked);
        }

        private static InputField CreateTimeInput(Transform parent, string name, float x, float y,
            float width, float height)
        {
            RectTransform rect = UIFactory.Rect(parent, name, x, y, width, height);
            Image bg = rect.gameObject.AddComponent<Image>();
            bg.color = UIFactory.Hex("33251B");
            InputField field = rect.gameObject.AddComponent<InputField>();
            Text text = UIFactory.Label(rect, "Number", "", 6, 2, width - 12, height - 4,
                19, UIFactory.White, TextAnchor.MiddleCenter);
            text.GetComponent<BitmapFontLabel>().enabled = false;
            field.textComponent = text;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = 10;
            return field;
        }
    }
}
