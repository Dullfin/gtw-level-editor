using System;
using System.Linq;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>The panel's Motion section, motion previews while editing, and copy/paste of motion settings.</summary>
    public partial class LevelEditorController
    {
        private LevelObjectData _motionClipboard;
        private static readonly Color MotionColour = new Color(0.35f, 0.85f, 1f, 0.9f);

        private void DrawMotionFields(LevelObjectData d)
        {
            Theme.Section("Motion");
            GUILayout.BeginVertical(Theme.Card);
            GUILayout.BeginHorizontal();
            foreach (MotionType m in Enum.GetValues(typeof(MotionType)))
                if (GUILayout.Toggle(d.Motion == m, m.ToString(), Theme.Segment) && d.Motion != m) SetMotion(d, m);
            GUILayout.EndHorizontal();

            switch (d.Motion)
            {
                case MotionType.None:
                    GUILayout.Label("Make this piece a moving platform, turn it around a point, or swing it like a pendulum.", Theme.Hint);
                    break;
                case MotionType.Move:
                    d.moveOffset = VectorField("Offset", "mvo", d.moveOffset, 0.5f);
                    d.moveTime = Mathf.Max(0.1f, FloatField("Travel s", "mvt", d.moveTime, 0.25f));
                    d.movePause = Mathf.Max(0f, FloatField("Pause s", "mvp", d.movePause, 0.25f));
                    GUILayout.Label("Travels by the offset and back, pausing at each end. The blue line shows the path.", Theme.Hint);
                    break;
                case MotionType.Rotate:
                case MotionType.Swing:
                    if (d.Kind != ObjectKind.Pendulum)
                    {
                        d.pivot = VectorField("Pivot", "pv", d.pivot, 0.5f);
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(74);
                        if (GUILayout.Button("At its centre")) { d.pivot = d.position; ClearFields("pv"); }
                        if (GUILayout.Button("4 m above")) { d.pivot = d.position + Vector3.up * 4f; ClearFields("pv"); }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Axis", GUILayout.Width(70));
                    for (int i = 0; i < 3; i++)
                        if (GUILayout.Toggle(d.axis == i, "XYZ"[i].ToString(), Theme.Segment)) d.axis = i;
                    GUILayout.EndHorizontal();
                    if (d.Motion == MotionType.Rotate)
                    {
                        d.rotateSpeed = Mathf.Clamp(FloatField("Speed °/s", "rs", d.rotateSpeed, 5f), -720f, 720f);
                        GUILayout.Label("Pieces with the same pivot, axis and speed turn together as one. Use Copy / Paste motion to share them.", Theme.Hint);
                    }
                    else
                    {
                        d.swingAngle = Mathf.Clamp(FloatField("Swing °", "sa", d.swingAngle, 5f), 1f, 180f);
                        d.swingPeriod = Mathf.Clamp(FloatField("Period s", "sp", d.swingPeriod, 0.25f), 0.2f, 60f);
                        GUILayout.Label(d.Kind == ObjectKind.Pendulum
                            ? "Grab the bob to ride it; let go to fly off. It swings around its hinge (the object's position)."
                            : "Swings back and forth around the pivot. The blue arc shows the swing.", Theme.Hint);
                    }
                    break;
            }

            if (d.Motion != MotionType.None)
            {
                d.phase = Mathf.Repeat(FloatField("Start offset", "ph", d.phase, 0.05f), 1f);
                GUILayout.Label("0–1: shifts the timing so neighbouring pieces move out of step.", Theme.Hint);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy motion")) { _motionClipboard = d.Clone(); Toast("Motion copied"); }
            GUI.enabled = _motionClipboard != null;
            if (GUILayout.Button("Paste motion")) { PasteMotion(_motionClipboard, d); ClearFields(""); Toast("Motion pasted"); }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void ClearFields(string prefix)
        {
            foreach (var key in _fieldText.Keys.Where(k => k.StartsWith(prefix)).ToList()) _fieldText.Remove(key);
        }

        /// <summary>Picks sensible pivots the first time a piece is set to turn or swing.</summary>
        private static void SetMotion(LevelObjectData d, MotionType m)
        {
            var was = d.Motion;
            d.Motion = m;
            if (d.Kind == ObjectKind.Pendulum || was == m) return;
            if (m == MotionType.Rotate && (was != MotionType.Swing || d.pivot == Vector3.zero)) d.pivot = d.position;
            if (m == MotionType.Swing && (was != MotionType.Rotate || d.pivot == Vector3.zero)) d.pivot = d.position + Vector3.up * 4f;
        }

        private static void PasteMotion(LevelObjectData from, LevelObjectData to)
        {
            if (from == null) return;
            to.motion = from.motion;
            to.moveOffset = from.moveOffset;
            to.moveTime = from.moveTime;
            to.movePause = from.movePause;
            to.pivot = from.pivot;
            to.axis = from.axis;
            to.rotateSpeed = from.rotateSpeed;
            to.swingAngle = from.swingAngle;
            to.swingPeriod = from.swingPeriod;
            to.phase = from.phase;
        }

        /// <summary>
        /// Dragging a moving piece with the handles carries its pivot along, so it keeps turning the
        /// same way around itself. (Pieces of a shared group keep their pivot if edited by number.)
        /// </summary>
        private static void ShiftPivot(LevelObjectData d, Vector3 newPosition)
        {
            if (d.Motion == MotionType.Rotate || d.Motion == MotionType.Swing) d.pivot += newPosition - d.position;
        }

        /// <summary>While editing, moving pieces show their path: a line, a circle or a swing arc.</summary>
        private void DrawMotionPaths(Camera cam, float ui)
        {
            foreach (var o in _objects)
            {
                if (o == null || !o.Data.HasMotion) continue;
                var d = o.Data;
                var a = ToGui(cam, d.position, out var av);
                switch (d.Motion)
                {
                    case MotionType.Move:
                    {
                        var b = ToGui(cam, d.position + d.moveOffset, out var bv);
                        if (av && bv)
                        {
                            Line(a, b, MotionColour, 3f * ui);
                            Dot(b, 10f * ui, MotionColour);
                        }
                        break;
                    }
                    case MotionType.Rotate:
                    case MotionType.Swing:
                    {
                        var pivot = LevelObject.PivotOf(d);
                        var axis = d.AxisVector;
                        var arm = d.position - pivot;
                        var pg = ToGui(cam, pivot, out var pv);
                        if (pv) Dot(pg, 12f * ui, Color.white);
                        if (d.Motion == MotionType.Rotate)
                        {
                            var centre = pivot + axis * Vector3.Dot(arm, axis);
                            float radius = Vector3.ProjectOnPlane(arm, axis).magnitude;
                            if (radius < 0.3f) radius = Mathf.Max(1.5f, d.scale.magnitude * 0.4f);
                            var ring = RingPoints(cam, centre, axis, radius);
                            for (int k = 0; k < ring.Count - 1; k++)
                                if (ring[k].visible && ring[k + 1].visible) Line(ring[k].p, ring[k + 1].p, MotionColour, 2.5f * ui);
                        }
                        else
                        {
                            Vector2 prev = default;
                            bool prevVis = false;
                            for (int k = 0; k <= 24; k++)
                            {
                                float angle = Mathf.Lerp(-d.swingAngle, d.swingAngle, k / 24f);
                                var g = ToGui(cam, pivot + Quaternion.AngleAxis(angle, axis) * arm, out var gv);
                                if (k > 0 && gv && prevVis) Line(prev, g, MotionColour, 2.5f * ui);
                                prev = g;
                                prevVis = gv;
                            }
                            if (pv && av && d.Kind != ObjectKind.Pendulum) Line(pg, a, new Color(1f, 1f, 1f, 0.45f), 2f * ui);
                        }
                        break;
                    }
                }
            }
        }

        // ---------------- debug ----------------

        /// <summary>Puts the player on top of an object where it is right now (index in the level).</summary>
        public string DebugTeleportOnto(int index, float above)
        {
            if (index < 0 || index >= _objects.Count) return "bad index";
            var o = _objects[index];
            var top = o.GetBounds();
            var p = GameBridge.Player;
            p.TeleportCharacter(new Vector3(top.center.x, top.max.y + above, top.center.z));
            p.ResetVelocityToZero();
            return $"on {o.Kind} at {o.transform.position}";
        }

        public string DebugMotion(string type, Vector3 a, float b, float c)
        {
            if (_selected == null) return "nothing selected";
            if (!Enum.TryParse(type, true, out MotionType m)) return "unknown motion";
            var d = _selected.Data;
            SetMotion(d, m);
            switch (m)
            {
                case MotionType.Move: d.moveOffset = a; d.moveTime = b > 0 ? b : d.moveTime; d.movePause = c; break;
                case MotionType.Rotate: d.pivot = a; d.rotateSpeed = b; d.axis = (int)c; break;
                case MotionType.Swing: if (d.Kind != ObjectKind.Pendulum) d.pivot = a; d.swingAngle = b > 0 ? b : d.swingAngle; d.axis = (int)c; break;
            }
            _selected.Refresh();
            return $"motion {m} on {d.Kind}";
        }
    }
}
