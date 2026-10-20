using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using xSkills.Skills;

using xSkills.Services;
using xSkills.Systems;
namespace xSkills.GUI
{
    internal sealed partial class SkillGraphicalUI
    {
        private RectTransform settingsRightOverlay;
        private RectTransform settingsListViewport;
        private readonly List<Image> settingsFrames = new List<Image>();
        private readonly List<string> settingsSectionIds = new List<string>();
        private int pinnedSettingsSection = -1;
        private int shownSettingsSection = -1;

        private void BuildSettingsPage()
        {
            pinnedSettingsSection = -1;
            shownSettingsSection = -1;
            settingsFrames.Clear();
            settingsSectionIds.Clear();
            settingsSectionIds.Add("audio");
            if (Raft_Network.IsHost)
            {
                settingsSectionIds.Add("timeworld");
                settingsSectionIds.Add("survival");
                settingsSectionIds.Add("balance");
                settingsSectionIds.Add("diet");
            }
            foreach (SkillData skill in skills) settingsSectionIds.Add("skill:" + skill.Id);

            UIFactory.Label(abilitiesRoot, "Settings heading", Localizer.T("settings-heading"),
                35, 12, 980, 54, 36, UIFactory.Gold);
            UIFactory.Label(abilitiesRoot, "Settings overview", Localizer.T("settings-overview"),
                35, 67, 980, 60, 21, UIFactory.White, TextAnchor.UpperLeft);

            const float viewportHeight = 654f;
            const float rowStep = 106f;
            settingsListViewport = UIFactory.Rect(abilitiesRoot, "Settings list viewport", 35, 143, 950, viewportHeight);
            settingsListViewport.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = settingsListViewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 75f;

            float listHeight = Mathf.Max(viewportHeight, settingsSectionIds.Count * rowStep - 14f);
            RectTransform list = UIFactory.Rect(settingsListViewport, "Settings rows", 0, 0, 950, listHeight);
            scroll.content = list;
            scroll.viewport = settingsListViewport;

            for (int i = 0; i < settingsSectionIds.Count; i++)
            {
                int selectedIndex = i;
                string id = settingsSectionIds[i];
                float y = i * rowStep;
                UIFactory.Image(list, "Settings " + id + " background", "elements", "abilitybox_bg_info",
                    0, y, 950, 94);
                Image frame = UIFactory.Image(list, "Settings " + id + " frame", "elements",
                    "abilitybox_frame_inactive", 0, y, 950, 94, 15);
                settingsFrames.Add(frame);

                string title = SettingsSectionTitle(id);
                string summary = SettingsSectionSummary(id);
                UIFactory.Label(list, "Settings " + id + " title", title,
                    22, y + 10, 900, 39, 28, UIFactory.Gold);
                UIFactory.Label(list, "Settings " + id + " summary", summary,
                    22, y + 52, 900, 28, 18, UIFactory.White);

                UIFactory.Button(list, "Settings " + id + " button", 0, y, 950, 94,
                    () => PinSettingsSection(selectedIndex),
                    () => { if (pinnedSettingsSection < 0) ShowSettingsSection(selectedIndex); },
                    () => { if (pinnedSettingsSection < 0) ShowSettingsSection(0); });
            }
            scroll.verticalNormalizedPosition = 1f;
            if (descriptionViewport != null) descriptionViewport.gameObject.SetActive(false);
            ShowSettingsSection(0, true);
        }

        private string SettingsSectionTitle(string id)
        {
            if (id == "timeworld") return Localizer.T("settings-timeworld-title");
            if (id == "survival") return Localizer.T("settings-survival-title");
            if (id == "audio") return Localizer.T("settings-audio-title");
            if (id == "balance") return Localizer.T("settings-balance-title");
            if (id == "diet") return Localizer.T("settings-diet-title");
            if (id.StartsWith("skill:", StringComparison.Ordinal))
                return Localizer.SkillName(id.Substring(6));
            return id;
        }

        private string SettingsSectionSummary(string id)
        {
            if (id == "timeworld") return Localizer.T("settings-timeworld-summary");
            if (id == "survival") return Localizer.T("settings-survival-summary");
            if (id == "audio") return Localizer.T("settings-audio-summary");
            if (id == "balance") return Localizer.T("settings-balance-summary");
            if (id == "diet") return Localizer.T("settings-diet-summary");
            return Localizer.T("settings-skill-summary");
        }

        private void PinSettingsSection(int index)
        {
            if (index < 0 || index >= settingsSectionIds.Count) return;
            pinnedSettingsSection = pinnedSettingsSection == index ? -1 : index;
            SoundPlayer.Play("pagesub");
            ShowSettingsSection(pinnedSettingsSection >= 0 ? pinnedSettingsSection : 0, true);
        }

        private void RefreshSettingsFrames()
        {
            for (int i = 0; i < settingsFrames.Count; i++)
                if (settingsFrames[i] != null)
                    settingsFrames[i].sprite = ResourceLoader.Sprite("elements",
                        pinnedSettingsSection == i || shownSettingsSection == i
                            ? "abilitybox_frame_active" : "abilitybox_frame_inactive", 15);
        }

