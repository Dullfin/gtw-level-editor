using HarmonyLib;
using Isto.GTW.Player;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// Marks an editor surface as slippery, with how much of the main game's friction, steering and
    /// air drag is kept on it (each 0-1, from the object's sliders).
    /// </summary>
    public class SlipperySurface : MonoBehaviour
    {
        public float Friction;
        public float Steering = 1f;
        public float Drag = 1f;

        private PhysicsMaterial _material;

        /// <summary>
        /// This surface's own physics material. The ball's material combines friction with "Maximum",
        /// so the contact uses the higher of the two: scaling both the ball's friction (see
        /// <see cref="Ice"/>) and this one (from Unity's default 0.6) scales the grip by exactly Friction.
        /// </summary>
        public PhysicsMaterial Material
        {
            get
            {
                if (_material == null)
                    _material = new PhysicsMaterial("LE_Slippery")
                    {
                        frictionCombine = PhysicsMaterialCombine.Minimum,
                        bounciness = 0f,
                        bounceCombine = PhysicsMaterialCombine.Average,
                    };
                _material.dynamicFriction = Ice.DefaultFriction * Friction;
                _material.staticFriction = Ice.DefaultFriction * Friction;
                return _material;
            }
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }

    /// <summary>
    /// Slippery surfaces. The game has none of its own: its "Slippery" tag only skips the short grip
    /// boost after letting go of a grab, and grabbing is left exactly as in the main game. While the
    /// ball is on a SlipperySurface, three things are scaled by that surface's settings:
    /// <list type="bullet">
    /// <item>Friction: the ball's friction, right after the game sets it each step.</item>
    /// <item>Steering: the game also steers by tilting gravity toward the stick, which ignores
    /// friction. That input is scaled while the game applies gravity.</item>
    /// <item>Air drag: the slowing drag the game sets each step.</item>
    /// </list>
    /// </summary>
    internal static class Ice
    {
        /// <summary>Unity's default friction, used by the game's normal surfaces.</summary>
        public const float DefaultFriction = 0.6f;

        private static readonly AccessTools.FieldRef<PlayerController, Vector3> MovementInput =
            AccessTools.FieldRefAccess<PlayerController, Vector3>("_movementInput");

        public static SlipperySurface SurfaceUnder(TorquePhysics physics)
        {
            if (!CustomSession.Active) return null;
            var ground = physics.PlayerController?.PlayerCollisionHandler;
            if (ground == null || !ground.IsGrounded) return null;
            var collider = ground.Hit.collider;
            return collider != null ? collider.GetComponent<SlipperySurface>() : null;
        }

        [HarmonyPatch(typeof(TorquePhysics), "UpdateFriction")]
        private static class LowerFriction
        {
            private static void Postfix(TorquePhysics __instance)
            {
                var ice = SurfaceUnder(__instance);
                if (ice == null) return;
                var material = __instance.PlayerController.MainBallCollider.material;
                material.dynamicFriction *= Mathf.Clamp01(ice.Friction);
                material.staticFriction = material.dynamicFriction;
            }
        }

        [HarmonyPatch(typeof(TorquePhysics), "UpdateTorque")]
        private static class LowerDrag
        {
            private static void Postfix(TorquePhysics __instance)
            {
                var ice = SurfaceUnder(__instance);
                if (ice == null) return;
                __instance.Rigidbody.linearDamping *= Mathf.Clamp01(ice.Drag);
            }
        }

        [HarmonyPatch(typeof(TorquePhysics), "UpdateGravity")]
        private static class LessSteering
        {
            private static void Prefix(TorquePhysics __instance, out Vector3? __state)
            {
                __state = null;
                var ice = SurfaceUnder(__instance);
                if (ice == null || ice.Steering >= 1f) return;
                ref var input = ref MovementInput(__instance.PlayerController);
                __state = input;
                input *= Mathf.Clamp01(ice.Steering);
            }

            private static void Postfix(TorquePhysics __instance, Vector3? __state)
            {
                if (__state.HasValue) MovementInput(__instance.PlayerController) = __state.Value;
            }
        }
    }
}
