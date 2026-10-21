using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using HMLLibrary;
using xSkills.Skills;

using xSkills.Services;
using xSkills.Systems;
namespace xSkills.GUI
{
    internal sealed partial class SkillGraphicalUI : IDisposable
    {
        private const float WindowW = 1800f;
        private const float WindowH = 1060f;
        private GameObject canvasObject;
        private Canvas canvas;
        private RectTransform window;
        private CanvasGroup windowGroup;
        private float openAnimation = 1f;
        private RectTransform topTabs;
        private RectTransform content;
        private RectTransform abilitiesRoot;
        private RectTransform rightInfo;
        private EventSystem ownEventSystem;
        private GraphicRaycaster mouseRaycaster;
        private SkillPointerInput pointerInput;
        private readonly Dictionary<EventSystem, bool> previousEventSystems = new Dictionary<EventSystem, bool>();
        private Vector2 abilityParallax;
        private readonly List<SkillData> skills = SurvivalFirst(SkillCatalog.Create());

        private static List<SkillData> SurvivalFirst(List<SkillData> registered)
        {
            var result = new List<SkillData>(registered);
            int position = result.FindIndex(skill => skill.Id == "survival");
            if (position > 0)
            {
                SkillData survival = result[position];
                result.RemoveAt(position);
                result.Insert(0, survival);
            }
            return result;
        }
        private readonly List<AbilityButton> visibleButtons = new List<AbilityButton>();
        private SkillData selectedSkill;
        private AbilityData selectedAbility;
        private string page = "";
        private bool isOpen;
        private RectTransform hudConsentOverlay;
        private bool oldCursorVisible;
        private CursorLockMode oldCursorLock;
        public bool IsOpen { get { return isOpen; } }

        public void Initialize()
        {
            canvasObject = new GameObject("xSkills UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;
            UnityEngine.Object.DontDestroyOnLoad(canvasObject);
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            // Отдельная обработка мыши исключает зависимость от EventSystem меню Raft
            GameObject events = new GameObject("xSkills EventSystem", typeof(EventSystem));
            ownEventSystem = events.GetComponent<EventSystem>();
            UnityEngine.Object.DontDestroyOnLoad(events);
            ownEventSystem.enabled = false;
            mouseRaycaster = canvasObject.GetComponent<GraphicRaycaster>();
            pointerInput = new SkillPointerInput(ownEventSystem, mouseRaycaster);
            Localizer.Refresh();
            DietSystem.Initialize();
            BuildWindow();
            SkillCatalog.Changed += OnProgressChanged;
            selectedSkill = skills[0];
            SelectPage("_home");
            canvasObject.SetActive(false);
        }

        public bool TryClosePopup()
        {
            if (!transferOpen) return false;
            CloseTransfer();
            return true;
        }

        public void Toggle()
        {
            if (isOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (isOpen || canvasObject == null) return;
            SelectPage("_home");
            isOpen = true;
            openAnimation = 0f;
            oldCursorVisible = Cursor.visible;
            oldCursorLock = Cursor.lockState;
            // Курсор передаётся RML с высоким приоритетом, чтобы игра не захватывала мышь
            RAPI.TogglePriorityCursor(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            canvasObject.SetActive(true);
            CaptureEventSystems();
            ownEventSystem.enabled = true;
            EventSystem.current = ownEventSystem;
            pointerInput.Reset();
            if (Localizer.Refresh()) RefreshLanguage();
            ResizeWindow();
            try { ShowHudConsentIfNeeded(); }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills HUD] Consent window failed: " + ex);
                DismissHudConsent();
            }
            SoundPlayer.Play("open");
        }

        private void ShowHudConsentIfNeeded()
        {
            if (!TemperatureHud.ConsentPending || window == null || hudConsentOverlay != null) return;

            // Запрос показывается внутри xSkills - курсор игры не захватывается фоновым HUD
            hudConsentOverlay = UIFactory.Rect(window, "HUD opt-in", 0, 0, WindowW, WindowH);
            Image shield = UIFactory.Fill(hudConsentOverlay, "HUD overlay", 0, 0,
                WindowW, WindowH, new Color(.02f, .02f, .025f, .82f));
            shield.raycastTarget = true;

            const float left = 495f;
            const float top = 305f;
            const float width = 810f;
            UIFactory.Fill(hudConsentOverlay, "HUD dialog background", left, top,
                width, 422f, new Color(.16f, .125f, .095f, 1f));
            UIFactory.Label(hudConsentOverlay, "HUD title", Localizer.T("hud-consent-title"),
                left + 35f, top + 28f, width - 70f, 58f, 34, UIFactory.Gold, TextAnchor.MiddleCenter);
            UIFactory.Label(hudConsentOverlay, "HUD details", Localizer.T("hud-consent-description"),
                left + 48f, top + 104f, width - 96f, 160f, 26, UIFactory.White, TextAnchor.MiddleCenter);

            const float buttonTop = top + 310f;
            Image enableFill = UIFactory.Fill(hudConsentOverlay, "HUD yes background",
                left + 40f, buttonTop, 350f, 72f, new Color(.24f, .36f, .17f, 1f));
            enableFill.raycastTarget = false;
            UIFactory.Label(hudConsentOverlay, "HUD yes text", Localizer.T("hud-consent-accept"),
                left + 40f, buttonTop + 10f, 350f, 52f, 28, UIFactory.White, TextAnchor.MiddleCenter);
            UIFactory.Button(hudConsentOverlay, "HUD accept", left + 40f, buttonTop, 350f, 72f,
                () => { TemperatureHud.SetEnabled(true); DismissHudConsent(); });

            Image declineFill = UIFactory.Fill(hudConsentOverlay, "HUD no background",
                left + 420f, buttonTop, 350f, 72f, new Color(.27f, .22f, .19f, 1f));
            declineFill.raycastTarget = false;
            UIFactory.Label(hudConsentOverlay, "HUD no text", Localizer.T("hud-consent-reject"),
                left + 420f, buttonTop + 10f, 350f, 52f, 28, UIFactory.White, TextAnchor.MiddleCenter);
            UIFactory.Button(hudConsentOverlay, "HUD decline", left + 420f, buttonTop, 350f, 72f,
                () => { TemperatureHud.SetEnabled(false); DismissHudConsent(); });
        }

        private void DismissHudConsent()
        {
            if (hudConsentOverlay == null) return;
            hudConsentOverlay.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(hudConsentOverlay.gameObject);
            hudConsentOverlay = null;
            if (pointerInput != null) pointerInput.Reset();
        }

        public void Close()
        {
            if (!isOpen) return;
            isOpen = false;
            DismissHudConsent();
            CloseTransfer(false);
            SoundPlayer.Play("close");
            if (pointerInput != null) pointerInput.Reset();
            if (canvasObject != null) canvasObject.SetActive(false);
            DisposePlayerHome();
            ownEventSystem.enabled = false;
            RestoreEventSystems();
            RAPI.TogglePriorityCursor(false);
            Cursor.lockState = oldCursorLock;
            Cursor.visible = oldCursorVisible;
        }

        public void Tick(float dt)
        {
            DietSystem.Tick();
            if (!isOpen || window == null) return;
            ResizeWindow();
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
            openAnimation = Mathf.Min(1f, openAnimation + dt * 5f);
            if (windowGroup != null) windowGroup.alpha = openAnimation * openAnimation * (3f - 2f * openAnimation);
            if (ownEventSystem != null) EventSystem.current = ownEventSystem;
            UpdateAbilityParallax(dt);
            if (pointerInput != null) pointerInput.Tick();
            if (page == "_home") TickPlayerHome(dt);
            else if (page == "_settings") UpdateClimateRateLabel();
        }

        private void CaptureEventSystems()
        {
            previousEventSystems.Clear();
            foreach (EventSystem existing in UnityEngine.Object.FindObjectsOfType<EventSystem>())
            {
                if (existing == null || existing == ownEventSystem) continue;
                previousEventSystems[existing] = existing.enabled;
                existing.enabled = false;
            }
        }

        private void RestoreEventSystems()
        {
            foreach (KeyValuePair<EventSystem, bool> entry in previousEventSystems)
                if (entry.Key != null) entry.Key.enabled = entry.Value;
            previousEventSystems.Clear();
        }

        private void UpdateAbilityParallax(float dt)
        {
            if (abilitiesRoot == null) return;
            Vector2 target = Vector2.zero;
            if (page != "_info" && page != "_spec" && page != "_home" && page != "_settings" &&
                RectTransformUtility.RectangleContainsScreenPoint(content, Input.mousePosition, null))
            {
                Vector2 local;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    content, Input.mousePosition, null, out local))
                {
                    float nx = Mathf.Clamp((local.x / 1335f - .5f) * 2f, -1f, 1f);
                    float ny = Mathf.Clamp(((-local.y) / 810f - .5f) * 2f, -1f, 1f);
                    target = new Vector2(nx * 18f, -ny * 12f);
                }
            }
            abilityParallax = Vector2.Lerp(abilityParallax, target, 1f - Mathf.Exp(-9f * dt));
            float homeOffsetX = page == "_home" ? (content.rect.width - abilitiesRoot.rect.width) * .5f : 0f;
            abilitiesRoot.anchoredPosition = new Vector2(homeOffsetX + abilityParallax.x, (page == "_home" ? 0f : -94f) + abilityParallax.y);
        }

