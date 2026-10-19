using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using xSkills.Skills;

using xSkills.Services;
namespace xSkills.GUI
{
    internal sealed partial class SkillGraphicalUI
    {
        private readonly Dictionary<string, Image> tabIndicators = new Dictionary<string, Image>();
        private readonly Dictionary<string, Text> tabLabels = new Dictionary<string, Text>();
        private readonly Dictionary<string, Image> tabIcons = new Dictionary<string, Image>();
        private RectTransform sidePanel;
        private Image rightDivider;
        private Image requirementDivider;
        private Image tooltipTopDivider;
        private Image mouseLeftImage;
        private Image mouseRightImage;
        private ScrollRect descriptionScroll;
        private RectTransform descriptionViewport;
        private Image descriptionScrollHitbox;
        private Text sideTitle;
        private Text sideSkillLevel;
        private Text sideExperience;
        private Text sidePointsPrefix;
        private Text sidePointsValue;
        private Text sidePointsSuffix;
        private Image sidePointsGlow;
        private Image sideXpProgress;
        private Text sideUnlearn;
        private Text sideUnlearnValue;
        private Text tooltipTitle;
        private Text tooltipTier;
        private Text tooltipDescription;
        private Text tooltipRequirements;
        private Text helpLearn;
        private Text helpUnlearn;
        private Image homeTabImage;
        private Text homeTabFallback;
        private Text settingsTabFallback;
        private const float HomeContentWidth = 1768f;
        public int PreviewLevel { get { return selectedSkill == null ? 1 : selectedSkill.PreviewLevel; } }

        private void BuildWindow()
        {
            RectTransform canvasRect = (RectTransform)canvas.transform;
            RectTransform shade = UIFactory.Rect(canvasRect, "Dim background", 0, 0, 0, 0);
            shade.anchorMin = Vector2.zero;
            shade.anchorMax = Vector2.one;
            shade.pivot = new Vector2(.5f, .5f);
            shade.anchoredPosition = Vector2.zero;
            shade.sizeDelta = Vector2.zero;
            Image shadeImage = shade.gameObject.AddComponent<Image>();
            shadeImage.color = new Color(0, 0, 0, .60f);
            shadeImage.raycastTarget = true;

            window = UIFactory.Rect(canvasRect, "Skill tree", 0, 0, WindowW, WindowH);
            window.anchorMin = window.anchorMax = new Vector2(.5f, .5f);
            window.pivot = new Vector2(.5f, .5f);
            window.anchoredPosition = Vector2.zero;
            windowGroup = window.gameObject.AddComponent<CanvasGroup>();
            UIFactory.Image(window, "Background", "elements", "bg", 0, 0, WindowW, WindowH);
            topTabs = UIFactory.Rect(window, "Tabs", 16, 16, WindowW - 32, 64);
            // Содержимое поднято ближе к оригинальной позиции под вкладками
            content = UIFactory.Rect(window, "Body", 16, 78, WindowW - 32, WindowH - 98);
            BuildTabs();
            BuildSideInfo();
            abilitiesRoot = UIFactory.Rect(content, "Abilities", 0, 94, 1335, 810);
            rightInfo = UIFactory.Rect(content, "Ability tooltip", 1355, 27, 387, 865);
            BuildRightInfo();
            UIFactory.Image(window, "Main border", "elements", "frame", 0, 0, WindowW, WindowH, 60);
            BuildTransferPopup();
        }

        private void BuildTabs()
        {
            AddTopTab("_home", "", "meta_home", 0, 64);
            AddTopTab("_spec", "", "meta_spec", 64, 64);
            AddTopTab("_info", "", "meta_info", 128, 64);
            AddTopTab("_settings", "", "meta_settings", 192, 64);
            float skillTabWidth = (WindowW - 32f - 256f) / Mathf.Max(1, skills.Count);
            for (int i = 0; i < skills.Count; i++)
                AddTopTab(skills[i].Id, skills[i].Title, null, 256f + i * skillTabWidth, skillTabWidth);
            DrawActiveTabs();
        }

