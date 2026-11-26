using System.Linq;
using TMPro;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// Signs: a board (the object's own cube) with TextMeshPro text on its front face, and optionally its back.
    /// The text is fitted to the board, uses the game's UI font, and is counter-scaled so a stretched board
    /// doesn't stretch the letters. Without the board the text floats and nothing is solid.
    /// </summary>
    public partial class LevelObject
    {
        private TextMeshPro _signFront, _signBack;

        private static TMP_FontAsset _signFont;
        private static Material _signMaterial;

        private void BuildSign()
        {
            _signFront = MakeSignText("TextFront");
            _signBack = MakeSignText("TextBack");
        }

        private TextMeshPro MakeSignText(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var tmp = go.AddComponent<TextMeshPro>();
            var font = SignFont;
            if (font != null) tmp.font = font;
            if (SignMaterial != null) tmp.fontSharedMaterial = SignMaterial;
            return tmp; // layout settings are applied in PlaceSignText (plain or monospace)
        }

        private void RefreshSign()
        {
            if (_signFront == null) return;
            var d = Data;
            var s = d.scale;
            float sx = Mathf.Max(0.01f, Mathf.Abs(s.x)), sy = Mathf.Max(0.01f, Mathf.Abs(s.y)), sz = Mathf.Max(0.01f, Mathf.Abs(s.z));
            // The board: solid and visible, or hidden (kept as a trigger so the editor can still click it).
            bool board = d.signBoard;
            if (_renderer != null) _renderer.enabled = board;
            if (_solid != null)
            {
                _solid.isTrigger = !board;
                if (!board) _solid.gameObject.layer = GameBridge.MarkerLayer; // never ground, like the Start marker
            }

            var colour = d.textColor;
            colour.a = 1f;
            if (_isGhost) colour = Ghostly(colour);
            // Fill the face with a margin; text never grows past the chosen share of a full-height line.
            var size = new Vector2(Mathf.Max(0.1f, sx - Mathf.Min(sx, sy) * 0.15f), Mathf.Max(0.1f, sy - Mathf.Min(sx, sy) * 0.15f));
            float maxFont = Mathf.Max(0.5f, 10f * size.y * Mathf.Clamp(d.textSize, 0.1f, 1f));
            float face = board ? 0.5f + 0.01f / sz : 0f;
            PlaceSignText(_signFront, new Vector3(0f, 0f, -face), Quaternion.identity, sx, sy, sz, size, maxFont, colour, true);
            PlaceSignText(_signBack, new Vector3(0f, 0f, face), Quaternion.Euler(0f, 180f, 0f), sx, sy, sz, size, maxFont, colour, d.signBothSides);
        }

        private void PlaceSignText(TextMeshPro tmp, Vector3 localPos, Quaternion localRot, float sx, float sy, float sz,
                                   Vector2 size, float maxFont, Color colour, bool show)
        {
            tmp.gameObject.SetActive(show);
            if (!show) return;
            var t = tmp.transform;
            t.localPosition = localPos;
            t.localRotation = localRot;
            // Undo the board's scale so the letters keep their shape; the text box is sized in metres instead.
            t.localScale = new Vector3(1f / sx, 1f / sy, 1f / sz);
            tmp.color = colour;
            if (Data.signMono) { LayOutMonospace(tmp, size); return; }
            tmp.richText = false;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.enableAutoSizing = true;
            tmp.rectTransform.sizeDelta = size;
            tmp.fontSizeMin = 0.1f;
            tmp.fontSizeMax = maxFont;
            string text = string.IsNullOrEmpty(Data.text) ? " " : Data.text;
            if (tmp.text != text) tmp.text = text;
        }

        /// <summary>The share of a letter's height each monospaced column takes (TMP's mspace, in em).</summary>
        private const string MonoTag = "<mspace=0.6em>";

        /// <summary>
        /// ASCII art: every character (spaces too) the same width, lines left-aligned and never wrapped. The text box
        /// is sized to the whole character grid (longest line by line count) and centred on the board, so two signs of
        /// the same size holding art of the same width line up when stacked.
        /// </summary>
        private void LayOutMonospace(TextMeshPro tmp, Vector2 space)
        {
            string body = (Data.text ?? "").Replace("\r", "").Replace("\t", "    ");
            var lines = body.Split('\n');
            int cols = Mathf.Max(1, lines.Max(l => l.Length));
            int rows = Mathf.Max(1, lines.Length);

            tmp.richText = true; // only for the monospace tag; the text itself goes inside <noparse>
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = false;
            tmp.fontSize = 1f;
            // Measure one column and one line at size 1, then scale the grid to fill the board.
            float colW = tmp.GetPreferredValues(MonoTag + new string('M', cols)).x;
            float rowH = tmp.GetPreferredValues(MonoTag + string.Join("\n", Enumerable.Repeat("M", rows))).y;
            float fit = colW > 0f && rowH > 0f ? Mathf.Min(space.x / colW, space.y / rowH) : 1f;
            float font = Mathf.Max(0.01f, fit * Mathf.Clamp(Data.textSize, 0.1f, 1f));
            tmp.fontSize = font;
            tmp.rectTransform.sizeDelta = new Vector2(colW * font * 1.01f, rowH * font);
            string text = MonoTag + "<noparse>" + body.Replace("</noparse>", "") + "</noparse>";
            if (tmp.text != text) tmp.text = text;
        }

        /// <summary>The font of the game's own UI text (the most used one on screen), so signs match the game.</summary>
        private static TMP_FontAsset SignFont
        {
            get
            {
                if (_signFont != null) return _signFont;
                var used = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>()
                    .Where(t => t != null && t.font != null && t.gameObject.scene.IsValid())
                    .GroupBy(t => t.font)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();
                _signFont = used ?? TMP_Settings.defaultFontAsset ?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
                return _signFont;
            }
        }

        /// <summary>
        /// The font's material, but drawn in the world: a UI font may use an "Overlay" shader that would show
        /// the text through walls, so that one is swapped for the plain distance-field shader.
        /// </summary>
        private static Material SignMaterial
        {
            get
            {
                if (_signMaterial != null) return _signMaterial;
                var font = SignFont;
                if (font == null || font.material == null) return null;
                var mat = font.material;
                if (mat.shader != null && mat.shader.name.Contains("Overlay"))
                {
                    var plain = Shader.Find("TextMeshPro/Distance Field");
                    if (plain != null)
                    {
                        mat = new Material(mat) { name = "LE_SignText" };
                        mat.shader = plain;
                    }
                }
                _signMaterial = mat;
                return _signMaterial;
            }
        }
    }
}
