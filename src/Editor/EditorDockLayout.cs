using BepInEx.Configuration;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// The docked layout: while editing, the object bar sits in a toolbar across the top, the
    /// panel fills a column down the right, and the game draws only into the space left over.
    /// Both stay on screen in fly mode too (the panel only takes clicks in mouse mode).
    /// </summary>
    public partial class LevelEditorController
    {
        public ConfigEntry<bool> DockedLayout;

        private const float ToolbarHeight = 74f, ToolbarPad = 10f;
        private bool _cameraConfined;
        private Texture2D _dockBackground, _dockEdge;

        /// <summary>True while the docked layout is in use: editing a ready custom level, outside the demo.</summary>
        private bool Docked => DockedLayout != null && DockedLayout.Value && IsEditing && CustomSession.Active && _sessionReady
                               && !DemoRunning && GameBridge.Player != null;

        /// <summary>The panel's width when docked: the remembered panel width, at most half the screen.</summary>
        private float DockMenuWidth => Mathf.Clamp(PanelSize != null ? PanelSize.Value.x : 440f, MinPanelWidth, Mathf.Max(MinPanelWidth, VW * 0.5f));

        private Rect DockMenuRect => new Rect(VW - DockMenuWidth, 0f, DockMenuWidth, VH);
        private Rect DockToolbarRect => new Rect(0f, 0f, VW - DockMenuWidth, ToolbarHeight);
        /// <summary>Where the game draws, in GUI units.</summary>
        private Rect GameViewRect => Docked ? new Rect(0f, ToolbarHeight, VW - DockMenuWidth, VH - ToolbarHeight) : new Rect(0f, 0f, VW, VH);

        /// <summary>Shrinks the camera's viewport to the game area while docked, and restores it afterwards.</summary>
        private void UpdateDockedCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (Docked)
            {
                var r = new Rect(0f, 0f, (VW - DockMenuWidth) / VW, (VH - ToolbarHeight) / VH);
                if (cam.rect != r) cam.rect = r;
                _cameraConfined = true;
            }
            else if (_cameraConfined)
            {
                cam.rect = new Rect(0f, 0f, 1f, 1f);
                _cameraConfined = false;
            }
        }

        /// <summary>Opaque backgrounds for the toolbar and the panel column (the camera no longer draws there).</summary>
        private void DrawDockBackgrounds()
        {
            if (_dockBackground == null) _dockBackground = SolidTexture(new Color(0.11f, 0.12f, 0.14f, 1f));
            if (_dockEdge == null) _dockEdge = SolidTexture(new Color(1f, 1f, 1f, 0.08f));
            var bar = DockToolbarRect;
            var menu = DockMenuRect;
            GUI.DrawTexture(bar, _dockBackground);
            GUI.DrawTexture(menu, _dockBackground);
            GUI.DrawTexture(new Rect(bar.x, bar.yMax - 1f, bar.width, 1f), _dockEdge);
            GUI.DrawTexture(new Rect(menu.x, 0f, 1f, menu.height), _dockEdge);
        }

        private const float DividerWidth = 10f;
        private int _dividerControl;
        private bool _draggingDivider;

        /// <summary>The strip just left of the panel column that you drag to change the panel's width.</summary>
        private Rect DividerRect => new Rect(DockMenuRect.x - DividerWidth, ToolbarHeight, DividerWidth, VH - ToolbarHeight);

        /// <summary>The divider between the game view and the panel: drag it left or right to resize the panel.</summary>
        private void DrawDockDivider()
        {
            var r = DividerRect;
            _dividerControl = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (_cursorMode && e.button == 0 && r.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = _dividerControl;
                        _draggingDivider = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_draggingDivider)
                    {
                        float width = Mathf.Clamp(VW - e.mousePosition.x - DividerWidth / 2f, MinPanelWidth, Mathf.Max(MinPanelWidth, VW * 0.5f));
                        var size = PanelSize.Value;
                        size.x = Mathf.Round(width);
                        PanelSize.Value = size;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (_draggingDivider)
                    {
                        _draggingDivider = false;
                        if (GUIUtility.hotControl == _dividerControl) GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
            if (e.type != EventType.Repaint) return;
            bool hot = _draggingDivider || (_cursorMode && r.Contains(e.mousePosition));
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, hot ? 0.5f : 0.12f);
            GUI.DrawTexture(new Rect(r.center.x - (hot ? 1.5f : 1f), r.y, hot ? 3f : 2f, r.height), Texture2D.whiteTexture);
            // A small grip in the middle so it reads as draggable.
            for (int i = -1; i <= 1; i++)
                GUI.DrawTexture(new Rect(r.center.x - 3f, r.center.y + i * 8f - 1f, 6f, 2f), Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>The panel, filling the right-hand column.</summary>
        private void DrawDockedPanel()
        {
            _window = DockMenuRect;
            GUILayout.Window(0x4C45, _window, DrawWindow, "Level Editor", GUILayout.Width(_window.width), GUILayout.Height(_window.height));
        }
    }
}