        private void AddTopTab(string key, string label, string icon, float x, float w)
        {
            Image line = UIFactory.Image(topTabs, key + " indicator", "elements", "tab_sep", x + w * .25f, 30, w * .5f, 4);
            tabIndicators[key] = line;
            if (!string.IsNullOrEmpty(icon))
            {
                Image tabIcon = UIFactory.Image(topTabs, key + " icon", "elements", icon, x + w / 2 - 12, 4, 24, 24);
                tabIcons[key] = tabIcon;
                if (key == "_home")
                {
                    homeTabImage = tabIcon;
                    if (!tabIcon.enabled)
                        homeTabFallback = UIFactory.Label(topTabs, "Home icon fallback", Localizer.T("ui-home-tab-letter"), x + 6, 0, w - 12, 32, 22,
                            UIFactory.Gold, TextAnchor.MiddleCenter);
                }
                if (key == "_settings" && !tabIcon.enabled)
                    settingsTabFallback = UIFactory.Label(topTabs, "Settings icon fallback", "S", x + 6, 0, w - 12, 32, 22,
                        UIFactory.Gold, TextAnchor.MiddleCenter);
            }
            else
                tabLabels[key] = UIFactory.Label(topTabs, key + " label", Localizer.SkillName(key, label), x + 3, 0, w - 6, 32, 20, UIFactory.Gold, TextAnchor.MiddleCenter);
            UIFactory.Button(topTabs, key + " button", x, 0, w, 35,
                () => { if (key == "_info" || key == "_spec" || key == "_home" || key == "_settings") SelectPage(key); else SelectSkill(FindSkill(key)); },
                () => { if (page != key) line.sprite = ResourceLoader.Sprite("elements", "tab_sep_hover"); },
                () => { DrawActiveTabs(); });
        }

        private SkillData FindSkill(string id)
        {
            foreach (SkillData skill in skills) if (skill.Id == id) return skill;
            return skills[0];
        }

        private void DrawActiveTabs()
        {
            foreach (KeyValuePair<string, Image> item in tabIndicators)
                item.Value.sprite = ResourceLoader.Sprite("elements", item.Key == page ? "tab_sep_selected" : "tab_sep");
            foreach (KeyValuePair<string, Image> item in tabIcons)
            {
                if (item.Key == "_home" || item.Key == "_settings")
                {
                    item.Value.color = page == item.Key ? UIFactory.Gold : UIFactory.White;
                    continue;
                }
                item.Value.sprite = ResourceLoader.Sprite("elements", item.Key == "_spec"
                    ? (page == "_spec" ? "meta_spec_selected" : "meta_spec")
                    : (page == "_info" ? "meta_info_selected" : "meta_info"));
            }
        }

        private void RefreshLanguage()
        {
            foreach (var tab in tabLabels)
                tab.Value.text = Localizer.SkillName(tab.Key, FindSkill(tab.Key).Title);
            if (page == "_info" || page == "_spec" || page == "_home" || page == "_settings") SelectPage(page);
            else SelectSkill(selectedSkill);
            foreach (AbilityButton button in visibleButtons) button.RefreshLanguage();
        }

        private void SelectPage(string id)
        {
            if (isOpen && page != id) SoundPlayer.Play("page");
            CloseTransfer(false);
            page = id;
            selectedAbility = null;
            DrawActiveTabs();
            sidePanel.gameObject.SetActive(false);
            UIFactory.RemoveChildren(abilitiesRoot);
            visibleButtons.Clear();
            UnpinInfoSection();
            DisposePlayerHome();
            DisposeSettingsPage();
            SetTooltipMode(true);
            bool home = id == "_home";
            abilitiesRoot.sizeDelta = new Vector2(home ? HomeContentWidth : 1335f, home ? 962f : 810f);
            float homeOffsetX = home ? (content.rect.width - HomeContentWidth) * .5f : 0f;
            abilitiesRoot.anchoredPosition = new Vector2(homeOffsetX, home ? 0f : -94f);
            rightInfo.gameObject.SetActive(!home);
            rightDivider.gameObject.SetActive(!home);
            if (id == "_info") BuildCustomInfoPage();
            else if (id == "_settings") BuildSettingsPage();
            else if (id == "_home") BuildPlayerHomePage();
            else BuildSpecializationsPage();
        }

