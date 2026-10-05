using System;
using UnityEngine;

namespace Lamplighter
{
    public class Lamp
    {
        /// <summary>On-screen height of a lamp post, in units.</summary>
        private const float LampH = 1.9f;

        public bool Lit;
        public readonly bool Final;
        public readonly LightSource Light;
        public readonly Vector2 Base;
        private readonly SpriteRenderer _sprite, _glow;
        private readonly Emitter _flame;
        private readonly Transform _world;
        private readonly float _bob = UnityEngine.Random.value * 10f;

        public Lamp(Transform world, LampDef def, Reveal reveal)
        {
            _world = world;
            Final = def.Final;
            Base = Config.P(def.X, def.Y + 4);
            _sprite = Res.MakeSprite("lamp", Res.Sprite("props/lamp_unlit"), world, Config.Order.Lamps);
            FitHeight();

            var head = Config.P(def.X, def.Y - 190 * 0.86f);
            Light = reveal.Add(new LightSource(head, 0f, 0f));
            _glow = Res.MakeGlow(world, Config.Hex(0x8ea3d6, 0.14f), 0.35f);
            _glow.transform.position = head;
            _flame = Emitter.Create("lamp-flame", FxPresets.LampFlame(), world, head);

            // Unlit lamps still give off a faint cold shimmer so players can spot them in the gloom.
            if (Final)
            {
                _glow.color = Config.Hex(0x9fb6ff, 0.25f);
                _glow.transform.localScale = Vector3.one * 0.6f;
            }
        }

        private void FitHeight()
        {
            float h = _sprite.sprite.bounds.size.y;
            _sprite.transform.localScale = Vector3.one * (LampH / h);
            _sprite.transform.position = new Vector3(Base.x, Base.y + LampH / 2f, 0f);
        }

        public float X => Base.x;

        public void Ignite(Action onDone = null)
        {
            if (Lit) return;
            Lit = true;
            _sprite.sprite = Res.Sprite("props/lamp_lit");
            FitHeight();
            GameAudio.I.Ignite();
            _flame.Emitting = true;
            Emitter.Burst(FxPresets.IgniteBurst(), _world, Light.Pos, 46);

            var warm = Config.Hex(0xffa648);
            var startColor = _glow.color;
            float startScale = _glow.transform.localScale.x;
            Tween.Run(0.7f, t =>
            {
                _glow.color = new Color(warm.r, warm.g, warm.b, Mathf.Lerp(startColor.a, 0.75f, t));
                _glow.transform.localScale = Vector3.one * Mathf.Lerp(startScale, 1.25f, t);
            }, Ease.SineOut, owner: _glow);
            Tween.Value(0f, Config.LampRadius, 1.6f, r =>
            {
                Light.Radius = r;
                Light.Intensity = Mathf.Min(1f, r / 1.4f);
            }, Ease.BackOut, onComplete: onDone, owner: _sprite);
        }

        public void Update(float time)
        {
            if (!Lit)
            {
                var c = _glow.color;
                c.a = (Final ? 0.22f : 0.12f) + Mathf.Sin(time / 0.6f + _bob) * 0.05f;
                _glow.color = c;
                return;
            }
            float f = 1f + Mathf.Sin(time / 0.12f + _bob) * 0.025f + Mathf.Sin(time / 0.047f + _bob) * 0.015f;
            _glow.transform.localScale = Vector3.one * 1.25f * f;
        }
    }

    /// <summary>
    /// A scrap of living gloom. It is drawn to the warmth of the lantern, but steady lamplight
    /// unmakes it, so lit lamps are safe ground and a flare burns it away.
    /// </summary>
    public class Wisp
    {
        private const float WispH = 0.74f;
        private enum State { Drift, Hunt, Dead }

        private readonly SpriteRenderer _sprite;
        private readonly Emitter _trail;
        private readonly Vector2 _home;
        private readonly Reveal _reveal;
        private readonly Transform _world;
        private readonly float _baseScale;
        private State _state = State.Drift;
        private Vector2 _vel;
        private float _phase = UnityEngine.Random.value * Mathf.PI * 2f;
        private float _respawnAt;
        private bool _animating;

        public bool Alive => _state != State.Dead;
        /// <summary>Test hook: keeps burned wisps from re-forming.</summary>
        public static bool Calm;
        public Vector2 Pos => _sprite.transform.position;

        public Wisp(Transform world, Vector2 homePx, Reveal reveal)
        {
            _world = world;
            _home = Config.P(homePx.x, homePx.y);
            _reveal = reveal;
            _sprite = Res.MakeSprite("wisp", Res.Sprite("props/wisp"), world, Config.Order.Wisps);
            _baseScale = WispH / _sprite.sprite.bounds.size.y;
            _sprite.transform.localScale = Vector3.one * _baseScale;
            _sprite.transform.position = _home;
            _trail = Emitter.Create("wisp-trail", FxPresets.WispTrail(), world, _home);
            _trail.Follow = _sprite.transform;
            _trail.Emitting = true;
        }

        /// <summary>Returns true when it burns away.</summary>
        public bool Burn()
        {
            if (_state == State.Dead) return false;
            _state = State.Dead;
            _respawnAt = Time.time + Config.WispRespawnTime;
            GameAudio.I.Dissolve();
            _trail.Emitting = false;
            Emitter.Burst(FxPresets.WispPuff(), _world, Pos, 30);
            _animating = true;
            var t0 = _sprite.transform.localScale.x;
            Tween.Run(0.38f, t =>
            {
                _sprite.color = new Color(1f, 1f, 1f, 1f - t);
                _sprite.transform.localScale = Vector3.one * Mathf.Lerp(t0, _baseScale * 1.6f, t);
            }, Ease.CubicOut, onComplete: () =>
            {
                _sprite.enabled = false;
                _animating = false;
            }, owner: _sprite);
            return true;
        }

