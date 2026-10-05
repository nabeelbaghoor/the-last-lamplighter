"""Renders every sound in The Last Lamplighter to WAV.

The browser version synthesizes audio live with WebAudio. Unity's WebGL player cannot run
custom DSP (no OnAudioFilterRead, no reverb filters), so the same graph is rendered offline
here: same oscillators, same filter sweeps, same envelopes, with the reverb baked in.
Ambience loops are rendered periodically (integer cycles, circular filtering and reverb)
so they loop without a seam.
"""
from pathlib import Path

import numpy as np
from scipy.io import wavfile
from scipy.signal import lfilter, lfilter_zi

SR = 44100
OUT = Path(__file__).resolve().parent.parent / "Assets" / "Resources" / "Audio"
OUT.mkdir(parents=True, exist_ok=True)
rng = np.random.default_rng(7)

MASTER, MUSIC, SFX, WET = 0.9, 0.55, 0.7, 0.55
ROOT_HZ = 293.66  # D4


def hz(semitones):
    return ROOT_HZ * 2 ** (semitones / 12)


# ---------------------------------------------------------------- building blocks

def exp_ramp(v0, v1, n):
    return v0 * (v1 / v0) ** (np.arange(n) / max(1, n - 1))


def osc(kind, freq, n, phase0=0.0):
    """freq may be a scalar or a per-sample array."""
    f = np.broadcast_to(np.asarray(freq, float), (n,))
    ph = (phase0 + np.cumsum(f) / SR) % 1.0
    if kind == "sine":
        return np.sin(2 * np.pi * ph)
    if kind == "triangle":
        return 4 * np.abs(ph - 0.5) - 1
    if kind == "sawtooth":
        return 2 * ph - 1
    raise ValueError(kind)


def biquad_coeffs(kind, f0, q):
    w0 = 2 * np.pi * min(f0, SR * 0.45) / SR
    alpha = np.sin(w0) / (2 * q)
    c = np.cos(w0)
    if kind == "lowpass":
        b = [(1 - c) / 2, 1 - c, (1 - c) / 2]
    elif kind == "bandpass":  # constant 0 dB peak gain, like WebAudio's bandpass
        b = [alpha, 0, -alpha]
    else:
        raise ValueError(kind)
    a = [1 + alpha, -2 * c, 1 - alpha]
    return np.array(b) / a[0], np.array(a) / a[0]


def sweep_filter(x, kind, freqs, q, block=64):
    """Biquad whose cutoff follows a per-sample frequency curve (updated every block)."""
    freqs = np.broadcast_to(np.asarray(freqs, float), x.shape)
    y = np.empty_like(x)
    b, a = biquad_coeffs(kind, freqs[0], q)
    zi = lfilter_zi(b, a) * 0
    for i in range(0, len(x), block):
        b, a = biquad_coeffs(kind, freqs[i], q)
        y[i:i + block], zi = lfilter(b, a, x[i:i + block], zi=zi)
    return y


def impulse(seconds=2.8, decay=2.2):
    n = int(SR * seconds)
    ir = (rng.random(n) * 2 - 1) * (1 - np.arange(n) / n) ** decay
    return ir / np.sqrt(np.sum(ir ** 2))


IR = impulse()


def with_reverb(dry, send):
    """dry + the reverb return; `send` is the part of the signal that feeds the reverb bus."""
    n = len(dry) + len(IR)
    out = np.zeros(n)
    out[:len(dry)] += dry
    size = 1 << int(np.ceil(np.log2(n)))
    wet = np.fft.irfft(np.fft.rfft(send, size) * np.fft.rfft(IR, size), size)[:n]
    out += wet * WET * 0.6
    # trim the silent end of the tail
    loud = np.nonzero(np.abs(out) > 1e-4)[0]
    return out[: (loud[-1] + 1) if len(loud) else 1]


def place(buf, sig, at):
    i = int(at * SR)
    end = min(len(buf), i + len(sig))
    buf[i:end] += sig[: end - i]


def bell_note(freq, vol, decay):
    n = int((decay + 0.05) * SR)
    env = np.full(n, 0.0001)
    a = int(0.008 * SR)
    d = int(decay * SR)
    env[:a] = exp_ramp(0.0001, vol, a)
    env[a:d] = exp_ramp(vol, 0.0001, d - a)
    sig = osc("sine", freq, n) + 0.35 * osc("sine", freq * 2, n) + 0.15 * osc("triangle", freq * 3.01, n)
    return sig * env


def noise_burst(duration, freq, q, vol, kind="bandpass", sweep_to=None):
    n = int((duration + 0.05) * SR)
    d = int(duration * SR)
    x = rng.random(n) * 2 - 1
    f = np.full(n, float(freq))
    if sweep_to:
        f[:d] = exp_ramp(freq, sweep_to, d)
        f[d:] = sweep_to
    env = np.full(n, 0.0001)
    env[:d] = exp_ramp(vol, 0.0001, d)
    return sweep_filter(x, kind, f, q) * env


def tone(freq, duration, vol, kind="sine", slide_to=None):
    n = int((duration + 0.05) * SR)
    d = int(duration * SR)
    f = np.full(n, float(freq))
    if slide_to:
        f[:d] = exp_ramp(freq, slide_to, d)
        f[d:] = slide_to
    env = np.full(n, 0.0001)
    a = int(0.01 * SR)
    env[:a] = exp_ramp(0.0001, vol, a)
    env[a:d] = exp_ramp(vol, 0.0001, d - a)
    return osc(kind, f, n) * env