        private void ShowSettingsSection(int index, bool force = false)
        {
            if (page != "_settings" || index < 0 || index >= settingsSectionIds.Count) return;
            if (!force && shownSettingsSection == index) return;
            shownSettingsSection = index;
            RefreshSettingsFrames();
            string id = settingsSectionIds[index];
            SetRightPanel(SettingsSectionTitle(id), "");
            if (settingsRightOverlay != null)
            {
                settingsRightOverlay.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(settingsRightOverlay.gameObject);
                settingsRightOverlay = null;
            }
            settingsRightOverlay = UIFactory.Rect(rightInfo, "Settings content", 0, 54, 645, 775);
            if (id == "audio") BuildSoundSettings(settingsRightOverlay);
            else if (id == "timeworld") BuildTimeWorldSettings(settingsRightOverlay);
            else if (id == "survival") BuildSurvivalSettings(settingsRightOverlay);
            else if (id == "balance") BuildBalanceSettings(settingsRightOverlay);
            else if (id == "diet") BuildDietRulesPage(settingsRightOverlay);
            else BuildSkillSettings(settingsRightOverlay, id.Substring(6));
        }

        private void BuildSoundSettings(RectTransform parent)
        {
            UIFactory.Label(parent, "Volume heading", Localizer.T("settings-volume-title"),
                22, 24, 580, 42, 26, UIFactory.Gold);
            Text valueText = UIFactory.Label(parent, "Volume percent", "",
                525, 80, 85, 35, 21, UIFactory.White, TextAnchor.MiddleRight);
            Slider slider = SettingsSlider(parent, "Master volume", 22, 86, 480, 0f, 100f,
                SoundPlayer.Volume * 100f, volume =>
                {
                    SoundPlayer.Volume = volume / 100f;
                    if (valueText != null) valueText.text = Mathf.RoundToInt(volume) + "%";
                });
            valueText.text = Mathf.RoundToInt(slider.value) + "%";
            UIFactory.Label(parent, "Volume details", Localizer.T("settings-volume-desc"),
                22, 152, 585, 130, 20, UIFactory.White, TextAnchor.UpperLeft);
            SettingsButton(parent, "Test volume", Localizer.T("settings-test-sound"), 22, 300, 260,
                () => SoundPlayer.Play("page"));
        }

        private void BuildBalanceSettings(RectTransform parent)
        {
            if (!Raft_Network.IsHost) return;
            UIFactory.Label(parent, "Balance explanation", Localizer.T("settings-balance-intro"),
                22, 16, 590, 100, 19, UIFactory.White, TextAnchor.UpperLeft);
            SettingsBalanceSlider(parent, "swim", "settings-swim-cap", 125);
            SettingsBalanceSlider(parent, "dive", "settings-dive-cap", 248);
            SettingsBalanceSlider(parent, "oxygen", "settings-oxygen-cap", 371);
            UIFactory.Label(parent, "Balance template notice", Localizer.T("settings-balance-future"),
                22, 520, 590, 170, 18, UIFactory.Grey, TextAnchor.UpperLeft);
        }

        private void SettingsBalanceSlider(RectTransform parent, string key, string titleKey, float y)
        {
            string prefKey = "xskills_balance_cap_" + key;
            float current = Mathf.Clamp(PlayerPrefs.GetFloat(prefKey, 50f), 0f, 200f);
            UIFactory.Label(parent, key + " label", Localizer.T(titleKey),
                22, y, 570, 33, 22, UIFactory.Gold);
            Text valueText = UIFactory.Label(parent, key + " percent", "",
                525, y + 45, 85, 30, 20, UIFactory.White, TextAnchor.MiddleRight);
            Slider slider = SettingsSlider(parent, key + " slider", 22, y + 50, 480, 0f, 200f,
                current, value =>
                {
                    if (!Raft_Network.IsHost) return;
                    PlayerPrefs.SetFloat(prefKey, value);
                    PlayerPrefs.Save();
                    if (valueText != null) valueText.text = "+" + Mathf.RoundToInt(value) + "%";
                });
            valueText.text = "+" + Mathf.RoundToInt(slider.value) + "%";
        }

        private void BuildDietRulesPage(RectTransform parent)
        {
            if (!Raft_Network.IsHost) return;
            UIFactory.Label(parent, "Diet rules header", Localizer.T("settings-diet-title"),
                22, 24, 590, 44, 26, UIFactory.Gold);
            UIFactory.Label(parent, "Diet rules", Localizer.T("settings-diet-future"),
                22, 84, 585, 120, 19, UIFactory.White, TextAnchor.UpperLeft);

            SettingsDietSlider(parent, "maxbonus", "settings-diet-max-bonus", 215, 0f, 200f,
                DietSystem.MaximumBonusHP, value => DietSystem.SetMaximumBonusHP(value), value => Mathf.RoundToInt(value) + " HP");
            SettingsDietSlider(parent, "dailyloss", "settings-diet-daily-loss", 338, 0f, 100f,
                DietSystem.DailyLossPercent, value => DietSystem.SetDailyLossPercent(value), value => Mathf.RoundToInt(value) + "%");
            SettingsDietSlider(parent, "deathloss", "settings-diet-death-loss", 461, 0f, 100f,
                DietSystem.DeathLossPercent, value => DietSystem.SetDeathLossPercent(value), value => Mathf.RoundToInt(value) + "%");

            UIFactory.Label(parent, "Diet bonus example", Localizer.T("settings-diet-bonus-rule"),
                22, 590, 590, 130, 18, UIFactory.Grey, TextAnchor.UpperLeft);
        }

