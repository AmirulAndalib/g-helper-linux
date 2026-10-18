#!/usr/bin/env python3
"""Offline checks for the vocoder / voice-effect chain.

Runs the real DSP (audio-helper/render, built from main.c) on a synthetic
voice and checks behaviour, not code:

  - every control the Vocoder window exposes changes the output
  - the wet vocoder signal is as loud as the voice, so Mix is not a volume knob
  - follow mode locks onto low male voices (80 Hz)
  - VOC 0 bypasses the whole stack, preset layers included

Usage: vocoder_offline.py <path-to-render>
"""
import subprocess
import sys

import numpy as np

FS = 48000
RENDER = sys.argv[1]


def vowel(f0_from, f0_to, seconds, formants):
    """Glottal pulse train with a pitch glide, shaped by formant peaks."""
    n = int(FS * seconds)
    f0 = np.linspace(f0_from, f0_to, n)
    phase = np.cumsum(f0 / FS)
    pulses = np.zeros(n)
    pulses[np.nonzero(np.diff(np.floor(phase)))[0]] = 1.0
    spec = np.fft.rfft(pulses)
    fr = np.fft.rfftfreq(n, 1 / FS)
    h = sum(g / (1 + ((fr - f) / bw) ** 2) for f, bw, g in formants)
    y = np.fft.irfft(spec * h, n)
    y *= np.hanning(n) ** 0.25
    return y / np.abs(y).max()


A = [(700, 110, 1.0), (1200, 140, 0.6), (2500, 200, 0.3)]
I = [(300, 80, 1.0), (2300, 160, 0.5), (3000, 200, 0.3)]
U = [(320, 80, 1.0), (800, 120, 0.4), (2400, 200, 0.1)]
gap = np.zeros(FS // 10)
SPEECH = 0.2 * np.concatenate([
    vowel(120, 95, 0.5, A), gap, vowel(140, 110, 0.4, I), gap,
    vowel(100, 130, 0.5, U), gap, vowel(110, 90, 0.6, A), gap,
]).astype(np.float32)

BASE = ["RNN 0", "VOC 1", "MTX 0", "PSH 0", "ATN 0", "BCR 0 1", "BPF 0 0", "STT 0 500"]


def render(cmds, x=SPEECH):
    p = subprocess.run([RENDER] + cmds, input=x.astype(np.float32).tobytes(),
                       capture_output=True, check=True)
    return np.frombuffer(p.stdout, dtype=np.float32)


def rms_db(y):
    return 20 * np.log10(np.sqrt(np.mean(y.astype(np.float64) ** 2)) + 1e-12)


def logspec(y, n=2048, hop=512):
    w = np.hanning(n)
    frames = np.array([y[i:i + n] * w for i in range(0, len(y) - n, hop)])
    return 20 * np.log10(np.abs(np.fft.rfft(frames, axis=1)) + 1e-9)


def specdist(a, b):
    """Mean abs dB difference, 100 Hz..8 kHz, over loud frames."""
    sa, sb = logspec(a), logspec(b)
    m = min(len(sa), len(sb))
    sa, sb = sa[:m], sb[:m]
    lo, hi = int(100 / FS * 2048), int(8000 / FS * 2048)
    loud = (sa.max(axis=1) > sa.max() - 40) | (sb.max(axis=1) > sb.max() - 40)
    return float(np.mean(np.abs(sa[loud, lo:hi] - sb[loud, lo:hi])))


def harmonic_energy(y, f0):
    s = np.abs(np.fft.rfft(y * np.hanning(len(y)))) ** 2
    fr = np.fft.rfftfreq(len(y), 1 / FS)
    mask = np.zeros_like(fr, dtype=bool)
    for k in range(1, int(4000 / f0)):
        mask |= np.abs(fr - k * f0) < 2.0
    return s[mask].sum()


failures = []


def check(ok, msg):
    print(("  ok    " if ok else "  FAIL  ") + msg)
    if not ok:
        failures.append(msg)


VOP = "VOP {mix} {hz} {atk} {rel} {det} {fol} {sh}"
ref = dict(mix=1000, hz=110, atk=5, rel=30, det=20, fol=0, sh=0)


def vop(**kw):
    return VOP.format(**{**ref, **kw})


base = render(BASE + [vop()])
print("every Vocoder window control changes the sound:")
variants = {
    "mix 300": [vop(mix=300)],
    "carrier 220 Hz": [vop(hz=220)],
    "detune 200": [vop(det=200)],
    "attack 80 ms": [vop(atk=80)],
    "release 400 ms": [vop(rel=400)],
    "follow on": [vop(fol=1)],
    "metallic 100%": ["MTX 1000", vop()],
    "pitch +7 st": ["PSH 700", vop()],
    "autotune": ["ATN 1", "ATT 0", "PSH 700", vop()],
    "low cut 700 Hz": ["BPF 700 0", vop()],
    "high cut 2000 Hz": ["BPF 0 2000", vop()],
    "bit crush 4 bit": ["BCR 4 1", vop()],
    "downsample 8x": ["BCR 0 8", vop()],
    "stutter 6 Hz": ["STT 6 500", vop()],
}
for name, cmds in variants.items():
    d = specdist(base, render(BASE + cmds))
    check(d > 1.0, f"{name:18s} {d:5.2f} dB from base")

ft = render(BASE + [vop(fol=1, sh=0)])
for sh in (-12, 12):
    d = specdist(ft, render(BASE + [vop(fol=1, sh=sh)]))
    check(d > 1.0, f"{'transpose %+d st' % sh:18s} {d:5.2f} dB from follow")

print("wet level matches the voice:")
dry = render(["RNN 0", "VOC 0"])
for name, cmds in {
    "clean, fixed 110": [vop()],
    "metallic 100%": ["MTX 1000", vop()],
    "carrier 50 Hz": [vop(hz=50)],
    "attack 100 ms": [vop(atk=100)],
    "follow": [vop(fol=1)],
}.items():
    diff = rms_db(render(BASE + cmds)) - rms_db(dry)
    check(abs(diff) < 3.0, f"{name:18s} wet - dry = {diff:+5.1f} dB")

print("follow mode locks onto low voices:")
for f0, other in ((80, 110), (65, 110), (150, 110)):
    x = (0.2 * vowel(f0, f0, 2.0, A)).astype(np.float32)
    y = render(BASE + [vop(fol=1)], x)[FS // 2:]
    good, bad = harmonic_energy(y, f0), harmonic_energy(y, other)
    check(good > 3 * bad, f"voice {f0:3d} Hz: harmonics of {f0} vs {other} = {good / bad:6.1f}x")

print("VOC 0 bypasses the preset layers:")
stack = ["PSH 1200", "BCR 4 4", "BPF 700 2000", "STT 6 500", "MTX 1000", vop()]
off = render(["RNN 0", "VOC 0"] + stack)
diff = float(np.max(np.abs(off - dry)))
check(diff < 1e-6, f"VOC 0 with full stack vs dry: max diff {diff:.2e}")

if failures:
    print(f"{len(failures)} check(s) failed")
    sys.exit(1)
print("all vocoder checks passed")
