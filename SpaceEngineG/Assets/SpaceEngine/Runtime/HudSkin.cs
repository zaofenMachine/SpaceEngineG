using UnityEngine;

namespace SpaceEngine.Runtime
{
    /// <summary>
    /// Shared IMGUI look: translucent rounded panels, compact chips and shadowed overlay text,
    /// so the view stays the subject and controls recede. All sizes are in the 1600x900 canvas.
    /// </summary>
    public static class HudSkin
    {
        public static readonly Color Ink = new Color(.87f, .92f, .95f);
        public static readonly Color Muted = new Color(.56f, .65f, .71f);
        public static readonly Color Faint = new Color(.5f, .58f, .64f);
        public static readonly Color Accent = new Color(.42f, .87f, .96f);
        public static readonly Color Warning = new Color(.98f, .78f, .38f);

        static Texture2D panel, panelEdge, chip, chipHover, chipActive, chipOn, field, white;
        static GUIStyle label, small, tiny, title, number, chipStyle, chipOnStyle, chipLeft, chipLeftOn, input, panelStyle, edgeStyle;
        static Font font;

        public static GUIStyle Label { get { Build(); return label; } }
        public static GUIStyle Small { get { Build(); return small; } }
        public static GUIStyle Tiny { get { Build(); return tiny; } }
        public static GUIStyle Title { get { Build(); return title; } }
        public static GUIStyle Number { get { Build(); return number; } }
        public static GUIStyle Chip { get { Build(); return chipStyle; } }
        public static GUIStyle ChipOn { get { Build(); return chipOnStyle; } }
        public static GUIStyle ChipLeft { get { Build(); return chipLeft; } }
        public static GUIStyle ChipLeftOn { get { Build(); return chipLeftOn; } }
        public static GUIStyle Input { get { Build(); return input; } }

        /// <summary>A rounded, translucent panel with a faint rim.</summary>
        public static void Panel(Rect rect, float opacity = .66f)
        {
            Build();
            if (Event.current.type != EventType.Repaint) return;
            Color previous = GUI.color;
            GUI.color = new Color(previous.r, previous.g, previous.b, previous.a * opacity);
            panelStyle.Draw(rect, false, false, false, false);
            GUI.color = previous;
            edgeStyle.Draw(rect, false, false, false, false);
        }

        public static void Fill(Rect rect, Color color)
        {
            Build();
            Color previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, white); GUI.color = previous;
        }

        /// <summary>Overlay text readable on bright planets and dark space alike.</summary>
        public static void Shadowed(Rect rect, string text, GUIStyle style)
        {
            Color previous = style.normal.textColor;
            style.normal.textColor = new Color(0, 0, 0, .75f);
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, style);
            style.normal.textColor = previous;
            GUI.Label(rect, text, style);
        }

        public static bool Button(Rect rect, string text, bool on = false, bool left = false)
        {
            Build();
            return GUI.Button(rect, text, left ? (on ? chipLeftOn : chipLeft) : (on ? chipOnStyle : chipStyle));
        }

        static void Build()
        {
            if (label != null && panel != null) return;
            font = ChineseText.Font;
            white = Texture2D.whiteTexture;
            panel = Rounded(new Color(.016f, .03f, .045f, 1), new Color(.016f, .03f, .045f, 1), 6);
            panelEdge = Rounded(new Color(0, 0, 0, 0), new Color(.42f, .66f, .74f, .28f), 6);
            chip = Rounded(new Color(.06f, .11f, .15f, .74f), new Color(.36f, .58f, .66f, .22f), 5);
            chipHover = Rounded(new Color(.1f, .19f, .25f, .88f), new Color(.45f, .8f, .9f, .5f), 5);
            chipActive = Rounded(new Color(.14f, .28f, .35f, .95f), new Color(.5f, .9f, 1, .7f), 5);
            chipOn = Rounded(new Color(.08f, .2f, .26f, .9f), new Color(.42f, .87f, .96f, .75f), 5);
            field = Rounded(new Color(.01f, .02f, .03f, .9f), new Color(.42f, .66f, .74f, .45f), 4);

            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, richText = false, clipping = TextClipping.Clip };
            label.normal.textColor = Ink;
            small = new GUIStyle(label) { fontSize = 12 }; small.normal.textColor = Muted;
            tiny = new GUIStyle(label) { fontSize = 11 }; tiny.normal.textColor = Faint;
            title = new GUIStyle(label) { fontSize = 16, fontStyle = FontStyle.Bold }; title.normal.textColor = Color.white;
            number = new GUIStyle(label) { fontSize = 28 }; number.normal.textColor = Accent;
            chipStyle = new GUIStyle(GUI.skin.button)
            {
                font = font, fontSize = 13, alignment = TextAnchor.MiddleCenter, border = new RectOffset(6, 6, 6, 6),
                padding = new RectOffset(8, 8, 2, 2), margin = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip
            };
            Skin(chipStyle, chip, chipHover, chipActive, Ink);
            chipOnStyle = new GUIStyle(chipStyle);
            Skin(chipOnStyle, chipOn, chipHover, chipActive, Accent);
            chipLeft = new GUIStyle(chipStyle) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 6, 2, 2) };
            chipLeftOn = new GUIStyle(chipOnStyle) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 6, 2, 2) };
            panelStyle = new GUIStyle { border = new RectOffset(8, 8, 8, 8) }; panelStyle.normal.background = panel;
            edgeStyle = new GUIStyle { border = new RectOffset(8, 8, 8, 8) }; edgeStyle.normal.background = panelEdge;
            input = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 14, padding = new RectOffset(8, 8, 4, 4), border = new RectOffset(5, 5, 5, 5) };
            input.normal.background = input.hover.background = input.focused.background = input.active.background = field;
            input.normal.textColor = input.hover.textColor = input.focused.textColor = Ink;
        }

        static void Skin(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active, Color text)
        {
            style.normal.background = normal; style.hover.background = hover;
            style.active.background = active; style.focused.background = normal;
            style.onNormal.background = normal; style.onHover.background = hover; style.onActive.background = active;
            style.normal.textColor = text; style.focused.textColor = text; style.onNormal.textColor = text;
            style.hover.textColor = Color.white; style.onHover.textColor = Color.white;
            style.active.textColor = Accent; style.onActive.textColor = Accent;
        }

        /// <summary>Anti-aliased rounded rectangle with a one-pixel rim, for 9-slice drawing.</summary>
        static Texture2D Rounded(Color fill, Color rim, float radius)
        {
            const int size = 24;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HUD rounded", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = x + .5f, py = y + .5f;
                float dx = Mathf.Max(0, Mathf.Max(radius - px, px - (size - radius)));
                float dy = Mathf.Max(0, Mathf.Max(radius - py, py - (size - radius)));
                float distance = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                float edge = Mathf.Min(Mathf.Min(px, size - px), Mathf.Min(py, size - py));
                float inside = Mathf.Clamp01(.5f - distance);
                float rimWeight = (dx > 0 && dy > 0) ? Mathf.Clamp01(1 - Mathf.Abs(distance + .8f)) : Mathf.Clamp01(1.6f - edge);
                Color color = Color.Lerp(fill, rim, rim.a > 0 ? rimWeight : 0);
                color.a = Mathf.Max(fill.a, rim.a * rimWeight) * inside;
                pixels[y * size + x] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
