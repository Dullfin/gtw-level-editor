using HarmonyLib;
using Isto.GTW.Player;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// Debug aid for testing "hold forward" courses: while on, the game behaves exactly as if the
    /// forward key were held (full forward along the camera's view), with nobody at the keyboard.
    /// Toggled with the debug command "autoforward on|off".
    /// </summary>
    [HarmonyPatch(typeof(PlayerController), "GetCameraSpaceMovementInput")]
    internal static class AutoForward
    {
        public static bool Enabled;
        /// <summary>Steer along the direction of travel instead of the camera's, like a player with camera auto-rotate on.</summary>
        public static bool Follow;

        private static readonly AccessTools.FieldRef<PlayerController, Vector3> RawInput =
            AccessTools.FieldRefAccess<PlayerController, Vector3>("_rawInput");

        private static void Postfix(PlayerController __instance, ref Vector3 __result)
        {
            if (!Enabled) return;
            var raw = new Vector3(0f, 0f, 1f);
            RawInput(__instance) = raw;
            var forward = __instance.PlayerVirtualCameraController.AimTarget.forward;
            forward.y = 0f;
            if (Follow)
            {
                var v = __instance.Rigidbody.linearVelocity;
                v.y = 0f;
                if (v.magnitude > 2f) forward = v;
            }
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            __result = Quaternion.LookRotation(forward.normalized, Vector3.up) * raw;
        }
    }
}
