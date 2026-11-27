using BepInEx.Configuration;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// The panel's tabs: the selected object, the level's settings and saved levels, prefabs, and the controls
    /// (with the editor's display options, including reopening the Controls window).
    /// </summary>
    public partial class LevelEditorController
    {
        public ConfigEntry<bool> ShowHelpWindow;

        private enum PanelTab { Object, Level, Prefabs, Controls }
        private static readonly string[] PanelTabNames = { "Object", "Level", "Prefabs", "Controls" };
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
            if (ShowTiming != null)
            {
                bool timing = GUILayout.Toggle(ShowTiming.Value, "Show editor timing (frame rate and the editor's share of each frame)");
                if (timing != ShowTiming.Value) ShowTiming.Value = timing;
            }
            GUILayout.EndVertical();

            Theme.Section("Snapping");
            GUILayout.BeginVertical(Theme.Card);
            DrawSnapSettings();
            GUILayout.Label("Placed and moved pieces lock flush against nearby objects' faces and line up with their edges and centres (they light up blue). Otherwise their centre snaps to the grid. Pieces resting on a surface keep their height.", Theme.Hint);
            GUILayout.EndVertical();

            DrawKeyBindings();
        }

        // ---------------- key bindings ----------------

        /// <summary>The binding waiting for a key press: an editor action's slot, or one of the two game-wide shortcuts.</summary>
        private bool _rebinding;
        private EditorAction _rebindAction;
        private int _rebindSlot;
        private ConfigEntry<KeyboardShortcut> _rebindShortcut;

        private static KeyCode[] _captureKeys;

        /// <summary>Keyboard keys that can be bound: not the mouse or pads, not Esc (cancels) and not Ctrl (the shortcut modifier).</summary>
        private static KeyCode[] CaptureKeys
        {
            get
            {
                if (_captureKeys != null) return _captureKeys;
                var list = new System.Collections.Generic.List<KeyCode>();
                foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
                {
                    if (k == KeyCode.None || k == KeyCode.Escape || k >= KeyCode.Mouse0) continue;
                    if (k == KeyCode.LeftControl || k == KeyCode.RightControl) continue;
                    list.Add(k);
                }
                return _captureKeys = list.ToArray();
            }
        }

        private void StartRebind(EditorAction action, int slot)
        {
            _rebinding = true;
            _rebindShortcut = null;
            _rebindAction = action;
            _rebindSlot = slot;
            GUIUtility.keyboardControl = 0;
        }

        private void StartRebind(ConfigEntry<KeyboardShortcut> shortcut)
        {
            _rebinding = true;
            _rebindShortcut = shortcut;
            GUIUtility.keyboardControl = 0;
        }

        /// <summary>Called from Update: takes the next key press for the binding being changed. True while waiting.</summary>
        private bool CaptureRebindKey()
        {
            if (!_rebinding) return false;
            if (!IsEditing || !_cursorMode) { _rebinding = false; return false; }
            if (Input.GetKeyDown(KeyCode.Escape)) { _rebinding = false; return true; }
            foreach (var k in CaptureKeys)
            {
                if (!Input.GetKeyDown(k)) continue;
                if (_rebindShortcut != null) _rebindShortcut.Value = new KeyboardShortcut(k);
                else EditorKeys.Set(_rebindAction, _rebindSlot, k);
                _rebinding = false;
                break;
            }
            return true;
        }

        private bool IsRebinding(EditorAction action, int slot) => _rebinding && _rebindShortcut == null && _rebindAction == action && _rebindSlot == slot;

        private void DrawKeyBindings()
        {
            Theme.Section("Keys");
            GUILayout.Label("Click a key to change it, then press the new key (Esc cancels). × clears a second key. Ctrl shortcuts keep Ctrl; the number keys always choose objects.", Theme.Hint);

            GUILayout.BeginVertical(Theme.Card);
            DrawShortcutRow("Play / edit", ToggleEditorKey);
            DrawShortcutRow("Mouse mode on / off", CursorModeKey);
            GUILayout.EndVertical();

            string group = null;
            foreach (var info in EditorKeys.All)
            {
                if (info.Group != group)
                {
                    if (group != null) GUILayout.EndVertical();
                    group = info.Group;
                    Theme.Section(group);
                    GUILayout.BeginVertical(Theme.Card);
                }
                DrawBindingRow(info);
            }
            if (group != null) GUILayout.EndVertical();

            GUILayout.Space(4);
            if (GUILayout.Button("Reset all keys to their defaults", GUILayout.Height(34)))
            {
                _rebinding = false;
                EditorKeys.ResetAll();
                ToggleEditorKey.Value = (KeyboardShortcut)ToggleEditorKey.DefaultValue;
                CursorModeKey.Value = (KeyboardShortcut)CursorModeKey.DefaultValue;
                Toast("Keys reset to their defaults");
            }

            Theme.Section("Mouse");
            GUILayout.Label(
                $"{K("Left click")} place (fly mode) or select (mouse mode){"\n"}{K("Right click")} pick up the aimed object; in mouse mode, hold to look around{"\n"}" +
                $"{K("Wheel")} size or ramp height; with {K(EditorKeys.Name(EditorAction.Fast))}: height or ramp width; with {K("Ctrl")}: pull closer / push away",
                RichLabel());
        }

        private void DrawShortcutRow(string label, ConfigEntry<KeyboardShortcut> shortcut)
        {
            if (shortcut == null) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.ExpandWidth(true));
            bool waiting = _rebinding && _rebindShortcut == shortcut;
            if (GUILayout.Button(waiting ? "Press a key…" : shortcut.Value.ToString(), waiting ? Theme.AccentButton : GUI.skin.button, GUILayout.Width(150)))
            {
                if (waiting) _rebinding = false; else StartRebind(shortcut);
            }
            GUILayout.Space(40);
            GUILayout.EndHorizontal();
        }

        private void DrawBindingRow(EditorKeys.Info info)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(info.Label, GUILayout.ExpandWidth(true));
            for (int slot = 0; slot < 2; slot++)
            {
                var key = slot == 0 ? info.Primary : info.Secondary;
                bool waiting = IsRebinding(info.Action, slot);
                string text = waiting ? "Press a key…" : key == KeyCode.None ? (slot == 0 ? "None" : "+") : EditorKeys.KeyName(key);
                if (GUILayout.Button(text, waiting ? Theme.AccentButton : slot == 1 && key == KeyCode.None ? Theme.GhostButton : GUI.skin.button, GUILayout.Width(slot == 0 ? 110 : 96)))
                {
                    if (waiting) _rebinding = false; else StartRebind(info.Action, slot);
                }
            }
            if (info.Secondary != KeyCode.None)
            {
                if (GUILayout.Button("×", Theme.GhostButton, GUILayout.Width(32))) EditorKeys.Set(info.Action, 1, KeyCode.None);
            }
            else GUILayout.Space(36);
            GUILayout.EndHorizontal();

            var clashes = EditorKeys.Conflicts(info);
            if (clashes.Count > 0)
                GUILayout.Label($"<color=#F2A65A>Same key as {string.Join(", ", clashes.ConvertAll(c => c.Label))}</color>", RichLabel());
        }
    }
}
