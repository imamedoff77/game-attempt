#!/usr/bin/env python3
"""
Makes every sound of the game from scratch (noise, sine and saw waves), so there is nothing to license.
Usage: python3 art/audio/generate_sounds.py unity-client/Assets/Resources/Audio
Mono, 22050 Hz, 16 bit. Deterministic: the same run gives the same files.
"""
import os
import sys
import wave

import numpy as np

SR = 22050


def tt(d):
    return np.arange(int(SR * d)) / SR


def noise(d, seed):
    return np.random.default_rng(seed).uniform(-1, 1, int(SR * d))


def lowpass(x, fc):
    """Smooth low-pass through the spectrum (no loops, so it is fast on long signals)."""
    spec = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    return np.fft.irfft(spec / (1 + (f / fc) ** 4), len(x))


def highpass(x, fc):
    return x - lowpass(x, fc)


def decay(d, tau):
    return np.exp(-tt(d) / tau)


def sweep(d, f0, f1):
    """Sine whose pitch glides from f0 to f1 (exponentially)."""
    t = tt(d)
    f = f0 * (f1 / f0) ** (t / d)
    return np.sin(2 * np.pi * np.cumsum(f) / SR)


def tone(d, f, tau=None):
    y = np.sin(2 * np.pi * f * tt(d))
    return y * (decay(d, tau) if tau else 1)


def saw(f, d, harmonics=10):
    t = tt(d)
    return sum(np.sin(2 * np.pi * f * k * t) / k for k in range(1, harmonics + 1) if f * k < SR / 2.2)


def fade(x, ms=4):
    n = int(SR * ms / 1000)
    x = x.copy()
    x[:n] *= np.linspace(0, 1, n)
    x[-n:] *= np.linspace(1, 0, n)
    return x


def norm(x, peak=0.85):
    return x / (np.max(np.abs(x)) + 1e-9) * peak


def write(folder, name, x):
    path = os.path.join(folder, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype("<i2").tobytes())
    print(f"{name}.wav {len(x) / SR:.2f}s")


def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


# ---------------------------------------------------------------- effects

def rifle(seed):
    d = 0.16
    crack = lowpass(noise(d, seed), 6000) * decay(d, 0.012)
    body = lowpass(noise(d, seed + 50), 1500) * decay(d, 0.035)
    thump = tone(d, 140 + seed * 9, 0.03)
    return fade(norm(crack * 0.8 + body * 0.7 + thump * 0.6))


def cannon():
    d = 0.7
    boom = sweep(d, 130, 32) * decay(d, 0.16)
    blast = lowpass(noise(d, 7), 1400) * decay(d, 0.09)
    click = highpass(noise(d, 8), 2500) * decay(d, 0.006)
    return fade(norm(boom * 1.0 + blast * 0.8 + click * 0.4))


def rocket():
    d = 0.9
    t = tt(d)
    env = np.minimum(t / 0.06, 1) * np.exp(-np.maximum(t - 0.06, 0) / 0.3)
    hiss = (lowpass(noise(d, 11), 3500) - lowpass(noise(d, 11), 400)) * env
    thump = sweep(d, 110, 45) * decay(d, 0.1)
    return fade(norm(hiss * 0.9 + thump * 0.6))


def explosion(d, tau, low, seed):
    blast = lowpass(noise(d, seed), low) * decay(d, tau)
    sub = sweep(d, 80, 28) * decay(d, tau * 1.2)
    crackle = highpass(noise(d, seed + 1), 3000) * decay(d, tau * 0.25) * (np.random.default_rng(seed + 2).uniform(0, 1, int(SR * d)) > 0.9)
    return fade(norm(blast * 0.9 + sub * 0.9 + crackle * 0.5), 6)


def click():
    d = 0.05
    return fade(norm(tone(d, 1500, 0.012) + 0.5 * tone(d, 3000, 0.006)), 2) * 0.7


def place():
    return norm(np.concatenate([fade(tone(0.09, 520, 0.05)), fade(tone(0.16, 780, 0.07))]), 0.7)


