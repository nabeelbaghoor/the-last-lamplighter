using UnityEngine;

namespace Lamplighter
{
    /// <summary>
    /// Side-scrolling camera: deadzone follow with a lerp, look-ahead in the direction of travel,
    /// shake, and parallax layers that scroll at a fraction of the camera.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        /// <summary>Child of the camera at z = 10, for screen-space sprites (composite, vignette, motes).</summary>
        public Transform Screen { get; private set; }

        public Transform Target;
        public float LookAhead;
        public bool Following = true;

        private float _x = Config.ViewW / 2f;
        private float _shakeUntil, _shakeAmount;
        private Tween.Handle _pan;
        private readonly System.Collections.Generic.List<(Transform t, float factor)> _parallax =
            new System.Collections.Generic.List<(Transform, float)>();

        public static CameraRig Create()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Config.ViewH / 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Config.Hex(0x07080f);
            cam.cullingMask = ~((1 << Config.LitLayer) | (1 << Config.LightMapLayer));
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 50f;
            cam.depth = 0;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            go.transform.position = new Vector3(Config.ViewW / 2f, Config.ViewH / 2f, -10f);
            var rig = go.AddComponent<CameraRig>();
            rig.Cam = cam;
            var screen = new GameObject("Screen");
            screen.transform.SetParent(go.transform, false);
            screen.transform.localPosition = new Vector3(0f, 0f, 10f);
            rig.Screen = screen.transform;
            return rig;
        }

        public float X => _x;

        public void AddParallax(Transform t, float factor) => _parallax.Add((t, factor));

        public void ClearParallax() => _parallax.Clear();

        public void SnapTo(float x)
        {
            _pan?.Kill();
            _x = Clamp(x);
        }

        /// <summary>Phaser-style shake: intensity is a fraction of the view size.</summary>
        public void Shake(float seconds, float intensity)
        {
            _shakeUntil = Time.time + seconds;
            _shakeAmount = intensity;
        }

        public void PanTo(float x, float seconds)
        {
            Following = false;
            float from = _x;
            _pan?.Kill();
            _pan = Tween.Value(from, Clamp(x), seconds, v => _x = v, Ease.SineInOut, owner: this);
        }

        private static float Clamp(float x) => Mathf.Clamp(x, Config.ViewW / 2f, Config.WorldW - Config.ViewW / 2f);

        private void LateUpdate()
        {
            if (Following && Target != null)
            {
                // Deadzone of 140 design px around the centre, then a 0.09-per-frame lerp at 60 fps.
                float t = Target.position.x + LookAhead;
                float desired = _x;
                if (t < _x - 0.7f) desired = t + 0.7f;
                else if (t > _x + 0.7f) desired = t - 0.7f;
                float k = 1f - Mathf.Pow(1f - 0.09f, Time.deltaTime * 60f);
                _x = Clamp(_x + (desired - _x) * k);
            }

            var shake = Vector2.zero;
            if (Time.time < _shakeUntil)
            {
                shake = new Vector2(Random.Range(-1f, 1f) * _shakeAmount * Config.ViewW, Random.Range(-1f, 1f) * _shakeAmount * Config.ViewH);
            }
            transform.position = new Vector3(_x + shake.x, Config.ViewH / 2f + shake.y, -10f);

            float scroll = _x - Config.ViewW / 2f;
            foreach (var (t, factor) in _parallax)
            {
                if (t == null) continue;
                t.position = new Vector3((1f - factor) * scroll, 0f, 0f);
            }
        }
    }
}
