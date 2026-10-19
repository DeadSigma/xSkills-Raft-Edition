using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using HMLLibrary;

using xSkills.Services;
using xSkills.Systems;
namespace xSkills.GUI
{
    internal sealed partial class SkillGraphicalUI
    {
        private const int HomeDietSegments = 16;
        private readonly Image[,] homeDietFill = new Image[4, HomeDietSegments];
        private readonly Text[] homeDietPercent = new Text[4];
        private Text dietHealthBonus;
        private PlayerModelPreview playerPreview;
        private RawImage playerPortrait;
        private Text playerPreviewFallback;
        private Text playerNameText;
        private Text healthText;
        private Text hungerText;
        private Text thirstText;
        private Text bodyTemperatureText;
        private Text wetnessText;
        private Text homeDateText;
        private Text homeClockText;
        private Text homeTemperatureText;
        private Text homeWeatherText;
        private bool playerPreviewAttempted;
        private float nextPlayerPreviewRetryAt;
        private float nextPlayerHomeUpdate;

        private void BuildPlayerHomePage()
        {
            const float left = 2f;
            const float leftWidth = 818f;
            const float right = 838f;
            const float rightWidth = 928f;

            HomePanel("Character", left, -22, leftWidth, 630, Localizer.T("ui-home-title"));
            playerNameText = UIFactory.Label(abilitiesRoot, "Character name", Localizer.T("ui-player-no-world"),
                left + 35, 67, leftWidth - 70, 39, 25, UIFactory.White, TextAnchor.MiddleCenter);

            UIFactory.Fill(abilitiesRoot, "Portrait inset", 252, 110, 318, 480,
                new Color(.08f, .07f, .06f, .53f));
            UIFactory.Image(abilitiesRoot, "Portrait inset border", "elements",
                "abilitybox_frame_inactive", 252, 110, 318, 480, 15);
            RectTransform portrait = UIFactory.Rect(abilitiesRoot, "Player 3D portrait", 256, 115, 310, 465);
            playerPortrait = portrait.gameObject.AddComponent<RawImage>();
            playerPortrait.color = Color.white;
            playerPortrait.raycastTarget = false;
            playerPreviewFallback = UIFactory.Label(abilitiesRoot, "Preview fallback", "", 268, 305, 287, 70,
                19, UIFactory.Grey, TextAnchor.MiddleCenter);

            HomePanel("Stats", right, -22, rightWidth, 986, Localizer.T("ui-home-stats"));
            HomeSection("Diet", right + 27, 83, rightWidth - 54, Localizer.T("ui-home-nutrition"));
            HomeNutritionRow(2, "Fruit", Localizer.T("ui-home-fruit"), right, 132, UIFactory.Hex("88A65B"));
            HomeNutritionRow(3, "Vegetables", Localizer.T("ui-home-vegetables"), right, 170, UIFactory.Hex("88A65B"));
            HomeNutritionRow(0, "Meat", Localizer.T("ui-home-meat"), right, 208, UIFactory.Hex("88A65B"));
            HomeNutritionRow(1, "Dairy", Localizer.T("ui-home-dairy"), right, 246, UIFactory.Hex("88A65B"));

            HomeSection("Physical", right + 27, 300, rightWidth - 54, Localizer.T("ui-home-physical"));
            healthText = HomePhysicalRow("Health", Localizer.T("ui-home-health-points"),
                Localizer.T("ui-home-preview-health"), "+10 HP", right, 352);
            hungerText = HomePhysicalRow("Satiety", Localizer.T("ui-home-satiety"),
                Localizer.T("ui-home-preview-satiety"), null, right, 409);
            thirstText = HomePhysicalRow("Thirst", Localizer.T("ui-thirst"),
                Localizer.T("ui-home-preview-thirst"), null, right, 466);
            bodyTemperatureText = HomePhysicalRow("Body temperature", Localizer.T("ui-home-body-temperature"),
                "36.6 °C", null, right, 523);
            wetnessText = HomePhysicalRow("Wetness", Localizer.T("ui-home-wetness"),
                Localizer.T("ui-home-dry"), null, right, 580);
            HomePhysicalRow("Walk speed", Localizer.T("ui-home-walk-speed"),
                "100%", "+10%", right, 637);
            HomePhysicalRow("Hunger rate", Localizer.T("ui-home-hunger-rate"),
                "100%", "-10%", right, 694);
            HomePhysicalRow("Swim speed", Localizer.T("ui-home-swim-speed"),
                "100%", "+15%", right, 751);
            HomePhysicalRow("Oxygen consumption", Localizer.T("ui-home-oxygen-consumption"),
                "100%", "-20%", right, 808);
            HomePhysicalRow("Oxygen reserve", Localizer.T("ui-home-oxygen-reserve"),
                Localizer.T("ui-home-preview-oxygen"), Localizer.T("ui-home-bonus-oxygen"), right, 865);

            UIFactory.Label(abilitiesRoot, "Stats demo note", Localizer.T("ui-home-bonus-note"),
                right + 27, 914, rightWidth - 54, 26, 16, UIFactory.Grey, TextAnchor.MiddleLeft);

            HomePanel("Environment", left, 624, leftWidth, 340, Localizer.T("ui-home-environment"));
            UIFactory.Label(abilitiesRoot, "Date label", Localizer.T("ui-home-date"),
                left + 27, 700, 110, 31, 21, UIFactory.White, TextAnchor.MiddleLeft);
            homeDateText = UIFactory.Label(abilitiesRoot, "Calendar date", Localizer.T("ui-home-preview-date"),
                left + 137, 700, 455, 31, 21, UIFactory.Gold, TextAnchor.MiddleLeft);
            homeClockText = UIFactory.Label(abilitiesRoot, "Time of day", Localizer.T("ui-home-preview-time"),
                left + 628, 700, 160, 31, 21, UIFactory.Gold, TextAnchor.MiddleRight);
            homeTemperatureText = HomeEnvironmentRow("Temperature", Localizer.T("ui-home-temperature"), left, 770);
            homeWeatherText = HomeEnvironmentRow("Weather", Localizer.T("ui-home-weather"), left, 840);
            UIFactory.Label(abilitiesRoot, "Environment note", Localizer.T("ui-home-environment-note"),
                left + 27, 914, leftWidth - 54, 29, 16, UIFactory.Grey, TextAnchor.MiddleLeft);

            RefreshWorldEnvironment();
            RefreshPlayerHome(true);
        }

