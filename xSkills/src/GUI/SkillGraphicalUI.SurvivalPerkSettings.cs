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
        private string selectedSurvivalPerkId = "longlife";
        private float survivalPerkListPosition = 1f;
        private float survivalPerkValuesPosition = 1f;

        private void BuildSurvivalPerkSettings(RectTransform panel)
        {
            SkillData skill = SkillCatalog.Find("survival");
            if (skill == null) return;
            UIFactory.Label(panel, "Perk balance heading", Localizer.T("settings-survival-perks-intro"),
                15, 1, 614, 48, 17, UIFactory.White, TextAnchor.UpperLeft);

            RectTransform left = UIFactory.Rect(panel, "Perks picker viewport", 12, 53, 198, 647);
            left.gameObject.AddComponent<RectMask2D>();
            Image leftHit = left.gameObject.AddComponent<Image>();
            leftHit.color = new Color(0f, 0f, 0f, .001f);
            ScrollRect leftScroll = left.gameObject.AddComponent<ScrollRect>();
            leftScroll.horizontal = false;
            leftScroll.vertical = true;
            leftScroll.scrollSensitivity = 64f;
            leftScroll.inertia = false;
            leftScroll.movementType = ScrollRect.MovementType.Clamped;
            RectTransform entries = UIFactory.Rect(left, "Perks list", 0, 0, 188, Mathf.Max(647, skill.Abilities.Count * 64 + 7));
            leftScroll.content = entries;
            leftScroll.viewport = left;

            AbilityData selected = skill.FindAbility(selectedSurvivalPerkId) ?? skill.Abilities[0];
            selectedSurvivalPerkId = selected.Id;
            for (int i = 0; i < skill.Abilities.Count; i++)
            {
                AbilityData perk = skill.Abilities[i];
                float y = i * 64 + 4;
                UIFactory.Image(entries, "Perk selector " + perk.Id, "elements",
                    selected == perk ? "abilitybox_frame_active" : "abilitybox_bg_info", 0, y, 184, 59, 8);
                UIFactory.Label(entries, "Perk name " + perk.Id, Localizer.AbilityName(perk.Id),
                    7, y + 4, 172, 30, 18, selected == perk ? UIFactory.Gold : UIFactory.White,
                    TextAnchor.MiddleLeft);
                UIFactory.Label(entries, "Perk level " + perk.Id,
                    Localizer.F("settings-perk-short", perk.RequiredLevel, perk.MaxTier),
                    7, y + 31, 172, 23, 14, UIFactory.Grey, TextAnchor.MiddleLeft);
                UIFactory.Button(entries, "Pick perk " + perk.Id, 0, y, 184, 59, () =>
                {
                    if (selectedSurvivalPerkId == perk.Id) return;
                    selectedSurvivalPerkId = perk.Id;
                    survivalPerkValuesPosition = 1f;
                    ShowSettingsSection(shownSettingsSection, true);
                });
            }
            leftScroll.verticalNormalizedPosition = survivalPerkListPosition;
            leftScroll.onValueChanged.AddListener(v => survivalPerkListPosition = v.y);

            RectTransform right = UIFactory.Rect(panel, "Perk editor", 217, 52, 413, 650);
            UIFactory.Label(right, "Selected perk", Localizer.AbilityName(selected.Id),
                8, 2, 396, 35, 24, UIFactory.Gold);
            EditSurvivalPerkInteger(right, "Min level", "settings-perk-required-level",
                40, 1, skill.MaxLevel, selected.RequiredLevel, value =>
                {
                    SurvivalAbilityBalance.SetMinimum(selected, value);
                    ShowSettingsSection(shownSettingsSection, true);
                });
            EditSurvivalPerkInteger(right, "Tier count", "settings-perk-tier-count",
                88, 1, SurvivalAbilityBalance.MaximumTiers, selected.MaxTier, value =>
                {
                    SurvivalAbilityBalance.SetTierCount(selected, value);
                    ShowSettingsSection(shownSettingsSection, true);
                });

            UIFactory.Label(right, "Perk values heading", Localizer.T("settings-perk-values-heading"),
                8, 145, 392, 33, 19, UIFactory.Gold);
            RectTransform viewport = UIFactory.Rect(right, "Perk values viewport", 5, 181, 400, 394);
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
            float contentHeight = selected.MaxTier * (42f + valuesPerTier * 51f) + 16f;
            RectTransform list = UIFactory.Rect(viewport, "Perk numeric values", 0, 0, 392, Mathf.Max(394f, contentHeight));
            scroll.content = list;
            scroll.viewport = viewport;
            float yPosition = 6f;
            for (int tier = 1; tier <= selected.MaxTier; tier++)
            {
                int currentTier = tier;
                UIFactory.Label(list, "Perk tier " + tier, Localizer.F("settings-perk-level-label", tier),
                    8, yPosition, 365, 32, 19, UIFactory.Gold);
                yPosition += 40f;
                for (int i = 0; i < valuesPerTier; i++)
                {
                    int arg = i;
                    int index = (tier - 1) * valuesPerTier + i;
                    string specific = "settings-perk-value-" + selected.Id + "-" + i;
                    string label = Localizer.T(specific);
                    if (label == specific) label = Localizer.F("settings-perk-parameter", i + 1);
                    UIFactory.Label(list, "Perk value title " + tier + "_" + i, label,
                        8, yPosition, 263, 38, 16, UIFactory.White, TextAnchor.MiddleLeft);
                    InputField edit = CreateTimeInput(list, "Perk value input " + tier + "_" + i,
                        283, yPosition, 98, 35);
                    edit.contentType = InputField.ContentType.IntegerNumber;
                    edit.text = selected.Values[index].ToString(CultureInfo.InvariantCulture);
                    edit.onEndEdit.AddListener(raw =>
                    {
                        int parsed;
                        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                        {
                            SurvivalAbilityBalance.SetValue(selected, currentTier, arg, parsed);
                            edit.text = selected.Values[(currentTier - 1) * selected.ValuesPerTier + arg]
                                .ToString(CultureInfo.InvariantCulture);
                        }
                        else edit.text = selected.Values[(currentTier - 1) * selected.ValuesPerTier + arg]
                            .ToString(CultureInfo.InvariantCulture);
                    });
                    yPosition += 51f;
                }
                yPosition += 2f;
            }
            scroll.verticalNormalizedPosition = survivalPerkValuesPosition;
            scroll.onValueChanged.AddListener(v => survivalPerkValuesPosition = v.y);
            SettingsButton(right, "Reset selected perk", Localizer.T("settings-perk-reset"),
                8, 591, 381, () =>
                {
                    SurvivalAbilityBalance.RestoreDefaults(selected);
                    ShowSettingsSection(shownSettingsSection, true);
                });
        }

        private void EditSurvivalPerkInteger(RectTransform panel, string id, string key,
            float y, int minimum, int maximum, int current, Action<int> apply)
        {
            UIFactory.Label(panel, id + " label", Localizer.T(key), 8, y, 257, 37,
                17, UIFactory.White, TextAnchor.MiddleLeft);
            InputField input = CreateTimeInput(panel, id + " input", 286, y, 97, 36);
            input.contentType = InputField.ContentType.IntegerNumber;
            input.text = current.ToString(CultureInfo.InvariantCulture);
            input.onEndEdit.AddListener(raw =>
            {
                int value;
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    apply(Mathf.Clamp(value, minimum, maximum));
                else input.text = current.ToString(CultureInfo.InvariantCulture);
            });
        }
    }
}
