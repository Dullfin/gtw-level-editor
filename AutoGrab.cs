using HarmonyLib;
using Isto.Core.Inputs;
using Isto.GTW.Inputs;
using Isto.GTW.Player;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// Debug aid for testing courses with launchers: while on, the grab button is "held" whenever
    /// the player touches a sticky surface, like a player holding grab into a launcher. On a cog it
    /// lets go once the player's velocity has turned down to <see cref="ReleaseAngle"/> degrees above
    /// <see cref="ReleaseDirection"/>, the way a player times the release. Toggled with the debug
    /// command "autograb on [angle] [dirX dirZ] | off".
    /// </summary>
    [HarmonyPatch(typeof(Controls), nameof(Controls.GetButton))]
    internal static class AutoGrab
    {
        public static bool Enabled;
        public static float ReleaseAngle = 35f;
        public static Vector3 ReleaseDirection = Vector3.forward;

        private static float _noGrabUntil;

        private static void Postfix(UserActions action, ref bool __result)
        {
            if (!Enabled || action == null || action.Name != GTWUserActions.GRAB.Name) return;
            var player = GameBridge.Player;
            if (player == null) return;
            __result = WantGrab(player);
        }

        private static bool WantGrab(PlayerController player)
        {
            if (player.IsGrabLinkActive)
            {
                var body = player.GrabLinkParentRigidbody;
                var obj = body != null ? body.GetComponentInParent<LevelObject>() : null;
                if (obj != null && obj.Data.Kind == ObjectKind.CogLauncher)
                {
                    var v = player.Rigidbody.linearVelocity;
                    var dir = ReleaseDirection;
                    dir.y = 0f;
                    dir.Normalize();
                    float forward = Vector3.Dot(v, dir);
                    if (forward > 1f && Mathf.Atan2(v.y, forward) * Mathf.Rad2Deg <= ReleaseAngle)
                    {
                        _noGrabUntil = Time.time + 0.6f;
                        return false;
                    }
                }
                return true;
            }
            if (Time.time < _noGrabUntil) return false;
            var handler = player.PlayerCollisionHandler;
            return handler != null && handler.IsGrounded && handler.Hit.collider != null && handler.Hit.collider.CompareTag("Sticky");
        }
    }
}
