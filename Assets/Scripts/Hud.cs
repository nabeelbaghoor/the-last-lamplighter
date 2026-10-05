using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Lamplighter
{
    /// <summary>
    /// All screen UI, built at runtime with uGUI on a 1280x720 reference canvas: flame bar, lamp
    /// counter, hints, banners, title, pause and end screens, the fade overlay and touch buttons.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        private const float BarW = 230f, BarH = 14f;

        private Canvas _canvas;
        private RectTransform _root;
        private Image _fade;
        private CanvasGroup _hud, _title, _pause, _end, _touch;
        private Image _barFill, _barShine, _emberIcon;
        private readonly List<Image> _lampIcons = new List<Image>();
        private readonly List<Image> _lampGlows = new List<Image>();
        private Text _banner, _mute, _hint, _titlePrompt, _endAgain;
        private readonly List<Text> _endLines = new List<Text>();
        private Tween.Handle _bannerTween, _fadeTween;
        private float _flame = 1f;
        private readonly List<Text> _hints = new List<Text>();

        // Touch buttons: centre (reference px), current state.
        private readonly List<(RectTransform rt, Image img, string id)> _buttons = new List<(RectTransform, Image, string)>();
        public int TouchDir { get; private set; }
        public bool TouchJump { get; private set; }
        public event Action TouchFlare;
        public event Action TouchPause;
        private readonly HashSet<string> _held = new HashSet<string>();

        public static Hud Create()
        {
            var go = new GameObject("HUD");
            var hud = go.AddComponent<Hud>();
            hud.Build();
            return hud;
        }

        // ------------------------------------------------------------------ helpers

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Design-pixel position (x right, y down from the top-left of 1280x720) relative to the centre anchor.</summary>
        private static Vector2 C(float x, float y) => new Vector2(x - 640f, 360f - y);

        private static Text MakeText(Transform parent, string s, Font font, int size, Color color, Vector2 anchor, Vector2 pos,
            TextAnchor align = TextAnchor.MiddleCenter, Vector2? pivot = null, bool shadow = true)
        {
            var rt = Rect("text", parent, anchor, pos, new Vector2(1200f, size * 1.6f), pivot);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.text = s;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            if (shadow)
            {
                var sh = rt.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
                sh.effectDistance = new Vector2(0f, -2f);
            }
            return t;
        }

        /// <summary>A soft coloured halo behind display text.</summary>
        private static void Halo(Text t, Color color)
        {
            foreach (var d in new[] { new Vector2(2f, 2f), new Vector2(-2f, -2f) })
            {
                var o = t.gameObject.AddComponent<Outline>();
                o.effectColor = color;
                o.effectDistance = d;
            }
        }

        private static Image MakeImage(Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 pos, Vector2 size, bool sliced = false,
            Vector2? pivot = null)
        {
            var rt = Rect(sprite ? sprite.name : "image", parent, anchor, pos, size, pivot);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            if (sliced) img.type = Image.Type.Sliced;
            return img;
        }

        private static CanvasGroup Group(string name, Transform parent)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero);
            Stretch(rt);
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            g.blocksRaycasts = false;
            g.interactable = false;
            return g;
        }

        private static Image Fit(Image img, float height)
        {
            var r = img.sprite.rect;
            img.rectTransform.sizeDelta = new Vector2(r.width / r.height * height, height);
            return img;
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _root = (RectTransform)transform;

            var mid = new Vector2(0.5f, 0.5f);
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);
            var round = Res.Sprite("fx/round_rect");
            var ring = Res.Sprite("fx/round_rect_ring");
            var cream = Config.Hex(0xffe2b0);

            // ---- in-game HUD
            _hud = Group("Hud", _root);
            MakeImage(_hud.transform, round, Config.Hex(0x05060c, 0.55f), topLeft, new Vector2(14f, -12f), new Vector2(BarW + 70f, 48f), true, topLeft);
            MakeImage(_hud.transform, Res.Sprite("fx/glow"), Config.Hex(0xff8a3c, 0.5f), topLeft, new Vector2(36f, -36f), new Vector2(77f, 77f));
            _emberIcon = Fit(MakeImage(_hud.transform, Res.Sprite("props/ember"), Color.white, topLeft, new Vector2(36f, -36f), Vector2.one), 40f);
            MakeImage(_hud.transform, round, new Color(0f, 0f, 0f, 0.45f), topLeft, new Vector2(64f, -30f), new Vector2(BarW, BarH), true, topLeft);
            _barFill = MakeImage(_hud.transform, round, Config.Hex(0xffb35c), topLeft, new Vector2(64f, -30f), new Vector2(BarW, BarH), true, topLeft);
            _barShine = MakeImage(_hud.transform, round, new Color(1f, 1f, 1f, 0.25f), topLeft, new Vector2(66f, -32f), new Vector2(BarW - 4f, 4f), true, topLeft);
            MakeImage(_hud.transform, ring, Config.Hex(0xffe2b0, 0.5f), topLeft, new Vector2(64f, -30f), new Vector2(BarW, BarH), true, topLeft);
            _mute = MakeText(_hud.transform, "", Res.Body, 18, Config.Hex(0xbfae95), topRight, new Vector2(-18f, -70f), TextAnchor.UpperRight, new Vector2(1f, 1f));

            // Hints and banners sit in the play area, centred.
            for (int i = 0; i < Level.Hints.Length; i++)
            {
                var h = MakeText(_hud.transform, Level.Hints[i].Text, Res.BodyItalic, 28, Config.Hex(0xf3e3c6), mid, C(640f, 214f));
                h.color = new Color(h.color.r, h.color.g, h.color.b, 0f);
                _hints.Add(h);
            }
            _banner = MakeText(_root, "", Res.BodyItalic, 32, cream, mid, C(640f, 120f));
            _banner.color = new Color(cream.r, cream.g, cream.b, 0f);
            _hud.alpha = 0f;

            // ---- title
            _title = Group("Title", _root);
            var title = MakeText(_title.transform, "The Last Lamplighter", Res.Title, 76, cream, mid, C(640f, 108f));
            Halo(title, Config.Hex(0xff9a3c, 0.35f));
            MakeText(_title.transform, "the gloom took the colour from the town. bring it back.", Res.BodyItalic, 28, Config.Hex(0xe6d5ba), mid,
                C(640f, 166f));
            _titlePrompt = MakeText(_title.transform, "press  Space  or click to begin", Res.BodyBold, 30, Config.Hex(0xffd28a), mid, C(640f, 720f * 0.84f));
            MakeText(_title.transform, "A D  or arrows  move      Space  jump      X  flare      M  mute", Res.Body, 22, Config.Hex(0xd8c9b0), mid,
                C(640f, 720f - 36f));
            MakeText(_title.transform, "art made with DreamLayer  ·  built in Unity", Res.BodyItalic, 18, Config.Hex(0x9f9384), topRight,
                new Vector2(-20f, -18f), TextAnchor.UpperRight, new Vector2(1f, 1f));
            _title.alpha = 0f;

            // ---- pause
            _pause = Group("Pause", _root);
            var pbg = MakeImage(_pause.transform, Res.Sprite("fx/white"), Config.Hex(0x05060c, 0.72f), mid, Vector2.zero, Vector2.one);
            Stretch(pbg.rectTransform);
            var paused = MakeText(_pause.transform, "Paused", Res.Title, 58, cream, mid, C(640f, 720f * 0.38f));
            Halo(paused, Config.Hex(0xff9a3c, 0.35f));
            var opts = MakeText(_pause.transform, "Esc  resume\nR  back to the last lamp\nQ  quit to title\nM  sound on / off", Res.Body, 30,
                Config.Hex(0xf0dfc2), mid, C(640f, 720f * 0.6f));
            opts.lineSpacing = 1.15f;
            opts.rectTransform.sizeDelta = new Vector2(1200f, 220f);
            _pause.alpha = 0f;

            // ---- end
            _end = Group("End", _root);
            var ebg = MakeImage(_end.transform, Res.Sprite("fx/white"), Config.Hex(0x05060c, 0.55f), mid, Vector2.zero, Vector2.one);
            Stretch(ebg.rectTransform);
            var awake = MakeText(_end.transform, "The town is awake", Res.Title, 64, cream, mid, C(640f, 720f * 0.27f));
            Halo(awake, Config.Hex(0xff9a3c, 0.35f));
            MakeText(_end.transform, "every lamp burns again, and the colour has come home.", Res.BodyItalic, 26, Config.Hex(0xe6d5ba), mid,
                C(640f, 720f * 0.27f + 64f));
            for (int i = 0; i < 4; i++)
                _endLines.Add(MakeText(_end.transform, "", Res.BodyBold, 28, Config.Hex(0xffd28a), mid, C(640f, 720f * 0.5f + i * 38f)));
            _endAgain = MakeText(_end.transform, "press  Space  to light it all again", Res.Body, 28, Config.Hex(0xfff1d6), mid, C(640f, 720f * 0.84f));
            MakeText(_end.transform, "art made with DreamLayer  ·  design & code by Nabeel Hassan", Res.BodyItalic, 20, Config.Hex(0xa99c88), mid,
                C(640f, 720f - 30f));
            _end.alpha = 0f;

            // ---- touch controls (phones and tablets)
            _touch = Group("Touch", _root);
            if (Input.touchSupported)
            {
                var bl = new Vector2(0f, 0f);
                var br = new Vector2(1f, 0f);
                AddButton("left", "<", bl, new Vector2(96f, 86f));
                AddButton("right", ">", bl, new Vector2(236f, 86f));
                AddButton("flare", "flare", br, new Vector2(-236f, 86f));
                AddButton("jump", "jump", br, new Vector2(-96f, 86f));
                AddButton("pause", "II", topRight, new Vector2(-40f, -110f), 30f);
            }
            _touch.alpha = 0f;

            // ---- fade overlay, always on top
            _fade = MakeImage(_root, Res.Sprite("fx/white"), Config.Ink, mid, Vector2.zero, Vector2.one);
            Stretch(_fade.rectTransform);
        }

        private void AddButton(string id, string label, Vector2 anchor, Vector2 pos, float radius = 58f)
        {
            var img = MakeImage(_touch.transform, Res.Sprite("fx/circle"), new Color(1f, 1f, 1f, 0.1f), anchor, pos, Vector2.one * radius * 2f);
            MakeImage(img.transform, Res.Sprite("fx/circle_ring"), Config.Hex(0xffe2b0, 0.45f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * radius * 2f);
            var t = MakeText(img.transform, label, Res.BodyBold, radius > 40f ? 30 : 22, Config.Hex(0xffe2b0, 0.85f), new Vector2(0.5f, 0.5f), Vector2.zero);
            t.rectTransform.sizeDelta = new Vector2(radius * 2f, radius * 2f);
            _buttons.Add((img.rectTransform, img, id));
        }

        // ------------------------------------------------------------------ public API

        public void Fade(float to, float seconds, Action onDone = null, Color? color = null)
        {
            _fadeTween?.Kill();
            if (color.HasValue)
            {
                var c = color.Value;
                _fade.color = new Color(c.r, c.g, c.b, _fade.color.a);
            }
            float from = _fade.color.a;
            _fadeTween = Tween.Value(from, to, seconds, a =>
            {
                var c = _fade.color;
                c.a = a;
                _fade.color = c;
            }, onComplete: onDone, unscaled: true);
        }

        public void ShowTitle(bool on)
        {
            _title.alpha = on ? 1f : 0f;
            Tween.KillOwner(_titlePrompt);
            if (on)
            {
                Tween.Value(1f, 0.35f, 1.1f, a => SetAlpha(_titlePrompt, a), Ease.SineInOut, owner: _titlePrompt, unscaled: true, yoyo: true, repeat: -1);
            }
        }

        public void ShowHud(int lamps)
        {
            foreach (var i in _lampIcons) Destroy(i.gameObject);
            _lampIcons.Clear();
            const float spacing = 30f;
            var topRight = new Vector2(1f, 1f);
            var plate = MakeImage(_hud.transform, Res.Sprite("fx/round_rect"), Config.Hex(0x05060c, 0.62f), topRight,
                new Vector2(-30f + 24f, -8f), new Vector2((lamps - 1) * spacing + 48f, 60f), true, new Vector2(1f, 1f));
            plate.transform.SetAsFirstSibling();
            _lampIcons.Add(plate); // destroyed with the icons on the next run
            foreach (var g in _lampGlows) Destroy(g.gameObject);
            _lampGlows.Clear();
            for (int i = 0; i < lamps; i++)
            {
                var pos = new Vector2(-30f - (lamps - 1 - i) * spacing, -38f);
                var glow = MakeImage(_hud.transform, Res.Sprite("fx/glow"), Config.Hex(0x8ea3d6, 0.35f), topRight, pos + new Vector2(0f, 14f), Vector2.one * 34f);
                _lampGlows.Add(glow);
                var icon = Fit(MakeImage(_hud.transform, Res.Sprite("props/lamp_unlit"), new Color(1f, 1f, 1f, 0.6f), topRight, pos, Vector2.one), 46f);
                _lampIcons.Add(icon);
            }
            _hud.alpha = 1f;
            _hudVisible = true;
            RefreshMute();
        }

        public void HideHud()
        {
            _hudVisible = false;
            _hud.alpha = 0f;
            _touch.alpha = 0f;
            foreach (var h in _hints) SetAlpha(h, 0f);
            SetAlpha(_banner, 0f);
        }

        public void SetFlame(float f) => _flame = f;

        public void SetLamps(bool[] lit)
        {
            for (int i = 0; i < lit.Length; i++)
            {
                if (i + 1 >= _lampIcons.Count) break;
                var icon = _lampIcons[i + 1];
                bool wasLit = icon.color.a > 0.99f;
                if (lit[i] == wasLit) continue;
                icon.sprite = Res.Sprite(lit[i] ? "props/lamp_lit" : "props/lamp_unlit");
                Fit(icon, 46f);
                icon.color = new Color(1f, 1f, 1f, lit[i] ? 1f : 0.6f);
                if (i < _lampGlows.Count)
                {
                    _lampGlows[i].color = lit[i] ? Config.Hex(0xffa648, 0.95f) : Config.Hex(0x8ea3d6, 0.35f);
                    _lampGlows[i].rectTransform.sizeDelta = Vector2.one * (lit[i] ? 52f : 34f);
                }
                if (lit[i])
                {
                    var rt = icon.rectTransform;
                    Tween.Run(0.22f, t => rt.localScale = Vector3.one * (1f + 0.4f * t), Ease.BackOut, owner: rt, unscaled: true, yoyo: true);
                }
            }
        }

        public void Banner(string msg, float hold = 2.6f)
        {
            _bannerTween?.Kill();
            Tween.KillOwner(_banner);
            _banner.text = msg;
            var rt = _banner.rectTransform;
            var basePos = C(640f, 120f);
            Tween.Run(0.4f, t =>
            {
                SetAlpha(_banner, t);
                rt.anchoredPosition = basePos + new Vector2(0f, -10f * (1f - t));
            }, Ease.SineOut, owner: _banner, unscaled: true);
            _bannerTween = Tween.Value(1f, 0f, 0.7f, a => SetAlpha(_banner, a), delay: hold, owner: _banner, unscaled: true);
        }

        /// <summary>Fades hint i in (or all out when -1), one at a time.</summary>
        public void UpdateHints(int active, float dt)
        {
            for (int i = 0; i < _hints.Count; i++)
            {
                float a = _hints[i].color.a;
                a += ((i == active ? 1f : 0f) - a) * Mathf.Min(1f, dt * 3f);
                SetAlpha(_hints[i], a);
            }
        }

        public void ShowPause(bool on) => _pause.alpha = on ? 1f : 0f;

        public void ShowEnd(string[] lines)
        {
            for (int i = 0; i < _endLines.Count; i++) _endLines[i].text = i < lines.Length ? lines[i] : "";
            Tween.Value(0f, 1f, 1.2f, a => _end.alpha = a, owner: _end, unscaled: true);
            Tween.Value(1f, 0.35f, 1f, a => SetAlpha(_endAgain, a), delay: 1.2f, owner: _endAgain, unscaled: true, yoyo: true, repeat: -1);
        }

        public void HideEnd()
        {
            Tween.KillOwner(_end);
            Tween.KillOwner(_endAgain);
            _end.alpha = 0f;
        }

        public void RefreshMute()
        {
            _mute.text = GameAudio.I != null && GameAudio.I.Muted ? "sound off  (M)" : "";
        }

        private static void SetAlpha(Graphic g, float a)
        {
            var c = g.color;
            c.a = a;
            g.color = c;
        }

        private void Update()
        {
            // Flame bar: shrinks with the flame, pulses red when it is nearly out.
            float f = Mathf.Clamp01(_flame);
            bool low = f < 0.25f;
            float pulse = low ? 0.55f + Mathf.Sin(Time.unscaledTime / 0.11f) * 0.35f : 1f;
            var col = low ? Config.Hex(0xff5a3c) : Config.Hex(0xffb35c);
            _barFill.enabled = _barShine.enabled = f > 0.01f;
            _barFill.color = new Color(col.r, col.g, col.b, pulse);
            _barFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(BarW * f, BarH), BarH);
            _barShine.color = new Color(1f, 1f, 1f, 0.25f * pulse);
            _barShine.rectTransform.sizeDelta = new Vector2(Mathf.Max(BarW * f - 4f, 6f), 4f);
            SetAlpha(_emberIcon, low ? pulse : 1f);

            ReadTouches();
        }

        private bool _hudVisible, _touchSeen;

        private void ReadTouches()
        {
            // Browsers on touch laptops report touch support too, so the buttons wait for a real touch.
            if (Input.touchCount > 0) _touchSeen = true;
            _touch.alpha = _hudVisible && _touchSeen && _buttons.Count > 0 ? 1f : 0f;
            if (_touch.alpha <= 0f) return;
            var now = new HashSet<string>();
            float scale = _canvas.scaleFactor;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;
                foreach (var (rt, _, id) in _buttons)
                {
                    float r = rt.sizeDelta.x * 0.5f * scale * 1.15f;
                    if (Vector2.Distance(t.position, rt.position) <= r) now.Add(id);
                }
            }
            foreach (var (_, img, id) in _buttons) img.color = new Color(1f, 1f, 1f, now.Contains(id) ? 0.25f : 0.1f);
            if (now.Contains("flare") && !_held.Contains("flare")) TouchFlare?.Invoke();
            if (now.Contains("pause") && !_held.Contains("pause")) TouchPause?.Invoke();
            TouchDir = (now.Contains("right") ? 1 : 0) - (now.Contains("left") ? 1 : 0);
            TouchJump = now.Contains("jump");
            _held.Clear();
            _held.UnionWith(now);
        }
    }
}
