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
        private int fishingSkillSubPage;
        private string selectedFishingPerkId = "angler";
        private float fishingPerkListPosition = 1f;
        private float fishingPerkValuesPosition = 1f;

        private static string FishingText(string russian, string english)
        {
            return Localizer.Code == "ru" ? russian : english;
        }

        private void BuildFishingSkillSettings(RectTransform parent)
        {
            string[] tabs = { "settings-skill-exp-tab", "settings-skill-perks-tab" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int selected = i;
                float x = 12f + i * 313f;
                UIFactory.Image(parent, "Fishing tab background " + i, "elements", "button_idle", x, 3, 306, 43);
                UIFactory.Label(parent, "Fishing tab label " + i, Localizer.T(tabs[i]),
                    x + 5, 6, 296, 37, 18, fishingSkillSubPage == i ? UIFactory.Gold : UIFactory.White,
                    TextAnchor.MiddleCenter);
                UIFactory.Button(parent, "Fishing tab button " + i, x, 3, 306, 43, () =>
                {
                    if (fishingSkillSubPage == selected) return;
                    fishingSkillSubPage = selected;
                    ShowSettingsSection(shownSettingsSection, true);
                });
            }
            RectTransform content = UIFactory.Rect(parent, "Fishing settings area", 0, 51, 645, 715);
            if (fishingSkillSubPage == 0) BuildFishingExperienceSettings(content);
            else BuildFishingPerkSettings(content);
        }

        private void BuildFishingExperienceSettings(RectTransform panel)
        {
            UIFactory.Label(panel, "Fishing XP introduction", FishingText(
                "Опыт рыбалки начисляется за успешное извлечение улова из воды. Укажите количество опыта за один улов",
                "Fishing experience is awarded for successfully reeling in a catch. Adjust the experience gained per catch"),
                22, 17, 592, 96, 19, UIFactory.White, TextAnchor.UpperLeft);
            UIFactory.Label(panel, "Fishing XP per catch", Localizer.T("settings-fishing-experience"),
                22, 137, 580, 34, 22, UIFactory.Gold);
            Slider slider = SettingsSlider(panel, "Fishing catch XP slider", 22, 191, 476,
                0f, Fishing.MaximumExperiencePerCatch, Fishing.ExperiencePerCatch, null, false);
            InputField input = CreateTimeInput(panel, "Fishing catch XP input", 515, 190, 98, 36);
            input.contentType = InputField.ContentType.DecimalNumber;
            Func<float, string> format = value => value.ToString("0.#", CultureInfo.InvariantCulture);
            Action<float> apply = amount =>
            {
                float value = Mathf.Round(Mathf.Clamp(amount, 0f, Fishing.MaximumExperiencePerCatch) * 10f) / 10f;
                Fishing.SetExperiencePerCatch(value);
                slider.SetValueWithoutNotify(value);
                input.text = format(value);
            };
            input.text = format(Fishing.ExperiencePerCatch);
            slider.onValueChanged.AddListener(value => apply(value));
            input.onEndEdit.AddListener(raw =>
            {
                float parsed;
                if (float.TryParse((raw ?? "").Replace(',', '.'), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out parsed) &&
                    !float.IsNaN(parsed) && !float.IsInfinity(parsed)) apply(parsed);
                else input.text = format(slider.value);
            });
            UIFactory.Label(panel, "Fishing XP note", FishingText(
                "Опыт начисляется только за успешный улов. Установка 0 отключает получение опыта за рыбалку. " +
                "Настройки скорости поклёвки и значений Angler находятся во вкладке баланса способностей",
                "Experience is awarded only for successful catches. Setting 0 disables fishing XP. " +
                "Bite-time settings and Angler values are in Ability Balance"),
                22, 262, 587, 133, 18, UIFactory.Grey, TextAnchor.UpperLeft);
            SettingsButton(panel, "Fishing XP reset", Localizer.T("settings-skill-exp-reset"),
                22, 422, 290, () =>
                {
                    Fishing.RestoreExperienceDefaults();
                    ShowSettingsSection(shownSettingsSection, true);
                });
        }

        private void BuildFishingPerkSettings(RectTransform panel)
        {
            SkillData skill = SkillCatalog.Find("fishing");
            if (skill == null || skill.Abilities.Count == 0) return;
            UIFactory.Label(panel, "Fishing perk balance heading", FishingText(
                "Настройка требований, уровней и значений способностей рыбалки",
                "Adjust fishing perk requirements, tiers and numeric values"),
                15, 1, 614, 48, 17, UIFactory.White, TextAnchor.UpperLeft);

            RectTransform left = UIFactory.Rect(panel, "Fishing perks picker viewport", 12, 53, 198, 635);
            left.gameObject.AddComponent<RectMask2D>();
            Image leftHit = left.gameObject.AddComponent<Image>();
            leftHit.color = new Color(0f, 0f, 0f, .001f);
            ScrollRect leftScroll = left.gameObject.AddComponent<ScrollRect>();
            leftScroll.horizontal = false;
            leftScroll.vertical = true;
            leftScroll.scrollSensitivity = 64f;
            leftScroll.inertia = false;
            leftScroll.movementType = ScrollRect.MovementType.Clamped;
            RectTransform entries = UIFactory.Rect(left, "Fishing perks list", 0, 0, 188,
                Mathf.Max(635, skill.Abilities.Count * 64 + 7));
            leftScroll.content = entries;
            leftScroll.viewport = left;

            AbilityData selected = skill.FindAbility(selectedFishingPerkId) ?? skill.Abilities[0];
            selectedFishingPerkId = selected.Id;
            for (int i = 0; i < skill.Abilities.Count; i++)
            {
                AbilityData perk = skill.Abilities[i];
                float y = i * 64 + 4;
                UIFactory.Image(entries, "Fishing perk selector " + perk.Id, "elements",
                    selected == perk ? "abilitybox_frame_active" : "abilitybox_bg_info", 0, y, 184, 59, 8);
                UIFactory.Label(entries, "Fishing perk name " + perk.Id, Localizer.AbilityName(perk.Id),
                    7, y + 4, 172, 30, 18, selected == perk ? UIFactory.Gold : UIFactory.White,
                    TextAnchor.MiddleLeft);
                UIFactory.Label(entries, "Fishing perk level " + perk.Id,
                    Localizer.F("settings-perk-short", perk.RequiredLevel, perk.MaxTier),
                    7, y + 31, 172, 23, 14, UIFactory.Grey, TextAnchor.MiddleLeft);
                UIFactory.Button(entries, "Pick fishing perk " + perk.Id, 0, y, 184, 59, () =>
                {
                    if (selectedFishingPerkId == perk.Id) return;
                    selectedFishingPerkId = perk.Id;
                    fishingPerkValuesPosition = 1f;
                    ShowSettingsSection(shownSettingsSection, true);
                });
            }
            leftScroll.verticalNormalizedPosition = fishingPerkListPosition;
            leftScroll.onValueChanged.AddListener(v => fishingPerkListPosition = v.y);

            RectTransform right = UIFactory.Rect(panel, "Fishing perk editor", 217, 52, 413, 650);
            UIFactory.Label(right, "Selected fishing perk", Localizer.AbilityName(selected.Id),
                8, 2, 396, 35, 24, UIFactory.Gold);
            EditSurvivalPerkInteger(right, "Fishing min level", "settings-perk-required-level",
                40, 1, skill.MaxLevel, selected.RequiredLevel, value =>
                {
                    FishingAbilityBalance.SetMinimum(selected, value);
                    ShowSettingsSection(shownSettingsSection, true);
                });
            EditSurvivalPerkInteger(right, "Fishing tier count", "settings-perk-tier-count",
                88, 1, FishingAbilityBalance.MaximumTiers, selected.MaxTier, value =>
                {
                    FishingAbilityBalance.SetTierCount(selected, value);
                    ShowSettingsSection(shownSettingsSection, true);
                });

            UIFactory.Label(right, "Fishing perk values heading", Localizer.T("settings-perk-values-heading"),
                8, 145, 392, 33, 19, UIFactory.Gold);
            RectTransform viewport = UIFactory.Rect(right, "Fishing perk values viewport", 5, 181, 400, 387);
            viewport.gameObject.AddComponent<RectMask2D>();
            Image hitbox = viewport.gameObject.AddComponent<Image>();
            hitbox.color = new Color(0f, 0f, 0f, .001f);
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 63f;
            int valuesPerTier = selected.ValuesPerTier;
            float contentHeight = 156f + selected.MaxTier * (42f + valuesPerTier * 51f) + 16f;
            RectTransform list = UIFactory.Rect(viewport, "Fishing perk numeric values", 0, 0, 392,
                Mathf.Max(387f, contentHeight));
            scroll.content = list;
            scroll.viewport = viewport;
            float yPosition = 6f;

            WaterSurvivalSettings parameters = WaterSurvivalSettings.Current;
            if (parameters != null)
            {
                UIFactory.Label(list, "Fishing base delay label", Localizer.T("settings-fishing-delay"),
                    8, yPosition, 266, 38, 16, UIFactory.White, TextAnchor.MiddleLeft);
                FishingModifierInput(list, "Fishing base delay input", 283, yPosition, .2f, 5f,
                    () => parameters.FishingDelayMultiplier, value => parameters.FishingDelayMultiplier = value);
                yPosition += 54f;
                UIFactory.Label(list, "Fishing speed factor label", Localizer.T("settings-fishing-per-rank"),
                    8, yPosition, 266, 38, 16, UIFactory.White, TextAnchor.MiddleLeft);
                FishingModifierInput(list, "Fishing speed factor input", 283, yPosition, 0f, 1f,
                    () => parameters.FishingSpeedPerRank, value => parameters.FishingSpeedPerRank = value);
                yPosition += 61f;
            }

            for (int tier = 1; tier <= selected.MaxTier; tier++)
            {
                int currentTier = tier;
                UIFactory.Label(list, "Fishing perk tier " + tier,
                    Localizer.F("settings-perk-level-label", tier),
                    8, yPosition, 365, 32, 19, UIFactory.Gold);
                yPosition += 40f;
                for (int i = 0; i < valuesPerTier; i++)
                {
                    int index = i;
                    string key = "settings-perk-value-" + selected.Id + "-" + i;
                    string label = Localizer.T(key);
                    if (label == key) label = selected.Id == "angler"
                        ? FishingText("Ускорение поклёвки (%)", "Bite speed bonus (%)")
                        : Localizer.F("settings-perk-parameter", i + 1);
                    UIFactory.Label(list, "Fishing perk value title " + tier + "_" + i, label,
                        8, yPosition, 263, 38, 16, UIFactory.White, TextAnchor.MiddleLeft);
                    InputField edit = CreateTimeInput(list, "Fishing perk value input " + tier + "_" + i,
                        283, yPosition, 98, 35);
                    edit.contentType = InputField.ContentType.IntegerNumber;
                    edit.text = selected.Values[(tier - 1) * valuesPerTier + i]
                        .ToString(CultureInfo.InvariantCulture);
                    edit.onEndEdit.AddListener(raw =>
                    {
                        int parsed;
                        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                            FishingAbilityBalance.SetValue(selected, currentTier, index, parsed);
                        edit.text = selected.Values[(currentTier - 1) * selected.ValuesPerTier + index]
                            .ToString(CultureInfo.InvariantCulture);
                    });
                    yPosition += 51f;
                }
                yPosition += 2f;
            }
            scroll.verticalNormalizedPosition = fishingPerkValuesPosition;
            scroll.onValueChanged.AddListener(v => fishingPerkValuesPosition = v.y);
            SettingsButton(right, "Reset fishing perk", Localizer.T("settings-perk-reset"),
                8, 582, 381, () =>
                {
                    FishingAbilityBalance.RestoreDefaults(selected);
                    if (parameters != null)
                    {
                        parameters.FishingDelayMultiplier = 1.5f;
                        parameters.FishingSpeedPerRank = .2f;
                        WaterSurvivalSettings.Changed();
                    }
                    ShowSettingsSection(shownSettingsSection, true);
                });
        }

        private void FishingModifierInput(Transform parent, string name, float x, float y,
            float min, float max, Func<float> getValue, Action<float> setValue)
        {
            InputField input = CreateTimeInput(parent, name, x, y, 98, 35);
            input.contentType = InputField.ContentType.DecimalNumber;
            Func<float, string> format = value => value.ToString("0.##", CultureInfo.InvariantCulture);
            input.text = format(getValue());
            input.onEndEdit.AddListener(raw =>
            {
                float parsed;
                if (float.TryParse((raw ?? "").Replace(',', '.'), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out parsed) &&
                    !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                {
                    setValue(Mathf.Round(Mathf.Clamp(parsed, min, max) * 100f) / 100f);
                    WaterSurvivalSettings.Changed();
                    SkillCatalog.NotifyChanged();
                }
                input.text = format(getValue());
            });
        }
    }
}
