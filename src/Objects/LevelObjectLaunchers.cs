using System.Collections.Generic;
using Isto.GTW.Camera;
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
                // Like the game's GiantWheel, the grab surface is one smooth disc and the teeth are only for show:
                // solid teeth (or the primitive's 20 flat sides) moving at ~16 m/s on the rim knock the ball off
                // before the grab can hold. The disc reaches the tips of the teeth so the ball rides on them.
                var mc = shape.AddComponent<MeshCollider>();
                mc.sharedMesh = CogGripMesh;
                mc.convex = true;
                // Teeth around the rim.
                const int teeth = 16;
                for (int i = 0; i < teeth; i++)
                {
                    float a = i * Mathf.PI * 2f / teeth;
                    var tooth = AddPart(PrimitiveType.Cube, "Tooth", new Vector3(Mathf.Cos(a) * 0.52f, 0f, Mathf.Sin(a) * 0.52f),
                                        new Vector3(0.07f, 0.96f, 0.09f), 0.8f, solid: false);
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

        // ---------------- cog camera ----------------

        private PlayerCameraOverride.CameraOverride _cogCamera;
        private PlayerCameraOverride _cogCameraOwner;
        private Transform _cogCameraAnchor;
        /// <summary>The player outline this cog switched on, to switch off again when the ride ends.</summary>
        private Behaviour _cogOutline;

        /// <summary>
        /// While the player rides a cog, push a camera override like the game's PlayerCameraOverrideTrigger on
        /// its wheels: the camera holds a point set back and above the cog, looking down at its centre,
        /// instead of whirling round with the player.
        /// </summary>
        private void UpdateCogCamera()
        {
            if (Data.Kind != ObjectKind.CogLauncher || _movingBody == null) return;
            var player = GameBridge.Player;
            bool running = Running && player != null;
            bool riding = running && player.IsGrabLinkActive && player.GrabLinkParentRigidbody == _movingBody;
            if (riding && !_riding)
            {
                StopCogCamera(); // grabbed again while the camera was still easing back
                _riding = true;
                StartCogCamera(player);
                ShowPlayerOutline(player);
                return;
            }
            if (!riding && _riding)
            {
                // Let go: keep the override and slide its pivot back to the player, so the camera follows you
                // smoothly instead of jumping. The view turns back through the game's usual auto-rotate.
                _riding = false;
                HidePlayerOutline();
                if (running && _cogCamera != null && _cogCameraAnchor != null)
                {
                    _cogReturn = 0f;
                    _cogReturnFrom = _cogCameraAnchor.position;
                    _cogCamera.hasRotationOverride = false;
                    var vcam = player.PlayerVirtualCameraController;
                    _cogReturnDistance = vcam != null ? vcam.Distance : _cogCamera.distanceOverride;
                }
                else StopCogCamera();
            }
            if (_cogReturn < 0f) return;
            if (!running) { StopCogCamera(); return; }
            _cogReturn += Time.deltaTime / CogCameraReturnTime;
            if (_cogReturn >= 1f) { StopCogCamera(); return; }
            float t = Mathf.SmoothStep(0f, 1f, _cogReturn);
            var target = player.PlayerModelController != null ? player.PlayerModelController.transform.position : player.PlayerPosition;
            _cogCameraAnchor.position = Vector3.Lerp(_cogReturnFrom, target, t);
            _cogCamera.distanceOverride = Mathf.Lerp(_cogRideDistance, _cogReturnDistance, t);
        }

        /// <summary>Seconds the camera takes to glide back to the player after letting go of a cog.</summary>
        private const float CogCameraReturnTime = 0.75f;
        private float _cogReturn = -1f, _cogRideDistance, _cogReturnDistance;
        private Vector3 _cogReturnFrom;

        private void StartCogCamera(Isto.GTW.Player.PlayerController player)
        {
            var owner = player.PlayerVirtualCameraController != null ? player.PlayerVirtualCameraController.PlayerCameraOverride : null;
            if (owner == null) return;

            if (_cogCameraAnchor == null)
            {
                _cogCameraAnchor = new GameObject("CogCamera").transform;
                _cogCameraAnchor.SetParent(transform, false);
            }
            var cam = Camera.main;
            Vector3 offset = CogCameraWorldOffset(cam != null ? cam.transform.position : transform.position - player.DirectionFlat);
            // The game's camera sits "distance" behind its pivot and is pulled in towards the pivot when something
            // blocks the view. A pivot at the cog's centre is inside the cog, so the camera could end up in it:
            // put the pivot just in front of the camera point instead, in open air outside the cog.
            float dist = offset.magnitude;
            float pivotBack = Mathf.Min(1.5f, dist * 0.5f);
            _cogCameraAnchor.position = transform.position + offset * (1f - pivotBack / dist);
            _cogCameraAnchor.rotation = Quaternion.LookRotation(-offset, Vector3.up);

            _cogRideDistance = pivotBack;
            _cogCamera = new PlayerCameraOverride.CameraOverride
            {
                source = this,
                priority = 10,
                hasPositionOverride = true,
                hasRotationOverride = true,
                positionRotationOverride = _cogCameraAnchor,
                hasDistanceOverride = true,
                distanceOverride = pivotBack,
            };
            owner.AddCameraOverride(_cogCamera, checkForDouble: true);
            _cogCameraOwner = owner;
        }

        /// <summary>
        /// Where the riding camera sits relative to the cog's centre (world space): the level's chosen point, or
        /// the automatic view, set back and above on the face nearest <paramref name="cameraPos"/>.
        /// </summary>
        public Vector3 CogCameraWorldOffset(Vector3 cameraPos)
        {
            if (Data.cogCamera && Data.cogCameraOffset.sqrMagnitude > 0.01f) return transform.rotation * Data.cogCameraOffset;
            return AutoCogCameraOffset(cameraPos);
        }

        /// <summary>The automatic riding view, as an offset from the cog's centre (world space).</summary>
        public Vector3 AutoCogCameraOffset(Vector3 cameraPos)
        {
            Vector3 centre = transform.position, axis = transform.up;
            Vector3 toCam = cameraPos - centre;
            // The face of the cog nearest the camera.
            Vector3 side = Vector3.Dot(toCam, axis) >= 0f ? axis : -axis;
            Vector3 back = Vector3.ProjectOnPlane(side, Vector3.up);
            float rise = 0.5f;
            if (back.sqrMagnitude < 0.25f)
            {
                // A flat, turntable-like cog: keep looking the way the camera already faces, from higher up.
                back = Vector3.ProjectOnPlane(toCam, Vector3.up);
                if (back.sqrMagnitude < 0.0001f) back = -Vector3.forward;
                rise = 0.8f;
            }
            float radius = 0.55f * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            return (back.normalized + Vector3.up * rise).normalized * (radius * 2f + 4f);
        }

        private bool _riding;

        /// <summary>
        /// The game's cogs switch on the player model's Outline (a SetPlayerOutlineEvent) while you ride them, so you
        /// can see your character in blue where it sinks into or hides behind the cog.
        /// </summary>
        private void ShowPlayerOutline(Isto.GTW.Player.PlayerController player)
        {
            var outline = player.PlayerModelController != null ? player.PlayerModelController.OutlineShader : null;
            if (outline == null || outline.enabled) return; // already on: leave it to whoever turned it on
            outline.enabled = true;
            _cogOutline = outline;
        }

        private void HidePlayerOutline()
        {
            if (_cogOutline != null) _cogOutline.enabled = false;
            _cogOutline = null;
        }

        /// <summary>Ends the ride's camera override and outline at once (also on deletion and when editing starts).</summary>
        private void StopCogCamera()
        {
            _riding = false;
            _cogReturn = -1f;
            if (_cogCameraOwner != null) _cogCameraOwner.RemoveCameraOverride(this);
            _cogCameraOwner = null;
            _cogCamera = null;
            HidePlayerOutline();
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

        private static Mesh _cogGripMesh;

        /// <summary>
        /// The cog's grab collider, in the cylinder primitive's space (height 2, y ±1): a smooth 48-sided disc
        /// out to the teeth's tips (radius 0.555 against the visible rim's 0.5).
        /// </summary>
        private static Mesh CogGripMesh
        {
            get
            {
                if (_cogGripMesh != null) return _cogGripMesh;
                const int sides = 48;
                const float r = 0.555f;
                var verts = new Vector3[sides * 2 + 2];
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    verts[i] = p + Vector3.up;
                    verts[sides + i] = p + Vector3.down;
                }
                verts[sides * 2] = Vector3.up;
                verts[sides * 2 + 1] = Vector3.down;
                var tris = new List<int>(sides * 12);
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    tris.AddRange(new[] { sides * 2, j, i });                       // top
                    tris.AddRange(new[] { sides * 2 + 1, sides + i, sides + j });   // bottom
                    tris.AddRange(new[] { i, j, sides + j, i, sides + j, sides + i }); // side
                }
                _cogGripMesh = new Mesh { name = "LE_CogGrip", vertices = verts, triangles = tris.ToArray() };
                _cogGripMesh.RecalculateNormals();
                _cogGripMesh.RecalculateBounds();
                return _cogGripMesh;
            }
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
