using System;
using UnityEngine;
using UnityEngine.UI;
using HMLLibrary;
using xSkills.Services;
using xSkills.Systems;

namespace xSkills.GUI
{
    internal static class TemperatureHud
    {
        private const string ChoiceKey = "xskills_temperature_hud_choice_v1";
        private const string LayoutPrefix = "xskills_temperature_hud_layout_v1_";
        private const int ItemCount = 5;
        private const float PanelW = 285f;
        private const float OutdoorPanelH = 68f;
        private const float PanelH = 39f;
        private const string DefaultLayoutMigrationKey = "xskills_temperature_hud_top_left_v2";
        private static readonly string[] Names = { "time", "outside", "body", "shade", "fire" };
        private static readonly RectTransform[] Widgets = new RectTransform[ItemCount];
        private static readonly Text[] Labels = new Text[ItemCount];
        private static readonly Text[] Values = new Text[ItemCount];
        private static Text weatherText;
        private static bool playerInDaytimeShade;
        private static bool fireAutoShifted;
        private static Vector2 originalFirePosition;
        private static readonly Vector2[] Defaults = {
            new Vector2(36f, 36f), new Vector2(36f, 81f),
            new Vector2(36f, 158f), new Vector2(36f, 203f), new Vector2(36f, 248f)
        };
        private static GameObject canvasObject;
        private static Canvas canvas;
        private static RectTransform canvasRect;
        private static RectTransform editHint;
        private static RectTransform elementEditor;
        private static readonly RectTransform[] elementButtons = new RectTransform[ItemCount];
        private static readonly Text[] elementButtonTexts = new Text[ItemCount];
        private const string EditorXKey = "xskills_temperature_hud_editor_x_v1";
        private const string EditorYKey = "xskills_temperature_hud_editor_y_v1";
        private static bool draggingEditor;
        private static Vector2 editorGrabOffset;
        private static Sprite fireSprite;
        private static Texture2D fireTexture;
        private static bool editing;
        private static bool menuOpen;
        private static bool ownsCursor;
        private static bool previousCursorVisible;
        private static CursorLockMode previousCursorLock;
        private static int dragged = -1;
        private static Vector2 grabOffset;
        private static float refreshAt;
        private static bool initialized;
        private static bool disabledAfterError;
        public static bool IsEditing { get { return editing; } }
        public static void EndEditing() { if (editing) StopEditing(); }

        public static bool Enabled { get { return PlayerPrefs.GetInt(ChoiceKey, 0) == 1; } }
        public static bool ConsentPending { get { return BodyTemperatureSystem.HasPlayer && !PlayerPrefs.HasKey(ChoiceKey); } }

        private const string WeatherLayoutMigrationKey = "xskills_temperature_hud_weather_layout_v1";

