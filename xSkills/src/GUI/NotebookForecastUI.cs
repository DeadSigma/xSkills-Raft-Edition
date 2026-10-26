using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using xSkills.Services;
using xSkills.Systems;

namespace xSkills.GUI
{
    // Вкладка создаётся отдельно от сюжетных страниц и сетевой нумерации
    internal sealed class NotebookForecastUI : MonoBehaviour
    {
        private const int LastDayOffset = 10;
        private NoteBookUI notebook;
        private RectTransform tabRoot;
        private RectTransform leftRoot;
        private RectTransform rightRoot;
        private TMP_Text nativeTabText;
        private Text nativeLegacyTabText;
        private Text leftTitle;
        private Text rightTitle;
        private Text leftBody;
        private Text rightBody;
        private bool warnedMissingBookmark;
        private bool initialized;
        private bool warnedUnsupportedPages;
        private bool unlocked;
        private bool inForecast;
        private int startDay = -1;
        private int forecastRevision = -1;
        private int spread;
        private uint savedStoryIndex;

        internal void Bind(NoteBookUI target)
        {
            if (notebook == target) return;
            if (notebook != null) notebook.OnBookActiveChanged -= BookVisibilityChanged;
            notebook = target;
            if (notebook != null) notebook.OnBookActiveChanged += BookVisibilityChanged;
        }

        internal void Sync(bool isUnlocked)
        {
            if (notebook == null) return;
            if (!initialized && !CreateInterface()) return;

            if (unlocked != isUnlocked)
            {
                unlocked = isUnlocked;
                if (!unlocked && inForecast) ExitForecast();
                if (tabRoot != null) tabRoot.gameObject.SetActive(unlocked);
            }
            if (unlocked) UpdateBookmarkLabel();
            if (inForecast && notebook.isDisplayed)
            {
                int current = Math.Max(0, WorldManager.DayCounter);
                if (startDay != current || forecastRevision != RainForecastSchedule.Revision)
                {
                    startDay = current;
                    forecastRevision = RainForecastSchedule.Revision;
                    RefreshSpread();
                }
            }
        }

        private bool CreateInterface()
        {
            if (warnedMissingBookmark) return false;
            NoteBookPage left = notebook.FindNotePageByIndex(0);
            NoteBookPage right = notebook.FindNotePageByIndex(1);
            if (left == null || right == null) return false;
            RectTransform leftTemplate = left.transform as RectTransform;
            RectTransform rightTemplate = right.transform as RectTransform;
            if (leftTemplate == null || rightTemplate == null)
            {
                if (!warnedUnsupportedPages)
                {
                    warnedUnsupportedPages = true;
                    Debug.LogWarning("[xSkills] Forecast: notebook page layout is not RectTransform");
                }
                return false;
            }

            leftRoot = CopyPageGeometry(leftTemplate, "xSkills forecast left page");
            rightRoot = CopyPageGeometry(rightTemplate, "xSkills forecast right page");
            BuildForecastPage(leftRoot, out leftTitle, out leftBody);
            BuildForecastPage(rightRoot, out rightTitle, out rightBody);
            if (!CreateVanillaBookmark())
            {
                Destroy(leftRoot.gameObject);
                Destroy(rightRoot.gameObject);
                leftRoot = null;
                rightRoot = null;
                return false;
            }
            leftRoot.gameObject.SetActive(false);
            rightRoot.gameObject.SetActive(false);
            tabRoot.gameObject.SetActive(false);
            initialized = true;
            return true;
        }

        private static RectTransform CopyPageGeometry(RectTransform source, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform result = (RectTransform)go.transform;
            result.SetParent(source.parent, false);
            result.anchorMin = source.anchorMin;
            result.anchorMax = source.anchorMax;
            result.pivot = source.pivot;
            result.anchoredPosition3D = source.anchoredPosition3D;
            result.sizeDelta = source.sizeDelta;
            result.localRotation = source.localRotation;
            result.localScale = source.localScale;
            result.SetAsLastSibling();
            return result;
        }

