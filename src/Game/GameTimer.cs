using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using Isto.Core.Speedrun;
using Isto.GTW.Speedrun;
using Isto.GTW.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GTWLevelEditor
{
    /// <summary>
    /// The game's own speedrun timer panel, reused for custom levels: the level's name, our run time
    /// and the level's personal best underneath. The panel keeps the look and sections the player
    /// chose in the game's settings; only its texts are ours.
    /// </summary>
    public partial class LevelEditorController
    {
        private static readonly AccessTools.FieldRef<SpeedrunTimer, TextMeshProUGUI> TimerMain =
            AccessTools.FieldRefAccess<SpeedrunTimer, TextMeshProUGUI>("_mainTimer");
        private static readonly AccessTools.FieldRef<SpeedrunTimer, TextMeshProUGUI> TimerChapterName =
            AccessTools.FieldRefAccess<SpeedrunTimer, TextMeshProUGUI>("_chapterName");
        private static readonly AccessTools.FieldRef<SpeedrunTimer, TextMeshProUGUI> TimerChapterBest =
            AccessTools.FieldRefAccess<SpeedrunTimer, TextMeshProUGUI>("_chapterBestTime");
        private static readonly AccessTools.FieldRef<SpeedrunTimer, SpeedrunSettings> TimerSettings =
            AccessTools.FieldRefAccess<SpeedrunTimer, SpeedrunSettings>("_speedrunSettings");
        private static readonly AccessTools.FieldRef<DoinklerSpeedrunTimer, TextMeshProUGUI> TimerHeader =
            AccessTools.FieldRefAccess<DoinklerSpeedrunTimer, TextMeshProUGUI>("_fileNameHeader");
        private static readonly AccessTools.FieldRef<DoinklerSpeedrunTimer, TextMeshProUGUI> TimerLevel =
            AccessTools.FieldRefAccess<DoinklerSpeedrunTimer, TextMeshProUGUI>("_currentLevelTimer");
        private static readonly AccessTools.FieldRef<DoinklerSpeedrunTimer, Slider> TimerProgress =
            AccessTools.FieldRefAccess<DoinklerSpeedrunTimer, Slider>("_progressSlider");

        /// <summary>The game's green for a finished run.</summary>
        private static readonly Color FinishedGreen = new Color(0.49f, 0.729f, 0.337f);

        private DoinklerSpeedrunTimer _gameTimer;
        private CanvasGroup _gameTimerGroup;
        private float _gameTimerSearchAt;
        /// <summary>The run reached the Finish; the time stays up in green until the next (re)start.</summary>
        private bool _runFinished;

        /// <summary>Inside a built custom level the panel shows our level, not the Doinkler one.</summary>
        private bool OwnsGameTimer => CustomSession.Active && _sessionReady;

        /// <summary>Whether the game's panel is on screen (it's off when the player turned the speedrun timer off).</summary>
        private bool GameTimerVisible => _gameTimer != null && _gameTimer.gameObject.activeInHierarchy && _gameTimerGroup != null && _gameTimerGroup.alpha > 0f;

        /// <summary>Called from LateUpdate, after the panel's own start-up code may have written its texts.</summary>
        private void UpdateGameTimer()
        {
            if (!OwnsGameTimer) { _gameTimer = null; return; }
            if (_gameTimer == null)
            {
                if (Time.unscaledTime < _gameTimerSearchAt) return;
                _gameTimerSearchAt = Time.unscaledTime + 1f;
                _gameTimer = FindFirstObjectByType<DoinklerSpeedrunTimer>(FindObjectsInactive.Include);
                if (_gameTimer == null) return;
                // Hidden while editing with a canvas group: switching the object off would stop its set-up.
                _gameTimerGroup = _gameTimer.GetComponent<CanvasGroup>();
                if (_gameTimerGroup == null) _gameTimerGroup = _gameTimer.gameObject.AddComponent<CanvasGroup>();
            }

            bool show = !IsEditing && !DemoRunning && _objects.Count > 0;
            _gameTimerGroup.alpha = show ? 1f : 0f;
            if (!show) return;

            var settings = TimerSettings(_gameTimer);
            Color normal = settings != null ? settings.GetNormalTimerTextColor() : Color.white;
            Color timeColor = _runFinished ? FinishedGreen : normal;

            SetTimerText(TimerHeader(_gameTimer), "Custom level", normal);
            SetTimerText(TimerChapterName(_gameTimer), string.IsNullOrEmpty(_level?.name) ? "Untitled" : _level.name, normal);

            // As the game: hours:minutes:seconds in the main timer, with hundredths in the level timer
            // (or in the main timer, for the timer styles that have no level timer).
            string full = GTWUtils.GetFormattedTimeOrDefaultString(_runTime.ToString(), timeColor, greyOutZeros: false);
            var main = TimerMain(_gameTimer);
            if (PlayerPrefs.GetInt(SpeedrunSettings.CONFIG_PLAYER_PREFS_KEY, 0) > 1)
                SetTimerText(main, full, timeColor);
            else
            {
                SetTimerText(main, GTWUtils.GetFormattedTimeOrDefaultStringSeconds(_runTime.ToString(), timeColor, greyOutZeros: false), timeColor);
                SetTimerText(TimerLevel(_gameTimer), full, timeColor);
            }

            // The personal best: in the split list's one row, or under the level name if the list is hidden.
            bool hasBest = _bestTime >= 0f;
            string bestText = hasBest ? GTWUtils.GetFormattedTimeOrDefaultString(_bestTime.ToString(), normal, greyOutZeros: false) : "--";
            bool inRow = ShowBestInSplitRow(bestText, normal);
            var best = TimerChapterBest(_gameTimer);
            if (best != null)
            {
                bool showBest = hasBest && !inRow;
                if (best.gameObject.activeSelf != showBest) best.gameObject.SetActive(showBest);
                if (showBest) SetTimerText(best, bestText, normal);
            }

            var progress = TimerProgress(_gameTimer);
            if (progress != null) progress.value = _runFinished ? 1f : 0f;
        }

        private static void SetTimerText(TMP_Text text, string value, Color color)
        {
            if (text == null) return;
            if (text.text != value) text.text = value;
            if (text.color != color) text.color = color;
        }

        /// <summary>The game's per-frame update would put the Doinkler world's times back.</summary>
        [HarmonyPatch(typeof(DoinklerSpeedrunTimer), "Update")]
        private static class KeepOurTimes
        {
            private static bool Prefix() => Instance == null || !Instance.OwnsGameTimer;
        }

        /// <summary>
        /// The game's Restart key resets the Doinkler stage: it respawns at the last checkpoint and
        /// restarts the stage's own timer (and counts an attempt and autosaves). In a custom level it
        /// starts a new run instead: back to the Start pad with our timer at zero.
        /// </summary>
        [HarmonyPatch(typeof(Isto.GTW.Managers.GTWDoinklerLevelTimeManager), "Events_OnDoinklerStageReset")]
        private static class RestartRun
        {
            private static bool Prefix()
            {
                var editor = Instance;
                if (editor == null || !editor.OwnsGameTimer || editor.IsEditing) return true;
                editor.RestartFromStageReset();
                return false;
            }
        }

        private void RestartFromStageReset()
        {
            ResetTimer();
            var start = StartObject;
            if (start != null) GameBridge.RespawnAt(start.AsCheckpoint());
            else GameBridge.Player?.CheckpointManager?.Respawn(showBlackOut: true);
        }

        // ---------------- the split rows: one "Personal Best" row instead of a row per Doinkler file ----------------

        private static readonly AccessTools.FieldRef<SpeedrunTimer, List<SpeedRunSingleTime>> TimerRows =
            AccessTools.FieldRefAccess<SpeedrunTimer, List<SpeedRunSingleTime>>("_segmentTimes");
        private static readonly AccessTools.FieldRef<SpeedrunTimer, RectTransform> TimerRowsSection =
            AccessTools.FieldRefAccess<SpeedrunTimer, RectTransform>("_fullDisplaySection");
        private static readonly AccessTools.FieldRef<SpeedRunSingleTime, TextMeshProUGUI> RowName =
            AccessTools.FieldRefAccess<SpeedRunSingleTime, TextMeshProUGUI>("_chapterName");

        /// <summary>When the panel's rows first existed: the game lays them out over its next frames, so they're left alone until then.</summary>
        private List<SpeedRunSingleTime> _timerRowsSeen;
        private float _timerRowsSeenAt;

        /// <summary>
        /// Shows the personal best in the first split row, renamed "Personal Best", and hides the
        /// others. Nothing is moved or resized: the row stays where the game laid it out. Returns false
        /// when the panel has no split list on screen (the player's timer style hides it), so the best
        /// goes under the level name instead.
        /// </summary>
        private bool ShowBestInSplitRow(string bestText, Color color)
        {
            var section = TimerRowsSection(_gameTimer);
            var rows = TimerRows(_gameTimer);
            if (section == null || !section.gameObject.activeInHierarchy || rows == null || rows.Count == 0 || rows[0] == null) return false;

            if (_timerRowsSeen != rows)
            {
                _timerRowsSeen = rows;
                _timerRowsSeenAt = Time.unscaledTime;
            }
            if (Time.unscaledTime - _timerRowsSeenAt < 1f) return true;

            for (int i = 1; i < rows.Count; i++)
                if (rows[i] != null && rows[i].gameObject.activeSelf) rows[i].gameObject.SetActive(false);

            var first = rows[0];
            if (!first.gameObject.activeSelf) first.gameObject.SetActive(true);
            SetTimerText(RowName(first), "Personal Best", color);
            first.SetTimer(bestText);
            first.SetColor(color);
            first.HideDiffTimer();
            first.HideTargetTimer();
            first.HideIcon();
            return true;
        }
    }

    /// <summary>Personal best times per level name, in BepInEx\LevelEditor\PersonalBests.json.</summary>
    internal static class PersonalBests
    {
        [Serializable]
        private class Entry
        {
            public string level;
            public float time;
        }

        [Serializable]
        private class FileData
        {
            public List<Entry> bests = new List<Entry>();
        }

        private static Dictionary<string, float> _times;

        private static string FilePath => Path.Combine(Paths.BepInExRootPath, "LevelEditor", "PersonalBests.json");

        /// <summary>The best time for <paramref name="level"/>, or -1 if it has none.</summary>
        public static float Get(string level)
        {
            Load();
            return level != null && _times.TryGetValue(level, out var t) ? t : -1f;
        }

        public static void Set(string level, float time)
        {
            if (string.IsNullOrEmpty(level)) return;
            Load();
            _times[level] = time;
            try
            {
                var data = new FileData();
                foreach (var kv in _times) data.bests.Add(new Entry { level = kv.Key, time = kv.Value });
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not save personal bests: " + e.Message);
            }
        }

        private static void Load()
        {
            if (_times != null) return;
            _times = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return;
                var data = JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath));
                if (data?.bests == null) return;
                foreach (var e in data.bests)
                    if (!string.IsNullOrEmpty(e.level) && e.time > 0f) _times[e.level] = e.time;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read personal bests: " + e.Message);
            }
        }
    }
}
