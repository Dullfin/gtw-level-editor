using System.Collections.Generic;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// A hole a hollow cylinder cuts through a platform: the tube's inside, as a polygon on the two
    /// platform faces it passes through. Everything is in the platform's local unit-cube space.
    /// </summary>
    internal class CutHole
    {
        /// <summary>The platform's local axis the tube runs along (0 = X, 1 = Y, 2 = Z).</summary>
        public int Axis;
        /// <summary>The hole's outline across that axis, as (u, v) with u = axis+1, v = axis+2 (mod 3).</summary>
        public List<Vector2> Outline;
    }

    /// <summary>Finds where hollow cylinders pass through platforms and rebuilds those platforms with holes.</summary>
    public partial class LevelEditorController
    {
        private int _cutSignature;
        private const int HoleSegments = 32;

        /// <summary>Recomputes the holes whenever a platform or hollow cylinder was placed, moved, resized or changed.</summary>
        private void UpdateTubeCuts()
        {
            int sig = 17;
            unchecked
            {
                foreach (var o in _objects)
                {
                    if (o == null) continue;
                    var d = o.Data;
                    if (d.Kind != ObjectKind.Platform && !d.IsHollow) continue;
                    sig = sig * 31 + o.GetInstanceID();
                    sig = sig * 31 + d.kind.GetHashCode();
                    sig = sig * 31 + d.position.GetHashCode();
                    sig = sig * 31 + d.rotation.GetHashCode();
                    sig = sig * 31 + d.scale.GetHashCode();
                    sig = sig * 31 + d.hollow.GetHashCode();
                    sig = sig * 31 + d.wallThickness.GetHashCode();
                    sig = sig * 31 + d.cutsPlatforms.GetHashCode();
                }
            }
            if (sig == _cutSignature) return;
            _cutSignature = sig;

            var tubes = new List<LevelObjectData>();
            foreach (var o in _objects) if (o != null && o.Data.IsHollow && o.Data.cutsPlatforms) tubes.Add(o.Data);
            foreach (var o in _objects)
            {
                if (o == null || o.Data.Kind != ObjectKind.Platform) continue;
                var holes = new List<CutHole>();
                foreach (var t in tubes)
                {
                    var hole = HoleFor(o.Data, t);
                    if (hole != null && !OverlapsAny(hole, holes)) holes.Add(hole);
                }
                o.SetCuts(holes);
            }
        }

        /// <summary>
        /// The hole tube <paramref name="t"/> cuts through platform <paramref name="p"/>, or null when it
        /// doesn't pass straight through it (it must run along one of the platform's axes, and its opening
        /// must fit inside the platform's face).
        /// </summary>
        private static CutHole HoleFor(LevelObjectData p, LevelObjectData t)
        {
            var pm = Matrix4x4.TRS(p.position, Quaternion.Euler(p.rotation), p.scale);
            var tm = Matrix4x4.TRS(t.position, Quaternion.Euler(t.rotation), t.scale);
            var toPlatform = pm.inverse * tm;

            // Which platform axis does the tube's axis run along? (Compare directions without scale.)
            var tubeAxis = Quaternion.Inverse(Quaternion.Euler(p.rotation)) * (Quaternion.Euler(t.rotation) * Vector3.up);
            int axis = -1;
            for (int i = 0; i < 3; i++) if (Mathf.Abs(tubeAxis[i]) > 0.996f) axis = i;
            if (axis < 0) return null;
            int u = (axis + 1) % 3, v = (axis + 2) % 3;

            // The tube must reach into the platform along that axis.
            float e0 = toPlatform.MultiplyPoint3x4(new Vector3(0f, -0.5f, 0f))[axis];
            float e1 = toPlatform.MultiplyPoint3x4(new Vector3(0f, 0.5f, 0f))[axis];
            if (Mathf.Max(e0, e1) < -0.5f || Mathf.Min(e0, e1) > 0.5f) return null;

            // The tube's inner wall, in the platform's (u, v) face coordinates.
            float wall = Mathf.Clamp(t.wallThickness, 0.02f, Mathf.Max(0.02f, Mathf.Min(t.scale.x, t.scale.z) * 0.5f - 0.02f));
            float ix = Mathf.Max(0.005f, 0.5f - wall / Mathf.Max(0.01f, t.scale.x));
            float iz = Mathf.Max(0.005f, 0.5f - wall / Mathf.Max(0.01f, t.scale.z));
            const float margin = 0.01f;
            var outline = new List<Vector2>(HoleSegments);
            for (int i = 0; i < HoleSegments; i++)
            {
                float a = i / (float)HoleSegments * Mathf.PI * 2f;
                var q = toPlatform.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * ix, 0f, Mathf.Sin(a) * iz));
                var pt = new Vector2(q[u], q[v]);
                if (Mathf.Abs(pt.x) > 0.5f - margin || Mathf.Abs(pt.y) > 0.5f - margin) return null; // the opening runs off the edge
                outline.Add(pt);
            }
            if (SignedArea(outline) > 0f) outline.Reverse(); // holes wind clockwise
            return new CutHole { Axis = axis, Outline = outline };
        }

        private static bool OverlapsAny(CutHole hole, List<CutHole> others)
        {
            foreach (var o in others)
            {
                if (o.Axis != hole.Axis) continue;
                foreach (var p in hole.Outline) if (PointInPolygon(p, o.Outline)) return true;
                foreach (var p in o.Outline) if (PointInPolygon(p, hole.Outline)) return true;
            }
            return false;
        }

        internal static float SignedArea(List<Vector2> poly)
        {
            float a = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % poly.Count];
                a += p.x * q.y - q.x * p.y;
            }
            return a * 0.5f;
        }

        internal static bool PointInPolygon(Vector2 p, List<Vector2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }

    /// <summary>Builds a box (the platform's unit cube) with holes through it, and triangulates faces with holes.</summary>
    internal static class HoleyBox
    {
        public static void Build(Mesh mesh, List<CutHole> holes)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f) { var t = b; b = c; c = t; }
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }

            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                Vector3 P(Vector2 q, float w)
                {
                    var p = Vector3.zero;
                    p[u] = q.x; p[v] = q.y; p[axis] = w;
                    return p;
                }

                var mine = holes.FindAll(h => h.Axis == axis);
                var outer = new List<Vector2> { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
                var faceTris = Triangulate(outer, mine.ConvertAll(h => h.Outline));
                for (int side = -1; side <= 1; side += 2)
                {
                    var n = Vector3.zero;
                    n[axis] = side;
                    float w = side * 0.5f;
                    for (int i = 0; i < faceTris.Count; i += 3)
                        Tri(P(faceTris[i], w), P(faceTris[i + 1], w), P(faceTris[i + 2], w), n);
                }

                // The inside wall of each hole, facing into the hole.
                foreach (var h in mine)
                {
                    var centre = Vector2.zero;
                    foreach (var q in h.Outline) centre += q;
                    centre /= h.Outline.Count;
                    for (int i = 0; i < h.Outline.Count; i++)
                    {
                        var a = h.Outline[i];
                        var b = h.Outline[(i + 1) % h.Outline.Count];
                        var into = centre - (a + b) * 0.5f;
                        var n = P(into, 0f);
                        Tri(P(a, -0.5f), P(b, -0.5f), P(b, 0.5f), n);
                        Tri(P(a, -0.5f), P(b, 0.5f), P(a, 0.5f), n);
                    }
                }
            }

            mesh.Clear();
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Ear-clipping triangulation of a counter-clockwise outline with clockwise holes. Each hole is
        /// first bridged into the outline, making one polygon. Returns triangle corners, three at a time.
        /// </summary>
        public static List<Vector2> Triangulate(List<Vector2> outer, List<List<Vector2>> holes)
        {
            var poly = new List<Vector2>(outer);
            var pending = new List<List<Vector2>>(holes);
            pending.Sort((a, b) => MaxX(b).CompareTo(MaxX(a)));
            for (int h = 0; h < pending.Count; h++)
            {
                var hole = pending[h];
                int m = 0;
                for (int i = 1; i < hole.Count; i++) if (hole[i].x > hole[m].x) m = i;
                var hp = hole[m];
                int best = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < poly.Count; i++)
                {
                    float d = (poly[i] - hp).sqrMagnitude;
                    if (d >= bestDist || !Visible(hp, poly[i], poly, pending, h)) continue;
                    best = i;
                    bestDist = d;
                }
                if (best < 0) continue; // shouldn't happen for holes inside the face
                var merged = new List<Vector2>(poly.Count + hole.Count + 2);
                for (int i = 0; i <= best; i++) merged.Add(poly[i]);
                for (int i = 0; i <= hole.Count; i++) merged.Add(hole[(m + i) % hole.Count]);
                merged.Add(poly[best]);
                for (int i = best + 1; i < poly.Count; i++) merged.Add(poly[i]);
                poly = merged;
            }
            return EarClip(poly);
        }

        private static float MaxX(List<Vector2> p)
        {
            float m = float.MinValue;
            foreach (var q in p) m = Mathf.Max(m, q.x);
            return m;
        }

        /// <summary>Whether the bridge a-b crosses no edge of the polygon or of the holes still to merge.</summary>
        private static bool Visible(Vector2 a, Vector2 b, List<Vector2> poly, List<List<Vector2>> holes, int from)
        {
            if (Crosses(a, b, poly)) return false;
            for (int h = from; h < holes.Count; h++) if (Crosses(a, b, holes[h])) return false;
            return true;
        }

        private static bool Crosses(Vector2 a, Vector2 b, List<Vector2> loop)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                var c = loop[i];
                var d = loop[(i + 1) % loop.Count];
                if (Same(c, a) || Same(c, b) || Same(d, a) || Same(d, b)) continue;
                if (SegmentsIntersect(a, b, c, d)) return true;
            }
            return false;
        }

        private static bool Same(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-10f;

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float d1 = Cross(p3, p4, p1), d2 = Cross(p3, p4, p2), d3 = Cross(p1, p2, p3), d4 = Cross(p1, p2, p4);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static List<Vector2> EarClip(List<Vector2> input)
        {
            var result = new List<Vector2>();
            var idx = new List<int>();
            for (int i = 0; i < input.Count; i++) idx.Add(i);
            int guard = input.Count * input.Count + 10;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    var a = input[idx[(k + idx.Count - 1) % idx.Count]];
                    var b = input[idx[k]];
                    var c = input[idx[(k + 1) % idx.Count]];
                    if (Cross(a, b, c) <= 1e-9f) continue; // reflex or flat
                    bool empty = true;
                    for (int j = 0; j < idx.Count && empty; j++)
                    {
                        var p = input[idx[j]];
                        if (Same(p, a) || Same(p, b) || Same(p, c)) continue;
                        if (Cross(a, b, p) >= 0f && Cross(b, c, p) >= 0f && Cross(c, a, p) >= 0f) empty = false;
                    }
                    if (!empty) continue;
                    result.Add(a); result.Add(b); result.Add(c);
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break; // numerical trouble: leave the rest open rather than loop forever
            }
            if (idx.Count == 3)
            {
                result.Add(input[idx[0]]); result.Add(input[idx[1]]); result.Add(input[idx[2]]);
            }
            return result;
        }
    }
}