        private void HomePanel(string id, float x, float y, float w, float h, string title)
        {
            Color background = new Color(.15f, .115f, .09f, .90f);
            Color border = new Color(.59f, .46f, .31f, .52f);
            UIFactory.Fill(abilitiesRoot, id + " background", x, y, w, h, background);
            UIFactory.Fill(abilitiesRoot, id + " top border", x, y, w, 2, border);
            UIFactory.Fill(abilitiesRoot, id + " left border", x, y, 2, h, border);
            UIFactory.Fill(abilitiesRoot, id + " right border", x + w - 2, y, 2, h, border);
            UIFactory.Fill(abilitiesRoot, id + " bottom border", x, y + h - 2, w, 2, border);
            UIFactory.Fill(abilitiesRoot, id + " header background", x + 2, y + 2, w - 4, 46,
                new Color(.27f, .20f, .15f, .97f));
            UIFactory.Fill(abilitiesRoot, id + " header separator", x + 2, y + 47, w - 4, 1, border);
            UIFactory.Label(abilitiesRoot, id + " header", title, x + 24, y + 7, w - 45, 33,
                25, UIFactory.White, TextAnchor.MiddleLeft);
        }

        private void HomeSection(string id, float x, float y, float width, string title)
        {
            UIFactory.Label(abilitiesRoot, id + " section title", title, x, y, width, 32,
                23, UIFactory.Gold, TextAnchor.MiddleLeft);
            UIFactory.Fill(abilitiesRoot, id + " divider", x, y + 37, width, 1,
                new Color(.67f, .48f, .26f, .34f));
        }

        private void HomeNutritionRow(int category, string id, string label, float panelX, float y, Color fillColor)
        {
            UIFactory.Label(abilitiesRoot, id + " label", label, panelX + 27, y, 395, 28,
                20, UIFactory.White, TextAnchor.MiddleLeft);
            for (int i = 0; i < HomeDietSegments; i++)
            {
                float x = panelX + 532 + i * 19f;
                UIFactory.Fill(abilitiesRoot, id + " meter slot " + i, x, y + 7, 16, 14,
                    new Color(.065f, .063f, .054f, .92f));
                Image filled = UIFactory.Fill(abilitiesRoot, id + " meter filled " + i,
                    x + 1, y + 8, 14, 12, fillColor);
                filled.enabled = false;
                homeDietFill[category, i] = filled;
            }
            homeDietPercent[category] = UIFactory.Label(abilitiesRoot, id + " percent", "0%",
                panelX + 849, y, 56, 28, 18, UIFactory.Grey, TextAnchor.MiddleRight);
        }

        private void RefreshHomeDiet()
        {
            for (int category = 0; category < 4; category++)
            {
                float fill = DietSystem.Level(category);
                int filled = Mathf.RoundToInt(fill * HomeDietSegments);
                for (int segment = 0; segment < HomeDietSegments; segment++)
                {
                    Image image = homeDietFill[category, segment];
                    if (image != null) image.enabled = segment < filled;
                }
                if (homeDietPercent[category] != null)
                    homeDietPercent[category].text = Mathf.RoundToInt(fill * 100f) + "%";
            }
        }

