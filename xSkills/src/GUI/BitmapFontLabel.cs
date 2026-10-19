using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using xSkills.Services;
namespace xSkills.GUI
{
    // Оригинальные atlas-координаты scarab/scarab_small из xSkillsGilded
    internal sealed class BitmapFontLabel : MonoBehaviour
    {
        private Text source;
        private RectTransform holder;
        private string previousText;
        private Vector2 previousSize;
        private int previousSizeFont;
        private Color previousColor;
        private Texture2D usedTexture;
        private readonly List<RectTransform> quads = new List<RectTransform>();

        private void Awake()
        {
            source = GetComponent<Text>();
            holder = GetComponent<RectTransform>();
        }

        private void LateUpdate()
        {
            if (source == null) return;
            if (!Localizer.UseBitmapFont)
            {
                if (!source.enabled) source.enabled = true;
                if (quads.Count > 0) DestroyQuads();
                previousText = null;
                return;
            }
            bool small = source.fontSize <= 24;
            Color color = source.color;
            bool gold = color.r > .9f && color.g > .4f && color.g < .85f && color.b < .35f;
            usedTexture = ResourceLoader.Texture("fonts", small ? (gold ? "scarab_small_gold" : "scarab_small") : (gold ? "scarab_gold" : "scarab"));
            string text = source.text ?? "";
            bool supported = usedTexture != null && text.IndexOf('<') < 0;
            foreach (char ch in text) if (ch != '\n' && (ch < ' ' || ch > '~')) { supported = false; break; }
            if (!supported)
            {
                if (!source.enabled) source.enabled = true;
                if (quads.Count > 0) DestroyQuads();
                previousText = null;
                return;
            }
            source.enabled = false;
            if (text == previousText && previousSize == holder.rect.size && previousSizeFont == source.fontSize && previousColor == color) return;
            previousText = text;
            previousSize = holder.rect.size;
            previousSizeFont = source.fontSize;
            previousColor = color;
            Rebuild(text, small, gold);
        }

        private void DestroyQuads()
        {
            foreach (RectTransform rect in quads) if (rect != null) Destroy(rect.gameObject);
            quads.Clear();
        }

        private void Rebuild(string text, bool small, bool gold)
        {
            DestroyQuads();
            BitmapGlyph[] alphabet = small ? GlyphTable.Small : GlyphTable.Large;
            float fontH = small ? 20f : 27f;
            float scale = source.fontSize / fontH;
            float lineH = fontH * scale * 1.14f;
            float maxWidth = Mathf.Max(20, holder.rect.width);
            var lines = new List<string>();
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = "";
                foreach (string token in raw.Split(' '))
                {
                    string candidate = line.Length == 0 ? token : line + " " + token;
                    if (line.Length > 0 && Width(candidate, alphabet, scale) > maxWidth)
                    {
                        lines.Add(line);
                        line = token;
                    }
                    else line = candidate;
                }
                lines.Add(line);
            }
            float blockHeight = lines.Count * lineH;
            float top = 0;
            switch (source.alignment)
            {
                case TextAnchor.MiddleLeft:
                case TextAnchor.MiddleCenter:
                case TextAnchor.MiddleRight:
                    top = (holder.rect.height - blockHeight) / 2; break;
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    top = holder.rect.height - blockHeight; break;
            }
            for (int row = 0; row < lines.Count; row++)
            {
                string line = lines[row];
                float lineWidth = Width(line, alphabet, scale);
                float left = 0;
                switch (source.alignment)
                {
                    case TextAnchor.UpperCenter:
                    case TextAnchor.MiddleCenter:
                    case TextAnchor.LowerCenter: left = (holder.rect.width - lineWidth) / 2; break;
                    case TextAnchor.UpperRight:
                    case TextAnchor.MiddleRight:
                    case TextAnchor.LowerRight: left = holder.rect.width - lineWidth; break;
                }
                float advance = left;
                foreach (char ch in line)
                {
                    if (ch == ' ') { advance += 5 * scale; continue; }
                    int idx = ch - 32;
                    if (idx < 0 || idx >= alphabet.Length) continue;
                    BitmapGlyph g = alphabet[idx];
                    GameObject go = new GameObject("Glyph " + ch, typeof(RectTransform), typeof(RawImage));
                    RectTransform rect = go.GetComponent<RectTransform>();
                    rect.SetParent(transform, false);
                    rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                    rect.pivot = new Vector2(0, 1);
                    rect.anchoredPosition = new Vector2(advance, -(top + row * lineH));
                    rect.sizeDelta = new Vector2(g.W * scale, g.H * scale);
                    RawImage img = go.GetComponent<RawImage>();
                    img.texture = usedTexture;
                    img.uvRect = new Rect(g.X / (float)usedTexture.width, 1 - (g.Y + g.H) / (float)usedTexture.height,
                        g.W / (float)usedTexture.width, g.H / (float)usedTexture.height);
                    img.color = gold ? Color.white : source.color;
                    img.raycastTarget = false;
                    quads.Add(rect);
                    advance += (g.W + (small ? 1 : 2)) * scale;
                }
            }
        }

        private static float Width(string value, BitmapGlyph[] font, float scale)
        {
            float width = 0;
            foreach (char ch in value)
            {
                if (ch == ' ') width += 5 * scale;
                else if (ch >= 32 && ch <= 126) width += (font[ch - 32].W + (font[0].H <= 20 ? 1 : 2)) * scale;
            }
            return width;
        }
    }
}