# ---------------------------------------------------------------- outputs

rendered: dict[str, np.ndarray] = {}


def sfx(name, parts):
    """parts: list of (start_seconds, signal) on the sfx bus."""
    length = max(int(t * SR) + len(s) for t, s in parts)
    bus = np.zeros(length)
    for t, s in parts:
        place(bus, s, t)
    bus *= SFX
    rendered[name] = with_reverb(bus * MASTER, bus * MASTER)


sfx("jump", [(0, noise_burst(0.18, 900, 0.7, 0.12, "bandpass", 2400))])
sfx("land", [(0, noise_burst(0.12, 260, 0.8, 0.16, "lowpass", 120))])
for i in range(3):
    sfx(f"step_{i}", [(0, noise_burst(0.05, 640 + i * 70, 1.2, 0.03, "bandpass"))])
sfx("ignite", [(0, noise_burst(0.6, 600, 0.5, 0.18, "lowpass", 3200))]
    + [(0.12 + i * 0.09, bell_note(hz(s + 12), 0.12, 2.2)) for i, s in enumerate([0, 4, 7, 12])])
sfx("pickup", [(i * 0.05, bell_note(hz(s + 12), 0.07, 0.9)) for i, s in enumerate([7, 12, 16])])
sfx("flare", [(0, noise_burst(0.45, 400, 0.6, 0.22, "lowpass", 5000)), (0, tone(220, 0.4, 0.08, "sawtooth", 660))])
sfx("dissolve", [(0, noise_burst(0.5, 3000, 2, 0.08, "bandpass", 600))])
sfx("hurt", [(0, tone(140, 0.5, 0.18, "triangle", 70)), (0, noise_burst(0.35, 300, 1, 0.12, "lowpass", 90))])
sfx("extinguish", [(0, noise_burst(0.9, 1800, 0.7, 0.2, "lowpass", 120)), (0, tone(196, 1.0, 0.12, "sine", 98))])
sfx("denied", [(0, tone(330, 0.25, 0.08, "triangle", 260))])
sfx("bell", [(i * 1.6 + o, bell_note(hz(s), v, d)) for i in range(3) for (o, s, v, d) in [(0, -12, 0.35, 5), (0.02, -5, 0.18, 4)]])

# Music-box notes, rendered at the loudest (downbeat) level; the runtime scales volume down.
NOTE_VOL = 0.14
for s in [0, 2, 4, 7, 9, 12, 14, 16, 19, 21]:
    dry = bell_note(hz(s), NOTE_VOL, 1.8) * MUSIC * MASTER
    rendered[f"note_{s}"] = with_reverb(dry, dry)
for s in [-12, -10, -8, -5, -3]:
    dry = bell_note(hz(s), NOTE_VOL, 2.6) * MUSIC * MASTER
    rendered[f"bass_{s}"] = with_reverb(dry, dry)


# ---------------------------------------------------------------- seamless ambience loops

def circular_reverb(loop):
    n = len(loop)
    ir = np.zeros(n)
    ir[: min(n, len(IR))] = IR[:n]
    return loop + np.fft.irfft(np.fft.rfft(loop) * np.fft.rfft(ir), n) * WET * 0.6


def drone(cutoff):
    lfo_hz = 0.07
    T = 1 / lfo_hz
    n = int(round(T * SR))
    T = n / SR
    t2 = 2 * n  # render two periods, keep the second (filter state has settled)
    x = np.zeros(t2)
    for f, cents in [(73.42, -6), (110, 4), (146.83, 9)]:
        fr = round(f * 2 ** (cents / 1200) * T) / T  # integer cycles per loop
        x += osc("triangle", fr, t2)
    lfo = np.sin(2 * np.pi * (np.arange(t2) / SR) / T)
    y = sweep_filter(x, "lowpass", cutoff + 140 * lfo, 0.8)[n:]
    return circular_reverb(y * 0.06 * MUSIC * MASTER)


def wind():
    lfo_hz = 0.11
    T = 2 / lfo_hz
    n = int(round(T * SR))
    x = rng.random(n) * 2 - 1
    # Circular bandpass in the frequency domain so the noise loop has no seam.
    f = np.fft.rfftfreq(n, 1 / SR)
    f0, q = 520.0, 0.6
    resp = (f / (f0 * q)) / np.sqrt((1 - (f / f0) ** 2) ** 2 + (f / (f0 * q)) ** 2 + 1e-12)
    y = np.fft.irfft(np.fft.rfft(x) * resp, n)
    gain = 0.035 + 0.02 * np.sin(2 * np.pi * np.arange(n) / n * 2)
    return circular_reverb(y * gain * MUSIC * MASTER)


rendered["drone_cold"] = drone(380)
rendered["drone_warm"] = drone(380 + 900)
rendered["wind"] = wind()

# One shared gain for everything keeps the mix exactly as balanced as the WebAudio version.
peak = max(np.max(np.abs(v)) for v in rendered.values())
gain = min(0.95 / peak, 4.0)
for name, sig in rendered.items():
    data = np.clip(sig * gain, -1, 1)
    wavfile.write(OUT / f"{name}.wav", SR, np.round(data * 32767).astype(np.int16))
print(f"{len(rendered)} sounds -> {OUT}  (shared gain {gain:.2f}, loudest was {peak:.3f})")
