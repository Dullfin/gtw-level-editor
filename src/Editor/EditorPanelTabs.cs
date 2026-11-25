using BepInEx.Configuration;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// The panel's tabs: the selected object, the level's settings, saved levels, and the controls
    /// (with the editor's display options, including reopening the Controls window).
    /// </summary>
    public partial class LevelEditorController
    {
        public ConfigEntry<bool> ShowHelpWindow;

        private enum PanelTab { Object, Level, Saved, Controls }
        private static readonly string[] PanelTabNames = { "Object", "Level", "Saved", "Controls" };
        private PanelTab _panelTab = PanelTab.Object;

        /// <summary>Whether the floating Controls window shows while editing (remembered in the config).</summary>
        private bool HelpVisible
        {
            get => ShowHelpWindow == null || ShowHelpWindow.Value;
            set { if (ShowHelpWindow != null) ShowHelpWindow.Value = value; }
        }

        private void DrawPanelTabs()
        {
            int tab = GUILayout.Toolbar((int)_panelTab, PanelTabNames, Theme.Segment);
            if (tab != (int)_panelTab) SetPanelTab((PanelTab)tab);
            GUILayout.Space(6);
        }

        private void SetPanelTab(PanelTab tab)
        {
            if (tab == _panelTab) return;
            _panelTab = tab;
            _scroll = Vector2.zero;
            GUIUtility.keyboardControl = 0;
        }

        private void DrawControlsTab()
        {
            Theme.Section("Display");
            GUILayout.BeginVertical(Theme.Card);
            bool help = GUILayout.Toggle(HelpVisible, "Show the Controls window");
            if (help != HelpVisible) HelpVisible = help;
            if (HelpVisible)
            {
                bool compact = GUILayout.Toggle(HelpCompact.Value, "Compact Controls window (one line)");
                if (compact != HelpCompact.Value) HelpCompact.Value = compact;
            }
            if (DockedLayout != null)
            {
                bool dock = GUILayout.Toggle(DockedLayout.Value, "Docked layout (toolbar on top, panel on the right)");
                if (dock != DockedLayout.Value) DockedLayout.Value = dock;
            }
            bool hide = GUILayout.Toggle(HidePlayerInEditor.Value, "Hide my character while editing (H)");
            if (hide != HidePlayerInEditor.Value) HidePlayerInEditor.Value = hide;
            GUILayout.EndVertical();

            const string line = "\n";
            Theme.Section("Anywhere");
            GUILayout.Label($"{K(CursorModeKey.Value.ToString())} mouse mode on / off{line}{K(ToggleEditorKey.Value.ToString())} play / edit{line}{K("G")} grid snap", RichLabel());
            Theme.Section("Placing (fly mode)");
            GUILayout.Label(PlacingControls(line), RichLabel());
            Theme.Section("Drawing a ramp (key 2)");
            GUILayout.Label(RampControls(line), RichLabel());
            Theme.Section("Mouse mode");
            GUILayout.Label(MouseModeControls(line), RichLabel());
        }
    }
}
