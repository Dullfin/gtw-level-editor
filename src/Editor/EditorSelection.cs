using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// Selecting several pieces at once in mouse mode: drag a box, or Shift+click to add and remove pieces.
    /// The last piece clicked stays the "active" one (<see cref="_selected"/>): the handles sit on it, and moving
    /// or rotating it carries the rest of the group along. Copy, paste, duplicate and delete work on the group.
    /// </summary>
    public partial class LevelEditorController
    {
        /// <summary>Every selected piece, including <see cref="_selected"/>. Only counts while it holds more than one.</summary>
        private readonly List<LevelObject> _group = new List<LevelObject>();

        /// <summary>True while more than one piece is selected.</summary>
        private bool GroupActive
        {
            get
            {
                if (_selected == null || _group.Count < 2) return false;
                _group.RemoveAll(o => o == null || !_objects.Contains(o));
                return _group.Count > 1 && _group.Contains(_selected);
            }
        }

        /// <summary>What copy, duplicate and delete act on: the group, or the one selected piece.</summary>
        private List<LevelObject> SelectionList() =>
            GroupActive ? new List<LevelObject>(_group) : _selected != null ? new List<LevelObject> { _selected } : new List<LevelObject>();

        private bool IsInSelection(LevelObject o) => o != null && (o == _selected || (GroupActive && _group.Contains(o)));

        // ---------------- clicking and box selection ----------------

        private const float BoxDragThreshold = 6f;
        private bool _pressPending, _boxing, _pressShift;
        private Vector2 _pressStart;

        /// <summary>A left click in the world that isn't on a handle: a click selects, a drag draws a box.</summary>
        private void BeginWorldPress(Vector2 mouse)
        {
            _pressPending = true;
            _boxing = false;
            _pressStart = mouse;
            _pressShift = Shift;
        }

        /// <summary>Runs every frame in mouse mode; returns true while a press or box is in progress.</summary>
        private bool UpdateWorldPress(Camera cam, Vector2 mouse)
        {
            if (!_pressPending) return false;
            if (!_boxing && Vector2.Distance(mouse, _pressStart) > BoxDragThreshold * UiScaleFactor) _boxing = true;
            if (_mouseHeld && !_mouseUp) return true;

            _pressPending = false;
            if (_boxing)
            {
                _boxing = false;
                BoxSelect(cam, BoxRect(_pressStart, mouse), _pressShift);
                return true;
            }
            ClickSelect(cam);
            return true;
        }

        private static Rect BoxRect(Vector2 a, Vector2 b) =>
            Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        private void ClickSelect(Camera cam)
        {
            var hit = RaycastObject(cam.ScreenPointToRay(new Vector2(_pressStart.x, Screen.height - _pressStart.y)));
            if (_pressShift)
            {
                if (hit != null) ToggleInSelection(hit);
                return;
            }
            if (hit != null)
            {
                // Clicking a ramp's body (not one of its points) goes back to handles for the whole ramp.
                if (hit == _selected && hit.IsRamp) ChoosePoint(-1);
                Select(hit);
            }
            else Select(null);
        }

        /// <summary>Shift+click: adds a piece to the selection (making it active), or takes it out.</summary>
        private void ToggleInSelection(LevelObject obj)
        {
            var current = SelectionList();
            if (current.Contains(obj))
            {
                current.Remove(obj);
                var active = obj == _selected ? current.LastOrDefault() : _selected;
                SetSelection(current, active);
            }
            else
            {
                current.Add(obj);
                SetSelection(current, obj);
            }
        }

        /// <summary>Selects every piece whose centre is inside the box (adding to the selection with Shift).</summary>
        private void BoxSelect(Camera cam, Rect box, bool add)
        {
            var picked = add ? SelectionList() : new List<LevelObject>();
            foreach (var o in _objects)
            {
                if (o == null || o == _ghost || picked.Contains(o)) continue;
                var sp = cam.WorldToScreenPoint(o.GetBounds().center);
                if (sp.z <= 0.1f) continue;
                if (box.Contains(new Vector2(sp.x, Screen.height - sp.y))) picked.Add(o);
            }
            var active = _selected != null && picked.Contains(_selected) ? _selected : picked.LastOrDefault();
            SetSelection(picked, active);
            if (picked.Count > 1) Toast($"{picked.Count} pieces selected");
        }

        /// <summary>Makes <paramref name="pieces"/> the selection, with <paramref name="active"/> carrying the handles.</summary>
        private void SetSelection(List<LevelObject> pieces, LevelObject active)
        {
            Select(active);
            _group.Clear();
            if (active != null) _group.AddRange(pieces.Where(o => o != null).Distinct());
            if (active != null && !_group.Contains(active)) _group.Add(active);
        }

        private void SelectAll()
        {
            var all = _objects.Where(o => o != null).ToList();
            if (all.Count == 0) return;
            SetSelection(all, _selected != null && all.Contains(_selected) ? _selected : all[all.Count - 1]);
            Toast($"{all.Count} pieces selected");
        }

        // ---------------- moving and rotating the group ----------------

        private struct GroupStart
        {
            public LevelObject Obj;
            public Vector3 Position, Pivot, MoveOffset;
            public Quaternion Rotation;
        }

        private readonly List<GroupStart> _groupStart = new List<GroupStart>();

        /// <summary>At the start of a move or rotate drag, remembers where the rest of the group was.</summary>
        private void BeginGroupDrag(DragKind kind)
        {
            _groupStart.Clear();
            if (!GroupActive || (kind != DragKind.MoveAxis && kind != DragKind.MovePlane && kind != DragKind.Rotate)) return;
            foreach (var o in _group)
            {
                if (o == _selected) continue;
                _groupStart.Add(new GroupStart
                {
                    Obj = o,
                    Position = o.Data.position,
                    Rotation = Quaternion.Euler(o.Data.rotation),
                    Pivot = o.Data.pivot,
                    MoveOffset = o.Data.moveOffset,
                });
            }
        }

        /// <summary>Carries the rest of the group along with the active piece: the same move, or the same turn around it.</summary>
        private void ContinueGroupDrag()
        {
            if (_groupStart.Count == 0) return;
            var d = _selected.Data;
            Vector3 move = d.position - _dragStartPos;
            Quaternion turn = Quaternion.Euler(d.rotation) * Quaternion.Inverse(_dragStartRot);
            bool rotating = _drag == DragKind.Rotate;
            foreach (var s in _groupStart)
            {
                if (s.Obj == null) continue;
                var od = s.Obj.Data;
                bool turns = od.Motion == MotionType.Rotate || od.Motion == MotionType.Swing;
                if (rotating)
                {
                    od.position = _dragStartPos + turn * (s.Position - _dragStartPos);
                    od.rotation = (turn * s.Rotation).eulerAngles;
                    if (turns) od.pivot = _dragStartPos + turn * (s.Pivot - _dragStartPos);
                    if (od.Motion == MotionType.Move) od.moveOffset = turn * s.MoveOffset;
                }
                else
                {
                    od.position = s.Position + move;
                    if (turns) od.pivot = s.Pivot + move;
                }
                s.Obj.Refresh();
            }
        }

        // ---------------- copy, paste, duplicate, delete ----------------

        /// <summary>The copied pieces, and the size of the box around them (for placing pasted copies beside the originals).</summary>
        private List<LevelObjectData> _clipboard;
        private Vector3 _clipboardSize, _clipboardCentre;

        private void CopySelection(List<LevelObject> pieces)
        {
            pieces = pieces.Where(o => o != null).ToList();
            if (pieces.Count == 0) return;
            _clipboard = pieces.Select(o => o.Data.Clone()).ToList();
            var bounds = GroupBounds(pieces);
            _clipboardSize = bounds.size;
            _clipboardCentre = bounds.center;
            Toast(pieces.Count == 1 ? "Copied " + pieces[0].Kind : $"Copied {pieces.Count} pieces");
        }

        private static Bounds GroupBounds(List<LevelObject> pieces)
        {
            var b = pieces[0].GetBounds();
            for (int i = 1; i < pieces.Count; i++) b.Encapsulate(pieces[i].GetBounds());
            return b;
        }

        /// <summary>Adds copies of several pieces at once, shifted by <paramref name="shift"/>, and selects them.</summary>
        private void AddCopies(List<LevelObjectData> source, Vector3 shift)
        {
            PushUndo();
            var made = new List<LevelObject>();
            int skippedStarts = 0;
            foreach (var data in source)
            {
                if (data.Kind == ObjectKind.Start) { skippedStarts++; continue; } // a level has one Start
                var copy = data.Clone();
                copy.position += shift;
                if (copy.Motion == MotionType.Rotate || copy.Motion == MotionType.Swing) copy.pivot += shift;
                var o = LevelObject.Create(copy, _root);
                o.SetEditorVisible(true);
                _objects.Add(o);
                made.Add(o);
            }
            if (made.Count == 0) { Toast("A level has only one Start"); return; }
            SetSelection(made, made[made.Count - 1]);
            Toast($"Pasted {made.Count} pieces" + (skippedStarts > 0 ? " (not the Start: a level has one)" : ""));
        }

        /// <summary>Beside the originals, along X, clear of the copied group.</summary>
        private Vector3 PasteBesideShift(Vector3 size) => Vector3.right * Mathf.Max(1f, size.x + 0.5f);

        /// <summary>Fly mode: pastes a copied group onto the surface under the crosshair (or in front of you).</summary>
        private void PasteGroupAtCrosshair(List<LevelObjectData> source)
        {
            var ray = CenterRay();
            // The bottom of the copied group's box sits on the surface under the crosshair.
            Vector3 target;
            if (RaycastSurface(ray, 80f, out var hit)) target = hit.point + Vector3.up * (_clipboardSize.y * 0.5f);
            else target = ray.GetPoint(Mathf.Max(10f, _clipboardSize.magnitude));
            var shift = target - _clipboardCentre;
            if (_snap) { shift.x = Snap(shift.x); shift.z = Snap(shift.z); }
            AddCopies(source, shift);
        }

        private void DeleteSelection()
        {
            var pieces = SelectionList();
            if (pieces.Count == 0) return;
            PushUndo();
            foreach (var o in pieces) DeleteObject(o, undo: false);
            _group.Clear();
            Toast(pieces.Count == 1 ? "Deleted " + pieces[0].Kind : $"Deleted {pieces.Count} pieces");
        }

        // ---------------- panel and drawing ----------------

        private void DrawGroupPanel()
        {
            var pieces = SelectionList();
            Theme.Section($"Selected  ·  {pieces.Count} pieces");
            GUILayout.BeginVertical(Theme.Card);
            var counts = pieces.GroupBy(o => o.Kind).Select(g => $"{g.Count()} × {KindName(g.Key)}");
            GUILayout.Label(string.Join(", ", counts), Theme.Hint);
            GUILayout.Label($"The handles sit on the last piece you picked ({KindName(_selected.Kind)}). Moving or rotating it carries the rest along. Scale and settings are one piece at a time: Shift+click a piece to pick just that one out, or click it on its own.", Theme.Hint);
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy")) CopySelection(pieces);
            if (GUILayout.Button("Duplicate")) DuplicateSelection();
            if (GUILayout.Button("Delete", Theme.DangerButton)) DeleteSelection();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Select only " + KindName(_selected.Kind), Theme.GhostButton)) Select(_selected);
        }

        private void DuplicateSelection()
        {
            var pieces = SelectionList();
            if (pieces.Count == 0) return;
            AddCopies(pieces.Select(o => o.Data).ToList(), PasteBesideShift(GroupBounds(pieces).size));
        }

        /// <summary>Outlines every selected piece in a group, and the box being dragged.</summary>
        private void DrawSelectionOverlay(Camera cam, float ui)
        {
            if (GroupActive)
            {
                var corners = new Vector3[8];
                foreach (var o in _group)
                {
                    if (o == null) continue;
                    var b = o.GetBounds();
                    for (int i = 0; i < 8; i++)
                        corners[i] = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var col = o == _selected ? new Color(1f, 0.92f, 0.2f, 0.9f) : new Color(0.35f, 0.8f, 1f, 0.8f);
                    for (int e = 0; e < BoxEdges.Length; e += 2)
                    {
                        var a = ToGui(cam, corners[BoxEdges[e]], out var av);
                        var c = ToGui(cam, corners[BoxEdges[e + 1]], out var cv);
                        if (av && cv) Line(a, c, col, 1.5f * ui);
                    }
                }
            }
            if (_pressPending && _boxing)
            {
                var r = BoxRect(_pressStart, MouseGui);
                var old = GUI.color;
                GUI.color = new Color(0.35f, 0.8f, 1f, 0.15f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = old;
                var edge = new Color(0.35f, 0.8f, 1f, 0.9f);
                float w = 1.5f * ui;
                Line(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), edge, w);
                Line(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), edge, w);
                Line(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), edge, w);
                Line(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), edge, w);
            }
        }
    }
}
