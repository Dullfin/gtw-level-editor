using HarmonyLib;
using Isto.GTW.Player;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>Marks an editor surface as icy.</summary>
    public class SlipperySurface : MonoBehaviour
    {
    }

    /// <summary>
    /// Icy surfaces. The game has none of its own: its "Slippery" tag only skips the short grip
    /// boost after letting go of a grab. The ball moves in two ways, and ice weakens both while
    /// the ball is on a SlipperySurface:
    /// <list type="bullet">
    /// <item>Rolling grips through friction. The ball's physics material combines friction with
    /// "Maximum", so a low-friction surface material can't help; the ball's own friction is
    /// lowered right after the game sets it each step.</item>
    /// <item>Steering also tilts gravity toward the stick, which ignores friction. That input is
    /// scaled down while the game applies gravity, and air drag is reduced so you glide.</item>
    /// </list>
    /// </summary>
    internal static class Ice
    {
        public const float Friction = 0.02f;
        /// <summary>How much of the normal steer-by-tilting-gravity is left on ice (config: Surfaces/SlipperyControl).</summary>
        public static BepInEx.Configuration.ConfigEntry<float> ControlSetting;
        public static float Control => ControlSetting != null ? Mathf.Clamp01(ControlSetting.Value) : 0.3f;
        /// <summary>How much of the normal slowing drag is left on ice, so you glide.</summary>
        public const float Drag = 0.1f;

        private static PhysicsMaterial _surfaceMaterial;

        /// <summary>A frictionless material for icy colliders.</summary>
        public static PhysicsMaterial SurfaceMaterial => _surfaceMaterial != null ? _surfaceMaterial : (_surfaceMaterial = new PhysicsMaterial("LE_Ice")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicsMaterialCombine.Average,
        });

        private static readonly AccessTools.FieldRef<PlayerController, Vector3> MovementInput =
            AccessTools.FieldRefAccess<PlayerController, Vector3>("_movementInput");

        public static bool IsOnIce(TorquePhysics physics)
        {
            if (!CustomSession.Active) return false;
            var ground = physics.PlayerController?.PlayerCollisionHandler;
            if (ground == null || !ground.IsGrounded) return false;
            var collider = ground.Hit.collider;
            return collider != null && collider.GetComponent<SlipperySurface>() != null;
        }

        [HarmonyPatch(typeof(TorquePhysics), "UpdateFriction")]
        private static class LowerFriction
        {
            private static void Postfix(TorquePhysics __instance)
            {
                if (!IsOnIce(__instance)) return;
                var material = __instance.PlayerController.MainBallCollider.material;
                material.dynamicFriction = Friction;
                material.staticFriction = Friction;
            }
        }

        [HarmonyPatch(typeof(TorquePhysics), "UpdateTorque")]
        private static class LowerDrag
        {
            private static void Postfix(TorquePhysics __instance)
            {
                if (!IsOnIce(__instance)) return;
                __instance.Rigidbody.linearDamping *= Drag;
            }
        }

        [HarmonyPatch(typeof(TorquePhysics), "UpdateGravity")]
        private static class LessSteering
        {
            private static void Prefix(TorquePhysics __instance, out Vector3? __state)
            {
                __state = null;
                if (!IsOnIce(__instance)) return;
                ref var input = ref MovementInput(__instance.PlayerController);
                __state = input;
                input *= Control;
            }

            private static void Postfix(TorquePhysics __instance, Vector3? __state)
            {
                if (__state.HasValue) MovementInput(__instance.PlayerController) = __state.Value;
            }
        }
    }
}
