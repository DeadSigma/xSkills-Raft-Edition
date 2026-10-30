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
        private string selectedCookingPerkId = "chef";
        private float cookingPerkListPosition = 1f;
        private float cookingPerkValuesPosition = 1f;

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
            if (skill == null || skill.Abilities.Count == 0) return;
            UIFactory.Label(panel, "Cooking perk balance heading", Localizer.T("settings-cooking-perks-intro"),
                15, 1, 614, 48, 17, UIFactory.White, TextAnchor.UpperLeft);

            RectTransform left = UIFactory.Rect(panel, "Cooking perks picker viewport", 12, 53, 198, 635);
            left.gameObject.AddComponent<RectMask2D>();
            Image leftHit = left.gameObject.AddComponent<Image>();
            leftHit.color = new Color(0f, 0f, 0f, .001f);
            ScrollRect leftScroll = left.gameObject.AddComponent<ScrollRect>();
            leftScroll.horizontal = false;
            leftScroll.vertical = true;
            leftScroll.scrollSensitivity = 64f;
            leftScroll.inertia = false;
            leftScroll.movementType = ScrollRect.MovementType.Clamped;
            RectTransform entries = UIFactory.Rect(left, "Cooking perks list", 0, 0, 188,
                Mathf.Max(635, skill.Abilities.Count * 64 + 7));
            leftScroll.content = entries;
            leftScroll.viewport = left;

            AbilityData selected = skill.FindAbility(selectedCookingPerkId) ?? skill.Abilities[0];
            selectedCookingPerkId = selected.Id;
            for (int i = 0; i < skill.Abilities.Count; i++)
            {
                AbilityData perk = skill.Abilities[i];
                float y = i * 64 + 4;
                UIFactory.Image(entries, "Cooking perk selector " + perk.Id, "elements",
                    selected == perk ? "abilitybox_frame_active" : "abilitybox_bg_info", 0, y, 184, 59, 8);
                UIFactory.Label(entries, "Cooking perk name " + perk.Id, Localizer.AbilityName(perk.Id),
                    7, y + 4, 172, 30, 18, selected == perk ? UIFactory.Gold : UIFactory.White,
                    TextAnchor.MiddleLeft);
                UIFactory.Label(entries, "Cooking perk level " + perk.Id,
                    Localizer.F("settings-perk-short", perk.RequiredLevel, perk.MaxTier),
                    7, y + 31, 172, 23, 14, UIFactory.Grey, TextAnchor.MiddleLeft);
                UIFactory.Button(entries, "Pick cooking perk " + perk.Id, 0, y, 184, 59, () =>
                {
                    if (selectedCookingPerkId == perk.Id) return;
                    selectedCookingPerkId = perk.Id;
                    cookingPerkValuesPosition = 1f;
                    ShowSettingsSection(shownSettingsSection, true);
                });
            }
            leftScroll.verticalNormalizedPosition = cookingPerkListPosition;
            leftScroll.onValueChanged.AddListener(v => cookingPerkListPosition = v.y);

            RectTransform right = UIFactory.Rect(panel, "Cooking perk editor", 217, 52, 413, 650);
            UIFactory.Label(right, "Selected cooking perk", Localizer.AbilityName(selected.Id),
                8, 2, 396, 35, 24, UIFactory.Gold);
            EditSurvivalPerkInteger(right, "Cooking min level", "settings-perk-required-level",
                40, 1, skill.MaxLevel, selected.RequiredLevel, value =>
                {
                    CookingAbilityBalance.SetMinimum(selected, value);
                    ShowSettingsSection(shownSettingsSection, true);
                });
            EditSurvivalPerkInteger(right, "Cooking tier count", "settings-perk-tier-count",
                88, 1, CookingAbilityBalance.TierLimit(selected), selected.MaxTier, value =>
                {
                    CookingAbilityBalance.SetTierCount(selected, value);
                    ShowSettingsSection(shownSettingsSection, true);
                });

            UIFactory.Label(right, "Cooking perk values heading", Localizer.T("settings-perk-values-heading"),
                8, 145, 392, 33, 19, UIFactory.Gold);
            RectTransform viewport = UIFactory.Rect(right, "Cooking perk values viewport", 5, 181, 400, 387);
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
            float extraHeight = selected.Id == "durablebowl" ? 65f : 0f;
            float contentHeight = selected.MaxTier * (42f + valuesPerTier * 51f) + extraHeight + 16f;
            RectTransform list = UIFactory.Rect(viewport, "Cooking perk numeric values", 0, 0, 392,
                Mathf.Max(387f, contentHeight));
            scroll.content = list;
            scroll.viewport = viewport;
            float yPosition = 6f;

            if (selected.Id == "durablebowl")
            {
                UIFactory.Label(list, "Cooking dish base durability", Localizer.T("settings-cooking-bowl-base-durability"),
                    8, yPosition, 263, 38, 16, UIFactory.White, TextAnchor.MiddleLeft);
                InputField baseInput = CreateTimeInput(list, "Cooking dish base durability input",
                    283, yPosition, 98, 35);
                baseInput.contentType = InputField.ContentType.IntegerNumber;
                baseInput.text = ReusableDishesSystem.BaseDurability.ToString(CultureInfo.InvariantCulture);
                baseInput.onEndEdit.AddListener(raw =>
                {
                    int parsed;
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                        ReusableDishesSystem.SetDurability(Mathf.Clamp(parsed, 1, 100));
                    baseInput.text = ReusableDishesSystem.BaseDurability.ToString(CultureInfo.InvariantCulture);
                });
                yPosition += 65f;
            }

            for (int tier = 1; tier <= selected.MaxTier; tier++)
            {
                int currentTier = tier;
                UIFactory.Label(list, "Cooking perk tier " + tier,
                    Localizer.F("settings-perk-level-label", tier),
                    8, yPosition, 365, 32, 19, UIFactory.Gold);
                yPosition += 40f;
                for (int i = 0; i < valuesPerTier; i++)
                {
                    int arg = i;
                    int valueIndex = (tier - 1) * valuesPerTier + i;
                    string tierKey = "settings-perk-value-" + selected.Id + "-tier" + tier + "-" + i;
                    string label = Localizer.T(tierKey);
                    if (label == tierKey)
                    {
                        string commonKey = "settings-perk-value-" + selected.Id + "-" + i;
                        label = Localizer.T(commonKey);
                        if (label == commonKey) label = Localizer.F("settings-perk-parameter", i + 1);
                    }
                    UIFactory.Label(list, "Cooking perk value title " + tier + "_" + i, label,
                        8, yPosition, 263, 38, 16, UIFactory.White, TextAnchor.MiddleLeft);
                    InputField edit = CreateTimeInput(list, "Cooking perk value input " + tier + "_" + i,
                        283, yPosition, 98, 35);
                    edit.contentType = InputField.ContentType.IntegerNumber;
                    bool fixedInfinite = selected.Id == "durablebowl" && tier == 2;
                    edit.text = fixedInfinite ? "∞" : selected.Values[valueIndex].ToString(CultureInfo.InvariantCulture);
                    if (fixedInfinite)
                        edit.interactable = false;
                    else
                        edit.onEndEdit.AddListener(raw =>
                        {
                            int parsed;
                            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                                CookingAbilityBalance.SetValue(selected, currentTier, arg, parsed);
                            edit.text = selected.Values[(currentTier - 1) * selected.ValuesPerTier + arg]
                                .ToString(CultureInfo.InvariantCulture);
                        });
                    yPosition += 51f;
                }
                yPosition += 2f;
            }
            scroll.verticalNormalizedPosition = cookingPerkValuesPosition;
            scroll.onValueChanged.AddListener(v => cookingPerkValuesPosition = v.y);
            SettingsButton(right, "Reset cooking perk", Localizer.T("settings-perk-reset"),
                8, 582, 381, () =>
                {
                    CookingAbilityBalance.RestoreDefaults(selected);
                    ShowSettingsSection(shownSettingsSection, true);
                });
        }
    }
}