        private void SelectSkill(SkillData skill)
        {
            if (skill == null) return;
            if (isOpen && page != skill.Id) SoundPlayer.Play("page");
            CloseTransfer(false);
            UnpinInfoSection();
            DisposePlayerHome();
            DisposeSettingsPage();
            selectedSkill = skill;
            page = skill.Id;
            DrawActiveTabs();
            sidePanel.gameObject.SetActive(true);
            abilitiesRoot.sizeDelta = new Vector2(1335f, 810f);
            abilitiesRoot.anchoredPosition = new Vector2(0f, -94f);
            rightInfo.gameObject.SetActive(true);
            rightDivider.gameObject.SetActive(true);
            SetTooltipMode(false);
            sideTitle.text = Localizer.SkillName(skill.Id);
            sideSkillLevel.text = Localizer.F("ui-level", skill.PreviewLevel);
            sideExperience.text = Localizer.F("ui-experience",
                Mathf.FloorToInt(skill.Experience), Mathf.CeilToInt(skill.RequiredExperience));
            UpdatePointsLine(skill);
            if (sideXpProgress != null)
            {
                float ratio = skill.RequiredExperience > 0 ? skill.Experience / skill.RequiredExperience : 1f;
                ((RectTransform)sideXpProgress.transform).sizeDelta = new Vector2(205f * Mathf.Clamp01(ratio), 6f);
            }
            sideUnlearn.text = Localizer.T("unlearnPoints");
            sideUnlearnValue.text = skill.PreviewUnlearnPoints.ToString();
            BuildAbilities(skill);
            Hover(null);
        }

        private void BuildSideInfo()
        {
            const float left = 12f;
            sidePanel = UIFactory.Rect(content, "Skill stats", 0, 0, 325, 215);
            sideTitle = UIFactory.Label(sidePanel, "Skill title", "", left, 12, 310, 40, 28, UIFactory.Gold);
            sideSkillLevel = UIFactory.Label(sidePanel, "Level", "", left, 53, 85, 26, 20, UIFactory.White);
            sideExperience = UIFactory.Label(sidePanel, "Experience", "", left + 130, 53, 160, 26, 19, UIFactory.Grey);
            UIFactory.Image(sidePanel, "XP background", "elements", "abilitybox_progerss_bg", left, 84, 205, 6, 2);
            sideXpProgress = UIFactory.Image(sidePanel, "XP progress", "elements", "abilitybox_progerss_content", left, 84, 0, 6, 2);
            sidePointsGlow = UIFactory.Image(sidePanel, "Available glow", "elements", "glow", left - 8, 104, 48, 28, 10);
            sidePointsGlow.color = new Color(UIFactory.Green.r, UIFactory.Green.g, UIFactory.Green.b, .3f);
            sidePointsPrefix = UIFactory.Label(sidePanel, "Available prefix", "", left, 105, 300, 25, 19, UIFactory.White);
            sidePointsValue = UIFactory.Label(sidePanel, "Available value", "", left, 105, 120, 25, 19, UIFactory.Green);
            sidePointsSuffix = UIFactory.Label(sidePanel, "Available suffix", "", left, 105, 300, 25, 19, UIFactory.White);
            sideUnlearn = UIFactory.Label(sidePanel, "Unlearn points", "", left, 135, 208, 25, 19, UIFactory.Hex("BF663F"));
            sideUnlearnValue = UIFactory.Label(sidePanel, "Unlearn value", "", left + 185, 135, 27, 25, 19, UIFactory.Hex("BF663F"), TextAnchor.MiddleRight);
            UIFactory.Image(sidePanel, "Unlearn progress", "elements", "abilitybox_progerss_bg", left, 163, 205, 4, 2);
            rightDivider = UIFactory.Image(content, "Separating line", "elements", "tooltip_sep_v", 1340, 23, 2, 858);
        }

