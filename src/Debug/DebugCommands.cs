using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GTWLevelEditor
{
    /// <summary>
    /// Development aid, off unless "EnableDebugCommands" is set in the config. Lines written to
    /// BepInEx\LevelEditor\Debug\commands.txt are run in order; results go to output.txt and
    /// screenshots to the same folder. Lets a script (or an assistant) drive and inspect the game.
    /// </summary>
    public class DebugCommands : MonoBehaviour
    {
        public string Folder;
        private string CommandFile => Path.Combine(Folder, "commands.txt");
        private string OutputFile => Path.Combine(Folder, "output.txt");
        private float _nextPoll;
        private bool _busy;

        private void Start()
        {
            Directory.CreateDirectory(Folder);
            Write($"--- debug commands ready {DateTime.Now:HH:mm:ss} ---");
        }

        private void Update()
        {
            if (_busy || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.5f;
            if (!File.Exists(CommandFile)) return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(CommandFile);
                File.Delete(CommandFile);
            }
            catch (IOException) { return; } // still being written
            StartCoroutine(Run(lines));
        }

        private IEnumerator Run(string[] lines)
        {
            _busy = true;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var cmd = parts[0].ToLowerInvariant();
                Write("> " + line);
                IEnumerator wait = null;
                try
                {
                    switch (cmd)
                    {
                        case "wait": wait = WaitSeconds(F(parts, 1, 1f)); break;
                        case "waitfor": wait = WaitFor(parts.Length > 1 ? parts[1] : "", F(parts, 2, 60f)); break;
                        case "screenshot": wait = Screenshot(parts.Length > 1 ? parts[1] : "shot"); break;
                        case "dump": Dump(); break;
                        case "status": Write(LevelEditorController.Instance.DebugStatus()); break;
                        case "new": Write(LevelEditorController.Instance.DebugBegin(null, (int)F(parts, 1, 0), true) ?? "ok"); break;
                        case "edit": Write(LevelEditorController.Instance.DebugBegin(Rest(parts), 0, true) ?? "ok"); break;
                        case "play": Write(LevelEditorController.Instance.DebugBegin(Rest(parts), 0, false) ?? "ok"); break;
                        case "toggle": LevelEditorController.Instance.DebugToggleEditor(); break;
                        case "panel": LevelEditorController.Instance.DebugTogglePanel(); break;
                        case "titlemenu": LevelEditorController.Instance.DebugToggleTitleMenu(); break;
                        case "place": Write(LevelEditorController.Instance.DebugPlace(parts[1], V(parts, 2))); break;
                        case "tp":
                        {
                            var p = GameBridge.Player;
                            if (p != null) { p.TeleportCharacter(V(parts, 1)); p.ResetVelocityToZero(); }
                            break;
                        }
                        case "look":
                        {
                            // Point the gameplay camera: yaw/pitch in degrees (only while the panel freezes Cinemachine).
                            var cam = Camera.main;
                            if (cam != null) cam.transform.rotation = Quaternion.Euler(F(parts, 2, 20f), F(parts, 1, 0f), 0f);
                            break;
                        }
                        case "campos":
                        {
                            var cam = Camera.main;
                            if (cam != null) cam.transform.position = V(parts, 1);
                            break;
                        }
                        case "ramp":
                        {
                            var pts = new List<Vector3>();
                            for (int i = 2; i + 2 < parts.Length; i += 3) pts.Add(V(parts, i));
                            Write(LevelEditorController.Instance.DebugRamp(parts[1], pts));
                            break;
                        }
                        case "select": Write(LevelEditorController.Instance.DebugSelect(parts.Length > 1 ? parts[1] : "last")); break;
                        case "info": Write(LevelEditorController.Instance.DebugSelectedInfo()); break;
                        case "json": Write(LevelEditorController.Instance.DebugJson()); break;
                        case "save": Write(LevelEditorController.Instance.DebugSave(parts[1])); break;
                        case "load": Write(LevelEditorController.Instance.DebugLoad(Rest(parts))); break;
                        case "explore": Write(LevelEditorController.Instance.DebugExplore((int)F(parts, 1, 0), (int)F(parts, 2, 0)) ?? "ok"); break;
                        case "find": Write(Find(parts.Length > 1 ? parts[1] : "launch", parts.Length > 2 ? parts[2] : "find")); break;
                        case "set": Write(LevelEditorController.Instance.DebugSetField(parts[1], F(parts, 2, 0))); break;
                        case "demo": Write(LevelEditorController.Instance.DebugDemo()); break;
                        case "autograb":
                            AutoGrab.Enabled = parts.Length < 2 || parts[1] != "off";
                            if (parts.Length > 2) AutoGrab.ReleaseAngle = F(parts, 2, 35f);
                            if (parts.Length > 4) AutoGrab.ReleaseDirection = new Vector3(F(parts, 3, 0f), 0f, F(parts, 4, 1f));
                            Write($"autograb {AutoGrab.Enabled} release at {AutoGrab.ReleaseAngle} deg toward {AutoGrab.ReleaseDirection}");
                            break;
                        case "gravity": Write(GameBridge.Player == null ? "no player" : $"gravity={GameBridge.Player.Gravity}"); break;
                        case "autoforward":
                            AutoForward.Enabled = parts.Length < 2 || parts[1] != "off";
                            AutoForward.Follow = parts.Length > 1 && parts[1] == "follow";
                            Write("autoforward " + AutoForward.Enabled + (AutoForward.Follow ? " (follow travel)" : ""));
                            break;
                        case "track": wait = Track(F(parts, 1, 120f), parts.Length > 2 ? parts[2] : "track"); break;
                        case "waitdemo": wait = WaitDemo(F(parts, 1, 180f)); break;
                        // motion move ox oy oz time pause | motion rotate px py pz speed axis | motion swing px py pz angle axis
                        case "motion": Write(LevelEditorController.Instance.DebugMotion(parts[1], V(parts, 2), F(parts, 5, 0), F(parts, 6, 0))); break;
                        case "tpon": Write(LevelEditorController.Instance.DebugTeleportOnto((int)F(parts, 1, 0), F(parts, 2, 0.5f))); break;
                        case "surface": Write(LevelEditorController.Instance.DebugSurface(parts[1])); break;
                        case "vel":
                        {
                            var pl = GameBridge.Player;
                            if (pl != null) pl.SetVelocity(V(parts, 1), Vector3.zero);
                            break;
                        }
                        case "speed":
                        {
                            var pl = GameBridge.Player;
                            Write(pl == null ? "no player" : $"pos={pl.PlayerPosition} speed={pl.Rigidbody.linearVelocity.magnitude:0.00} m/s friction={pl.MainBallCollider.material.dynamicFriction:0.000} ground={(pl.PlayerCollisionHandler.IsGrounded ? pl.PlayerCollisionHandler.Hit.collider?.transform.parent?.name + "/" + pl.PlayerCollisionHandler.Hit.collider?.name : "air")}");
                            break;
                        }
                        case "colliders": Write(Colliders(parts.Length > 1 ? parts[1] : "slip", parts.Length > 2 ? parts[2] : "colliders")); break;
                        case "tree": Write(Tree(parts[1], (int)F(parts, 2, 4))); break;
                        case "fonts": Write(string.Join(Environment.NewLine, Resources.FindObjectsOfTypeAll<Font>().Select(f => $"{f.name} dynamic={f.dynamic} names={string.Join("/", f.fontNames ?? new string[0])}"))); break;
                        case "handles": Write(LevelEditorController.Instance.DebugHandles()); break;
                        case "gizmo": LevelEditorController.Instance.DebugSetGizmo(parts.Length > 1 ? parts[1] : "move"); break;
                        case "undo": LevelEditorController.Instance.DebugUndo(false); break;
                        case "redo": LevelEditorController.Instance.DebugUndo(true); break;
                        case "drag": wait = Drag(F(parts, 1, 0.5f), F(parts, 2, 0.5f), F(parts, 3, 0.5f), F(parts, 4, 0.5f), (int)F(parts, 5, 20)); break;
                        case "title": CustomSession.FindGameState()?.LoadTitleScene(); break;
                        case "quit": Application.Quit(); break;
                        default: Write("unknown command"); break;
                    }
                }
                catch (Exception e)
                {
                    Write("ERROR " + e);
                }
                if (wait != null) yield return wait;
            }
            Write("--- done ---");
            _busy = false;
        }

        /// <summary>Everything after the command word, so names may contain spaces.</summary>
        private static string Rest(string[] p) => string.Join(" ", p.Skip(1));

        private static float F(string[] p, int i, float d) =>
            p.Length > i && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;

        private static Vector3 V(string[] p, int i) => new Vector3(F(p, i, 0), F(p, i + 1, 0), F(p, i + 2, 0));

        /// <summary>Presses the left mouse at one screen point (fractions, top-left origin), moves to another and releases.</summary>
        private IEnumerator Drag(float x1, float y1, float x2, float y2, int frames)
        {
            var ed = LevelEditorController.Instance;
            Vector2 Px(float x, float y) => new Vector2(x * Screen.width, (1f - y) * Screen.height);
            ed.DebugMouse = Px(x1, y1);
            yield return null;
            ed.DebugMouseHeld = true;
            yield return null;
            for (int i = 1; i <= frames; i++)
            {
                float t = i / (float)frames;
                ed.DebugMouse = Px(Mathf.Lerp(x1, x2, t), Mathf.Lerp(y1, y2, t));
                yield return null;
            }
            ed.DebugMouseHeld = false;
            yield return null;
            yield return null;
            ed.DebugMouse = null;
            Write("drag done: " + ed.DebugLastClick);
        }

        /// <summary>
        /// Writes every object whose name or component type contains <paramref name="text"/>, plus
        /// every grab launcher (MovingObject / RotatingObject), with the details needed to rebuild it.
        /// </summary>
        private string Find(string text, string file)
        {
            var sb = new StringBuilder();
            text = text.ToLowerInvariant();
            int count = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var comps = t.GetComponents<Component>().Where(c => c != null).ToArray();
                    bool match = t.name.ToLowerInvariant().Contains(text) ||
                                 comps.Any(c => c.GetType().Name.ToLowerInvariant().Contains(text));
                    if (!match) continue;
                    if (++count > 400) break;
                    sb.AppendLine($"== {scene.name} :: {PathOf(t)}  active={t.gameObject.activeInHierarchy} tag={t.tag} layer={LayerMask.LayerToName(t.gameObject.layer)}");
                    sb.AppendLine($"   pos={t.position} rot={t.eulerAngles} lossyScale={t.lossyScale}");
                    sb.AppendLine("   components: " + string.Join(", ", comps.Select(c => c.GetType().Name)));
                    foreach (var c in comps) DescribeFields(sb, c);
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true).Take(12))
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        sb.AppendLine($"   renderer {PathOf(r.transform)} mesh={(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-")} mats={string.Join("/", r.sharedMaterials.Where(m => m != null).Select(m => m.name))} bounds={r.bounds.center} size={r.bounds.size}");
                    }
                    foreach (var col in t.GetComponentsInChildren<Collider>(true).Take(12))
                        sb.AppendLine($"   collider {PathOf(col.transform)} {col.GetType().Name} trigger={col.isTrigger} tag={col.tag} layer={LayerMask.LayerToName(col.gameObject.layer)} bounds={col.bounds.center} size={col.bounds.size}");
                }
            }
            var path = Path.Combine(Folder, file + ".txt");
            File.WriteAllText(path, sb.ToString());
            return $"find '{text}': {count} matches written to {path}";
        }

        /// <summary>Colliders whose tag, layer or physics material name contains the text, with the material's friction.</summary>
        private string Colliders(string text, string file)
        {
            text = text.ToLowerInvariant();
            var sb = new StringBuilder();
            var materials = new Dictionary<string, int>();
            int count = 0;
            foreach (var c in FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var m = c.sharedMaterial;
                string mat = m == null ? "none" : $"{m.name} dyn={m.dynamicFriction} stat={m.staticFriction} combine={m.frictionCombine} bounce={m.bounciness}/{m.bounceCombine}";
                if (m != null) materials[mat] = materials.TryGetValue(mat, out var n) ? n + 1 : 1;
                string layer = LayerMask.LayerToName(c.gameObject.layer);
                if (!(c.tag.ToLowerInvariant().Contains(text) || layer.ToLowerInvariant().Contains(text) || (m != null && m.name.ToLowerInvariant().Contains(text)))) continue;
                if (++count > 60) continue;
                sb.AppendLine($"{c.gameObject.scene.name} :: {PathOf(c.transform)} {c.GetType().Name} tag={c.tag} layer={layer} trigger={c.isTrigger} mat={mat}");
                foreach (var r in c.GetComponents<Renderer>())
                    sb.AppendLine("   renderer mats=" + string.Join("/", r.sharedMaterials.Where(x => x != null).Select(x => x.name)));
            }
            sb.AppendLine("---- all physics materials in use ----");
            foreach (var kv in materials.OrderByDescending(k => k.Value)) sb.AppendLine($"{kv.Value,5}  {kv.Key}");
            var path = Path.Combine(Folder, file + ".txt");
            File.WriteAllText(path, sb.ToString());
            return $"colliders '{text}': {count} matches written to {path}";
        }

        /// <summary>The UI hierarchy under every object with this exact name: layout, text, images and click handlers.</summary>
        private string Tree(string name, int depth)
        {
            var sb = new StringBuilder();
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t.name != name || !t.gameObject.scene.IsValid()) continue;
                TreeNode(sb, t, 0, depth);
            }
            var path = Path.Combine(Folder, "tree_" + name + ".txt");
            File.WriteAllText(path, sb.ToString());
            return "tree written to " + path;
        }

        private static void TreeNode(StringBuilder sb, Transform t, int level, int depth)
        {
            var pad = new string(' ', level * 2);
            var comps = t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name);
            sb.AppendLine($"{pad}{t.name} active={t.gameObject.activeSelf} [{string.Join(", ", comps)}]");
            if (t is RectTransform rt)
                sb.AppendLine($"{pad}  rect anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} pivot={rt.pivot} anchoredPos={rt.anchoredPosition} size={rt.sizeDelta} scale={rt.localScale} rotZ={rt.localEulerAngles.z}");
            var tmp = t.GetComponent<TMPro.TMP_Text>();
            if (tmp != null) sb.AppendLine($"{pad}  text='{tmp.text}' font={tmp.font?.name} size={tmp.fontSize} color={tmp.color} style={tmp.fontStyle}");
            var img = t.GetComponent<UnityEngine.UI.Image>();
            if (img != null) sb.AppendLine($"{pad}  image sprite={img.sprite?.name} color={img.color} type={img.type}");
            var btn = t.GetComponent<UnityEngine.UI.Button>();
            if (btn != null)
            {
                for (int i = 0; i < btn.onClick.GetPersistentEventCount(); i++)
                    sb.AppendLine($"{pad}  onClick {btn.onClick.GetPersistentTarget(i)?.GetType().Name}({btn.onClick.GetPersistentTarget(i)?.name}).{btn.onClick.GetPersistentMethodName(i)}");
                sb.AppendLine($"{pad}  transition={btn.transition} normal={btn.colors.normalColor} highlighted={btn.colors.highlightedColor}");
            }
            foreach (var c in t.GetComponents<MonoBehaviour>())
                if (c != null && !(c.GetType().Namespace ?? "").StartsWith("UnityEngine") && !(c.GetType().Namespace ?? "").StartsWith("TMPro")) DescribeFields(sb, c);
            if (level >= depth) return;
            foreach (Transform child in t) TreeNode(sb, child, level + 1, depth);
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Serialized fields of the game's own components (skips Unity's and the editor's).</summary>
        private static void DescribeFields(StringBuilder sb, Component c)
        {
            var type = c.GetType();
            var ns = type.Namespace ?? "";
            if (ns.StartsWith("UnityEngine") || ns.StartsWith("GTWLevelEditor") || ns.StartsWith("Cinemachine")) return;
            foreach (var f in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (!f.IsPublic && f.GetCustomAttributes(typeof(SerializeField), true).Length == 0) continue;
                object v;
                try { v = f.GetValue(c); } catch { continue; }
                sb.AppendLine($"     .{f.Name} = {Describe(v)}");
            }
        }

        private static string Describe(object v)
        {
            switch (v)
            {
                case null: return "null";
                case Transform tr: return $"{tr.name} pos={tr.position} rot={tr.eulerAngles}";
                case Component co: return $"{co.GetType().Name}({co.name})";
                case UnityEngine.Object o: return $"{o.GetType().Name}({o.name})";
                case string str: return str;
                case System.Collections.IEnumerable list:
                {
                    var items = new List<string>();
                    foreach (var item in list)
                    {
                        if (items.Count >= 12) { items.Add("..."); break; }
                        items.Add(item == null ? "null" : DescribeShallow(item));
                    }
                    return "[" + string.Join("; ", items) + "]";
                }
                default: return DescribeShallow(v);
            }
        }

        /// <summary>Plain values as-is; small serializable classes (like MoveStep) field by field.</summary>
        private static string DescribeShallow(object v)
        {
            var type = v.GetType();
            if (type.IsPrimitive || type.IsEnum || v is Vector3 || v is Quaternion || v is Color || type.Namespace == "UnityEngine" || v is UnityEngine.Object)
                return v is Transform tr ? $"{tr.name} pos={tr.position} rot={tr.eulerAngles}" : v is UnityEngine.Object o ? $"{o.GetType().Name}({o.name})" : v.ToString();
            var fields = type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            return "{" + string.Join(", ", fields.Select(f => f.Name + "=" + (f.GetValue(v) is Transform tr2 ? $"{tr2.name} pos={tr2.position} rot={tr2.eulerAngles}" : f.GetValue(v)?.ToString() ?? "null"))) + "}";
        }

        /// <summary>Logs the player's position and speed every 0.25 s until they finish or time runs out.</summary>
        private IEnumerator Track(float seconds, string file)
        {
            var ed = LevelEditorController.Instance;
            var sb = new StringBuilder();
            float end = Time.unscaledTime + seconds, next = 0f;
            float finishedBefore = ed.LastFinishTime;
            int killsBefore = ed.Kills;
            bool failed = false, stuck = false, ragdoll = false;
            float slowSince = -1f;
            while (Time.unscaledTime < end)
            {
                if (Time.unscaledTime >= next)
                {
                    next = Time.unscaledTime + 0.25f;
                    var p = GameBridge.Player;
                    if (p != null)
                    {
                        var g = p.PlayerCollisionHandler;
                        string ground = g != null && g.IsGrounded && g.Hit.collider != null ? g.Hit.collider.transform.parent?.name : "air";
                        float camYaw = Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f;
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,6:0.00} run={1,6:0.00} pos=({2:0.0},{3:0.0},{4:0.0}) v={5:0.0} cam={6:0} on={7}{8}",
                            Time.unscaledTime - (end - seconds), ed.RunTime, p.PlayerPosition.x, p.PlayerPosition.y, p.PlayerPosition.z, p.Rigidbody.linearVelocity.magnitude, camYaw, ground, p.IsGrabLinkActive ? " GRAB" : ""));
                    }
                }
                if (ed.LastFinishTime != finishedBefore)
                {
                    sb.AppendLine($"FINISHED in {ed.LastFinishTime:0.00}");
                    break;
                }
                var pl = GameBridge.Player;
                if (pl != null && pl.IsRagdollState)
                {
                    // A ragdoll (from a hard impact) also means the course failed.
                    sb.AppendLine($"RAGDOLL at {pl.PlayerPosition} after {ed.RunTime:0.00} s");
                    ragdoll = true;
                    break;
                }
                if (pl != null && pl.Rigidbody.linearVelocity.magnitude < 1f)
                {
                    if (slowSince < 0f) slowSince = Time.unscaledTime;
                    else if (Time.unscaledTime - slowSince > 5f)
                    {
                        sb.AppendLine($"STUCK at {pl.PlayerPosition} after {ed.RunTime:0.00} s");
                        stuck = true;
                        break;
                    }
                }
                else slowSince = -1f;
                if (ed.Kills != killsBefore)
                {
                    // Any kill (kill zone or kill height) means the course failed.
                    sb.AppendLine($"FAILED: killed at {ed.LastKillPosition} after {ed.RunTime:0.00} s");
                    failed = true;
                    break;
                }
                yield return null;
            }
            File.WriteAllText(Path.Combine(Folder, file + ".txt"), sb.ToString());
            Write("track done: " + (ragdoll ? $"RAGDOLL at {GameBridge.Player?.PlayerPosition} after {ed.RunTime:0.00} s" : stuck ? $"STUCK at {GameBridge.Player?.PlayerPosition} after {ed.RunTime:0.00} s" : failed ? $"FAILED, killed at {ed.LastKillPosition} after {ed.RunTime:0.00} s"
                                        : ed.LastFinishTime != finishedBefore ? $"finished in {ed.LastFinishTime:0.00}" : "not finished"));
        }

        private IEnumerator WaitDemo(float timeout)
        {
            float end = Time.unscaledTime + timeout;
            yield return null;
            while (LevelEditorController.Instance.DemoRunning && Time.unscaledTime < end) yield return null;
            Write("demo finished");
        }

        private IEnumerator WaitSeconds(float s)
        {
            float end = Time.unscaledTime + s;
            while (Time.unscaledTime < end) yield return null;
        }

        /// <summary>Waits for a named condition: title, loaded (custom level built), player.</summary>
        private IEnumerator WaitFor(string what, float timeout)
        {
            float end = Time.unscaledTime + timeout;
            while (Time.unscaledTime < end)
            {
                bool ok = what == "title" ? CustomSession.IsOnTitleScreen()
                        : what == "loaded" ? LevelEditorController.Instance.DebugSessionReady
                        : what == "player" ? GameBridge.Player != null
                        : true;
                if (ok) { Write($"waitfor {what}: ok"); yield break; }
                yield return null;
            }
            Write($"waitfor {what}: TIMED OUT");
        }

        /// <summary>Saves a 1600x900 JPG of the screen (full 4K PNGs are too big to look at quickly).</summary>
        private IEnumerator Screenshot(string name)
        {
            yield return new WaitForEndOfFrame();
            var full = ScreenCapture.CaptureScreenshotAsTexture();
            int w = 1600, h = Mathf.RoundToInt(1600f * full.height / full.width);
            var rt = RenderTexture.GetTemporary(w, h, 0);
            Graphics.Blit(full, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var small = new Texture2D(w, h, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            small.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var path = Path.Combine(Folder, name + ".jpg");
            File.WriteAllBytes(path, small.EncodeToJPG(85));
            Destroy(full);
            Destroy(small);
            Write("screenshot " + path);
        }

        /// <summary>Loaded scenes, their root objects, and where the player, camera and lights live.</summary>
        private void Dump()
        {
            var sb = new StringBuilder();
            var active = SceneManager.GetActiveScene();
            sb.AppendLine($"Screen {Screen.width}x{Screen.height}, active scene '{active.name}'");
            var p = GameBridge.Player;
            sb.AppendLine("Player: " + (p == null ? "none" : $"{p.name} in scene '{p.gameObject.scene.name}', ball in '{p.PlayerTransform.gameObject.scene.name}' at {p.PlayerPosition}, state move={p.IsMoveState} noclip={GameBridge.IsInNoClip(p)}"));
            var cam = Camera.main;
            sb.AppendLine("Main camera: " + (cam == null ? "none" : $"{cam.name} in '{cam.gameObject.scene.name}' at {cam.transform.position}"));
            sb.AppendLine($"RenderSettings (active scene): skybox={RenderSettings.skybox?.name ?? "none"} ambientMode={RenderSettings.ambientMode} ambient={RenderSettings.ambientLight} fog={RenderSettings.fog} sun={RenderSettings.sun?.name ?? "none"}");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                sb.AppendLine($"Scene '{s.name}' loaded={s.isLoaded} roots={s.rootCount}{(s == active ? " ACTIVE" : "")}");
                if (!s.isLoaded) continue;
                foreach (var root in s.GetRootGameObjects())
                {
                    int renderers = root.GetComponentsInChildren<Renderer>(true).Length;
                    var lights = root.GetComponentsInChildren<Light>(true);
                    var notable = root.GetComponentsInChildren<MonoBehaviour>(true)
                        .Where(m => m != null)
                        .Select(m => m.GetType().Name)
                        .Where(n => n.Contains("Context") || n.Contains("Installer") || n.Contains("SectionLoader") || n.Contains("CheckpointManager")
                                    || n.Contains("PlayerController") || n.Contains("Volume") || n.Contains("Brain") || n.Contains("Manager"))
                        .Distinct().Take(12);
                    sb.AppendLine($"  {(root.activeSelf ? " " : "x")} {root.name}  renderers={renderers} lights={lights.Length}{(lights.Length > 0 ? " [" + string.Join(", ", lights.Take(4).Select(l => $"{l.type}:{l.name}")) + "]" : "")}  {string.Join(" ", notable)}");
                }
            }
            Write(sb.ToString());
        }

        private void Write(string text)
        {
            try { File.AppendAllText(OutputFile, text + Environment.NewLine); } catch (IOException) { }
            Plugin.Log.LogInfo("[debug] " + text);
        }
    }
}
