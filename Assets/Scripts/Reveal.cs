using System.Collections.Generic;
using UnityEngine;

namespace Lamplighter
{
    public sealed class LightSource
    {
        public Vector2 Pos;
        public float Radius;
        /// <summary>0..1</summary>
        public float Intensity;

        public LightSource(Vector2 pos, float radius, float intensity)
        {
            Pos = pos;
            Radius = radius;
            Intensity = intensity;
        }
    }

    /// <summary>
    /// The town is painted twice: a cold gloom painting and a warm lit painting (a DreamLayer edit
    /// of the same scene). The gloom stack renders normally. A second camera renders the lit stack
    /// into a texture, a third paints every light into a small light map, and a full-screen quad
    /// blends the lit world over the gloom wherever there is light.
    /// </summary>
    public class Reveal : MonoBehaviour
    {
        /// <summary>0..1, floods the whole screen with light for the ending.</summary>
        public float FullReveal;

        private readonly List<LightSource> _lights = new List<LightSource>();
        private readonly List<SpriteRenderer> _brushes = new List<SpriteRenderer>();
        private Camera _main, _litCam, _lightCam;
        private RenderTexture _litRT, _lightRT;
        private Transform _brushRoot;
        private MeshRenderer _composite;
        private Material _compositeMat;
        private Sprite _brushSprite;

        public static Reveal Create(Camera main, Transform screenSpace)
        {
            var go = new GameObject("Reveal");
            var r = go.AddComponent<Reveal>();
            r.Init(main, screenSpace);
            return r;
        }

        private void Init(Camera main, Transform screenSpace)
        {
            _main = main;
            _litCam = MakeCamera("LitCamera", 1 << Config.LitLayer, new Color(0, 0, 0, 0), main.depth - 2);
            _lightCam = MakeCamera("LightCamera", 1 << Config.LightMapLayer, Color.black, main.depth - 1);
            _brushRoot = new GameObject("Lights").transform;
            _brushRoot.SetParent(transform, false);
            _brushSprite = Res.Sprite("fx/light_brush");

            // A unit quad built by hand (CreatePrimitive would pull in the 3D physics module).
            var quad = new GameObject("RevealComposite");
            quad.transform.SetParent(screenSpace, false);
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
            };
            mesh.RecalculateBounds();
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            _composite = quad.AddComponent<MeshRenderer>();
            _compositeMat = new Material(Res.Composite);
            _composite.sharedMaterial = _compositeMat;
            _composite.sortingOrder = Config.Order.Composite;
            _composite.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _composite.receiveShadows = false;
            EnsureTargets();
        }

        private Camera MakeCamera(string name, int mask, Color clear, float depth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_main.transform, false);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = _main.orthographicSize;
            cam.cullingMask = mask;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = clear;
            cam.depth = depth;
            cam.nearClipPlane = _main.nearClipPlane;
            cam.farClipPlane = _main.farClipPlane;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            return cam;
        }

        private void EnsureTargets()
        {
            int w = Mathf.Max(16, Screen.width), h = Mathf.Max(16, Screen.height);
            if (_litRT == null || _litRT.width != w || _litRT.height != h)
            {
                if (_litRT != null) _litRT.Release();
                _litRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "LitWorld", filterMode = FilterMode.Bilinear };
                _litCam.targetTexture = _litRT;
                _compositeMat.SetTexture("_LitTex", _litRT);
            }
            // Light is soft by nature; a quarter-resolution map is indistinguishable and cheap.
            int lw = Mathf.Max(16, w / 4), lh = Mathf.Max(16, h / 4);
            if (_lightRT == null || _lightRT.width != lw || _lightRT.height != lh)
            {
                if (_lightRT != null) _lightRT.Release();
                _lightRT = new RenderTexture(lw, lh, 0, RenderTextureFormat.ARGB32) { name = "LightMap", filterMode = FilterMode.Bilinear };
                _lightCam.targetTexture = _lightRT;
                _compositeMat.SetTexture("_LightMap", _lightRT);
            }
        }

        public LightSource Add(LightSource l)
        {
            if (!_lights.Contains(l)) _lights.Add(l);
            return l;
        }

        public void Remove(LightSource l) => _lights.Remove(l);

        /// <summary>How strongly a world point is lit by any source except the excluded one (0..1).</summary>
        public float LightAt(Vector2 p, LightSource exclude = null)
        {
            float best = FullReveal;
            foreach (var l in _lights)
            {
                if (l == exclude || l.Radius <= 0f || l.Intensity <= 0f) continue;
                float d = Vector2.Distance(p, l.Pos);
                if (d < l.Radius) best = Mathf.Max(best, (1f - d / l.Radius) * l.Intensity);
            }
            return best;
        }

        private void LateUpdate()
        {
            EnsureTargets();
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            _composite.transform.localScale = new Vector3(_main.orthographicSize * 2f * aspect, _main.orthographicSize * 2f, 1f);

            float fr = Mathf.Clamp01(FullReveal);
            _lightCam.backgroundColor = new Color(fr, fr, fr, 1f);

            // The brush texture is 256 px = 2.56 units across, so radius r needs scale r / 1.28.
            Vector2 cam = _main.transform.position;
            float halfW = _main.orthographicSize * aspect, halfH = _main.orthographicSize;
            int used = 0;
            if (fr < 1f)
            {
                foreach (var l in _lights)
                {
                    if (l.Radius <= 0.01f || l.Intensity <= 0f) continue;
                    var d = l.Pos - cam;
                    if (Mathf.Abs(d.x) > halfW + l.Radius || Mathf.Abs(d.y) > halfH + l.Radius) continue;
                    if (used == _brushes.Count)
                    {
                        var sr = Res.MakeSprite("light", _brushSprite, _brushRoot, 0, Config.LightMapLayer);
                        sr.sharedMaterial = Res.Brush;
                        _brushes.Add(sr);
                    }
                    var b = _brushes[used++];
                    b.enabled = true;
                    b.transform.position = new Vector3(l.Pos.x, l.Pos.y, 0f);
                    b.transform.localScale = Vector3.one * (l.Radius / 1.28f);
                    b.color = new Color(1f, 1f, 1f, Mathf.Clamp01(l.Intensity));
                }
            }
            for (int i = used; i < _brushes.Count; i++) _brushes[i].enabled = false;
        }

        private void OnDestroy()
        {
            if (_litCam) Destroy(_litCam.gameObject);
            if (_lightCam) Destroy(_lightCam.gameObject);
            if (_composite) Destroy(_composite.gameObject);
            if (_litRT) _litRT.Release();
            if (_lightRT) _lightRT.Release();
        }
    }
}
