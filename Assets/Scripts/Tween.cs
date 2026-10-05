using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lamplighter
{
    public static class Ease
    {
        public static float Linear(float t) => t;
        public static float SineIn(float t) => 1f - Mathf.Cos(t * Mathf.PI / 2f);
        public static float SineOut(float t) => Mathf.Sin(t * Mathf.PI / 2f);
        public static float SineInOut(float t) => -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;
        public static float CubicOut(float t) => 1f - Mathf.Pow(1f - t, 3f);

        public static float BackOut(float t)
        {
            const float s = 1.70158f;
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }
    }

    /// <summary>
    /// A small tween runner: every tween drives one float from 0 to 1 and hands it to a callback.
    /// Tweens can be tied to an owner object (killed with it) and can run on unscaled time (UI, pause).
    /// </summary>
    public class Tween : MonoBehaviour
    {
        public sealed class Handle
        {
            internal float Delay, Duration, Elapsed;
            internal Func<float, float> Ease;
            internal Action<float> OnUpdate;
            internal Action OnComplete;
            internal bool Unscaled, Yoyo, Reversing;
            internal int Repeat; // -1 = forever
            internal object Owner;
            public bool Done { get; internal set; }
            public void Kill() => Done = true;
        }

        private static Tween _runner;
        private readonly List<Handle> _tweens = new List<Handle>();
        private readonly List<Handle> _adding = new List<Handle>();

        private static Tween Runner
        {
            get
            {
                if (_runner == null)
                {
                    var go = new GameObject("Tweens");
                    DontDestroyOnLoad(go);
                    _runner = go.AddComponent<Tween>();
                }
                return _runner;
            }
        }

        public static Handle Run(float duration, Action<float> onUpdate, Func<float, float> ease = null, float delay = 0f,
            Action onComplete = null, object owner = null, bool unscaled = false, bool yoyo = false, int repeat = 0)
        {
            var h = new Handle
            {
                Duration = Mathf.Max(0.0001f, duration), OnUpdate = onUpdate, Ease = ease ?? Lamplighter.Ease.Linear,
                Delay = delay, OnComplete = onComplete, Owner = owner, Unscaled = unscaled, Yoyo = yoyo, Repeat = repeat,
            };
            Runner._adding.Add(h);
            return h;
        }

        /// <summary>Tween from a to b.</summary>
        public static Handle Value(float a, float b, float duration, Action<float> set, Func<float, float> ease = null, float delay = 0f,
            Action onComplete = null, object owner = null, bool unscaled = false, bool yoyo = false, int repeat = 0) =>
            Run(duration, t => set(a + (b - a) * t), ease, delay, onComplete, owner, unscaled, yoyo, repeat);

        public static Handle Delay(float seconds, Action action, object owner = null, bool unscaled = false) =>
            Run(0.0001f, _ => { }, null, seconds, action, owner, unscaled);

        public static void KillOwner(object owner)
        {
            if (_runner == null || owner == null) return;
            foreach (var t in _runner._tweens) if (ReferenceEquals(t.Owner, owner)) t.Done = true;
            foreach (var t in _runner._adding) if (ReferenceEquals(t.Owner, owner)) t.Done = true;
        }

        public static void KillAll()
        {
            if (_runner == null) return;
            foreach (var t in _runner._tweens) t.Done = true;
            _runner._adding.Clear();
        }

        private void Update()
        {
            if (_adding.Count > 0)
            {
                _tweens.AddRange(_adding);
                _adding.Clear();
            }
            for (int i = 0; i < _tweens.Count; i++)
            {
                var t = _tweens[i];
                if (t.Done) continue;
                if (t.Owner is UnityEngine.Object uo && uo == null) { t.Done = true; continue; }
                float dt = t.Unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                if (t.Delay > 0f)
                {
                    t.Delay -= dt;
                    if (t.Delay > 0f) continue;
                    dt = -t.Delay;
                }
                t.Elapsed += dt;
                float p = Mathf.Clamp01(t.Elapsed / t.Duration);
                float k = t.Ease(t.Reversing ? 1f - p : p);
                try { t.OnUpdate?.Invoke(k); }
                catch (Exception e) { Debug.LogException(e); t.Done = true; continue; }
                if (p < 1f) continue;

                if (t.Yoyo && !t.Reversing)
                {
                    t.Reversing = true; // play back down to the start value
                    t.Elapsed = 0f;
                    continue;
                }
                t.Reversing = false;
                if (t.Repeat != 0)
                {
                    if (t.Repeat > 0) t.Repeat--;
                    t.Elapsed = 0f;
                    continue;
                }
                t.Done = true;
                try { t.OnComplete?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            _tweens.RemoveAll(t => t.Done);
        }
    }
}
