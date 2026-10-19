using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using xSkills.Skills;

using xSkills.Services;
namespace xSkills.GUI
{
    internal sealed class AbilityButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private SkillGraphicalUI owner;
        private AbilityData data;
        private Image frame;
        private Image icon;
        private Image glow;
        private Image selection;
        private Image progress;
        private Text nameLabel;
        private float fullWidth;
        private bool hovered;

        public void Setup(SkillGraphicalUI ui, AbilityData ability, RectTransform parent, float x, float y, float width, float height)
        {
            owner = ui;
            data = ability;
            fullWidth = width;
            RectTransform holder = (RectTransform)transform;
            holder.SetParent(parent, false);
            holder.anchorMin = holder.anchorMax = new Vector2(0, 1);
            holder.pivot = new Vector2(0, 1);
            holder.anchoredPosition = new Vector2(x, -y);
            holder.sizeDelta = new Vector2(width, height);

            glow = UIFactory.Image(holder, "Glow", "elements", "ability_glow", -64, -80, width + 128, height + 160);
            glow.color = new Color(1, .69f, .2f, 0);
            UIFactory.Image(holder, "Background", "elements", "abilitybox_bg", 0, 0, width, height);
            icon = UIFactory.Image(holder, "Ability icon", "abilityicon", data.Id, 0, 0, width, height);
            UIFactory.Image(holder, "Shadow", "elements", "ability_shadow", 0, 0, width, height, 30);
            UIFactory.Image(holder, "Name backing", "elements", "abilitybox_name_under", 0, height - 48, width, 48);
            nameLabel = UIFactory.Label(holder, "Name", Localizer.AbilityName(data.Id, data.Title), 3, height - 51, width - 6, 45, 18, UIFactory.Grey, TextAnchor.MiddleCenter);
            // Однострочные подписи опускаются, двустрочные сохраняют исходное положение
            AdjustNamePosition();
            UIFactory.Image(holder, "Progress background", "elements", "abilitybox_progerss_bg", 0, height - 8, width, 6, 2);
            progress = UIFactory.Image(holder, "Progress", "elements", "abilitybox_progerss_content", 0, height - 8, 0, 6, 2);
            for (int i = 1; i < data.MaxTier; i++)
                UIFactory.Image(holder, "Progress tier divider", "elements", "abilitybox_progerss_overlay", (width / data.MaxTier) * (i - 1), height - 8, width / data.MaxTier + 1, 6, 2);
            frame = UIFactory.Image(holder, "Frame", "elements", "abilitybox_frame_inactive", 0, 0, width, height, 15);
            selection = UIFactory.Image(holder, "Selected", "elements", "abilitybox_frame_selected", -16, -16, width + 32, height + 32, 30);
            selection.enabled = false;
            Image clickSurface = UIFactory.Fill(holder, "Mouse hitbox", 0, 0, width, height, new Color(1f, 1f, 1f, .001f));
            clickSurface.raycastTarget = true;
            Refresh();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            owner.Hover(data);
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            owner.Hover(null);
            Refresh();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            int before = data.PreviewTier;
            int target = before;
            if (eventData.button == PointerEventData.InputButton.Left) target++;
            else if (eventData.button == PointerEventData.InputButton.Right) target--;
            else return;
            bool changed = owner.ChangeAbilityTier(data, target);
            if (changed)
            {
                SoundPlayer.Play(target < before ? "downgraded" :
                    target == data.MaxTier ? "upgradedmax" : "upgraded");
                data.Glow = 1f;
                owner.Hover(data);
            }
            Refresh();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            data.VisualTier = Mathf.Lerp(data.VisualTier, data.PreviewTier, 1f - Mathf.Pow(.15f, dt * 10f));
            data.Glow = Mathf.MoveTowards(data.Glow, 0, dt * 1.2f);
            RectTransform progressRect = (RectTransform)progress.transform;
            progressRect.sizeDelta = new Vector2(Mathf.Max(0, fullWidth * data.VisualTier / data.MaxTier), 6);
            glow.color = new Color(data.PreviewTier == data.MaxTier ? 1f : .48f, data.PreviewTier == data.MaxTier ? .68f : .85f, .2f, data.Glow);
        }

        private void Refresh()
        {
            bool max = data.PreviewTier >= data.MaxTier;
            bool learnable = data.RequiredLevel <= owner.PreviewLevel && !data.PreviewLocked;
            frame.sprite = ResourceLoader.Sprite("elements", max ? "abilitybox_frame_max" : learnable ? "abilitybox_frame_active" : data.PreviewTier > 0 ? "abilitybox_frame_idle" : "abilitybox_frame_inactive", 15);
            icon.color = data.PreviewTier > 0 || learnable || hovered ? Color.white : new Color(1, 1, 1, .28f);
            selection.enabled = hovered;
            nameLabel.color = max ? UIFactory.Gold : learnable ? UIFactory.Green : data.PreviewTier > 0 ? UIFactory.White : UIFactory.Grey;
        }

        public void RefreshLanguage()
        {
            if (nameLabel != null)
            {
                nameLabel.text = Localizer.AbilityName(data.Id, data.Title);
                AdjustNamePosition();
            }
            Refresh();
        }

        private void AdjustNamePosition()
        {
            if (nameLabel == null) return;
            string text = nameLabel.text ?? "";
            bool multiline = text.Contains("\n");
            if (!multiline)
            {
                // Перенос без явного перевода строки определяется шириной текста
                nameLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
                multiline = nameLabel.preferredWidth > fullWidth - 12f;
            }
            RectTransform rect = (RectTransform)nameLabel.transform;
            rect.anchoredPosition = new Vector2(3f, -(multiline ? 49f : 56f));
            rect.sizeDelta = new Vector2(fullWidth - 6f, 45f);
        }

        public void ResetHover()
        {
            hovered = false;
            Refresh();
        }
    }
}
