using System.Collections.Generic;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// Launchers you grab, rebuilt from the game's own objects:
    /// <list type="bullet">
    /// <item>Launcher / Wall launcher = the game's Slingshot_45deg / Slingshot_Side_45deg: a sticky
    /// carriage that, while grabbed, shoots along its track (local forward) and throws you.</item>
    /// <item>Cog launcher = the game's GiantWheel: a sticky cog spinning around its local up axis.</item>
    /// </list>
    /// The carriage / cog is a kinematic Rigidbody, so the game's grab code attaches the player to it
    /// and carries them along, exactly as with the game's MovingObject and RotatingObject.
    /// </summary>
    public partial class LevelObject
    {
        // Timings from the game's slingshots (MovingObject move steps).
        private const float ShootTime = 0.3f, HoldTime = 1f, ReturnTime = 1f, CollidersOffTime = 0.5f;

        private readonly List<Collider> _extraSolids = new List<Collider>();
        private readonly List<(Renderer renderer, Material material, float shade)> _extraMaterials = new List<(Renderer, Material, float)>();
        private Transform _moving;
        private Rigidbody _movingBody;

        private enum LaunchState { Idle, Shooting, Holding, Returning }
        private LaunchState _launchState;
        private float _launchTime, _collidersOffUntil;
        private bool _collidersOff, _heldPlayer, _wasRunning;

        private static Mesh _arrowMesh;

        /// <summary>Builds the moving part and its decoration; returns the main grab surface.</summary>
        private GameObject BuildLauncher()
        {
            var moving = new GameObject("Moving");
            moving.transform.SetParent(transform, false);
            _moving = moving.transform;
            _movingBody = moving.AddComponent<Rigidbody>();
            _movingBody.isKinematic = true;
            _movingBody.useGravity = false;
            // No interpolation: the game reads the grabbed body's transform in FixedUpdate, which must match the physics pose.
            _movingBody.interpolation = RigidbodyInterpolation.None;
            _movingBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            GameObject shape;
            if (Data.Kind == ObjectKind.CogLauncher)
            {
                shape = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                shape.transform.SetParent(_moving, false);
                shape.transform.localScale = new Vector3(1f, 0.5f, 1f);
                DestroyImmediate(shape.GetComponent<Collider>());
                var mc = shape.AddComponent<MeshCollider>();
                mc.sharedMesh = shape.GetComponent<MeshFilter>().sharedMesh;
                mc.convex = true;
                // Teeth around the rim, also sticky.
                const int teeth = 16;
                for (int i = 0; i < teeth; i++)
                {
                    float a = i * Mathf.PI * 2f / teeth;
                    var tooth = AddPart(PrimitiveType.Cube, "Tooth", new Vector3(Mathf.Cos(a) * 0.52f, 0f, Mathf.Sin(a) * 0.52f),
                                        new Vector3(0.07f, 0.96f, 0.09f), 0.8f, solid: true);
                    tooth.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                }
                // Hub on both faces.
                AddPart(PrimitiveType.Cylinder, "Hub", Vector3.zero, new Vector3(0.22f, 0.56f, 0.22f), 0.45f, solid: false);
            }
            else
            {
                shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shape.transform.SetParent(_moving, false);
                // Side rails like the game's slingshot, and an arrow showing the launch direction.
                AddPart(PrimitiveType.Cube, "RailL", new Vector3(-0.5f, 0.6f, 0f), new Vector3(0.07f, 1.4f, 1f), 0.55f, solid: true);
                AddPart(PrimitiveType.Cube, "RailR", new Vector3(0.5f, 0.6f, 0f), new Vector3(0.07f, 1.4f, 1f), 0.55f, solid: true);
                var arrow = new GameObject("Arrow");
                arrow.transform.SetParent(_moving, false);
                arrow.transform.localPosition = new Vector3(0f, 0.51f, 0f);
                arrow.AddComponent<MeshFilter>().sharedMesh = ArrowMesh;
                var r = arrow.AddComponent<MeshRenderer>();
                var m = GameBridge.CreateMaterial(Color.white);
                r.sharedMaterial = m;
                _extraMaterials.Add((r, m, -1f));
            }
            return shape;
        }

        /// <summary>A decoration (or extra grab surface) on the moving part, tinted from the object's colour.</summary>
        private GameObject AddPart(PrimitiveType type, string name, Vector3 pos, Vector3 scale, float shade, bool solid)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(_moving, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (solid)
            {
                if (col is CapsuleCollider)
                {
                    DestroyImmediate(col);
                    col = go.AddComponent<BoxCollider>();
                }
                _extraSolids.Add(col);
            }
            else DestroyImmediate(col);
            var r = go.GetComponent<Renderer>();
            var m = GameBridge.CreateMaterial(Data.color * shade);
            r.sharedMaterial = m;
            _extraMaterials.Add((r, m, shade));
            return go;
        }

        private void RefreshLauncherParts()
        {
            foreach (var (_, material, shade) in _extraMaterials)
            {
                if (shade < 0f) continue; // the white arrow keeps its colour
                var c = Data.color * shade;
                c.a = 1f;
                GameBridge.SetColor(material, _isGhost ? Ghostly(c) : c);
            }
            foreach (var c in _extraSolids)
            {
                c.gameObject.tag = "Untagged";
                ApplySurface(c.gameObject);
            }
        }

        /// <summary>The direction a launcher shoots along: its local forward, along the carriage surface.</summary>
        public Vector3 LaunchDirection => transform.rotation * Vector3.forward;

        /// <summary>Where the carriage's centre ends up when it fires, for the editor's path preview.</summary>
        public Vector3 LaunchEnd => transform.position + LaunchDirection * Data.travel;

        private bool Running => !_isGhost && LevelEditorController.Instance != null && (!LevelEditorController.Instance.IsEditing || LevelEditorController.Instance.DemoAnimate);

        private void FixedUpdateLauncher()
        {
            if (_movingBody == null) return;
            bool running = Running;
            if (!running)
            {
                if (_wasRunning) ResetLauncher();
                _wasRunning = false;
                return;
            }
            _wasRunning = true;

            if (_collidersOff && Time.time >= _collidersOffUntil) SetCollidersEnabled(true);

            if (Data.Kind == ObjectKind.CogLauncher)
            {
                _movingBody.MoveRotation(_movingBody.rotation * Quaternion.AngleAxis(Data.spin * Time.fixedDeltaTime, Vector3.up));
                return;
            }

            var player = GameBridge.Player;
            bool grabbed = player != null && player.IsGrabLinkActive && player.GrabLinkParentRigidbody == _movingBody;
            _launchTime += Time.fixedDeltaTime;
            switch (_launchState)
            {
                case LaunchState.Idle:
                    if (grabbed)
                    {
                        // Like the game's first move step (forceHoldGrab): you can't let go mid-shot.
                        player.ForceHoldGrab = true;
                        _heldPlayer = true;
                        _launchState = LaunchState.Shooting;
                        _launchTime = 0f;
                    }
                    break;
                case LaunchState.Shooting:
                {
                    float f = Mathf.Clamp01(_launchTime / ShootTime);
                    _movingBody.MovePosition(transform.position + LaunchDirection * Data.travel * f);
                    if (f >= 1f)
                    {
                        Release(player, grabbed);
                        _launchState = LaunchState.Holding;
                        _launchTime = 0f;
                    }
                    break;
                }
                case LaunchState.Holding:
                    if (_launchTime >= HoldTime) { _launchState = LaunchState.Returning; _launchTime = 0f; }
                    break;
                case LaunchState.Returning:
                {
                    float f = Mathf.Clamp01(_launchTime / ReturnTime);
                    _movingBody.MovePosition(transform.position + LaunchDirection * Data.travel * (1f - f));
                    if (f >= 1f) _launchState = LaunchState.Idle;
                    break;
                }
            }
        }

        /// <summary>The game's MovingObject.MoveStepFinished: drop the grab and throw the player along the track.</summary>
        private void Release(Isto.GTW.Player.PlayerController player, bool grabbed)
        {
            if (player == null) return;
            if (_heldPlayer) player.ForceHoldGrab = false;
            _heldPlayer = false;
            if (!grabbed) return; // respawned or fell off meanwhile: don't fling them from elsewhere
            player.DeactivateGrabLink(0.5f);
            player.SetVelocity(LaunchDirection * Data.force, Vector3.zero);
            // Keep the carriage from catching the player again as they leave.
            SetCollidersEnabled(false);
            _collidersOffUntil = Time.time + CollidersOffTime;
        }

        private void SetCollidersEnabled(bool on)
        {
            _collidersOff = !on;
            if (_solid != null) _solid.enabled = on && !_isGhost;
            foreach (var c in _extraSolids) c.enabled = on && !_isGhost;
        }

        /// <summary>Back to the resting pose, for editing.</summary>
        private void ResetLauncher()
        {
            if (_heldPlayer && GameBridge.Player != null) GameBridge.Player.ForceHoldGrab = false;
            _heldPlayer = false;
            _launchState = LaunchState.Idle;
            _launchTime = 0f;
            _moving.localPosition = Vector3.zero;
            _moving.localRotation = Quaternion.identity;
            _movingBody.position = _moving.position;
            _movingBody.rotation = _moving.rotation;
            SetCollidersEnabled(true);
        }

        /// <summary>A flat arrow pointing along +Z, seen from both sides.</summary>
        private static Mesh ArrowMesh
        {
            get
            {
                if (_arrowMesh != null) return _arrowMesh;
                var v = new[]
                {
                    new Vector3(-0.08f, 0f, -0.35f), new Vector3(0.08f, 0f, -0.35f), new Vector3(0.08f, 0f, 0.05f), new Vector3(-0.08f, 0f, 0.05f),
                    new Vector3(-0.22f, 0f, 0.05f), new Vector3(0.22f, 0f, 0.05f), new Vector3(0f, 0f, 0.38f),
                };
                // Front faces up, back faces down; separate vertices so each side gets its own normal.
                var verts = new Vector3[v.Length * 2];
                v.CopyTo(verts, 0);
                v.CopyTo(verts, v.Length);
                var front = new[] { 0, 3, 2, 0, 2, 1, 4, 6, 5 };
                var t = new int[front.Length * 2];
                for (int i = 0; i < front.Length; i += 3)
                {
                    t[i] = front[i]; t[i + 1] = front[i + 1]; t[i + 2] = front[i + 2];
                    t[front.Length + i] = front[i] + v.Length; t[front.Length + i + 1] = front[i + 2] + v.Length; t[front.Length + i + 2] = front[i + 1] + v.Length;
                }
                _arrowMesh = new Mesh { name = "LE_Arrow", vertices = verts, triangles = t };
                _arrowMesh.RecalculateNormals();
                _arrowMesh.RecalculateBounds();
                return _arrowMesh;
            }
        }
    }
}
