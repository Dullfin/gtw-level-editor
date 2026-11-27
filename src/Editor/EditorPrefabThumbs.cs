using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace GTWLevelEditor
{
    /// <summary>
    /// Prefab pictures, drawn straight from each prefab's file: the pieces are built for a moment far above the
    /// level with nothing solid or moving, photographed by a hidden camera that sees only them, and removed in the
    /// same frame. Pictures are kept for the session (redrawn if the file changes) and drawn a few per frame,
    /// in the order the tiles are shown, so opening a folder of many prefabs never stalls the game.
    /// </summary>
    public partial class LevelEditorController
    {
        private const int ThumbSize = 192;
        /// <summary>Time per frame spent drawing pictures (at least one is drawn each frame while any are waiting).</summary>
        private const double ThumbBudgetMs = 4.0;
        /// <summary>Where the temporary pieces are built: far above any level.</summary>
        private static readonly Vector3 ThumbStage = new Vector3(0f, 5000f, 0f);

        private class Thumb
        {
            public RenderTexture Texture;
            public DateTime Stamp;
            public bool Failed;
        }

        private readonly Dictionary<string, Thumb> _thumbs = new Dictionary<string, Thumb>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PrefabEntry> _thumbQueue = new List<PrefabEntry>();
        private readonly HashSet<string> _thumbQueued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Camera _thumbCam;
        private int _thumbLayer = -1;
        private bool _thumbsBroken;
        private int _thumbBatchCount;
        private double _thumbBatchMs;

        /// <summary>The prefab's picture, or null while it's waiting to be drawn (it's queued) or couldn't be drawn.</summary>
        private Texture PrefabThumb(PrefabEntry entry)
        {
            if (_thumbs.TryGetValue(entry.Path, out var t) && t.Stamp == entry.Stamp) return t.Failed ? null : t.Texture;
            if (!_thumbsBroken && _thumbQueued.Add(entry.Path)) _thumbQueue.Add(entry);
            return t != null && !t.Failed ? t.Texture : null; // an older picture until the new one is ready
        }

        /// <summary>True when the picture failed to draw (a broken file, or nothing visible in it).</summary>
        private bool PrefabThumbFailed(PrefabEntry entry) =>
            _thumbsBroken || (_thumbs.TryGetValue(entry.Path, out var t) && t.Stamp == entry.Stamp && t.Failed);

        private void ForgetPrefabThumb(string path)
        {
            if (!_thumbs.TryGetValue(path, out var t)) return;
            if (t.Texture != null) { t.Texture.Release(); Destroy(t.Texture); }
            _thumbs.Remove(path);
        }

        /// <summary>Called every frame: draws waiting pictures within the frame's time budget.</summary>
        private void UpdatePrefabThumbnails()
        {
            if (_thumbQueue.Count == 0 || _thumbsBroken) return;
            var clock = Stopwatch.StartNew();
            do
            {
                var entry = _thumbQueue[0];
                _thumbQueue.RemoveAt(0);
                _thumbQueued.Remove(entry.Path);
                DrawPrefabThumb(entry);
                _thumbBatchCount++;
            } while (_thumbQueue.Count > 0 && !_thumbsBroken && clock.Elapsed.TotalMilliseconds < ThumbBudgetMs);
            _thumbBatchMs += clock.Elapsed.TotalMilliseconds;
            if (_thumbQueue.Count == 0 && _thumbBatchCount > 0)
            {
                Plugin.Log.LogInfo($"Drew {_thumbBatchCount} prefab picture(s) in {_thumbBatchMs:0.0} ms ({_thumbBatchMs / _thumbBatchCount:0.00} ms each).");
                _thumbBatchCount = 0;
                _thumbBatchMs = 0;
            }
        }

        private void DrawPrefabThumb(PrefabEntry entry)
        {
            ForgetPrefabThumb(entry.Path);
            var thumb = new Thumb { Stamp = entry.Stamp, Failed = true };
            _thumbs[entry.Path] = thumb;

            LevelData prefab;
            try { prefab = LevelJson.LevelFromJson(File.ReadAllText(entry.Path)); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't read prefab {entry.Rel} for its picture: {e.Message}");
                return;
            }
            if (prefab.objects == null || prefab.objects.Count == 0) return;

            var root = new GameObject("LE_PrefabPicture");
            try
            {
                foreach (var source in prefab.objects)
                {
                    var d = source.Clone();
                    d.position += ThumbStage;
                    if (d.Motion == MotionType.Rotate || d.Motion == MotionType.Swing) d.pivot += ThumbStage;
                    var o = LevelObject.Create(d, root.transform);
                    o.SetEditorVisible(true);
                }
                // Nothing may touch the level or move: no collisions, triggers, physics or updates.
                foreach (var c in root.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                foreach (var b in root.GetComponentsInChildren<Rigidbody>(true)) { b.isKinematic = true; b.detectCollisions = false; }
                foreach (var o in root.GetComponentsInChildren<LevelObject>(true)) o.enabled = false;
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();

                int layer = ThumbLayer;
                foreach (var tr in root.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;

                var renderers = root.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToList();
                if (renderers.Count == 0) return;
                var box = renderers[0].bounds;
                foreach (var r in renderers) box.Encapsulate(r.bounds);

                var texture = new RenderTexture(ThumbSize, ThumbSize, 24, RenderTextureFormat.ARGB32)
                {
                    name = "LE_Prefab_" + entry.Name,
                    antiAliasing = 4,
                };
                texture.Create();
                if (!RenderThumb(box, layer, texture))
                {
                    texture.Release();
                    Destroy(texture);
                    return;
                }
                thumb.Texture = texture;
                thumb.Failed = false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't draw the picture of prefab {entry.Rel}: {e}");
            }
            finally
            {
                root.SetActive(false); // gone before the game's own camera draws this frame
                Destroy(root);
            }
        }

        /// <summary>Photographs the box from the front, above and to the left, filling the picture.</summary>
        private bool RenderThumb(Bounds box, int layer, RenderTexture target)
        {
            var cam = ThumbCamera;
            cam.cullingMask = 1 << layer;
            float radius = Mathf.Max(0.5f, box.extents.magnitude);
            var rotation = Quaternion.Euler(28f, 32f, 0f);
            float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
            cam.transform.SetPositionAndRotation(box.center - rotation * Vector3.forward * distance, rotation);
            cam.nearClipPlane = Mathf.Max(0.05f, distance - radius * 1.5f);
            cam.farClipPlane = distance + radius * 1.5f;
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else
                {
                    cam.targetTexture = target;
                    cam.Render();
                }
                return true;
            }
            catch (Exception e)
            {
                // Rendering on demand doesn't work here: stop trying, and the tiles show names only.
                _thumbsBroken = true;
                _thumbQueue.Clear();
                _thumbQueued.Clear();
                Plugin.Log.LogWarning($"Prefab pictures are off: the camera couldn't render on demand. {e}");
                return false;
            }
            finally
            {
                cam.targetTexture = null;
            }
        }

        private Camera ThumbCamera
        {
            get
            {
                if (_thumbCam != null) return _thumbCam;
                var go = new GameObject("LE_PrefabPictureCamera");
                go.transform.SetParent(transform, false);
                _thumbCam = go.AddComponent<Camera>();
                _thumbCam.enabled = false; // only renders when asked
                _thumbCam.clearFlags = CameraClearFlags.SolidColor;
                _thumbCam.backgroundColor = new Color(0.16f, 0.18f, 0.23f, 1f);
                _thumbCam.fieldOfView = 30f;
                _thumbCam.allowHDR = false;
                return _thumbCam;
            }
        }

        /// <summary>A layer nothing in the game uses (no name), so the camera sees only the prefab's pieces.</summary>
        private int ThumbLayer
        {
            get
            {
                if (_thumbLayer >= 0) return _thumbLayer;
                _thumbLayer = 31;
                for (int i = 31; i >= 8; i--)
                {
                    if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) { _thumbLayer = i; break; }
                }
                Plugin.Log.LogInfo($"Prefab pictures use layer {_thumbLayer}.");
                return _thumbLayer;
            }
        }
    }
}
