using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using HMLLibrary;
using xSkills.Services;
using xSkills.Systems;

namespace xSkills.GUI
{
    internal static class WaterStaminaHud
    {
        private static GameObject root;
        private static Image fill;
        private static RectTransform fillRect;
        private static Text title;
        private static TextMeshProUGUI tmpTitle;
        private static AssetBundle bundle;
        private static bool usingOriginal;
        private static bool assetAttempted;

        internal static void Refresh(float current, float maximum, bool visible)
        {
            if (!visible && root == null) return;
            if (root == null && !Build()) return;
            if (root == null) return;

            visible = visible && CanvasHelper.ActiveMenu == MenuType.None;
            if (root.activeSelf != visible) root.SetActive(visible);
            if (!visible) return;

            float ratio = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
            if (usingOriginal)
            {
                if (fill != null)
                {
                    fill.fillAmount = ratio;
                    fill.color = Color.Lerp(Color.red, Color.cyan, ratio);
                }
            }
            else
            {
                if (fillRect != null) fillRect.anchorMax = new Vector2(ratio, 1f);
                if (fill != null)
                    fill.color = Color.Lerp(new Color(.8f, .25f, .23f),
                        new Color(.26f, .73f, .67f), ratio);
            }

            string translated = WaterSurvivalSystem.LastChancePressesRemaining > 0
                ? Localizer.F("ui-water-last-chance", WaterSurvivalSystem.LastChancePressesRemaining)
                : Localizer.T("ui-water-fatigue");
            if (title != null && title.text != translated) title.text = translated;
            if (tmpTitle != null && tmpTitle.text != translated) tmpTitle.text = translated;
        }

        private static bool Build()
        {
            CanvasHelper helper = ComponentManager<CanvasHelper>.Value;
            if (helper == null) return false;
            Canvas parentCanvas = helper.GetComponentInParent<Canvas>();
            if (parentCanvas == null) return false;

            if (!assetAttempted)
            {
                assetAttempted = true;
                if (TryBuildOriginal(helper, parentCanvas)) return true;
            }
            return BuildFallback(helper, parentCanvas);
        }

        private static bool TryBuildOriginal(CanvasHelper helper, Canvas parentCanvas)
        {
            // Оригинальный интерфейс загружается из AssetBundle BetterRaft, если ассет присутствует
            byte[] bytes = ResourceLoader.FileBytes("assets/ui/fatiguegui.assets", false) ??
                           ResourceLoader.FileBytes("assets/fatiguegui.assets", false) ??
                           ResourceLoader.FileBytes("fatiguegui.assets", false);
            if (bytes == null || bytes.Length == 0) return false;

            try
            {
                bundle = AssetBundle.LoadFromMemory(bytes);
                if (bundle == null) throw new InvalidOperationException("fatiguegui.assets could not be loaded");
                GameObject prefab = bundle.LoadAsset<GameObject>("OxygenCanvas");
                if (prefab == null) throw new InvalidOperationException("OxygenCanvas prefab not found");

                root = UnityEngine.Object.Instantiate(prefab);
                root.name = "xSkills Water Stamina (BetterRaft)";
                root.transform.SetParent(parentCanvas.transform, false);

                Transform bar = root.transform.Find("OxygenMeter/Foreground");
                if (bar == null || (fill = bar.GetComponent<Image>()) == null)
                    throw new InvalidOperationException("OxygenMeter/Foreground image not found");

                Transform label = root.transform.Find("OxygenMeter/Label");
                if (label != null)
                {
                    title = label.GetComponent<Text>();
                    tmpTitle = label.GetComponent<TextMeshProUGUI>();
                }

                RectTransform rect = root.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
                    rect.pivot = new Vector2(1f, 0f);
                    rect.anchoredPosition = new Vector2(-10f, 70f);
                    if (helper.fatigueParent != null)
                    {
                        RectTransform reference = helper.fatigueParent.GetComponent<RectTransform>();
                        if (reference != null)
                        {
                            rect.anchorMin = reference.anchorMin;
                            rect.anchorMax = reference.anchorMax;
                            rect.pivot = reference.pivot;
                            float offset = -(reference.rect.height * reference.localScale.y) - 15f;
                            rect.anchoredPosition = reference.anchoredPosition + new Vector2(0f, offset);
                        }
                    }
                }

                usingOriginal = true;
                root.SetActive(false);
                Debug.Log("[xSkills] Original BetterRaft stamina UI loaded");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Could not load BetterRaft stamina UI: " + ex.Message);
                if (root != null) UnityEngine.Object.Destroy(root);
                root = null;
                fill = null;
                title = null;
                tmpTitle = null;
                if (bundle != null) bundle.Unload(false);
                bundle = null;
                return false;
            }
        }

        private static bool BuildFallback(CanvasHelper helper, Canvas parentCanvas)
        {
            root = new GameObject("xSkills Water Stamina", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            root.transform.SetParent(parentCanvas.transform, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(194f, 28f);
            rect.anchoredPosition = new Vector2(-16f, 75f);
            Image bg = root.GetComponent<Image>();
            bg.color = new Color(.12f, .10f, .08f, .86f);
            bg.raycastTarget = false;

            GameObject bar = new GameObject("Fill", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            bar.transform.SetParent(root.transform, false);
            fillRect = (RectTransform)bar.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(3f, 3f);
            fillRect.offsetMax = new Vector2(-3f, -3f);
            fill = bar.GetComponent<Image>();
            fill.type = Image.Type.Simple;
            fill.raycastTarget = false;

            GameObject label = new GameObject("Label", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            label.transform.SetParent(root.transform, false);
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            title = label.GetComponent<Text>();
            title.text = Localizer.T("ui-water-fatigue");
            title.color = Color.white;
            title.fontSize = 14;
            title.alignment = TextAnchor.MiddleCenter;
            title.raycastTarget = false;
            Text template = helper.GetComponentInChildren<Text>(true);
            if (template != null && template.font != null) title.font = template.font;
            if (title.font == null) title.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            root.SetActive(false);
            return true;
        }

        internal static void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            fill = null;
            fillRect = null;
            title = null;
            tmpTitle = null;
            usingOriginal = false;
            assetAttempted = false;
            if (bundle != null) bundle.Unload(false);
            bundle = null;
        }
    }
}