        private static RectTransform AnchoredRect(Transform parent, string name,
            float minX, float minY, float maxX, float maxY)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Text CreateText(RectTransform parent, string name, float minX,
            float minY, float maxX, float maxY, int fontSize, TextAnchor alignment, Color color)
        {
            RectTransform rect = AnchoredRect(parent, name, minX, minY, maxX, maxY);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 8;
            text.resizeTextMaxSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private void BuildForecastPage(RectTransform root, out Text title, out Text content)
        {
            Color ink = new Color(0.25f, 0.18f, 0.12f, 1f);
            title = CreateText(root, "Forecast title", 0.1f, 0.82f, 0.9f, 0.94f,
                34, TextAnchor.MiddleCenter, ink);
            content = CreateText(root, "Forecast information", 0.11f, 0.18f, 0.89f, 0.81f,
                28, TextAnchor.UpperLeft, ink);
        }

        private bool CreateVanillaBookmark()
        {
            Notebook_ThumbnailShortcut template = null;
            Notebook_ThumbnailShortcut[] shortcuts = notebook.GetComponentsInChildren<Notebook_ThumbnailShortcut>(true);
            for (int i = 0; i < shortcuts.Length; i++)
            {
                Notebook_ThumbnailShortcut shortcut = shortcuts[i];
                if (shortcut != null && shortcut.targetPage != null && shortcut.targetPage.pageIndex == 0U)
                {
                    template = shortcut;
                    break;
                }
            }

            if (template == null)
            {
                if (!warnedMissingBookmark)
                {
                    warnedMissingBookmark = true;
                    Debug.LogWarning("[xSkills] Forecast: vanilla First page bookmark was not found");
                }
                return false;
            }

            Transform bookmarkTemplate = template.transform;
            Button sourceButton = template.GetComponentInParent<Button>();
            if (sourceButton != null && sourceButton.GetComponentInChildren<Notebook_ThumbnailShortcut>(true) == template)
                bookmarkTemplate = sourceButton.transform;
            RectTransform original = bookmarkTemplate as RectTransform;
            if (original == null) return false;

            GameObject clone = UnityEngine.Object.Instantiate(bookmarkTemplate.gameObject, bookmarkTemplate.parent, false);
            clone.name = "xSkills Weather Bookmark";
            tabRoot = clone.GetComponent<RectTransform>();
            if (tabRoot == null)
            {
                Destroy(clone);
                return false;
            }
            clone.SetActive(false);

            // Ванильная кнопка остаётся активной - сохраняются её состояния наведения
            Notebook_ThumbnailShortcut[] copiedShortcuts = clone.GetComponentsInChildren<Notebook_ThumbnailShortcut>(true);
            for (int i = 0; i < copiedShortcuts.Length; i++) copiedShortcuts[i].enabled = false;
            Button[] copiedButtons = clone.GetComponentsInChildren<Button>(true);
            Button nativeButton = clone.GetComponent<Button>();
            if (nativeButton == null && copiedButtons.Length > 0)
                nativeButton = copiedButtons[0];
            for (int i = 0; i < copiedButtons.Length; i++)
            {
                Button button = copiedButtons[i];
                if (button == nativeButton) continue;
                button.enabled = false;
            }

            TMP_Text[] textLabels = clone.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < textLabels.Length; i++)
            {
                if (nativeTabText == null || !string.IsNullOrEmpty(textLabels[i].text))
                    nativeTabText = textLabels[i];
                if (nativeTabText != null && !string.IsNullOrEmpty(nativeTabText.text)) break;
            }
            if (nativeTabText == null)
            {
                Text[] legacyLabels = clone.GetComponentsInChildren<Text>(true);
                if (legacyLabels.Length > 0) nativeLegacyTabText = legacyLabels[0];
            }

            // Встроенные вызовы сюжетной закладки заменяются погодным действием
            if (nativeButton == null)
            {
                RectTransform hitArea = AnchoredRect(tabRoot, "Weather bookmark hit area", 0f, 0f, 1f, 1f);
                Image hitGraphic = hitArea.gameObject.AddComponent<Image>();
                hitGraphic.color = new Color(1f, 1f, 1f, 0.001f);
                nativeButton = hitArea.gameObject.AddComponent<Button>();
                hitArea.SetAsLastSibling();
            }
            nativeButton.enabled = true;
            nativeButton.interactable = true;
            nativeButton.onClick = new Button.ButtonClickedEvent();
            nativeButton.onClick.AddListener(ToggleForecast);
            nativeButton.navigation = new Navigation { mode = Navigation.Mode.None };
            Graphic graphic = nativeButton.targetGraphic;
            if (graphic == null || !graphic.raycastTarget)
            {
                Image image = nativeButton.GetComponent<Image>();
                if (image == null)
                {
                    image = nativeButton.gameObject.AddComponent<Image>();
                    image.color = new Color(1f, 1f, 1f, 0.001f);
                }
                image.raycastTarget = true;
                nativeButton.targetGraphic = image;
            }
            Animator tabAnimator = nativeButton.GetComponentInParent<Animator>();
            if (tabAnimator == null || !tabAnimator.transform.IsChildOf(tabRoot))
            {
                NotebookBookmarkHover hover = nativeButton.gameObject.AddComponent<NotebookBookmarkHover>();
                hover.Setup(tabRoot);
            }

            Canvas bookCanvas = notebook.GetComponentInChildren<Canvas>(true);
            RectTransform bookRect = bookCanvas != null ? bookCanvas.transform as RectTransform : null;
            if (bookRect != null)
            {
                // Положение по горизонтали наследуется от «First page», по вертикали - низ книги
                if (bookmarkTemplate.GetComponentInParent<NoteBookPage>() != null)
                    tabRoot.SetParent(bookRect, true);
                Vector3 local = bookRect.InverseTransformPoint(tabRoot.position);
                local.y = bookRect.rect.yMin + bookRect.rect.height * 0.095f;
                tabRoot.position = bookRect.TransformPoint(local);
            }
            else
            {
                tabRoot.anchoredPosition += new Vector2(0f, -original.rect.height * 6f);
            }
            tabRoot.SetAsLastSibling();
            UpdateBookmarkLabel();
            return true;
        }

