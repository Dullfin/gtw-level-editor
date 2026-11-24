using System.Linq;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// The editor's look: dark rounded panels, Noto Sans (the game's own UI font) and an amber
    /// accent taken from the game's menu highlight. Built once from generated textures, as a
    /// separate skin so other mods' IMGUI keeps its own look.
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Panel = new Color(0.085f, 0.09f, 0.11f, 0.95f);
        public static readonly Color PanelSoft = new Color(0.085f, 0.09f, 0.11f, 0.82f);
        public static readonly Color Surface = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color SurfaceHover = new Color(1f, 1f, 1f, 0.13f);
        public static readonly Color SurfacePress = new Color(1f, 1f, 1f, 0.19f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.09f);
        public static readonly Color Text = new Color(0.93f, 0.94f, 0.96f);
        public static readonly Color Muted = new Color(0.6f, 0.64f, 0.71f);
        public static readonly Color Accent = new Color(1f, 0.74f, 0.25f);
        public static readonly Color AccentHover = new Color(1f, 0.8f, 0.4f);
        public static readonly Color OnAccent = new Color(0.12f, 0.09f, 0.03f);
        public static readonly Color Danger = new Color(0.93f, 0.36f, 0.33f);
        public const string AccentHex = "#FFBD40";
        public const string MutedHex = "#99A3B5";

        public static GUISkin Skin { get; private set; }
        public static GUIStyle Header, Title, Hint, Big, Caption, Hud, AccentButton, DangerButton, GhostButton,
                               Segment, Row, Swatch, SwatchRing, AxisLabel, Chip, Card, Pill, Small, Close;
        public static Texture2D White;
        private static Font _regular, _bold;

        public static void Ensure()
        {
            if (Skin != null) return;
            var fonts = Resources.FindObjectsOfTypeAll<Font>();
            _regular = fonts.FirstOrDefault(f => f.name == "Core-NotoSans-Regular");
            _bold = fonts.FirstOrDefault(f => f.name == "Core-NotoSans-Bold") ?? _regular;
            White = Solid(Color.white);

            Skin = Object.Instantiate(GUI.skin);
            Skin.font = _regular;
            Skin.settings.cursorColor = Accent;
            Skin.settings.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);

            var s = Skin.window;
            Set(s, Round(Panel, 14, Line), 15, 14);
            s.onNormal.background = s.normal.background;
            s.font = _bold;
            s.fontSize = 17;
            s.alignment = TextAnchor.UpperLeft;
            s.padding = new RectOffset(18, 18, 50, 16);
            s.contentOffset = new Vector2(0f, -34f);
            Colors(s, Text);

            s = Skin.label;
            s.font = _regular;
            s.fontSize = 15;
            s.wordWrap = true;
            s.richText = true;
            s.padding = new RectOffset(2, 2, 4, 4);
            s.margin = new RectOffset(2, 2, 2, 2);
            Colors(s, Text);

            s = Skin.button;
            ButtonLike(s, Surface, SurfaceHover, SurfacePress, Text);
            s.onNormal.background = Round(Accent, 8, null);
            s.onHover.background = Round(AccentHover, 8, null);
            s.onActive.background = s.onHover.background;
            s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = OnAccent;

            s = Skin.textField;
            Set(s, Round(new Color(0f, 0f, 0f, 0.38f), 7, Line), 8, 7);
            s.focused.background = Round(new Color(0f, 0f, 0f, 0.5f), 7, Accent);
            s.hover.background = Round(new Color(0f, 0f, 0f, 0.42f), 7, new Color(1f, 1f, 1f, 0.18f));
            s.font = _regular;
            s.fontSize = 15;
            s.padding = new RectOffset(9, 9, 6, 6);
            s.margin = new RectOffset(3, 3, 3, 3);
            s.alignment = TextAnchor.MiddleLeft;
            s.clipping = TextClipping.Clip;
            Colors(s, Text);

            s = Skin.toggle;
            s.normal.background = Check(false, false);
            s.hover.background = Check(false, true);
            s.active.background = Check(false, true);
            s.onNormal.background = Check(true, false);
            s.onHover.background = s.onActive.background = Check(true, true);
            s.border = new RectOffset(26, 2, 0, 0);
            s.overflow = new RectOffset(0, 0, 0, 0);
            s.padding = new RectOffset(30, 4, 3, 3);
            s.margin = new RectOffset(2, 2, 4, 4);
            s.fixedHeight = 24;
            s.font = _regular;
            s.fontSize = 15;
            s.alignment = TextAnchor.MiddleLeft;
            Colors(s, Text);

            s = Skin.box;
            Set(s, Round(Surface, 9, null), 10, 9);
            Colors(s, Text);

            Skin.scrollView.normal.background = null;
            Skin.scrollView.padding = new RectOffset(0, 6, 0, 0);
            Scrollbar(Skin.verticalScrollbar, Skin.verticalScrollbarThumb, true);
            Scrollbar(Skin.horizontalScrollbar, Skin.horizontalScrollbarThumb, false);
            foreach (var b in new[] { Skin.verticalScrollbarUpButton, Skin.verticalScrollbarDownButton, Skin.horizontalScrollbarLeftButton, Skin.horizontalScrollbarRightButton })
            {
                b.fixedWidth = b.fixedHeight = 0f;
                b.normal.background = null;
                b.margin = new RectOffset(0, 0, 0, 0);
            }

            // ---- named styles ----
            Header = new GUIStyle(Skin.label) { font = _bold, fontSize = 12, wordWrap = false, padding = new RectOffset(2, 2, 12, 4) };
            Colors(Header, Muted);
            Title = new GUIStyle(Skin.label) { font = _bold, fontSize = 17 };
            Hint = new GUIStyle(Skin.label) { fontSize = 13 };
            Colors(Hint, Muted);
            Hud = new GUIStyle(Skin.label) { fontSize = 15, padding = new RectOffset(0, 0, 2, 2) };
            Big = new GUIStyle(Skin.label) { font = _bold, fontSize = 30, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            Colors(Big, Color.white);
            Caption = new GUIStyle(Big) { fontSize = 30 };

            AccentButton = new GUIStyle(Skin.button);
            ButtonLike(AccentButton, Accent, AccentHover, AccentHover, OnAccent);
            DangerButton = new GUIStyle(Skin.button);
            ButtonLike(DangerButton, new Color(Danger.r, Danger.g, Danger.b, 0.18f), new Color(Danger.r, Danger.g, Danger.b, 0.32f), new Color(Danger.r, Danger.g, Danger.b, 0.45f), new Color(1f, 0.72f, 0.7f));
            GhostButton = new GUIStyle(Skin.button);
            ButtonLike(GhostButton, new Color(1f, 1f, 1f, 0f), SurfaceHover, SurfacePress, Muted);
            GhostButton.hover.textColor = Text;
            Close = new GUIStyle(GhostButton) { fontSize = 24, padding = new RectOffset(0, 0, 0, 2), font = _regular };

            Segment = new GUIStyle(Skin.button) { font = _bold, fontSize = 14, margin = new RectOffset(2, 2, 2, 2), padding = new RectOffset(8, 8, 6, 6) };

            Row = new GUIStyle { normal = { background = Round(new Color(1f, 1f, 1f, 0.045f), 9, null) }, border = new RectOffset(10, 10, 10, 10),
                                 padding = new RectOffset(12, 8, 6, 6), margin = new RectOffset(0, 0, 3, 3) };
            Card = new GUIStyle(Row) { padding = new RectOffset(12, 12, 10, 12), margin = new RectOffset(0, 0, 4, 6) };

            Swatch = new GUIStyle { normal = { background = Round(Color.white, 6, null) }, border = new RectOffset(7, 7, 7, 7), margin = new RectOffset(3, 3, 3, 3), fixedWidth = 26, fixedHeight = 22 };
            SwatchRing = new GUIStyle { normal = { background = RoundRing(Color.white, 8, 2) }, border = new RectOffset(9, 9, 9, 9) };

            AxisLabel = new GUIStyle(Skin.label) { font = _bold, fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(4, 0, 3, 3) };

            Pill = new GUIStyle { normal = { background = Round(PanelSoft, 14, Line) }, border = new RectOffset(15, 15, 15, 15) };
            Small = new GUIStyle(Skin.button) { padding = new RectOffset(4, 4, 5, 5), fontSize = 16 };

            Chip = new GUIStyle(Skin.button) { font = _bold, fontSize = 12, wordWrap = false, clipping = TextClipping.Overflow, richText = true, padding = new RectOffset(4, 4, 4, 4), margin = new RectOffset(0, 0, 0, 0) };
            ButtonLike(Chip, PanelSoft, new Color(0.16f, 0.17f, 0.2f, 0.92f), new Color(0.2f, 0.21f, 0.25f, 0.95f), Text);
            Chip.onNormal.background = Chip.onHover.background = Chip.onActive.background = Round(Accent, 9, null);
            Chip.onNormal.textColor = Chip.onHover.textColor = Chip.onActive.textColor = OnAccent;
        }

        /// <summary>Text with a soft drop shadow, for HUD text over the 3D world.</summary>
        public static void ShadowLabel(Rect r, string text, GUIStyle style)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            style.normal.textColor = old;
            GUI.Label(r, text, style);
        }

        /// <summary>A section heading: small, bold, muted capitals.</summary>
        public static void Section(string text) => GUILayout.Label(text.ToUpperInvariant(), Header);

        public static string Key(string k) => $"<color={AccentHex}><b>{k}</b></color>";

        // ---------------- texture helpers ----------------

        private static void ButtonLike(GUIStyle s, Color normal, Color hover, Color press, Color text)
        {
            Set(s, Round(normal, 8, null), 9, 8);
            s.hover.background = Round(hover, 8, null);
            s.active.background = Round(press, 8, null);
            s.focused.background = s.normal.background;
            s.font = _bold;
            s.fontSize = 14;
            s.alignment = TextAnchor.MiddleCenter;
            s.padding = new RectOffset(12, 12, 7, 7);
            s.margin = new RectOffset(3, 3, 3, 3);
            Colors(s, text);
        }

        private static void Set(GUIStyle s, Texture2D bg, int border, int radius)
        {
            s.normal.background = bg;
            s.border = new RectOffset(border, border, border, border);
            s.overflow = new RectOffset(0, 0, 0, 0);
        }

        private static void Colors(GUIStyle s, Color c)
        {
            foreach (var st in new[] { s.normal, s.hover, s.active, s.focused, s.onNormal, s.onHover, s.onActive, s.onFocused })
                st.textColor = c;
        }

        private static void Scrollbar(GUIStyle bar, GUIStyle thumb, bool vertical)
        {
            bar.normal.background = null;
            bar.border = new RectOffset(0, 0, 0, 0);
            bar.margin = new RectOffset(4, 0, 2, 2);
            bar.padding = new RectOffset(0, 0, 0, 0);
            thumb.normal.background = Round(new Color(1f, 1f, 1f, 0.22f), 4, null);
            thumb.hover.background = Round(new Color(1f, 1f, 1f, 0.34f), 4, null);
            thumb.border = new RectOffset(5, 5, 5, 5);
            thumb.padding = new RectOffset(0, 0, 0, 0);
            if (vertical) { bar.fixedWidth = 8; thumb.fixedWidth = 8; }
            else { bar.fixedHeight = 8; thumb.fixedHeight = 8; }
        }

        private static Texture2D NewTex(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }

        public static Texture2D Solid(Color c)
        {
            var t = NewTex(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        /// <summary>Signed distance from a point to a rounded rectangle (negative inside).</summary>
        private static float RoundedDist(float x, float y, float w, float h, float r)
        {
            float qx = Mathf.Abs(x - w / 2f) - (w / 2f - r);
            float qy = Mathf.Abs(y - h / 2f) - (h / 2f - r);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>A rounded rectangle for 9-slicing, anti-aliased, with an optional 1 px border.</summary>
        public static Texture2D Round(Color fill, int radius, Color? border)
        {
            int size = radius * 2 + 4;
            var t = NewTex(size, size);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundedDist(x + 0.5f, y + 0.5f, size, size, radius);
                float a = Mathf.Clamp01(0.5f - d);
                var c = fill;
                if (border.HasValue)
                {
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(d + 0.75f));
                    c = Color.Lerp(fill, new Color(border.Value.r, border.Value.g, border.Value.b, Mathf.Max(fill.a, border.Value.a)), edge * border.Value.a / Mathf.Max(0.01f, Mathf.Max(fill.a, border.Value.a)));
                }
                c.a *= a;
                px[y * size + x] = c;
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Only the outline of a rounded rectangle.</summary>
        private static Texture2D RoundRing(Color color, int radius, float width)
        {
            int size = radius * 2 + 4;
            var t = NewTex(size, size);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundedDist(x + 0.5f, y + 0.5f, size, size, radius);
                float a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + width + 0.5f);
                px[y * size + x] = new Color(color.r, color.g, color.b, color.a * a);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>A checkbox on the left of a 24 px tall strip (the rest stretches transparently).</summary>
        private static Texture2D Check(bool on, bool hover)
        {
            const int w = 30, h = 24, box = 18, top = 3;
            var t = NewTex(w, h);
            var px = new Color[w * h];
            var fill = on ? (hover ? AccentHover : Accent) : new Color(0f, 0f, 0f, 0.38f);
            var edge = on ? fill : (hover ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 1f, 1f, 0.2f));
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = new Color(0f, 0f, 0f, 0f);
                if (x < box + 2)
                {
                    float d = RoundedDist(x + 0.5f - 1f, y + 0.5f - top, box, box, 5f);
                    float a = Mathf.Clamp01(0.5f - d);
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(d + 0.75f));
                    c = Color.Lerp(fill, edge, rim);
                    c.a = Mathf.Max(fill.a, rim * edge.a) * a;
                    if (on)
                    {
                        // A check mark: two strokes, in texture space (y up).
                        float lx = x + 0.5f - 1f, ly = y + 0.5f - top;
                        float s1 = DistSeg(lx, ly, 4.5f, 9.5f, 7.8f, 6f);
                        float s2 = DistSeg(lx, ly, 7.8f, 6f, 13.8f, 13f);
                        float m = Mathf.Clamp01(1.6f - Mathf.Min(s1, s2));
                        c = Color.Lerp(c, new Color(OnAccent.r, OnAccent.g, OnAccent.b, c.a), m);
                    }
                }
                px[y * w + x] = c;
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static float DistSeg(float px, float py, float ax, float ay, float bx, float by)
        {
            var p = new Vector2(px, py);
            var a = new Vector2(ax, ay);
            var ab = new Vector2(bx, by) - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
