using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using xSkills.Skills;

using xSkills.Services;
namespace xSkills.GUI
{
    // HUD работает и при закрытом меню навыков
    internal sealed class SkillNotifications : MonoBehaviour
    {
        private sealed class Drop
        {
            public string SkillId;
            public float Amount;
            public float Age;
            public RectTransform Rect;
            public CanvasGroup Group;
            public Text AmountText;
            public Image Progress;
        }

        private readonly List<Drop> drops = new List<Drop>();
        private RectTransform hud;
        private RectTransform levelPopup;
        private CanvasGroup levelGroup;
        private Text levelTitle;
        private Text levelHint;
        private Image levelIcon;
        private Image levelSeparator;
        private float levelAge = 100f;
        private const float DropLifetime = 3.5f;
        private const float PopupLifetime = 4f;
        private const int MaxDrops = 5;

        public static SkillNotifications Create()
        {
            GameObject root = new GameObject("xSkills Notifications", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(root);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31010;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            SkillNotifications notifications = root.AddComponent<SkillNotifications>();
            notifications.Construct();
            return notifications;
        }

        private void Construct()
        {
            hud = UIFactory.Rect(transform, "XP notifications", 0f, 0f, 1f, 1f);
            hud.anchorMin = hud.anchorMax = new Vector2(1f, 1f);
            hud.pivot = new Vector2(1f, 1f);
            hud.anchoredPosition = Vector2.zero;
            levelPopup = UIFactory.Rect(transform, "Level popup", 0f, 0f, 560f, 160f);
            levelPopup.anchorMin = levelPopup.anchorMax = new Vector2(.5f, 1f);
            levelPopup.pivot = new Vector2(.5f, 1f);
            levelPopup.anchoredPosition = new Vector2(0f, -24f);
            levelGroup = levelPopup.gameObject.AddComponent<CanvasGroup>();
            levelGroup.blocksRaycasts = false;
            Image glow = UIFactory.Image(levelPopup, "Level glow", "elements", "level_up_glow",
                0, 0, 560, 160, 0f, UIFactory.Brown);
            glow.raycastTarget = false;
            levelSeparator = UIFactory.Image(levelPopup, "Level separator", "elements", "level_sep",
                40, 32, 480, 64, 0f, UIFactory.White);
            levelSeparator.raycastTarget = false;
            Image iconGlow = UIFactory.Image(levelPopup, "Icon glow", "elements", "level_up_glow",
                240, 61, 80, 80, 0f, UIFactory.Brown);
            iconGlow.raycastTarget = false;
            levelIcon = UIFactory.Image(levelPopup, "Skill icon", "skillicon", "survival",
                264, 85, 32, 32, 0f, UIFactory.Gold);
            levelTitle = UIFactory.Label(levelPopup, "Level up", "", 0, 41, 560, 30, 17,
                UIFactory.Gold, TextAnchor.MiddleCenter);
            levelHint = UIFactory.Label(levelPopup, "Open skills hint", "", 0, 116, 560, 28, 15,
                UIFactory.White, TextAnchor.MiddleCenter);
            levelPopup.gameObject.SetActive(false);
            SkillCatalog.ExperienceGained += OnExperience;
            SkillCatalog.LevelGained += OnLevel;
        }

        private void OnExperience(SkillData skill, float amount)
        {
            if (skill == null || amount <= 0f || !isActiveAndEnabled) return;
            // Близкие начисления одного навыка складываются
            foreach (Drop existing in drops)
            {
                if (existing.SkillId == skill.Id && existing.Age < .8f)
                {
                    existing.Amount += amount;
                    existing.Age = 0f;
                    UpdateDropText(existing);
                    return;
                }
            }

            Drop drop = new Drop { SkillId = skill.Id, Amount = amount };
            drop.Rect = UIFactory.Rect(hud, "XP " + skill.Id, 0, 0, 306, 65);
            drop.Rect.anchorMin = drop.Rect.anchorMax = new Vector2(1f, 1f);
            drop.Rect.pivot = new Vector2(1f, 1f);
            drop.Rect.anchoredPosition = new Vector2(-34f, -92f);
            drop.Group = drop.Rect.gameObject.AddComponent<CanvasGroup>();
            drop.Group.blocksRaycasts = false;
            Image bg = UIFactory.Fill(drop.Rect, "Background", 0, 0, 306, 65,
                new Color(.13f, .09f, .08f, .88f));
            bg.raycastTarget = false;
            UIFactory.Image(drop.Rect, "Skill icon", "skillicon", skill.Id, 8, 9, 48, 48);
            UIFactory.Label(drop.Rect, "Skill name", Localizer.SkillName(skill.Id),
                65, 5, 224, 24, 18, UIFactory.Gold);
            drop.AmountText = UIFactory.Label(drop.Rect, "Experience gained", "",
                65, 28, 224, 24, 21, UIFactory.Green);
            Image barBg = UIFactory.Fill(drop.Rect, "Bar background", 65, 56, 224, 4,
                new Color(.1f, .07f, .06f, .95f));
            barBg.raycastTarget = false;
            float fraction = skill.RequiredExperience > 0f
                ? Mathf.Clamp01(skill.Experience / skill.RequiredExperience) : 1f;
            drop.Progress = UIFactory.Fill(drop.Rect, "Experience bar", 65, 56,
                224f * fraction, 4, UIFactory.Green);
            drop.Progress.raycastTarget = false;
            UpdateDropText(drop);
            drops.Add(drop);
            if (drops.Count > MaxDrops) RemoveDrop(0);
        }

        private static void UpdateDropText(Drop drop)
        {
            drop.AmountText.text = Localizer.F("ui-xp-gained", drop.Amount.ToString("0.##"));
        }

        private void OnLevel(SkillData skill, int level)
        {
            if (skill == null || !isActiveAndEnabled) return;
            levelAge = 0f;
            levelPopup.gameObject.SetActive(true);
            levelTitle.text = Localizer.F("ui-levelup", Localizer.SkillName(skill.Id), level);
            levelHint.text = Localizer.T("ui-levelup-hint");
            levelIcon.sprite = ResourceLoader.Sprite("skillicon", skill.Id);
            levelIcon.enabled = levelIcon.sprite != null;
            SoundPlayer.Play("levelup");
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = drops.Count - 1; i >= 0; i--)
            {
                Drop drop = drops[i];
                drop.Age += dt;
                if (drop.Age >= DropLifetime)
                {
                    RemoveDrop(i);
                    continue;
                }
                int index = drops.Count - 1 - i;
                float baseY = 92f + index * 72f;
                drop.Rect.anchoredPosition = new Vector2(-34f, -baseY + drop.Age * 9f);
                float fadeIn = Mathf.Clamp01(drop.Age / .18f);
                float fadeOut = Mathf.Clamp01((DropLifetime - drop.Age) / .9f);
                drop.Group.alpha = Mathf.Min(fadeIn, fadeOut);
            }

            if (levelPopup.gameObject.activeSelf)
            {
                levelAge += dt;
                if (levelAge >= PopupLifetime)
                {
                    levelPopup.gameObject.SetActive(false);
                }
                else
                {
                    float a = Mathf.Min(Mathf.Clamp01(levelAge / .35f),
                        Mathf.Clamp01((PopupLifetime - levelAge) / .5f));
                    levelGroup.alpha = a * a * (3f - 2f * a);
                    levelPopup.anchoredPosition = new Vector2(0, -24 - (1f - a) * 12f);
                    if (levelSeparator != null)
                    {
                        RectTransform line = (RectTransform)levelSeparator.transform;
                        line.sizeDelta = new Vector2(480f * Mathf.Clamp01(levelAge / .7f), 64f);
                    }
                }
            }
        }

        private void RemoveDrop(int index)
        {
            Drop drop = drops[index];
            drops.RemoveAt(index);
            if (drop.Rect != null) Destroy(drop.Rect.gameObject);
        }

        public void Dispose()
        {
            SkillCatalog.ExperienceGained -= OnExperience;
            SkillCatalog.LevelGained -= OnLevel;
            Destroy(gameObject);
        }
    }
}