        private void UpdateBookmarkLabel()
        {
            string label = Localizer.T("forecast-tab");
            if (nativeTabText != null && nativeTabText.text != label)
                nativeTabText.text = label;
            if (nativeLegacyTabText != null && nativeLegacyTabText.text != label)
                nativeLegacyTabText.text = label;
        }

        internal void OverrideVanillaArrows(GameObject left, GameObject right,
            GameObject leftConsole, GameObject rightConsole)
        {
            if (!inForecast || notebook == null || !notebook.isDisplayed) return;
            bool canGoLeft = spread > 0;
            bool canGoRight = (spread + 1) * 2 <= LastDayOffset;
            bool gamepad = SimpleMonoBehaviourSingleton<CustomInputConfig>.Instance.Gamepad;
            SetActiveIfChanged(left, canGoLeft && !gamepad);
            SetActiveIfChanged(right, canGoRight && !gamepad);
            SetActiveIfChanged(leftConsole, canGoLeft && gamepad);
            SetActiveIfChanged(rightConsole, canGoRight && gamepad);
        }

        private static void SetActiveIfChanged(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }

        private void ToggleForecast()
        {
            if (notebook == null || !notebook.isDisplayed) return;
            // Перед нажатием повторно проверяется доступность способности
            if (!NotebookForecastSystem.IsUnlocked()) return;
            if (inForecast) ExitForecast(true);
            else EnterForecast();
        }

        private void EnterForecast()
        {
            Localizer.Refresh();
            savedStoryIndex = notebook.GetCurrentPageIndex();
            inForecast = true;
            spread = 0;
            startDay = Math.Max(0, WorldManager.DayCounter);
            forecastRevision = RainForecastSchedule.Revision;
            NoteBookPage[] originalPages = notebook.GetComponentsInChildren<NoteBookPage>(true);
            for (int i = 0; i < originalPages.Length; i++)
                originalPages[i].SetPageActive(false);
            leftRoot.gameObject.SetActive(true);
            rightRoot.gameObject.SetActive(true);
            leftRoot.SetAsLastSibling();
            rightRoot.SetAsLastSibling();
            tabRoot.SetAsLastSibling();
            RefreshSpread();
        }

