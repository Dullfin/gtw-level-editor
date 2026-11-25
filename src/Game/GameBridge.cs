using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Isto.Core.StateMachine;
using Isto.GTW.LevelFeatures;
using Isto.GTW.Player;
using Isto.GTW.Player.States;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GTWLevelEditor
{
    /// <summary>
    /// Everything that touches the game's own classes lives here, so a game update
    /// that renames something only breaks this file.
    /// </summary>
    internal static class GameBridge
    {
        private static readonly FieldInfo NoClipStateField = AccessTools.Field(typeof(PlayerController), "_noClipState");
        private static readonly FieldInfo CurrentStateField = AccessTools.Field(typeof(PlayerController), "_currentState");
        private static readonly FieldInfo ActiveCheckpointField = AccessTools.Field(typeof(CheckpointManager), "_activeCheckpoint");

        public const string PlayerTriggerTag = "PlayerEventTrigger";

        private static PlayerController _player;
        private static int _groundLayer = -1;
        private static int _triggerLayer = -1;
        private static int _slipperyLayer = -2;

        public static PlayerController Player
        {
            get
            {
                if (_player == null)
                {
                    _player = Object.FindFirstObjectByType<PlayerController>();
                    if (_player != null)
                    {
                        // A new level was loaded: re-detect layers for it.
                        _groundLayer = -1;
                        _triggerLayer = -1;
                    }
                }
                return _player;
            }
        }

        public static bool IsPlayerTrigger(Collider other) => other != null && other.CompareTag(PlayerTriggerTag);

        // ---------- player states ----------

        public static bool IsInNoClip(PlayerController p) => CurrentStateField?.GetValue(p) is GTWPlayerNoClipState;

        public static void EnterNoClip(PlayerController p)
        {
            if (NoClipStateField?.GetValue(p) is State noClip && !IsInNoClip(p))
            {
                p.ChangeToCustomState(noClip);
            }
        }

        // ---------- checkpoints ----------

        public static CheckpointData.LevelCheckpointData MakeCheckpoint(string name, Vector3 position, Vector3 forward)
        {
            return new CheckpointData.LevelCheckpointData
            {
                checkpointName = name,
                sceneName = SceneManager.GetActiveScene().name,
                checkpointId = -1,
                position = position,
                forward = forward,
                spawnOnGround = true,
                showInSpeedrunTimer = false,
            };
        }

        /// <summary>
        /// Points the game's respawn at our checkpoint without registering it in the level's
        /// CheckpointData asset, so speedrun splits and leaderboards are left alone.
        /// </summary>
        public static void SetActiveCheckpoint(CheckpointData.LevelCheckpointData checkpoint)
        {
            var manager = Player?.CheckpointManager;
            if (manager != null && ActiveCheckpointField != null)
            {
                ActiveCheckpointField.SetValue(manager, checkpoint);
            }
        }

        public static void RespawnAt(CheckpointData.LevelCheckpointData checkpoint)
        {
            var p = Player;
            if (p == null) return;
            SetActiveCheckpoint(checkpoint);
            if (p.CheckpointManager != null) p.CheckpointManager.Respawn(showBlackOut: true);
            else p.RespawnAtCheckpoint(checkpoint);
        }

        // ---------- camera ----------

        /// <summary>Turns Cinemachine on or off on the main camera, which freezes the view while editing with the mouse.</summary>
        public static void SetCameraBrainEnabled(bool enabled)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (cam.GetComponent("CinemachineBrain") is Behaviour brain) brain.enabled = enabled;
        }

        // ---------- layers, tags and materials ----------

        /// <summary>A layer the player's ground check treats as solid, preferring Default.</summary>
        public static int GroundLayer
        {
            get
            {
                if (_groundLayer >= 0) return _groundLayer;
                _groundLayer = 0;
                var handler = Player != null ? Player.PlayerCollisionHandler : null;
                if (handler != null)
                {
                    int mask = handler.GroundLayerMask.value;
                    if ((mask & 1) == 0)
                    {
                        for (int i = 0; i < 32; i++)
                        {
                            if ((mask & (1 << i)) != 0) { _groundLayer = i; break; }
                        }
                    }
                }
                return _groundLayer;
            }
        }

        /// <summary>
        /// A layer the player's ground check ignores, for markers (Start, Checkpoint, Finish).
        /// The game's ground sphere cast also hits triggers, so a trigger on a ground layer
        /// would still hold the character up. Prefers Unity's "Ignore Raycast" layer.
        /// </summary>
        public static int MarkerLayer
        {
            get
            {
                var handler = Player != null ? Player.PlayerCollisionHandler : null;
                int mask = handler != null ? handler.GroundLayerMask.value : 1;
                if ((mask & (1 << 2)) == 0) return 2;
                if ((mask & (1 << TriggerLayer)) == 0) return TriggerLayer;
                for (int i = 31; i >= 8; i--)
                    if ((mask & (1 << i)) == 0) return i;
                return 2;
            }
        }

        public static int SlipperyLayer
        {
            get
            {
                if (_slipperyLayer == -2) _slipperyLayer = LayerMask.NameToLayer("Slippery");
                return _slipperyLayer;
            }
        }

        /// <summary>
        /// The layer the game's own trigger volumes use, so ours collide with the player's
        /// event trigger but are never mistaken for ground.
        /// </summary>
        public static int TriggerLayer
        {
            get
            {
                if (_triggerLayer >= 0) return _triggerLayer;
                _triggerLayer = 2; // Ignore Raycast
                foreach (var t in Object.FindObjectsByType<LevelEventTrigger>(FindObjectsSortMode.None))
                {
                    var c = t.GetComponentInChildren<Collider>();
                    if (c != null && c.isTrigger) { _triggerLayer = c.gameObject.layer; return _triggerLayer; }
                }
                return _triggerLayer;
            }
        }

        public static void TrySetTag(GameObject go, string tag)
        {
            try { go.tag = tag; }
            catch (UnityException) { /* tag not defined in this build */ }
        }

        // ---------- materials ----------
        //
        // URP's Lit shader isn't in the player build itself; it lives in the game's shared
        // Addressables bundle, which is loaded whenever a Doinkler world is. We load the shader
        // asset from that bundle. If that fails, we copy an opaque material from the scene instead.

        private static Material _template;
        private static string _colorProperty;
        public static string MaterialInfo { get; private set; } = "none yet";

        public static Material CreateMaterial(Color color)
        {
            if (_template == null) _template = BuildTemplate();
            var mat = _template != null ? new Material(_template) : new Material(Shader.Find("Standard"));
            SetColor(mat, color);
            return mat;
        }

        private static Shader LoadLitShader()
        {
            var found = Shader.Find("Universal Render Pipeline/Lit");
            if (found != null && found.isSupported) return found;
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (bundle == null || bundle.isStreamedSceneAssetBundle) continue;
                foreach (var name in bundle.GetAllAssetNames())
                {
                    if (!name.EndsWith("/lit.shader") || !name.Contains("universal")) continue;
                    var shader = bundle.LoadAsset<Shader>(name);
                    if (shader != null && shader.isSupported)
                    {
                        Plugin.Log.LogInfo($"Loaded {shader.name} from bundle {bundle.name}");
                        return shader;
                    }
                }
            }
            return null;
        }

        private static Material BuildTemplate()
        {
            try
            {
                var lit = LoadLitShader();
                if (lit != null)
                {
                    var m = new Material(lit) { name = "LE_Template" };
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
                    _colorProperty = "_BaseColor";
                    MaterialInfo = "shader " + lit.name;
                    Plugin.Log.LogInfo("Shapes use " + MaterialInfo);
                    return m;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not load the URP Lit shader: " + e.Message);
            }
            return BuildTemplateFromScene();
        }

        public static void SetColor(Material mat, Color color)
        {
            if (_colorProperty != null && mat.HasProperty(_colorProperty)) mat.SetColor(_colorProperty, color);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        }

        private static Material BuildTemplateFromScene()
        {
            Material best = null;
            int bestScore = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (var m in r.sharedMaterials)
                {
                    int score = ScoreMaterial(m);
                    if (score > bestScore) { best = m; bestScore = score; }
                }
            }
            if (best == null)
            {
                Plugin.Log.LogWarning("No usable material found in the level; shapes may render pink.");
                return null;
            }

            var template = new Material(best) { name = "LE_Template" };
            _colorProperty = FindColorProperty(template.shader);
            // Replace albedo textures with white so the tint shows as a flat colour.
            var shader = template.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                var name = shader.GetPropertyName(i);
                var lower = name.ToLowerInvariant();
                if (lower.Contains("base") || lower.Contains("main") || lower.Contains("albedo") || lower.Contains("diffuse") || lower.Contains("color"))
                {
                    template.SetTexture(name, Texture2D.whiteTexture);
                }
            }
            template.DisableKeyword("_EMISSION");
            MaterialInfo = $"copied material '{best.name}' (shader {shader.name})";
            Plugin.Log.LogInfo("Shapes use " + MaterialInfo);
            return template;
        }

        /// <summary>Prefers plain opaque URP Lit materials without textures; rejects anything transparent or special.</summary>
        private static int ScoreMaterial(Material m)
        {
            if (m == null || m.shader == null) return 0;
            var s = m.shader.name;
            if (s.Contains("Hidden") || s.Contains("Error") || s.Contains("Particle") || s.Contains("Sprite") || s.Contains("UI/")
                || s.Contains("Skybox") || s.Contains("Unlit") || s.Contains("Water") || s.Contains("Decal") || s.Contains("Terrain")) return 0;
            if (m.renderQueue >= 2450) return 0; // alpha-tested or transparent
            if (FindColorProperty(m.shader) == null) return 0;

            int score = 10;
            if (s == "Universal Render Pipeline/Lit") score += 100;
            else if (s == "Universal Render Pipeline/Simple Lit") score += 80;
            else if (s.Contains("Lit")) score += 40;
            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") == null) score += 20;
            if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") == null) score += 20;
            if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") == null) score += 5;
            return score;
        }

        private static string FindColorProperty(Shader shader)
        {
            string fallback = null;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                var name = shader.GetPropertyName(i);
                if (name == "_BaseColor" || name == "_Color") return name;
                var lower = name.ToLowerInvariant();
                if (fallback == null && (lower.Contains("base") || lower.Contains("albedo") || lower.Contains("tint") || lower.Contains("main")))
                    fallback = name;
            }
            return fallback;
        }

        // ---------- Doinkler UI and level start ----------

        /// <summary>
        /// Hides the game's simple Doinkler level timer while a custom level runs. The speedrun timer
        /// panel stays: it shows our level's time instead (see GameTimer).
        /// </summary>
        public static void HideGameTimers()
        {
            foreach (var t in Object.FindObjectsByType<Isto.GTW.UI.UIDoinklerTimer>(FindObjectsSortMode.None))
                HideUi(t.gameObject);
        }

        private static void HideUi(GameObject go)
        {
            // Never switch off a whole canvas, only the timer's own object.
            if (go.GetComponent<Canvas>() != null || go.transform.parent == null) return;
            go.SetActive(false);
            Plugin.Log.LogInfo("Hid game timer UI " + go.name);
        }

        // ---------- the custom world ----------

        /// <summary>
        /// Switches off the Doinkler world's own hazards. Its level geometry is never loaded
        /// (see the SectionLoader patch), but the world scene still has a death zone and
        /// the like that would interfere with a custom level.
        /// </summary>
        public static void DisableWorldHazards()
        {
            var essentials = Player != null ? Player.gameObject.scene : default;
            int groundMask = Player != null && Player.PlayerCollisionHandler != null ? Player.PlayerCollisionHandler.GroundLayerMask.value : 1;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded || s == essentials) continue;
                foreach (var root in s.GetRootGameObjects())
                {
                    if (root.name.Contains("DeathZone") || root.GetComponentInChildren<LevelEventTrigger>(true) != null)
                    {
                        root.SetActive(false);
                        Plugin.Log.LogInfo($"Disabled world object {root.name} in {s.name}");
                        continue;
                    }
                    // The player's ground sphere cast also hits triggers. A huge world trigger (like the
                    // ambience volume around the level) then counts as "ground" near the world origin and
                    // holds the character up, so world triggers on ground layers lose their collider.
                    foreach (var col in root.GetComponentsInChildren<Collider>(true))
                    {
                        if (!col.isTrigger || (groundMask & (1 << col.gameObject.layer)) == 0 || col.name.StartsWith("Cinemachine")) continue;
                        col.enabled = false;
                        Plugin.Log.LogInfo($"Disabled world trigger {col.name} in {s.name}");
                    }
                }
            }
        }

        /// <summary>Forget everything when the level the player was in goes away.</summary>
        public static void ResetForNewLevel()
        {
            _player = null;
            if (_template != null) Object.Destroy(_template);
            _template = null;
            _colorProperty = null;
        }
    }
}
