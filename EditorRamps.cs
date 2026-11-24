using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>The ramp tool: draw a ramp by clicking points, and the panel section that edits one.</summary>
    public partial class LevelEditorController
    {
        /// <summary>True while number key 2 (Ramp) is chosen and clicks add ramp points.</summary>
        private bool _rampTool;
        /// <summary>Points of the ramp being drawn, in world space.</summary>
        private readonly List<RampPoint> _draft = new List<RampPoint>();
        private float _draftHeight, _draftWidth = 4f, _draftBank;
        private Vector3 _draftCursor, _draftGround;
        private bool _draftSnapped;
        /// <summary>Shape settings for new ramps: copied from the last ramp you edited.</summary>
        private LevelObjectData _rampTemplate = LevelObjectData.CreateDefault(ObjectKind.Ramp);

        private const float JoinDistance = 1.5f;

        private void RampDrawUpdate(bool fast)
        {
            // ---- the next point, under the crosshair ----
            var ray = CenterRay();
            _draftGround = RaycastSurface(ray, 120f, out var hit) ? hit.point : ray.GetPoint(_ghostDistance);
            if (_snap) _draftGround = new Vector3(Snap(_draftGround.x), _draftGround.y, Snap(_draftGround.z));

            _draftSnapped = false;
            RampPoint joinPoint = null;
            Vector3 joinOutward = Vector3.zero;
            if (_draft.Count == 0 && FindRampEnd(_draftGround + Vector3.up * _draftHeight, out var joinPos, out joinOutward, out joinPoint))
            {
                _draftCursor = joinPos;
                _draftSnapped = true;
            }
            else
            {
                // A little above the surface so the ramp doesn't flicker against the floor it starts on.
                _draftCursor = _draftGround + Vector3.up * (_draftHeight + 0.02f);
            }

            // ---- adjust the next point ----
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f)
            {
                float step = Mathf.Sign(wheel) * Mathf.Max(1f, Mathf.Round(Mathf.Abs(wheel))) * 0.5f; // one 0.5 m step per wheel notch
                if (Input.GetKey(KeyCode.LeftAlt)) _ghostDistance = Mathf.Clamp(_ghostDistance * Mathf.Pow(1.1f, wheel), 2f, 80f);
                else if (fast) _draftWidth = Mathf.Clamp(_draftWidth + step, 0.5f, 60f);
                else _draftHeight = Mathf.Clamp(_draftHeight + step, -50f, 100f);
            }
            float bankStep = fast ? 15f : 5f;
            if (Input.GetKeyDown(KeyCode.Q)) _draftBank = Mathf.Clamp(_draftBank - bankStep, -90f, 90f);
            if (Input.GetKeyDown(KeyCode.E)) _draftBank = Mathf.Clamp(_draftBank + bankStep, -90f, 90f);
            if (Input.GetKeyDown(KeyCode.T)) { _draftHeight = 0f; _draftBank = 0f; }

            // ---- add / remove / finish ----
            if (Input.GetMouseButtonDown(0))
            {
                if (_draft.Count >= 2 && Vector3.Distance(_draftCursor, _draft[_draft.Count - 1].position) < 0.6f)
                {
                    FinishRamp();
                    return;
                }
                if (_draftSnapped)
                {
                    // Continue the other ramp: same width and bank, and a short straight piece so the join is smooth.
                    _draftWidth = joinPoint.width;
                    _draftBank = joinPoint.bank;
                    _draft.Add(new RampPoint { position = _draftCursor, width = _draftWidth, bank = _draftBank });
                    _draft.Add(new RampPoint { position = _draftCursor + joinOutward * 2f, width = _draftWidth, bank = _draftBank });
                    _draftHeight = _draft[1].position.y - _draftGround.y;
                    Toast("Joined to the ramp's end");
                }
                else
                {
                    _draft.Add(new RampPoint { position = _draftCursor, width = _draftWidth, bank = _draftBank });
                }
            }
            if (Input.GetKeyDown(KeyCode.Backspace) && _draft.Count > 0) _draft.RemoveAt(_draft.Count - 1);
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && _draft.Count >= 2)
            {
                FinishRamp();
                return;
            }
            if (Input.GetMouseButtonDown(1))
            {
                var aimed = AimedObject();
                if (aimed != null && _draft.Count == 0) { PickUp(aimed); return; }
            }

            UpdateDraftPreview();
        }

        /// <summary>The ghost shows the ramp so far plus the point under the crosshair.</summary>
        private void UpdateDraftPreview()
        {
            if (_draft.Count == 0)
            {
                DestroyGhost();
                return;
            }
            var points = _draft.Select(p => p.Clone()).ToList();
            points.Add(new RampPoint { position = _draftCursor, width = _draftWidth, bank = _draftBank });
            var data = DraftData(points);
            if (_ghost == null || !_ghost.IsRamp)
            {
                SpawnGhost(data);
            }
            else
            {
                _ghost.Data = data;
                _ghost.Refresh();
            }
        }

        /// <summary>A ramp object at the first point, with the points stored relative to it.</summary>
        private LevelObjectData DraftData(List<RampPoint> worldPoints)
        {
            var d = _rampTemplate.Clone();
            d.Kind = ObjectKind.Ramp;
            d.position = worldPoints[0].position;
            d.rotation = Vector3.zero;
            d.scale = Vector3.one;
            d.points = worldPoints.Select(p => new RampPoint { position = p.position - worldPoints[0].position, width = p.width, bank = p.bank }).ToList();
            return d;
        }

        private void FinishRamp()
        {
            if (_draft.Count < 2) return;
            PushUndo();
            DestroyGhost();
            var obj = LevelObject.Create(DraftData(_draft), _root);
            obj.SetEditorVisible(true);
            _objects.Add(obj);
            Select(obj);
            _draft.Clear();
            _draftHeight = 0f;
            Toast($"Ramp placed. {CursorModeKey.Value} to drag its points", 3f);
        }

        /// <summary>The closest free ramp end near <paramref name="p"/>, to start a new ramp from.</summary>
        private bool FindRampEnd(Vector3 p, out Vector3 position, out Vector3 outward, out RampPoint point)
        {
            position = outward = Vector3.zero;
            point = null;
            float best = JoinDistance;
            foreach (var ramp in _objects.Where(o => o.IsRamp))
            {
                foreach (bool atEnd in new[] { true, false })
                {
                    if (!RampMesh.EndInfo(ramp, atEnd, out var pos, out var dir, out var pt)) continue;
                    float dist = Vector3.Distance(p, pos);
                    if (dist >= best) continue;
                    best = dist;
                    position = pos;
                    outward = dir;
                    point = pt;
                }
            }
            return point != null;
        }

        // ---------------- panel ----------------

        private void DrawRampFields(LevelObject obj)
        {
            var d = obj.Data;
            GUILayout.Label("Shape across the track", Theme.Hint);
            GUILayout.BeginHorizontal();
            foreach (var prof in new[] { RampProfile.Flat, RampProfile.Gutter, RampProfile.HalfPipe })
                if (GUILayout.Toggle(d.Profile == prof, ProfileName(prof), Theme.Segment)) d.Profile = prof;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var prof in new[] { RampProfile.QuarterPipe, RampProfile.Walls })
                if (GUILayout.Toggle(d.Profile == prof, ProfileName(prof), Theme.Segment)) d.Profile = prof;
            GUILayout.EndHorizontal();
            d.thickness = Mathf.Clamp(FloatField("Thickness", "thick", d.thickness, 0.25f), 0.05f, 20f);
            if (d.Profile != RampProfile.Flat)
                d.wallHeight = Mathf.Clamp(FloatField(d.Profile == RampProfile.Gutter ? "Depth" : "Wall height", "wallH", d.wallHeight, 0.25f), 0.1f, 50f);
            d.followTwist = GUILayout.Toggle(d.followTwist, "Free twist (needed for loops)");

            var pts = d.points;
            if (pts.Count == 0) return;
            if (_selectedPoint < 0 || _selectedPoint >= pts.Count) _selectedPoint = 0;
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30))) ChoosePoint((_selectedPoint + pts.Count - 1) % pts.Count);
            GUILayout.Label($"<b>Point {_selectedPoint + 1} of {pts.Count}</b>", RichLabel());
            if (GUILayout.Button(">", GUILayout.Width(30))) ChoosePoint((_selectedPoint + 1) % pts.Count);
            GUILayout.EndHorizontal();

            var pt = pts[_selectedPoint];
            var world = obj.transform.TransformPoint(pt.position);
            var newWorld = VectorField("Point position", "pt", world, 0.5f);
            if (newWorld != world) pt.position = obj.transform.InverseTransformPoint(newWorld);
            pt.width = Mathf.Clamp(FloatField("Width", "ptW", pt.width, 0.5f), 0.2f, 60f);
            pt.bank = Mathf.Clamp(FloatField("Bank", "ptB", pt.bank, 5f), -180f, 180f);
            GUILayout.Label("Bank: positive raises the right edge.", Theme.Hint);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Insert point after"))
            {
                RampPoint inserted;
                if (_selectedPoint == pts.Count - 1)
                {
                    var dir = (pt.position - pts[_selectedPoint - 1].position).normalized;
                    inserted = new RampPoint { position = pt.position + dir * 4f, width = pt.width, bank = pt.bank };
                }
                else
                {
                    var next = pts[_selectedPoint + 1];
                    inserted = new RampPoint { position = (pt.position + next.position) * 0.5f, width = (pt.width + next.width) * 0.5f, bank = (pt.bank + next.bank) * 0.5f };
                }
                pts.Insert(_selectedPoint + 1, inserted);
                ChoosePoint(_selectedPoint + 1);
            }
            GUI.enabled = pts.Count > 2;
            if (GUILayout.Button("Delete point")) DeleteRampPoint(obj);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Use this width and bank for every point"))
                foreach (var other in pts) { other.width = pt.width; other.bank = pt.bank; }

            RememberRampStyle(d);
        }

        private static string ProfileName(RampProfile p) =>
            p == RampProfile.HalfPipe ? "Half-pipe" : p == RampProfile.QuarterPipe ? "Quarter-pipe" : p.ToString();

        private void RememberRampStyle(LevelObjectData d)
        {
            _rampTemplate.profile = d.profile;
            _rampTemplate.thickness = d.thickness;
            _rampTemplate.wallHeight = d.wallHeight;
            _rampTemplate.color = d.color;
            _rampTemplate.surface = d.surface;
        }

        private void ChoosePoint(int index)
        {
            _selectedPoint = index;
            foreach (var key in _fieldText.Keys.Where(k => k.StartsWith("pt")).ToList()) _fieldText.Remove(key);
        }

        private void DeleteRampPoint(LevelObject ramp)
        {
            var pts = ramp.Data.points;
            if (pts.Count <= 2 || _selectedPoint < 0 || _selectedPoint >= pts.Count) return;
            pts.RemoveAt(_selectedPoint);
            ChoosePoint(Mathf.Min(_selectedPoint, pts.Count - 1));
            ramp.Refresh();
        }
    }
}
