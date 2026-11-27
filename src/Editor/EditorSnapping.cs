using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// Snapping (toggled with G). An object being placed or moved locks flush against, or lines up
    /// with, the faces, edges and centres of nearby objects; on axes with nothing to lock to, its
    /// centre snaps to the grid. A faint grid shows around it, and the objects it locked to light up.
    /// </summary>
    public partial class LevelEditorController
    {
        public ConfigEntry<float> GridSize;
        public ConfigEntry<bool> ObjectSnap;

        public static readonly float[] GridSizes = { 0.25f, 0.5f, 1f, 2f };

        /// <summary>How close (m) a face or centre has to be to lock onto another object's.</summary>
        private const float SnapReach = 0.5f;
        /// <summary>Only objects this close (m) to the moving one are considered.</summary>
        private const float SnapNeighbourhood = 3f;

        /// <summary>The objects the last snap locked onto, outlined while editing.</summary>
        private readonly List<Bounds> _snapTargets = new List<Bounds>();
        private int _snapTargetsFrame = -1;

        private float GridStep => Mathf.Clamp(GridSize != null ? GridSize.Value : 0.5f, 0.05f, 10f);
        private bool ObjectSnapOn => ObjectSnap == null || ObjectSnap.Value;

        private float Snap(float v) => Mathf.Round(v / GridStep) * GridStep;

        /// <summary>
        /// Snaps <paramref name="pos"/> (where <paramref name="mover"/> would go) on the chosen axes.
        /// <paramref name="boundsAtPos"/> is the mover's world bounds with it at <paramref name="pos"/>.
        /// Each axis locks to the nearest face, edge or centre of a nearby object within reach,
        /// otherwise to the grid.
        /// </summary>
        private Vector3 SnapPosition(LevelObject mover, Bounds boundsAtPos, Vector3 pos, bool x, bool y, bool z)
        {
            _snapTargets.Clear();
            _snapTargetsFrame = Time.frameCount;
            if (!_snap) return pos;
            bool[] axes = { x, y, z };
            var locked = new bool[3];

            if (ObjectSnapOn && mover != null && !mover.IsRamp)
            {
                var best = new float[] { SnapReach, SnapReach, SnapReach };
                var shift = new float[3];
                var targets = new Bounds?[3];
                var near = boundsAtPos;
                near.Expand(SnapNeighbourhood * 2f);
                foreach (var o in _objects)
                {
                    if (o == null || o == mover || o.IsRamp) continue;
                    var t = o.GetBounds();
                    if (!near.Intersects(t)) continue;
                    for (int i = 0; i < 3; i++)
                    {
                        if (!axes[i]) continue;
                        float mMin = boundsAtPos.min[i], mMax = boundsAtPos.max[i], mC = boundsAtPos.center[i];
                        float tMin = t.min[i], tMax = t.max[i], tC = t.center[i];
                        // Flush (touching) first, so it wins a tie with an edge line-up.
                        Consider(i, tMax - mMin, t);
                        Consider(i, tMin - mMax, t);
                        Consider(i, tMin - mMin, t);
                        Consider(i, tMax - mMax, t);
                        Consider(i, tC - mC, t);
                    }
                }
                for (int i = 0; i < 3; i++)
                {
                    if (targets[i] == null) continue;
                    pos[i] += shift[i];
                    locked[i] = true;
                    if (!_snapTargets.Contains(targets[i].Value)) _snapTargets.Add(targets[i].Value);
                }

                void Consider(int axis, float d, Bounds target)
                {
                    if (Mathf.Abs(d) >= best[axis]) return;
                    best[axis] = Mathf.Abs(d);
                    shift[axis] = d;
                    targets[axis] = target;
                }
            }

            for (int i = 0; i < 3; i++)
                if (axes[i] && !locked[i]) pos[i] = Snap(pos[i]);
            return pos;
        }

        /// <summary>The mover's bounds as if it stood at <paramref name="pos"/> instead of where it is now.</summary>
        private static Bounds BoundsAt(LevelObject o, Vector3 pos)
        {
            var b = o.GetBounds();
            b.center += pos - o.transform.position;
            return b;
        }

        // ---------------- drawing ----------------

        private readonly Vector3[] _snapCorners = new Vector3[8];

        /// <summary>The grid under the piece being placed or moved, and outlines on what it locked onto.</summary>
        private void DrawSnapGuides(Camera cam, float ui)
        {
            if (!_snap) return;
            LevelObject piece = null;
            if (_cursorMode) piece = _selected;
            else if (!_rampTool && _ghost != null && _ghost.gameObject.activeSelf) piece = _ghost;
            BeginLineBatch(); // the grid and outlines are hundreds of lines: one draw call instead
            if (piece != null && !piece.IsRamp) DrawGrid(cam, ui, piece.GetBounds());

            // Outlines last only while the snap that found them is current (this frame or the last).
            if (Time.frameCount - _snapTargetsFrame > 1) { EndLineBatch(); return; }
            var color = new Color(0.3f, 0.95f, 1f, 0.9f);
            foreach (var b in _snapTargets)
            {
                int k = 0;
                for (int yy = 0; yy < 2; yy++)
                    for (int zz = 0; zz < 2; zz++)
                        for (int xx = 0; xx < 2; xx++)
                            _snapCorners[k++] = new Vector3(xx == 0 ? b.min.x : b.max.x, yy == 0 ? b.min.y : b.max.y, zz == 0 ? b.min.z : b.max.z);
                for (int e = 0; e < BoxEdges.Length; e += 2)
                {
                    var a = ToGui(cam, _snapCorners[BoxEdges[e]], out var av);
                    var c = ToGui(cam, _snapCorners[BoxEdges[e + 1]], out var cv);
                    if (av && cv) Line(a, c, color, 2.5f * ui);
                }
            }
            EndLineBatch();
        }

        /// <summary>A faint grid on the level of the piece's base, fading out around it.</summary>
        private void DrawGrid(Camera cam, float ui, Bounds b)
        {
            float step = GridStep;
            int n = Mathf.Clamp(Mathf.CeilToInt(4f / step), 4, 10);
            float cx = Snap(b.center.x), cz = Snap(b.center.z), y = b.min.y + 0.02f;
            float w = 1.5f * ui;
            for (int k = -n; k <= n; k++)
            {
                for (int s = -n; s < n; s++)
                {
                    // Fade with distance from the centre of the grid.
                    float r = Mathf.Max(Mathf.Abs(k), Mathf.Max(Mathf.Abs(s), Mathf.Abs(s + 1))) / (float)n;
                    var color = new Color(1f, 1f, 1f, 0.28f * (1f - r * r));
                    if (color.a < 0.02f) continue;
                    // Line along Z at x = k, then line along X at z = k.
                    DrawGridSegment(cam, new Vector3(cx + k * step, y, cz + s * step), new Vector3(cx + k * step, y, cz + (s + 1) * step), color, w);
                    DrawGridSegment(cam, new Vector3(cx + s * step, y, cz + k * step), new Vector3(cx + (s + 1) * step, y, cz + k * step), color, w);
                }
            }
        }

        private void DrawGridSegment(Camera cam, Vector3 a, Vector3 b, Color color, float width)
        {
            var ga = ToGui(cam, a, out var av);
            var gb = ToGui(cam, b, out var bv);
            if (av && bv) Line(ga, gb, color, width);
        }

        /// <summary>The snap settings, for the Controls tab.</summary>
        private void DrawSnapSettings()
        {
            bool on = GUILayout.Toggle(_snap, "Snapping on (G)");
            if (on != _snap) SetSnap(on);
            if (ObjectSnap != null)
            {
                bool obj = GUILayout.Toggle(ObjectSnap.Value, "Snap to other objects' faces and edges");
                if (obj != ObjectSnap.Value) ObjectSnap.Value = obj;
            }
            if (GridSize != null)
            {
                GUILayout.Label("Grid size", Theme.Hint);
                int current = System.Array.IndexOf(GridSizes, GridSize.Value);
                int pick = SegmentRow(current, new[] { "0.25 m", "0.5 m", "1 m", "2 m" });
                if (pick >= 0 && pick != current) GridSize.Value = GridSizes[pick];
            }
        }
    }
}
