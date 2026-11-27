using System.Diagnostics;
using BepInEx.Configuration;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// An optional readout (Controls tab, or ShowEditorTiming in the config) of the frame rate and how much of
    /// each frame the editor itself takes: its Update and all of its GUI passes (panel, overlays, windows).
    /// Averaged over about a second so it can be read, and shown bottom-left.
    /// </summary>
    public partial class LevelEditorController
    {
        public ConfigEntry<bool> ShowTiming;

        private readonly Stopwatch _timingClock = new Stopwatch();
        private readonly Stopwatch _ghostClock = new Stopwatch();
        private double _ghostMs;                   // this frame: placing the ghost under the crosshair
        private float _avgGhost;
        private float _worstFrame, _worstShown;    // the slowest single frame of editor work in the last second
        private float _worstResetTime;
        private string _lastPlace = "";
        private int _timingFrame = -1;
        private double _updateMs, _guiMs;          // this frame so far
        private float _avgUpdate, _avgGui, _avgFrame = 16f;
        private float _timingTextTime;
        private string _timingText = "";

        /// <summary>Adds the time since <see cref="_timingClock"/> was restarted to this frame's total in <paramref name="bucket"/>.</summary>
        private void AddTiming(ref double bucket)
        {
            RollTimingFrame();
            bucket += _timingClock.Elapsed.TotalMilliseconds;
        }

        /// <summary>On the first measurement of a new frame, folds the last frame's totals into the averages.</summary>
        private void RollTimingFrame()
        {
            int frame = Time.frameCount;
            if (frame == _timingFrame) return;
            if (_timingFrame >= 0)
            {
                const float k = 0.03f;
                _avgUpdate = Mathf.Lerp(_avgUpdate, (float)_updateMs, k);
                _avgGui = Mathf.Lerp(_avgGui, (float)_guiMs, k);
                _avgFrame = Mathf.Lerp(_avgFrame, Time.unscaledDeltaTime * 1000f, k);
                _avgGhost = Mathf.Lerp(_avgGhost, (float)_ghostMs, k);
                _worstFrame = Mathf.Max(_worstFrame, (float)(_updateMs + _guiMs));
                if (Time.unscaledTime >= _worstResetTime)
                {
                    _worstShown = _worstFrame;
                    _worstFrame = 0f;
                    _worstResetTime = Time.unscaledTime + 1f;
                }
            }
            _updateMs = _guiMs = _ghostMs = 0;
            _timingFrame = frame;
        }

        /// <summary>Called after placing a piece: shown on the readout and written to the log while timing is on.</summary>
        private void NotePlaceTiming(double total, double undo, double solid, double nextGhost)
        {
            if (ShowTiming == null || !ShowTiming.Value) return;
            _lastPlace = $"  ·  last place {total:0.0} ms";
            Plugin.Log.LogInfo($"Placing took {total:0.00} ms: undo snapshot {undo:0.00}, making it solid {solid:0.00}, next ghost {nextGhost:0.00} ({_objects.Count} pieces).");
        }

        private void DrawTimingReadout()
        {
            if (ShowTiming == null || !ShowTiming.Value || !CustomSession.Active || Theme.Hud == null) return;
            if (Time.unscaledTime >= _timingTextTime)
            {
                _timingTextTime = Time.unscaledTime + 0.25f;
                float frame = Mathf.Max(0.01f, _avgFrame);
                float editor = _avgUpdate + _avgGui;
                _timingText = $"{1000f / frame:0} fps  ·  frame {frame:0.00} ms  ·  editor {editor:0.00} ms "
                            + $"(update {_avgUpdate:0.00} incl. ghost {_avgGhost:0.00}, GUI {_avgGui:0.00}) = {editor / frame * 100f:0}%"
                            + $"  ·  worst frame {_worstShown:0.0} ms" + _lastPlace;
            }
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Theme.ShadowLabel(new Rect(12f, Screen.height - 34f, Screen.width - 24f, 26f), _timingText, Theme.Hud);
            GUI.matrix = matrix;
        }
    }
}
