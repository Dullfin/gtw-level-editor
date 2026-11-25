using System.Collections.Generic;
using HarmonyLib;
using Isto.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GTWLevelEditor
{
    /// <summary>
    /// The game's Esc menu inside a custom level: "Level select" opens a list of our saved levels
    /// instead of the Doinkler one, and "Save splits" becomes "Save level".
    /// </summary>
    public partial class LevelEditorController
    {
        private const int PausePickerId = 0x4C48;

        private GTWGameMenuState _pauseMenu;
        private bool _pausePickerOpen;
        private Rect _pausePickerRect;
        private Vector2 _pausePickerScroll;
        /// <summary>A level chosen in the picker, loaded once the Esc menu has closed and time runs again.</summary>
        private string _pausePickerLoad;
        private bool _pausePickerLoadEdit;

        private static readonly AccessTools.FieldRef<UISimpleGameMenuState, SimpleGameMenuStateMachine> MainMenu =
            AccessTools.FieldRefAccess<UISimpleGameMenuState, SimpleGameMenuStateMachine>("_mainMenu");

        /// <summary>The Esc menu's custom buttons apply only inside a built custom level.</summary>
        private bool PauseMenuIsOurs => CustomSession.Active && _sessionReady && !DemoRunning;

        private void OpenPausePicker(GTWGameMenuState menu)
        {
            _pauseMenu = menu;
            _pausePickerOpen = true;
            _pausePickerScroll = Vector2.zero;
            RefreshLevelList();
            // Keep the game's buttons from taking clicks or keys behind our list.
            SetPauseMenuInteractive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private void ClosePausePicker()
        {
            if (!_pausePickerOpen) return;
            _pausePickerOpen = false;
            SetPauseMenuInteractive(true);
        }

        private bool _pauseMenuBlockedRaycasts = true;

        private void SetPauseMenuInteractive(bool on)
        {
            var group = _pauseMenu != null ? _pauseMenu.canvasGroup : null;
            if (group == null) return;
            if (!on) _pauseMenuBlockedRaycasts = group.blocksRaycasts;
            group.interactable = on;
            group.blocksRaycasts = on ? _pauseMenuBlockedRaycasts : false;
        }

        private void ChoosePauseLevel(string file, bool edit)
        {
            _pausePickerLoad = file;
            _pausePickerLoadEdit = edit;
            ClosePausePicker();
            var menu = _pauseMenu != null ? MainMenu(_pauseMenu) : null;
            if (menu != null) menu.CloseMenu();
        }

        /// <summary>Called from Update: loads the chosen level once the Esc menu has let time run again.</summary>
        private void RunPausePickerLoad()
        {
            if (_pausePickerLoad == null || Time.timeScale <= 0f || !PauseMenuIsOurs) return;
            string file = _pausePickerLoad;
            _pausePickerLoad = null;
            if (_pausePickerLoadEdit && !IsEditing) EnterEditor();
            else if (!_pausePickerLoadEdit && IsEditing) ExitEditor();
            LoadLevel(file);
        }

        private void DrawPausePicker()
        {
            if (!_pausePickerOpen) return;
            if (_pauseMenu == null || !_pauseMenu.isActiveAndEnabled || !PauseMenuIsOurs)
            {
                ClosePausePicker();
                return;
            }
            _pausePickerRect.width = Mathf.Min(640f, VW - 40f);
            _pausePickerRect.height = Mathf.Min(560f, VH - 80f);
            _pausePickerRect.position = new Vector2((VW - _pausePickerRect.width) / 2f, (VH - _pausePickerRect.height) / 2f);
            GUI.Window(PausePickerId, _pausePickerRect, DrawPausePickerWindow, "Level select");
            GUI.BringWindowToFront(PausePickerId);
            GUI.FocusWindow(PausePickerId);
        }

        private void DrawPausePickerWindow(int id)
        {
            var r = _pausePickerRect;
            if (GUI.Button(new Rect(r.width - 48, 12, 32, 30), "×", Theme.Close)) { ClosePausePicker(); return; }

            GUILayout.BeginArea(new Rect(16, 52, r.width - 32, r.height - 68));
            GUILayout.Label("Loading another level replaces the one you have open. Use Save level first to keep your changes.", Theme.Hint);
            GUILayout.Space(4);
            _pausePickerScroll = GUILayout.BeginScrollView(_pausePickerScroll, GUILayout.ExpandHeight(true));
            if (_levelFiles.Length == 0) GUILayout.Label("No saved levels yet.", Theme.Hint);
            foreach (var file in _levelFiles)
            {
                GUILayout.BeginHorizontal(Theme.Row);
                bool open = string.Equals(file, _level?.name, System.StringComparison.OrdinalIgnoreCase);
                GUILayout.Label(open ? $"{file}  <color={Theme.MutedHex}>(open)</color>" : file, RichLabel(), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Play", Theme.AccentButton, GUILayout.Width(70))) ChoosePauseLevel(file, edit: false);
                if (GUILayout.Button("Edit", GUILayout.Width(70))) ChoosePauseLevel(file, edit: true);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.Space(6);
            if (GUILayout.Button("Back", GUILayout.Height(36))) ClosePausePicker();
            GUILayout.EndArea();
        }

        // ---------------- the game's Esc menu ----------------

        /// <summary>The game's labels, put back on its buttons outside custom levels.</summary>
        private static readonly Dictionary<int, string> OriginalLabels = new Dictionary<int, string>();

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null) return;
            foreach (var mb in button.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == "Localize") mb.enabled = label == null; // or it puts the game's text back
            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                int key = text.GetInstanceID();
                if (label != null)
                {
                    if (!OriginalLabels.ContainsKey(key)) OriginalLabels[key] = text.text;
                    text.text = label;
                }
                else if (OriginalLabels.TryGetValue(key, out var original))
                {
                    text.text = original;
                    OriginalLabels.Remove(key);
                }
            }
        }

        private static void ShowButton(Button button)
        {
            if (button == null) return;
            button.gameObject.SetActive(true);
            button.interactable = true;
        }

        [HarmonyPatch(typeof(GTWGameMenuState), "UpdateActiveButtons")]
        private static class RelabelButtons
        {
            private static void Postfix(GTWGameMenuState __instance)
            {
                var editor = Instance;
                bool ours = editor != null && editor.PauseMenuIsOurs;
                SetButtonLabel(__instance.saveSplitsButton, ours ? "SAVE LEVEL" : null);
                if (!ours) return;
                ShowButton(__instance.saveSplitsButton);
                ShowButton(__instance.levelSelectButton);
            }
        }

        [HarmonyPatch(typeof(GTWGameMenuState), nameof(GTWGameMenuState.Button_OnSaveSplits))]
        private static class SaveLevelButton
        {
            private static bool Prefix()
            {
                var editor = Instance;
                if (editor == null || !editor.PauseMenuIsOurs) return true;
                editor.SaveLevel();
                return false;
            }
        }

        [HarmonyPatch(typeof(GTWGameMenuState), nameof(GTWGameMenuState.Button_OnLevelSelect))]
        private static class LevelSelectButton
        {
            private static bool Prefix(GTWGameMenuState __instance)
            {
                var editor = Instance;
                if (editor == null || !editor.PauseMenuIsOurs) return true;
                editor.OpenPausePicker(__instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(GTWGameMenuState), nameof(GTWGameMenuState.Exit))]
        private static class MenuClosed
        {
            private static void Postfix()
            {
                var editor = Instance;
                if (editor != null) editor.ClosePausePicker();
            }
        }
    }
}