        private void ExitForecast(bool restoreStoryPage = true)
        {
            if (!inForecast) return;
            inForecast = false;
            if (leftRoot != null) leftRoot.gameObject.SetActive(false);
            if (rightRoot != null) rightRoot.gameObject.SetActive(false);
            // При нажатии сюжетной закладки целевую страницу выставляет сама Raft
            if (restoreStoryPage && notebook != null)
                notebook.SetPageIndex(savedStoryIndex);
            UpdateBookmarkLabel();
        }

        internal void OnVanillaNavigation()
        {
            if (inForecast) ExitForecast(false);
        }

        internal bool HandleVanillaPageFlip(int step)
        {
            if (!inForecast) return false;
            if (step < 0) PreviousSpread();
            else NextSpread();
            return true;
        }

        private void PreviousSpread()
        {
            if (!inForecast || spread <= 0) return;
            spread--;
            RefreshSpread();
        }

        private void NextSpread()
        {
            if (!inForecast || (spread + 1) * 2 > LastDayOffset) return;
            spread++;
            RefreshSpread();
        }

        private void RefreshSpread()
        {
            if (!inForecast) return;
            UpdateBookmarkLabel();
            FillDay(leftTitle, leftBody, spread * 2);
            FillDay(rightTitle, rightBody, spread * 2 + 1);
        }

        private void FillDay(Text heading, Text body, int offset)
        {
            if (offset > LastDayOffset || startDay > int.MaxValue - offset)
            {
                heading.text = string.Empty;
                body.text = string.Empty;
                return;
            }
            WorldEnvironment.ForecastDay forecast;
            if (!WorldEnvironment.TryForecastDay(startDay + offset, out forecast))
            {
                heading.text = string.Empty;
                body.text = string.Empty;
                return;
            }
            heading.text = offset == 0 ? Localizer.T("forecast-today") :
                offset == 1 ? Localizer.T("forecast-tomorrow") :
                Localizer.F("forecast-in-days", offset);
            string day = WorldEnvironment.FormatForecastDate(forecast.Day);
            string climate = !string.IsNullOrEmpty(forecast.DayClimate) ? forecast.DayClimate :
                !string.IsNullOrEmpty(forecast.NightClimate) ? forecast.NightClimate :
                Localizer.T("forecast-normal");
            string more = string.Empty;
            if (!string.IsNullOrEmpty(forecast.NightClimate) &&
                forecast.NightClimate != climate)
                more = "\n" + forecast.NightClimate;

            string observed = string.Empty;
            if (offset == 0)
            {
                WorldEnvironment.Snapshot now;
                if (WorldEnvironment.TryRead(out now))
                    observed = "\n\n" + Localizer.F("forecast-now", now.Weather,
                        now.Temperature.ToString("0.0"));
            }
            bool raining;
            string details;
            string rainText;
            if (RainForecastSchedule.TryGetRainDescription(startDay + offset, out raining, out details))
            {
                rainText = Localizer.T(raining ? "forecast-rain-yes" : "forecast-rain-no");
                if (raining && !string.IsNullOrEmpty(details)) rainText += "\n" + details;
            }
            else rainText = Localizer.T("forecast-rain-unavailable");
            body.text = day + "\n\n" + rainText + "\n\n" +
                Localizer.F("forecast-day-temp", forecast.DayTemperature.ToString("0.0")) + "\n" +
                Localizer.F("forecast-night-temp", forecast.NightTemperature.ToString("0.0")) + "\n\n" +
                Localizer.F("forecast-day-humidity", Mathf.RoundToInt(forecast.DayHumidity)) + "\n" +
                Localizer.F("forecast-night-humidity", Mathf.RoundToInt(forecast.NightHumidity)) + "\n\n" +
                climate + more + observed;
        }

        private void BookVisibilityChanged(bool opened)
        {
            if (!opened) ExitForecast();
            else if (unlocked) UpdateBookmarkLabel();
        }

        private void OnDestroy()
        {
            ExitForecast();
            if (notebook != null) notebook.OnBookActiveChanged -= BookVisibilityChanged;
            if (leftRoot != null) Destroy(leftRoot.gameObject);
            if (rightRoot != null) Destroy(rightRoot.gameObject);
            if (tabRoot != null) Destroy(tabRoot.gameObject);
        }
    }
}