        private void SettingsDietSlider(RectTransform parent, string key, string titleKey, float y,
            float minimum, float maximum, float current, Action<float> changed, Func<float, string> format)
        {
            UIFactory.Label(parent, key + " label", Localizer.T(titleKey),
                22, y, 570, 33, 22, UIFactory.Gold);
            Text valueText = UIFactory.Label(parent, key + " value", "",
                525, y + 45, 85, 30, 20, UIFactory.White, TextAnchor.MiddleRight);
            Slider slider = SettingsSlider(parent, key + " slider", 22, y + 50, 480, minimum, maximum,
                current, value =>
                {
                    if (changed != null) changed(value);
                    if (valueText != null) valueText.text = format != null ? format(value) : Mathf.RoundToInt(value).ToString();
                });
            valueText.text = format != null ? format(slider.value) : Mathf.RoundToInt(slider.value).ToString();
        }

        private void BuildSkillSettings(RectTransform parent, string skillId)
        {
            if (skillId == "fishing") { BuildFishingIntegrationSettings(parent); return; }
            UIFactory.Label(parent, "Skill settings summary", Localizer.F("settings-skill-heading", Localizer.SkillName(skillId)),
                22, 26, 585, 55, 26, UIFactory.Gold);
            UIFactory.Label(parent, "Skill settings template", Localizer.T("settings-skill-future"),
                22, 96, 585, 220, 20, UIFactory.White, TextAnchor.UpperLeft);
        }

        private static Slider SettingsSlider(Transform parent, string id, float x, float y, float width,
            float minimum, float maximum, float initial, Action<float> changed, bool wholeNumbers = true)
        {
            const float height = 36f;
            const float sideInset = 11f;
            RectTransform root = UIFactory.Rect(parent, id, x, y, width, height);
            Image hitbox = root.gameObject.AddComponent<Image>();
            hitbox.color = new Color(.12f, .09f, .065f, .015f);
            hitbox.raycastTarget = true;

            Slider slider = root.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.wholeNumbers = wholeNumbers;

            float innerWidth = width - sideInset * 2f;
            UIFactory.Fill(root, "Track border", sideInset - 2f, 13f, innerWidth + 4f, 11f,
                UIFactory.Hex("6B5038"));
            UIFactory.Fill(root, "Track background", sideInset, 15f, innerWidth, 7f,
                UIFactory.Hex("241B16"));

            RectTransform fillArea = UIFactory.Rect(root, "Fill area", sideInset, 15f, innerWidth, 7f);
            Image fill = UIFactory.Fill(fillArea, "Fill", 0f, 0f, 0f, 7f,
                UIFactory.Gold);
            fill.raycastTarget = false;
            RectTransform fillRect = (RectTransform)fill.transform;
            // Заполняемая часть растягивается якорями Slider без прибавления ширины
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.pivot = new Vector2(0f, .5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = Vector2.zero;

            RectTransform handleArea = UIFactory.Rect(root, "Handle area", sideInset, 4f, innerWidth, 28f);
            Image handle = UIFactory.Fill(handleArea, "Handle", 0f, 0f, 16f, 28f,
                UIFactory.Hex("E5B467"));
            handle.raycastTarget = false;
            RectTransform handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = new Vector2(0f, .5f);
            handleRect.anchorMax = new Vector2(0f, .5f);
            handleRect.pivot = new Vector2(.5f, .5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(16f, 28f);

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.value = initial;
            slider.onValueChanged.AddListener(value => { if (changed != null) changed(value); });
            return slider;
        }

        private static void SettingsButton(Transform parent, string id, string title, float x, float y,
            float width, Action clicked)
        {
            UIFactory.Image(parent, id + " background", "elements", "button_idle", x, y, width, 52);
            UIFactory.Label(parent, id + " text", title, x + 8, y + 4, width - 16, 44,
                20, UIFactory.Gold, TextAnchor.MiddleCenter);
            UIFactory.Button(parent, id + " click", x, y, width, 52, clicked);
        }

        private void DisposeSettingsPage()
        {
            if (settingsRightOverlay != null)
            {
                settingsRightOverlay.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(settingsRightOverlay.gameObject);
                settingsRightOverlay = null;
            }
            if (descriptionViewport != null) descriptionViewport.gameObject.SetActive(true);
            settingsListViewport = null;
            settingsFrames.Clear();
            settingsSectionIds.Clear();
            pinnedSettingsSection = -1;
            shownSettingsSection = -1;
        }
    }
}
