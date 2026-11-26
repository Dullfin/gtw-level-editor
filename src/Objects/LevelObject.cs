using System.Collections.Generic;
using Isto.GTW.LevelFeatures;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// A placed object. Holds its saved data and builds its visuals, collider and
    /// trigger from it. Gameplay effects (launching, checkpoints, finish) are handled here too.
    /// </summary>
    public partial class LevelObject : MonoBehaviour
    {
        public LevelObjectData Data;

        private Renderer _renderer;
        private Material _material;
        private Collider _solid;
        private Collider _trigger;
        private bool _isGhost;
        private float _cooldown;
        private CheckpointData.LevelCheckpointData _checkpoint;

        private static Mesh _wedgeMesh;
        private Mesh _rampMesh;
        private int _rampBuiltSig, _rampColliderSig;

        public ObjectKind Kind => Data.Kind;

        public static LevelObject Create(LevelObjectData data, Transform parent)
        {
            var go = new GameObject("LE_" + data.kind);
            go.transform.SetParent(parent, false);
            var obj = go.AddComponent<LevelObject>();
            obj.Data = data;
            obj.Build();
            obj.Refresh();
            return obj;
        }

        private void Build()
        {
            // Visual + solid collider on a child so the trigger volume can be scaled independently.
            GameObject shape;
            switch (Data.Kind)
            {
                case ObjectKind.Sphere:
                case ObjectKind.Bumper:
                    shape = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    break;
                case ObjectKind.Cylinder:
                case ObjectKind.JumpPad:
                case ObjectKind.Checkpoint:
                case ObjectKind.Start:
                case ObjectKind.Finish:
                    shape = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    // Unity's cylinder is 2 units tall; halve it so scale.y means height.
                    shape.transform.localScale = new Vector3(1f, 0.5f, 1f);
                    // Its capsule collider is a poor fit for flat pads.
                    DestroyImmediate(shape.GetComponent<Collider>());
                    var mc = shape.AddComponent<MeshCollider>();
                    mc.sharedMesh = shape.GetComponent<MeshFilter>().sharedMesh;
                    mc.convex = true;
                    if (Data.Kind == ObjectKind.Cylinder) _solidCylinderMesh = mc.sharedMesh;
                    break;
                case ObjectKind.Ramp:
                    shape = new GameObject("Ramp");
                    _rampMesh = new Mesh { name = "LE_Ramp" };
                    shape.AddComponent<MeshFilter>().sharedMesh = _rampMesh;
                    shape.AddComponent<MeshRenderer>();
                    shape.AddComponent<MeshCollider>(); // mesh assigned once built and not a ghost
                    _rampBuiltSig = _rampColliderSig = int.MinValue;
                    break;
                case ObjectKind.Pendulum:
                    shape = BuildPendulum();
                    break;
                case ObjectKind.Launcher:
                case ObjectKind.WallLauncher:
                case ObjectKind.CogLauncher:
                    shape = BuildLauncher();
                    break;
                case ObjectKind.Wedge:
                    shape = new GameObject("Wedge");
                    shape.AddComponent<MeshFilter>().sharedMesh = WedgeMesh;
                    shape.AddComponent<MeshRenderer>();
                    shape.AddComponent<MeshCollider>().sharedMesh = WedgeMesh;
                    break;
                default: // Platform, KillZone, Sign
                    shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    break;
            }
            shape.name = "Shape";
            if (shape.transform.parent == null) shape.transform.SetParent(transform, false);
            _renderer = shape.GetComponent<Renderer>();
            _material = GameBridge.CreateMaterial(Data.color);
            _renderer.sharedMaterial = _material;
            _solid = shape.GetComponent<Collider>();

            if (Data.Kind == ObjectKind.KillZone)
            {
                // A kill zone is only a trigger.
                _trigger = _solid;
                _trigger.isTrigger = true;
                _solid = null;
                shape.layer = GameBridge.TriggerLayer;
                shape.AddComponent<TriggerRelay>().Owner = this;
            }
            else
            {
                ApplySurface(shape);
                // Markers never get in the way: the player passes straight through them.
                // The collider stays (as a trigger) so the editor can still click-select them.
                if (IsMarker) _solid.isTrigger = true;
                if (NeedsTrigger(Data.Kind)) BuildTrigger();
            }
            if (Data.Kind == ObjectKind.Sign) BuildSign();
        }

        /// <summary>Start, Checkpoint and Finish: shown only while editing, and never solid.</summary>
        public bool IsMarker => Data.Kind == ObjectKind.Start || Data.Kind == ObjectKind.Checkpoint || Data.Kind == ObjectKind.Finish;

        private static bool NeedsTrigger(ObjectKind kind) =>
            kind == ObjectKind.JumpPad || kind == ObjectKind.Bumper || kind == ObjectKind.Checkpoint || kind == ObjectKind.Finish;

        private void BuildTrigger()
        {
            var t = new GameObject("Trigger");
            t.layer = GameBridge.TriggerLayer;
            t.transform.SetParent(transform, false);
            if (Data.Kind == ObjectKind.Bumper)
            {
                var s = t.AddComponent<SphereCollider>();
                s.radius = 0.62f; // slightly outside the 0.5 radius sphere
                _trigger = s;
            }
            else
            {
                // A column above the pad. Height is in local units, so it is corrected for scale in ApplyTransform.
                _trigger = t.AddComponent<BoxCollider>();
            }
            _trigger.isTrigger = true;
            t.AddComponent<TriggerRelay>().Owner = this;
        }

        private void ApplySurface(GameObject shape)
        {
            if (IsMarker && shape == _solid?.gameObject)
            {
                shape.layer = GameBridge.MarkerLayer; // never ground: the player rolls straight through
                return;
            }
            shape.layer = GameBridge.GroundLayer;
            var ice = shape.GetComponent<SlipperySurface>();
            bool slippery = Data.IsSolidShape && !Data.IsLauncher && Data.Surface == SurfaceType.Slippery;
            if (slippery && ice == null) ice = shape.AddComponent<SlipperySurface>();
            else if (!slippery && ice != null) { Destroy(ice); ice = null; }
            if (ice != null)
            {
                ice.Friction = Mathf.Clamp01(Data.iceFriction);
                ice.Steering = Mathf.Clamp01(Data.iceSteering);
                ice.Drag = Mathf.Clamp01(Data.iceDrag);
            }
            // Friction combines with the ball's "Maximum" mode, so the surface's own grip is scaled too.
            var col = shape.GetComponent<Collider>();
            if (col != null) col.sharedMaterial = ice != null ? ice.Material : null;
            if (Data.IsLauncher || Data.Kind == ObjectKind.Pendulum)
            {
                // The game's launchers are grabbed through the Sticky tag: it glues you on and
                // throws you with the surface's speed when you let go.
                GameBridge.TrySetTag(shape, "Sticky");
                return;
            }
            if (!Data.IsSolidShape) return;
            switch (Data.Surface)
            {
                case SurfaceType.Slippery:
                    // Almost no grip (see SlipperyFriction); the game's tag also skips the grip boost after a grab.
                    GameBridge.TrySetTag(shape, "Slippery");
                    break;
                case SurfaceType.Sticky:
                    GameBridge.TrySetTag(shape, "Sticky");
                    break;
            }
        }

        /// <summary>Re-applies transform, colour and surface after the data was edited.</summary>
        public void Refresh()
        {
            ApplyTransform();
            if (_rampMesh != null) RefreshRamp();
            if (Data.Kind == ObjectKind.Cylinder) RefreshHollow();
            if (Data.Kind == ObjectKind.Wedge) RefreshWedgeCurve();
            if (_material != null) GameBridge.SetColor(_material, _isGhost ? Ghostly(Data.color) : Data.color);
            if (_solid != null)
            {
                _solid.gameObject.tag = "Untagged";
                ApplySurface(_solid.gameObject);
            }
            RefreshLauncherParts();
            RefreshJumpZone();
            RefreshPendulum();
            RefreshMotionBody();
            RefreshSign();
        }

        /// <summary>Rebuilds the ramp mesh when its points or shape changed; the collider only while not a ghost.</summary>
        private void RefreshRamp()
        {
            int sig = RampSignature(Data);
            if (sig != _rampBuiltSig)
            {
                RampMesh.Build(Data, _rampMesh);
                _rampBuiltSig = sig;
            }
            if (_isGhost || sig == _rampColliderSig || !(_solid is MeshCollider mc)) return;
            mc.sharedMesh = null;
            if (_rampMesh.vertexCount > 0) mc.sharedMesh = _rampMesh;
            _rampColliderSig = sig;
        }

        private static int RampSignature(LevelObjectData d)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (d.profile ?? "").GetHashCode();
                h = h * 31 + d.thickness.GetHashCode();
                h = h * 31 + d.wallHeight.GetHashCode();
                h = h * 31 + d.followTwist.GetHashCode();
                if (d.points != null)
                    foreach (var p in d.points)
                    {
                        h = h * 31 + p.position.GetHashCode();
                        h = h * 31 + p.width.GetHashCode();
                        h = h * 31 + p.bank.GetHashCode();
                    }
                return h;
            }
        }

        public bool IsRamp => Data.Kind == ObjectKind.Ramp;

        private void ApplyTransform()
        {
            transform.position = Data.position;
            transform.rotation = Quaternion.Euler(Data.rotation);
            transform.localScale = Data.scale;
            if (_trigger is BoxCollider box && _trigger != _solid)
            {
                // 3 m tall column standing on the pad, whatever the pad's own height.
                float height = 3f / Mathf.Max(0.01f, Data.scale.y);
                box.size = new Vector3(1f, height, 1f);
                box.center = new Vector3(0f, height * 0.5f, 0f);
            }
        }

        /// <summary>While a ghost, the object follows the cursor and must not collide or trigger.</summary>
        public void SetGhost(bool ghost)
        {
            _isGhost = ghost;
            if (_solid != null) _solid.enabled = !ghost;
            if (_trigger != null) _trigger.enabled = !ghost;
            if (_jumpZone != null) _jumpZone.enabled = !ghost;
            foreach (var c in _extraSolids) c.enabled = !ghost;
            Refresh();
        }

        private static Color Ghostly(Color c) => Color.Lerp(c, Color.white, 0.45f);

        /// <summary>Kill zones and markers (Start, Checkpoint, Finish) are visible only while editing.</summary>
        public void SetEditorVisible(bool editing)
        {
            if ((Data.Kind == ObjectKind.KillZone || IsMarker) && _renderer != null) _renderer.enabled = editing;
        }

        public Bounds GetBounds() => _renderer != null ? _renderer.bounds : new Bounds(transform.position, Vector3.one);

        public CheckpointData.LevelCheckpointData AsCheckpoint()
        {
            if (_checkpoint == null) _checkpoint = GameBridge.MakeCheckpoint("LE_" + Data.kind + "_" + GetInstanceID(), Vector3.zero, Vector3.forward);
            // Spawn 1 m above the pad; spawnOnGround drops the player onto it.
            _checkpoint.position = transform.position + transform.up * (Data.scale.y * 0.5f + 1f);
            var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            _checkpoint.forward = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            return _checkpoint;
        }

        private void Update()
        {
            if (_cooldown > 0f) _cooldown -= Time.deltaTime;
            UpdateCogCamera();
        }

        internal void OnPlayerEnter()
        {
            if (_isGhost || LevelEditorController.Instance == null || LevelEditorController.Instance.IsEditing) return;
            var player = GameBridge.Player;
            if (player == null) return;

            switch (Data.Kind)
            {
                case ObjectKind.JumpPad:
                    if (_cooldown > 0f) return;
                    _cooldown = 0.3f;
                    Launch(player.Rigidbody, transform.up);
                    break;
                case ObjectKind.Bumper:
                {
                    if (_cooldown > 0f) return;
                    _cooldown = 0.2f;
                    // Same as the game's Bumper: push away from the centre within the bumper's horizontal plane.
                    var plane = new Plane(transform.up, transform.position);
                    var dir = plane.ClosestPointOnPlane(player.PlayerPosition) - transform.position;
                    if (dir.sqrMagnitude < 0.0001f) dir = -player.Rigidbody.linearVelocity;
                    Launch(player.Rigidbody, dir.normalized);
                    break;
                }
                case ObjectKind.Checkpoint:
                    LevelEditorController.Instance.ReachCheckpoint(this);
                    break;
                case ObjectKind.Platform:
                case ObjectKind.Cylinder:
                case ObjectKind.Sphere:
                    // The launch zone above a Jump pad surface.
                    if (Data.HasJumpZone) JumpOff(transform.up);
                    break;
                case ObjectKind.Finish:
                    LevelEditorController.Instance.ReachFinish();
                    break;
                case ObjectKind.KillZone:
                    LevelEditorController.Instance.KillPlayer();
                    break;
            }
        }

        /// <summary>Matches the game's JumpPad: cancel velocity going against the launch, then add the launch speed.</summary>
        private void Launch(Rigidbody body, Vector3 direction)
        {
            var force = direction * Data.force;
            if (Vector3.Angle(direction, body.linearVelocity) > 90f)
            {
                force -= Vector3.Project(body.linearVelocity, direction);
            }
            body.AddForce(force, ForceMode.VelocityChange);
        }

        private void OnDestroy()
        {
            StopCogCamera();
            if (_material != null) Destroy(_material);
            foreach (var m in _extraMaterials) if (m.material != null) Destroy(m.material);
            if (_rampMesh != null) Destroy(_rampMesh);
            if (_hollowMesh != null) Destroy(_hollowMesh);
            if (_cutMesh != null) Destroy(_cutMesh);
            if (_curvedWedgeMesh != null) Destroy(_curvedWedgeMesh);
        }

        // ---------------- curved wedge slopes ----------------

        private const int WedgeCurveSteps = 16;
        private Mesh _curvedWedgeMesh;
        private float _wedgeCurveBuilt;

        /// <summary>
        /// A point on the wedge's slope profile in local space: <paramref name="s"/> runs 0 (front, at
        /// the bottom) to 1 (back, at the top) along a curve bent by <paramref name="curve"/> (-1 to 1),
        /// at side <paramref name="x"/> (-0.5 to 0.5).
        /// </summary>
        public static Vector3 WedgeSlopePoint(float curve, float s, float x)
        {
            // A quadratic curve from (0,0) to (1,1) whose middle control point slides from (1,0)
            // (curved in) through (0.5,0.5) (straight) to (0,1) (curved out).
            float c = Mathf.Clamp(curve, -1f, 1f);
            var p = new Vector2(0.5f - 0.5f * c, 0.5f + 0.5f * c);
            // (1-s)²·(0,0) + 2(1-s)s·p + s²·(1,1); the first term is zero.
            var q = 2f * (1f - s) * s * p + s * s * Vector2.one;
            return new Vector3(x, q.y - 0.5f, q.x - 0.5f);
        }

        /// <summary>A straight wedge uses the shared mesh; a curved one gets its own, rebuilt when the curve changes.</summary>
        private void RefreshWedgeCurve()
        {
            float c = Mathf.Clamp(Data.slopeCurve, -1f, 1f);
            var filter = _renderer != null ? _renderer.GetComponent<MeshFilter>() : null;
            if (filter == null) return;
            Mesh want;
            if (Mathf.Abs(c) < 0.005f) want = WedgeMesh;
            else
            {
                if (_curvedWedgeMesh == null) { _curvedWedgeMesh = new Mesh { name = "LE_CurvedWedge" }; _wedgeCurveBuilt = float.NaN; }
                if (!(c == _wedgeCurveBuilt))
                {
                    BuildCurvedWedge(_curvedWedgeMesh, c);
                    _wedgeCurveBuilt = c;
                    if (_solid is MeshCollider rebuilt && rebuilt.sharedMesh == _curvedWedgeMesh) { rebuilt.sharedMesh = null; rebuilt.sharedMesh = _curvedWedgeMesh; }
                }
                want = _curvedWedgeMesh;
            }
            if (filter.sharedMesh != want) filter.sharedMesh = want;
            if (_solid is MeshCollider mc && mc.sharedMesh != want) mc.sharedMesh = want;
        }

        private static void BuildCurvedWedge(Mesh mesh, float curve)
        {
            int n = WedgeCurveSteps;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3 a = new Vector3(-0.5f, -0.5f, -0.5f), b = new Vector3(0.5f, -0.5f, -0.5f);
            Vector3 c = new Vector3(0.5f, -0.5f, 0.5f), d = new Vector3(-0.5f, -0.5f, 0.5f);
            Vector3 e = new Vector3(0.5f, 0.5f, 0.5f), f = new Vector3(-0.5f, 0.5f, 0.5f);

            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int[] order)
            {
                int i0 = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
                foreach (var k in order) tris.Add(i0 + k);
            }
            int[] quadOrder = { 0, 2, 1, 0, 3, 2 };
            Quad(a, d, c, b, quadOrder); // bottom
            Quad(d, f, e, c, quadOrder); // back

            // The slope: one strip whose vertices are shared along the curve, so it shades smoothly.
            int slope = verts.Count;
            for (int k = 0; k <= n; k++)
            {
                float s = k / (float)n;
                verts.Add(WedgeSlopePoint(curve, s, -0.5f));
                verts.Add(WedgeSlopePoint(curve, s, 0.5f));
            }
            for (int k = 0; k < n; k++)
            {
                int l0 = slope + 2 * k, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                tris.Add(l0); tris.Add(r1); tris.Add(r0);
                tris.Add(l0); tris.Add(l1); tris.Add(r1);
            }

            // The two sides: a column under each step of the curve.
            for (int k = 0; k < n; k++)
            {
                float s0 = k / (float)n, s1 = (k + 1) / (float)n;
                for (int side = 0; side < 2; side++)
                {
                    float x = side == 0 ? -0.5f : 0.5f;
                    var t0 = WedgeSlopePoint(curve, s0, x);
                    var t1 = WedgeSlopePoint(curve, s1, x);
                    var b0 = new Vector3(x, -0.5f, t0.z);
                    var b1 = new Vector3(x, -0.5f, t1.z);
                    Quad(b0, b1, t1, t0, side == 0 ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 });
                }
            }

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>A unit wedge: flat bottom, vertical back face at +Z, slope rising from -Z to +Z.</summary>
        private static Mesh WedgeMesh
        {
            get
            {
                if (_wedgeMesh != null) return _wedgeMesh;
                Vector3 a = new Vector3(-0.5f, -0.5f, -0.5f), b = new Vector3(0.5f, -0.5f, -0.5f);
                Vector3 c = new Vector3(0.5f, -0.5f, 0.5f), d = new Vector3(-0.5f, -0.5f, 0.5f);
                Vector3 e = new Vector3(0.5f, 0.5f, 0.5f), f = new Vector3(-0.5f, 0.5f, 0.5f);
                // Each face gets its own vertices so normals stay flat.
                var verts = new[]
                {
                    a, d, c, b,       // bottom
                    d, f, e, c,       // back
                    a, b, e, f,       // slope
                    a, d, f,          // left
                    b, e, c,          // right
                };
                var tris = new[]
                {
                    0, 2, 1, 0, 3, 2,
                    4, 6, 5, 4, 7, 6,
                    8, 10, 9, 8, 11, 10,
                    12, 13, 14,
                    15, 16, 17,
                };
                _wedgeMesh = new Mesh { name = "LE_Wedge", vertices = verts, triangles = tris };
                _wedgeMesh.RecalculateNormals();
                _wedgeMesh.RecalculateBounds();
                return _wedgeMesh;
            }
        }
    }

    /// <summary>Forwards trigger hits from a child collider to its <see cref="LevelObject"/>.</summary>
    public class TriggerRelay : MonoBehaviour
    {
        public LevelObject Owner;

        private void OnTriggerEnter(Collider other)
        {
            if (Owner != null && GameBridge.IsPlayerTrigger(other)) Owner.OnPlayerEnter();
        }
    }
}
