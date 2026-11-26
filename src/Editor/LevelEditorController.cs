using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using Isto.Core.Game;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// The editor itself: flying, placing and editing objects, the IMGUI panel,
    /// saving/loading, and running the level (timer, checkpoints, finish, kill height).
    /// </summary>
    public partial class LevelEditorController : MonoBehaviour
    {
        public static LevelEditorController Instance { get; private set; }

        // ---- config, set by the plugin ----
        public ConfigEntry<KeyboardShortcut> ToggleEditorKey;
        public ConfigEntry<KeyboardShortcut> CursorModeKey;
        public ConfigEntry<float> FlyVerticalSpeed;
        public ConfigEntry<Vector2> HelpPosition;
        public ConfigEntry<bool> HelpCompact;
        public ConfigEntry<float> UiScale;
        public string LevelsDirectory;

        // ---- level state ----
        private Transform _root;
        private readonly List<LevelObject> _objects = new List<LevelObject>();
        private LevelData _level = new LevelData();

        // ---- editor state ----
        public bool IsEditing { get; private set; }
        private bool _cursorMode;
        private bool _snap;
        private ObjectKind _kind = ObjectKind.Platform;
        private LevelObject _ghost;
        private LevelObject _selected;
        private Vector3 _ghostEuler;
        private float _ghostDistance = 10f;
        /// <summary>How far away (m) the object being placed sits, set with Ctrl + wheel. 0 = on whatever the crosshair hits.</summary>
        private float _ghostReach;
        private Vector3 _frozenPlayerPos;
        private bool _savedRestart, _savedRestartStart, _savedHighSpeed;

        // ---- play state ----
        private bool _hadPlayer;
        /// <summary>The custom level has been built in the hidden world and can be edited/played.</summary>
        private bool _sessionReady;
        private bool _exploreSeen;
        private float _sessionSettleTime;
        private bool _timerRunning;
        private float _runTime;
        private float _bestTime = -1f;
        private float _killCooldown;
        private string _message;
        private float _messageUntil;

        // ---- GUI state ----
        private Rect _window = new Rect(20, 120, 420, 640);
        private Vector2 _scroll;
        private string[] _levelFiles = new string[0];
        private readonly Dictionary<string, string> _fieldText = new Dictionary<string, string>();
        private LevelObject _fieldTextOwner;
        private GUIStyle _hudStyle, _bigStyle;
        private Rect _helpRect;
        private Vector2? _helpPos;
        private bool _helpPosDirty;
        private bool _onTitle;
        private float _beginTime;
        private bool _titleMenuOpen;
        private Rect _titleRect = new Rect(0, 0, 440, 500);
        private Vector2 _titleScroll;
        private int _titleEnvironment;
        private List<string> _environmentNames = new List<string>();
        private string _titleError;

        /// <summary>The object bar, in key order.</summary>
        private static readonly ObjectKind[] Hotbar =
        {
            ObjectKind.Platform, ObjectKind.Ramp, ObjectKind.Wedge, ObjectKind.Cylinder, ObjectKind.Sphere, ObjectKind.JumpPad,
            ObjectKind.Bumper, ObjectKind.Launcher, ObjectKind.WallLauncher, ObjectKind.CogLauncher,
            ObjectKind.Checkpoint, ObjectKind.Start, ObjectKind.Finish, ObjectKind.KillZone, ObjectKind.Pendulum, ObjectKind.Sign,
        };

        /// <summary>Keys 1-0 pick the first ten; Shift+1 to Shift+6 the rest.</summary>
        private static readonly KeyCode[] HotbarKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
            KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
        };

        private static bool HotbarNeedsShift(int i) => i >= 10;

        private static readonly string[] HotbarKeyNames = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "Shift 1", "Shift 2", "Shift 3", "Shift 4", "Shift 5", "Shift 6" };

        private static readonly Color[] Palette =
        {
            new Color(0.78f, 0.78f, 0.8f), new Color(0.3f, 0.3f, 0.33f), new Color(0.9f, 0.3f, 0.25f),
            new Color(1f, 0.55f, 0.1f), new Color(1f, 0.85f, 0.2f), new Color(0.3f, 0.8f, 0.35f),
            new Color(0.2f, 0.6f, 0.95f), new Color(0.6f, 0.35f, 0.9f), new Color(0.55f, 0.38f, 0.22f),
        };

        private void Awake()
        {
            Instance = this;
            var rootGo = new GameObject("GTWLevelEditor_Level");
            rootGo.transform.SetParent(transform, false);
            _root = rootGo.transform;
        }

        // =====================================================================
        // Main loop
        // =====================================================================

        private void Update()
        {
            ReadMouse();
            RunPendingFileAction();
            UpdatePlayerVisibility();
            if (CustomSession.Active && _sessionReady) UpdateTubeCuts();
            _onTitle = CustomSession.IsOnTitleScreen();
            if (_onTitle) EnsureTitleButton();
            if (CustomSession.Active && !_sessionReady && _onTitle && Time.unscaledTime - _beginTime > 20f)
            {
                // The game never started loading the world; don't leave saves blocked.
                CustomSession.End();
                _titleError = "The level didn't start. Open the Doinkler Portfolio once, then try again.";
                _titleMenuOpen = true;
            }

            if (CustomSession.Explore)
            {
                // Debug exploring: the world runs untouched; end when back on the title screen.
                if (!_onTitle && GameBridge.Player != null && !Isto.Core.Game.GameState.CurrentlyLoading) _exploreSeen = true;
                if (_onTitle && _exploreSeen) { _exploreSeen = false; CustomSession.End(); }
                return;
            }

            var player = GameBridge.Player;
            if (player == null)
            {
                if (_hadPlayer) OnLeftLevel();
                return;
            }
            if (!_hadPlayer) OnEnteredLevel();

            // The editor only exists inside a custom level started from the title screen,
            // so it never mixes with (or teleports you around) the game's own levels.
            if (!CustomSession.Active) return;
            if (!_sessionReady)
            {
                TrySetUpSession(player);
                return;
            }

            if (DemoRunning)
            {
                // The demo drives the camera and builds the level; keep the player parked.
                player.PlayerTransform.position = _demoPlayerPos;
                return;
            }

            RunPausePickerLoad();
            // The game's Esc menu stops time: its clicks and keys are for the menu, not the editor.
            if (Time.timeScale <= 0f) return;
            // Waiting for a new key binding: the key pressed is for that, not the editor.
            if (CaptureRebindKey()) return;

            if (ToggleEditorKey.Value.IsDown())
            {
                if (IsEditing) ExitEditor(); else EnterEditor();
            }

            if (IsEditing) EditorUpdate(player);
            else PlayUpdate(player);
        }

        private void OnEnteredLevel()
        {
            _hadPlayer = true;
            _sessionReady = false;
            _sessionSettleTime = 0f;
            _root.gameObject.SetActive(true);
        }

        private void OnLeftLevel()
        {
            _hadPlayer = false;
            IsEditing = false;
            _cursorMode = false;
            DestroyGhost();
            _timerRunning = false;
            // The title screen can have its own player, which disappears when the world starts
            // loading; only a player leaving a built custom level ends the session.
            if (_sessionReady)
            {
                ClearLevel();
                CustomSession.End();
            }
            _sessionReady = false;
            _root.gameObject.SetActive(false);
            GameBridge.ResetForNewLevel();
        }

        /// <summary>
        /// Waits until the world has loaded and the game has done its own first respawn,
        /// then hides the world and builds the custom level.
        /// </summary>
        private void TrySetUpSession(Isto.GTW.Player.PlayerController p)
        {
            if (GameState.CurrentlyLoading || !p.IsMoveState || CustomSession.FindGameState()?.IsInTitleScene() != false)
            {
                _sessionSettleTime = 0f;
                return;
            }
            _sessionSettleTime += Time.unscaledDeltaTime;
            if (_sessionSettleTime < 0.5f) return;

            _sessionReady = true;
            _ = GameBridge.TriggerLayer; // detect from the world's own triggers before they are switched off
            GameBridge.DisableWorldHazards();
            GameBridge.HideGameTimers();
            ApplyLevel(CustomSession.Level ?? LevelData.CreateNew(CustomSession.Environment));
            _bestTime = PersonalBests.Get(_level.name);
            if (CustomSession.StartInEditor)
            {
                MovePlayerToStart();
                EnterEditor();
            }
            else
            {
                StartPlay();
            }
        }

        private void MovePlayerToStart()
        {
            var start = StartObject;
            var p = GameBridge.Player;
            if (start == null || p == null) return;
            var cp = start.AsCheckpoint();
            p.TeleportCharacter(cp.position, Quaternion.LookRotation(cp.forward));
            p.ResetVelocityToZero();
        }

        // =====================================================================
        // Editor mode
        // =====================================================================

        private void EnterEditor()
        {
            var p = GameBridge.Player;
            IsEditing = true;
            _timerRunning = false;
            _snap = GridSnap != null && GridSnap.Value;
            _savedRestart = p.restartAbilityAvailable;
            _savedRestartStart = p.restartStartingCheckpointAbilityAvailable;
            _savedHighSpeed = p.noClipHighSpeedMode;
            // The no-clip state respawns on these buttons, which would throw us out of the editor.
            p.restartAbilityAvailable = false;
            p.restartStartingCheckpointAbilityAvailable = false;
            GameBridge.EnterNoClip(p);
            foreach (var o in _objects) o.SetEditorVisible(true);
            SpawnGhost(LevelObjectData.CreateDefault(_kind));
            Toast("Editor on");
        }

        private void ExitEditor()
        {
            var p = GameBridge.Player;
            SetCursorMode(false);
            DestroyGhost();
            IsEditing = false;
            p.restartAbilityAvailable = _savedRestart;
            p.restartStartingCheckpointAbilityAvailable = _savedRestartStart;
            p.noClipHighSpeedMode = _savedHighSpeed;
            foreach (var o in _objects) o.SetEditorVisible(false);
            StartPlay();
        }

        private void EditorUpdate(Isto.GTW.Player.PlayerController p)
        {
            // Something (a respawn, the game's no-clip key) may have changed the player's state.
            if (!GameBridge.IsInNoClip(p) && !_cursorMode) GameBridge.EnterNoClip(p);

            if (CursorModeKey.Value.IsDown()) SetCursorMode(!_cursorMode);
            bool typing = GUIUtility.keyboardControl != 0;
            if (!typing) HandleShortcuts();
            if (!typing && !Ctrl && EditorKeys.Down(EditorAction.HidePlayer)) TogglePlayerHidden();

            if (_cursorMode)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                p.PlayerTransform.position = _frozenPlayerPos;
                CursorModeUpdate(typing);
                return;
            }

            // ---- flying ----
            float vertical = (EditorKeys.Held(EditorAction.FlyUp) ? 1f : 0f) - (EditorKeys.Held(EditorAction.FlyDown) ? 1f : 0f);
            bool fast = EditorKeys.Held(EditorAction.Fast);
            p.noClipHighSpeedMode = fast;
            if (vertical != 0f)
            {
                p.PlayerTransform.position += Vector3.up * vertical * FlyVerticalSpeed.Value * (fast ? 3f : 1f) * Time.unscaledDeltaTime;
            }

            // ---- object choice ----
            for (int i = 0; i < HotbarKeys.Length; i++)
            {
                if (Input.GetKeyDown(HotbarKeys[i]) && !Ctrl && Shift == HotbarNeedsShift(i)) SetKind(Hotbar[i]);
            }

            if (EditorKeys.Down(EditorAction.Snap) && !Ctrl) SetSnap(!_snap);

            if (_rampTool)
            {
                RampDrawUpdate(fast);
                return;
            }

            if (_ghost == null) SpawnGhost(LevelObjectData.CreateDefault(_kind));

            // ---- rotate / scale the ghost ----
            if (EditorKeys.Down(EditorAction.TurnLeft)) _ghostEuler.y -= 15f;
            if (EditorKeys.Down(EditorAction.TurnRight)) _ghostEuler.y += 15f;
            if (EditorKeys.Down(EditorAction.TiltBack)) _ghostEuler.x -= 15f;
            if (EditorKeys.Down(EditorAction.TiltForward)) _ghostEuler.x += 15f;
            if (EditorKeys.Down(EditorAction.ResetTurn))
            {
                _ghostEuler = LevelObjectData.CreateDefault(_kind).rotation; // launchers start tilted
                _ghostReach = 0f;
            }

            // ---- launch speed: the aimed-at jump pad/bumper if there is one, otherwise the one being placed ----
            bool lower = EditorKeys.Down(EditorAction.LaunchSlower), raise = EditorKeys.Down(EditorAction.LaunchFaster);
            if (lower || raise)
            {
                var aimed = AimedObject();
                var target = aimed != null && aimed.Data.HasForce ? aimed : (_ghost.Data.HasForce ? _ghost : null);
                if (target != null)
                {
                    if (target == aimed) PushUndo();
                    float step = fast ? 5f : 1f;
                    target.Data.force = Mathf.Clamp(target.Data.force + (raise ? step : -step), 0f, 200f);
                    Toast($"{target.Kind} launch: {target.Data.force:0} m/s" + (target == _ghost ? "" : " (placed)"));
                    if (target == aimed) Select(aimed);
                }
            }

            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f)
            {
                float factor = Mathf.Pow(1.1f, wheel);
                if (Ctrl)
                {
                    // Start from where it is now, so the first notch pulls it in from the surface.
                    if (_ghostReach <= 0f) _ghostReach = Vector3.Distance(CenterRay().origin, _ghost.Data.position);
                    _ghostReach = Mathf.Clamp(_ghostReach * factor, 2f, 120f);
                }
                else if (_ghost.IsRamp) { } // a copied ramp keeps its shape
                else if (fast) _ghost.Data.scale.y = Mathf.Clamp(_ghost.Data.scale.y * factor, 0.05f, 100f);
                else _ghost.Data.scale = ClampScale(_ghost.Data.scale * factor);
            }

            PositionGhost();

            // ---- place / pick up / delete ----
            if (Input.GetMouseButtonDown(0)) PlaceGhost();
            else if (Input.GetMouseButtonDown(1))
            {
                var aimed = AimedObject();
                if (aimed != null) PickUp(aimed);
            }
            if (EditorKeys.Down(EditorAction.DeleteAimed))
            {
                var aimed = AimedObject();
                if (aimed != null) DeleteObject(aimed);
            }
        }

        /// <summary>Chooses what the next click places. The ramp tool draws instead of placing a ghost.</summary>
        private void SetKind(ObjectKind kind)
        {
            _kind = kind;
            _ghostEuler = LevelObjectData.CreateDefault(kind).rotation;
            _draft.Clear();
            if (kind == ObjectKind.Ramp)
            {
                _rampTool = true;
                DestroyGhost();
            }
            else
            {
                _rampTool = false;
                SpawnGhost(LevelObjectData.CreateDefault(kind));
                if (_cursorMode) _ghost.gameObject.SetActive(false);
            }
        }

        private static Vector3 ClampScale(Vector3 s) =>
            new Vector3(Mathf.Clamp(s.x, 0.05f, 200f), Mathf.Clamp(s.y, 0.05f, 200f), Mathf.Clamp(s.z, 0.05f, 200f));

        private Ray CenterRay() => Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        /// <summary>Puts the ghost where the crosshair points: resting on the surface it hits, or floating in front.</summary>
        private void PositionGhost()
        {
            var ghost = _ghost;
            ghost.Data.rotation = _ghostEuler;
            ghost.Data.position = Vector3.zero;
            ghost.Refresh();

            var ray = CenterRay();
            Vector3 pos;
            int along = -1; // the axis the ghost rests along on a surface (its height on a floor)
            // A surface closer than the chosen distance still catches it; otherwise it floats at that distance.
            if (RaycastSurface(ray, _ghostReach > 0f ? _ghostReach : 80f, out var hit))
            {
                // Push the object out along the surface normal by its half-size in that direction.
                var b = ghost.GetBounds();
                var n = hit.normal;
                float offset = Mathf.Abs(n.x) * b.extents.x + Mathf.Abs(n.y) * b.extents.y + Mathf.Abs(n.z) * b.extents.z;
                pos = hit.point + n * offset - b.center;
                along = Mathf.Abs(n.y) >= Mathf.Abs(n.x) && Mathf.Abs(n.y) >= Mathf.Abs(n.z) ? 1 : Mathf.Abs(n.x) >= Mathf.Abs(n.z) ? 0 : 2;
            }
            else
            {
                pos = ray.GetPoint(_ghostReach > 0f ? _ghostReach : _ghostDistance);
            }
            // Resting on a surface keeps that height (or depth against a wall): only the other axes snap.
            pos = SnapPosition(ghost, BoundsAt(ghost, pos), pos, along != 0, along != 1, along != 2);
            ghost.Data.position = pos;
            ghost.Refresh();
        }


        private void SetSnap(bool on)
        {
            _snap = on;
            if (GridSnap != null) GridSnap.Value = on;
            Toast(on ? $"Snapping on ({GridStep:0.##} m grid, 15°{(ObjectSnapOn ? ", to other objects" : "")})" : "Snapping off");
        }

        private bool RaycastSurface(Ray ray, float distance, out RaycastHit best)
        {
            best = default;
            float bestDist = float.MaxValue;
            var playerRoot = GameBridge.Player != null ? GameBridge.Player.transform : null;
            foreach (var h in Physics.RaycastAll(ray, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.distance >= bestDist) continue;
                if (playerRoot != null && h.collider.transform.IsChildOf(playerRoot)) continue;
                if (_ghost != null && h.collider.transform.IsChildOf(_ghost.transform)) continue;
                best = h;
                bestDist = h.distance;
            }
            return bestDist < float.MaxValue;
        }

        private LevelObject RaycastObject(Ray ray)
        {
            LevelObject best = null;
            float bestDist = float.MaxValue;
            foreach (var h in Physics.RaycastAll(ray, 120f, ~0, QueryTriggerInteraction.Collide))
            {
                var obj = h.collider.GetComponentInParent<LevelObject>();
                if (obj == null || obj == _ghost || h.distance >= bestDist) continue;
                best = obj;
                bestDist = h.distance;
            }
            return best;
        }

        private LevelObject AimedObject() => RaycastObject(CenterRay());

        private void SpawnGhost(LevelObjectData data)
        {
            DestroyGhost();
            _ghost = LevelObject.Create(data, _root);
            _ghost.SetGhost(true);
            _ghost.SetEditorVisible(true);
        }

        private void DestroyGhost()
        {
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;
        }

        private void PlaceGhost()
        {
            PushUndo();
            var placed = _ghost;
            _ghost = null;
            placed.SetGhost(false);
            if (placed.Kind == ObjectKind.Start)
            {
                foreach (var other in _objects.Where(o => o.Kind == ObjectKind.Start).ToList()) DeleteObject(other, undo: false);
            }
            _objects.Add(placed);
            Select(placed);
            if (placed.IsRamp)
            {
                // A moved or pasted ramp: go back to drawing ramps.
                _kind = ObjectKind.Ramp;
                _rampTool = true;
                return;
            }
            // Next ghost copies the last one so runs of identical platforms are quick to build.
            SpawnGhost(placed.Data.Clone());
        }

        private void PickUp(LevelObject obj)
        {
            PushUndo();
            DestroyGhost();
            _draft.Clear();
            _rampTool = false;
            _objects.Remove(obj);
            if (_selected == obj) _selected = null;
            _kind = obj.Kind;
            _ghostEuler = obj.Data.rotation;
            _ghost = obj;
            _ghost.SetGhost(true);
        }

        private void DeleteObject(LevelObject obj, bool undo = true)
        {
            if (undo) PushUndo();
            _objects.Remove(obj);
            if (_selected == obj) _selected = null;
            Destroy(obj.gameObject);
        }

        private void Select(LevelObject obj)
        {
            if (obj != _selected)
            {
                _selectedPoint = -1;
                if (obj != null) SetPanelTab(PanelTab.Object); // picking something shows its settings
            }
            _selected = obj;
            _group.Clear(); // a plain select picks just this one; SetSelection rebuilds a group after
            if (obj != null) _group.Add(obj);
            _fieldText.Clear();
            _fieldTextOwner = obj;
        }

        private void SetCursorMode(bool on)
        {
            if (on == _cursorMode) return;
            _cursorMode = on;
            _drag = DragKind.None;
            _pressPending = _boxing = false;
            GameBridge.SetCameraBrainEnabled(!on);
            if (_ghost != null) _ghost.gameObject.SetActive(!on);
            if (on)
            {
                _frozenPlayerPos = GameBridge.Player.PlayerTransform.position;
                RememberMouseModeCamera();
                RefreshLevelList();
            }
            else
            {
                HandCameraBackToPlayer();
                GUIUtility.keyboardControl = 0; // a text field left focused would block the shortcuts
                SaveHelpPosition();
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
        }

        // =====================================================================
        // Play mode
        // =====================================================================

        private LevelObject StartObject => _objects.FirstOrDefault(o => o.Kind == ObjectKind.Start);

        private void StartPlay()
        {
            var p = GameBridge.Player;
            var start = StartObject;
            if (start != null) GameBridge.RespawnAt(start.AsCheckpoint());
            else p.ChangeToMoveState();
            ResetTimer();
        }

        private void ResetTimer()
        {
            _runTime = 0f;
            _runFinished = false;
            _timerRunning = _objects.Count > 0;
            _motionStart = Time.fixedTime; // moving pieces start from their placed pose on every (re)start
        }

        private float _motionStart;

        /// <summary>Seconds since play (re)started; every moving piece runs on this one clock so they stay in step.</summary>
        public float MotionTime => Time.fixedTime - _motionStart;

        private void PlayUpdate(Isto.GTW.Player.PlayerController p)
        {
            if (_objects.Count == 0) return;
            if (_timerRunning) _runTime += Time.deltaTime;
            if (_killCooldown > 0f) _killCooldown -= Time.unscaledDeltaTime;
            if (_level.useKillHeight && p.PlayerPosition.y < _level.killHeight) KillPlayer();
        }

        /// <summary>Times the player was killed (kill zone or kill height), for scripted test runs.</summary>
        public int Kills { get; private set; }
        public Vector3 LastKillPosition { get; private set; }

        public void KillPlayer()
        {
            if (_killCooldown > 0f) return;
            _killCooldown = 1f;
            var p = GameBridge.Player;
            Kills++;
            LastKillPosition = p != null ? p.PlayerPosition : Vector3.zero;
            if (p?.CheckpointManager != null) p.CheckpointManager.Respawn(showBlackOut: true);
        }

        public void ReachCheckpoint(LevelObject checkpoint)
        {
            GameBridge.SetActiveCheckpoint(checkpoint.AsCheckpoint());
            Toast("Checkpoint");
        }

        /// <summary>The time of the last finished run (for scripted test runs).</summary>
        public float LastFinishTime { get; private set; } = -1f;
        public float RunTime => _runTime;

        public void ReachFinish()
        {
            if (!_timerRunning) return;
            _timerRunning = false;
            _runFinished = true;
            bool best = _bestTime < 0f || _runTime < _bestTime;
            if (best)
            {
                _bestTime = _runTime;
                PersonalBests.Set(_level.name, _runTime);
            }
            LastFinishTime = _runTime;
            Toast($"Finished in {FormatTime(_runTime)}" + (best ? "  (best!)" : $"  (best {FormatTime(_bestTime)})"), 6f);
        }

        /// <summary>Called by the Harmony patch when the game restarts from the starting checkpoint.</summary>
        public bool TryRestartFromStart()
        {
            if (IsEditing) return false;
            ResetTimer(); // a restart starts a new run, with or without a Start pad
            var start = StartObject;
            if (start == null) return false;
            GameBridge.RespawnAt(start.AsCheckpoint());
            return true;
        }

        private static string FormatTime(float t) => TimeSpan.FromSeconds(t).ToString(@"mm\:ss\.ff");

        private void Toast(string text, float seconds = 2f)
        {
            _message = text;
            _messageUntil = Time.unscaledTime + seconds;
        }

        // =====================================================================
        // Saving and loading
        // =====================================================================

        private string PathFor(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(name)) name = "Untitled";
            return Path.Combine(LevelsDirectory, name + ".json");
        }

        private void RefreshLevelList()
        {
            Directory.CreateDirectory(LevelsDirectory);
            _levelFiles = Directory.GetFiles(LevelsDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void SaveLevel()
        {
            _level.objects = _objects.Select(o => o.Data).ToList();
            _level.environment = CustomSession.Environment;
            var path = PathFor(_level.name);
            Directory.CreateDirectory(LevelsDirectory);
            File.WriteAllText(path, LevelJson.LevelToJson(_level));
            RefreshLevelList();
            Toast("Saved " + Path.GetFileName(path));
            Plugin.Log.LogInfo("Saved level to " + path);
        }

        private void LoadLevel(string name)
        {
            try
            {
                var data = ReadLevel(name);
                ApplyLevel(data);
                ClearUndo();
                _bestTime = PersonalBests.Get(_level.name);
                string note = data.environment != CustomSession.Environment
                    ? $" (made in World {data.environment + 1}, opened in World {CustomSession.Environment + 1})" : "";
                Toast($"Loaded {name}: {_objects.Count} objects{note}", 4f);
                if (IsEditing) MovePlayerToStart();
                else StartPlay();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not load level {name}: {e}");
                Toast("Could not load " + name);
            }
        }

        private LevelData ReadLevel(string name)
        {
            var data = LevelJson.LevelFromJson(File.ReadAllText(PathFor(name)));
            if (data == null) throw new InvalidDataException("empty file");
            data.name = name;
            return data;
        }

        /// <summary>Replaces the placed objects with the ones in <paramref name="data"/>.</summary>
        private void ApplyLevel(LevelData data)
        {
            ClearLevel();
            _level = data;
            foreach (var d in data.objects)
            {
                var o = LevelObject.Create(d, _root);
                o.SetEditorVisible(IsEditing);
                _objects.Add(o);
            }
        }

        private void ClearLevel()
        {
            foreach (var o in _objects) Destroy(o.gameObject);
            _objects.Clear();
            _selected = null;
            _level = new LevelData { environment = CustomSession.Environment };
        }

        // =====================================================================
        // GUI
        // =====================================================================

        /// <summary>
        /// All editor UI is laid out for a 1080p screen and scaled up, so it stays the same
        /// size relative to the screen at 1440p or 4K. UiScale in the config multiplies this.
        /// </summary>
        private float UiScaleFactor => Mathf.Max(0.5f, Screen.height / 1080f * Mathf.Clamp(UiScale.Value, 0.25f, 4f));
        private float VW => Screen.width / UiScaleFactor;
        private float VH => Screen.height / UiScaleFactor;

        private bool MouseOverWindow()
        {
            var m = MousePos;
            var p = new Vector2(m.x, Screen.height - m.y) / UiScaleFactor;
            if (Docked && (!GameViewRect.Contains(p) || DividerRect.Contains(p) || _draggingDivider)) return true; // the toolbar, the divider and the panel column
            return _window.Contains(p) || (HelpVisible && _helpRect.Contains(p)) || _hotbarRect.Contains(p);
        }

        private GUISkin EditorSkin()
        {
            Theme.Ensure();
            return Theme.Skin;
        }

        private void OnGUI()
        {
            if (IsEditing && CustomSession.Active && _sessionReady && Event.current.type == EventType.Repaint && (!DemoRunning || _demoOverlays))
            {
                GUI.matrix = Matrix4x4.identity;
                DrawOverlays();
            }
            float s = UiScaleFactor;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            GUI.skin = EditorSkin();
            if (_hudStyle == null)
            {
                _hudStyle = Theme.Hud;
                _bigStyle = Theme.Big;
                _rich = null;
            }

            if (!CustomSession.Active || !_sessionReady || GameBridge.Player == null)
            {
                DrawTitleMenu();
                return;
            }

            if (DemoRunning)
            {
                DrawDemoGui();
                return;
            }

            if (IsEditing)
            {
                bool docked = Docked;
                if (docked) DrawDockBackgrounds();
                if (HelpVisible) DrawHelp();
                DrawHotbar();
                if (_cursorMode)
                {
                    Cursor.visible = true;
                    Cursor.lockState = CursorLockMode.None;
                }
                if (docked)
                {
                    DrawDockDivider();
                    DrawDockedPanel();
                }
                else if (_cursorMode)
                {
                    if (!_resizingPanel) ApplyPanelSize();
                    _window = GUILayout.Window(0x4C45, _window, DrawWindow, "Level Editor", GUILayout.Width(_window.width), GUILayout.Height(_window.height));
                }
                if (!_cursorMode)
                {
                    var c = GameViewRect.center;
                    Theme.ShadowLabel(new Rect(c.x - 15, c.y - 18, 30, 36), "+", _bigStyle);
                }
            }
            else if (_objects.Count > 0 && !GameTimerVisible)
            {
                // Only when the game's own timer panel is switched off in its settings.
                Theme.ShadowLabel(new Rect(VW / 2f - 150, 20, 300, 44), FormatTime(_runTime), _bigStyle);
            }

            DrawPausePicker();

            if (_message != null && Time.unscaledTime < _messageUntil)
            {
                var size = Theme.Caption.CalcSize(new GUIContent(_message));
                var view = GameViewRect;
                var r = new Rect(view.center.x - size.x / 2f - 28f, view.y + view.height * 0.2f, size.x + 56f, 60f);
                GUI.Box(r, GUIContent.none, Theme.Pill);
                GUI.Label(r, _message, Theme.Caption);
            }
        }

        // ---------------- help window (draggable while the panel is open) ----------------

        private void DrawHelp()
        {
            // Kept inside the game area (the whole screen, or what the docked toolbar and panel leave).
            var view = GameViewRect;
            var size = HelpCompact.Value ? new Vector2(Mathf.Min(view.width - 20, 760), 92) : new Vector2(Mathf.Min(view.width - 20, 1000), 196);
            if (_helpPos == null) _helpPos = HelpPosition.Value;
            var pos = _helpPos.Value;
            _helpRect = new Rect(
                Mathf.Clamp(pos.x, view.x, Mathf.Max(view.x, view.xMax - size.x)),
                Mathf.Clamp(pos.y, view.y, Mathf.Max(view.y, view.yMax - size.y)),
                size.x, size.y);
            var moved = GUI.Window(0x4C46, _helpRect, DrawHelpContents, _cursorMode ? "Controls  ·  drag to move" : "Controls");
            if (moved.position != _helpRect.position)
            {
                _helpRect = moved;
                _helpPos = moved.position;
                _helpPosDirty = true;
            }
            // Write the config file once, when the drag ends.
            if (_helpPosDirty && Event.current.rawType == EventType.MouseUp) SaveHelpPosition();
        }

        private void SaveHelpPosition()
        {
            if (!_helpPosDirty || _helpPos == null) return;
            _helpPosDirty = false;
            HelpPosition.Value = _helpPos.Value;
        }

        private static string K(string key) => Theme.Key(key);

        private void DrawHelpContents(int id)
        {
            const string gap = "      ";
            string what = _rampTool
                ? $"Drawing a <b>ramp</b>  <color={Theme.MutedHex}>{_draft.Count} points · width {_draftWidth:0.#} m · height +{_draftHeight:0.#} m · bank {_draftBank:0}°" + (_draftReach > 0f ? $" · {_draftReach:0} m away" : "") + "</color>"
                : $"Placing <b>{KindName(_kind)}</b>" + (_ghost != null && _ghost.Data.HasForce ? $"  <color={Theme.MutedHex}>{_ghost.Data.force:0} m/s</color>" : "")
                  + (_ghostReach > 0f ? $"  <color={Theme.MutedHex}>{_ghostReach:0} m away</color>" : "");
            string text = $"{what}{gap}<color={Theme.MutedHex}>Snap {(_snap ? "on" : "off")}</color>{gap}{K(CursorModeKey.Value.ToString())} mouse mode{gap}{K(ToggleEditorKey.Value.ToString())} play";
            if (!HelpCompact.Value)
                text += "\n" + (_cursorMode ? MouseModeControls(gap) : _rampTool ? RampControls(gap) : PlacingControls(gap));
            GUI.Label(new Rect(18, 44, _helpRect.width - 170, _helpRect.height - 50), text, _hudStyle);
            if (GUI.Button(new Rect(_helpRect.width - 136, 10, 80, 30), HelpCompact.Value ? "More" : "Less", Theme.GhostButton))
                HelpCompact.Value = !HelpCompact.Value;
            if (GUI.Button(new Rect(_helpRect.width - 48, 10, 32, 30), "×", Theme.Close))
            {
                HelpVisible = false;
                Toast("Controls hidden. Reopen them from the panel's Controls tab", 3f);
            }
            if (_cursorMode) GUI.DragWindow();
        }

        /// <summary>The key(s) bound to an editor action, as a key cap.</summary>
        private static string B(EditorAction a) => K(EditorKeys.Name(a));
        private static string BC(EditorAction a) => K("Ctrl+" + EditorKeys.Name(a));
        private static string Fast => EditorKeys.Name(EditorAction.Fast);

        private static string MouseModeControls(string gap) =>
            $"{K("Click")} select an object, then drag its handles{gap}{K("Drag")} box-select several{gap}{K("Shift+Click")} add / remove{gap}{BC(EditorAction.SelectAll)} all" +
            $"\nWith several selected, the yellow-outlined one carries the handles; moving or rotating it takes the rest along{gap}{B(EditorAction.HandlesMove)} move (yellow square: along the ground){gap}{B(EditorAction.HandlesRotate)} rotate{gap}{B(EditorAction.HandlesScale)} scale" +
            $"\n{K("Drag")} a ramp's white points to reshape it ({K("Shift")} for up / down); a chosen point gets handles: {B(EditorAction.HandlesMove)} move, {B(EditorAction.HandlesRotate)} bank, {B(EditorAction.HandlesScale)} width. Click the ramp itself for whole-ramp handles" +
            $"\n{B(EditorAction.DeleteSelected)} removes the chosen point, object or selection" +
            $"\n{K("Hold right mouse")} look around, with {B(EditorAction.CameraForward)}{B(EditorAction.CameraLeft)}{B(EditorAction.CameraBack)}{B(EditorAction.CameraRight)} to fly, {B(EditorAction.CameraDown)} / {B(EditorAction.CameraUp)} down / up{gap}{K("Wheel")} move forward / back{gap}{B(EditorAction.HidePlayer)} hide player" +
            $"\n{BC(EditorAction.Undo)} undo{gap}{BC(EditorAction.Redo)} redo{gap}{BC(EditorAction.Duplicate)} duplicate{gap}{BC(EditorAction.Copy)} / {BC(EditorAction.Paste)} copy / paste";

        private static string RampControls(string gap) =>
            $"{K("Click")} add a point; the ramp curves smoothly through them. Start on another ramp's end to join it." +
            $"\n{K("Wheel")} height of the next point{gap}{K(Fast + "+Wheel")} width{gap}{B(EditorAction.BankLeft)} / {B(EditorAction.BankRight)} bank{gap}{B(EditorAction.ResetRampPoint)} reset{gap}{K("Ctrl+Wheel")} pull the point closer / push it away" +
            $"\n{B(EditorAction.RemoveRampPoint)} remove the last point{gap}{B(EditorAction.FinishRamp)} or click the last point again: finish" +
            $"\n{B(EditorAction.FlyUp)} / {B(EditorAction.FlyDown)} fly up / down{gap}{K(Fast)} fast{gap}{B(EditorAction.Snap)} grid snap{gap}{K(Instance != null ? Instance.CursorModeKey.Value.ToString() : "Tab")} afterwards to fine-tune the points";

        private static string PlacingControls(string gap) =>
            $"{K("1")}–{K("0")}, {K("Shift+1")}–{K("6")} or the bar at the top: choose an object{gap}{B(EditorAction.FlyUp)} / {B(EditorAction.FlyDown)} fly up / down{gap}{K(Fast)} fast" +
            $"\n{K("Left click")} place{gap}{K("Right click")} pick up{gap}{B(EditorAction.DeleteAimed)} delete{gap}{B(EditorAction.TurnLeft)} / {B(EditorAction.TurnRight)} turn{gap}{B(EditorAction.TiltBack)} / {B(EditorAction.TiltForward)} tilt{gap}{B(EditorAction.ResetTurn)} reset{gap}{B(EditorAction.Snap)} snap" +
            $"\n{K("Wheel")} size{gap}{K(Fast + "+Wheel")} height{gap}{K("Ctrl+Wheel")} pull closer / push away{gap}{B(EditorAction.LaunchSlower)} / {B(EditorAction.LaunchFaster)} launch speed{gap}{B(EditorAction.HidePlayer)} hide player" +
            $"\n{BC(EditorAction.Undo)} undo{gap}{BC(EditorAction.Redo)} redo{gap}{BC(EditorAction.Duplicate)} pick up a copy of the aimed object{gap}{BC(EditorAction.Copy)} / {BC(EditorAction.Paste)} copy / paste";

        // ---------------- title screen menu ----------------

        private void DrawTitleMenu()
        {
            if (!_onTitle)
            {
                if (CustomSession.Active)
                    Theme.ShadowLabel(new Rect(0, VH - 70, VW, 44), "Loading custom level…", _bigStyle);
                return;
            }
            if (CustomSession.Active)
            {
                Theme.ShadowLabel(new Rect(0, VH - 70, VW, 44), "Starting custom level…", _bigStyle);
                return;
            }
            if (!_titleMenuOpen) return;

            _titleRect.width = 620;
            if (_titleRect.x == 0 && _titleRect.y == 0)
                _titleRect.position = new Vector2(VW - _titleRect.width - 40, 40); // over the logo, clear of the game's buttons
            _titleRect = GUILayout.Window(0x4C47, _titleRect, DrawTitleWindow, "Level Editor");
        }

        private void DrawTitleWindow(int id)
        {
            if (GUI.Button(new Rect(_titleRect.width - 48, 12, 32, 30), "×", Theme.Close)) _titleMenuOpen = false;

            GUILayout.Label("Build your own courses in an empty Doinkler world. Your Doinkler saves aren't touched while a custom level runs.", Theme.Hint);

            if (_environmentNames.Count > 0)
            {
                Theme.Section("New level  ·  choose the sky");
                _titleEnvironment = Mathf.Clamp(_titleEnvironment, 0, _environmentNames.Count - 1);
                _titleEnvironment = GUILayout.SelectionGrid(_titleEnvironment, _environmentNames.ToArray(), 3, Theme.Segment);
                GUILayout.Space(4);
                if (GUILayout.Button("Create new level", Theme.AccentButton, GUILayout.Height(40)))
                    Begin(LevelData.CreateNew(_titleEnvironment), edit: true);
            }

            Theme.Section("Saved levels");
            _titleScroll = GUILayout.BeginScrollView(_titleScroll, GUILayout.Height(230));
            if (_levelFiles.Length == 0) GUILayout.Label("No saved levels yet. Make one and press Save in the editor panel, or import one.", Theme.Hint);
            foreach (var file in _levelFiles) DrawSavedLevelRow(file);
            GUILayout.EndScrollView();
            GUILayout.Space(6);
            DrawLevelFileButtons();

            DrawTitleMessages();
            GUI.DragWindow(new Rect(0, 0, _titleRect.width - 56, 46));
        }

        private void BeginFromFile(string file, bool edit)
        {
            try { Begin(ReadLevel(file), edit); }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not read level {file}: {e}");
                _titleError = "Could not read " + file;
            }
        }

        private void Begin(LevelData level, bool edit)
        {
            _titleError = CustomSession.Begin(level, edit);
            if (_titleError == null)
            {
                _titleMenuOpen = false;
                _beginTime = Time.unscaledTime;
            }
        }

        // ---------------- hooks for DebugCommands ----------------

        public bool DebugSessionReady => CustomSession.Active && _sessionReady;

        public string DebugStatus() =>
            $"material={GameBridge.MaterialInfo} triggerLayer={GameBridge.TriggerLayer}({LayerMask.LayerToName(GameBridge.TriggerLayer)}) groundLayer={GameBridge.GroundLayer} uiScale={UiScaleFactor:0.00} " +
            $"session={CustomSession.Active} ready={_sessionReady} editing={IsEditing} panel={_cursorMode} onTitle={_onTitle} " +
            $"objects={_objects.Count} env={CustomSession.Environment} player={(GameBridge.Player != null ? GameBridge.Player.PlayerPosition.ToString() : "none")}";

        public string DebugExplore(int env, int level)
        {
            if (!_onTitle) return "not on the title screen";
            CustomSession.Explore = true;
            CustomSession.LevelIndex = level;
            var err = CustomSession.Begin(LevelData.CreateNew(env), true);
            if (err != null) CustomSession.End();
            return err;
        }

        public string DebugBegin(string file, int env, bool edit)
        {
            if (!_onTitle) return "not on the title screen";
            _environmentNames = CustomSession.EnvironmentNames();
            if (file == null) Begin(LevelData.CreateNew(env), edit);
            else BeginFromFile(file, edit);
            return _titleError;
        }

        public void DebugToggleEditor()
        {
            if (!DebugSessionReady) return;
            if (IsEditing) ExitEditor(); else EnterEditor();
        }

        public void DebugToggleTitleMenu()
        {
            _titleMenuOpen = !_titleMenuOpen;
            RefreshLevelList();
            _environmentNames = CustomSession.EnvironmentNames();
        }

        public void DebugTogglePanel()
        {
            if (IsEditing) SetCursorMode(!_cursorMode);
        }

        public string DebugPlace(string kind, Vector3 position)
        {
            if (!DebugSessionReady) return "no session";
            if (!Enum.TryParse(kind, true, out ObjectKind k)) return "unknown kind";
            var d = LevelObjectData.CreateDefault(k);
            d.position = position;
            var o = LevelObject.Create(d, _root);
            o.SetEditorVisible(IsEditing);
            _objects.Add(o);
            return "placed " + k;
        }

        // ---------------- in-level panel ----------------

        private void DrawWindow(int id)
        {
            bool docked = Docked;
            if (!docked && GUI.Button(new Rect(_window.width - 48, 12, 32, 30), "×", Theme.Close)) SetCursorMode(false);
            if (docked && !_cursorMode)
                GUILayout.Label($"Press {CursorModeKey.Value} for mouse mode to use the panel.", Theme.Hint);
            DrawPanelTabs();
            BeginPanelScroll();
            switch (_panelTab)
            {
                case PanelTab.Object: DrawObjectTab(); break;
                case PanelTab.Level: DrawLevelTab(); break;
                case PanelTab.Saved: DrawSavedTab(); break;
                case PanelTab.Controls: DrawControlsTab(); break;
            }
            EndPanelScroll();
            if (docked) return; // fixed in place: no resize grip or dragging
            DrawPanelGrip();
            GUI.DragWindow(new Rect(0, 0, _window.width - 56, 46));
        }

        private void DrawObjectTab()
        {
            if (_selected == null)
            {
                GUILayout.Label("Nothing selected. Click an object in the world to select it.", Theme.Hint);
                return;
            }
            if (GroupActive) { DrawGroupPanel(); return; }
            var sel = _selected;
            string before = LevelJson.ObjectToJson(sel.Data);
            DrawSelected(sel);
            if (_objects.Contains(sel) && LevelJson.ObjectToJson(sel.Data) != before) NotePanelEdit(sel, before);
        }

        private void DrawLevelTab()
        {
            Theme.Section($"Level  ·  World {CustomSession.Environment + 1}");
            GUILayout.BeginVertical(Theme.Card);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(70));
            _level.name = GUILayout.TextField(_level.name ?? "");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save", Theme.AccentButton)) SaveLevel();
            if (GUILayout.Button("New (clear)", Theme.DangerButton)) { PushUndo(); ApplyLevel(LevelData.CreateNew(CustomSession.Environment)); MovePlayerToStart(); Toast("New level"); }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            _level.useKillHeight = GUILayout.Toggle(_level.useKillHeight, "Respawn when falling below the kill height");
            if (_level.useKillHeight)
            {
                _level.killHeight = FloatField("Kill height", "killY", _level.killHeight, 5f);
                if (GUILayout.Button("Set kill height 20 m below me"))
                {
                    _level.killHeight = Mathf.Round(GameBridge.Player.PlayerPosition.y - 20f);
                    _fieldText.Remove("killY");
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawSavedTab()
        {
            Theme.Section("Saved levels");
            if (_levelFiles.Length == 0) GUILayout.Label("None yet.", Theme.Hint);
            foreach (var file in _levelFiles)
            {
                GUILayout.BeginHorizontal(Theme.Row);
                GUILayout.Label(file, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Load", GUILayout.Width(72))) LoadLevel(file);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(LevelsDirectory, Theme.Hint);
        }

        private void DrawSelected(LevelObject obj)
        {
            if (_fieldTextOwner != obj) { _fieldText.Clear(); _fieldTextOwner = obj; }
            var d = obj.Data;
            Theme.Section("Selected  ·  " + KindName(d.Kind));
            GUILayout.BeginVertical(Theme.Card);
            d.position = VectorField("Position", "pos", d.position, 0.5f);
            d.rotation = VectorField("Rotation", "rot", d.rotation, 15f);
            if (d.Kind == ObjectKind.Ramp) DrawRampFields(obj);
            else if (d.Kind == ObjectKind.Pendulum) d.length = Mathf.Clamp(FloatField("Length m", "len", d.length, 0.25f), 0.5f, 40f);
            else d.scale = ClampScale(VectorField("Size", "scl", d.scale, 0.25f));
            if (d.Kind == ObjectKind.Cylinder)
            {
                d.hollow = GUILayout.Toggle(d.hollow, "Hollow (an open tube you can roll through)");
                if (d.hollow)
                {
                    float maxWall = Mathf.Max(0.05f, Mathf.Min(d.scale.x, d.scale.z) * 0.5f - 0.05f);
                    d.wallThickness = Mathf.Clamp(FloatField("Wall m", "wall", d.wallThickness, 0.1f), 0.05f, maxWall);
                    d.cutsPlatforms = GUILayout.Toggle(d.cutsPlatforms, "Cut through platforms (a hole where it passes through one)");
                    GUILayout.Label("Size X / Z set the outside diameter and Y the length. Turn it with Rotation to lie it on its side.", Theme.Hint);
                }
            }
            if (d.Kind == ObjectKind.Wedge)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Slope curve", GUILayout.Width(110));
                float curve = GUILayout.HorizontalSlider(Mathf.Clamp(d.slopeCurve, -1f, 1f), -1f, 1f, GUILayout.ExpandWidth(true));
                curve = Mathf.Round(curve * 20f) / 20f;
                GUILayout.Label(curve < 0f ? $"in {-curve * 100f:0}%" : curve > 0f ? $"out {curve * 100f:0}%" : "straight", GUILayout.Width(80));
                if (GUILayout.Button("Reset", Theme.GhostButton, GUILayout.Width(64))) curve = 0f;
                GUILayout.EndHorizontal();
                d.slopeCurve = curve;
                GUILayout.Label("Bend the slope in (a smooth run-up, like a quarter pipe) or out (a rounded hill). With Scale handles (R), drag the pink handle on the slope's edge.", Theme.Hint);
            }
            if (d.HasForce) d.force = Mathf.Max(0f, FloatField("Launch m/s", "force", d.force, 1f));
            if (d.Kind == ObjectKind.Launcher || d.Kind == ObjectKind.WallLauncher)
            {
                d.travel = Mathf.Clamp(FloatField("Travel m", "travel", d.travel, 0.5f), 0.5f, 50f);
                GUILayout.Label("Grab it and it shoots along the arrow, then throws you. Tilt it with Rotation X.", Theme.Hint);
            }
            if (d.Kind == ObjectKind.CogLauncher)
            {
                d.spin = Mathf.Clamp(FloatField("Spin °/s", "spin", d.spin, 15f), -1080f, 1080f);
                GUILayout.Label("Grab the cog to ride it; let go to fly off. Negative spin turns the other way.", Theme.Hint);
                DrawCogCameraFields(obj);
            }
            if (d.Kind == ObjectKind.Sign) DrawSignFields(d);

            if (d.IsSolidShape)
            {
                GUILayout.Space(2);
                GUILayout.Label("Surface", Theme.Hint);
                var surfaces = (SurfaceType[])Enum.GetValues(typeof(SurfaceType));
                int chosen = SegmentRow(Array.IndexOf(surfaces, d.Surface), Array.ConvertAll(surfaces, s => s == SurfaceType.JumpPad ? "Jump pad" : s.ToString()));
                if (chosen >= 0 && surfaces[chosen] != d.Surface)
                {
                    d.Surface = surfaces[chosen];
                    if (d.Surface == SurfaceType.JumpPad && d.force <= 0f) d.force = 20f;
                    if (d.Surface == SurfaceType.Slippery) { d.iceFriction = 0f; d.iceSteering = 1f; d.iceDrag = 1f; }
                }
                if (d.Surface == SurfaceType.Slippery)
                {
                    d.iceFriction = PercentSlider("Friction", d.iceFriction);
                    d.iceSteering = PercentSlider("Steering", d.iceSteering);
                    d.iceDrag = PercentSlider("Air drag", d.iceDrag);
                    GUILayout.Label("How much of the main game's grip, steering and slowing drag you keep on it. 100% is normal ground; 0% friction means you can't roll up it.", Theme.Hint);
                }
                else if (d.Surface == SurfaceType.Sticky)
                    GUILayout.Label("Grab it to stick on, even upside down.", Theme.Hint);
                else if (d.Surface == SurfaceType.JumpPad)
                {
                    if (d.Kind == ObjectKind.Platform || d.Kind == ObjectKind.Cylinder || d.Kind == ObjectKind.Sphere)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Launch zone", GUILayout.Width(110));
                        d.padHeight = GUILayout.HorizontalSlider(d.padHeight, 0f, 3f, GUILayout.ExpandWidth(true));
                        GUILayout.Label(d.padHeight < 0.01f ? "off" : $"{d.padHeight:0.00} m", GUILayout.Width(64));
                        GUILayout.EndHorizontal();
                        d.padHeight = d.padHeight < 0.02f ? 0f : Mathf.Round(d.padHeight * 20f) / 20f;
                        GUILayout.Label("The orange box: rolling into it throws you along the pad's up, before you touch the top. At \"off\" you bounce on contact.", Theme.Hint);
                    }
                    else
                        GUILayout.Label("Bounces you off the surface you land on at the launch speed. Tilt it to aim.", Theme.Hint);
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Colour", GUILayout.Width(70));
            var old = GUI.backgroundColor;
            foreach (var c in Palette)
            {
                GUI.backgroundColor = c;
                if (GUILayout.Button(GUIContent.none, Theme.Swatch)) d.color = c;
                if (SameColour(c, d.color))
                {
                    var r = GUILayoutUtility.GetLastRect();
                    GUI.backgroundColor = Color.white;
                    GUI.Box(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), GUIContent.none, Theme.SwatchRing);
                }
            }
            GUI.backgroundColor = old;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            if (d.CanMove) DrawMotionFields(d);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Duplicate")) DuplicateSelected();
            if (GUILayout.Button("Delete", Theme.DangerButton)) DeleteObject(obj);
            GUILayout.EndHorizontal();

            if (_selected != null) _selected.Refresh();
        }

        private static bool SameColour(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;

        private static GUIStyle _rich;
        private static GUIStyle RichLabel() => _rich ?? (_rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });

        private static readonly Color[] AxisColours = { new Color(1f, 0.42f, 0.42f), new Color(0.45f, 0.9f, 0.45f), new Color(0.45f, 0.65f, 1f) };
        private float _scrubStart, _scrubX;

        /// <summary>One row: label, then X / Y / Z boxes. Drag an axis letter left or right to change its value.</summary>
        private Vector3 VectorField(string label, string key, Vector3 v, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(70));
            for (int i = 0; i < 3; i++) v[i] = AxisField("XYZ"[i].ToString(), key + "xyz"[i], v[i], step, AxisColours[i]);
            GUILayout.EndHorizontal();
            return v;
        }

        private float AxisField(string axis, string key, float value, float step, Color colour)
        {
            var r = GUILayoutUtility.GetRect(new GUIContent(axis), Theme.AxisLabel, GUILayout.Width(18), GUILayout.Height(32));
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (r.Contains(e.mousePosition) && e.button == 0)
                    {
                        GUIUtility.hotControl = id;
                        GUIUtility.keyboardControl = 0;
                        _scrubStart = value;
                        _scrubX = e.mousePosition.x;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        // Smooth: the value follows the mouse (one step per 12 px, Shift for fine),
                        // snapping to whole steps only while grid snap is on.
                        float steps = (e.mousePosition.x - _scrubX) / 12f * (e.shift ? 0.2f : 1f);
                        value = _snap ? _scrubStart + Mathf.Round(steps) * step
                                      : Mathf.Round((_scrubStart + steps * step) * 100f) / 100f;
                        _fieldText.Remove(key);
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
            }
            var old = Theme.AxisLabel.normal.textColor;
            Theme.AxisLabel.normal.textColor = GUIUtility.hotControl == id || r.Contains(e.mousePosition) ? Color.white : colour;
            GUI.Label(r, axis, Theme.AxisLabel);
            Theme.AxisLabel.normal.textColor = old;
            return NumberBox(key, value, GUILayout.MinWidth(70));
        }

        /// <summary>A text box for a number; the text is kept per field so half-typed numbers aren't overwritten.</summary>
        private float NumberBox(string key, float value, params GUILayoutOption[] options)
        {
            if (!_fieldText.TryGetValue(key, out var text)) text = value.ToString("0.##", CultureInfo.InvariantCulture);
            var newText = GUILayout.TextField(text, options);
            if (newText != text)
            {
                _fieldText[key] = newText;
                if (float.TryParse(newText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) value = parsed;
            }
            return value;
        }

        /// <summary>A labelled number box with − / + buttons.</summary>
        /// <summary>A 0-100% slider in 5% steps.</summary>
        /// <summary>The camera while riding a cog: automatic, or a point you place (it always looks at the cog's centre).</summary>
        private void DrawCogCameraFields(LevelObject obj)
        {
            var d = obj.Data;
            GUILayout.Space(2);
            GUILayout.Label("Riding camera", Theme.Hint);
            int mode = SegmentRow(d.cogCamera ? 1 : 0, new[] { "Automatic", "Custom point" });
            if (mode == 1 && !d.cogCamera)
            {
                // Start from the view the automatic camera would pick right now.
                var cam = Camera.main;
                var world = obj.AutoCogCameraOffset(cam != null ? cam.transform.position : obj.transform.position - Vector3.forward);
                d.cogCameraOffset = Quaternion.Inverse(obj.transform.rotation) * world;
                d.cogCamera = true;
            }
            else if (mode == 0) d.cogCamera = false;
            if (!d.cogCamera) return;

            float dist = d.cogCameraOffset.magnitude;
            if (dist < 0.1f) { d.cogCameraOffset = new Vector3(0f, 4f, -8f); dist = d.cogCameraOffset.magnitude; }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Distance", GUILayout.Width(110));
            float nd = GUILayout.HorizontalSlider(dist, 2f, 40f, GUILayout.ExpandWidth(true));
            nd = Mathf.Round(nd * 4f) / 4f;
            GUILayout.Label($"{nd:0.##} m", GUILayout.Width(64));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(nd - dist) > 0.001f) d.cogCameraOffset = d.cogCameraOffset / dist * nd;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Use my view"))
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    var world = cam.transform.position - obj.transform.position;
                    if (world.magnitude < 2f) world = world.sqrMagnitude < 1e-4f ? -cam.transform.forward * 2f : world.normalized * 2f;
                    if (world.magnitude > 40f) world = world.normalized * 40f;
                    d.cogCameraOffset = Quaternion.Inverse(obj.transform.rotation) * world;
                    Toast("Riding camera set from your view");
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Drag the camera marker (the blue dot joined to the cog) to move it, or \"Use my view\" to put it where you are. It always looks at the cog's centre and turns with the cog's rotation.", Theme.Hint);
        }

        private static readonly Color[] SignTextColours =
        {
            Color.white, new Color(0.1f, 0.1f, 0.1f), new Color(1f, 0.85f, 0.2f), new Color(1f, 0.35f, 0.25f),
            new Color(0.35f, 0.65f, 1f), new Color(0.4f, 0.9f, 0.4f),
        };

        private void DrawSignFields(LevelObjectData d)
        {
            GUILayout.Space(2);
            GUILayout.Label("Text", Theme.Hint);
            d.text = GUILayout.TextArea(d.text ?? "", 500, GUILayout.MinHeight(60));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Text colour", GUILayout.Width(110));
            var old = GUI.backgroundColor;
            foreach (var c in SignTextColours)
            {
                GUI.backgroundColor = c;
                if (GUILayout.Button(GUIContent.none, Theme.Swatch)) d.textColor = c;
                if (SameColour(c, d.textColor))
                {
                    var r = GUILayoutUtility.GetLastRect();
                    GUI.backgroundColor = Color.white;
                    GUI.Box(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), GUIContent.none, Theme.SwatchRing);
                }
            }
            GUI.backgroundColor = old;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            d.textSize = Mathf.Max(0.1f, PercentSlider("Text size", d.textSize));
            d.signBoard = GUILayout.Toggle(d.signBoard, "Show the board (off: the text floats, and you pass through it)");
            d.signBothSides = GUILayout.Toggle(d.signBothSides, "Text on the back too");
            d.signMono = GUILayout.Toggle(d.signMono, "Monospace (for ASCII art: equal-width letters, lines kept as typed)");
            GUILayout.Label("The text fits itself to the board: stretch Size X / Y for more room. Turn the sign with Rotation Y (or the turn keys while placing).", Theme.Hint);
        }

        private static float PercentSlider(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            value = GUILayout.HorizontalSlider(Mathf.Clamp01(value), 0f, 1f, GUILayout.ExpandWidth(true));
            value = Mathf.Round(value * 20f) / 20f;
            GUILayout.Label($"{value * 100f:0}%", GUILayout.Width(64));
            GUILayout.EndHorizontal();
            return value;
        }

        private float FloatField(string label, string key, float value, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            value = NumberBox(key, value, GUILayout.MinWidth(80));
            if (GUILayout.Button("−", Theme.Small, GUILayout.Width(34))) { value -= step; _fieldText.Remove(key); }
            if (GUILayout.Button("+", Theme.Small, GUILayout.Width(34))) { value += step; _fieldText.Remove(key); }
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
