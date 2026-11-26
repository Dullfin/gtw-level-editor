using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace GTWLevelEditor
{
    /// <summary>Everything in the editor that a key does (besides choosing objects with the number keys).</summary>
    public enum EditorAction
    {
        // anywhere
        Snap, HidePlayer,
        // flying
        FlyUp, FlyDown, Fast,
        // placing
        TurnLeft, TurnRight, TiltBack, TiltForward, ResetTurn, LaunchSlower, LaunchFaster, DeleteAimed,
        // drawing a ramp
        BankLeft, BankRight, ResetRampPoint, RemoveRampPoint, FinishRamp,
        // mouse mode
        HandlesMove, HandlesRotate, HandlesScale, DeleteSelected,
        CameraForward, CameraBack, CameraLeft, CameraRight, CameraDown, CameraUp,
        // with Ctrl held
        Undo, Redo, Copy, Paste, Duplicate, SelectAll,
    }

    /// <summary>
    /// The editor's key bindings: up to two keys per action, saved in the config's "Keys" section
    /// (e.g. "Delete, Backspace") and changed from the panel's Controls tab.
    /// </summary>
    internal static class EditorKeys
    {
        public sealed class Info
        {
            public EditorAction Action;
            public string Group;
            public string Label;
            public KeyCode[] Defaults;
            public ConfigEntry<string> Entry;
            public KeyCode Primary = KeyCode.None, Secondary = KeyCode.None;
        }

        public const string Anywhere = "Anywhere", Flying = "Flying", Placing = "Placing (fly mode)",
            Ramp = "Drawing a ramp", Mouse = "Mouse mode", WithCtrl = "With Ctrl held";

        /// <summary>Groups whose keys work at the same time, so a key must not do two things in them.</summary>
        public static readonly string[][] ActiveTogether =
        {
            new[] { Anywhere, Flying, Placing, WithCtrl },
            new[] { Anywhere, Flying, Ramp, WithCtrl },
            new[] { Anywhere, Mouse, WithCtrl },
        };

        public static readonly List<Info> All = new List<Info>();
        private static readonly Dictionary<EditorAction, Info> ByAction = new Dictionary<EditorAction, Info>();

        private static void Add(EditorAction a, string group, string label, params KeyCode[] defaults)
        {
            var info = new Info { Action = a, Group = group, Label = label, Defaults = defaults };
            All.Add(info);
            ByAction[a] = info;
        }

        static EditorKeys()
        {
            Add(EditorAction.Snap, Anywhere, "Snapping on / off", KeyCode.G);
            Add(EditorAction.HidePlayer, Anywhere, "Hide my character", KeyCode.H);

            Add(EditorAction.FlyUp, Flying, "Fly up", KeyCode.Space);
            Add(EditorAction.FlyDown, Flying, "Fly down", KeyCode.LeftAlt);
            Add(EditorAction.Fast, Flying, "Fast (and with the wheel: height / width)", KeyCode.LeftShift);

            Add(EditorAction.TurnLeft, Placing, "Turn left", KeyCode.Q);
            Add(EditorAction.TurnRight, Placing, "Turn right", KeyCode.E);
            Add(EditorAction.TiltBack, Placing, "Tilt back", KeyCode.R);
            Add(EditorAction.TiltForward, Placing, "Tilt forward", KeyCode.F);
            Add(EditorAction.ResetTurn, Placing, "Reset turn and distance", KeyCode.T);
            Add(EditorAction.LaunchSlower, Placing, "Launch speed down", KeyCode.LeftBracket);
            Add(EditorAction.LaunchFaster, Placing, "Launch speed up", KeyCode.RightBracket);
            Add(EditorAction.DeleteAimed, Placing, "Delete the aimed object", KeyCode.Delete, KeyCode.Backspace);

            Add(EditorAction.BankLeft, Ramp, "Bank left", KeyCode.Q);
            Add(EditorAction.BankRight, Ramp, "Bank right", KeyCode.E);
            Add(EditorAction.ResetRampPoint, Ramp, "Reset height, bank and distance", KeyCode.T);
            Add(EditorAction.RemoveRampPoint, Ramp, "Remove the last point", KeyCode.Backspace);
            Add(EditorAction.FinishRamp, Ramp, "Finish the ramp", KeyCode.Return, KeyCode.KeypadEnter);

            Add(EditorAction.HandlesMove, Mouse, "Move handles", KeyCode.W);
            Add(EditorAction.HandlesRotate, Mouse, "Rotate handles", KeyCode.E);
            Add(EditorAction.HandlesScale, Mouse, "Scale handles", KeyCode.R);
            Add(EditorAction.DeleteSelected, Mouse, "Delete the selection", KeyCode.Delete);
            Add(EditorAction.CameraForward, Mouse, "Camera forward (right mouse held)", KeyCode.W);
            Add(EditorAction.CameraBack, Mouse, "Camera back", KeyCode.S);
            Add(EditorAction.CameraLeft, Mouse, "Camera left", KeyCode.A);
            Add(EditorAction.CameraRight, Mouse, "Camera right", KeyCode.D);
            Add(EditorAction.CameraDown, Mouse, "Camera down", KeyCode.Q);
            Add(EditorAction.CameraUp, Mouse, "Camera up", KeyCode.E, KeyCode.Space);

            Add(EditorAction.Undo, WithCtrl, "Undo (Ctrl+Shift: redo)", KeyCode.Z);
            Add(EditorAction.Redo, WithCtrl, "Redo", KeyCode.Y);
            Add(EditorAction.Copy, WithCtrl, "Copy", KeyCode.C);
            Add(EditorAction.Paste, WithCtrl, "Paste", KeyCode.V);
            Add(EditorAction.Duplicate, WithCtrl, "Duplicate", KeyCode.D);
            Add(EditorAction.SelectAll, WithCtrl, "Select everything (mouse mode)", KeyCode.A);
        }

        /// <summary>Binds every action to the config; missing or unreadable entries use the defaults.</summary>
        public static void Bind(ConfigFile config)
        {
            foreach (var info in All)
            {
                info.Entry = config.Bind("Keys", info.Action.ToString(), Format(info.Defaults),
                    $"{info.Group}: {info.Label}. One or two Unity key names separated by a comma; empty for none. Change it in the editor panel's Controls tab.");
                Parse(info);
            }
        }

        private static void Parse(Info info)
        {
            info.Primary = info.Secondary = KeyCode.None;
            var parts = (info.Entry?.Value ?? "").Split(',');
            int n = 0;
            foreach (var raw in parts)
            {
                var s = raw.Trim();
                if (s.Length == 0 || !Enum.TryParse(s, true, out KeyCode k) || k == KeyCode.None) continue;
                if (n == 0) info.Primary = k; else if (n == 1) info.Secondary = k;
                n++;
            }
        }

        private static string Format(KeyCode[] keys)
        {
            var list = new List<string>();
            foreach (var k in keys) if (k != KeyCode.None) list.Add(k.ToString());
            return string.Join(", ", list);
        }

        public static Info Get(EditorAction a) => ByAction[a];

        public static void Set(EditorAction a, int slot, KeyCode key)
        {
            var info = Get(a);
            if (slot == 0) info.Primary = key; else info.Secondary = key;
            if (info.Primary == KeyCode.None && info.Secondary != KeyCode.None) { info.Primary = info.Secondary; info.Secondary = KeyCode.None; }
            if (info.Entry != null) info.Entry.Value = Format(new[] { info.Primary, info.Secondary });
        }

        public static void ResetAll()
        {
            foreach (var info in All)
            {
                if (info.Entry != null) info.Entry.Value = Format(info.Defaults);
                Parse(info);
            }
        }

        public static bool Down(EditorAction a)
        {
            var i = Get(a);
            return (i.Primary != KeyCode.None && Input.GetKeyDown(i.Primary)) || (i.Secondary != KeyCode.None && Input.GetKeyDown(i.Secondary));
        }

        public static bool Held(EditorAction a)
        {
            var i = Get(a);
            return (i.Primary != KeyCode.None && Input.GetKey(i.Primary)) || (i.Secondary != KeyCode.None && Input.GetKey(i.Secondary));
        }

        /// <summary>The action's keys for the help texts, e.g. "Delete" or "Return / KeypadEnter".</summary>
        public static string Name(EditorAction a)
        {
            var i = Get(a);
            if (i.Primary == KeyCode.None) return "unbound";
            return i.Secondary == KeyCode.None ? KeyName(i.Primary) : KeyName(i.Primary) + " / " + KeyName(i.Secondary);
        }

        /// <summary>Short, readable key names.</summary>
        public static string KeyName(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.None: return "—";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Return: return "Enter";
                case KeyCode.KeypadEnter: return "Num Enter";
                case KeyCode.LeftAlt: return "Alt";
                case KeyCode.RightAlt: return "Right Alt";
                case KeyCode.LeftShift: return "Shift";
                case KeyCode.RightShift: return "Right Shift";
                case KeyCode.LeftControl: return "Ctrl";
                case KeyCode.RightControl: return "Right Ctrl";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Quote: return "'";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Slash: return "/";
                case KeyCode.Backslash: return "\\";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.BackQuote: return "`";
            }
            var s = k.ToString();
            if (s.StartsWith("Alpha")) return s.Substring(5);
            if (s.StartsWith("Keypad")) return "Num " + s.Substring(6);
            return s;
        }

        /// <summary>Other actions that share a key with <paramref name="info"/> while both are active.</summary>
        public static List<Info> Conflicts(Info info)
        {
            var result = new List<Info>();
            foreach (var other in All)
            {
                if (other == info || !ActiveAtOnce(info.Group, other.Group)) continue;
                if (Shares(info, other)) result.Add(other);
            }
            return result;
        }

        private static bool Shares(Info a, Info b)
        {
            foreach (var x in new[] { a.Primary, a.Secondary })
                if (x != KeyCode.None && (x == b.Primary || x == b.Secondary)) return true;
            return false;
        }

        private static bool ActiveAtOnce(string a, string b)
        {
            // Ctrl shortcuts only fire with Ctrl held, so they only clash with each other.
            if ((a == WithCtrl) != (b == WithCtrl)) return false;
            foreach (var set in ActiveTogether)
                if (Array.IndexOf(set, a) >= 0 && Array.IndexOf(set, b) >= 0) return true;
            return false;
        }
    }
}