        private static float Measure(Text label, string value)
        {
            // Text.preferredWidth используется и для русского системного шрифта
            // Для запечённого шрифта выполняется расчёт по таблице символов
            if (Localizer.UseBitmapFont)
            {
                BitmapGlyph[] glyphs = label.fontSize <= 24 ? GlyphTable.Small : GlyphTable.Large;
                float scale = label.fontSize / (label.fontSize <= 24 ? 20f : 27f);
                float result = 0;
                foreach (char c in value)
                {
                    if (c == ' ') result += 5 * scale;
                    else if (c >= 32 && c <= 126) result += (glyphs[c - 32].W + 1) * scale;
                }
                return result;
            }
            string before = label.text;
            label.text = value;
            float width = label.preferredWidth;
            label.text = before;
            return width;
        }

        private void UpdatePointsLine(SkillData skill)
        {
            string template = Localizer.T("pointsAvailable");
            int placeholder = template.IndexOf("{0}", StringComparison.Ordinal);
            if (placeholder < 0) template = "{0} " + template;
            placeholder = template.IndexOf("{0}", StringComparison.Ordinal);
            string prefix = template.Substring(0, placeholder);
            string number = skill.PreviewPoints.ToString();
            string suffix = template.Substring(placeholder + 3);
            const float x = 12f;
            sidePointsPrefix.text = prefix;
            sidePointsValue.text = number;
            sidePointsSuffix.text = suffix;
            float numberX = x + Measure(sidePointsPrefix, prefix);
            float suffixX = numberX + Measure(sidePointsValue, number) + 2;
            ((RectTransform)sidePointsValue.transform).anchoredPosition = new Vector2(numberX, -105);
            ((RectTransform)sidePointsSuffix.transform).anchoredPosition = new Vector2(suffixX, -105);
            ((RectTransform)sidePointsGlow.transform).anchoredPosition = new Vector2(numberX - 12, -103);
            ((RectTransform)sidePointsGlow.transform).sizeDelta = new Vector2(Measure(sidePointsValue, number) + 24, 29);
            sidePointsGlow.enabled = skill.PreviewPoints > 0 && sidePointsGlow.sprite != null;
            sidePointsPrefix.gameObject.SetActive(prefix.Length > 0);
            // В русском цифра выделяется зелёным, как и в английском оригинале
            sidePointsValue.color = UIFactory.Green;
        }

        private void BuildAbilities(SkillData skill)
        {
            UIFactory.RemoveChildren(abilitiesRoot);
            visibleButtons.Clear();
            List<int> levels = new List<int>();
            foreach (AbilityData a in skill.Abilities)
                if (!levels.Contains(a.RequiredLevel)) levels.Add(a.RequiredLevel);
            levels.Sort();
            int rowCount = levels.Count;
            const float w = 128f;
            const float h = 100f;
            const float pad = 16f;
            const int cols = 6;
            float usedHeight = rowCount * h + Math.Max(0, rowCount - 1) * pad;
            float top = Math.Max(6, (810f - usedHeight) / 2f);
            float gridLeft = (1335f - (cols * w + (cols - 1) * pad)) * .5f;

            // Разделители рисуются первыми, чтобы их не было поверх иконок
            for (int row = 0; row < rowCount - 1; row++)
            {
                int required = levels[rowCount - 1 - row];
                float dividerBottom = top + row * (h + pad) + h + pad * .5f;
                UIFactory.Image(abilitiesRoot, "Divider " + required, "elements", "level_sep",
                    100, dividerBottom - 64, 1095, 64);
                UIFactory.Label(abilitiesRoot, "Level number " + required,
                    Localizer.F("ui-level-line", required),
                    112, dividerBottom - 26, 160, 25, 18,
                    required <= skill.PreviewLevel ? UIFactory.Gold : UIFactory.Grey);
            }
            for (int row = 0; row < rowCount; row++)
            {
                int required = levels[rowCount - 1 - row];
                List<AbilityData> group = skill.Abilities.FindAll(a => a.RequiredLevel == required);
                float y = top + row * (h + pad);
                // Оригинальная сетка - шесть колонок с дискретным выравниванием
                int startColumn = Mathf.Max(0, (cols - group.Count) / 2);
                for (int column = 0; column < group.Count; column++)
                {
                    AbilityData ability = group[column];
                    GameObject go = new GameObject("Ability " + ability.Id, typeof(RectTransform), typeof(AbilityButton));
                    AbilityButton component = go.GetComponent<AbilityButton>();
                    component.Setup(this, ability, abilitiesRoot, gridLeft + (startColumn + column) * (w + pad), y, w, h);
                    visibleButtons.Add(component);
                }
            }
        }