        public void Update(float dt, Player player)
        {
            var tr = _sprite.transform;
            if (_state == State.Dead)
            {
                // Only re-form somewhere still dark, and away from the player.
                if (!Calm && Time.time > _respawnAt && !_animating && _reveal.LightAt(_home, player.Light) < 0.15f &&
                    Vector2.Distance(player.Pos, _home) > 5f)
                {
                    _state = State.Drift;
                    tr.position = _home;
                    _sprite.enabled = true;
                    _animating = true;
                    Tween.Run(0.9f, t =>
                    {
                        _sprite.color = new Color(1f, 1f, 1f, t);
                        tr.localScale = Vector3.one * _baseScale * Mathf.Lerp(0.4f, 1f, t);
                    }, onComplete: () => _animating = false, owner: _sprite);
                    _trail.Emitting = true;
                }
                return;
            }

            // Steady lamplight (not the player's own lantern) unmakes a wisp.
            if (_reveal.LightAt(Pos, player.Light) > 0.35f)
            {
                Burn();
                return;
            }

            var target = player.Light.Pos;
            float dist = Vector2.Distance(Pos, target);
            bool playerSafe = _reveal.LightAt(target, player.Light) > 0.3f;
            _state = dist < Config.WispSenseRadius && !playerSafe && !player.Frozen ? State.Hunt : State.Drift;

            _phase += dt * 2.4f;
            Vector2 goal;
            float speed;
            if (_state == State.Hunt)
            {
                goal = target;
                speed = Config.WispSpeed * (1f + 0.25f * Mathf.Sin(_phase * 0.5f));
            }
            else
            {
                goal = _home + new Vector2(Mathf.Cos(_phase * 0.35f) * 0.6f, Mathf.Sin(_phase * 0.5f) * 0.3f);
                speed = Config.WispSpeed * 0.45f;
            }
            var to = goal - Pos;
            float ang = Mathf.Atan2(to.y, to.x);
            float steer = 3.2f * dt;
            _vel += (new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * speed - _vel) * steer;
            tr.position = (Vector2)tr.position + new Vector2(_vel.x, _vel.y + Mathf.Sin(_phase * 2f) * 0.18f) * dt;

            _sprite.flipX = _vel.x < 0f;
            tr.rotation = Quaternion.Euler(0f, 0f, -Mathf.Sin(_phase) * 0.08f * Mathf.Rad2Deg);
            if (!_animating)
            {
                tr.localScale = Vector3.one * _baseScale * (1f + Mathf.Sin(_phase * 1.7f) * 0.06f);
                _sprite.color = new Color(1f, 1f, 1f, _state == State.Hunt ? 1f : 0.85f);
            }
        }
    }

    /// <summary>A floating ember that tops up the lantern. Glows faintly so it can be found in the dark.</summary>
    public class Ember
    {
        private const float EmberH = 0.46f;
        public bool Taken;
        private readonly SpriteRenderer _sprite, _glow;
        private readonly LightSource _light;
        private readonly Reveal _reveal;
        private readonly Vector2 _pos;
        private float _t = UnityEngine.Random.value * 10f;

        public Vector2 Pos => _sprite.transform.position;

        public Ember(Transform world, Vector2 posPx, Reveal reveal)
        {
            _reveal = reveal;
            _pos = Config.P(posPx.x, posPx.y);
            _sprite = Res.MakeSprite("ember", Res.Sprite("props/ember"), world, Config.Order.Pickups);
            _sprite.transform.localScale = Vector3.one * (EmberH / _sprite.sprite.bounds.size.y);
            _sprite.transform.position = _pos;
            _glow = Res.MakeGlow(world, Config.Hex(0xff8a3c, 0.45f), 0.28f);
            _glow.transform.position = _pos;
            _light = reveal.Add(new LightSource(_pos, 0.7f, 0.5f));
        }

        public void Collect()
        {
            if (Taken) return;
            Taken = true;
            _reveal.Remove(_light);
            var s0 = _sprite.transform.localScale;
            var g0 = _glow.transform.localScale;
            var y0 = _sprite.transform.position.y;
            Tween.Run(0.32f, t =>
            {
                _sprite.transform.localScale = s0 * Mathf.Lerp(1f, 1.8f, t);
                _glow.transform.localScale = g0 * Mathf.Lerp(1f, 1.8f, t);
                float y = y0 + 0.24f * t;
                _sprite.transform.position = new Vector3(_pos.x, y, 0f);
                _glow.transform.position = new Vector3(_pos.x, y, 0f);
                _sprite.color = new Color(1f, 1f, 1f, 1f - t);
                var gc = _glow.color;
                gc.a = 0.45f * (1f - t);
                _glow.color = gc;
            }, onComplete: () =>
            {
                UnityEngine.Object.Destroy(_sprite.gameObject);
                UnityEngine.Object.Destroy(_glow.gameObject);
            }, owner: _sprite);
        }

        public void Update(float dt)
        {
            if (Taken) return;
            _t += dt;
            float y = _pos.y + Mathf.Sin(_t * 2.2f) * 0.06f;
            _sprite.transform.position = new Vector3(_pos.x, y, 0f);
            _glow.transform.position = new Vector3(_pos.x, y, 0f);
            _light.Pos = new Vector2(_pos.x, y);
            var gc = _glow.color;
            gc.a = 0.38f + Mathf.Sin(_t * 5f) * 0.08f;
            _glow.color = gc;
        }
    }
}