        private void RefreshHomeHealth(PlayerStats stats)
        {
            if (healthText == null) return;
            float current, maximum;
            if (stats == null || !TryReadHomeStat(stats, out current, out maximum, "stat_health", "health"))
            {
                healthText.text = Localizer.T("ui-home-preview-health");
                if (dietHealthBonus != null) dietHealthBonus.text = "";
                return;
            }

            float extraHealth = stats.stat_BonusHealth != null
                ? Mathf.Max(0f, stats.stat_BonusHealth.Value) : 0f;
            float fullHealth = Mathf.Max(0f, current) + extraHealth;
            healthText.text = maximum > 0f
                ? fullHealth.ToString("0.#") + " / " + maximum.ToString("0.#")
                : fullHealth.ToString("0.#");

            if (dietHealthBonus != null)
            {
                // Учитываются дополнительные HP диеты и ванильных эффектов
                float increasedMaximum = Mathf.Max(0f, maximum - 100f);
                float totalBonus = extraHealth + increasedMaximum;
                dietHealthBonus.text = totalBonus >= .05f
                    ? "+" + totalBonus.ToString("0.#") + " HP" : "";
            }
        }

        private Text HomePhysicalRow(string id, string label, string previewValue, string bonus,
            float panelX, float y)
        {
            UIFactory.Label(abilitiesRoot, id + " label", label, panelX + 27, y, 378, 32,
                20, UIFactory.White, TextAnchor.MiddleLeft);
            Text current = UIFactory.Label(abilitiesRoot, id + " value", previewValue,
                panelX + 573, y, 235, 32, 20, UIFactory.White, TextAnchor.MiddleRight);
            if (!string.IsNullOrEmpty(bonus))
            {
                Text bonusLabel = UIFactory.Label(abilitiesRoot, id + " bonus", bonus,
                    panelX + 813, y, 92, 32, 19, UIFactory.Green, TextAnchor.MiddleRight);
                if (id == "Health") dietHealthBonus = bonusLabel;
            }
            return current;
        }

        private Text HomeEnvironmentRow(string id, string label, float x, float y)
        {
            UIFactory.Label(abilitiesRoot, id + " label", label, x + 27, y, 345, 31,
                21, UIFactory.White, TextAnchor.MiddleLeft);
            return UIFactory.Label(abilitiesRoot, id + " value", "", x + 375, y, 413, 31,
                20, UIFactory.Gold, TextAnchor.MiddleRight);
        }

        private void TickPlayerHome(float dt)
        {
            if (playerPreview != null && playerPreview.Available) playerPreview.Render();
            if (Time.unscaledTime < nextPlayerHomeUpdate) return;
            nextPlayerHomeUpdate = Time.unscaledTime + .25f;
            RefreshPlayerHome(false);
            RefreshWorldEnvironment();
        }

        private void RefreshWorldEnvironment()
        {
            if (homeDateText == null || homeClockText == null ||
                homeTemperatureText == null || homeWeatherText == null) return;

            WorldEnvironment.Snapshot state;
            if (!WorldEnvironment.TryRead(out state))
            {
                homeDateText.text = "-";
                homeClockText.text = "--:--";
                homeTemperatureText.text = "-";
                homeWeatherText.text = "-";
                return;
            }

            homeDateText.text = state.Date;
            homeClockText.text = state.Clock;
            homeTemperatureText.text = state.Temperature.ToString("0.0") + " °C";
            homeWeatherText.text = string.IsNullOrEmpty(state.Climate) ? state.Weather :
                state.Weather + " - " + state.Climate;
        }

        private void RefreshPlayerHome(bool initial)
        {
            Network_Player player = null;
            try { player = RAPI.GetLocalPlayer(); }
            catch (Exception) { }
            if (player == null)
            {
                if (playerNameText != null) playerNameText.text = Localizer.T("ui-player-no-world");
                if (playerPreviewFallback != null) playerPreviewFallback.text = Localizer.T("ui-player-no-world");
                return;
            }

            if (playerNameText != null) playerNameText.text = GetPlayerDisplayName(player);
            PlayerStats stats = player.Stats;
            RefreshHomeHealth(stats);
            RefreshConsumable(stats != null ? stats.stat_hunger : null, hungerText);
            RefreshConsumable(stats != null ? stats.stat_thirst : null, thirstText);
            RefreshHomeDiet();
            RefreshHomeTemperature();

            if (!playerPreviewAttempted ||
                ((playerPreview == null || !playerPreview.Available) && Time.unscaledTime >= nextPlayerPreviewRetryAt))
            {
                playerPreviewAttempted = true;
                nextPlayerPreviewRetryAt = Time.unscaledTime + 5f;
                if (playerPreview != null) playerPreview.Dispose();
                playerPreview = new PlayerModelPreview(player);
                if (playerPreview.Available)
                {
                    if (playerPortrait != null) playerPortrait.texture = playerPreview.Texture;
                    if (playerPreviewFallback != null) playerPreviewFallback.gameObject.SetActive(false);
                }
                else if (playerPreviewFallback != null)
                {
                    playerPreviewFallback.text = Localizer.T("ui-preview-unavailable");
                    playerPreviewFallback.gameObject.SetActive(true);
                }
            }
        }

