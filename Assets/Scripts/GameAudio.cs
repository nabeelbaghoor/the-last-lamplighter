using UnityEngine;

namespace Lamplighter
{
    /// <summary>
    /// Plays the pre-rendered sound set (tools/synth_audio.py). A low drone and wind play in the
    /// gloom; a music-box melody grows denser as lamps are lit, so the score warms up with the town.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio I { get; private set; }

        private static readonly int[] Pentatonic = { 0, 2, 4, 7, 9 };
        private const float Beat = 60f / 76f / 2f; // eighth notes at 76 bpm
        private const float NoteVolume = 0.14f;   // level the notes were rendered at

        private AudioSource _droneCold, _droneWarm, _wind;
        private AudioSource[] _voices;
        private int _nextVoice;
        private float _warmth, _droneWarmth, _windLevel = 1f, _droneFade;
        private float _nextNoteTime;
        private int _beatIndex;
        private bool _started;
        private int _stepVariant;

        public bool Muted { get; private set; }

        public static GameAudio Create()
        {
            var go = new GameObject("Audio");
            DontDestroyOnLoad(go);
            go.AddComponent<AudioListener>();
            return go.AddComponent<GameAudio>();
        }

        private void Awake()
        {
            I = this;
            _droneCold = Loop("drone_cold");
            _droneWarm = Loop("drone_warm");
            _wind = Loop("wind");
            _voices = new AudioSource[16];
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i] = gameObject.AddComponent<AudioSource>();
                _voices[i].playOnAwake = false;
            }
        }

        private AudioSource Loop(string clip)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = Res.Clip(clip);
            s.loop = true;
            s.playOnAwake = false;
            s.volume = 0f;
            return s;
        }

        /// <summary>Drone, wind and the music-box scheduler. Safe to call repeatedly.</summary>
        public void StartAmbience()
        {
            if (_started) return;
            _started = true;
            _droneCold.Play();
            _droneWarm.Play();
            _wind.Play();
            _droneFade = 0f;
            _nextNoteTime = Time.unscaledTime + 1.5f;
        }

        /// <summary>0 = cold gloom, 1 = every lamp lit.</summary>
        public void SetWarmth(float w) => _warmth = Mathf.Clamp01(w);

        public void ToggleMute()
        {
            Muted = !Muted;
            AudioListener.volume = Muted ? 0f : 1f;
        }

        private void Update()
        {
            if (!_started) return;
            float dt = Time.unscaledDeltaTime;
            // Same time constants as the WebAudio setTargetAtTime calls.
            _droneWarmth += (_warmth - _droneWarmth) * (1f - Mathf.Exp(-dt / 1.5f));
            _windLevel += ((1f - _warmth * 0.7f) - _windLevel) * (1f - Mathf.Exp(-dt / 2f));
            _droneFade = Mathf.Min(1f, _droneFade + dt / 4f);
            _droneCold.volume = _droneFade * (1f - _droneWarmth);
            _droneWarm.volume = _droneFade * _droneWarmth;
            _wind.volume = _windLevel;

            while (_nextNoteTime < Time.unscaledTime)
            {
                PlayStep();
                _nextNoteTime += Beat;
                _beatIndex++;
            }
        }

        private void PlayStep()
        {
            float w = _warmth;
            float density = 0.08f + w * 0.55f;
            bool strong = _beatIndex % 8 == 0;
            if (!(strong || Random.value < density)) return;
            int octave = Random.value < 0.25f + w * 0.3f ? 12 : 0;
            int degree = Pentatonic[Random.Range(0, Pentatonic.Length)];
            Play("note_" + (degree + octave), (strong ? 0.14f : 0.08f) / NoteVolume);
            if (w > 0.5f && strong) Play("bass_" + (degree - 12), 0.06f / NoteVolume);
        }

        public void Play(string clip, float volume = 1f)
        {
            var c = Res.Clip(clip);
            if (c == null) return;
            var v = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            v.Stop();
            v.clip = c;
            v.volume = volume;
            v.Play();
        }

        public void Jump() => Play("jump");
        public void Land() => Play("land");
        public void Step() => Play("step_" + (_stepVariant = (_stepVariant + 1) % 3));
        public void Ignite() => Play("ignite");
        public void Pickup() => Play("pickup");
        public void Flare() => Play("flare");
        public void Dissolve() => Play("dissolve");
        public void Hurt() => Play("hurt");
        public void Extinguish() => Play("extinguish");
        public void Bell() => Play("bell");
        public void Denied() => Play("denied");
    }
}
