using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace xSkills.GUI
{
    internal static class UIFactory
    {
        public static readonly Color White = Color.white;
        public static readonly Color Gold = Hex("FEAE34");
        public static readonly Color Grey = Hex("92806A");
        public static readonly Color Brown = Hex("392A1C");
        public static readonly Color Green = Hex("7AC62F");
        private static Font defaultFont;

        public static Color Hex(string value)
        {
            Color color;
            return ColorUtility.TryParseHtmlString("#" + value, out color) ? color : Color.white;
        }

        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        public static Image Image(Transform parent, string name, string category, string spriteName, float x, float y, float w, float h, float border = 0, Color? tint = null)
        {
            var rect = Rect(parent, name, x, y, w, h);
            Image img = rect.gameObject.AddComponent<Image>();
            img.sprite = ResourceLoader.Sprite(category, spriteName, border);
            img.type = border > 0 && img.sprite != null ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.raycastTarget = false;
            img.color = tint ?? White;
            img.enabled = img.sprite != null;
            return img;
        }

        public static Image Fill(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            Image img = Image(parent, name, "elements", "pixel", x, y, w, h);
            img.color = color;
            if (img.sprite == null) img.sprite = SpriteFromWhite();
            img.enabled = true;
            return img;
        }

        private static Sprite whiteSprite;
        private static Sprite SpriteFromWhite()
        {
            if (whiteSprite == null)
                whiteSprite = Sprite.Create(Texture2D.whiteTexture, new UnityEngine.Rect(0, 0, 1, 1), new Vector2(.5f, .5f));
            return whiteSprite;
        }

        public static Text Label(Transform parent, string name, string content, float x, float y, float w, float h, int fontSize, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var rect = Rect(parent, name, x, y, w, h);
            Text text = rect.gameObject.AddComponent<Text>();
            if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.font = defaultFont;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            rect.gameObject.AddComponent<BitmapFontLabel>();
            return text;
        }

        public static Button Button(Transform parent, string name, float x, float y, float w, float h, Action onClick, Action onEnter = null, Action onExit = null)
        {
            var rect = Rect(parent, name, x, y, w, h);
            Image bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = SpriteFromWhite();
            bg.color = new Color(1, 1, 1, 0.002f);
            bg.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            if (onEnter != null || onExit != null)
            {
                EventTrigger trigger = rect.gameObject.AddComponent<EventTrigger>();
                if (onEnter != null)
                {
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    entry.callback.AddListener(_ => onEnter());
                    trigger.triggers.Add(entry);
                }
                if (onExit != null)
                {
                    var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    entry.callback.AddListener(_ => onExit());
                    trigger.triggers.Add(entry);
                }
            }
            return button;
        }

        public static void RemoveChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }
    }
}
