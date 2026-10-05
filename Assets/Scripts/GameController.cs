using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Lamplighter
{
    /// <summary>
    /// Boots the game and runs it: title, play, the dawn ending and the end card.
    /// Everything is created from code and Resources, so the only scene is an empty one.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        private enum Mode { Title, Playing, Ending, End }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindObjectOfType<GameController>() != null) return;
            // Named "Game" so the WebGL page (and tests) can reach it with SendMessage("Game", "Cmd", ...).
            new GameObject("Game").AddComponent<GameController>();
        }

        private Mode _mode;
        private CameraRig _rig;
        private Hud _hud;
        private Reveal _reveal;
        private Transform _world, _screenFx;
        private Player _player;
        private readonly List<Lamp> _lamps = new List<Lamp>();
        private readonly List<Wisp> _wisps = new List<Wisp>();
        private readonly List<Ember> _embers = new List<Ember>();
        private Vector2 _checkpoint;
        private bool _dying, _paused, _starting, _endReady;
        private float _lastDeniedAt = -99f, _lowWarnedAt = -99f, _lookAhead, _hudTimer, _hitStopUntil;
        private float _startedAt, _finishedAt;
        private int _deaths, _wispsBurned, _embersTaken;
        private bool _god;

        private void Start()
        {
            Application.targetFrameRate = -1;
            string url = Application.absoluteURL ?? "";
            // `timer` drives the WebGL loop with setTimeout instead of requestAnimationFrame, so the
            // game keeps running in hidden test browsers. `god` keeps the lantern full for captures.
            if (url.Contains("timer")) Application.targetFrameRate = 60;
            _god = url.Contains("god");

            Physics2D.gravity = new Vector2(0f, -Config.Gravity);
            Time.fixedDeltaTime = 1f / 60f;
            QualitySettings.vSyncCount = 0;

            _rig = CameraRig.Create();
            GameAudio.Create();
            _hud = Hud.Create();
            _hud.TouchFlare += () => { if (_mode == Mode.Playing && !_paused && !_dying) _player.TryFlare(); };
            _hud.TouchPause += () => { if (_mode == Mode.Playing && !_dying) TogglePause(); };
            ShowTitle();
        }

        // ------------------------------------------------------------------ title

        private Transform _titleRoot;

        private void ShowTitle()
        {
            Cleanup();
            _mode = Mode.Title;
            _starting = false;
            _rig.SnapTo(Config.ViewW / 2f);

            _titleRoot = new GameObject("TitleScreen").transform;
            _titleRoot.SetParent(_rig.Screen, false);
            var art = Res.MakeSprite("title_art", Res.Sprite("ui/title_art"), _titleRoot, 0);
            var size = art.sprite.bounds.size;
            float s = Mathf.Max(Config.ViewW / size.x, Config.ViewH / size.y) * 1.04f;
            art.transform.localScale = Vector3.one * s;
            Tween.Value(s, s * 1.05f, 16f, v => art.transform.localScale = Vector3.one * v, Ease.SineInOut, owner: art, yoyo: true, repeat: -1);
            var shade = Res.MakeSprite("shade", Res.Sprite("fx/shade"), _titleRoot, 1);
            var ss = shade.sprite.bounds.size;
            shade.transform.localScale = new Vector3(Config.ViewW * 1.2f / ss.x, Config.ViewH / ss.y, 1f);
            var embers = Emitter.Create("embers", FxPresets.TitleEmbers(), _titleRoot, Vector2.zero);
            embers.Emitting = true;

            _hud.HideHud();
            _hud.HideEnd();
            _hud.ShowTitle(true);
            _hud.Fade(0f, 0.9f, color: Config.Ink);
        }

        private void BeginFromTitle()
        {
            if (_starting) return;
            _starting = true;
            GameAudio.I.StartAmbience();
            _hud.Fade(1f, 0.7f, StartGame, Config.Ink);
        }

        // ------------------------------------------------------------------ play

        private void Cleanup()
        {
            Time.timeScale = 1f;
            _paused = false;
            _hud.ShowPause(false);
            if (_titleRoot) Destroy(_titleRoot.gameObject);
            if (_world) Destroy(_world.gameObject);
            if (_screenFx) Destroy(_screenFx.gameObject);
            if (_reveal) Destroy(_reveal.gameObject);
            _rig.ClearParallax();
            _rig.Target = null;
            _lamps.Clear();
            _wisps.Clear();
            _embers.Clear();
            _player = null;
        }

        private void StartGame()
        {
            Cleanup();
            _hud.ShowTitle(false);
            _mode = Mode.Playing;
            _dying = false;
            _endReady = false;
            _checkpoint = Config.P(Level.PlayerStart.x, Level.PlayerStart.y);
            _startedAt = Time.time;
            _deaths = _wispsBurned = _embersTaken = 0;

            _rig.SnapTo(Config.ViewW / 2f);
            _rig.Following = true;
            _world = World.Build(_rig);
            _reveal = Reveal.Create(_rig.Cam, _rig.Screen);

            foreach (var def in Level.Lamps) _lamps.Add(new Lamp(_world, def, _reveal));
            foreach (var p in Level.Pickups) _embers.Add(new Ember(_world, p, _reveal));
            foreach (var w in Level.Wisps) _wisps.Add(new Wisp(_world, w, _reveal));

            _player = Player.Create(_world, _checkpoint, _rig);
            _reveal.Add(_player.Light);
            _rig.Target = _player.transform;

            // Screen-space atmosphere: drifting ash and a vignette.
            _screenFx = new GameObject("ScreenFx").transform;
            _screenFx.SetParent(_rig.Screen, false);
            Emitter.Create("ash", FxPresets.Ash(), _screenFx, Vector2.zero).Emitting = true;
            var vig = Res.MakeSprite("vignette", Res.Sprite("fx/vignette"), _screenFx, Config.Order.Vignette);
            var vs = vig.sprite.bounds.size;
            vig.transform.localScale = new Vector3(Config.ViewW * 1.25f / vs.x, Config.ViewH / vs.y, 1f);

            GameAudio.I.SetWarmth(0f);
            _hud.ShowHud(_lamps.Count);
            _hud.SetLamps(_lamps.Select(l => l.Lit).ToArray());
            _hud.Fade(0f, 0.9f);
        }

        private bool NearLitLamp(Vector2 p) =>
            _lamps.Any(l => l.Lit && Vector2.Distance(p, l.Light.Pos) < Config.LampRadius * 0.78f);

        private void TryLightLamps()
        {
            var p = _player.Pos;
            foreach (var lamp in _lamps)
            {
                if (lamp.Lit) continue;
                bool near = Mathf.Abs(p.x - lamp.Base.x) < 0.7f && Mathf.Abs(p.y - lamp.Base.y) < 0.7f;
                if (!near) continue;

                if (lamp.Final)
                {
                    int remaining = _lamps.Count(l => !l.Lit && !l.Final);
                    if (remaining > 0)
                    {
                        if (Time.time - _lastDeniedAt > 3.5f)
                        {
                            _lastDeniedAt = Time.time;
                            GameAudio.I.Denied();
                            _hud.Banner(remaining == 1 ? "One lamp still sleeps in the dark" : $"{remaining} lamps still sleep in the dark");
                        }
                        continue;
                    }
                }

                SparkTo(lamp);
                lamp.Ignite();
                _hud.SetLamps(_lamps.Select(l => l.Lit).ToArray());
                HitStop(0.09f);
                WarmBloom();
                _rig.Shake(0.16f, 0.003f);
                _checkpoint = lamp.Base + new Vector2(-0.7f, 0.9f);
                _player.Flame = Mathf.Min(1f, _player.Flame + 0.25f);
                if (lamp.Final) BeginDawn();
                else
                {
                    int lit = _lamps.Count(l => l.Lit);
                    GameAudio.I.SetWarmth((float)lit / _lamps.Count);
                    if (lit - 1 < Level.LampBanners.Length) _hud.Banner(Level.LampBanners[lit - 1]);
                }
            }
        }

        private void SparkTo(Lamp lamp)
        {
            var spark = Res.MakeGlow(_world, Config.Hex(0xffd27a), 0.18f);
            Vector2 from = _player.Light.Pos, to = lamp.Light.Pos;
            spark.transform.position = from;
            Tween.Run(0.26f, t => spark.transform.position = Vector2.LerpUnclamped(from, to, t), Ease.SineIn,
                onComplete: () => Destroy(spark.gameObject), owner: spark);
        }

        /// <summary>A soft additive wash of warm light, gentler than a camera flash.</summary>
        private void WarmBloom()
        {
            var r = Res.MakeSprite("bloom", Res.Sprite("fx/white"), _screenFx, Config.Order.Bloom);
            r.sharedMaterial = Res.Additive;
            var size = r.sprite.bounds.size;
            r.transform.localScale = new Vector3(Config.ViewW * 1.3f / size.x, Config.ViewH * 1.1f / size.y, 1f);
            var c = Config.Hex(0xffb35c, 0.32f);
            r.color = c;
            Tween.Value(0.32f, 0f, 0.52f, a => r.color = new Color(c.r, c.g, c.b, a), Ease.SineOut,
                onComplete: () => Destroy(r.gameObject), owner: r);
        }

        /// <summary>A tiny freeze-frame sells the moment of ignition.</summary>
        private void HitStop(float seconds)
        {
            _hitStopUntil = Time.unscaledTime + seconds;
            if (!_paused) Time.timeScale = 0f;
        }

        private void KillPlayer(string reason)
        {
            if (_dying || _mode != Mode.Playing) return;
            _dying = true;
            if (reason != "restart") _deaths++;
            _player.Frozen = true;
            _player.Stop();
            if (reason == "gloom") GameAudio.I.Extinguish();
            if (reason == "fall") GameAudio.I.Hurt();
            var dark = new Color32(3, 4, 10, 255);
            _hud.Fade(1f, 0.65f, () =>
            {
                if (_player == null) return;
                if (reason == "gloom" || reason == "fall") _player.Flame = 0f;
                _player.Respawn(_checkpoint);
                _rig.SnapTo(_checkpoint.x);
                _hud.Fade(0f, 0.65f);
                _dying = false;
                if (reason == "gloom") _hud.Banner("The gloom swallowed your flame.  The last lamp rekindles it");
            }, dark);
        }

        private void BeginDawn()
        {
            _mode = Mode.Ending;
            _finishedAt = Time.time;
            _player.Frozen = true;
            _player.Stop(horizontalOnly: true);
            GameAudio.I.SetWarmth(1f);
            Tween.Delay(0.6f, () => GameAudio.I.Bell(), this);
            _hud.Banner("The town wakes");
            foreach (var w in _wisps) if (w.Burn()) _wispsBurned++;

            var reveal = _reveal;
            Tween.Value(0f, 1f, 4.2f, v => { if (reveal) reveal.FullReveal = v; }, Ease.SineInOut, delay: 0.9f, owner: reveal);
            Tween.Delay(1.4f, () => _rig.PanTo(Config.WorldW * 0.42f, 6.5f), this);
            Tween.Delay(8.4f, ShowEnd, this);
        }

        private static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private void ShowEnd()
        {
            if (_mode != Mode.Ending) return;
            _mode = Mode.End;
            _hud.ShowEnd(new[]
            {
                $"time   {FormatTime(_finishedAt - _startedAt)}",
                $"embers gathered   {_embersTaken}",
                $"wisps burned away   {_wispsBurned}",
                $"times the gloom took you   {_deaths}",
            });
            Tween.Delay(1.5f, () => _endReady = true, this);
        }

        private void TogglePause()
        {
            _paused = !_paused;
            Time.timeScale = _paused ? 0f : 1f;
            _hud.ShowPause(_paused);
        }

        // ------------------------------------------------------------------ loop

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.M))
            {
                GameAudio.I.ToggleMute();
                _hud.RefreshMute();
            }

            if (_hitStopUntil > 0f && Time.unscaledTime >= _hitStopUntil)
            {
                _hitStopUntil = 0f;
                if (!_paused) Time.timeScale = 1f;
            }

            switch (_mode)
            {
                case Mode.Title:
                    if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0)) BeginFromTitle();
                    return;
                case Mode.End:
                    if (_endReady && (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)))
                    {
                        _endReady = false;
                        _hud.Fade(1f, 0.7f, ShowTitle, Config.Ink);
                    }
                    break;
            }
            if (_player == null) return;

            if (_mode == Mode.Playing && !_dying)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) TogglePause();
                if (_paused)
                {
                    if (Input.GetKeyDown(KeyCode.R))
                    {
                        TogglePause();
                        KillPlayer("restart");
                    }
                    else if (Input.GetKeyDown(KeyCode.Q))
                    {
                        Time.timeScale = 1f;
                        _hud.Fade(1f, 0.5f, ShowTitle, Config.Ink);
                        _mode = Mode.Ending; // stop input while fading
                    }
                    return;
                }
                if (Input.GetKeyDown(KeyCode.R)) KillPlayer("restart");
            }
            if (_paused || Time.timeScale <= 0f) return;

            float dt = Time.deltaTime;
            var p = _player;
            p.SetTouch(_hud.TouchDir, _hud.TouchJump);

            if (_mode == Mode.Playing && !_dying)
            {
                // The lantern burns down in the dark and is fed by lamplight.
                if (NearLitLamp(p.Light.Pos)) p.Flame = Mathf.Min(1f, p.Flame + Config.FlameRefillPerSec * dt);
                else p.Flame = Mathf.Max(0f, p.Flame - Config.FlameDrainPerSec * dt);
                if (_god) p.Flame = Mathf.Max(p.Flame, 0.9f);

                if (p.Flame <= 0f) KillPlayer("gloom");
                else if (p.Flame < 0.22f && Time.time - _lowWarnedAt > 14f)
                {
                    _lowWarnedAt = Time.time;
                    _hud.Banner("Your flame is guttering.  Find lamplight or an ember");
                }
                if (p.Pos.y < -0.8f)
                {
                    p.Flame = Mathf.Max(0f, p.Flame - 0.2f);
                    KillPlayer("fall");
                }

                TryLightLamps();

                foreach (var e in _embers)
                {
                    if (!e.Taken && Vector2.Distance(p.Pos + new Vector2(0f, 0.5f), e.Pos) < 0.56f)
                    {
                        e.Collect();
                        p.Flame = Mathf.Min(1f, p.Flame + Config.PickupFlame);
                        _embersTaken++;
                        GameAudio.I.Pickup();
                    }
                }
            }

            float t = Time.time;
            foreach (var l in _lamps) l.Update(t);
            foreach (var e in _embers) e.Update(dt);
            foreach (var w in _wisps)
            {
                bool wasAlive = w.Alive;
                w.Update(dt, p);
                if (wasAlive && !w.Alive) _wispsBurned++;
                if (!w.Alive || _mode != Mode.Playing || _dying) continue;
                if (p.FlareRadius > 0f && Vector2.Distance(w.Pos, p.Light.Pos) < p.FlareRadius)
                {
                    if (w.Burn()) _wispsBurned++;
                    continue;
                }
                if (Vector2.Distance(w.Pos, p.Pos + new Vector2(0f, 0.55f)) < 0.48f) p.Hurt(w.Pos.x);
            }

            // One hint at a time: the zone the player is in.
            int active = -1;
            for (int i = 0; i < Level.Hints.Length; i++) if (p.Pos.x * 100f >= Level.Hints[i].X) active = i;
            if (active >= 0 && (_mode != Mode.Playing || p.Pos.x * 100f >= Level.Hints[active].X + 760f)) active = -1;
            _hud.UpdateHints(active, dt);

            // The camera leads slightly in the direction of travel.
            float targetLook = _mode == Mode.Playing ? p.MoveDir * 1.1f : 0f;
            _lookAhead += (targetLook - _lookAhead) * Mathf.Min(1f, dt * 1.6f);
            _rig.LookAhead = _lookAhead;

            _hudTimer -= dt;
            if (_hudTimer <= 0f)
            {
                _hudTimer = 0.08f;
                _hud.SetFlame(p.Flame);
            }
        }

        // ------------------------------------------------------------------ test bridge

        /// <summary>
        /// Commands from the page, e.g. unityInstance.SendMessage("Game", "Cmd", "warp 3800").
        /// Used by the automated playtest; results are printed to the browser console.
        /// </summary>
        public void Cmd(string command)
        {
            var parts = command.Split(' ');
            switch (parts[0])
            {
                case "start":
                    if (_mode == Mode.Title) BeginFromTitle();
                    break;
                case "god":
                    _god = !_god;
                    break;
                case "warp" when _player != null && parts.Length > 1:
                    float x = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) / 100f;
                    float y = parts.Length > 2 ? (720f - float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)) / 100f : 2.5f;
                    _player.Respawn(new Vector2(x, y));
                    _rig.SnapTo(x);
                    break;
                case "light" when _player != null && parts.Length > 1:
                    int idx = int.Parse(parts[1]);
                    if (idx >= 0 && idx < _lamps.Count)
                    {
                        var lamp = _lamps[idx];
                        _player.Respawn(lamp.Base + new Vector2(-0.2f, 0.05f));
                        _rig.SnapTo(lamp.Base.x);
                    }
                    break;
                case "flame" when _player != null && parts.Length > 1:
                    _player.Flame = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }
            Debug.Log("LL_STATE " + StateJson());
        }

        private string StateJson()
        {
            string Num(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var lit = string.Join(",", _lamps.Select(l => l.Lit ? "1" : "0"));
            var pos = _player != null ? _player.Pos : Vector2.zero;
            return "{" +
                   $"\"mode\":\"{_mode}\",\"paused\":{(_paused ? "true" : "false")},\"dying\":{(_dying ? "true" : "false")}," +
                   $"\"x\":{Num(pos.x * 100f)},\"y\":{Num(720f - pos.y * 100f)},\"flame\":{Num(_player != null ? _player.Flame : 0f)}," +
                   $"\"lit\":[{lit}],\"wispsAlive\":{_wisps.Count(w => w.Alive)},\"embers\":{_embersTaken},\"deaths\":{_deaths}," +
                   $"\"burned\":{_wispsBurned},\"fps\":{Num(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime))},\"timeScale\":{Num(Time.timeScale)}" +
                   "}";
        }
    }
}