        public static void Initialize()
        {
            // HUD создаётся только после согласия, без изменения курсора при запуске мира
            if (initialized || !Enabled || disabledAfterError) return;
            MigrateDefaultPositions();
            MigrateWeatherLayout();
            initialized = true;
            canvasObject = new GameObject("xSkills Temperature HUD", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.Object.DontDestroyOnLoad(canvasObject);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30500;
            canvasRect = canvasObject.GetComponent<RectTransform>();
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            for (int i = 0; i < ItemCount; i++) BuildWidget(i);
            editHint = MakeRect(canvasRect, "Edit controls", 0, 30, 1120, 50);
            editHint.anchorMin = editHint.anchorMax = new Vector2(.5f, 1f);
            editHint.pivot = new Vector2(.5f, 1f);
            editHint.anchoredPosition = new Vector2(0, -30);
            AddBackground(editHint, new Color(.05f, .055f, .06f, .90f));
            Text hint = AddText(editHint, "Instructions", new Vector2(16f, -4f), new Vector2(1088f, 42f),
                21, TextAnchor.MiddleCenter, new Color(.95f, .79f, .48f));
            hint.text = Localizer.T("hud-edit-hint");
            editHint.gameObject.SetActive(false);
            BuildElementEditor();
            canvasObject.SetActive(false);
        }

        public static void Tick(bool skillMenuOpen)
        {
            if (disabledAfterError) return;
            try
            {
                if (!Enabled)
                {
                    if (editing) StopEditing();
                    RestoreFirePosition();
                    if (canvasObject != null) canvasObject.SetActive(false);
                    return;
                }
                if (!initialized) Initialize();
                TickActive(skillMenuOpen);
            }
            catch (Exception ex)
            {
                Debug.LogError("[xSkills HUD] UI disabled after error: " + ex);
                try { Dispose(); } catch (Exception) { }
                disabledAfterError = true;
            }
        }

        private static void TickActive(bool skillMenuOpen)
        {
            if (!initialized || canvasObject == null) return;
            menuOpen = skillMenuOpen;
            bool inWorld = BodyTemperatureSystem.HasPlayer;
            if (!inWorld)
            {
                if (editing) StopEditing();
                RestoreFirePosition();
                playerInDaytimeShade = false;
                canvasObject.SetActive(false);
                return;
            }

            bool gameMenuOpen = CanvasHelper.ActiveMenu != MenuType.None;
            if (!menuOpen && Input.GetKeyDown(KeyCode.F6) && !ConsoleInputCapture.IsTyping() &&
                (editing || (!gameMenuOpen && GameInputGuard.CanToggle(false))))
            {
                if (editing) StopEditing();
                else StartEditing();
            }
            if (editing && (menuOpen || (Input.GetKeyDown(KeyCode.Escape) && !ConsoleInputCapture.IsTyping())))
                StopEditing();
            if (editing)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            canvasObject.SetActive(editing || (!menuOpen && !gameMenuOpen));
            SetWidgetVisibility(true);
            if (editing) ProcessEditor();
            if (Time.unscaledTime >= refreshAt)
            {
                refreshAt = Time.unscaledTime + 0.33f;
                RefreshWidgets();
            }
        }

        private static void RefreshWidgets()
        {
            if (Labels[0] == null) return;
            WorldEnvironment.Snapshot world;
            bool hasWeather = WorldEnvironment.TryRead(out world);
            Labels[0].text = Localizer.T("hud-time");
            Values[0].text = hasWeather ? world.Clock + " · " + world.Date : "--:--";
            Labels[1].text = Localizer.T("hud-outside");
            Values[1].text = hasWeather ? world.Temperature.ToString("0.0") + " °C" : "-- °C";
            Labels[2].text = Localizer.T("hud-body");
            Values[2].text = BodyTemperatureSystem.Temperature.ToString("0.0") + " °C";
            playerInDaytimeShade = hasWeather && world.Daylight > 0.03f &&
                BodyTemperatureSystem.IsShaded;
            Labels[3].text = Localizer.T("hud-shade");
            Values[3].text = "";
            Labels[4].text = Localizer.T("hud-fire");
            Values[4].text = "";
            if (weatherText != null) weatherText.text = hasWeather ? world.Weather : "--";
            SetWidgetVisibility(true);
            if (editHint != null)
            {
                Text text = editHint.GetComponentInChildren<Text>();
                if (text != null) text.text = Localizer.T("hud-edit-hint");
            }
        }

        private static void BuildWidget(int index)
        {
            RectTransform root = MakeRect(canvasRect, "HUD " + Names[index], 0f, 0f,
                WidgetWidth(index), WidgetHeight(index));
            Widgets[index] = root;
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(
                PlayerPrefs.GetFloat(LayoutPrefix + Names[index] + "_x", Defaults[index].x),
                -PlayerPrefs.GetFloat(LayoutPrefix + Names[index] + "_y", Defaults[index].y));
            root.localScale = Vector3.one * Mathf.Clamp(PlayerPrefs.GetFloat(
                LayoutPrefix + Names[index] + "_scale", 1f), .55f, 2f);
            AddBackground(root, new Color(.085f, .09f, .105f, .62f));
            float captionWidth = index == 0 ? 69f : index == 4 ? 210f : 177f;
            float valueX = index == 0 ? 80f : 185f;
            float labelY = -3f;
            float labelHeight = 32f;
            int fontSize = 18;
            Labels[index] = AddText(root, "Caption", new Vector2(12f, labelY),
                new Vector2(captionWidth, labelHeight), fontSize,
                TextAnchor.MiddleLeft, Color.white);
            Values[index] = AddText(root, "Value", new Vector2(valueX, labelY),
                new Vector2(WidgetWidth(index) - valueX - 9f, labelHeight), fontSize,
                TextAnchor.MiddleRight, new Color(.60f, .9f, .50f));
            if (index == 0)
            {
                Values[index].resizeTextForBestFit = true;
                Values[index].resizeTextMinSize = 11;
                Values[index].resizeTextMaxSize = 18;
                Values[index].horizontalOverflow = HorizontalWrapMode.Overflow;
                Values[index].verticalOverflow = VerticalWrapMode.Truncate;
            }
            if (index == 1)
            {
                weatherText = AddText(root, "Weather condition", new Vector2(12f, -36f),
                    new Vector2(PanelW - 24f, 26f), 16, TextAnchor.MiddleLeft,
                    new Color(.80f, .83f, .87f));
                weatherText.resizeTextForBestFit = true;
                weatherText.resizeTextMinSize = 13;
                weatherText.resizeTextMaxSize = 16;
            }
            if (index == 4)
            {
                Labels[index].rectTransform.anchoredPosition = new Vector2(47f, -3f);
                if (fireSprite == null) LoadFireSprite();
                GameObject fire = new GameObject("Fire icon", typeof(RectTransform), typeof(Image));
                fire.transform.SetParent(root, false);
                RectTransform rect = fire.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(13f, -3f);
                rect.sizeDelta = new Vector2(25f, 32f);
                Image image = fire.GetComponent<Image>();
                image.sprite = fireSprite;
                image.raycastTarget = false;
            }
        }

        private static RectTransform MakeRect(Transform parent, string name, float x, float y, float width, float height)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static void AddBackground(RectTransform parent, Color color)
        {
            Image image = parent.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static Text AddText(Transform parent, string name, Vector2 position,
            Vector2 size, int fontSize, TextAnchor alignment, Color color)
        {
            RectTransform rect = MakeRect(parent, name, position.x, -position.y, size.x, size.y);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        private static void LoadFireSprite()
        {
            byte[] bytes = ResourceLoader.FileBytes("assets/ui/warming.png");
            if (bytes == null || bytes.Length == 0) return;

            fireTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            fireTexture.filterMode = FilterMode.Bilinear;
            if (!fireTexture.LoadImage(bytes))
            {
                Debug.LogWarning("[xSkills HUD] Invalid PNG: assets/ui/warming.png");
                UnityEngine.Object.Destroy(fireTexture);
                fireTexture = null;
                return;
            }

            fireSprite = Sprite.Create(fireTexture,
                new Rect(0f, 0f, fireTexture.width, fireTexture.height),
                new Vector2(.5f, .5f));
        }

        private static float WidgetWidth(int index)
        {
            return PanelW;
        }

        private static float WidgetHeight(int index)
        {
            return index == 1 ? OutdoorPanelH : PanelH;
        }

        private static bool ItemEnabled(int index)
        {
            return PlayerPrefs.GetInt(LayoutPrefix + Names[index] + "_visible", 1) != 0;
        }

        private static void SetWidgetVisibility(bool visible)
        {
            bool shadeVisible = visible && ItemEnabled(3) &&
                (editing || playerInDaytimeShade);
            bool fireVisible = visible && ItemEnabled(4) &&
                (editing || BodyTemperatureSystem.IsWarmingByFire);

            UpdateFireAutoPosition(shadeVisible, fireVisible);
            for (int i = 0; i < ItemCount; i++)
            {
                if (Widgets[i] == null) continue;
                bool itemVisible = i == 3 ? shadeVisible : i == 4 ? fireVisible :
                    visible && ItemEnabled(i);
                Widgets[i].gameObject.SetActive(itemVisible);
            }
            if (editHint != null) editHint.gameObject.SetActive(visible && editing);
            if (elementEditor != null) elementEditor.gameObject.SetActive(visible && editing);
        }

        private static bool HasDefaultPosition(int index)
        {
            RectTransform widget = Widgets[index];
            if (widget == null) return false;
            Vector2 position = GetPosition(index);
            return Mathf.Abs(position.x - Defaults[index].x) < 0.5f &&
                Mathf.Abs(position.y - Defaults[index].y) < 0.5f &&
                Mathf.Abs(widget.localScale.x - 1f) < 0.01f;
        }

        private static void RestoreFirePosition()
        {
            if (!fireAutoShifted) return;
            if (Widgets[4] != null)
                Widgets[4].anchoredPosition = originalFirePosition;
            fireAutoShifted = false;
        }

        private static void UpdateFireAutoPosition(bool shadeVisible, bool fireVisible)
        {
            if (fireAutoShifted && (!fireVisible || shadeVisible || editing))
                RestoreFirePosition();

            if (fireAutoShifted || editing || shadeVisible || !fireVisible ||
                !HasDefaultPosition(3) || !HasDefaultPosition(4)) return;

            originalFirePosition = Widgets[4].anchoredPosition;
            Widgets[4].anchoredPosition = Widgets[3].anchoredPosition;
            fireAutoShifted = true;
        }

        private static void BuildElementEditor()
        {
            elementEditor = MakeRect(canvasRect, "Visible HUD elements", 0f, 0f, 278f, 283f);
            elementEditor.anchorMin = elementEditor.anchorMax = new Vector2(1f, 1f);
            elementEditor.pivot = new Vector2(1f, 1f);
            elementEditor.anchoredPosition = new Vector2(-36f, -91f);
            if (PlayerPrefs.HasKey(EditorXKey) && PlayerPrefs.HasKey(EditorYKey))
                SetEditorPosition(new Vector2(PlayerPrefs.GetFloat(EditorXKey),
                    PlayerPrefs.GetFloat(EditorYKey)));
            AddBackground(elementEditor, new Color(.06f, .065f, .075f, .91f));
            AddText(elementEditor, "Element editor title", new Vector2(12f, -7f),
                new Vector2(252f, 32f), 19, TextAnchor.MiddleLeft, Color.white);
            for (int i = 0; i < ItemCount; i++)
            {
                RectTransform button = MakeRect(elementEditor, "Toggle " + Names[i],
                    10f, 46f + i * 45f, 258f, 39f);
                AddBackground(button, new Color(.19f, .2f, .21f, .94f));
                elementButtons[i] = button;
                elementButtonTexts[i] = AddText(button, "State", new Vector2(10f, -2f),
                    new Vector2(238f, 35f), 16, TextAnchor.MiddleLeft, Color.white);
            }
            UpdateElementEditor();
            elementEditor.gameObject.SetActive(false);
        }

        private static void UpdateElementEditor()
        {
            if (elementEditor == null) return;
            Text title = elementEditor.GetComponentInChildren<Text>();
            if (title != null) title.text = Localizer.T("hud-elements-title");
            for (int i = 0; i < ItemCount; i++)
            {
                if (elementButtonTexts[i] == null) continue;
                bool enabled = ItemEnabled(i);
                string titleKey = i == 3 ? "hud-light-status" :
                    i == 1 ? "hud-outside-weather" : "hud-" + Names[i];
                elementButtonTexts[i].text = Localizer.T(titleKey) + "  -  " +
                    Localizer.T(enabled ? "hud-element-on" : "hud-element-off");
                elementButtonTexts[i].color = enabled
                    ? new Color(.75f, .98f, .67f) : new Color(.7f, .7f, .7f);
            }
        }

        public static void SetEnabled(bool value)
        {
            PlayerPrefs.SetInt(ChoiceKey, value ? 1 : 0);
            PlayerPrefs.Save();
            if (value) disabledAfterError = false;
            if (!value && editing) StopEditing();
            ReleaseCursor();
            if (canvasObject != null && !value) canvasObject.SetActive(false);
        }

        public static void StartEditing()
        {
            if (!Enabled || !BodyTemperatureSystem.HasPlayer) return;
            if (!initialized)
            {
                try { Initialize(); }
                catch (Exception ex)
                {
                    Debug.LogError("[xSkills HUD] Editor unavailable: " + ex);
                    try { Dispose(); } catch (Exception) { }
                    disabledAfterError = true;
                    return;
                }
            }
            if (!initialized || editing) return;
            editing = true;
            dragged = -1;
            draggingEditor = false;
            AcquireCursor();
            if (canvasObject != null) canvasObject.SetActive(true);
            SetWidgetVisibility(true);
            UpdateElementEditor();
        }

        private static void StopEditing()
        {
            if (!editing) return;
            dragged = -1;
            draggingEditor = false;
            editing = false;
            SaveLayout();
            if (editHint != null) editHint.gameObject.SetActive(false);
            ReleaseCursor();
        }

        private static void ProcessEditor()
        {
            if (canvasRect == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,
                Input.mousePosition, null, out local)) return;
            Vector2 point = new Vector2(local.x - canvasRect.rect.xMin, canvasRect.rect.yMax - local.y);

            if (dragged >= 0 && Input.GetMouseButton(0))
            {
                SetPosition(dragged, point - grabOffset);
                return;
            }

            if (draggingEditor)
            {
                if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
                    draggingEditor = false;
                else
                    SetEditorPosition(point - editorGrabOffset);
                return;
            }

            bool overEditor = elementEditor != null &&
                RectTransformUtility.RectangleContainsScreenPoint(elementEditor, Input.mousePosition, null);
            if (overEditor)
            {
                dragged = -1;
                if (Input.GetMouseButtonDown(0))
                {
                    Vector2 panelPosition = GetEditorPosition();
                    if (point.y < panelPosition.y + 44f)
                    {
                        draggingEditor = true;
                        editorGrabOffset = point - panelPosition;
                        return;
                    }
                    for (int i = 0; i < ItemCount; i++)
                    {
                        if (elementButtons[i] == null || !RectTransformUtility.RectangleContainsScreenPoint(
                            elementButtons[i], Input.mousePosition, null)) continue;
                        PlayerPrefs.SetInt(LayoutPrefix + Names[i] + "_visible", ItemEnabled(i) ? 0 : 1);
                        UpdateElementEditor();
                        SetWidgetVisibility(true);
                        break;
                    }
                }
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                dragged = -1;
                for (int i = ItemCount - 1; i >= 0; i--)
                {
                    if (Widgets[i] != null && Widgets[i].gameObject.activeInHierarchy &&
                        RectTransformUtility.RectangleContainsScreenPoint(
                            Widgets[i], Input.mousePosition, null))
                    {
                        dragged = i;
                        grabOffset = point - GetPosition(i);
                        break;
                    }
                }
            }
            if (Input.GetMouseButtonUp(0)) dragged = -1;
            if (dragged >= 0 && Input.GetMouseButton(0))
                SetPosition(dragged, point - grabOffset);

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                for (int i = ItemCount - 1; i >= 0; i--)
                {
                    if (Widgets[i] != null && Widgets[i].gameObject.activeInHierarchy &&
                        RectTransformUtility.RectangleContainsScreenPoint(
                            Widgets[i], Input.mousePosition, null))
                    {
                        float old = Widgets[i].localScale.x;
                        float next = Mathf.Clamp(old + Mathf.Sign(wheel) * .08f, .55f, 2f);
                        Widgets[i].localScale = Vector3.one * next;
                        SetPosition(i, GetPosition(i));
                        break;
                    }
                }
            }
        }

        private static Vector2 GetEditorPosition()
        {
            return new Vector2(canvasRect.rect.width + elementEditor.anchoredPosition.x -
                elementEditor.rect.width, -elementEditor.anchoredPosition.y);
        }

        private static void SetEditorPosition(Vector2 position)
        {
            if (canvasRect == null || elementEditor == null) return;
            float left = Mathf.Clamp(position.x, 0f,
                Mathf.Max(0f, canvasRect.rect.width - elementEditor.rect.width));
            float top = Mathf.Clamp(position.y, 0f,
                Mathf.Max(0f, canvasRect.rect.height - elementEditor.rect.height));
            elementEditor.anchoredPosition = new Vector2(
                left + elementEditor.rect.width - canvasRect.rect.width, -top);
        }

        private static Vector2 GetPosition(int i)
        {
            return new Vector2(Widgets[i].anchoredPosition.x, -Widgets[i].anchoredPosition.y);
        }

        private static void SetPosition(int i, Vector2 position)
        {
            float scale = Widgets[i].localScale.x;
            float right = Mathf.Max(0f, canvasRect.rect.width - WidgetWidth(i) * scale);
            float bottom = Mathf.Max(0f, canvasRect.rect.height - WidgetHeight(i) * scale);
            Widgets[i].anchoredPosition = new Vector2(
                Mathf.Clamp(position.x, 0f, right), -Mathf.Clamp(position.y, 0f, bottom));
        }

        private static void SaveLayout()
        {
            RestoreFirePosition();
            for (int i = 0; i < ItemCount; i++)
            {
                if (Widgets[i] == null) continue;
                Vector2 position = GetPosition(i);
                PlayerPrefs.SetFloat(LayoutPrefix + Names[i] + "_x", position.x);
                PlayerPrefs.SetFloat(LayoutPrefix + Names[i] + "_y", position.y);
                PlayerPrefs.SetFloat(LayoutPrefix + Names[i] + "_scale", Widgets[i].localScale.x);
            }
            if (elementEditor != null && canvasRect != null)
            {
                Vector2 editorPosition = GetEditorPosition();
                PlayerPrefs.SetFloat(EditorXKey, editorPosition.x);
                PlayerPrefs.SetFloat(EditorYKey, editorPosition.y);
            }
            PlayerPrefs.Save();
        }

        private static void MigrateDefaultPositions()
        {
            if (PlayerPrefs.GetInt(DefaultLayoutMigrationKey, 0) != 0) return;
            for (int i = 0; i < ItemCount; i++)
            {
                string prefix = LayoutPrefix + Names[i];
                if (!PlayerPrefs.HasKey(prefix + "_x") || !PlayerPrefs.HasKey(prefix + "_y")) continue;
                float x = PlayerPrefs.GetFloat(prefix + "_x");
                float y = PlayerPrefs.GetFloat(prefix + "_y");
                float oldY = 156f + i * 45f;
                if (Mathf.Abs(x - 36f) < 0.5f && Mathf.Abs(y - oldY) < 0.5f)
                    PlayerPrefs.SetFloat(prefix + "_y", Defaults[i].y);
            }
            PlayerPrefs.SetInt(DefaultLayoutMigrationKey, 1);
            PlayerPrefs.Save();
        }

        private static void MigrateWeatherLayout()
        {
            if (PlayerPrefs.GetInt(WeatherLayoutMigrationKey, 0) != 0) return;
            float[] previousY = { 36f, 81f, 126f, 171f, 216f };
            for (int i = 2; i <= 4; i++)
            {
                string prefix = LayoutPrefix + Names[i];
                if (!PlayerPrefs.HasKey(prefix + "_x") || !PlayerPrefs.HasKey(prefix + "_y")) continue;
                float x = PlayerPrefs.GetFloat(prefix + "_x");
                float y = PlayerPrefs.GetFloat(prefix + "_y");
                if (Mathf.Abs(x - 36f) < .5f && Mathf.Abs(y - previousY[i]) < .5f)
                    PlayerPrefs.SetFloat(prefix + "_y", Defaults[i].y);
            }
            PlayerPrefs.SetInt(WeatherLayoutMigrationKey, 1);
            PlayerPrefs.Save();
        }

        public static void ResetLayout()
        {
            RestoreFirePosition();
            for (int i = 0; i < ItemCount; i++)
            {
                PlayerPrefs.DeleteKey(LayoutPrefix + Names[i] + "_x");
                PlayerPrefs.DeleteKey(LayoutPrefix + Names[i] + "_y");
                PlayerPrefs.DeleteKey(LayoutPrefix + Names[i] + "_scale");
                if (Widgets[i] != null)
                {
                    Widgets[i].anchoredPosition = new Vector2(Defaults[i].x, -Defaults[i].y);
                    Widgets[i].localScale = Vector3.one;
                }
            }
            PlayerPrefs.DeleteKey(EditorXKey);
            PlayerPrefs.DeleteKey(EditorYKey);
            if (elementEditor != null) elementEditor.anchoredPosition = new Vector2(-36f, -91f);
            PlayerPrefs.Save();
        }

        private static void AcquireCursor()
        {
            if (ownsCursor) return;
            previousCursorVisible = Cursor.visible;
            previousCursorLock = Cursor.lockState;
            RAPI.TogglePriorityCursor(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            ownsCursor = true;
        }

        private static void ReleaseCursor()
        {
            if (!ownsCursor) return;
            ownsCursor = false;
            RAPI.TogglePriorityCursor(false);
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        public static void Dispose()
        {
            if (!initialized && canvasObject == null) return;
            if (editing) StopEditing();
            ReleaseCursor();
            if (canvasObject != null) UnityEngine.Object.Destroy(canvasObject);
            if (fireSprite != null) UnityEngine.Object.Destroy(fireSprite);
            if (fireTexture != null) UnityEngine.Object.Destroy(fireTexture);
            canvasObject = null;
            canvas = null;
            canvasRect = null;
            editHint = null;
            elementEditor = null;
            weatherText = null;
            draggingEditor = false;
            playerInDaytimeShade = false;
            fireAutoShifted = false;
            fireSprite = null;
            fireTexture = null;
            for (int i = 0; i < ItemCount; i++)
            {
                Widgets[i] = null;
                Labels[i] = null;
                Values[i] = null;
                elementButtons[i] = null;
                elementButtonTexts[i] = null;
            }
            initialized = false;
        }
    }
}
