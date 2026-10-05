using System.Collections.Generic;
using UnityEngine;

namespace Lamplighter
{
    /// <summary>
    /// Settings for one kind of particle, mirroring the browser version's emitters
    /// (speeds in units/second, lifespans in seconds).
    /// </summary>
    public sealed class EmitterConfig
    {
        public string Texture = "fx/spark";
        public bool Additive;
        public Vector2 Life = new Vector2(0.5f, 1f);
        /// <summary>Radial speed range; used when SpeedX/SpeedY are not set.</summary>
        public Vector2 Speed;
        public Vector2? SpeedX, SpeedY;
        public float ScaleStart = 1f, ScaleEnd;
        /// <summary>If set, each particle gets a random size in this range for its whole life.</summary>
        public Vector2? ScaleRange;
        public float AlphaStart = 1f, AlphaEnd;
        /// <summary>Alpha follows sin(pi * t) * AlphaPeak instead of start/end, for fade-in-out motes.</summary>
        public float AlphaPeak;
        public Color[] Tints = { Color.white };
        /// <summary>Seconds between particles while emitting.</summary>
        public float Frequency = 0.1f;
        /// <summary>Spawn area (relative to the emitter): x/y ranges.</summary>
        public Vector2 AreaX, AreaY;
        public int Order = Config.Order.Fx;
    }

    /// <summary>
    /// A pooled sprite particle emitter. Particles live as children of a space transform: the world
    /// root for world effects, or the camera for screen-space effects (motes).
    /// </summary>
    public class Emitter : MonoBehaviour
    {
        private struct P
        {
            public SpriteRenderer Sr;
            public Vector2 Pos, Vel;
            public float Age, Life, Scale0, Scale1;
            public Color Tint;
        }

        public EmitterConfig Cfg;
        public bool Emitting;
        /// <summary>Follow this transform's position (wisp trails).</summary>
        public Transform Follow;
        public Vector2 FollowOffset;

        private Transform _space;
        private readonly List<P> _live = new List<P>();
        private readonly Stack<SpriteRenderer> _pool = new Stack<SpriteRenderer>();
        private float _timer;
        private Sprite _sprite;

        public static Emitter Create(string name, EmitterConfig cfg, Transform space, Vector2 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(space, false);
            go.transform.localPosition = localPos;
            var e = go.AddComponent<Emitter>();
            e.Cfg = cfg;
            e._space = space;
            e._sprite = Res.Sprite(cfg.Texture);
            return e;
        }

        public Vector2 LocalPos
        {
            get => transform.localPosition;
            set => transform.localPosition = value;
        }

        public void Explode(int count)
        {
            for (int i = 0; i < count; i++) Spawn();
        }

        public void ExplodeAt(int count, Vector2 localPos)
        {
            LocalPos = localPos;
            Explode(count);
        }

        private void Spawn()
        {
            var c = Cfg;
            var sr = _pool.Count > 0 ? _pool.Pop() : NewRenderer();
            sr.gameObject.SetActive(true);
            Vector2 vel;
            if (c.SpeedX.HasValue || c.SpeedY.HasValue)
            {
                vel = new Vector2(c.SpeedX.HasValue ? Random.Range(c.SpeedX.Value.x, c.SpeedX.Value.y) : 0f,
                                  c.SpeedY.HasValue ? Random.Range(c.SpeedY.Value.x, c.SpeedY.Value.y) : 0f);
            }
            else
            {
                float a = Random.value * Mathf.PI * 2f;
                vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(c.Speed.x, c.Speed.y);
            }
            float s0 = c.ScaleStart, s1 = c.ScaleEnd;
            if (c.ScaleRange.HasValue) s0 = s1 = Random.Range(c.ScaleRange.Value.x, c.ScaleRange.Value.y);
            var p = new P
            {
                Sr = sr,
                Pos = (Vector2)transform.localPosition + new Vector2(Random.Range(c.AreaX.x, c.AreaX.y), Random.Range(c.AreaY.x, c.AreaY.y)),
                Vel = vel,
                Life = Random.Range(c.Life.x, c.Life.y),
                Scale0 = s0,
                Scale1 = s1,
                Tint = c.Tints[Random.Range(0, c.Tints.Length)],
            };
            _live.Add(p);
            Apply(ref p);
        }

        private SpriteRenderer NewRenderer()
        {
            var sr = Res.MakeSprite("p", _sprite, _space, Cfg.Order);
            if (Cfg.Additive) sr.sharedMaterial = Res.Additive;
            return sr;
        }

