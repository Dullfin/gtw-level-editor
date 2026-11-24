using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// What you see while editing: hiding the player model, and flying the camera in mouse mode
    /// (hold the right mouse button and use WASD, like a 3D editor's viewport).
    /// </summary>
    public partial class LevelEditorController
    {
        public ConfigEntry<bool> HidePlayerInEditor;
        public ConfigEntry<bool> GridSnap;
        public static KeyCode HidePlayerKey = KeyCode.H;

        private readonly List<Renderer> _playerRenderers = new List<Renderer>();
        private Isto.GTW.Player.PlayerController _renderersOf;
        private bool _playerHidden;

        // The camera's pose when mouse mode opened, to put the player back under it on leaving.
        private Vector3 _mouseModeCamPos;
        private Quaternion _mouseModeCamRot;
        private Vector3 _mouseModePlayerPos;

        private bool WantPlayerHidden => IsEditing && HidePlayerInEditor != null && HidePlayerInEditor.Value;

        private void TogglePlayerHidden()
        {
            if (HidePlayerInEditor == null) return;
            HidePlayerInEditor.Value = !HidePlayerInEditor.Value;
            Toast(HidePlayerInEditor.Value ? "Player hidden" : "Player shown");
        }

        /// <summary>Hides or shows the player model, every frame, since the game may swap renderers in and out.</summary>
        private void UpdatePlayerVisibility()
        {
            var p = GameBridge.Player;
            bool hide = p != null && WantPlayerHidden;
            if (p != _renderersOf)
            {
                _playerRenderers.Clear();
                _renderersOf = p;
                _playerHidden = false;
            }
            if (p == null) return;
            if (hide && _playerRenderers.Count == 0)
            {
                var model = p.PlayerModelController;
                if (model != null) _playerRenderers.AddRange(model.GetComponentsInChildren<Renderer>(true));
            }
            if (hide == _playerHidden && !hide) return;
            foreach (var r in _playerRenderers)
                if (r != null) r.forceRenderingOff = hide;
            _playerHidden = hide;
        }

        private void RememberMouseModeCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            _mouseModeCamPos = cam.transform.position;
            _mouseModeCamRot = cam.transform.rotation;
            _mouseModePlayerPos = GameBridge.Player.PlayerTransform.position;
        }

        /// <summary>
        /// On leaving mouse mode, moves the player so the game's camera picks up where the mouse-mode
        /// camera was, looking the same way, instead of snapping back to where you were.
        /// </summary>
        private void HandCameraBackToPlayer()
        {
            var cam = Camera.main;
            var p = GameBridge.Player;
            if (cam == null || p == null) return;
            var moved = (cam.transform.position - _mouseModeCamPos).sqrMagnitude > 0.01f ||
                        Quaternion.Angle(cam.transform.rotation, _mouseModeCamRot) > 0.5f;
            if (!moved) return;
            var offset = Quaternion.Inverse(_mouseModeCamRot) * (_mouseModePlayerPos - _mouseModeCamPos);
            var target = cam.transform.position + cam.transform.rotation * offset;
            p.PlayerTransform.position = target;
            _frozenPlayerPos = target;
            var e = cam.transform.eulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            p.PlayerVirtualCameraController.SetCameraRotation(new Vector2(pitch, e.y));
        }

        /// <summary>Right mouse + WASD flies the mouse-mode camera; Q / E or Space go down / up; Shift is faster.</summary>
        private void FlyMouseModeCamera(Camera cam)
        {
            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) move += Vector3.back;
            if (Input.GetKey(KeyCode.D)) move += Vector3.right;
            if (Input.GetKey(KeyCode.A)) move += Vector3.left;
            float up = (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            if (move == Vector3.zero && up == 0f) return;
            float speed = 14f * (Shift ? 3f : 1f) * Time.unscaledDeltaTime;
            cam.transform.position += (cam.transform.rotation * move.normalized + Vector3.up * up) * speed;
        }
    }
}

namespace GTWLevelEditor
{
    /// <summary>
    /// While editing, the editor flies you up and down itself (Space / Left Alt), so the game's own
    /// no-clip up / down buttons are switched off: they share Ctrl with the editor's shortcuts.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Isto.Core.Inputs.Controls), nameof(Isto.Core.Inputs.Controls.GetButton))]
    internal static class EditorNoClipKeys
    {
        private static void Postfix(Isto.Core.Inputs.UserActions action, ref bool __result)
        {
            if (!__result || action == null) return;
            var editor = LevelEditorController.Instance;
            if (editor == null || !editor.IsEditing) return;
            if (action.Name == Isto.GTW.Inputs.GTWUserActions.NOCLIPMOVEDOWN.Name || action.Name == Isto.GTW.Inputs.GTWUserActions.NOCLIPMOVEUP.Name)
                __result = false;
        }
    }
}
