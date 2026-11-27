using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Isto.Core.Data;
using Isto.Core.Platforms;
using Isto.GTW;
using Isto.GTW.Configuration;
using Isto.GTW.UI;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// A custom level runs inside a Doinkler Portfolio world that we start through the game's
    /// own level-select code, then hide completely. The world only provides the player,
    /// camera, lighting and sky. While a session is active, nothing is written to the saves.
    /// </summary>
    internal static class CustomSession
    {
        private static readonly FieldInfo SelectedLevelField = AccessTools.Field(typeof(UIDoinklerPortfolio), "_selectedLevel");
        private static readonly FieldInfo WorldIndexField = AccessTools.Field(typeof(UIDoinklerPortfolio), "_currentWorldIndex");
        private static readonly FieldInfo PortfolioDefinitionField = AccessTools.Field(typeof(UIDoinklerPortfolio), "_doinklerPortfolioDefinition");

        /// <summary>True from pressing a title-menu button until the player is back on the title screen.</summary>
        public static bool Active { get; private set; }
        /// <summary>The level to build once the world has loaded.</summary>
        public static LevelData Level { get; private set; }
        public static bool StartInEditor { get; private set; }
        public static int Environment { get; private set; }
        /// <summary>Debug only: load the world normally, sections included, to inspect its objects.</summary>
        public static bool Explore;
        /// <summary>Debug only: which level of the world to start.</summary>
        public static int LevelIndex;

        private static GTWGameState _gameState;

        public static GTWGameState FindGameState()
        {
            if (_gameState == null) _gameState = UnityEngine.Object.FindFirstObjectByType<GTWGameState>(FindObjectsInactive.Include);
            return _gameState;
        }

        public static bool IsOnTitleScreen()
        {
            var gs = FindGameState();
            return gs != null && gs.IsInTitleScene() && !Isto.Core.Game.GameState.CurrentlyLoading;
        }

        private static UIDoinklerPortfolio FindPortfolio() =>
            UnityEngine.Object.FindFirstObjectByType<UIDoinklerPortfolio>(FindObjectsInactive.Include);

        /// <summary>The Doinkler worlds' names, after their settings (skybox and scenery), in the game's order.</summary>
        private static readonly string[] WorldNames = { "Farm", "Hotel", "Stairwell", "City Sky", "Sky", "Space" };

        /// <summary>A world's name, or "World N" for any world past the named ones.</summary>
        public static string WorldName(int env) =>
            env >= 0 && env < WorldNames.Length ? WorldNames[env] : "World " + (env + 1);

        /// <summary>Names of the worlds the custom level can be built in (Doinkler worlds 1-6).</summary>
        public static List<string> EnvironmentNames()
        {
            var names = new List<string>();
            var worlds = Worlds(FindPortfolio());
            if (worlds == null) return names;
            for (int i = 0; i < worlds.Count; i++) names.Add(WorldName(i));
            return names;
        }

        private static List<GTWGameWorldDefinition> Worlds(UIDoinklerPortfolio portfolio)
        {
            if (portfolio == null) return null;
            var def = PortfolioDefinitionField?.GetValue(portfolio) as GTWDoinklerPortfolioDefinition;
            return def?.GTWGameWorldDefinitions;
        }

        /// <summary>Starts the world through the level-select menu's own Play button code.</summary>
        public static string Begin(LevelData level, bool startInEditor)
        {
            var portfolio = FindPortfolio();
            var worlds = Worlds(portfolio);
            if (worlds == null || worlds.Count == 0) return "Could not find the Doinkler level select on this screen.";

            int env = Mathf.Clamp(level.environment, 0, worlds.Count - 1);
            var world = worlds[env];
            if (world.GameLevels == null || world.GameLevels.Count == 0) return $"The {WorldName(env)} world has no levels.";

            Level = level;
            Level.environment = env;
            StartInEditor = startInEditor;
            Environment = env;
            Active = true;
            try
            {
                WorldIndexField.SetValue(portfolio, env);
                SelectedLevelField.SetValue(portfolio, world.GameLevels[Mathf.Clamp(LevelIndex, 0, world.GameLevels.Count - 1)]);
                portfolio.StartLevel_ButtonClicked();
            }
            catch (Exception e)
            {
                Active = false;
                Plugin.Log.LogError("Starting the custom level failed: " + e);
                return "Starting the level failed; see BepInEx\\LogOutput.log.";
            }
            Plugin.Log.LogInfo($"Starting custom level '{level.name}' in world {env + 1}, {WorldName(env)} ({(startInEditor ? "edit" : "play")}).");
            return null;
        }

        public static void End()
        {
            Active = false;
            Level = null;
            Explore = false;
            LevelIndex = 0;
        }
    }

    /// <summary>
    /// A custom level gets its own empty world: the Doinkler world's managers, sun and sky
    /// load as usual, but none of its level sections (the actual geometry) ever do.
    /// Reporting "not busy, fully loaded" lets the game's loading screen and the player's
    /// wait-for-sections state finish normally.
    /// </summary>
    [HarmonyPatch(typeof(Isto.GTW.SectionLoading.SectionLoader), "UpdateLoadRequirement")]
    internal static class SkipSectionLoading
    {
        private static readonly MethodInfo SetIsBusy = AccessTools.PropertySetter(typeof(Isto.GTW.SectionLoading.SectionLoader), "IsBusy");
        private static readonly MethodInfo SetProgress = AccessTools.PropertySetter(typeof(Isto.GTW.SectionLoading.SectionLoader), "SectionLoadingProgress");

        private static bool Prefix()
        {
            if (!CustomSession.Active || CustomSession.Explore) return true;
            SetIsBusy.Invoke(null, new object[] { false });
            SetProgress.Invoke(null, new object[] { 1f });
            return false;
        }
    }

    // While a custom level runs, the Doinkler world must not write progress, positions
    // or times into the player's real Doinkler save slots. Each save reports success and does nothing.

    [HarmonyPatch(typeof(PCGameData), nameof(PCGameData.SaveGameData))]
    internal static class BlockSaveGameData
    {
        private static bool Prefix(Action<bool> onSaveComplete)
        {
            if (!CustomSession.Active) return true;
            Plugin.Log.LogInfo("Skipped a game save during a custom level.");
            onSaveComplete?.Invoke(true);
            return false;
        }
    }

    [HarmonyPatch(typeof(PCGameData), nameof(PCGameData.SaveGameMetaData))]
    internal static class BlockSaveGameMetaData
    {
        private static bool Prefix(Action<bool> onSaveComplete)
        {
            if (!CustomSession.Active) return true;
            onSaveComplete?.Invoke(true);
            return false;
        }
    }

    [HarmonyPatch(typeof(PCGameData), nameof(PCGameData.SaveSpeedrunTimes))]
    internal static class BlockSaveSpeedrunTimes
    {
        private static bool Prefix(Action<bool> onSaveComplete)
        {
            if (!CustomSession.Active) return true;
            onSaveComplete?.Invoke(true);
            return false;
        }
    }
}
