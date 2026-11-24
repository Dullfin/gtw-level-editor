using System.Collections.Generic;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>Hollow cylinders (open tubes) and the Jump pad surface for solid shapes.</summary>
    public partial class LevelObject
    {
        private const int TubeSegments = 40;

        private Mesh _solidCylinderMesh;
        private Mesh _hollowMesh;
        private int _hollowSig = int.MinValue;
        private float _jumpCooldown;
        private BoxCollider _jumpZone;

        // ---------------- hollow cylinders ----------------

        /// <summary>Switches a cylinder between solid and an open tube, rebuilding the tube when its size or wall changes.</summary>
        private void RefreshHollow()
        {
            if (_solid == null || !(_solid is MeshCollider mc)) return;
            var shape = _solid.transform;
            var mf = shape.GetComponent<MeshFilter>();
            if (!Data.hollow)
            {
                if (_hollowSig == int.MinValue) return;
                _hollowSig = int.MinValue;
                mf.sharedMesh = _solidCylinderMesh;
                shape.localScale = new Vector3(1f, 0.5f, 1f);
                mc.sharedMesh = null;
                mc.convex = true;
                mc.sharedMesh = _solidCylinderMesh;
                return;
            }

            int sig = unchecked(Data.scale.GetHashCode() * 31 + Data.wallThickness.GetHashCode());
            if (sig == _hollowSig) return;
            _hollowSig = sig;
            if (_hollowMesh == null) _hollowMesh = new Mesh { name = "LE_Tube" };
            BuildTube(_hollowMesh, Data.scale, Data.wallThickness);
            mf.sharedMesh = _hollowMesh;
            shape.localScale = Vector3.one;
            mc.sharedMesh = null;
            mc.convex = false; // a tube is concave: the inside must be solid to roll on
            mc.sharedMesh = _hollowMesh;
        }

        /// <summary>
        /// An open tube in the object's local space (radius 0.5, height 1, like the solid cylinder),
        /// with the wall thickness in world metres whatever the object's scale.
        /// </summary>
        private static void BuildTube(Mesh mesh, Vector3 scale, float wall)
        {
            float maxWall = Mathf.Min(scale.x, scale.z) * 0.5f - 0.02f;
            wall = Mathf.Clamp(wall, 0.02f, Mathf.Max(0.02f, maxWall));
            float ix = Mathf.Max(0.005f, 0.5f - wall / Mathf.Max(0.01f, scale.x));
            float iz = Mathf.Max(0.005f, 0.5f - wall / Mathf.Max(0.01f, scale.z));

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            const float top = 0.5f, bottom = -0.5f;

            // Each band (outer wall, inner wall, top ring, bottom ring) gets its own vertices for hard edges.
            void Band(System.Func<float, Vector3> a, System.Func<float, Vector3> b, System.Func<float, Vector3> normal, bool flip)
            {
                int start = verts.Count;
                for (int i = 0; i <= TubeSegments; i++)
                {
                    float t = i / (float)TubeSegments * Mathf.PI * 2f;
                    verts.Add(a(t)); normals.Add(normal(t));
                    verts.Add(b(t)); normals.Add(normal(t));
                }
                for (int i = 0; i < TubeSegments; i++)
                {
                    int v = start + i * 2;
                    if (!flip) { tris.Add(v); tris.Add(v + 1); tris.Add(v + 2); tris.Add(v + 2); tris.Add(v + 1); tris.Add(v + 3); }
                    else { tris.Add(v); tris.Add(v + 2); tris.Add(v + 1); tris.Add(v + 2); tris.Add(v + 3); tris.Add(v + 1); }
                }
            }

            Vector3 Outer(float t, float y) => new Vector3(Mathf.Cos(t) * 0.5f, y, Mathf.Sin(t) * 0.5f);
            Vector3 Inner(float t, float y) => new Vector3(Mathf.Cos(t) * ix, y, Mathf.Sin(t) * iz);
            Vector3 Out(float t) => new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));

            Band(t => Outer(t, bottom), t => Outer(t, top), Out, flip: false);
            Band(t => Inner(t, bottom), t => Inner(t, top), t => -Out(t), flip: true);
            Band(t => Inner(t, top), t => Outer(t, top), t => Vector3.up, flip: true);
            Band(t => Inner(t, bottom), t => Outer(t, bottom), t => Vector3.down, flip: false);

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        // ---------------- Jump pad surface ----------------

        /// <summary>
        /// A solid shape with the Jump pad surface throws you off it at its launch speed, along the
        /// surface you landed on, with the same maths as the jump pad.
        /// </summary>
        private void FixedUpdateJumpSurface()
        {
            if (_jumpCooldown > 0f) _jumpCooldown -= Time.fixedDeltaTime;
            if (!IsActiveJumpSurface || _jumpCooldown > 0f) return;
            var player = GameBridge.Player;
            var ground = player != null ? player.PlayerCollisionHandler : null;
            if (ground == null || !ground.IsGrounded || ground.Hit.collider != _solid) return;
            JumpOff(ground.Hit.normal);
        }

        /// <summary>
        /// The launch zone: a trigger box standing on the top of the shape, padHeight metres tall, so
        /// the pad throws you before you land on it (like the game's jump pads).
        /// </summary>
        private void RefreshJumpZone()
        {
            bool wants = !_isGhost && _solid != null && Data.HasJumpZone;
            if (!wants)
            {
                if (_jumpZone != null) _jumpZone.enabled = false;
                return;
            }
            if (_jumpZone == null)
            {
                var t = new GameObject("JumpZone");
                t.layer = GameBridge.TriggerLayer;
                t.transform.SetParent(transform, false);
                _jumpZone = t.AddComponent<BoxCollider>();
                _jumpZone.isTrigger = true;
                t.AddComponent<TriggerRelay>().Owner = this;
            }
            _jumpZone.enabled = true;
            float h = Data.padHeight / Mathf.Max(0.01f, Data.scale.y);
            _jumpZone.size = new Vector3(1f, h, 1f);
            _jumpZone.center = new Vector3(0f, 0.5f + h * 0.5f, 0f);
        }

        /// <summary>The launch zone's eight corners in world space, for drawing it while editing.</summary>
        public bool JumpZoneCorners(Vector3[] corners)
        {
            if (!Data.HasJumpZone) return false;
            float h = Data.padHeight / Mathf.Max(0.01f, Data.scale.y);
            int i = 0;
            for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                    for (int x = 0; x < 2; x++)
                        corners[i++] = transform.TransformPoint(new Vector3(x - 0.5f, 0.5f + y * h, z - 0.5f));
            return true;
        }

        private bool IsActiveJumpSurface => _solid != null && Data.IsSolidShape && Data.Surface == SurfaceType.JumpPad && Running;

        /// <summary>The Jump pad surface <paramref name="collider"/> belongs to, if it is one in play.</summary>
        internal static LevelObject JumpSurfaceOf(Collider collider)
        {
            if (collider == null) return null;
            var obj = collider.GetComponentInParent<LevelObject>();
            return obj != null && obj._solid == collider && obj.IsActiveJumpSurface ? obj : null;
        }

        /// <summary>Throws the player off along <paramref name="normal"/> (unless it just did).</summary>
        internal void JumpOff(Vector3 normal)
        {
            if (_jumpCooldown > 0f) return;
            var player = GameBridge.Player;
            if (player == null) return;
            _jumpCooldown = 0.3f;
            Launch(player.Rigidbody, normal.sqrMagnitude > 0.5f ? normal.normalized : transform.up);
        }
    }

    /// <summary>
    /// Landing on a Jump pad surface bounces you straight away, and never counts as a crash: the
    /// game's hard-landing check would otherwise ragdoll you when you fall back onto the pad.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Isto.GTW.Player.PlayerCollisionHandler), "OnCollisionEnter")]
    internal static class JumpSurfaceLanding
    {
        public static bool Bouncing;

        private static void Prefix(Collision collision)
        {
            var pad = LevelObject.JumpSurfaceOf(collision.collider);
            if (pad == null) return;
            Bouncing = true;
            var player = GameBridge.Player;
            var away = collision.contactCount > 0 && player != null ? player.PlayerPosition - collision.GetContact(0).point : Vector3.zero;
            pad.JumpOff(away);
        }

        private static void Postfix() => Bouncing = false;
    }

    [HarmonyLib.HarmonyPatch(typeof(Isto.GTW.Player.PlayerRagdoll), nameof(Isto.GTW.Player.PlayerRagdoll.CollisionHit))]
    internal static class JumpSurfaceNoCrash
    {
        private static bool Prefix() => !JumpSurfaceLanding.Bouncing;
    }
}

