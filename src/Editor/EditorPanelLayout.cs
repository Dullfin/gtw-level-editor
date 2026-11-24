using BepInEx.Configuration;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// The mouse-mode panel's size: drag the corner grip to resize it (remembered in the config), and
    /// rows of choices wrap onto more lines instead of making the panel scroll sideways.
    /// </summary>
    public partial class LevelEditorController
    {
        public ConfigEntry<Vector2> PanelSize;

        private const float MinPanelWidth = 400f, MinPanelHeight = 320f, GripSize = 22f;
        private int _gripControl;
        private bool _resizingPanel;

        /// <summary>Width available to the panel's content, inside the padding and the vertical scrollbar.</summary>
        private float PanelInnerWidth => Mathf.Max(200f, _window.width - 52f);

        private void ApplyPanelSize()
        {
            var size = PanelSize != null ? PanelSize.Value : new Vector2(440f, 640f);
            _window.width = Mathf.Clamp(size.x, MinPanelWidth, Mathf.Max(MinPanelWidth, VW - 20f));
            _window.height = Mathf.Clamp(size.y, MinPanelHeight, Mathf.Max(MinPanelHeight, VH - 20f));
        }

        private void BeginPanelScroll()
        {
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(PanelInnerWidth));
        }

        private void EndPanelScroll()
        {
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        /// <summary>The corner grip: drag it to resize the panel. Call last inside the window.</summary>
        private void DrawPanelGrip()
        {
            var r = new Rect(_window.width - GripSize - 4f, _window.height - GripSize - 4f, GripSize, GripSize);
            _gripControl = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = _gripControl;
                        _resizingPanel = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_resizingPanel)
                    {
                        var size = new Vector2(_window.width + e.delta.x, _window.height + e.delta.y);
                        size.x = Mathf.Clamp(size.x, MinPanelWidth, Mathf.Max(MinPanelWidth, VW - _window.x - 4f));
                        size.y = Mathf.Clamp(size.y, MinPanelHeight, Mathf.Max(MinPanelHeight, VH - _window.y - 4f));
                        _window.width = size.x;
                        _window.height = size.y;
                        if (PanelSize != null) PanelSize.Value = size;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (_resizingPanel)
                    {
                        _resizingPanel = false;
                        if (GUIUtility.hotControl == _gripControl) GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, r.Contains(e.mousePosition) || _resizingPanel ? 0.9f : 0.45f);
            for (int i = 0; i < 3; i++)
            {
                float o = 6f + i * 5f;
                GUI.DrawTexture(new Rect(r.xMax - o, r.yMax - 3f, o - 2f, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.xMax - 3f, r.yMax - o, 2f, o - 2f), Texture2D.whiteTexture);
            }
            GUI.color = old;
        }

        /// <summary>
        /// A row of mutually exclusive choices that wraps onto more lines when the panel is narrow.
        /// Returns the chosen index.
        /// </summary>
        private int SegmentRow(int selected, string[] labels, float available = -1f)
        {
            if (available <= 0f) available = PanelInnerWidth - 20f;
            float widest = 0f;
            foreach (var l in labels) widest = Mathf.Max(widest, Theme.Segment.CalcSize(new GUIContent(l)).x + 4f);
            int columns = Mathf.Clamp(Mathf.FloorToInt(available / Mathf.Max(1f, widest)), 1, labels.Length);
            // Even rows look tidier than one long row and a short one.
            int rows = Mathf.CeilToInt(labels.Length / (float)columns);
            columns = Mathf.CeilToInt(labels.Length / (float)rows);
            return GUILayout.SelectionGrid(selected, labels, columns, Theme.Segment);
        }
    }
}