        private void BuildRightInfo()
        {
            tooltipTitle = UIFactory.Label(rightInfo, "Title", "", 8, 0, 270, 37, 28, UIFactory.Gold);
            tooltipTier = UIFactory.Label(rightInfo, "Tier", "", 272, 3, 100, 32, 19, UIFactory.White, TextAnchor.MiddleRight);
            tooltipTopDivider = UIFactory.Image(rightInfo, "Line", "elements", "tooltip_sep", 4, 41, 366, 4);
            descriptionViewport = UIFactory.Rect(rightInfo, "Description viewport", 9, 55, 354, 195);
            descriptionViewport.gameObject.AddComponent<RectMask2D>();
            descriptionScroll = descriptionViewport.gameObject.AddComponent<ScrollRect>();
            descriptionScroll.horizontal = false;
            descriptionScroll.vertical = true;
            descriptionScroll.movementType = ScrollRect.MovementType.Clamped;
            descriptionScroll.scrollSensitivity = 55f;
            descriptionScroll.inertia = false;
            descriptionScrollHitbox = UIFactory.Fill(descriptionViewport, "Scroll hitbox", 0, 0, 354, 195,
                new Color(1f, 1f, 1f, .001f));
            descriptionScrollHitbox.raycastTarget = true;
            tooltipDescription = UIFactory.Label(descriptionViewport, "Description", "", 0, 0, 354, 195, 19, UIFactory.White, TextAnchor.UpperLeft);
            tooltipDescription.verticalOverflow = VerticalWrapMode.Overflow;
            tooltipDescription.supportRichText = true;
            descriptionScroll.content = (RectTransform)tooltipDescription.transform;
            descriptionScroll.viewport = descriptionViewport;
            requirementDivider = UIFactory.Image(rightInfo, "Requirements line", "elements", "tooltip_sep", 4, 260, 366, 3);
            tooltipRequirements = UIFactory.Label(rightInfo, "Requirements", "", 9, 270, 352, 350, 19, UIFactory.Green, TextAnchor.UpperLeft);
            // Подсказка располагается у нижней границы окна, а не внутри боковой панели
            mouseLeftImage = UIFactory.Image(window, "Mouse left", "elements", "mouse_left", 0, 987, 64, 32);
            helpLearn = UIFactory.Label(window, "Learn title", "", 0, 1012, 130, 28, 19, UIFactory.Grey);
            mouseRightImage = UIFactory.Image(window, "Mouse right", "elements", "mouse_right", 0, 987, 64, 32);
            helpUnlearn = UIFactory.Label(window, "Unlearn title", "", 0, 1012, 160, 28, 19, UIFactory.Grey);
        }

        private void UpdateActionHints()
        {
            // Оригинальные подсказки стоят рядом - иконка над соответствующей надписью
            helpLearn.text = Localizer.T("actionLearn");
            helpUnlearn.text = Localizer.T("actionUnlearn");
            float rightEdge = WindowW - 32f;
            float unlearnWidth = Mathf.Max(72f, Measure(helpUnlearn, helpUnlearn.text) + 6f);
            float unlearnX = rightEdge - unlearnWidth;
            ((RectTransform)helpUnlearn.transform).anchoredPosition = new Vector2(unlearnX, -1012f);
            ((RectTransform)helpUnlearn.transform).sizeDelta = new Vector2(unlearnWidth, 28f);
            ((RectTransform)mouseRightImage.transform).anchoredPosition =
                new Vector2(unlearnX + (unlearnWidth - 64f) * .5f, -981f);

            float learnRight = unlearnX - 16f;
            float learnWidth = Mathf.Max(64f, Measure(helpLearn, helpLearn.text) + 6f);
            float learnX = learnRight - learnWidth;
            ((RectTransform)helpLearn.transform).anchoredPosition = new Vector2(learnX, -1012f);
            ((RectTransform)helpLearn.transform).sizeDelta = new Vector2(learnWidth, 28f);
            ((RectTransform)mouseLeftImage.transform).anchoredPosition =
                new Vector2(learnX + (learnWidth - 64f) * .5f, -981f);
        }