def ready():
    a = tone(0.35, 880, 0.12) + 0.3 * tone(0.35, 1760, 0.08)
    b = tone(0.5, 1320, 0.16) + 0.3 * tone(0.5, 2640, 0.1)
    return norm(np.concatenate([fade(a), fade(b)]), 0.6)


def victory():
    out = np.zeros(int(SR * 2.2))
    for m, d, start in ((60, 0.4, 0.0), (64, 0.4, 0.18), (67, 0.4, 0.36), (72, 1.4, 0.54)):
        n = lowpass(saw(midi(m), d, 8) * decay(d, d * 0.5), 3000)
        s = int(SR * start)
        out[s:s + len(n)] += n
    return fade(norm(out, 0.7), 8)


def defeat():
    out = np.zeros(int(SR * 2.4))
    for m, d, start in ((57, 0.9, 0.0), (53, 0.9, 0.5), (50, 1.4, 1.0)):
        n = lowpass(saw(midi(m), d, 8) * decay(d, d * 0.6), 1200)
        s = int(SR * start)
        out[s:s + len(n)] += n
    return fade(norm(out, 0.7), 8)


def alarm():
    out = []
    for _ in range(2):
        out.append(fade(tone(0.18, 440, 0.2) * 0.8 + tone(0.18, 466, 0.2) * 0.8))
        out.append(np.zeros(int(SR * 0.06)))
    return norm(np.concatenate(out), 0.6)


# ---------------------------------------------------------------- music

def music():
    """A slow, dark pad loop (A minor, F, C, G; twelve seconds each) with a soft bass pulse and a little wind."""
    chords = [(45, 52, 57, 60), (41, 48, 53, 57), (48, 52, 55, 60), (43, 50, 55, 59)]
    seg = 12.0
    total = seg * len(chords)
    tail = 3.0
    n = int(SR * (total + tail))
    out = np.zeros(n)
    t_seg = tt(seg + tail)
    for i, notes in enumerate(chords):
        pad = np.zeros(len(t_seg))
        for k, m in enumerate(notes):
            for detune in (-0.35, 0.35):
                pad += saw(midi(m) * 2 ** (detune * 20 / 1200), seg + tail, 7) * (0.5 if k == 0 else 0.35)
        pad = lowpass(pad, 900)
        env = np.minimum(t_seg / 3.0, 1) * np.where(t_seg > seg, np.maximum(0, 1 - (t_seg - seg) / tail), 1)
        pad *= env * (0.85 + 0.15 * np.sin(2 * np.pi * 0.11 * t_seg + i))
        s = int(SR * seg * i)
        out[s:s + len(pad)] += pad
    # bass pulse
    pulse = np.zeros(n)
    for i, notes in enumerate(chords):
        for beat in range(8):
            start = int(SR * (seg * i + beat * 1.5))
            d = 1.4
            b = tone(d, midi(notes[0] - 12), 0.35) * 0.9
            end = min(start + len(b), n)
            pulse[start:end] += b[:end - start]
    out += pulse * 0.9
    wind = lowpass(noise(total + tail, 99), 500) * (0.5 + 0.5 * np.sin(2 * np.pi * 0.07 * tt(total + tail)))
    out += wind * 0.35
    # make it loop: the tail fades into the start
    loop = out[:int(SR * total)].copy()
    loop[:n - int(SR * total)] += out[int(SR * total):]
    return norm(loop, 0.6)


def main():
    folder = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(folder, exist_ok=True)
    for i in range(3):
        write(folder, f"rifle{i}", rifle(i + 1))
    write(folder, "cannon", cannon())
    write(folder, "rocket", rocket())
    write(folder, "explosion_small", explosion(0.7, 0.14, 2400, 31))
    write(folder, "explosion_big", explosion(1.6, 0.34, 1300, 41))
    write(folder, "click", click())
    write(folder, "place", place())
    write(folder, "ready", ready())
    write(folder, "victory", victory())
    write(folder, "defeat", defeat())
    write(folder, "alarm", alarm())
    write(folder, "music", music())


if __name__ == "__main__":
    main()
