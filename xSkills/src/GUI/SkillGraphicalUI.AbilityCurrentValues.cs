using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using xSkills.Services;
using xSkills.Skills;
using xSkills.Systems;

namespace xSkills.GUI
{
    internal sealed partial class SkillGraphicalUI
    {
        private static readonly HashSet<string> ChancePerks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "laststand", "strongline", "carefulcaster", "baitmaster", "doublehook",
            "happymeal", "preserver"
        };

        private static readonly HashSet<string> PercentPerks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chef", "eggtimer", "preserver", "happymeal", "fastfood",
            "fisher", "strongline", "carefulcaster", "baitmaster", "doublehook",
            "shipwright", "coastalpilot", "opensea"
        };

        private void ClearCurrentValues()
        {
            if (tooltipCurrentValues != null)
            {
                tooltipCurrentValues.text = "";
                tooltipCurrentValues.gameObject.SetActive(false);
            }
            PlacePerkRequirements(260f);
        }

        private void PlacePerkRequirements(float dividerY)
        {
            if (requirementDivider == null || tooltipRequirements == null) return;
            ((RectTransform)requirementDivider.transform).anchoredPosition = new Vector2(4f, -dividerY);
            ((RectTransform)tooltipRequirements.transform).anchoredPosition = new Vector2(9f, -(dividerY + 10f));
            ((RectTransform)tooltipRequirements.transform).sizeDelta = new Vector2(352f, 855f - dividerY - 16f);
        }

        private static string Amount(float value, string suffix = "", bool signed = false)
        {
            string number = value.ToString("0.#", CultureInfo.CurrentCulture);
            return (signed && value > 0f ? "+" : "") + number + suffix;
        }

        private static string ColorAmount(string value)
        {
            return "<color=#7AC62F>" + value + "</color>";
        }

        private static void AddCurrentLine(List<string> lines, string key, string number)
        {
            lines.Add(Localizer.T(key) + ": " + ColorAmount(number));
        }

        private static int TierValue(AbilityData ability, int index)
        {
            if (ability == null || ability.PreviewTier <= 0 || index >= ability.ValuesPerTier) return 0;
            int at = (Mathf.Min(ability.PreviewTier, ability.MaxTier) - 1) * ability.ValuesPerTier + index;
            return at >= 0 && at < ability.Values.Length ? ability.Values[at] : 0;
        }

        private void UpdateCurrentPerkValues()
        {
            if (tooltipCurrentValues == null || selectedAbility == null || selectedSkill == null ||
                selectedAbility.PreviewTier <= 0 || selectedAbility.ValuesPerTier == 0)
            {
                ClearCurrentValues();
                return;
            }

            AbilityData ability = selectedAbility;
            List<string> lines = new List<string>();
            int first = TierValue(ability, 0);
            int second = TierValue(ability, 1);
            switch (ability.Id)
            {
                case "longlife":
                    AddCurrentLine(lines, "ui-perk-current-health", Amount(SurvivalPerkSystem.CurrentHealthBonusPercent, "%", true));
                    break;
                case "strongback":
                    AddCurrentLine(lines, "settings-perk-value-strongback-0",
                        Amount(StrongBackSystem.ActiveSlots));
                    break;
                case "hugestomach":
                    AddCurrentLine(lines, "ui-perk-current-stomach-bonus",
                        Amount(SurvivalPerkSystem.CurrentStomachBonus, " " + Localizer.T("ui-perk-stomach-units"), true));
                    float? capacity = SurvivalPerkSystem.CurrentStomachCapacity;
                    if (capacity.HasValue)
                        AddCurrentLine(lines, "ui-perk-current-stomach-capacity",
                            Amount(capacity.Value, " " + Localizer.T("ui-perk-stomach-units")));
                    break;
                case "canteencook":
                    AddCurrentLine(lines, "ui-canteencook-batches",
                        Amount(ability.PreviewTier >= 2 ? CanteenCookSystem.BatchLimit : first));
                    break;
                case "durablebowl":
                    if (ability.PreviewTier >= 2)
                        lines.Add(Localizer.T("ui-bowl-unbreakable"));
                    else
                        AddCurrentLine(lines, "ui-bowl-durability",
                            Amount(ReusableDishesSystem.BaseDurability * ReusableDishesSystem.FirstTierMultiplier));
                    break;
                case "laststand":
                    float? chance = SurvivalPerkSystem.CurrentLastStandChancePercent;
                    AddCurrentLine(lines, "ui-perk-current-chance", chance.HasValue ? Amount(chance.Value, "%") : "—");
                    float? remainingDays = SurvivalPerkSystem.CurrentLastStandCooldownDays;
                    if (remainingDays.HasValue)
                        lines.Add(remainingDays.Value <= 0f
                            ? Localizer.T("ui-perk-laststand-ready")
                            : Localizer.F("ui-perk-laststand-cooldown", Mathf.CeilToInt(remainingDays.Value)));
                    break;
                case "photosynthesis":
                    AddCurrentLine(lines, "ui-perk-current-regeneration", Amount(SurvivalPerkSystem.CurrentPhotosynthesisBonusPercent, "%", true));
                    break;
                case "diver":
                    AddCurrentLine(lines, "ui-perk-current-oxygen", Amount(Mathf.Clamp(first, 0, 95), "%"));
                    break;
                case "featherfall":
                    AddCurrentLine(lines, "ui-perk-current-fall-flat", Amount(first));
                    AddCurrentLine(lines, "ui-perk-current-fall-percent", Amount(Mathf.Clamp(second, 0, 100), "%"));
                    break;
                case "meatshield":
                    AddCurrentLine(lines, "ui-perk-current-damage", Amount(Mathf.Clamp(first, 0, 100), "%"));
                    AddCurrentLine(lines, "ui-perk-current-satiety-cost", "×" + Amount(second));
                    break;
                case "scout":
                    AddCurrentLine(lines, "ui-perk-current-speed", Amount(first, "%", true));
                    break;
                case "sprinter":
                    AddCurrentLine(lines, "ui-perk-current-speed", Amount(first, "%", true));
                    AddCurrentLine(lines, "ui-perk-current-hunger", Amount(second, "%", true));
                    break;
                case "forecast":
                    AddCurrentLine(lines, "ui-perk-current-forecast", Amount(first));
                    break;
                case "angler":
                    // Ускорение вычисляется из настроек рыбалки, а не из массива описания
                    WaterSurvivalSettings fishing = WaterSurvivalSettings.Current;
                    float baseDelay = fishing != null ? Mathf.Max(.1f, fishing.FishingDelayMultiplier) : 1f;
                    float speed = fishing != null ?
                        100f * (1f - Mathf.Clamp(FishingIntegration.DelayMultiplier / baseDelay, 0f, 1f)) : 0f;
                    AddCurrentLine(lines, "ui-perk-current-fishing", Amount(speed, "%", true));
                    break;
                default:
                    // Значения остальных перков пока не подключены к игровым эффектам
                    bool chancePerk = ChancePerks.Contains(ability.Id);
                    bool percentPerk = chancePerk || PercentPerks.Contains(ability.Id);
                    string description = Localizer.AbilityDescription(ability.Id);
                    for (int i = 0; i < ability.ValuesPerTier; i++)
                    {
                        bool percent = percentPerk || Regex.IsMatch(description, @"\{" + i + @"\}%");
                        string value = Amount(TierValue(ability, i), percent ? "%" : "");
                        string key = chancePerk ? "ui-perk-configured-chance" : "ui-perk-configured-value";
                        if (ability.ValuesPerTier > 1)
                            lines.Add(Localizer.F("ui-perk-parameter-line", i + 1, ColorAmount(value)));
                        else
                            AddCurrentLine(lines, key, value);
                    }
                    lines.Add(Localizer.T("ui-perk-not-implemented"));
                    break;
            }

            string shown = string.Join("\n", lines.ToArray());
            if (tooltipCurrentValues.text != shown) tooltipCurrentValues.text = shown;
            tooltipCurrentValues.gameObject.SetActive(true);
            // Отступ учитывает локализацию и перенос длинных строк
            float height = Mathf.Max(28f, Mathf.Ceil(tooltipCurrentValues.preferredHeight) + 6f);
            ((RectTransform)tooltipCurrentValues.transform).sizeDelta = new Vector2(352f, height);
            PlacePerkRequirements(260f + height + 8f);
        }
    }
}
