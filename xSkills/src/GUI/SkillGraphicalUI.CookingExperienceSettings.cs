using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using xSkills.Services;
using xSkills.Skills;

namespace xSkills.GUI
{
    internal sealed partial class SkillGraphicalUI
    {
        private void BuildCookingExperienceSettings(RectTransform panel)
        {
            UIFactory.Label(panel, "Cooking XP introduction", Localizer.T("settings-cooking-exp-intro"),
                22, 17, 592, 75, 19, UIFactory.White, TextAnchor.UpperLeft);

            CookingExperienceValue(panel, "grill", "settings-cooking-exp-grill",
                "settings-cooking-exp-grill-hint", 107, 25f, Cooking.GrillExperience);
            CookingExperienceValue(panel, "pot", "settings-cooking-exp-pot",
                "settings-cooking-exp-pot-hint", 226, 50f, Cooking.PotExperience);
            CookingExperienceValue(panel, "water", "settings-cooking-exp-water",
                "settings-cooking-exp-water-hint", 345, 10f, Cooking.WaterExperience);
            CookingExperienceValue(panel, "diversity", "settings-cooking-exp-diversity",
                "settings-cooking-exp-diversity-hint", 464, 25f, Cooking.DiversityBonus);

            UIFactory.Label(panel, "Cooking XP explanation", Localizer.T("settings-cooking-exp-note"),
                22, 595, 592, 49, 17, UIFactory.Grey, TextAnchor.UpperLeft);
            SettingsButton(panel, "Cooking XP reset", Localizer.T("settings-skill-exp-reset"),
                22, 665, 290, () =>
                {
                    Cooking.RestoreExperienceDefaults();
                    ShowSettingsSection(shownSettingsSection, true);
                });
        }

        private void CookingExperienceValue(RectTransform panel, string type, string labelKey,
            string hintKey, float y, float maximum, float initial)
        {
            UIFactory.Label(panel, "Cooking XP " + type + " label", Localizer.T(labelKey),
                22, y, 584, 32, 22, UIFactory.Gold);

            Slider slider = SettingsSlider(panel, "Cooking XP " + type + " slider",
                22, y + 38, 476, 0f, maximum, initial, null, false);
            InputField input = CreateTimeInput(panel, "Cooking XP " + type + " input",
                515, y + 37, 98, 36);
            input.contentType = InputField.ContentType.DecimalNumber;

            Func<float, string> format = amount => amount.ToString("0.##", CultureInfo.InvariantCulture);
            Action<float> apply = value =>
            {
                float result = Mathf.Round(Mathf.Clamp(value, 0f, maximum) * 100f) / 100f;
                slider.SetValueWithoutNotify(result);
                input.text = format(result);
                Cooking.SetExperience(type, result);
            };
            input.text = format(initial);
            slider.onValueChanged.AddListener(value => apply(value));
            input.onEndEdit.AddListener(raw =>
            {
                float parsed;
                if (float.TryParse((raw ?? "").Replace(',', '.'), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out parsed) &&
                    !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                    apply(parsed);
                else input.text = format(slider.value);
            });

            UIFactory.Label(panel, "Cooking XP " + type + " hint", Localizer.T(hintKey),
                22, y + 78, 580, 37, 16, UIFactory.Grey, TextAnchor.UpperLeft);
        }
    }
}