        private void Apply(ref P p)
        {
            float t = Mathf.Clamp01(p.Age / p.Life);
            float s = Mathf.Lerp(p.Scale0, p.Scale1, t);
            float a = Cfg.AlphaPeak > 0f ? Mathf.Sin(t * Mathf.PI) * Cfg.AlphaPeak : Mathf.Lerp(Cfg.AlphaStart, Cfg.AlphaEnd, t);
            p.Sr.transform.localPosition = p.Pos;
            p.Sr.transform.localScale = new Vector3(s, s, 1f);
            var col = p.Tint;
            col.a = a;
            p.Sr.color = col;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (Follow != null) transform.position = (Vector2)Follow.position + FollowOffset;
            if (Emitting && Cfg.Frequency > 0f)
            {
                _timer -= dt;
                while (_timer <= 0f)
                {
                    Spawn();
                    _timer += Cfg.Frequency;
                }
            }
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var p = _live[i];
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    p.Sr.gameObject.SetActive(false);
                    _pool.Push(p.Sr);
                    _live.RemoveAt(i);
                    continue;
                }
                p.Pos += p.Vel * dt;
                Apply(ref p);
                _live[i] = p;
            }
        }

        public void Clear()
        {
            foreach (var p in _live)
            {
                p.Sr.gameObject.SetActive(false);
                _pool.Push(p.Sr);
            }
            _live.Clear();
        }

        /// <summary>Stops emitting and destroys the emitter once its last particle has faded.</summary>
        public void DestroyWhenDone()
        {
            Emitting = false;
            _destroyWhenDone = true;
        }

        private bool _destroyWhenDone;

        private void LateUpdate()
        {
            if (_destroyWhenDone && _live.Count == 0) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            foreach (var p in _live) if (p.Sr) Destroy(p.Sr.gameObject);
            while (_pool.Count > 0)
            {
                var sr = _pool.Pop();
                if (sr) Destroy(sr.gameObject);
            }
        }

        /// <summary>A one-shot burst that cleans itself up.</summary>
        public static void Burst(EmitterConfig cfg, Transform space, Vector2 pos, int count)
        {
            var e = Create("burst", cfg, space, pos);
            e.Explode(count);
            e.DestroyWhenDone();
        }
    }

    /// <summary>The particle looks used across the game, converted from the browser version.</summary>
    public static class FxPresets
    {
        private static Color H(uint c) => Config.Hex(c);

        public static EmitterConfig Dust() => new EmitterConfig
        {
            Texture = "fx/smoke", Life = new Vector2(0.3f, 0.55f), SpeedX = new Vector2(-0.7f, 0.7f), SpeedY = new Vector2(0.08f, 0.45f),
            ScaleStart = 0.42f, ScaleEnd = 0f, AlphaStart = 0.45f, AlphaEnd = 0f, Tints = new[] { H(0xbfae95), H(0x8f8270) },
            Order = Config.Order.Dust,
        };

        public static EmitterConfig LampFlame() => new EmitterConfig
        {
            Texture = "fx/spark", Additive = true, Life = new Vector2(0.5f, 1.1f), SpeedY = new Vector2(0.2f, 0.6f), SpeedX = new Vector2(-0.14f, 0.14f),
            ScaleStart = 0.55f, ScaleEnd = 0f, AlphaStart = 0.9f, AlphaEnd = 0f, Tints = new[] { H(0xffd27a), H(0xff9a3c), H(0xffeec2) },
            Frequency = 0.09f,
        };

        public static EmitterConfig IgniteBurst() => new EmitterConfig
        {
            Texture = "fx/spark", Additive = true, Life = new Vector2(0.6f, 1.4f), Speed = new Vector2(0.8f, 2.6f),
            ScaleStart = 0.8f, ScaleEnd = 0f, AlphaStart = 1f, AlphaEnd = 0f, Tints = new[] { H(0xffd27a), H(0xff9a3c), Color.white },
        };

        public static EmitterConfig WispTrail() => new EmitterConfig
        {
            Texture = "fx/smoke", Life = new Vector2(0.5f, 0.9f), Speed = new Vector2(0.04f, 0.22f),
            ScaleStart = 0.5f, ScaleEnd = 0.05f, AlphaStart = 0.55f, AlphaEnd = 0f, Tints = new[] { H(0x120b24), H(0x2a1d4a), H(0x05040c) },
            Frequency = 0.045f, Order = Config.Order.WispTrail,
        };

        public static EmitterConfig WispPuff() => new EmitterConfig
        {
            Texture = "fx/spark", Additive = true, Life = new Vector2(0.4f, 0.9f), Speed = new Vector2(0.4f, 1.8f),
            ScaleStart = 0.6f, ScaleEnd = 0f, AlphaStart = 1f, AlphaEnd = 0f, Tints = new[] { H(0xb59cff), H(0xffe2a8), H(0x7a5cff) },
        };

        /// <summary>Drifting ash over the play view (screen space, so it lives under the camera).</summary>
        public static EmitterConfig Ash() => new EmitterConfig
        {
            Texture = "fx/mote", Life = new Vector2(7f, 7f), SpeedY = new Vector2(-0.22f, -0.06f), SpeedX = new Vector2(-0.14f, 0.04f),
            ScaleRange = new Vector2(0.2f, 0.55f), AlphaPeak = 0.5f, Tints = new[] { H(0x8d97b8), H(0x5e6788) },
            Frequency = 0.09f, AreaX = new Vector2(-Config.ViewW / 2f, Config.ViewW / 2f), AreaY = new Vector2(-Config.ViewH / 2f, Config.ViewH / 2f + 0.2f),
            Order = Config.Order.Motes,
        };

        /// <summary>Warm embers rising behind the title.</summary>
        public static EmitterConfig TitleEmbers() => new EmitterConfig
        {
            Texture = "fx/mote", Additive = true, Life = new Vector2(9f, 9f), SpeedY = new Vector2(0.15f, 0.4f), SpeedX = new Vector2(-0.08f, 0.08f),
            ScaleRange = new Vector2(0.3f, 0.9f), AlphaStart = 0.9f, AlphaEnd = 0f, Tints = new[] { H(0xffc27a), H(0xffe7b8), H(0xff9a4a) },
            Frequency = 0.16f, AreaX = new Vector2(-Config.ViewW / 2f, Config.ViewW / 2f), AreaY = new Vector2(-Config.ViewH / 2f - 0.1f, -Config.ViewH / 2f - 0.1f),
            Order = Config.Order.Motes,
        };
    }
}
