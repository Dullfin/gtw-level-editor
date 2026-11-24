using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>Import, export and delete for the saved levels on the title screen menu.</summary>
    public partial class LevelEditorController
    {
        private string _titleNote;
        private string _confirmDelete;
        // File dialogs are modal, so they run from Update rather than in the middle of drawing the GUI.
        private Action _pendingFileAction;

        private void RunPendingFileAction()
        {
            if (_pendingFileAction == null) return;
            var action = _pendingFileAction;
            _pendingFileAction = null;
            try { action(); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Level file action failed: " + e);
                SetTitleError("Something went wrong: " + e.Message);
            }
        }

        private void SetTitleNote(string note) { _titleNote = note; _titleError = null; }
        private void SetTitleError(string error) { _titleError = error; _titleNote = null; }

        private void ImportLevel()
        {
            var path = FileDialogs.OpenLevel("Import a level");
            if (path == null) return;

            string text = File.ReadAllText(path);
            LevelData data;
            try { data = LevelJson.LevelFromJson(text); }
            catch (Exception) { data = null; }
            if (data == null || data.objects == null || data.objects.Count == 0)
            {
                SetTitleError(Path.GetFileName(path) + " isn't a level file.");
                return;
            }

            Directory.CreateDirectory(LevelsDirectory);
            if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(path)), Path.GetFullPath(LevelsDirectory), StringComparison.OrdinalIgnoreCase))
            {
                SetTitleNote(Path.GetFileNameWithoutExtension(path) + " is already in your levels.");
                return;
            }

            string name = UniqueLevelName(Path.GetFileNameWithoutExtension(path));
            File.WriteAllText(PathFor(name), text);
            RefreshLevelList();
            SetTitleNote($"Imported {name} ({data.objects.Count} objects).");
            Plugin.Log.LogInfo($"Imported level {path} as {PathFor(name)}");
        }

        private void ExportLevel(string name)
        {
            var source = PathFor(name);
            if (!File.Exists(source)) { SetTitleError(name + " no longer exists."); RefreshLevelList(); return; }
            var path = FileDialogs.SaveLevel("Export " + name, Path.GetFileName(source));
            if (path == null) return;
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
            {
                SetTitleNote(name + " is already saved there.");
                return;
            }
            File.Copy(source, path, overwrite: true);
            SetTitleNote("Exported to " + path);
            Plugin.Log.LogInfo($"Exported level {source} to {path}");
        }

        private void DeleteLevel(string name)
        {
            _confirmDelete = null;
            var path = PathFor(name);
            if (FileDialogs.Recycle(path))
                SetTitleNote($"Deleted {name}. It's in the Recycle Bin if you need it back.");
            else
                SetTitleError("Could not delete " + name + ".");
            RefreshLevelList();
        }

        private void OpenLevelsFolder()
        {
            Directory.CreateDirectory(LevelsDirectory);
            Process.Start("explorer.exe", "\"" + Path.GetFullPath(LevelsDirectory) + "\"");
        }

        /// <summary>The name itself if no saved level uses it, otherwise "name (2)", "name (3)"...</summary>
        private string UniqueLevelName(string name)
        {
            if (!File.Exists(PathFor(name))) return name;
            for (int i = 2; ; i++)
            {
                var candidate = $"{name} ({i})";
                if (!File.Exists(PathFor(candidate))) return candidate;
            }
        }

        /// <summary>One row of the saved-level list: play, edit, export and delete (with a confirm step).</summary>
        private void DrawSavedLevelRow(string file)
        {
            GUILayout.BeginHorizontal(Theme.Row);
            if (_confirmDelete == file)
            {
                GUILayout.Label($"Delete {file}?", Theme.Title, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Delete", Theme.DangerButton, GUILayout.Width(84)))
                    _pendingFileAction = () => DeleteLevel(file);
                if (GUILayout.Button("Cancel", GUILayout.Width(84))) _confirmDelete = null;
            }
            else
            {
                GUILayout.Label(file, Theme.Title, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Play", Theme.AccentButton, GUILayout.Width(70))) BeginFromFile(file, edit: false);
                if (GUILayout.Button("Edit", GUILayout.Width(70))) BeginFromFile(file, edit: true);
                if (GUILayout.Button("Export", Theme.GhostButton, GUILayout.Width(78)))
                    _pendingFileAction = () => ExportLevel(file);
                if (GUILayout.Button("Delete", Theme.DangerButton, GUILayout.Width(78))) _confirmDelete = file;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawLevelFileButtons()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Import level…", GUILayout.Height(34))) _pendingFileAction = ImportLevel;
            if (GUILayout.Button("Open levels folder", Theme.GhostButton, GUILayout.Height(34))) _pendingFileAction = OpenLevelsFolder;
            GUILayout.EndHorizontal();
        }

        private void DrawTitleMessages()
        {
            if (!string.IsNullOrEmpty(_titleError))
                GUILayout.Label($"<color=#FF8A80>{_titleError}</color>", Theme.Hint);
            else if (!string.IsNullOrEmpty(_titleNote))
                GUILayout.Label($"<color=#9BE7A6>{_titleNote}</color>", Theme.Hint);
        }
    }
}