namespace GTWLevelEditor
{
    public partial class LevelObject
    {
        private Mesh _cubeMesh;
        private Mesh _cutMesh;

        /// <summary>Rebuilds a platform with the holes hollow cylinders cut through it (or back to a plain box).</summary>
        internal void SetCuts(System.Collections.Generic.List<CutHole> holes)
        {
            if (Data.Kind != ObjectKind.Platform || _solid == null) return;
            var shape = _solid.gameObject;
            var mf = shape.GetComponent<MeshFilter>();
            if (_cubeMesh == null) _cubeMesh = mf.sharedMesh;
            bool enabled = _solid.enabled;
            if (holes.Count == 0)
            {
                if (_solid is BoxCollider) return;
                mf.sharedMesh = _cubeMesh;
                DestroyImmediate(_solid);
                _solid = shape.AddComponent<BoxCollider>();
            }
            else
            {
                if (_cutMesh == null) _cutMesh = new Mesh { name = "LE_PlatformCut" };
                HoleyBox.Build(_cutMesh, holes);
                mf.sharedMesh = _cutMesh;
                if (!(_solid is MeshCollider))
                {
                    DestroyImmediate(_solid);
                    _solid = shape.AddComponent<MeshCollider>();
                }
                var mc = (MeshCollider)_solid;
                mc.sharedMesh = null;
                mc.sharedMesh = _cutMesh;
            }
            _solid.enabled = enabled;
            shape.tag = "Untagged";
            ApplySurface(shape);
        }
    }
}
