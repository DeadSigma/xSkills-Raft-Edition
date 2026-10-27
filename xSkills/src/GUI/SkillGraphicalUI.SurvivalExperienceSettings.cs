using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using xSkills.Services;
using xSkills.Skills;
using xSkills.Systems;

namespace xSkills.GUI
{
    internal sealed partial class SkillGraphicalUI
    {
        private int survivalSkillSubPage;

        private void BuildSurvivalSkillSettings(RectTransform panel)
        {
            string[] tabs = { "settings-skill-exp-tab", "settings-skill-perks-tab" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int selected = i;
                float x = 12f + i * 313f;
                UIFactory.Image(panel, "Skill survival tab background " + i, "elements", "button_idle", x, 3, 306, 43);
                UIFactory.Label(panel, "Skill survival tab text " + i, Localizer.T(tabs[i]),
                    x + 5, 6, 296, 37, 18, survivalSkillSubPage == i ? UIFactory.Gold : UIFactory.White,
                    TextAnchor.MiddleCenter);
                UIFactory.Button(panel, "Skill survival tab button " + i, x, 3, 306, 43, () =>
                {
                    if (survivalSkillSubPage == selected) return;
                    survivalSkillSubPage = selected;
                    ShowSettingsSection(shownSettingsSection, true);
                });
            }
            RectTransform area = UIFactory.Rect(panel, "Survival skill subsection", 0, 51, 645, 715);
            if (survivalSkillSubPage == 0) BuildSkillExperiencePage(area, "survival");
            else BuildSurvivalPerkSettings(area);
        }

        private void BuildSkillExperiencePage(RectTransform panel, string skillId)
        {
            UIFactory.Label(panel, "Skill experience description", Localizer.T("settings-skill-exp-intro"),
                22, 16, 591, 93, 19, UIFactory.White, TextAnchor.UpperLeft);
            SkillExperienceValue(panel, skillId, "interval", "settings-skill-exp-interval",
                "settings-skill-exp-interval-hint", 120, SkillExperienceSettings.MinIntervalMinutes,
                SkillExperienceSettings.MaxIntervalMinutes, SkillExperienceSettings.IntervalMinutes(skillId),
                true, value => SkillExperienceSettings.SetIntervalMinutes(skillId, value));
            SkillExperienceValue(panel, skillId, "amount", "settings-skill-exp-amount",
                "settings-skill-exp-amount-hint", 310, SkillExperienceSettings.MinExperience,
                SkillExperienceSettings.MaxExperience, SkillExperienceSettings.Experience(skillId),
                false, value => SkillExperienceSettings.SetExperience(skillId, value));
            UIFactory.Label(panel, "Skill experience default", Localizer.T("settings-skill-exp-default"),
                22, 533, 595, 66, 18, UIFactory.Grey, TextAnchor.UpperLeft);
            SettingsButton(panel, "Skill experience reset", Localizer.T("settings-skill-exp-reset"),
                22, 614, 290, () =>
                {
                    SkillExperienceSettings.RestoreDefaults(skillId);
                    SurvivalExperienceSystem.SettingsChanged();
                    ShowSettingsSection(shownSettingsSection, true);
                });
        }

        private void SkillExperienceValue(RectTransform panel, string skillId, string id,
            string titleKey, string hintKey, float y, float min, float max, float current,
            bool integer, Action<float> apply)
        {
            UIFactory.Label(panel, "Skill XP " + id + " title", Localizer.T(titleKey),
                22, y, 570, 36, 23, UIFactory.Gold);
            Slider slider = SettingsSlider(panel, "Skill XP " + id + " slider",
                22, y + 48, 480, min, max, current, null, integer);
            InputField input = CreateTimeInput(panel, "Skill XP " + id + " input",
                515, y + 46, 98, 36);
            input.contentType = InputField.ContentType.DecimalNumber;
            Func<float, string> format = value => value.ToString(integer ? "0" : "0.#", CultureInfo.InvariantCulture);
            Action<float> change = raw =>
            {
                float safe = Mathf.Clamp(raw, min, max);
                float selected = integer ? Mathf.Round(safe) : Mathf.Round(safe * 10f) / 10f;
                slider.SetValueWithoutNotify(selected);
                input.text = format(selected);
                apply(selected);
                SurvivalExperienceSystem.SettingsChanged();
            };
            input.text = format(current);
            slider.onValueChanged.AddListener(value => change(value));
            input.onEndEdit.AddListener(raw =>
            {
                float parsed;
                if (float.TryParse((raw ?? "").Replace(',', '.'), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                    change(parsed);
                else input.text = format(slider.value);
            });
            UIFactory.Label(panel, "Skill XP " + id + " hint", Localizer.T(hintKey),
                22, y + 94, 581, 73, 17, UIFactory.Grey, TextAnchor.UpperLeft);
        }
    }
}
