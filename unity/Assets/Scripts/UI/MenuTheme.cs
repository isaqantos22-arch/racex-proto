using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RaceX.UI
{
    public static class MenuTheme
    {
        public static readonly Color Accent = new Color(1f, 0.4784f, 0.102f);
        public static readonly Color AccentDim = new Color(0.62f, 0.3f, 0.06f);
        public static readonly Color Bg = new Color(0.048f, 0.055f, 0.078f);
        public static readonly Color Panel = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Text = new Color(0.92f, 0.93f, 0.95f);
        public static readonly Color TextDim = new Color(0.52f, 0.55f, 0.6f);

        public static Button StyledButton(RectTransform parent, string name, string label, Vector2 anchorRef, Vector2 anchorSelf, Vector2 pos, Vector2 size, int font, Action onClick)
        {
            var img = UIFactory.Panel(name, parent, anchorRef, anchorSelf, pos, size, Panel);
            var btn = img.gameObject.AddComponent<Button>();
            var cb = btn.colors;
            var plain = Color.white;
            cb.normalColor = plain; cb.highlightedColor = plain; cb.pressedColor = plain; cb.selectedColor = plain;
            cb.disabledColor = plain; cb.colorMultiplier = 1f; cb.fadeDuration = 0f;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var lineImg = UIFactory.Panel(name + "_Line", img.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 2f), new Vector2(size.x - 4f, 3f), Accent);
            lineImg.raycastTarget = false;
            var labelT = UIFactory.Label(name + "_Label", img.transform, Spaced(label), font, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), size, Text);
            labelT.raycastTarget = false;

            var trig = img.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            enter.callback.AddListener(_ => labelT.color = btn.interactable ? Accent : TextDim);
            exit.callback.AddListener(_ => labelT.color = btn.interactable ? Text : TextDim);
            trig.triggers.Add(enter);
            trig.triggers.Add(exit);
            return btn;
        }

        public static void Header(RectTransform parent, string title)
        {
            float w = parent.rect.width > 10f ? parent.rect.width : 1800f;
            UIFactory.Label("HeaderTitle", parent, title.ToUpperInvariant(), 58, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -36f), new Vector2(w - 160f, 80f), Text);
            UIFactory.Panel("HeaderLine", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -122f), new Vector2(w - 80f, 3f), Accent);
        }

        public static void SubHeader(RectTransform parent, string title)
        {
            float w = parent.rect.width > 10f ? parent.rect.width : 1800f;
            UIFactory.Label("SubHeaderTitle", parent, title.ToUpperInvariant(), 34, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -24f), new Vector2(w - 160f, 48f), TextDim);
            UIFactory.Panel("SubHeaderLine", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -76f), new Vector2(w - 80f, 2f), AccentDim);
        }

        public static void StatBar(RectTransform parent, string label, float t)
        {
            var labelT = UIFactory.Label("StatLabel_" + label, parent, label.ToUpperInvariant(), 20, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(140f, 26f), TextDim);
            labelT.raycastTarget = false;
            var bg = UIFactory.Panel("StatBar_" + label, parent, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(160f, 14f), new Color(1f, 1f, 1f, 0.1f));
            bg.raycastTarget = false;
            var fill = UIFactory.Panel("StatFill_" + label, bg.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(160f * Mathf.Clamp01(t), 14f), Accent);
            fill.raycastTarget = false;
        }

        public static Sprite RoundedSprite(int w, int h, int radius)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float r = Mathf.Max(1f, radius);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int cx = Mathf.Min(x, w - 1 - x);
                    int cy = Mathf.Min(y, h - 1 - y);
                    float a = 1f;
                    if (cx < r && cy < r)
                    {
                        float dx = r - (cx + 0.5f);
                        float dy = r - (cy + 0.5f);
                        a = Mathf.Clamp01(r + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    }
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        static string Spaced(string s)
        {
            s = s.ToUpperInvariant();
            return s.Length <= 10 ? string.Join("\u2009", s.ToCharArray()) : s;
        }
    }
}