        private void ResizeWindow()
        {
            RectTransform canvasRect = (RectTransform)canvas.transform;
            float visibleW = canvasRect.rect.width;
            float visibleH = canvasRect.rect.height;
            float scale = Mathf.Min((visibleW - 64f) / WindowW, (visibleH - 64f) / WindowH);
            window.localScale = Vector3.one * Mathf.Max(.1f, Mathf.Min(scale, 2f)) * Mathf.Lerp(.97f, 1f, openAnimation);
        }

        public void Dispose()
        {
            DietSystem.Dispose();
            Close();
            DisposePlayerHome();
            SkillCatalog.Changed -= OnProgressChanged;
            if (canvasObject != null) UnityEngine.Object.Destroy(canvasObject);
            if (ownEventSystem != null) UnityEngine.Object.Destroy(ownEventSystem.gameObject);
            canvasObject = null;
        }

        private void OnProgressChanged()
        {
            if (canvasObject == null) return;
            if (page == "_settings")
            {
                if (shownSettingsSection >= 0) ShowSettingsSection(shownSettingsSection, true);
                return;
            }
            if (page == "_spec" || page == "_info" || page == "_home") SelectPage(page);
            else if (selectedSkill != null) SelectSkill(selectedSkill);
        }

        public bool ChangeAbilityTier(AbilityData ability, int tier)
        {
            SkillProgressStorage.EnsureLoaded();
            if (!SkillProgressStorage.IsWorldBound) return false;
            if (selectedSkill == null || !selectedSkill.SetTier(ability, tier)) return false;
            SkillCatalog.NotifyChanged();
            return true;
        }

        public void Hover(AbilityData ability)
        {
            if (page == "_info" || page == "_spec" || page == "_home" || page == "_settings") return;
            selectedAbility = ability;
            UpdateRightInfo();
        }
    }
}