        private void SetTooltipMode(bool meta)
        {
            ((RectTransform)rightInfo).anchoredPosition = new Vector2(meta ? 1075f : 1355f, -27f);
            rightInfo.sizeDelta = new Vector2(meta ? 665f : 387f, 865f);
            ((RectTransform)rightDivider.transform).anchoredPosition = new Vector2(meta ? 1060f : 1340f, -23f);
            ((RectTransform)tooltipTitle.transform).sizeDelta = new Vector2(meta ? 585 : 270, 37);
            ((RectTransform)tooltipTier.transform).anchoredPosition = new Vector2(meta ? 565 : 272, -3);
            ((RectTransform)tooltipTopDivider.transform).sizeDelta = new Vector2(meta ? 627 : 366, 4);
            ((RectTransform)descriptionViewport).sizeDelta = new Vector2(meta ? 627 : 354, meta ? 738 : 195);
            if (descriptionScrollHitbox != null)
                ((RectTransform)descriptionScrollHitbox.transform).sizeDelta =
                    new Vector2(meta ? 627 : 354, meta ? 738 : 195);
            ((RectTransform)tooltipDescription.transform).sizeDelta = new Vector2(meta ? 627 : 354, 195);
            mouseLeftImage.gameObject.SetActive(!meta);
            mouseRightImage.gameObject.SetActive(!meta);
            helpLearn.gameObject.SetActive(!meta);
            helpUnlearn.gameObject.SetActive(!meta);
            tooltipRequirements.gameObject.SetActive(!meta);
            requirementDivider.gameObject.SetActive(!meta);
        }

        private void SetRightPanel(string title, string body, string tier = "")
        {
            tooltipTitle.text = title;
            tooltipTier.text = tier;
            tooltipDescription.text = Localizer.FormatText(body);
            if (descriptionScroll != null && descriptionViewport != null)
            {
                RectTransform textRect = (RectTransform)tooltipDescription.transform;
                float width = descriptionViewport.rect.width;
                if (width <= 0f) width = descriptionViewport.sizeDelta.x;
                textRect.sizeDelta = new Vector2(width, descriptionViewport.rect.height);
                Canvas.ForceUpdateCanvases();
                float preferredHeight = tooltipDescription.preferredHeight;
                float height = Mathf.Max(descriptionViewport.rect.height,
                    Mathf.Ceil(preferredHeight) + 12f);
                textRect.sizeDelta = new Vector2(width, height);
                descriptionScroll.StopMovement();
                descriptionScroll.verticalNormalizedPosition = 1f;
            }
        }

        private void UpdateRightInfo()
        {
            if (tooltipTitle == null || selectedSkill == null || page == "_info" || page == "_spec" || page == "_home" || page == "_settings") return;
            UpdateActionHints();
            if (selectedAbility == null)
            {
                SetRightPanel(Localizer.SkillName(selectedSkill.Id), Localizer.T("ui-hover-ability"));
                tooltipRequirements.text = "";
            }
            else
            {
                SetRightPanel(Localizer.AbilityName(selectedAbility.Id),
                    Localizer.AbilityDescription(selectedAbility.Id),
                    Localizer.F("ui-tier", selectedAbility.PreviewTier, selectedAbility.MaxTier));
                tooltipRequirements.text = selectedAbility.RequiredLevel > selectedSkill.PreviewLevel
                    ? Localizer.F("ui-required-level", selectedAbility.RequiredLevel) : "";
            }
        }
    }
}
