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
        private void BuildTemperatureSettings(RectTransform panel, bool cold)
        {
            BodyTemperatureSettings s = BodyTemperatureSettings.Current;
            RectTransform viewport = UIFactory.Rect(panel, "Temperature settings viewport", 8, 5, 625, 694);
            viewport.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            Image scrollHitbox = UIFactory.Fill(viewport, "Temperature scroll hitbox", 0f, 0f, 625f, 694f,
                new Color(1f, 1f, 1f, .001f));
            scrollHitbox.raycastTarget = true;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = false;
            scroll.scrollSensitivity = 60f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            RectTransform list = UIFactory.Rect(viewport, "Temperature settings content", 0, 0, 610,
                cold ? 860 : 1190);
            scroll.content = list;
            scroll.viewport = viewport;
            float y = 4f;
            UIFactory.Label(list, "Temperature settings heading", Localizer.T(cold ?
                "settings-temp-cold-explanation" : "settings-temp-heat-explanation"),
                20, y, 562, 57, 18, UIFactory.White, TextAnchor.UpperLeft);
            y += 65f;
            if (cold)
            {
                TemperatureToggle(list, "cold", "settings-temp-cold-enabled", ref y,
                    () => s.ColdEnabled, v => s.ColdEnabled = v);
                TemperatureSlider(list, "cold-slow", "settings-temp-cold-slow-start", ref y,
                    32f, 36.4f, s.ColdSlowStart, v => s.ColdSlowStart = v, "°C");
                TemperatureSlider(list, "cold-speed", "settings-temp-cold-min-speed", ref y,
                    30f, 100f, s.ColdMinimumSpeed * 100f, v => s.ColdMinimumSpeed = v / 100f, "%");
                TemperatureToggle(list, "damage", "settings-temp-damage-enabled", ref y,
                    () => s.DamageEnabled, v => s.DamageEnabled = v);
                TemperatureSlider(list, "cold-damage-start", "settings-temp-cold-damage-start", ref y,
                    31f, 35f, s.ColdDamageStart, v => s.ColdDamageStart = v, "°C");
                TemperatureSlider(list, "cold-base", "settings-temp-cold-base-damage", ref y,
                    0f, 30f, s.ColdBaseDamage, v => s.ColdBaseDamage = v, Localizer.T("settings-temp-unit-hp"));
                TemperatureSlider(list, "cold-slope", "settings-temp-cold-damage-slope", ref y,
                    0f, 25f, s.ColdDamagePerDegree, v => s.ColdDamagePerDegree = v, Localizer.T("settings-temp-unit-hp-per-degree"));
                TemperatureSlider(list, "cold-interval", "settings-temp-damage-interval", ref y,
                    .5f, 10f, s.DamageInterval, v => s.DamageInterval = v, Localizer.T("settings-time-second-symbol"));
            }
            else
            {
                TemperatureToggle(list, "heat", "settings-temp-heat-enabled", ref y,
                    () => s.HeatEnabled, v => s.HeatEnabled = v);
                TemperatureSlider(list, "heat-slow", "settings-temp-heat-slow-start", ref y,
                    37f, 40f, s.HeatSlowStart, v => s.HeatSlowStart = v, "°C");
                TemperatureSlider(list, "heat-speed", "settings-temp-heat-min-speed", ref y,
                    50f, 100f, s.HeatMinimumSpeed * 100f, v => s.HeatMinimumSpeed = v / 100f, "%");
                TemperatureToggle(list, "damage", "settings-temp-damage-enabled", ref y,
                    () => s.DamageEnabled, v => s.DamageEnabled = v);
                TemperatureSlider(list, "heat-damage-start", "settings-temp-heat-damage-start", ref y,
                    38f, 41f, s.HeatDamageStart, v => s.HeatDamageStart = v, "°C");
                TemperatureSlider(list, "heat-base", "settings-temp-heat-base-damage", ref y,
                    0f, 30f, s.HeatBaseDamage, v => s.HeatBaseDamage = v, Localizer.T("settings-temp-unit-hp"));
                TemperatureSlider(list, "heat-slope", "settings-temp-heat-damage-slope", ref y,
                    0f, 30f, s.HeatDamagePerDegree, v => s.HeatDamagePerDegree = v, Localizer.T("settings-temp-unit-hp-per-degree"));
                TemperatureSlider(list, "heat-interval", "settings-temp-damage-interval", ref y,
                    .5f, 10f, s.DamageInterval, v => s.DamageInterval = v, Localizer.T("settings-time-second-symbol"));
                TemperatureToggle(list, "nausea", "settings-temp-nausea-enabled", ref y,
                    () => s.NauseaEnabled, v => s.NauseaEnabled = v);
                TemperatureSlider(list, "nausea-start", "settings-temp-nausea-start", ref y,
                    37f, 40.5f, s.NauseaStart, v => s.NauseaStart = v, "°C");
                TemperatureToggle(list, "distortion", "settings-temp-distortion-enabled", ref y,
                    () => s.NauseaDistortionEnabled, v => s.NauseaDistortionEnabled = v);
                TemperatureSlider(list, "nausea-strength", "settings-temp-nausea-strength", ref y,
                    0f, 200f, s.NauseaStrength * 100f, v => s.NauseaStrength = v / 100f, "%");
            }
            SettingsButton(list, "Temperature reset", Localizer.T("settings-temp-reset"),
                22, y + 8, 265, () =>
                {
                    if (cold) BodyTemperatureSettings.ResetCold();
                    else BodyTemperatureSettings.ResetHeat();
                    ShowSettingsSection(shownSettingsSection, true);
                });
            list.sizeDelta = new Vector2(610, y + 90);
            scroll.verticalNormalizedPosition = 1f;
        }

        private void TemperatureToggle(Transform parent, string name, string titleKey, ref float y,
            Func<bool> read, Action<bool> set)
        {
            float row = y;
            UIFactory.Label(parent, "Temp " + name + " toggle title",
                Localizer.T(titleKey), 20, row, 390, 47, 20, UIFactory.Gold);
            UIFactory.Image(parent, "Temp " + name + " toggle background", "elements",
                "button_idle", 414, row, 190, 47);
            Text state = UIFactory.Label(parent, "Temp " + name + " toggle value", "",
                424, row, 175, 47, 17, UIFactory.White, TextAnchor.MiddleCenter);
            Action update = () => state.text = Localizer.T(read() ? "settings-enabled" : "settings-disabled");
            update();
            UIFactory.Button(parent, "Temp " + name + " toggle click", 414, row, 190, 47, () =>
            {
                set(!read());
                update();
                BodyTemperatureSettings.Changed();
            });
            y += 67f;
        }

        private void TemperatureSlider(Transform parent, string name, string titleKey, ref float y,
            float min, float max, float value, Action<float> set, string unit)
        {
            float row = y;
            UIFactory.Label(parent, "Temp " + name + " title", Localizer.T(titleKey),
                20, row, 560, 28, 20, UIFactory.Gold);
            InputField input = CreateTimeInput(parent, "Temp " + name + " input", 479, row + 29, 114, 36);
            Slider slider = SettingsSlider(parent, "Temp " + name + " slider",
                20, row + 35, 445, min, max, value, null, false);
            Action<float> update = v =>
            {
                float selected = Mathf.Round(Mathf.Clamp(v, min, max) * 10f) / 10f;
                slider.SetValueWithoutNotify(selected);
                set(selected);
                input.text = selected.ToString("0.#", CultureInfo.InvariantCulture);
                BodyTemperatureSettings.Changed();
            };
            input.text = value.ToString("0.#", CultureInfo.InvariantCulture);
            slider.onValueChanged.AddListener(v => update(v));
            input.onEndEdit.AddListener(raw =>
            {
                float parsed;
                if (float.TryParse(raw.Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                    update(parsed);
                else input.text = slider.value.ToString("0.#", CultureInfo.InvariantCulture);
            });
            UIFactory.Label(parent, "Temp " + name + " unit", unit,
                520, row + 65, 78, 20, 14, UIFactory.Grey, TextAnchor.MiddleRight);
            y += 90f;
        }
    }
}
