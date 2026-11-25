using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Isto.GTW.LevelFeatures;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GTWLevelEditor
{
    [BepInPlugin(Guid, "GTW Level Editor", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.Dullfin.GTWLevelEditor";
        public const string Version = "0.6.1";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            // The editor lives on its own object: some games destroy BepInEx's manager object.
            var host = new GameObject("GTWLevelEditor");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            var editor = host.AddComponent<LevelEditorController>();
            editor.ToggleEditorKey = Config.Bind("Keys", "ToggleEditor", new KeyboardShortcut(KeyCode.F8),
                "Switch between editing and playing the level.");
            editor.CursorModeKey = Config.Bind("Keys", "TogglePanel", new KeyboardShortcut(KeyCode.Tab),
                "While editing: show the mouse cursor and the save/load/properties panel.");
            editor.FlyVerticalSpeed = Config.Bind("Editor", "FlyVerticalSpeed", 8f,
                "Speed in m/s when flying up (Space) or down (Left Alt) in the editor.");
            editor.PanelSize = Config.Bind("Editor", "PanelSize", new Vector2(440f, 640f),
                "Size of the mouse-mode panel. Drag its bottom-right corner to change this.");
            editor.HelpPosition = Config.Bind("Editor", "HelpWindowPosition", new Vector2(10f, 10f),
                "Screen position of the help window. Drag it while the panel is open to change this.");
            editor.HelpCompact = Config.Bind("Editor", "HelpWindowCompact", false,
                "Show only the status line in the help window.");
            editor.HidePlayerInEditor = Config.Bind("Editor", "HidePlayerInEditor", false,
                "Hide your character while editing so it doesn't block the view (toggle in game with H).");
            editor.ShowHelpWindow = Config.Bind("Editor", "ShowControlsWindow", true,
                "Show the floating Controls window while editing. Close it with its × button; reopen it from the panel's Controls tab.");
            editor.DockedLayout = Config.Bind("Editor", "DockedLayout", true,
                "While editing: object bar in a toolbar across the top, the panel docked on the right, and the game view in the space left over.");
            editor.GridSnap = Config.Bind("Editor", "GridSnap", false,
                "Start with grid snapping on (0.5 m steps, 15° turns). Toggle in game with G.");
            editor.UiScale = Config.Bind("Editor", "UiScale", 1f,
                "Size of the editor UI. 1 = sized for the screen height (the same proportion at 1080p and 4K); 1.25 = 25% bigger.");
            editor.LevelsDirectory = Path.Combine(Paths.BepInExRootPath, "LevelEditor", "Levels");
            Ice.ControlSetting = Config.Bind("Surfaces", "SlipperyControl", 0.3f,
                new ConfigDescription("How much steering you keep on Slippery surfaces: 0 = none (pure ice), 1 = as on normal ground.",
                    new AcceptableValueRange<float>(0f, 1f)));

            var debug = Config.Bind("Debug", "EnableDebugCommands", false,
                @"Development aid: run commands written to BepInEx\LevelEditor\Debug\commands.txt (screenshots, scene dumps, starting levels).");
            if (debug.Value)
            {
                var dbg = host.AddComponent<DebugCommands>();
                dbg.Folder = Path.Combine(Paths.BepInExRootPath, "LevelEditor", "Debug");
            }

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"GTW Level Editor {Version} loaded. Press {editor.ToggleEditorKey.Value} in a level to start editing.");
        }
    }

    /// <summary>The game's "restart from the beginning" goes to our Start pad when a custom level has one.</summary>
    [HarmonyPatch(typeof(CheckpointManager), nameof(CheckpointManager.RespawnAtStartingCheckpoint))]
    internal static class RespawnAtStartingCheckpointPatch
    {
        private static bool Prefix(ref bool __result)
        {
            var editor = LevelEditorController.Instance;
            if (editor != null && editor.TryRestartFromStart())
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
