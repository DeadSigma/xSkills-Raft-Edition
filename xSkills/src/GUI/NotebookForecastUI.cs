using System;
using UnityEngine;
using UnityEngine.UI;
using xSkills.Services;
using xSkills.Skills;
using xSkills.Systems;

namespace xSkills.GUI
{
    // Вкладка создаётся отдельно от сюжетных страниц и сетевой нумерации
    internal sealed class NotebookForecastUI : MonoBehaviour
    {
        private static int LastDayOffset
        {
            get
            {
                SkillData skill = SkillCatalog.Find("survival");
                AbilityData perk = skill != null ? skill.FindAbility("forecast") : null;
                if (perk == null || perk.ValuesPerTier == 0 || perk.Values.Length == 0) return 10;
                int tier = Mathf.Clamp(Mathf.Max(1, perk.PreviewTier), 1, perk.MaxTier);
                return Mathf.Clamp(perk.Values[(tier - 1) * perk.ValuesPerTier], 1, 30);
            }
        }
        private NoteBookUI notebook;
        private RectTransform tabRoot;
        private RectTransform leftRoot;
        private RectTransform rightRoot;
        private Text tabText;
        private Text leftTitle;
        private Text rightTitle;
        private Text leftBody;
        private Text rightBody;
        private Button previousButton;
        private Button nextButton;
        private bool initialized;
        private bool warnedUnsupportedPages;
        private bool unlocked;
        private bool inForecast;
        private int startDay = -1;
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
            if (inForecast && notebook.isDisplayed)
            {
                int current = Math.Max(0, WorldManager.DayCounter);
                if (startDay != current)
                {
                    startDay = current;
                    RefreshSpread();
                }
            }
        }

        private bool CreateInterface()
        {
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
            tabRoot = CopyPageGeometry(rightTemplate, "xSkills forecast tab anchor");
            BuildForecastPage(leftRoot, out leftTitle, out leftBody, true);
            BuildForecastPage(rightRoot, out rightTitle, out rightBody, false);
            BuildTab(tabRoot);
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

        private static Button CreateButton(RectTransform parent, string name,
            float minX, float minY, float maxX, float maxY, Action onClick)
        {
            RectTransform rect = AnchoredRect(parent, name, minX, minY, maxX, maxY);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.81f, 0.71f, 0.51f, 0.82f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.AddListener(() => onClick());
            return button;
        }

        private void BuildForecastPage(RectTransform root, out Text title,
            out Text content, bool isLeft)
        {
            Color ink = new Color(0.25f, 0.18f, 0.12f, 1f);
            title = CreateText(root, "Forecast title", 0.1f, 0.82f, 0.9f, 0.94f,
                34, TextAnchor.MiddleCenter, ink);
            content = CreateText(root, "Forecast information", 0.11f, 0.18f, 0.89f, 0.81f,
                28, TextAnchor.UpperLeft, ink);
            Button nav = CreateButton(root, "Forecast navigation",
                isLeft ? 0.13f : 0.62f, 0.055f, isLeft ? 0.40f : 0.89f, 0.14f,
                isLeft ? (Action)PreviousSpread : NextSpread);
            Text arrow = CreateText((RectTransform)nav.transform, "Arrow", 0f, 0f, 1f, 1f,
                22, TextAnchor.MiddleCenter, ink);
            arrow.text = isLeft ? "◀" : "▶";
            if (isLeft) previousButton = nav;
            else nextButton = nav;
        }

        private void BuildTab(RectTransform anchor)
        {
            Button button = CreateButton(anchor, "Weather bookmark", 0.73f, 0.87f, 1.02f, 0.98f, ToggleForecast);
            tabText = CreateText((RectTransform)button.transform, "Bookmark text", 0f, 0f, 1f, 1f,
                22, TextAnchor.MiddleCenter, new Color(0.22f, 0.16f, 0.1f, 1f));
            tabText.text = Localizer.T("forecast-tab");
        }

        private void ToggleForecast()
        {
            if (!unlocked || notebook == null || !notebook.isDisplayed) return;
            if (inForecast) ExitForecast();
            else EnterForecast();
        }

        private void EnterForecast()
        {
            Localizer.Refresh();
            savedStoryIndex = notebook.GetCurrentPageIndex();
            inForecast = true;
            spread = 0;
            startDay = Math.Max(0, WorldManager.DayCounter);
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

        private void ExitForecast()
        {
            if (!inForecast) return;
            inForecast = false;
            if (leftRoot != null) leftRoot.gameObject.SetActive(false);
            if (rightRoot != null) rightRoot.gameObject.SetActive(false);
            if (notebook != null) notebook.SetPageIndex(savedStoryIndex);
            if (tabText != null) tabText.text = Localizer.T("forecast-tab");
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
            tabText.text = Localizer.T("forecast-back");
            FillDay(leftTitle, leftBody, spread * 2);
            FillDay(rightTitle, rightBody, spread * 2 + 1);
            previousButton.gameObject.SetActive(spread > 0);
            nextButton.gameObject.SetActive((spread + 1) * 2 <= LastDayOffset);
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
            string day = Localizer.F("forecast-day-number", forecast.Day);
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
            body.text = day + "\n\n" +
                Localizer.F("forecast-day-temp", forecast.DayTemperature.ToString("0.0")) + "\n" +
                Localizer.F("forecast-night-temp", forecast.NightTemperature.ToString("0.0")) + "\n\n" +
                Localizer.F("forecast-day-humidity", Mathf.RoundToInt(forecast.DayHumidity)) + "\n" +
                Localizer.F("forecast-night-humidity", Mathf.RoundToInt(forecast.NightHumidity)) + "\n\n" +
                climate + more + observed + "\n\n" + Localizer.T("forecast-precipitation-note");
        }

        private void BookVisibilityChanged(bool opened)
        {
            if (!opened) ExitForecast();
            else if (unlocked && tabText != null) tabText.text = Localizer.T("forecast-tab");
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
