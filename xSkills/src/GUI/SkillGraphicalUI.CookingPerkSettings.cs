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
        private int cookingSkillSubPage;

        private void BuildCookingSkillSettings(RectTransform parent)
        {
            string[] titles = { "settings-skill-exp-tab", "settings-skill-perks-tab" };
            for (int i = 0; i < titles.Length; i++)
            {
                int selected = i;
                float x = 12f + i * 313f;
                UIFactory.Image(parent, "Cooking tab background " + i, "elements", "button_idle", x, 3, 306, 43);
                UIFactory.Label(parent, "Cooking tab label " + i, Localizer.T(titles[i]),
                    x + 5, 6, 296, 37, 18, cookingSkillSubPage == i ? UIFactory.Gold : UIFactory.White,
                    TextAnchor.MiddleCenter);
                UIFactory.Button(parent, "Cooking tab button " + i, x, 3, 306, 43, () =>
                {
                    if (cookingSkillSubPage == selected) return;
                    cookingSkillSubPage = selected;
                    ShowSettingsSection(shownSettingsSection, true);
                });
            }
            RectTransform content = UIFactory.Rect(parent, "Cooking settings area", 0, 51, 645, 715);
            if (cookingSkillSubPage == 0) BuildCookingExperienceSettings(content);
            else BuildCookingPerkSettings(content);
        }

        private void BuildCookingPerkSettings(RectTransform panel)
        {
            SkillData skill = SkillCatalog.Find("cooking");
            AbilityData ability = skill != null ? skill.FindAbility("durablebowl") : null;
            if (ability == null) return;

            UIFactory.Label(panel, "Cooking perk name", Localizer.AbilityName(ability.Id),
                22, 17, 575, 43, 26, UIFactory.Gold);
            UIFactory.Label(panel, "Cooking perk description", Localizer.T("settings-cooking-bowl-intro"),
                22, 70, 584, 100, 19, UIFactory.White, TextAnchor.UpperLeft);

            CookingPerkNumber(panel, "required", "settings-perk-required-level", 191,
                1, skill.MaxLevel, ReusableDishesSystem.RequiredLevel, ReusableDishesSystem.SetRequiredLevel);
            CookingPerkNumber(panel, "durability", "settings-cooking-bowl-base-durability", 305,
                1, 100, ReusableDishesSystem.BaseDurability, ReusableDishesSystem.SetDurability);
            CookingPerkNumber(panel, "multiplier", "settings-cooking-bowl-multiplier", 419,
                1, 10, ReusableDishesSystem.FirstTierMultiplier, ReusableDishesSystem.SetMultiplier);

            UIFactory.Label(panel, "Cooking perk tiers", Localizer.T("settings-cooking-bowl-tier-two"),
                22, 536, 583, 80, 18, UIFactory.Grey, TextAnchor.UpperLeft);
            SettingsButton(panel, "Cooking perk reset", Localizer.T("settings-perk-reset"),
                22, 642, 290, () =>
                {
                    ReusableDishesSystem.RestoreDefaults();
                    ShowSettingsSection(shownSettingsSection, true);
                });
        }

        private void CookingPerkNumber(RectTransform parent, string id, string labelKey,
            float y, int minimum, int maximum, int current, Action<int> apply)
        {
            UIFactory.Label(parent, "Cooking perk " + id, Localizer.T(labelKey),
                22, y, 571, 37, 22, UIFactory.Gold);
            Slider slider = SettingsSlider(parent, "Cooking perk slider " + id,
                22, y + 44, 476, minimum, maximum, current, null, true);
            InputField input = CreateTimeInput(parent, "Cooking perk input " + id,
                515, y + 43, 98, 36);
            input.contentType = InputField.ContentType.IntegerNumber;
            input.text = current.ToString(CultureInfo.InvariantCulture);
            Action<int> update = value =>
            {
                int normalized = Mathf.Clamp(value, minimum, maximum);
                slider.SetValueWithoutNotify(normalized);
                input.text = normalized.ToString(CultureInfo.InvariantCulture);
                apply(normalized);
            };
            slider.onValueChanged.AddListener(value => update(Mathf.RoundToInt(value)));
            input.onEndEdit.AddListener(raw =>
            {
                int result;
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                    update(result);
                else input.text = Mathf.RoundToInt(slider.value).ToString(CultureInfo.InvariantCulture);
            });
        }
    }
}
