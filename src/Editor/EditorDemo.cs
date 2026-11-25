using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>
    /// A scripted showcase for recording: builds a course that uses every object type,
    /// piece by piece with a moving camera and captions, then flies along the finished course.
    /// Started with the debug command "demo" while editing a custom level.
    /// </summary>
    public partial class LevelEditorController
    {
        public bool DemoRunning { get; private set; }
        /// <summary>Lets launchers and cogs move while the demo shows the editor.</summary>
        public bool DemoAnimate { get; private set; }

        private bool _demoOverlays, _demoFlying, _demoHotbar;
        private Vector3 _demoPlayerPos;
        private string _demoCaption, _demoTitle, _demoSubtitle;
        private float _demoTitleAlpha;
        private Vector3 _demoFocus, _demoFocusSmooth, _demoCamVel, _demoFocusVel;
        private float _demoDist = 25f, _demoYaw = 200f, _demoPitch = 28f;
        private GUIStyle _demoCaptionStyle, _demoTitleStyle, _demoSubStyle;
        private Texture2D _demoShade;

        private class DemoStep
        {
            public LevelObjectData Data;
            public string Caption;
            public float Hold = 0.45f;
            public bool ShowPanel;
        }

        public string DebugDemo()
        {
            if (!DebugSessionReady || !IsEditing) return "start a level in the editor first";
            if (DemoRunning) return "already running";
            StartCoroutine(RunDemo());
            return "demo started";
        }

        // ---------------- the course ----------------

        private static readonly Color Grey = new Color(0.8f, 0.8f, 0.82f), Dark = new Color(0.32f, 0.33f, 0.37f),
            Blue = new Color(0.35f, 0.62f, 0.95f), Purple = new Color(0.62f, 0.42f, 0.92f), Green = new Color(0.35f, 0.78f, 0.45f),
            Teal = new Color(0.3f, 0.75f, 0.75f), Sand = new Color(0.9f, 0.8f, 0.6f);

        private static LevelObjectData Obj(ObjectKind kind, Vector3 pos, Vector3? scale = null, Vector3? rot = null, Color? color = null, float? force = null)
        {
            var d = LevelObjectData.CreateDefault(kind);
            d.position = pos;
            if (scale.HasValue) d.scale = scale.Value;
            if (rot.HasValue) d.rotation = rot.Value;
            if (color.HasValue) d.color = color.Value;
            if (force.HasValue) d.force = force.Value;
            return d;
        }

        /// <summary>A ramp through world points; w is the bank in degrees.</summary>
        private static LevelObjectData Ramp(RampProfile profile, float width, float wall, Color color, params Vector4[] pts)
        {
            var d = LevelObjectData.CreateDefault(ObjectKind.Ramp);
            d.Profile = profile;
            d.wallHeight = wall;
            d.color = color;
            d.position = pts[0];
            d.points = pts.Select(p => new RampPoint { position = (Vector3)p - (Vector3)pts[0], width = width, bank = p.w }).ToList();
            return d;
        }

        private static List<DemoStep> DemoCourse() => new List<DemoStep>
        {
            new DemoStep { Caption = "Start with a tower", Data = Obj(ObjectKind.Cylinder, new Vector3(0, 9.5f, 0), new Vector3(3, 19, 3), color: Dark) },
            new DemoStep { Caption = "Platforms: place, then size them with the wheel", Data = Obj(ObjectKind.Platform, new Vector3(0, 19.5f, 0), new Vector3(8, 1, 8), color: Grey) },
            new DemoStep { Caption = "Set the Start", Data = Obj(ObjectKind.Start, new Vector3(0, 20.075f, -2)) },
            new DemoStep { Caption = "Draw curved ramps point by point, with banked turns", Hold = 0.8f, Data = Ramp(RampProfile.Flat, 5f, 2f, Blue,
                new Vector4(0, 20.02f, 4, 0), new Vector4(0, 18, 10, 0), new Vector4(-3, 15.5f, 17, 12), new Vector4(-9, 13, 22, 22),
                new Vector4(-15, 11.5f, 24, 10), new Vector4(-20, 11.02f, 24, 0)) },
            new DemoStep { Caption = "Platforms", Data = Obj(ObjectKind.Platform, new Vector3(-24, 10.5f, 24), new Vector3(8, 1, 8), color: Teal) },
            new DemoStep { Caption = "Checkpoints", Data = Obj(ObjectKind.Checkpoint, new Vector3(-21.5f, 11.075f, 24)) },
            new DemoStep { Caption = "Bumpers", Hold = 0.2f, Data = Obj(ObjectKind.Bumper, new Vector3(-24.5f, 11.75f, 21.3f)) },
            new DemoStep { Caption = "Bumpers", Data = Obj(ObjectKind.Bumper, new Vector3(-24.5f, 11.75f, 26.7f)) },
            new DemoStep { Caption = "Jump pads, tilted and tuned", Data = Obj(ObjectKind.JumpPad, new Vector3(-27, 11.1f, 24), rot: new Vector3(0, 0, 30), force: 14) },
            new DemoStep { Caption = "Kill zones", Data = Obj(ObjectKind.KillZone, new Vector3(-35, 1, 24), new Vector3(30, 1, 30)) },
            new DemoStep { Caption = "Platforms", Data = Obj(ObjectKind.Platform, new Vector3(-44, 10.5f, 24), new Vector3(10, 1, 10), color: Grey) },
            new DemoStep { Caption = "Wedges", Data = Obj(ObjectKind.Wedge, new Vector3(-46.5f, 11.5f, 20.5f), new Vector3(3, 1, 3), color: Sand) },
            new DemoStep { Caption = "Half-pipes", Hold = 0.8f, Data = Ramp(RampProfile.HalfPipe, 6f, 2.5f, Purple,
                new Vector4(-44, 11.02f, 29, 0), new Vector4(-44, 9.5f, 37, 0), new Vector4(-40, 7.5f, 46, 0), new Vector4(-33, 6, 52, 0), new Vector4(-28, 5.5f, 54, 0)) },
            new DemoStep { Caption = "Cog launchers: grab on and ride", Hold = 0.8f, Data = Obj(ObjectKind.CogLauncher, new Vector3(-20, 5, 54), rot: new Vector3(90, 0, 0)) },
            new DemoStep { Caption = "Platforms", Data = Obj(ObjectKind.Platform, new Vector3(-8, 4.5f, 54), new Vector3(8, 1, 8), color: Teal) },
            new DemoStep { Caption = "Checkpoints", Data = Obj(ObjectKind.Checkpoint, new Vector3(-10.5f, 5.075f, 51.3f)) },
            new DemoStep { Caption = "Slingshot launchers: grab and get flung", ShowPanel = true, Data = Obj(ObjectKind.Launcher, new Vector3(-6.5f, 6, 54), rot: new Vector3(-45, 90, 0), force: 12) },
            new DemoStep { Caption = "Platforms", Data = Obj(ObjectKind.Platform, new Vector3(10, 6.5f, 54), new Vector3(12, 1, 10), color: Grey) },
            new DemoStep { Caption = "Walls", Data = Obj(ObjectKind.Platform, new Vector3(12, 9.5f, 59.4f), new Vector3(10, 6, 0.4f), color: Dark) },
            new DemoStep { Caption = "Wall launchers", Hold = 0.7f, Data = Obj(ObjectKind.WallLauncher, new Vector3(12, 8.6f, 59)) },
            new DemoStep { Caption = "Bumpers", Data = Obj(ObjectKind.Bumper, new Vector3(6, 7.75f, 51.5f)) },
            new DemoStep { Caption = "Platforms", Data = Obj(ObjectKind.Platform, new Vector3(32, 12.5f, 56), new Vector3(10, 1, 10), color: Teal) },
            new DemoStep { Caption = "Spheres and cylinders", Hold = 0.2f, Data = Obj(ObjectKind.Sphere, new Vector3(29.5f, 14, 53.5f), new Vector3(2, 2, 2), color: Sand) },
            new DemoStep { Caption = "Spheres and cylinders", Data = Obj(ObjectKind.Cylinder, new Vector3(38, 5, 65), new Vector3(5, 16, 5), color: Dark) },
            new DemoStep { Caption = "Spiral gutters", Hold = 0.9f, Data = Ramp(RampProfile.Gutter, 4f, 1.4f, Green,
                new Vector4(37, 13.02f, 56, 0), new Vector4(43, 12.2f, 58.5f, 8), new Vector4(46.5f, 11, 64, 12), new Vector4(45, 9.8f, 70.5f, 12),
                new Vector4(39.5f, 8.6f, 73.5f, 12), new Vector4(33, 7.4f, 71, 12), new Vector4(30.5f, 6.2f, 65, 12), new Vector4(32, 5, 59, 8), new Vector4(34, 4.2f, 55.5f, 0)) },
            new DemoStep { Caption = "Platforms", Data = Obj(ObjectKind.Platform, new Vector3(34, 3.5f, 51), new Vector3(8, 1, 8), color: Grey) },
            new DemoStep { Caption = "And a Finish line", Hold = 0.9f, Data = Obj(ObjectKind.Finish, new Vector3(34, 4.075f, 50)) },
        };

        /// <summary>The route through the course, for the flythrough camera.</summary>
        private static readonly Vector3[] DemoRoute =
        {
            new Vector3(0, 22, -6), new Vector3(0, 21.5f, 3), new Vector3(0, 19.5f, 10), new Vector3(-3, 17, 17), new Vector3(-9, 14.5f, 22),
            new Vector3(-15, 13, 24), new Vector3(-21, 12.8f, 24), new Vector3(-27, 13, 24), new Vector3(-35, 17, 24), new Vector3(-44, 13, 24),
            new Vector3(-44, 12.5f, 30), new Vector3(-44, 11.2f, 37), new Vector3(-40, 9.2f, 46), new Vector3(-33, 8, 52), new Vector3(-27, 8, 54),
            new Vector3(-20, 12.5f, 54), new Vector3(-13, 9.5f, 54), new Vector3(-8, 7.5f, 54), new Vector3(-4, 10, 54), new Vector3(3, 13, 54),
            new Vector3(10, 9.5f, 54), new Vector3(13, 11, 56.5f), new Vector3(20, 16, 57), new Vector3(27, 16, 56), new Vector3(32, 15, 56),
            new Vector3(37, 15, 56), new Vector3(43, 14.2f, 58.5f), new Vector3(46.5f, 13, 64), new Vector3(45, 11.8f, 70.5f), new Vector3(39.5f, 10.6f, 73.5f),
            new Vector3(33, 9.4f, 71), new Vector3(30.5f, 8.2f, 65), new Vector3(32, 7, 59), new Vector3(34, 6.3f, 54), new Vector3(34, 6, 49),
        };

        // ---------------- the director ----------------

        private IEnumerator RunDemo()
        {
            DemoRunning = true;
            DemoAnimate = false;
            _demoOverlays = true;
            _demoFlying = false;
            _demoHotbar = false;
            SetCursorMode(false);
            DestroyGhost();
            _draft.Clear();
            _rampTool = false;
            _demoPlayerPos = new Vector3(0, 80, -60); // out of sight until the Start exists
            GameBridge.SetCameraBrainEnabled(false);
            HideGameHud(true);

            ApplyLevel(new LevelData { name = "Demo Course", environment = CustomSession.Environment });
            ClearUndo();
            _selected = null;

            var cam = Camera.main;
            _demoFocus = _demoFocusSmooth = new Vector3(0, 12, 30);
            _demoDist = 70f;
            _demoYaw = 215f;
            _demoPitch = 18f;
            cam.transform.position = DemoCamGoal();
            cam.transform.LookAt(_demoFocusSmooth);

            // Title card.
            _demoTitle = "GTW Level Editor";
            _demoSubtitle = "Building a full course from scratch";
            yield return FadeTitle(1f, 0.6f);
            yield return Wait(2.2f);
            yield return FadeTitle(0f, 0.6f);
            _demoTitle = null;
            _demoHotbar = true;

            foreach (var step in DemoCourse())
            {
                var d = step.Data;
                _kind = d.Kind;
                _demoCaption = step.Caption;
                FrameForDemo(d);
                yield return Wait(0.75f);

                LevelObject obj;
                if (d.Kind == ObjectKind.Ramp)
                {
                    _rampTool = true;
                    obj = null;
                    yield return DrawRampForDemo(d, o => obj = o);
                    _rampTool = false;
                }
                else
                {
                    obj = LevelObject.Create(d, _root);
                    obj.SetEditorVisible(true);
                    _objects.Add(obj);
                    yield return Pop(obj);
                }
                if (d.Kind == ObjectKind.Start) _demoPlayerPos = d.position + Vector3.up * 0.6f;

                if (step.ShowPanel)
                {
                    // Show the panel and the move handles on the new object.
                    _demoCaption = "Fine-tune anything in the panel, or drag the handles";
                    Select(obj);
                    _gizmo = GizmoMode.Move;
                    _window.position = new Vector2(20, 110);
                    SetCursorMode(true);
                    yield return Wait(3f);
                    SetCursorMode(false);
                    GameBridge.SetCameraBrainEnabled(false);
                    _selected = null;
                    Cursor.visible = false;
                }
                yield return Wait(step.Hold);
            }

            _level.name = "Demo Course";
            SaveLevel();

            // Overview of the finished course.
            _demoCaption = $"{_objects.Count} pieces. Saved as one file you can share.";
            _demoFocus = new Vector3(0, 10, 40);
            _demoDist = 85f;
            _demoPitch = 32f;
            yield return Wait(3.5f);

            // Flythrough with everything moving.
            _demoHotbar = false;
            _demoOverlays = false;
            DemoAnimate = true;
            _demoCaption = "Press F8 and play it";
            yield return FlyRoute(cam, 26f);

            _demoCaption = null;
            _demoTitle = "GTW Level Editor";
            _demoSubtitle = "Curved ramps · launchers · cogs · jump pads · undo · save & share";
            yield return FadeTitle(1f, 0.8f);
            yield return Wait(3f);

            // Hand the editor back.
            DemoAnimate = false;
            _demoTitle = null;
            _demoTitleAlpha = 0f;
            DemoRunning = false;
            GameBridge.SetCameraBrainEnabled(true);
            HideGameHud(false);
            MovePlayerToStart();
            _kind = ObjectKind.Platform;
        }

        private readonly List<Canvas> _hiddenCanvases = new List<Canvas>();

        /// <summary>Hides the game's own HUD (speedometer, grab prompt) for clean shots, and restores it.</summary>
        private void HideGameHud(bool hide)
        {
            if (hide)
            {
                _hiddenCanvases.Clear();
                foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (!c.enabled || !c.isRootCanvas) continue;
                    c.enabled = false;
                    _hiddenCanvases.Add(c);
                }
            }
            else
            {
                foreach (var c in _hiddenCanvases) if (c != null) c.enabled = true;
                _hiddenCanvases.Clear();
            }
        }

        private IEnumerator Wait(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        private IEnumerator FadeTitle(float to, float seconds)
        {
            float from = _demoTitleAlpha;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                _demoTitleAlpha = Mathf.Lerp(from, to, t / seconds);
                yield return null;
            }
            _demoTitleAlpha = to;
        }

        /// <summary>Points the orbiting camera at where the next piece goes.</summary>
        private void FrameForDemo(LevelObjectData d)
        {
            Vector3 center;
            float size;
            if (d.Kind == ObjectKind.Ramp)
            {
                var world = d.points.Select(p => d.position + p.position).ToList();
                center = world.Aggregate(Vector3.zero, (a, b) => a + b) / world.Count;
                size = world.Max(p => Vector3.Distance(p, center)) * 2f;
            }
            else
            {
                center = d.position;
                size = d.scale.magnitude;
                if (d.Kind == ObjectKind.Launcher || d.Kind == ObjectKind.WallLauncher) size = 8f;
            }
            _demoFocus = center;
            _demoDist = Mathf.Clamp(size * 1.3f, 11f, 34f);
            _demoPitch = 26f;
        }

        private Vector3 DemoCamGoal() =>
            _demoFocusSmooth + Quaternion.Euler(_demoPitch, _demoYaw, 0f) * new Vector3(0f, 0f, -_demoDist);

        /// <summary>The build camera: drifts around the current piece, easing between pieces.</summary>
        private void LateUpdate()
        {
            UpdateDockedCamera();
            UpdateGameTimer();
            if (!DemoRunning || _demoFlying) return;
            var cam = Camera.main;
            if (cam == null) return;
            float dt = Time.unscaledDeltaTime;
            _demoYaw += 7f * dt;
            _demoFocusSmooth = Vector3.SmoothDamp(_demoFocusSmooth, _demoFocus, ref _demoFocusVel, 0.7f, Mathf.Infinity, dt);
            var goal = DemoCamGoal();
            cam.transform.position = Vector3.SmoothDamp(cam.transform.position, goal, ref _demoCamVel, 0.7f, Mathf.Infinity, dt);
            cam.transform.rotation = Quaternion.LookRotation(_demoFocusSmooth - cam.transform.position);
        }

        /// <summary>The piece grows in with a little overshoot.</summary>
        private IEnumerator Pop(LevelObject obj)
        {
            var full = obj.transform.localScale;
            const float duration = 0.4f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float x = t / duration - 1f;
                float s = 1f + 2.70158f * x * x * x + 1.70158f * x * x; // ease-out-back
                obj.transform.localScale = full * Mathf.Max(0.01f, s);
                yield return null;
            }
            obj.Refresh();
        }

        /// <summary>Shows the ramp tool: points drop in one by one and the track follows them.</summary>
        private IEnumerator DrawRampForDemo(LevelObjectData d, System.Action<LevelObject> created)
        {
            var all = d.points.Select(p => p.Clone()).ToList();
            var origin = d.position;
            _draft.Clear();
            LevelObject obj = null;
            for (int i = 0; i < all.Count; i++)
            {
                var world = origin + all[i].position;
                _draft.Add(new RampPoint { position = world, width = all[i].width, bank = all[i].bank });
                _draftCursor = _draftGround = world;
                if (i >= 1)
                {
                    d.points = all.Take(i + 1).Select(p => p.Clone()).ToList();
                    if (obj == null)
                    {
                        obj = LevelObject.Create(d, _root);
                        obj.SetEditorVisible(true);
                        _objects.Add(obj);
                    }
                    else obj.Refresh();
                }
                yield return Wait(0.32f);
            }
            _draft.Clear();
            created(obj);
        }

        /// <summary>Flies the camera along the course at a steady speed, looking ahead.</summary>
        private IEnumerator FlyRoute(Camera cam, float seconds)
        {
            _demoFlying = true;
            var path = new LevelObjectData { points = DemoRoute.Select(p => new RampPoint { position = p }).ToList() };
            var samples = RampMesh.SampleCurve(path).Select(s => s.Pos).ToList();
            var startPos = cam.transform.position;
            var startRot = cam.transform.rotation;
            // Ease from the overview onto the route first.
            for (float t = 0f; t < 1.5f; t += Time.unscaledDeltaTime)
            {
                float e = Mathf.SmoothStep(0f, 1f, t / 1.5f);
                cam.transform.position = Vector3.Lerp(startPos, samples[0] + Vector3.up * 3f, e);
                cam.transform.rotation = Quaternion.Slerp(startRot, Quaternion.LookRotation(samples[Mathf.Min(20, samples.Count - 1)] - samples[0]), e);
                yield return null;
            }
            var look = cam.transform.rotation;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float f = t / seconds * (samples.Count - 1);
                int i = Mathf.Min((int)f, samples.Count - 2);
                var pos = Vector3.Lerp(samples[i], samples[i + 1], f - i) + Vector3.up * 3f;
                var ahead = samples[Mathf.Min(i + 32, samples.Count - 1)];
                cam.transform.position = pos;
                look = Quaternion.Slerp(look, Quaternion.LookRotation(ahead - pos + Vector3.down * 0.8f), 3f * Time.unscaledDeltaTime);
                cam.transform.rotation = look;
                yield return null;
            }
            _demoFlying = false;
            _demoFocus = _demoFocusSmooth = new Vector3(34, 5, 50);
            _demoDist = 30f;
        }

        // ---------------- on-screen text ----------------

        private void DrawDemoGui()
        {
            if (_demoCaptionStyle == null)
            {
                _demoCaptionStyle = new GUIStyle(Theme.Caption) { fontSize = 32, richText = true };
                _demoTitleStyle = new GUIStyle(Theme.Big) { fontSize = 84 };
                _demoSubStyle = new GUIStyle(Theme.Hint) { fontSize = 30, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                _demoSubStyle.normal.textColor = new Color(0.9f, 0.92f, 0.95f);
                _demoShade = SolidTexture(new Color(0f, 0f, 0f, 1f));
            }
            if (_demoHotbar) DrawHotbar();
            if (_cursorMode) _window = GUILayout.Window(0x4C45, _window, DrawWindow, "Level Editor");

            if (!string.IsNullOrEmpty(_demoCaption))
            {
                var size = _demoCaptionStyle.CalcSize(new GUIContent(_demoCaption));
                var r = new Rect((VW - size.x) / 2f - 30f, VH - 130f, size.x + 60f, 70f);
                GUI.Box(r, GUIContent.none, Theme.Pill);
                GUI.Label(r, _demoCaption, _demoCaptionStyle);
            }

            if (_demoTitle != null && _demoTitleAlpha > 0.01f)
            {
                var old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.6f * _demoTitleAlpha);
                GUI.DrawTexture(new Rect(0, 0, VW, VH), _demoShade);
                GUI.color = new Color(1f, 1f, 1f, _demoTitleAlpha);
                GUI.Label(new Rect(0, VH / 2f - 110f, VW, 120f), _demoTitle, _demoTitleStyle);
                GUI.Label(new Rect(0, VH / 2f + 10f, VW, 60f), _demoSubtitle, _demoSubStyle);
                GUI.color = old;
            }
        }
    }
}
