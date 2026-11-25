using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// Mouse-mode editing: move/rotate/scale handles, dragging ramp points, looking around,
    /// plus undo/redo and copy/paste.
    /// </summary>
    public partial class LevelEditorController
    {
        // ---------------- mouse input (DebugCommands can drive it for scripted tests) ----------------

        internal Vector2? DebugMouse;
        internal string DebugLastClick;
        internal bool DebugMouseHeld;
        private bool _debugMouseWasHeld;
        private bool _mouseDown, _mouseHeld, _mouseUp;

        /// <summary>Mouse position in screen pixels, origin bottom-left like Input.mousePosition.</summary>
        private Vector2 MousePos => DebugMouse ?? (Vector2)Input.mousePosition;
        /// <summary>The same position with the origin top-left, as IMGUI draws.</summary>
        private Vector2 MouseGui => new Vector2(MousePos.x, Screen.height - MousePos.y);

        private void ReadMouse()
        {
            _mouseDown = Input.GetMouseButtonDown(0) || (DebugMouseHeld && !_debugMouseWasHeld);
            _mouseUp = Input.GetMouseButtonUp(0) || (!DebugMouseHeld && _debugMouseWasHeld);
            _mouseHeld = Input.GetMouseButton(0) || DebugMouseHeld;
            _debugMouseWasHeld = DebugMouseHeld;
        }

        private static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // ---------------- shortcuts ----------------

        private LevelObjectData _clipboard;

        private void HandleShortcuts()
        {
            if (!Ctrl) return;
            if (Input.GetKeyDown(KeyCode.Z)) { if (Shift) Redo(); else Undo(); }
            else if (Input.GetKeyDown(KeyCode.Y)) Redo();
            else if (Input.GetKeyDown(KeyCode.C))
            {
                var src = _cursorMode ? _selected : (AimedObject() ?? _selected);
                if (src != null) { _clipboard = src.Data.Clone(); Toast("Copied " + src.Kind); }
            }
            else if (Input.GetKeyDown(KeyCode.V) && _clipboard != null)
            {
                if (_cursorMode) AddCopy(_clipboard);
                else HoldCopy(_clipboard);
            }
            else if (Input.GetKeyDown(KeyCode.D))
            {
                if (_cursorMode) DuplicateSelected();
                else
                {
                    var src = AimedObject() ?? _selected;
                    if (src != null) HoldCopy(src.Data);
                }
            }
        }

        /// <summary>Puts a copy on the crosshair, to be placed with a click.</summary>
        private void HoldCopy(LevelObjectData data)
        {
            _draft.Clear();
            _rampTool = false;
            _kind = data.Kind;
            _ghostEuler = data.rotation;
            SpawnGhost(data.Clone());
            Toast("Click to place the copy");
        }

        private void DuplicateSelected()
        {
            if (_selected != null) AddCopy(_selected.Data);
        }

        /// <summary>Adds a copy next to the original and selects it.</summary>
        private void AddCopy(LevelObjectData data)
        {
            PushUndo();
            var copy = data.Clone();
            var size = data.Kind == ObjectKind.Ramp ? 4f : data.scale.x;
            copy.position += Vector3.right * Mathf.Max(1f, size + 0.5f);
            var o = LevelObject.Create(copy, _root);
            o.SetEditorVisible(true);
            _objects.Add(o);
            Select(o);
            Toast("Copied " + data.Kind);
        }

        // ---------------- undo / redo ----------------

        private class Snapshot
        {
            public List<LevelObjectData> Objects;
            public int Selected;
        }

        private readonly List<Snapshot> _undo = new List<Snapshot>();
        private readonly List<Snapshot> _redo = new List<Snapshot>();
        private LevelObject _panelEditObject;
        private float _panelEditTime;

        private Snapshot Capture() => new Snapshot
        {
            Objects = _objects.Select(o => o.Data.Clone()).ToList(),
            Selected = _selected != null ? _objects.IndexOf(_selected) : -1,
        };

        /// <summary>Records the level as it is now (or as given), before a change.</summary>
        private void PushUndo(Snapshot snapshot = null)
        {
            _undo.Add(snapshot ?? Capture());
            if (_undo.Count > 200) _undo.RemoveAt(0);
            _redo.Clear();
            _panelEditObject = null;
        }

        private void ClearUndo()
        {
            _undo.Clear();
            _redo.Clear();
            _panelEditObject = null;
        }

        /// <summary>
        /// A panel field changed <paramref name="obj"/>. Typing or clicking +/- in a row makes one
        /// undo step, so each keystroke isn't its own step.
        /// </summary>
        private void NotePanelEdit(LevelObject obj, string jsonBefore)
        {
            if (!(_panelEditObject == obj && Time.unscaledTime - _panelEditTime < 1.5f))
            {
                var snap = Capture();
                int i = _objects.IndexOf(obj);
                if (i >= 0) snap.Objects[i] = LevelJson.ObjectFromJson(jsonBefore);
                PushUndo(snap);
            }
            _panelEditObject = obj;
            _panelEditTime = Time.unscaledTime;
            if (obj.IsRamp) RememberRampStyle(obj.Data);
        }

        private void Undo()
        {
            if (_rampTool && _draft.Count > 0)
            {
                _draft.RemoveAt(_draft.Count - 1);
                return;
            }
            if (_undo.Count == 0) { Toast("Nothing to undo"); return; }
            _redo.Add(Capture());
            Restore(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
            Toast($"Undo ({_undo.Count} left)");
        }

        private void Redo()
        {
            if (_redo.Count == 0) { Toast("Nothing to redo"); return; }
            _undo.Add(Capture());
            Restore(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
            Toast("Redo");
        }

        private void Restore(Snapshot s)
        {
            DestroyGhost();
            _drag = DragKind.None;
            foreach (var o in _objects) Destroy(o.gameObject);
            _objects.Clear();
            foreach (var d in s.Objects)
            {
                var o = LevelObject.Create(d.Clone(), _root);
                o.SetEditorVisible(IsEditing);
                _objects.Add(o);
            }
            _selected = s.Selected >= 0 && s.Selected < _objects.Count ? _objects[s.Selected] : null;
            _selectedPoint = -1;
            _fieldText.Clear();
            _panelEditObject = null;
            if (_rampTool) _draft.Clear();
        }

        // ---------------- mouse mode: handles ----------------

        private enum GizmoMode { Move, Rotate, Scale }
        private enum DragKind { None, MoveAxis, MovePlane, Rotate, ScaleAxis, ScaleUniform, RampPoint, Look, PointAxis, PointPlane, PointBank, PointWidth }

        private GizmoMode _gizmo = GizmoMode.Move;
        private DragKind _drag;
        private int _dragAxis;
        private Vector3 _dragStartPos, _dragStartScale, _dragStartVec, _dragStartHit, _dragAxisVec;
        private Quaternion _dragStartRot;
        private float _dragStartT, _dragStartValue;
        private Vector2 _dragStartMouse, _lastMouse;
        private int _selectedPoint = -1;
        private int _hoverAxis = -1;
        private DragKind _hoverKind;
        private float _lookYaw, _lookPitch;

        private static readonly Vector3[] WorldAxes = { Vector3.right, Vector3.up, Vector3.forward };
        private static readonly Color[] AxisColors = { new Color(1f, 0.3f, 0.3f), new Color(0.4f, 1f, 0.4f), new Color(0.35f, 0.6f, 1f) };

        private float HandlePick => 12f * UiScaleFactor;

        private void CursorModeUpdate(bool typing)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var mouse = MouseGui;

            bool flying = Input.GetMouseButton(1);
            if (!typing && !flying)
            {
                if (!Ctrl && Input.GetKeyDown(KeyCode.W)) _gizmo = GizmoMode.Move;
                if (!Ctrl && Input.GetKeyDown(KeyCode.E)) _gizmo = GizmoMode.Rotate;
                if (!Ctrl && Input.GetKeyDown(KeyCode.R)) _gizmo = GizmoMode.Scale;
                if (Input.GetKeyDown(KeyCode.Delete) && _selected != null)
                {
                    if (_selected.IsRamp && _selectedPoint >= 0 && _selected.Data.points.Count > 2)
                    {
                        PushUndo();
                        DeleteRampPoint(_selected);
                    }
                    else DeleteObject(_selected);
                }
            }

            // ---- looking around with the right mouse button, and the wheel to move forward ----
            if (Input.GetMouseButtonDown(1) && !MouseOverWindow())
            {
                var e = cam.transform.eulerAngles;
                _lookYaw = e.y;
                _lookPitch = e.x > 180f ? e.x - 360f : e.x;
                _lastMouse = MousePos;
            }
            if (Input.GetMouseButton(1))
            {
                var delta = MousePos - _lastMouse;
                _lastMouse = MousePos;
                _lookYaw += delta.x * 0.15f;
                _lookPitch = Mathf.Clamp(_lookPitch - delta.y * 0.15f, -89f, 89f);
                cam.transform.rotation = Quaternion.Euler(_lookPitch, _lookYaw, 0f);
                if (!typing) FlyMouseModeCamera(cam);
            }
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f && !MouseOverWindow()) cam.transform.position += cam.transform.forward * wheel * (Shift ? 4f : 1.5f);

            if (_selected == null) _drag = DragKind.None;
            if (_drag != DragKind.None)
            {
                if (!_mouseHeld || _mouseUp) { _drag = DragKind.None; return; }
                ContinueDrag(cam, mouse);
                return;
            }

            UpdateHover(cam, mouse);
            if (_mouseDown) DebugLastClick = $"click at {mouse} (screen {Screen.width}x{Screen.height}) hover={_hoverKind}/{_hoverAxis} overWindow={MouseOverWindow()}";
            if (!_mouseDown || MouseOverWindow()) return;
            GUIUtility.keyboardControl = 0; // clicking the world leaves any text field

            if (_hoverKind != DragKind.None)
            {
                BeginDrag(cam, mouse, _hoverKind, _hoverAxis);
                return;
            }
            var hit = RaycastObject(cam.ScreenPointToRay(MousePos));
            if (hit != null)
            {
                // Clicking a ramp's body (not one of its points) goes back to handles for the whole ramp.
                if (hit == _selected && hit.IsRamp) ChoosePoint(-1);
                Select(hit);
            }
            else _selected = null;
        }

        // ---------------- ramp point handles ----------------

        /// <summary>True when a ramp point is chosen: the handles then move, bank and widen that point.</summary>
        private bool PointGizmo => _selected != null && _selected.IsRamp && _selectedPoint >= 0 && _selectedPoint < _selected.Data.points.Count;

        /// <summary>Where the handles sit: the chosen ramp point, or the selected object's origin.</summary>
        private Vector3 GizmoCenter => PointGizmo
            ? _selected.transform.TransformPoint(_selected.Data.points[_selectedPoint].position)
            : _selected.transform.position;

        /// <summary>The track's direction through the chosen point (world space).</summary>
        private Vector3 PointTangent()
        {
            var pts = _selected.Data.points;
            int a = Mathf.Max(_selectedPoint - 1, 0), b = Mathf.Min(_selectedPoint + 1, pts.Count - 1);
            var t = _selected.transform.TransformDirection(pts[b].position - pts[a].position);
            return t.sqrMagnitude < 1e-8f ? _selected.transform.forward : t.normalized;
        }

        /// <summary>Across the track at the chosen point, tilted by its bank (world space).</summary>
        private Vector3 PointRight()
        {
            var t = PointTangent();
            var r = Mathf.Abs(t.y) < 0.98f ? Vector3.Cross(Vector3.up, t).normalized : _selected.transform.right;
            return Quaternion.AngleAxis(_selected.Data.points[_selectedPoint].bank, t) * r;
        }

        private void SetPointWorld(LevelObjectData d, Vector3 world) =>
            d.points[_selectedPoint].position = _selected.transform.InverseTransformPoint(world);

        private Vector2 ToGui(Camera cam, Vector3 world, out bool visible)
        {
            var sp = cam.WorldToScreenPoint(world);
            visible = sp.z > 0.1f;
            return new Vector2(sp.x, Screen.height - sp.y);
        }

        private float GizmoLength(Camera cam, Vector3 center) => Mathf.Max(0.5f, Vector3.Distance(cam.transform.position, center) * 0.16f);

        private Vector3 GizmoAxis(int i) => _gizmo == GizmoMode.Scale && _selected != null
            ? _selected.transform.rotation * WorldAxes[i]
            : WorldAxes[i];

        private Vector3 PlaneHandlePos(Vector3 c, float len) => c + (Vector3.right + Vector3.forward) * len * 0.3f;

        /// <summary>Which handle (ramp point, axis, ring, box) is under the mouse.</summary>
        private void UpdateHover(Camera cam, Vector2 mouse)
        {
            _hoverKind = DragKind.None;
            _hoverAxis = -1;
            if (_selected == null || MouseOverWindow()) return;
            float best = HandlePick;

            if (_selected.IsRamp)
            {
                var pts = _selected.Data.points;
                for (int i = 0; i < pts.Count; i++)
                {
                    var g = ToGui(cam, _selected.transform.TransformPoint(pts[i].position), out var vis);
                    float d = Vector2.Distance(g, mouse);
                    if (vis && d < best) { best = d; _hoverKind = DragKind.RampPoint; _hoverAxis = i; }
                }
                if (_hoverKind != DragKind.None) return;
            }

            var center = GizmoCenter;
            var c = ToGui(cam, center, out var cVis);
            if (!cVis) return;
            float len = GizmoLength(cam, center);
            best = HandlePick;
            bool point = PointGizmo;

            switch (_gizmo)
            {
                case GizmoMode.Move:
                {
                    var ph = ToGui(cam, PlaneHandlePos(center, len), out var pv);
                    if (pv && Vector2.Distance(ph, mouse) < HandlePick * 1.1f) { _hoverKind = point ? DragKind.PointPlane : DragKind.MovePlane; return; }
                    for (int i = 0; i < 3; i++)
                    {
                        var e = ToGui(cam, center + WorldAxes[i] * len, out var v);
                        float d = DistToSegment(mouse, c, e);
                        if (v && d < best) { best = d; _hoverKind = point ? DragKind.PointAxis : DragKind.MoveAxis; _hoverAxis = i; }
                    }
                    break;
                }
                case GizmoMode.Scale when point:
                {
                    // One handle across the track: the point's width.
                    var e = ToGui(cam, center + PointRight() * len, out var v);
                    if (v && DistToSegment(mouse, c, e) < best) { _hoverKind = DragKind.PointWidth; _hoverAxis = 0; }
                    break;
                }
                case GizmoMode.Rotate when point:
                {
                    // One ring around the track: the point's bank.
                    var ring = RingPoints(cam, center, PointTangent(), len);
                    for (int k = 0; k < ring.Count - 1; k++)
                    {
                        if (!ring[k].visible || !ring[k + 1].visible) continue;
                        float d = DistToSegment(mouse, ring[k].p, ring[k + 1].p);
                        if (d < best) { best = d; _hoverKind = DragKind.PointBank; _hoverAxis = 2; }
                    }
                    break;
                }
                case GizmoMode.Scale:
                {
                    if (_selected.IsRamp) return; // ramps are shaped by their points
                    if (Vector2.Distance(c, mouse) < HandlePick) { _hoverKind = DragKind.ScaleUniform; return; }
                    for (int i = 0; i < 3; i++)
                    {
                        var e = ToGui(cam, center + GizmoAxis(i) * len, out var v);
                        float d = DistToSegment(mouse, c, e);
                        if (v && d < best) { best = d; _hoverKind = DragKind.ScaleAxis; _hoverAxis = i; }
                    }
                    break;
                }
                case GizmoMode.Rotate:
                    for (int i = 0; i < 3; i++)
                    {
                        var ring = RingPoints(cam, center, WorldAxes[i], len);
                        for (int k = 0; k < ring.Count - 1; k++)
                        {
                            if (!ring[k].visible || !ring[k + 1].visible) continue;
                            float d = DistToSegment(mouse, ring[k].p, ring[k + 1].p);
                            if (d < best) { best = d; _hoverKind = DragKind.Rotate; _hoverAxis = i; }
                        }
                    }
                    break;
            }
        }

        private List<(Vector2 p, bool visible)> RingPoints(Camera cam, Vector3 center, Vector3 axis, float radius)
        {
            var a = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            var b = Vector3.Cross(axis, a);
            var list = new List<(Vector2, bool)>(49);
            for (int k = 0; k <= 48; k++)
            {
                float t = k / 48f * Mathf.PI * 2f;
                var g = ToGui(cam, center + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * radius, out var v);
                list.Add((g, v));
            }
            return list;
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        private Ray MouseRay(Camera cam) => cam.ScreenPointToRay(MousePos);

        /// <summary>Where along the line (origin + t·axis) the mouse ray passes closest, or null if they're parallel.</summary>
        private static float? ClosestOnAxis(Ray ray, Vector3 origin, Vector3 axis)
        {
            var w0 = origin - ray.origin;
            float b = Vector3.Dot(axis, ray.direction);
            float d = Vector3.Dot(axis, w0), e = Vector3.Dot(ray.direction, w0);
            float denom = 1f - b * b;
            if (denom < 1e-4f) return null;
            return (b * e - d) / denom;
        }

        private static bool RayPlane(Ray ray, Vector3 normal, Vector3 point, out Vector3 hit)
        {
            hit = Vector3.zero;
            var plane = new Plane(normal, point);
            if (!plane.Raycast(ray, out float dist)) return false;
            hit = ray.GetPoint(dist);
            return true;
        }

        private void BeginDrag(Camera cam, Vector2 mouse, DragKind kind, int axis)
        {
            PushUndo();
            _fieldText.Clear();
            _drag = kind;
            _dragAxis = axis;
            _dragStartMouse = mouse;
            var t = _selected.transform;
            _dragStartPos = t.position;
            _dragStartRot = t.rotation;
            _dragStartScale = _selected.Data.scale;
            var ray = MouseRay(cam);
            switch (kind)
            {
                case DragKind.MoveAxis:
                    _dragStartT = ClosestOnAxis(ray, _dragStartPos, WorldAxes[axis]) ?? 0f;
                    break;
                case DragKind.ScaleAxis:
                    _dragStartT = ClosestOnAxis(ray, _dragStartPos, GizmoAxis(axis)) ?? 1f;
                    if (Mathf.Abs(_dragStartT) < 0.05f) _dragStartT = 0.05f;
                    break;
                case DragKind.MovePlane:
                    _dragPlaneNormal = UsablePlane(cam, Vector3.up);
                    RayPlane(ray, _dragPlaneNormal, _dragStartPos, out _dragStartHit);
                    break;
                case DragKind.Rotate:
                    RayPlane(ray, WorldAxes[axis], _dragStartPos, out var h);
                    _dragStartVec = h - _dragStartPos;
                    break;
                case DragKind.PointAxis:
                    _dragStartPos = GizmoCenter;
                    _dragStartT = ClosestOnAxis(ray, _dragStartPos, WorldAxes[axis]) ?? 0f;
                    break;
                case DragKind.PointPlane:
                    _dragStartPos = GizmoCenter;
                    _dragPlaneNormal = UsablePlane(cam, Vector3.up);
                    RayPlane(ray, _dragPlaneNormal, _dragStartPos, out _dragStartHit);
                    break;
                case DragKind.PointBank:
                {
                    _dragStartPos = GizmoCenter;
                    _dragAxisVec = PointTangent();
                    _dragStartValue = _selected.Data.points[_selectedPoint].bank;
                    RayPlane(ray, _dragAxisVec, _dragStartPos, out var hb);
                    _dragStartVec = hb - _dragStartPos;
                    break;
                }
                case DragKind.PointWidth:
                    _dragStartPos = GizmoCenter;
                    _dragAxisVec = PointRight();
                    _dragStartValue = _selected.Data.points[_selectedPoint].width;
                    _dragStartT = ClosestOnAxis(ray, _dragStartPos, _dragAxisVec) ?? 1f;
                    if (Mathf.Abs(_dragStartT) < 0.05f) _dragStartT = 0.05f;
                    break;
                case DragKind.RampPoint:
                {
                    ChoosePoint(axis);
                    var world = t.TransformPoint(_selected.Data.points[axis].position);
                    _dragStartPos = world;
                    _dragStartT = Shift ? 1f : 0f; // 1 = vertical drag
                    _dragPlaneNormal = UsablePlane(cam, DragPlaneNormal(cam));
                    RayPlane(ray, _dragPlaneNormal, world, out _dragStartHit);
                    break;
                }
            }
        }

        /// <summary>
        /// The plane the mouse is projected onto. Seen nearly edge-on, a plane turns small mouse
        /// moves into huge jumps, so then a plane facing the camera is used instead.
        /// </summary>
        private Vector3 UsablePlane(Camera cam, Vector3 wanted)
        {
            var toPoint = (_dragStartPos - cam.transform.position).normalized;
            return Mathf.Abs(Vector3.Dot(toPoint, wanted)) < 0.35f ? -toPoint : wanted;
        }

        private Vector3 _dragPlaneNormal = Vector3.up;

        /// <summary>Ramp points drag along the ground, or up and down while Shift was held when the drag began.</summary>
        private Vector3 DragPlaneNormal(Camera cam)
        {
            if (_dragStartT < 0.5f) return Vector3.up;
            var f = cam.transform.forward;
            f.y = 0f;
            return f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
        }

        /// <summary>
        /// A move on a camera-facing plane, turned into a move along the ground: sideways stays
        /// sideways, and up/down on screen becomes away/towards the camera.
        /// </summary>
        private static Vector3 HorizontalFromScreen(Camera cam, Vector3 delta)
        {
            var right = cam.transform.right;
            right.y = 0f;
            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = cam.transform.up;
            fwd.y = 0f;
            right.Normalize();
            fwd.Normalize();
            return right * Vector3.Dot(delta, cam.transform.right) + fwd * Vector3.Dot(delta, cam.transform.up);
        }

        private void ContinueDrag(Camera cam, Vector2 mouse)
        {
            var d = _selected.Data;
            var ray = MouseRay(cam);
            switch (_drag)
            {
                case DragKind.MoveAxis:
                {
                    var t = ClosestOnAxis(ray, _dragStartPos, WorldAxes[_dragAxis]);
                    if (t == null) return;
                    if (Mathf.Abs(t.Value - _dragStartT) > 1000f) return;
                    var pos = _dragStartPos + WorldAxes[_dragAxis] * (t.Value - _dragStartT);
                    pos = SnapPosition(_selected, BoundsAt(_selected, pos), pos, _dragAxis == 0, _dragAxis == 1, _dragAxis == 2);
                    ShiftPivot(d, pos);
                    d.position = pos;
                    break;
                }
                case DragKind.MovePlane:
                {
                    if (!RayPlane(ray, _dragPlaneNormal, _dragStartPos, out var h)) return;
                    var delta = h - _dragStartHit;
                    if (delta.magnitude > 3f * Vector3.Distance(cam.transform.position, _dragStartPos) + 10f) return;
                    if (_dragPlaneNormal != Vector3.up) delta = HorizontalFromScreen(cam, delta);
                    var pos = _dragStartPos + delta;
                    pos.y = _dragStartPos.y;
                    pos = SnapPosition(_selected, BoundsAt(_selected, pos), pos, true, false, true);
                    ShiftPivot(d, pos);
                    d.position = pos;
                    break;
                }
                case DragKind.Rotate:
                {
                    var axis = WorldAxes[_dragAxis];
                    if (!RayPlane(ray, axis, _dragStartPos, out var h)) return;
                    float angle = Vector3.SignedAngle(_dragStartVec, h - _dragStartPos, axis);
                    if (_snap) angle = Mathf.Round(angle / 15f) * 15f;
                    var rot = Quaternion.AngleAxis(angle, axis) * _dragStartRot;
                    d.rotation = rot.eulerAngles;
                    // Ramps (and anything else) turn around their own origin.
                    break;
                }
                case DragKind.ScaleAxis:
                {
                    var t = ClosestOnAxis(ray, _dragStartPos, GizmoAxis(_dragAxis));
                    if (t == null) return;
                    float factor = Mathf.Max(0.02f, t.Value / _dragStartT);
                    var s = _dragStartScale;
                    float v = s[_dragAxis] * factor;
                    if (_snap) v = Mathf.Max(0.25f, Mathf.Round(v * 4f) / 4f);
                    s[_dragAxis] = v;
                    d.scale = ClampScale(s);
                    break;
                }
                case DragKind.ScaleUniform:
                {
                    var c = ToGui(cam, _dragStartPos, out _);
                    float start = Mathf.Max(4f, Vector2.Distance(_dragStartMouse, c));
                    float factor = Mathf.Max(0.02f, Vector2.Distance(mouse, c) / start);
                    d.scale = ClampScale(_dragStartScale * factor);
                    break;
                }
                case DragKind.RampPoint:
                {
                    if (_dragAxis >= d.points.Count) return;
                    if (!RayPlane(ray, _dragPlaneNormal, _dragStartPos, out var h)) return;
                    var delta = h - _dragStartHit;
                    if (delta.magnitude > 3f * Vector3.Distance(cam.transform.position, _dragStartPos) + 10f) return;
                    if (_dragStartT < 0.5f && _dragPlaneNormal != Vector3.up) delta = HorizontalFromScreen(cam, delta);
                    Vector3 pos;
                    if (_dragStartT > 0.5f)
                    {
                        pos = _dragStartPos + Vector3.up * delta.y;
                        if (_snap) pos.y = Snap(pos.y);
                    }
                    else
                    {
                        pos = _dragStartPos + new Vector3(delta.x, 0f, delta.z);
                        if (_snap) { pos.x = Snap(pos.x); pos.z = Snap(pos.z); }
                    }
                    d.points[_dragAxis].position = _selected.transform.InverseTransformPoint(pos);
                    break;
                }
                case DragKind.PointAxis:
                {
                    if (!PointGizmo) return;
                    var t = ClosestOnAxis(ray, _dragStartPos, WorldAxes[_dragAxis]);
                    if (t == null || Mathf.Abs(t.Value - _dragStartT) > 1000f) return;
                    var pos = _dragStartPos + WorldAxes[_dragAxis] * (t.Value - _dragStartT);
                    if (_snap) pos[_dragAxis] = Snap(pos[_dragAxis]);
                    SetPointWorld(d, pos);
                    break;
                }
                case DragKind.PointPlane:
                {
                    if (!PointGizmo || !RayPlane(ray, _dragPlaneNormal, _dragStartPos, out var h)) return;
                    var delta = h - _dragStartHit;
                    if (delta.magnitude > 3f * Vector3.Distance(cam.transform.position, _dragStartPos) + 10f) return;
                    if (_dragPlaneNormal != Vector3.up) delta = HorizontalFromScreen(cam, delta);
                    var pos = _dragStartPos + delta;
                    pos.y = _dragStartPos.y;
                    if (_snap) { pos.x = Snap(pos.x); pos.z = Snap(pos.z); }
                    SetPointWorld(d, pos);
                    break;
                }
                case DragKind.PointBank:
                {
                    if (!PointGizmo || !RayPlane(ray, _dragAxisVec, _dragStartPos, out var h)) return;
                    float angle = Vector3.SignedAngle(_dragStartVec, h - _dragStartPos, _dragAxisVec);
                    float bank = _dragStartValue + angle;
                    if (_snap) bank = Mathf.Round(bank / 15f) * 15f;
                    d.points[_selectedPoint].bank = Mathf.Repeat(bank + 180f, 360f) - 180f;
                    break;
                }
                case DragKind.PointWidth:
                {
                    if (!PointGizmo) return;
                    var t = ClosestOnAxis(ray, _dragStartPos, _dragAxisVec);
                    if (t == null) return;
                    float w = _dragStartValue * Mathf.Max(0.02f, t.Value / _dragStartT);
                    if (_snap) w = Mathf.Round(w * 4f) / 4f;
                    d.points[_selectedPoint].width = Mathf.Clamp(w, 0.2f, 60f);
                    break;
                }
            }
            _selected.Refresh();
        }

        // ---------------- drawing (screen pixels, on top of the world) ----------------

        private Rect _hotbarRect;
        private GUIStyle _hotbarStyle;

        private void DrawOverlays()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float ui = UiScaleFactor;

            DrawLaunchPaths(cam, ui);
            DrawMotionPaths(cam, ui);
            DrawJumpZones(cam, ui);
            DrawSnapGuides(cam, ui);

            if (!_cursorMode && _rampTool)
            {
                // Where the next ramp point goes.
                var ground = ToGui(cam, _draftGround, out var gv);
                var cursor = ToGui(cam, _draftCursor, out var cv);
                if (gv && cv && Mathf.Abs(_draftCursor.y - _draftGround.y) > 0.05f) Line(ground, cursor, new Color(1f, 1f, 1f, 0.6f), 2f * ui);
                for (int i = 0; i < _draft.Count; i++)
                {
                    var p = ToGui(cam, _draft[i].position, out var v);
                    if (v) Dot(p, 10f * ui, Color.white);
                }
                if (cv) Dot(cursor, 14f * ui, _draftSnapped ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.85f, 0.3f));
                return;
            }

            if (!_cursorMode || _selected == null) return;

            var center = GizmoCenter;
            var c = ToGui(cam, center, out var cVis);
            float len = GizmoLength(cam, center);
            float w = 4f * ui;
            bool point = PointGizmo;
            Color Pick(int axis, DragKind kind) =>
                (_drag == kind && (_dragAxis == axis || kind == DragKind.PointBank || kind == DragKind.PointWidth))
                || (_drag == DragKind.None && _hoverKind == kind && _hoverAxis == axis)
                    ? new Color(1f, 0.92f, 0.2f) : AxisColors[axis];

            if (cVis)
            {
                switch (_gizmo)
                {
                    case GizmoMode.Move:
                    {
                        var axisKind = point ? DragKind.PointAxis : DragKind.MoveAxis;
                        var planeKind = point ? DragKind.PointPlane : DragKind.MovePlane;
                        for (int i = 0; i < 3; i++)
                        {
                            var e = ToGui(cam, center + WorldAxes[i] * len, out var v);
                            if (!v) continue;
                            var col = Pick(i, axisKind);
                            Line(c, e, col, w);
                            Dot(e, 12f * ui, col);
                        }
                        var ph = ToGui(cam, PlaneHandlePos(center, len), out var pv);
                        bool planeHot = _drag == planeKind || (_drag == DragKind.None && _hoverKind == planeKind);
                        if (pv) Dot(ph, 16f * ui, planeHot ? new Color(1f, 0.92f, 0.2f) : new Color(1f, 0.75f, 0.2f, 0.85f));
                        break;
                    }
                    case GizmoMode.Rotate when point:
                    {
                        var col = Pick(2, DragKind.PointBank);
                        var ring = RingPoints(cam, center, PointTangent(), len);
                        for (int k = 0; k < ring.Count - 1; k++)
                            if (ring[k].visible && ring[k + 1].visible) Line(ring[k].p, ring[k + 1].p, col, w * 0.8f);
                        break;
                    }
                    case GizmoMode.Scale when point:
                    {
                        var e = ToGui(cam, center + PointRight() * len, out var v);
                        if (!v) break;
                        var col = Pick(0, DragKind.PointWidth);
                        Line(c, e, col, w);
                        Dot(e, 14f * ui, col);
                        break;
                    }
                    case GizmoMode.Rotate:
                        for (int i = 0; i < 3; i++)
                        {
                            var col = Pick(i, DragKind.Rotate);
                            var ring = RingPoints(cam, center, WorldAxes[i], len);
                            for (int k = 0; k < ring.Count - 1; k++)
                                if (ring[k].visible && ring[k + 1].visible) Line(ring[k].p, ring[k + 1].p, col, w * 0.8f);
                        }
                        break;
                    case GizmoMode.Scale:
                        if (_selected.IsRamp) break;
                        for (int i = 0; i < 3; i++)
                        {
                            var e = ToGui(cam, center + GizmoAxis(i) * len, out var v);
                            if (!v) continue;
                            var col = Pick(i, DragKind.ScaleAxis);
                            Line(c, e, col, w);
                            Dot(e, 14f * ui, col);
                        }
                        bool uniHot = _drag == DragKind.ScaleUniform || (_drag == DragKind.None && _hoverKind == DragKind.ScaleUniform);
                        Dot(c, 16f * ui, uniHot ? new Color(1f, 0.92f, 0.2f) : Color.white);
                        break;
                }
            }

            if (_selected.IsRamp)
            {
                var pts = _selected.Data.points;
                Vector2 prev = default;
                bool prevVis = false;
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = ToGui(cam, _selected.transform.TransformPoint(pts[i].position), out var v);
                    if (v && prevVis) Line(prev, p, new Color(1f, 1f, 1f, 0.35f), 2f * ui);
                    prev = p;
                    prevVis = v;
                }
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = ToGui(cam, _selected.transform.TransformPoint(pts[i].position), out var v);
                    if (!v) continue;
                    bool hot = i == _selectedPoint || (_drag == DragKind.None && _hoverKind == DragKind.RampPoint && _hoverAxis == i);
                    Dot(p, 18f * ui, Color.black);
                    Dot(p, 14f * ui, hot ? new Color(1f, 0.85f, 0.3f) : Color.white);
                }
            }
        }

        /// <summary>While editing, each launcher shows where its carriage shoots to.</summary>
        private void DrawLaunchPaths(Camera cam, float ui)
        {
            var color = new Color(1f, 0.75f, 0.2f, 0.9f);
            foreach (var o in _objects.Concat(_ghost != null ? new[] { _ghost } : new LevelObject[0]))
            {
                if (o == null || (o.Kind != ObjectKind.Launcher && o.Kind != ObjectKind.WallLauncher)) continue;
                var a = ToGui(cam, o.transform.position, out var av);
                var b = ToGui(cam, o.LaunchEnd, out var bv);
                if (!av || !bv) continue;
                Line(a, b, color, 3f * ui);
                Dot(b, 10f * ui, color);
                // A short line beyond the end shows the throw direction.
                var c = ToGui(cam, o.LaunchEnd + o.LaunchDirection * Mathf.Max(2f, o.Data.force * 0.3f), out var cv);
                if (cv) Line(b, c, new Color(1f, 1f, 1f, 0.5f), 2f * ui);
            }
        }

        private readonly Vector3[] _zoneCorners = new Vector3[8];
        private static readonly int[] BoxEdges = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };

        /// <summary>Outlines the launch zone above each Jump pad surface (brighter on the selection).</summary>
        private void DrawJumpZones(Camera cam, float ui)
        {
            foreach (var o in _objects)
            {
                if (o == null || !o.JumpZoneCorners(_zoneCorners)) continue;
                var color = o == _selected ? new Color(1f, 0.6f, 0.15f, 0.95f) : new Color(1f, 0.6f, 0.15f, 0.35f);
                for (int e = 0; e < BoxEdges.Length; e += 2)
                {
                    var a = ToGui(cam, _zoneCorners[BoxEdges[e]], out var av);
                    var b = ToGui(cam, _zoneCorners[BoxEdges[e + 1]], out var bv);
                    if (av && bv) Line(a, b, color, 2f * ui);
                }
            }
        }

        private static void Line(Vector2 a, Vector2 b, Color color, float width)
        {
            var d = b - a;
            float length = d.magnitude;
            if (length < 0.5f) return;
            var matrix = GUI.matrix;
            var old = GUI.color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.color = color;
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, length, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = old;
        }

        private static void Dot(Vector2 p, float size, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(p.x - size / 2f, p.y - size / 2f, size, size), Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>The object bar along the bottom: what each number key places. Clickable in mouse mode.</summary>
        private void DrawHotbar()
        {
            const float bh = 54f, gap = 6f;
            float bw = 106f;
            if (Docked)
            {
                // Centred in the toolbar, shrinking the buttons to fit beside the panel.
                var bar = DockToolbarRect;
                bw = Mathf.Min(bw, (bar.width - ToolbarPad * 2f - (Hotbar.Length - 1) * gap) / Hotbar.Length);
                float width = Hotbar.Length * bw + (Hotbar.Length - 1) * gap;
                _hotbarRect = new Rect(bar.x + (bar.width - width) / 2f, (ToolbarHeight - bh) / 2f, width, bh);
            }
            else
            {
                float total = Hotbar.Length * bw + (Hotbar.Length - 1) * gap;
                // Top centre: the help window and the game's own prompts use the bottom of the screen.
                _hotbarRect = new Rect((VW - total) / 2f, 12f, total, bh);
            }
            if (_hotbarStyle == null) _hotbarStyle = Theme.Chip;
            for (int i = 0; i < Hotbar.Length; i++)
            {
                var r = new Rect(_hotbarRect.x + i * (bw + gap), _hotbarRect.y, bw, bh);
                bool current = _kind == Hotbar[i] && (Hotbar[i] != ObjectKind.Ramp || _rampTool);
                string key = current ? HotbarKeyNames[i] : $"<color={Theme.MutedHex}>{HotbarKeyNames[i]}</color>";
                string label = $"<size=11>{key}</size>\n{KindName(Hotbar[i])}";
                bool clicked = GUI.Toggle(r, current, label, _hotbarStyle);
                if (clicked && !current && _cursorMode) SetKind(Hotbar[i]);
            }
        }

        private static Texture2D SolidTexture(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static string KindName(ObjectKind k)
        {
            switch (k)
            {
                case ObjectKind.JumpPad: return "Jump pad";
                case ObjectKind.KillZone: return "Kill zone";
                case ObjectKind.WallLauncher: return "Wall launcher";
                case ObjectKind.CogLauncher: return "Cog launcher";
                default: return k.ToString();
            }
        }

        // ---------------- hooks for DebugCommands ----------------

        public string DebugHandles()
        {
            var cam = Camera.main;
            if (cam == null || _selected == null) return "nothing selected";
            string F(Vector2 g) => $"{g.x / Screen.width:0.000} {g.y / Screen.height:0.000}";
            var center = _selected.transform.position;
            float len = GizmoLength(cam, center);
            var sb = new System.Text.StringBuilder();
            sb.Append($"selected {_selected.Kind} gizmo={_gizmo} center {F(ToGui(cam, center, out _))}");
            for (int i = 0; i < 3; i++) sb.Append($" | axis{i} {F(ToGui(cam, center + GizmoAxis(i) * len, out _))}");
            sb.Append($" | plane {F(ToGui(cam, PlaneHandlePos(center, len), out _))}");
            if (_selected.IsRamp)
                for (int i = 0; i < _selected.Data.points.Count; i++)
                    sb.Append($" | point{i} {F(ToGui(cam, _selected.transform.TransformPoint(_selected.Data.points[i].position), out _))}");
            return sb.ToString();
        }

        public void DebugSetGizmo(string mode)
        {
            _gizmo = mode == "rotate" ? GizmoMode.Rotate : mode == "scale" ? GizmoMode.Scale : GizmoMode.Move;
        }

        public string DebugSelect(string which)
        {
            if (_objects.Count == 0) return "no objects";
            int i = which == "last" ? _objects.Count - 1 : int.TryParse(which, out var n) ? n : -1;
            if (i < 0 || i >= _objects.Count) return "bad index";
            Select(_objects[i]);
            return $"selected {i} {_objects[i].Kind} at {_objects[i].Data.position}";
        }

        public string DebugJson() => _selected == null ? "none" : LevelJson.ObjectToJson(_selected.Data);

        public string DebugSave(string name)
        {
            _level.name = name;
            SaveLevel();
            return "saved " + name;
        }

        public string DebugLoad(string name)
        {
            LoadLevel(name);
            return $"loaded {name}: {_objects.Count} objects, ramps with points: " +
                   string.Join(",", _objects.Where(o => o.IsRamp).Select(o => o.Data.points.Count.ToString()));
        }

        public string DebugSurface(string surface)
        {
            if (_selected == null) return "nothing selected";
            if (!System.Enum.TryParse(surface, true, out SurfaceType s)) return "unknown surface";
            _selected.Data.Surface = s;
            _selected.Refresh();
            return "surface = " + s;
        }

        /// <summary>Sets a number field (force, travel, spin, ...) of the selected object.</summary>
        public string DebugSetField(string field, float value)
        {
            if (_selected == null) return "nothing selected";
            var f = typeof(LevelObjectData).GetField(field);
            if (f == null || f.FieldType != typeof(float)) return "no float field " + field;
            f.SetValue(_selected.Data, value);
            _selected.Refresh();
            return $"{field} = {value}";
        }

        public void DebugUndo(bool redo) { if (redo) Redo(); else Undo(); }

        public string DebugRamp(string profile, List<Vector3> worldPoints)
        {
            if (!DebugSessionReady) return "no session";
            if (worldPoints.Count < 2) return "need 2+ points";
            var pts = worldPoints.Select(p => new RampPoint { position = p, width = 4f }).ToList();
            var data = DraftData(pts);
            if (System.Enum.TryParse(profile, true, out RampProfile prof)) data.Profile = prof;
            PushUndo();
            var o = LevelObject.Create(data, _root);
            o.SetEditorVisible(IsEditing);
            _objects.Add(o);
            Select(o);
            var b = o.GetBounds();
            return $"ramp {data.Profile} with {pts.Count} points, bounds {b.center} size {b.size}";
        }

        public string DebugSelectedInfo() =>
            _selected == null ? "selected: none" :
            $"selected {_selected.Kind} live {_selected.transform.position} liveRot {_selected.transform.eulerAngles} pos {_selected.Data.position} rot {_selected.Data.rotation} scale {_selected.Data.scale}" +
            (_selected.IsRamp ? " points " + string.Join(" ", _selected.Data.points.Select(p => p.position.ToString())) : "") +
            $" undo={_undo.Count} redo={_redo.Count} objects={_objects.Count}";
    }
}
