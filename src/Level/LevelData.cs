using System;
using System.Collections.Generic;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>Every kind of object the editor can place. Saved by name, so only append new values.</summary>
    public enum ObjectKind
    {
        Platform,
        Wedge,
        Cylinder,
        Sphere,
        JumpPad,
        Bumper,
        Checkpoint,
        Start,
        Finish,
        KillZone,
        Ramp,
        /// <summary>The game's slingshot: a sticky carriage that shoots along its track when grabbed.</summary>
        Launcher,
        /// <summary>The same slingshot mounted on a wall.</summary>
        WallLauncher,
        /// <summary>A sticky spinning cog wheel: ride it and let go to fly off.</summary>
        CogLauncher,
        /// <summary>The game's swinging pendulum: a sticky bob on a rod, hinged at the object's position.</summary>
        Pendulum,
        /// <summary>A board with text on it (or floating text), for directions and messages.</summary>
        Sign,
    }

    /// <summary>How a piece moves in play mode.</summary>
    public enum MotionType
    {
        None,
        /// <summary>Back and forth along an offset.</summary>
        Move,
        /// <summary>Round and round a pivot point.</summary>
        Rotate,
        /// <summary>A pendulum swing around a pivot point.</summary>
        Swing,
    }

    /// <summary>The cross-section of a ramp.</summary>
    public enum RampProfile
    {
        Flat,
        Gutter,
        HalfPipe,
        QuarterPipe,
        Walls,
    }

    /// <summary>A control point of a ramp's curve, in the ramp object's local space.</summary>
    [Serializable]
    public class RampPoint
    {
        public Vector3 position;
        public float width = 4f;
        /// <summary>Roll around the direction of travel in degrees; positive raises the right edge.</summary>
        public float bank;

        public RampPoint Clone() => (RampPoint)MemberwiseClone();
    }

    /// <summary>How a solid surface behaves when the player rolls on it or grabs it.</summary>
    public enum SurfaceType
    {
        Normal,
        Slippery,
        Sticky,
        /// <summary>Bounces you off at the object's launch speed, like a jump pad.</summary>
        JumpPad,
    }

    /// <summary>A saved level. Written by LevelJson (JsonUtility per object), so fields must stay public.</summary>
    [Serializable]
    public class LevelData
    {
        public int version = 1;
        public string name = "Untitled";
        /// <summary>Which Doinkler world (0-5) provides the sky and lighting.</summary>
        public int environment;
        public bool useKillHeight = true;
        public float killHeight = -30f;
        public List<LevelObjectData> objects = new List<LevelObjectData>();

        /// <summary>A fresh level: a floor with the Start pad in the middle.</summary>
        public static LevelData CreateNew(int environment)
        {
            var level = new LevelData { environment = environment, killHeight = -30f };
            var floor = LevelObjectData.CreateDefault(ObjectKind.Platform);
            floor.position = new Vector3(0f, -0.5f, 0f);
            floor.scale = new Vector3(20f, 1f, 20f);
            level.objects.Add(floor);
            var start = LevelObjectData.CreateDefault(ObjectKind.Start);
            start.position = new Vector3(0f, start.scale.y * 0.5f, 0f);
            level.objects.Add(start);
            return level;
        }
    }

    [Serializable]
    public class LevelObjectData
    {
        public string kind = nameof(ObjectKind.Platform);
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale = Vector3.one;
        public Color color = Color.white;
        public string surface = nameof(SurfaceType.Normal);
        /// <summary>Launch speed in m/s for jump pads and bumpers.</summary>
        public float force;

        // ---- launchers only ----
        /// <summary>How far (m) a launcher's carriage shoots before throwing you.</summary>
        public float travel = 4.5f;
        /// <summary>A cog launcher's spin in degrees per second (negative spins the other way).</summary>
        public float spin = 180f;
        /// <summary>A cog launcher: use <see cref="cogCameraOffset"/> for the riding camera instead of the automatic view.</summary>
        public bool cogCamera;
        /// <summary>Where the riding camera sits, from the cog's centre, in the cog's own (rotated, unscaled) frame. It always looks at the centre.</summary>
        public Vector3 cogCameraOffset = new Vector3(0f, 4f, -8f);

        // ---- signs only ----
        /// <summary>The sign's text; new lines are kept.</summary>
        public string text = "Sign";
        public Color textColor = Color.white;
        /// <summary>0.1-1: the largest the text may grow, as a share of the size that fills the board.</summary>
        public float textSize = 1f;
        /// <summary>Show the board behind the text; without it the text floats and nothing is solid.</summary>
        public bool signBoard = true;
        /// <summary>Text on the back as well as the front.</summary>
        public bool signBothSides;
        /// <summary>Monospaced text for ASCII art: every character the same width, lines kept left-aligned and never wrapped.</summary>
        public bool signMono;

        // ---- motion (moving platforms, rotating groups, swinging pieces) ----
        public string motion = nameof(MotionType.None);
        /// <summary>Move: how far it travels (world metres).</summary>
        public Vector3 moveOffset = new Vector3(0f, 0f, 8f);
        public float moveTime = 3f;
        public float movePause = 1f;
        /// <summary>Rotate / Swing: the world point it turns around.</summary>
        public Vector3 pivot;
        /// <summary>Rotate / Swing: the world axis it turns around (0 = X, 1 = Y, 2 = Z).</summary>
        public int axis = 1;
        /// <summary>Rotate: degrees per second (negative turns the other way).</summary>
        public float rotateSpeed = 30f;
        public float swingAngle = 25f;
        public float swingPeriod = 3f;
        /// <summary>0-1: shifts the timing so neighbouring pieces move out of step.</summary>
        public float phase;
        /// <summary>Pendulum: rod length from the hinge to the bob's centre.</summary>
        public float length = 3.2f;

        // ---- cylinders only ----
        /// <summary>An open tube instead of a solid cylinder.</summary>
        public bool hollow;
        /// <summary>A hollow cylinder's wall thickness in metres.</summary>
        public float wallThickness = 0.4f;
        /// <summary>A hollow cylinder that cuts a hole through the platforms it passes through.</summary>
        public bool cutsPlatforms;

        /// <summary>Jump pad surface: how far above the top (m) the launch zone reaches, so you bounce before touching it. 0 = on contact only.</summary>
        public float padHeight = 0.6f;

        // ---- wedges only ----
        /// <summary>-1 to 1: how the slope bends. Negative curves it in (concave, like a quarter pipe), positive curves it out (a rounded hill), 0 is straight.</summary>
        public float slopeCurve;

        // ---- slippery surfaces: how much of the main game's grip is kept (0-1) ----
        public float iceFriction;
        public float iceSteering = 1f;
        public float iceDrag = 1f;

        // ---- ramps only ----
        public List<RampPoint> points = new List<RampPoint>();
        public string profile = nameof(RampProfile.Flat);
        public float thickness = 0.5f;
        /// <summary>Wall height of half/quarter pipes and walls, depth of a gutter.</summary>
        public float wallHeight = 2f;
        /// <summary>Let the track twist freely with the curve (needed for loops) instead of keeping its "up" pointing at the sky.</summary>
        public bool followTwist;

        public ObjectKind Kind
        {
            get => Enum.TryParse(kind, out ObjectKind k) ? k : ObjectKind.Platform;
            set => kind = value.ToString();
        }

        public SurfaceType Surface
        {
            get => Enum.TryParse(surface, out SurfaceType s) ? s : SurfaceType.Normal;
            set => surface = value.ToString();
        }

        public MotionType Motion
        {
            get => Enum.TryParse(motion, out MotionType m) ? m : MotionType.None;
            set => motion = value.ToString();
        }

        /// <summary>Pieces that can be given a motion (launchers move on their own; markers stay put).</summary>
        public bool CanMove => Kind <= ObjectKind.Sphere || Kind == ObjectKind.Ramp || Kind == ObjectKind.JumpPad || Kind == ObjectKind.Bumper || Kind == ObjectKind.Pendulum;
        public bool HasMotion => CanMove && Motion != MotionType.None;
        public Vector3 AxisVector => axis == 0 ? Vector3.right : axis == 2 ? Vector3.forward : Vector3.up;

        public RampProfile Profile
        {
            get => Enum.TryParse(profile, out RampProfile p) ? p : RampProfile.Flat;
            set => profile = value.ToString();
        }

        public LevelObjectData Clone()
        {
            var c = (LevelObjectData)MemberwiseClone();
            c.points = points == null ? new List<RampPoint>() : points.ConvertAll(p => p.Clone());
            return c;
        }

        public static LevelObjectData CreateDefault(ObjectKind kind)
        {
            var d = new LevelObjectData { Kind = kind };
            switch (kind)
            {
                case ObjectKind.Platform: d.scale = new Vector3(4f, 0.5f, 4f); d.color = new Color(0.78f, 0.78f, 0.8f); break;
                case ObjectKind.Wedge: d.scale = new Vector3(3f, 2f, 4f); d.color = new Color(0.7f, 0.74f, 0.82f); break;
                case ObjectKind.Cylinder: d.scale = new Vector3(2f, 1f, 2f); d.color = new Color(0.75f, 0.7f, 0.62f); break;
                case ObjectKind.Sphere: d.scale = new Vector3(2f, 2f, 2f); d.color = new Color(0.65f, 0.75f, 0.68f); break;
                case ObjectKind.JumpPad: d.scale = new Vector3(2f, 0.2f, 2f); d.color = new Color(1f, 0.55f, 0.1f); d.force = 20f; break;
                case ObjectKind.Bumper: d.scale = new Vector3(1.5f, 1.5f, 1.5f); d.color = new Color(0.95f, 0.25f, 0.6f); d.force = 15f; break;
                case ObjectKind.Checkpoint: d.scale = new Vector3(2.5f, 0.15f, 2.5f); d.color = new Color(0.2f, 0.5f, 1f); break;
                case ObjectKind.Start: d.scale = new Vector3(3f, 0.15f, 3f); d.color = new Color(0.25f, 0.85f, 0.35f); break;
                case ObjectKind.Finish: d.scale = new Vector3(3f, 0.15f, 3f); d.color = new Color(1f, 0.82f, 0.15f); break;
                case ObjectKind.KillZone: d.scale = new Vector3(10f, 1f, 10f); d.color = new Color(0.8f, 0.1f, 0.1f); break;
                case ObjectKind.Ramp: d.color = new Color(0.62f, 0.72f, 0.86f); break;
                // Sizes, travel and speeds copied from the game's own Slingshot_45deg,
                // Slingshot_Side_45deg and GiantWheel objects.
                case ObjectKind.Launcher:
                    d.scale = new Vector3(2f, 0.2f, 2f); d.color = new Color(1f, 0.5f, 0.12f);
                    d.rotation = new Vector3(-45f, 0f, 0f); d.force = 10f; d.travel = 4.5f;
                    break;
                case ObjectKind.WallLauncher:
                    d.scale = new Vector3(2.8f, 0.2f, 2.8f); d.color = new Color(1f, 0.5f, 0.12f);
                    d.rotation = Quaternion.LookRotation(new Vector3(1f, 1f, 0f).normalized, Vector3.back).eulerAngles;
                    d.force = 15f; d.travel = 4f;
                    break;
                case ObjectKind.Pendulum:
                    // Copied from the game's Pendulum_Small (Doinkler 4-5): ±25° every 3 s, swinging across its facing.
                    d.color = new Color(1f, 0.5f, 0.12f);
                    d.Motion = MotionType.Swing; d.axis = 0; d.swingAngle = 25f; d.swingPeriod = 3f; d.length = 3.2f;
                    break;
                case ObjectKind.Sign:
                    d.scale = new Vector3(4f, 2f, 0.2f); d.color = new Color(0.45f, 0.3f, 0.18f);
                    break;
                case ObjectKind.CogLauncher:
                    d.scale = new Vector3(10f, 3f, 10f); d.color = new Color(0.85f, 0.62f, 0.25f);
                    d.rotation = new Vector3(0f, 0f, 90f); d.spin = 180f;
                    break;
            }
            return d;
        }

        /// <summary>Solid shapes that the player rolls on; only these use <see cref="surface"/>.</summary>
        public bool IsSolidShape => Kind <= ObjectKind.Sphere || Kind == ObjectKind.Ramp;
        public bool HasForce => Kind == ObjectKind.JumpPad || Kind == ObjectKind.Bumper || Kind == ObjectKind.Launcher || Kind == ObjectKind.WallLauncher ||
                                (IsSolidShape && Surface == SurfaceType.JumpPad);
        public bool IsHollow => Kind == ObjectKind.Cylinder && hollow;
        /// <summary>Shapes with a flat top, where a Jump pad surface can have a launch zone above it.</summary>
        public bool HasJumpZone => Surface == SurfaceType.JumpPad && (Kind == ObjectKind.Platform || Kind == ObjectKind.Cylinder || Kind == ObjectKind.Sphere) && padHeight > 0.001f;
        public bool IsLauncher => Kind == ObjectKind.Launcher || Kind == ObjectKind.WallLauncher || Kind == ObjectKind.CogLauncher;
    }
}
