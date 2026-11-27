using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// Prefabs: a saved set of pieces that can be placed into any level. Each is a small file in the Prefabs
    /// folder (or a folder inside it), in the same format as a level, with the pieces stored around the
    /// bottom-centre of their box so a placed copy sits on the surface in the middle of the view.
    /// The Prefabs tab shows the open folder as a grid of pictures (see EditorPrefabThumbs) with its sub-folders
    /// first; prefabs and folders can be renamed, prefabs moved between folders, and either deleted.
    /// </summary>
    public partial class LevelEditorController
    {
        public string PrefabsDirectory;

        private class PrefabEntry
        {
            public string Name;  // file name without .json
            public string Rel;   // folder/name, for messages
            public string Path;  // full path of the file
            public DateTime Stamp;
        }

        /// <summary>The open folder, relative to the Prefabs folder with '/' separators; "" is the top.</summary>
        private string _prefabFolder = "";
        private List<string> _prefabSubfolders = new List<string>();
        private List<PrefabEntry> _prefabEntries = new List<PrefabEntry>();
        private List<string> _allPrefabFolders = new List<string>();
        private Dictionary<string, int> _folderCounts = new Dictionary<string, int>();

        private string _prefabName = "";
        private bool _newFolderOpen;
        private string _newFolderName = "";

        /// <summary>The prefab or folder whose actions (rename, move, delete) are open, by full path.</summary>
        private string _prefabEdit;
        private bool _prefabEditIsFolder;
        private string _prefabRename = "";
        private bool _prefabConfirmDelete;

        private GUIStyle _tileName, _tileNote;

        /// <summary>
        /// What a click in the tab asked for, run once the tab has finished drawing: changing the lists or the
        /// edit card part-way through a GUI pass would leave IMGUI's layout out of step.
        /// </summary>
        private Action _prefabDo;
        private void Later(Action action) => _prefabDo = action;

        // ---------------- paths and listing ----------------

        private string FolderPath(string rel) =>
            string.IsNullOrEmpty(rel) ? PrefabsDirectory : Path.Combine(PrefabsDirectory, rel.Replace('/', Path.DirectorySeparatorChar));

        private static string JoinRel(string folder, string name) => string.IsNullOrEmpty(folder) ? name : folder + "/" + name;

        private static string ParentRel(string rel)
        {
            int i = rel.LastIndexOf('/');
            return i < 0 ? "" : rel.Substring(0, i);
        }

        /// <summary>A name that's safe as a file or folder name, or "" if nothing usable is left.</summary>
        private static string SafeName(string name)
        {
            name = name ?? "";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim().TrimEnd('.', ' ');
        }

        private string PrefabFile(string folder, string name) => Path.Combine(FolderPath(folder), name + ".json");

        private void RefreshPrefabList()
        {
            if (string.IsNullOrEmpty(PrefabsDirectory)) return;
            Directory.CreateDirectory(PrefabsDirectory);
            if (!Directory.Exists(FolderPath(_prefabFolder))) _prefabFolder = "";
            var dir = FolderPath(_prefabFolder);

            _prefabSubfolders = Directory.GetDirectories(dir)
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _folderCounts = _prefabSubfolders.ToDictionary(
                n => n, n => Directory.GetFiles(Path.Combine(dir, n), "*.json", SearchOption.AllDirectories).Length);
            _prefabEntries = Directory.GetFiles(dir, "*.json")
                .Select(p => new PrefabEntry
                {
                    Name = Path.GetFileNameWithoutExtension(p),
                    Rel = JoinRel(_prefabFolder, Path.GetFileNameWithoutExtension(p)),
                    Path = p,
                    Stamp = File.GetLastWriteTimeUtc(p),
                })
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string root = Path.GetFullPath(PrefabsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            _allPrefabFolders = Directory.GetDirectories(PrefabsDirectory, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetFullPath(p).Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (_prefabEdit != null && !File.Exists(_prefabEdit) && !Directory.Exists(_prefabEdit)) CloseEdit();
        }

        private void OpenFolder(string rel)
        {
            _prefabFolder = rel ?? "";
            CloseEdit();
            _newFolderOpen = false;
            _scroll = Vector2.zero;
            RefreshPrefabList();
        }

        private void OpenEdit(string path, bool folder, string name)
        {
            if (_prefabEdit == path) { CloseEdit(); return; }
            _prefabEdit = path;
            _prefabEditIsFolder = folder;
            _prefabRename = name;
            _prefabConfirmDelete = false;
            GUIUtility.keyboardControl = 0;
        }

        private void CloseEdit()
        {
            _prefabEdit = null;
            _prefabConfirmDelete = false;
        }

        // ---------------- saving and placing ----------------

        /// <summary>Opens the Prefabs tab ready to save what's selected.</summary>
        private void StartSavePrefab()
        {
            SetPanelTab(PanelTab.Prefabs);
            RefreshPrefabList();
            if (string.IsNullOrWhiteSpace(_prefabName)) _prefabName = SuggestPrefabName(SelectionList());
        }

        private static string SuggestPrefabName(List<LevelObject> pieces)
        {
            if (pieces.Count == 0) return "";
            var kinds = pieces.Select(o => o.Kind).Distinct().ToList();
            return kinds.Count == 1 ? KindName(kinds[0]) + (pieces.Count > 1 ? " set" : "") : "My prefab";
        }

        private void SavePrefab(List<LevelObject> pieces, string name)
        {
            name = SafeName(name);
            pieces = pieces.Where(o => o != null && o.Kind != ObjectKind.Start).ToList();
            if (name.Length == 0) { Toast("Give the prefab a name first"); return; }
            if (pieces.Count == 0) { Toast("Select some pieces first (the Start can't go in a prefab)"); return; }

            // Store the pieces around the bottom-centre of their box: that point lands on the surface when placed.
            var box = GroupBounds(pieces);
            var origin = new Vector3(box.center.x, box.min.y, box.center.z);
            var prefab = new LevelData { name = name, objects = new List<LevelObjectData>() };
            foreach (var o in pieces)
            {
                var d = o.Data.Clone();
                d.position -= origin;
                if (d.Motion == MotionType.Rotate || d.Motion == MotionType.Swing) d.pivot -= origin;
                prefab.objects.Add(d);
            }
            Directory.CreateDirectory(FolderPath(_prefabFolder));
            File.WriteAllText(PrefabFile(_prefabFolder, name), LevelJson.LevelToJson(prefab));
            RefreshPrefabList();
            _prefabName = ""; // the next save suggests a fresh name instead of offering to replace this one
            Toast($"Saved prefab \"{JoinRel(_prefabFolder, name)}\" ({pieces.Count} piece{(pieces.Count == 1 ? "" : "s")})");
        }

        /// <summary>Places a prefab on the surface under the middle of the view (or in front of the camera), selected.</summary>
        private void PlacePrefab(PrefabEntry entry)
        {
            LevelData prefab;
            try { prefab = LevelJson.LevelFromJson(File.ReadAllText(entry.Path)); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't read prefab {entry.Rel}: {e}");
                Toast($"Couldn't read prefab \"{entry.Name}\"");
                RefreshPrefabList();
                return;
            }
            if (prefab.objects == null || prefab.objects.Count == 0) { Toast($"Prefab \"{entry.Name}\" is empty"); return; }

            var ray = CenterRay();
            Vector3 target = RaycastSurface(ray, 80f, out var hit) ? hit.point : ray.GetPoint(12f);
            if (_snap) { target.x = Snap(target.x); target.z = Snap(target.z); }
            AddCopies(prefab.objects, target);
            if (prefab.objects.Any(d => d.Kind != ObjectKind.Start)) Toast($"Placed \"{entry.Name}\": drag the handles to move it");
        }

        // ---------------- folders, renaming, moving, deleting ----------------

        private void CreateFolder(string name)
        {
            name = SafeName(name);
            if (name.Length == 0) { Toast("Give the folder a name first"); return; }
            var path = Path.Combine(FolderPath(_prefabFolder), name);
            if (Directory.Exists(path)) { Toast($"There's already a folder called \"{name}\" here"); return; }
            Directory.CreateDirectory(path);
            _newFolderOpen = false;
            _newFolderName = "";
            RefreshPrefabList();
            Toast($"Made folder \"{name}\"");
        }

        /// <summary>Moves or renames a file or folder; a change of capital letters only goes through a temporary name.</summary>
        private static void MovePath(string from, string to, bool folder)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                var temp = to + ".renaming";
                if (folder) { Directory.Move(from, temp); Directory.Move(temp, to); }
                else { File.Move(from, temp); File.Move(temp, to); }
            }
            else if (folder) Directory.Move(from, to);
            else File.Move(from, to);
        }

        private void RenameEdited(string newName)
        {
            newName = SafeName(newName);
            if (newName.Length == 0) { Toast("Type a name first"); return; }
            string from = _prefabEdit;
            string parent = Path.GetDirectoryName(from);
            string to = _prefabEditIsFolder ? Path.Combine(parent, newName) : Path.Combine(parent, newName + ".json");
            if (from == to) { CloseEdit(); return; }
            bool taken = !string.Equals(from, to, StringComparison.OrdinalIgnoreCase) && (File.Exists(to) || Directory.Exists(to));
            if (taken) { Toast($"\"{newName}\" is already taken here"); return; }
            try
            {
                MovePath(from, to, _prefabEditIsFolder);
                ForgetThumbsUnder(from);
                Toast($"Renamed to \"{newName}\"");
                CloseEdit();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't rename {from}: {e}");
                Toast("Couldn't rename it (is the file open somewhere?)", 3f);
            }
            RefreshPrefabList();
        }

        private void MoveEditedPrefab(string folder)
        {
            string from = _prefabEdit;
            string name = Path.GetFileName(from);
            string to = Path.Combine(FolderPath(folder), name);
            if (File.Exists(to)) { Toast($"{(folder.Length == 0 ? "The top level" : folder)} already has a prefab called \"{Path.GetFileNameWithoutExtension(name)}\""); return; }
            try
            {
                Directory.CreateDirectory(FolderPath(folder));
                File.Move(from, to);
                ForgetThumbsUnder(from);
                Toast($"Moved \"{Path.GetFileNameWithoutExtension(name)}\" to {(folder.Length == 0 ? "the top level" : folder)}");
                CloseEdit();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't move {from}: {e}");
                Toast("Couldn't move it (is the file open somewhere?)", 3f);
            }
            RefreshPrefabList();
        }

        private void DeleteEdited()
        {
            string path = _prefabEdit;
            string name = _prefabEditIsFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
            bool done = FileDialogs.Recycle(path);
            if (done) ForgetThumbsUnder(path);
            CloseEdit();
            RefreshPrefabList();
            Toast(done ? $"Deleted \"{name}\" (it's in the Recycle Bin)" : $"Couldn't delete \"{name}\"", 3f);
        }

        /// <summary>Drops the pictures of a prefab, or of every prefab in a folder.</summary>
        private void ForgetThumbsUnder(string path)
        {
            string asFolder = path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var key in _thumbs.Keys.ToList())
                if (string.Equals(key, path, StringComparison.OrdinalIgnoreCase) || key.StartsWith(asFolder, StringComparison.OrdinalIgnoreCase))
                    ForgetPrefabThumb(key);
        }

        // ---------------- the tab ----------------

        private void DrawPrefabsTab()
        {
            DrawSavePrefabCard();
            DrawPrefabBreadcrumb();
            if (_prefabEdit != null) DrawPrefabEditCard();
            DrawPrefabGrid();
            if (_prefabDo != null)
            {
                var action = _prefabDo;
                _prefabDo = null;
                action();
            }
            GUILayout.Space(4);
            GUILayout.Label("Click a picture to place a copy on the surface in the middle of your view, selected so you can move or turn it. Prefabs work in every level and world. \"...\" renames, moves or deletes.", Theme.Hint);
            GUILayout.Label(FolderPath(_prefabFolder), Theme.Hint);
        }

        private void DrawSavePrefabCard()
        {
            var pieces = SelectionList().Where(o => o != null).ToList();
            Theme.Section("Save the selection as a prefab");
            GUILayout.BeginVertical(Theme.Card);
            if (pieces.Count == 0)
            {
                GUILayout.Label("Select a piece, or drag a box around several, then name the prefab here and save it.", Theme.Hint);
            }
            else
            {
                GUILayout.Label((pieces.Count == 1 ? "1 piece selected (" + KindName(pieces[0].Kind) + ")" : $"{pieces.Count} pieces selected")
                                + $", saving into {(_prefabFolder.Length == 0 ? "the top level" : _prefabFolder)}", Theme.Hint);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Name", GUILayout.Width(70));
                _prefabName = GUILayout.TextField(_prefabName ?? "", 60);
                GUILayout.EndHorizontal();
                string safe = SafeName(_prefabName);
                bool exists = safe.Length > 0 && File.Exists(PrefabFile(_prefabFolder, safe));
                if (GUILayout.Button(exists ? "Replace the saved prefab" : "Save prefab", exists ? Theme.DangerButton : Theme.AccentButton))
                {
                    string name = _prefabName;
                    Later(() => SavePrefab(pieces, name));
                }
                if (pieces.Any(o => o.Kind == ObjectKind.Start)) GUILayout.Label("The Start isn't saved in prefabs: a level has only one.", Theme.Hint);
            }
            GUILayout.EndVertical();
        }

        /// <summary>Where you are (each step clickable), and a new-folder button.</summary>
        private void DrawPrefabBreadcrumb()
        {
            Theme.Section("Your prefabs");
            GUILayout.BeginHorizontal();
            if (_prefabFolder.Length > 0 && GUILayout.Button("‹ Up", Theme.GhostButton, GUILayout.Width(64))) Later(() => OpenFolder(ParentRel(_prefabFolder)));
            if (GUILayout.Button("Prefabs", _prefabFolder.Length == 0 ? Theme.Chip : Theme.GhostButton, GUILayout.ExpandWidth(false))) Later(() => OpenFolder(""));
            if (_prefabFolder.Length > 0)
            {
                var parts = _prefabFolder.Split('/');
                for (int i = 0; i < parts.Length; i++)
                {
                    GUILayout.Label("›", GUILayout.ExpandWidth(false));
                    bool last = i == parts.Length - 1;
                    string target = string.Join("/", parts.Take(i + 1));
                    if (GUILayout.Button(parts[i], last ? Theme.Chip : Theme.GhostButton, GUILayout.ExpandWidth(false)) && !last)
                        Later(() => OpenFolder(target));
                }
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_newFolderOpen ? "Cancel" : "New folder", Theme.GhostButton, GUILayout.ExpandWidth(false)))
                Later(() => { _newFolderOpen = !_newFolderOpen; _newFolderName = ""; });
            GUILayout.EndHorizontal();

            if (_newFolderOpen)
            {
                GUILayout.BeginHorizontal(Theme.Row);
                GUILayout.Label("Folder name", GUILayout.Width(100));
                _newFolderName = GUILayout.TextField(_newFolderName ?? "", 40);
                string folderName = _newFolderName;
                if (GUILayout.Button("Make", Theme.AccentButton, GUILayout.Width(72))) Later(() => CreateFolder(folderName));
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>Rename, move (prefabs) and delete for the prefab or folder whose "..." was clicked.</summary>
        private void DrawPrefabEditCard()
        {
            string current = _prefabEditIsFolder ? Path.GetFileName(_prefabEdit) : Path.GetFileNameWithoutExtension(_prefabEdit);
            GUILayout.BeginVertical(Theme.Card);
            GUILayout.BeginHorizontal();
            GUILayout.Label((_prefabEditIsFolder ? "Folder: " : "Prefab: ") + current, Theme.Header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", Theme.Close, GUILayout.Width(32))) Later(CloseEdit);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _prefabRename = GUILayout.TextField(_prefabRename ?? "", 60);
            string rename = _prefabRename;
            if (GUILayout.Button("Rename", GUILayout.Width(84))) Later(() => RenameEdited(rename));
            GUILayout.EndHorizontal();

            if (!_prefabEditIsFolder)
            {
                var targets = new List<string> { "" };
                targets.AddRange(_allPrefabFolders);
                targets.Remove(_prefabFolder);
                if (targets.Count > 0)
                {
                    GUILayout.Label("Move to", Theme.Hint);
                    foreach (var folder in targets)
                    {
                        string target = folder;
                        if (GUILayout.Button(folder.Length == 0 ? "Top level" : folder.Replace("/", "  ›  "), Theme.GhostButton))
                            Later(() => MoveEditedPrefab(target));
                    }
                }
                else GUILayout.Label("Make a folder with New folder to move prefabs into it.", Theme.Hint);
            }
            GUILayout.Space(4);
            if (!_prefabConfirmDelete)
            {
                if (GUILayout.Button(_prefabEditIsFolder ? "Delete folder…" : "Delete prefab…", Theme.DangerButton)) Later(() => _prefabConfirmDelete = true);
            }
            else
            {
                string what = _prefabEditIsFolder
                    ? $"Delete the folder and the {(_folderCounts.TryGetValue(current, out var n) ? n : 0)} prefab(s) in it?"
                    : "Delete this prefab?";
                GUILayout.Label(what + " It goes to the Recycle Bin.", Theme.Hint);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Delete", Theme.DangerButton)) Later(DeleteEdited);
                if (GUILayout.Button("Keep")) Later(() => _prefabConfirmDelete = false);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        /// <summary>The open folder as a grid: sub-folders first, then each prefab's picture with its name.</summary>
        private void DrawPrefabGrid()
        {
            if (_tileName == null)
            {
                _tileName = new GUIStyle(Theme.Hint) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
                _tileNote = new GUIStyle(Theme.Hint) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            }
            int count = _prefabSubfolders.Count + _prefabEntries.Count;
            if (count == 0)
            {
                GUILayout.Label(_prefabFolder.Length == 0 ? "None yet." : "This folder is empty.", Theme.Hint);
                return;
            }

            const float gap = 8f;
            float width = PanelInnerWidth - 6f;
            int columns = Mathf.Clamp(Mathf.FloorToInt((width + gap) / (110f + gap)), 2, 6);
            float tile = Mathf.Floor((width - gap * (columns - 1)) / columns);

            for (int start = 0; start < count; start += columns)
            {
                GUILayout.BeginHorizontal();
                for (int i = start; i < Mathf.Min(count, start + columns); i++)
                {
                    if (i > start) GUILayout.Space(gap);
                    GUILayout.BeginVertical(GUILayout.Width(tile));
                    if (i < _prefabSubfolders.Count) DrawFolderTile(_prefabSubfolders[i], tile);
                    else DrawPrefabTile(_prefabEntries[i - _prefabSubfolders.Count], tile);
                    GUILayout.EndVertical();
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(gap);
            }
        }

        private void DrawFolderTile(string name, float tile)
        {
            var r = GUILayoutUtility.GetRect(tile, tile, GUILayout.Width(tile), GUILayout.Height(tile));
            string path = Path.Combine(FolderPath(_prefabFolder), name);
            GUI.Box(r, GUIContent.none, Theme.Row);
            // A simple folder shape: a tab and a body.
            var old = GUI.color;
            GUI.color = new Color(1f, 0.74f, 0.25f, 0.9f);
            float w = r.width * 0.56f, h = r.height * 0.36f;
            var body = new Rect(r.center.x - w / 2f, r.center.y - h / 2f - 6f, w, h);
            GUI.DrawTexture(new Rect(body.x, body.y - h * 0.18f, w * 0.42f, h * 0.22f), Theme.White);
            GUI.DrawTexture(body, Theme.White);
            GUI.color = old;
            int n = _folderCounts.TryGetValue(name, out var c) ? c : 0;
            GUI.Label(new Rect(r.x, body.yMax + 2f, r.width, 22f), n == 1 ? "1 prefab" : $"{n} prefabs", _tileNote);
            if (_prefabEdit == path) DrawTileRing(r);
            if (GUI.Button(r, new GUIContent("", "Open " + name), GUIStyle.none)) Later(() => OpenFolder(JoinRel(_prefabFolder, name)));
            DrawTileFooter(name, () => OpenEdit(path, true, name));
        }

        private void DrawPrefabTile(PrefabEntry entry, float tile)
        {
            var r = GUILayoutUtility.GetRect(tile, tile, GUILayout.Width(tile), GUILayout.Height(tile));
            var picture = PrefabThumb(entry);
            if (picture != null) GUI.DrawTexture(r, picture, ScaleMode.ScaleToFit);
            else
            {
                GUI.Box(r, GUIContent.none, Theme.Row);
                GUI.Label(r, PrefabThumbFailed(entry) ? "No picture" : "Drawing…", _tileNote);
            }
            if (_prefabEdit == entry.Path) DrawTileRing(r);
            if (GUI.Button(r, new GUIContent("", "Place " + entry.Name), GUIStyle.none)) Later(() => PlacePrefab(entry));
            DrawTileFooter(entry.Name, () => OpenEdit(entry.Path, false, entry.Name));
        }

        private void DrawTileFooter(string name, Action edit)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(name, name), _tileName, GUILayout.ExpandWidth(true), GUILayout.MinWidth(10f));
            if (GUILayout.Button(new GUIContent("...", "Rename, move or delete"), Theme.Small, GUILayout.Width(30))) Later(edit);
            GUILayout.EndHorizontal();
        }

        private static void DrawTileRing(Rect r)
        {
            var old = GUI.color;
            GUI.color = Theme.Accent;
            const float t = 2f;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Theme.White);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Theme.White);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Theme.White);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Theme.White);
            GUI.color = old;
        }
    }
}
