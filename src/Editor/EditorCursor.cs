using System.Reflection;
using HarmonyLib;

namespace GTWLevelEditor
{
    public partial class LevelEditorController
    {
        /// <summary>True while the editor needs a free, visible mouse (mouse mode).</summary>
        internal bool WantsCursor => IsEditing && _cursorMode;
    }

    /// <summary>
    /// The game's camera controller hides and locks the cursor every LateUpdate during gameplay
    /// (PixelCrushers.CursorControl.SetCursorActive(false)), while mouse mode shows and frees it in Update.
    /// Locking and unlocking the cursor every frame made it flicker or vanish and lagged the game, so the
    /// game's request is turned into "show it" while the editor wants the mouse.
    /// </summary>
    [HarmonyPatch]
    internal static class EditorCursorPatch
    {
        private static MethodBase Target()
        {
            var type = AccessTools.TypeByName("PixelCrushers.CursorControl");
            if (type == null)
            {
                // It lives in the game's Plugins.dll, which may not be loaded yet when our patches go in.
                try { type = Assembly.Load("Plugins").GetType("PixelCrushers.CursorControl"); } catch { }
            }
            return type == null ? null : AccessTools.Method(type, "SetCursorActive", new[] { typeof(bool) });
        }

        private static bool Prepare()
        {
            if (Target() != null) return true;
            Plugin.Log.LogWarning("PixelCrushers.CursorControl.SetCursorActive not found; mouse mode may fight the game over the cursor.");
            return false;
        }

        private static MethodBase TargetMethod() => Target();

        private static void Prefix(ref bool value)
        {
            var editor = LevelEditorController.Instance;
            if (editor != null && editor.WantsCursor) value = true;
        }
    }
}