        private void RefreshHomeTemperature()
        {
            if (bodyTemperatureText != null)
                bodyTemperatureText.text = BodyTemperatureSystem.Temperature.ToString("0.0") + " °C";
            if (wetnessText == null) return;

            float amount = BodyTemperatureSystem.Wetness;
            string key = amount < 1f ? "ui-home-dry" : amount < 55f ? "ui-home-wet-slight" :
                amount < 95f ? "ui-home-wet" : "ui-home-wet-soaked";
            wetnessText.text = Mathf.RoundToInt(amount) + "% - " + Localizer.T(key);
        }

        private static string GetPlayerDisplayName(Network_Player player)
        {
            object visual = FindHomeMember(player, "visualName", "VisualName", "realName", "RealName");
            string name = visual as string;
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            name = player.gameObject.name;
            const string servicePrefix = "PlayerStats,";
            if (name.StartsWith(servicePrefix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(servicePrefix.Length).Trim();
            return name;
        }

        private static void RefreshConsumable(Stat_Bonus_Consumable stat, Text label)
        {
            if (label == null) return;
            if (stat == null || stat.Normal == null)
            {
                label.text = "-";
                return;
            }

            float current = Mathf.Max(0f, stat.Normal.Value);
            float maximum = Mathf.Max(0f, stat.Normal.Max);
            label.text = maximum > 0f
                ? current.ToString("0.#") + " / " + maximum.ToString("0.#")
                : current.ToString("0.#");

            if (stat.Bonus != null && stat.Bonus.Value > .05f)
                label.text += " (+" + stat.Bonus.Value.ToString("0.#") + ")";
        }

        private static void RefreshHomeStat(object stats, Text text, string previewValue, params string[] names)
        {
            if (text == null) return;
            float current, maximum;
            if (!TryReadHomeStat(stats, out current, out maximum, names))
            {
                text.text = previewValue;
                return;
            }
            text.text = maximum > 0f
                ? current.ToString("0.#") + " / " + maximum.ToString("0.#")
                : current.ToString("0.#");
        }

        private static bool TryReadHomeStat(object owner, out float current, out float maximum, params string[] names)
        {
            current = 0f;
            maximum = 0f;
            object stat = FindHomeMember(owner, names);
            if (stat == null) return false;
            if (!TryHomeNumber(stat, out current))
            {
                object value = FindHomeMember(stat, "CurrentValue", "currentValue", "Value", "value", "Current", "current");
                if (!TryHomeNumber(value, out current)) return false;
            }
            object upper = FindHomeMember(stat, "MaxValue", "maxValue", "MaximumValue", "Maximum", "Max", "max");
            float foundMax;
            if (TryHomeNumber(upper, out foundMax)) maximum = foundMax;
            return true;
        }

        private static bool TryHomeNumber(object value, out float number)
        {
            number = 0;
            if (value == null) return false;
            if (!(value is float || value is double || value is int || value is long || value is short)) return false;
            try { number = Convert.ToSingle(value); return !float.IsNaN(number) && !float.IsInfinity(number); }
            catch (Exception) { return false; }
        }

        private static object FindHomeMember(object owner, params string[] names)
        {
            if (owner == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
            Type type = owner.GetType();
            foreach (string name in names)
            {
                try
                {
                    FieldInfo field = type.GetField(name, flags);
                    if (field != null) return field.GetValue(owner);
                    PropertyInfo property = type.GetProperty(name, flags);
                    if (property != null && property.GetIndexParameters().Length == 0)
                        return property.GetValue(owner, null);
                }
                catch (Exception) { }
            }
            return null;
        }

        private void DisposePlayerHome()
        {
            if (playerPreview != null) playerPreview.Dispose();
            playerPreview = null;
            playerPortrait = null;
            playerPreviewFallback = null;
            dietHealthBonus = null;
            for (int category = 0; category < 4; category++)
            {
                homeDietPercent[category] = null;
                for (int i = 0; i < HomeDietSegments; i++) homeDietFill[category, i] = null;
            }
            playerNameText = null;
            healthText = null;
            hungerText = null;
            thirstText = null;
            bodyTemperatureText = null;
            wetnessText = null;
            homeDateText = null;
            homeClockText = null;
            homeTemperatureText = null;
            homeWeatherText = null;
            playerPreviewAttempted = false;
            nextPlayerPreviewRetryAt = 0f;
            nextPlayerHomeUpdate = 0f;
        }
    }
}
