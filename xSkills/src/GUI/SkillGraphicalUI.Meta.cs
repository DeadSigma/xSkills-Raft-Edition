using UnityEngine;
using UnityEngine.UI;
using xSkills.Skills;

using xSkills.Services;
namespace xSkills.GUI
{
    internal sealed partial class SkillGraphicalUI
    {
        private int pinnedInfoSection = -1;
        private int shownInfoSection = -1;
        private readonly Image[] infoFrames = new Image[7];

        private void UnpinInfoSection()
        {
            pinnedInfoSection = -1;
            shownInfoSection = -1;
            for (int i = 0; i < infoFrames.Length; i++) infoFrames[i] = null;
        }

        private void PinInfoSection(int section)
        {
            pinnedInfoSection = pinnedInfoSection == section ? -1 : section;
            if (pinnedInfoSection > 0) ShowInfoSection(pinnedInfoSection, true);
            else ShowInfoIntro(true);
            RefreshInfoFrames();
        }

        private void RefreshInfoFrames()
        {
            for (int i = 0; i < infoFrames.Length; i++)
                if (infoFrames[i] != null)
                    infoFrames[i].sprite = ResourceLoader.Sprite("elements",
                        pinnedInfoSection == i + 1 || shownInfoSection == i + 1
                            ? "abilitybox_frame_active" : "abilitybox_frame_inactive", 15);
        }

        private void BuildCustomInfoPage()
        {
            UIFactory.Label(abilitiesRoot, "Information heading", Localizer.T("custominfo-left-title"),
                35, 12, 980, 54, 36, UIFactory.Gold);
            UIFactory.Label(abilitiesRoot, "Information overview", Localizer.FormatText(Localizer.T("custominfo-left-desc")),
                35, 67, 985, 65, 22, UIFactory.White, TextAnchor.UpperLeft);

            UIFactory.Label(abilitiesRoot, "Info pin hint", Localizer.T("ui-info-pin-hint"),
                35, 132, 950, 25, 17, UIFactory.Grey);

            const float viewportHeight = 635f;
            const float rowStep = 133f;
            RectTransform viewport = UIFactory.Rect(abilitiesRoot, "Information list viewport", 35, 164, 950, viewportHeight);
            viewport.gameObject.AddComponent<RectMask2D>();
            Image hitbox = viewport.gameObject.AddComponent<Image>();
            hitbox.color = new Color(0f, 0f, 0f, .001f);
            hitbox.raycastTarget = true;
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 75f;
            RectTransform list = UIFactory.Rect(viewport, "Information rows", 0, 0, 950, 7f * rowStep);
            scroll.content = list;
            scroll.viewport = viewport;

            for (int i = 1; i <= 7; i++)
            {
                int section = i;
                float y = (i - 1) * rowStep;
                UIFactory.Image(list, "Info " + i + " bg", "elements",
                    "abilitybox_bg_info", 0, y, 950, 116);
                UIFactory.Image(list, "Info " + i + " shadow", "elements",
                    "ability_shadow", 0, y, 950, 116, 30);
                Image frame = UIFactory.Image(list, "Info " + i + " frame", "elements",
                    "abilitybox_frame_inactive", 0, y, 950, 116, 15);
                infoFrames[i - 1] = frame;
                UIFactory.Label(list, "Info " + i + " title", Localizer.T("custominfo-title-" + i),
                    20, y + 13, 915, 43, 29, UIFactory.Gold);
                UIFactory.Label(list, "Info " + i + " summary", Localizer.T("custominfo-shortdesc-" + i),
                    20, y + 57, 915, 43, 19, UIFactory.White);
                UIFactory.Button(list, "Info " + i + " hover", 0, y, 950, 116,
                    () => PinInfoSection(section),
                    () => { if (pinnedInfoSection < 0) ShowInfoSection(section); RefreshInfoFrames(); },
                    () => { if (pinnedInfoSection < 0) ShowInfoIntro(); RefreshInfoFrames(); });
            }
            scroll.verticalNormalizedPosition = 1f;
            ShowInfoIntro(true);
        }

        private void ShowInfoIntro(bool force = false)
        {
            if (pinnedInfoSection > 0 && !force) return;
            if (!force && shownInfoSection == -1) return;
            shownInfoSection = -1;
            SetRightPanel(Localizer.T("custominfo-left-title"), Localizer.T("custominfo-left-desc"));
        }

        private void ShowInfoSection(int index, bool force = false)
        {
            if (page != "_info" || (pinnedInfoSection > 0 && !force)) return;
            if (!force && shownInfoSection == index) return;
            shownInfoSection = index;
            string detail = Localizer.T("custominfo-details-" + index);
            if (index == 7)
                detail = Localizer.T("custominfo-commands");
            SetRightPanel(Localizer.T("custominfo-title-" + index), detail);
        }

        private void BuildSpecializationsPage()
        {
            UIFactory.Label(abilitiesRoot, "Spec heading", Localizer.T("specialisations"),
                35, 12, 940, 54, 36, UIFactory.Gold);
            UIFactory.Label(abilitiesRoot, "Spec overview", Localizer.T("ui-spec-hint"),
                35, 67, 930, 55, 20, UIFactory.White, TextAnchor.UpperLeft);

            // Список формируется из зарегистрированных веток, а не из последней открытой
            for (int i = 0; i < skills.Count; i++)
            {
                SkillData skill = skills[i];
                float y = 142 + i * 134;
                Image rowFrame = UIFactory.Image(abilitiesRoot, "Spec frame " + skill.Id, "elements",
                    "abilitybox_frame_inactive", 35, y, 940, 115, 15);
                UIFactory.Image(abilitiesRoot, "Spec icon " + skill.Id, "skillicon", skill.Id,
                    50, y + 15, 79, 79);
                UIFactory.Label(abilitiesRoot, "Spec name " + skill.Id,
                    Localizer.SkillName(skill.Id), 155, y + 18, 570, 42, 29, UIFactory.Gold);
                UIFactory.Label(abilitiesRoot, "Spec level " + skill.Id,
                    Localizer.F("ui-level", skill.PreviewLevel), 155, y + 65, 400, 35, 21, UIFactory.White);
                UIFactory.Button(abilitiesRoot, "Spec transfer " + skill.Id, 35, y, 940, 115,
                    () => OpenTransfer(skill),
                    () =>
                    {
                        rowFrame.sprite = ResourceLoader.Sprite("elements", "abilitybox_frame_active", 15);
                        ShowSpecSkill(skill);
                    },
                    () =>
                    {
                        rowFrame.sprite = ResourceLoader.Sprite("elements", "abilitybox_frame_inactive", 15);
                        ShowSpecIntro();
                    });
            }
            ShowSpecIntro();
        }

        private void ShowSpecIntro()
        {
            SetRightPanel(Localizer.T("specialisations"), Localizer.T("ui-spec-hint"));
        }

        private void ShowSpecSkill(SkillData skill)
        {
            if (page != "_spec" || transferOpen) return;
            SetRightPanel(Localizer.SkillName(skill.Id),
                Localizer.F("ui-spec-details", skill.PreviewLevel, skill.PreviewPoints));
        }
    }
}
