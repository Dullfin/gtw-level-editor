using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GTWLevelEditor
{
    /// <summary>
    /// Builds a solid ramp mesh by sweeping a cross-section (flat, gutter, half-pipe, ...)
    /// along a smooth curve through the ramp's control points.
    /// </summary>
    public static class RampMesh
    {
        private const float SampleSpacing = 0.4f;

        public struct Sample
        {
            public Vector3 Pos, Tangent, Right, Up;
            public float Width;
        }

        /// <summary>Points along the curve with a frame (tangent, right, up) at each, in local space.</summary>
        public static List<Sample> SampleCurve(LevelObjectData d)
        {
            var result = new List<Sample>();
            var pts = d.points;
            if (pts == null || pts.Count < 2) return result;

            // Centripetal Catmull-Rom through the points; end segments use mirrored phantom points.
            var raw = new List<(Vector3 pos, float width, float bank)>();
            int m = pts.Count;
            for (int k = 0; k < m - 1; k++)
            {
                Vector3 p1 = pts[k].position, p2 = pts[k + 1].position;
                Vector3 p0 = k > 0 ? pts[k - 1].position : 2f * p1 - p2;
                Vector3 p3 = k + 2 < m ? pts[k + 2].position : 2f * p2 - p1;
                int n = Mathf.Clamp(Mathf.CeilToInt((p2 - p1).magnitude / SampleSpacing), 2, 120);
                int count = k == m - 2 ? n + 1 : n; // the last segment includes its end point
                for (int j = 0; j < count; j++)
                {
                    float t = j / (float)n;
                    float e = t * t * (3f - 2f * t);
                    raw.Add((CatmullRom(p0, p1, p2, p3, t),
                             Mathf.Lerp(pts[k].width, pts[k + 1].width, e),
                             Mathf.Lerp(pts[k].bank, pts[k + 1].bank, e)));
                }
            }

            // Drop repeated positions (two control points in the same place).
            for (int i = raw.Count - 1; i > 0; i--)
                if ((raw[i].pos - raw[i - 1].pos).sqrMagnitude < 1e-8f) raw.RemoveAt(i);
            if (raw.Count < 2) return result;

            Vector3 prevT = Vector3.forward, prevR = Vector3.right;
            var tangents = new List<Vector3>(raw.Count);
            var rights = new List<Vector3>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                var t = raw[Mathf.Min(i + 1, raw.Count - 1)].pos - raw[Mathf.Max(i - 1, 0)].pos;
                t = t.sqrMagnitude < 1e-10f ? prevT : t.normalized;
                Vector3 r;
                if (i > 0 && d.followTwist)
                {
                    // Parallel transport: the track keeps its own twist, so loops work.
                    r = Quaternion.FromToRotation(prevT, t) * prevR;
                }
                else if (Mathf.Abs(Vector3.Dot(t, Vector3.up)) < 0.98f)
                {
                    // Keep the track's "up" towards the sky, so helixes and turns stay level.
                    r = Vector3.Cross(Vector3.up, t);
                }
                else
                {
                    r = i > 0 ? prevR : Vector3.right; // going straight up or down: keep the last right
                }
                r -= Vector3.Dot(r, t) * t;
                r = r.sqrMagnitude < 1e-8f ? Vector3.Cross(t, Vector3.forward) : r;
                r.Normalize();
                prevT = t;
                prevR = r;

                tangents.Add(t);
                rights.Add(r);
            }

            // A free-twist track that drifts sideways (a corkscrew-like loop) comes out rolled over.
            // Spread a roll correction along its length so it ends level, like a track that turns back
            // to the sky (only when the end isn't pointing straight up or down).
            if (d.followTwist && rights.Count > 1)
            {
                int last = rights.Count - 1;
                var tEnd = tangents[last];
                if (Mathf.Abs(Vector3.Dot(tEnd, Vector3.up)) < 0.95f)
                {
                    var level = Vector3.Cross(Vector3.up, tEnd).normalized;
                    float roll = Vector3.SignedAngle(rights[last], level, tEnd);
                    float total = 0f;
                    var lengths = new float[rights.Count];
                    for (int i = 1; i < raw.Count; i++) lengths[i] = total += Vector3.Distance(raw[i].pos, raw[i - 1].pos);
                    for (int i = 1; i <= last; i++)
                        rights[i] = Quaternion.AngleAxis(roll * lengths[i] / Mathf.Max(1e-4f, total), tangents[i]) * rights[i];
                }
            }

            for (int i = 0; i < raw.Count; i++)
            {
                var q = Quaternion.AngleAxis(raw[i].bank, tangents[i]);
                var right = q * rights[i];
                result.Add(new Sample
                {
                    Pos = raw[i].pos,
                    Tangent = tangents[i],
                    Right = right,
                    Up = Vector3.Cross(tangents[i], right),
                    Width = Mathf.Max(0.2f, raw[i].width),
                });
            }
            return result;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float t0 = 0f;
            float t1 = t0 + Knot(p0, p1);
            float t2 = t1 + Knot(p1, p2);
            float t3 = t2 + Knot(p2, p3);
            float t = Mathf.Lerp(t1, t2, u);
            var a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
            var a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
            var a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
            var b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
            var b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
            return (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2;
        }

        private static float Knot(Vector3 a, Vector3 b) => Mathf.Max(1e-3f, Mathf.Pow((b - a).sqrMagnitude, 0.25f));

        /// <summary>The top surface across the track, left to right, as (across, up) offsets.</summary>
        public static List<Vector2> Profile(RampProfile profile, float width, float wallHeight)
        {
            float w = width * 0.5f, h = Mathf.Max(0.1f, wallHeight);
            var p = new List<Vector2>();
            const int arc = 10;
            switch (profile)
            {
                case RampProfile.Gutter:
                    for (int i = 0; i <= arc * 2; i++)
                    {
                        float a = Mathf.PI * (1f + i / (float)(arc * 2));
                        p.Add(new Vector2(w * Mathf.Cos(a), h * (1f + Mathf.Sin(a))));
                    }
                    break;
                case RampProfile.HalfPipe:
                {
                    float r = Mathf.Min(h, w * 0.95f);
                    if (h > r) p.Add(new Vector2(-w, h));
                    for (int i = 0; i <= arc; i++) p.Add(ArcPoint(new Vector2(-w + r, r), r, 180f + 90f * i / arc));
                    for (int i = 0; i <= arc; i++) p.Add(ArcPoint(new Vector2(w - r, r), r, 270f + 90f * i / arc));
                    if (h > r) p.Add(new Vector2(w, h));
                    break;
                }
                case RampProfile.QuarterPipe:
                {
                    float r = Mathf.Min(h, w * 1.9f);
                    p.Add(new Vector2(-w, 0f));
                    for (int i = 0; i <= arc; i++) p.Add(ArcPoint(new Vector2(w - r, r), r, 270f + 90f * i / arc));
                    if (h > r) p.Add(new Vector2(w, h));
                    break;
                }
                case RampProfile.Walls:
                    // Repeated corner points give the walls hard edges.
                    p.Add(new Vector2(-w, h)); p.Add(new Vector2(-w, 0f));
                    p.Add(new Vector2(-w, 0f)); p.Add(new Vector2(w, 0f));
                    p.Add(new Vector2(w, 0f)); p.Add(new Vector2(w, h));
                    break;
                default:
                    p.Add(new Vector2(-w, 0f)); p.Add(new Vector2(w, 0f));
                    break;
            }
            return p;
        }

        private static Vector2 ArcPoint(Vector2 c, float r, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        /// <summary>Upward normal of the profile segment i → i+1 (zero if the segment has no length).</summary>
        private static Vector2 SegmentNormal(List<Vector2> p, int i)
        {
            var d = p[i + 1] - p[i];
            return d.sqrMagnitude < 1e-10f ? Vector2.zero : new Vector2(-d.y, d.x).normalized;
        }

        /// <summary>Points under the surface, pushed back along the averaged surface normal.</summary>
        private static List<Vector2> Underside(List<Vector2> top, float thickness)
        {
            var bottom = new List<Vector2>(top.Count);
            for (int i = 0; i < top.Count; i++)
            {
                var n = Vector2.zero;
                for (int j = i - 1; j >= 0; j--) { var s = SegmentNormal(top, j); if (s != Vector2.zero) { n += s; break; } }
                for (int j = i; j < top.Count - 1; j++) { var s = SegmentNormal(top, j); if (s != Vector2.zero) { n += s; break; } }
                n = n.sqrMagnitude < 1e-8f ? Vector2.up : n.normalized;
                bottom.Add(top[i] - n * thickness);
            }
            return bottom;
        }

        public static void Build(LevelObjectData d, Mesh mesh)
        {
            mesh.Clear();
            var samples = SampleCurve(d);
            if (samples.Count < 2) return;

            int S = samples.Count;
            float thickness = Mathf.Max(0.05f, d.thickness);
            var tops = new List<Vector2>[S];
            var bots = new List<Vector2>[S];
            for (int s = 0; s < S; s++)
            {
                tops[s] = Profile(d.Profile, samples[s].Width, d.wallHeight);
                bots[s] = Underside(tops[s], thickness);
            }
            int n = tops[0].Count;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3 At(int s, Vector2 p) => samples[s].Pos + samples[s].Right * p.x + samples[s].Up * p.y;
            Vector3 Dir(int s, Vector2 p) => samples[s].Right * p.x + samples[s].Up * p.y;

            // Adds a grid of quads with its own vertices. pos(s, i) is a vertex, normal(s, i) the
            // outward direction of the quad between columns i and i+1; winding follows it.
            void Grid(int rows, int cols, Func<int, int, Vector3> pos, Func<int, int, Vector3> normal)
            {
                int start = verts.Count;
                for (int s = 0; s < rows; s++)
                    for (int i = 0; i < cols; i++)
                        verts.Add(pos(s, i));
                for (int s = 0; s < rows - 1; s++)
                    for (int i = 0; i < cols - 1; i++)
                    {
                        var want = normal(s, i);
                        if (want == Vector3.zero) continue;
                        int a = start + s * cols + i, b = a + 1, c = a + cols, e = c + 1;
                        Tri(a, c, e, want);
                        Tri(a, e, b, want);
                    }
            }

            void Tri(int a, int b, int c, Vector3 want)
            {
                var cross = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                if (cross.sqrMagnitude < 1e-12f) return;
                if (Vector3.Dot(cross, want) < 0f) { var t = b; b = c; c = t; }
                tris.Add(a); tris.Add(b); tris.Add(c);
            }

            // Top surface (smooth across, except where the profile repeats a point).
            Grid(S, n, (s, i) => At(s, tops[s][i]), (s, i) => Dir(s, SegmentNormal(tops[s], i)));
            // Underside.
            Grid(S, n, (s, i) => At(s, bots[s][i]), (s, i) => -Dir(s, SegmentNormal(tops[s], i)));
            // The two long edges joining top and underside.
            foreach (var (edge, inner) in new[] { (0, 1), (n - 1, n - 2) })
            {
                Grid(S, 2, (s, i) => At(s, i == 0 ? tops[s][edge] : bots[s][edge]), (s, i) =>
                {
                    var across = bots[s][edge] - tops[s][edge];
                    var away = tops[s][edge] - tops[s][inner];
                    away -= Vector2.Dot(away, across) / Mathf.Max(1e-6f, across.sqrMagnitude) * across;
                    return Dir(s, away);
                });
            }
            // End caps.
            foreach (var (s, sign) in new[] { (0, -1f), (S - 1, 1f) })
            {
                Grid(2, n, (row, i) => At(s, row == 0 ? tops[s][i] : bots[s][i]), (row, i) => samples[s].Tangent * sign);
            }

            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>World-space position, outward direction, width and bank at one end of a ramp.</summary>
        public static bool EndInfo(LevelObject ramp, bool atEnd, out Vector3 position, out Vector3 outward, out RampPoint point)
        {
            position = outward = Vector3.zero;
            point = null;
            var pts = ramp.Data.points;
            if (pts == null || pts.Count < 2) return false;
            var tr = ramp.transform;
            point = atEnd ? pts[pts.Count - 1] : pts[0];
            var neighbour = atEnd ? pts[pts.Count - 2] : pts[1];
            position = tr.TransformPoint(point.position);
            outward = (position - tr.TransformPoint(neighbour.position)).normalized;
            return true;
        }
    }
}
