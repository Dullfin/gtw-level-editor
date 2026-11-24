using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// Moving pieces: back-and-forth platforms, groups turning around a shared pivot (like
    /// Doinkler 6-1 "Capital Rotation") and swinging pendulums (like 4-5 "The Swoop").
    /// The whole object is a kinematic Rigidbody moved with MovePosition / MoveRotation, so the
    /// player is carried when standing on it and the game's grab follows it, as with the game's
    /// own RotatingObject and animated pendulums. Every piece runs on the same clock from the
    /// moment play starts, so pieces with the same settings stay in step.
    /// </summary>
    public partial class LevelObject
    {
        private Rigidbody _motionBody;
        private bool _motionRunning;
        private Transform _rod, _hub;
        private float _builtLength = -1f;
        private bool _carrying;
        private Vector3 _carryVelocity;

        private void FixedUpdate()
        {
            FixedUpdateLauncher();
            FixedUpdateMotion();
        }

        /// <summary>Builds the pendulum: hub at the hinge (the object's position), rod, and the sticky bob.</summary>
        private GameObject BuildPendulum()
        {
            _hub = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            _hub.name = "Hub";
            _hub.SetParent(transform, false);
            _hub.localScale = Vector3.one * 0.6f;
            DestroyImmediate(_hub.GetComponent<Collider>());

            _rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            _rod.name = "Rod";
            _rod.SetParent(transform, false);
            DestroyImmediate(_rod.GetComponent<Collider>());

            foreach (var part in new[] { _hub, _rod })
            {
                var r = part.GetComponent<Renderer>();
                var m = GameBridge.CreateMaterial(new Color(0.3f, 0.31f, 0.34f));
                r.sharedMaterial = m;
                _extraMaterials.Add((r, m, -1f));
            }

            var bob = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bob.transform.SetParent(transform, false);
            return bob;
        }

        /// <summary>Places the rod and bob for the current length.</summary>
        private void RefreshPendulum()
        {
            if (_rod == null || Mathf.Approximately(_builtLength, Data.length)) return;
            float len = Mathf.Max(0.5f, Data.length);
            _builtLength = Data.length;
            _rod.localPosition = new Vector3(0f, -len * 0.5f, 0f);
            _rod.localScale = new Vector3(0.18f, len * 0.5f, 0.18f);
            // The bob of the game's Pendulum_Small: about 2.6 x 1.4 x 2.3 m.
            var bob = _solid != null ? _solid.transform : null;
            if (bob != null)
            {
                bob.localPosition = new Vector3(0f, -len, 0f);
                bob.localScale = new Vector3(2.3f, 1.4f, 2.6f);
            }
        }

        /// <summary>Adds or removes the kinematic body that carries a moving piece.</summary>
        private void RefreshMotionBody()
        {
            bool wants = Data.HasMotion && !_isGhost;
            if (wants && _motionBody == null)
            {
                _motionBody = gameObject.AddComponent<Rigidbody>();
                _motionBody.isKinematic = true;
                _motionBody.useGravity = false;
                // No interpolation: the game reads a grabbed body's transform in FixedUpdate.
                _motionBody.interpolation = RigidbodyInterpolation.None;
                _motionBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
            else if (!wants && _motionBody != null)
            {
                Destroy(_motionBody);
                _motionBody = null;
            }
        }

        private void FixedUpdateMotion()
        {
            if (_motionBody == null) return;
            bool running = Running;
            if (!running)
            {
                if (_motionRunning)
                {
                    // Back to the placed pose for editing.
                    _motionRunning = false;
                    ApplyTransform();
                    _motionBody.position = transform.position;
                    _motionBody.rotation = transform.rotation;
                }
                return;
            }
            _motionRunning = true;
            float t = LevelEditorController.Instance != null ? LevelEditorController.Instance.MotionTime : Time.fixedTime;
            MotionPose(Data, t, out var pos, out var rot);
            CarryPlayer(_motionBody.position, _motionBody.rotation, pos, rot);
            _motionBody.MovePosition(pos);
            _motionBody.MoveRotation(rot);
        }

        /// <summary>
        /// Friction alone can't keep a rolling ball on a platform that speeds up, stops or turns.
        /// While the player stands on this piece (not grabbing; the game's grab already follows it),
        /// the change in the piece's velocity at the player's position is passed on to the ball each
        /// step, so they ride along and keep its momentum when they leave.
        /// </summary>
        private void CarryPlayer(Vector3 oldPos, Quaternion oldRot, Vector3 newPos, Quaternion newRot)
        {
            var player = GameBridge.Player;
            var ground = player?.PlayerCollisionHandler;
            bool onThis = ground != null && ground.IsGrounded && !player.IsGrabLinkActive &&
                          ground.Hit.collider != null && ground.Hit.collider.transform.IsChildOf(transform);
            if (!onThis)
            {
                _carrying = false;
                return;
            }
            var p = player.Rigidbody.position;
            var moved = newPos + newRot * (Quaternion.Inverse(oldRot) * (p - oldPos));
            var velocity = (moved - p) / Time.fixedDeltaTime;
            if (_carrying) player.Rigidbody.linearVelocity += velocity - _carryVelocity;
            _carryVelocity = velocity;
            _carrying = true;
        }

        /// <summary>Where a piece is at time t (seconds since play started).</summary>
        public static void MotionPose(LevelObjectData d, float t, out Vector3 pos, out Quaternion rot)
        {
            pos = d.position;
            rot = Quaternion.Euler(d.rotation);
            switch (d.Motion)
            {
                case MotionType.Move:
                {
                    float go = Mathf.Max(0.05f, d.moveTime), wait = Mathf.Max(0f, d.movePause);
                    float cycle = 2f * (go + wait);
                    float u = Mathf.Repeat(t + d.phase * cycle, cycle);
                    float f;
                    if (u < go) f = Ease(u / go);
                    else if (u < go + wait) f = 1f;
                    else if (u < 2f * go + wait) f = 1f - Ease((u - go - wait) / go);
                    else f = 0f;
                    pos += d.moveOffset * f;
                    break;
                }
                case MotionType.Rotate:
                case MotionType.Swing:
                {
                    float angle = d.Motion == MotionType.Rotate
                        ? d.rotateSpeed * t + d.phase * 360f
                        : d.swingAngle * Mathf.Sin(2f * Mathf.PI * (t / Mathf.Max(0.1f, d.swingPeriod) + d.phase));
                    var pivot = PivotOf(d);
                    var q = Quaternion.AngleAxis(angle, d.AxisVector);
                    pos = pivot + q * (d.position - pivot);
                    rot = q * rot;
                    break;
                }
            }
        }

        /// <summary>A pendulum always hinges at its own position; other pieces use their pivot field.</summary>
        public static Vector3 PivotOf(LevelObjectData d) => d.Kind == ObjectKind.Pendulum ? d.position : d.pivot;

        private static float Ease(float x) => 0.5f - 0.5f * Mathf.Cos(Mathf.Clamp01(x) * Mathf.PI);
    }
}
