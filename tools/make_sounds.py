#!/usr/bin/env python3
"""Selene's own interface sounds, synthesized (no samples): a crystal cursor tick and a harp-like boot arpeggio, in the
spirit of classic JRPG menus but with our own notes. Pure Python, no packages.

python3 tools/make_sounds.py            -> writes the chosen variants into theme/Audio (navigation.wav, Boot.wav)
python3 tools/make_sounds.py preview DIR -> writes every variant into DIR to listen to
"""
import math
import random
import struct
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RATE = 44100
CHOSEN = {"navigation.wav": "cursor_glass", "activation.wav": "confirm_glass", "Boot.wav": "boot_a"}


def note(name):
    """'E6' -> Hz"""
    names = {"C": -9, "D": -7, "E": -5, "F": -4, "G": -2, "A": 0, "B": 2}
    semis = names[name[0]] + (1 if "#" in name else 0) + (int(name[-1]) - 4) * 12
    return 440.0 * 2 ** (semis / 12)


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def mix(into, sound, at):
    start = int(at * RATE)
    need = start + len(sound) - len(into)
    if need > 0:
        into.extend([0.0] * need)
    for i, v in enumerate(sound):
        into[start + i] += v


def bell(freq, length, decay, partials=((1, 1.0), (2.76, 0.35), (5.4, 0.12)), attack=0.002, drop=0.0):
    """struck crystal / glass: a few inharmonic partials with exponential decay; drop = slight pitch fall"""
    out = []
    n = int(RATE * length)
    phases = [0.0] * len(partials)
    for i in range(n):
        t = i / RATE
        env = (min(1.0, t / attack)) * math.exp(-t / decay)
        bend = 1.0 - drop * min(1.0, t / 0.03)
        v = 0.0
        for k, (mult, amp) in enumerate(partials):
            phases[k] += 2 * math.pi * freq * mult * bend / RATE
            v += amp * math.sin(phases[k]) * math.exp(-t * (mult - 1) * 6)
        out.append(v * env)
    return out


def pluck(freq, length, brightness=0.5, seed=0):
    """harp-like plucked string (Karplus-Strong)"""
    rnd = random.Random(seed)
    period = int(RATE / freq)
    buf = [rnd.uniform(-1, 1) for _ in range(period)]
    # soften the excitation for a rounder harp tone
    for _ in range(2):
        buf = [(buf[i] + buf[i - 1]) / 2 for i in range(period)]
    out = []
    idx = 0
    damp = 0.996 + brightness * 0.0035
    for _ in range(int(RATE * length)):
        nxt = (idx + 1) % period
        val = damp * 0.5 * (buf[idx] + buf[nxt])
        out.append(buf[idx])
        buf[idx] = val
        idx = nxt
    return out


def reverb(sound, mix_level=0.25, tail=1.2):
    """a cheap airy room: a few feedback delays"""
    out = sound + [0.0] * int(RATE * tail)
    for delay, gain in ((0.031, 0.5), (0.047, 0.42), (0.071, 0.35), (0.113, 0.28)):
        d = int(delay * RATE)
        wet = [0.0] * len(out)
        for i in range(d, len(out)):
            wet[i] = out[i - d] * gain + wet[i - d] * gain * 0.6
        out = [a + b * mix_level for a, b in zip(out, wet)]
    return out


def lowpass(sound, amount):
    out, last = [], 0.0
    for v in sound:
        last += (v - last) * amount
        out.append(last)
    return out


def normalize(sound, peak):
    top = max(abs(v) for v in sound) or 1
    return [v / top * peak for v in sound]


def fade_tail(sound, seconds=0.05):
    n = min(len(sound), int(RATE * seconds))
    for i in range(n):
        sound[-1 - i] *= i / n
    return sound


# ---------------- the sounds ----------------
def cursor_a():
    # small crystal tink, a fifth on top
    s = bell(note("E7"), 0.12, 0.022, partials=((1, 1.0), (1.5, 0.25), (3.0, 0.08)), drop=0.01)
    return normalize(fade_tail(reverb(s, 0.12, 0.12)), 0.5)


def cursor_b():
    # softer and lower, a wooden-glass blip
    s = bell(note("B6"), 0.1, 0.018, partials=((1, 1.0), (2.0, 0.2)), drop=0.03)
    return normalize(fade_tail(lowpass(reverb(s, 0.1, 0.1), 0.6)), 0.5)


def cursor_c():
    # two very quick notes (up), like an old menu cursor
    s = silence(0)
    mix(s, bell(note("A6"), 0.06, 0.012, partials=((1, 1.0), (3.0, 0.1))), 0)
    mix(s, bell(note("E7"), 0.09, 0.018, partials=((1, 1.0), (3.0, 0.1))), 0.028)
    return normalize(fade_tail(reverb(s, 0.1, 0.1)), 0.5)


def boot_a():
    # rising harp arpeggio over two octaves, a crystal shimmer on the last note
    notes = ["C4", "G4", "D5", "E5", "G5", "C6", "D6", "E6", "G6"]
    s = silence(0)
    for i, n in enumerate(notes):
        mix(s, [v * (0.75 + 0.25 * i / len(notes)) for v in pluck(note(n), 2.2, 0.5, seed=i)], i * 0.085)
    mix(s, [v * 0.35 for v in bell(note("E7"), 1.6, 0.5)], len(notes) * 0.085)
    return normalize(fade_tail(reverb(lowpass(s, 0.5), 0.3, 1.6), 0.4), 0.7)


def boot_b():
    # slower, dreamier: up and settling back on the fifth
    notes = ["A3", "E4", "B4", "C#5", "E5", "A5", "B5", "E6", "B5", "C#6"]
    s = silence(0)
    for i, n in enumerate(notes):
        mix(s, pluck(note(n), 2.4, 0.4, seed=10 + i), i * 0.12)
    return normalize(fade_tail(reverb(lowpass(s, 0.45), 0.35, 1.8), 0.4), 0.7)


def boot_c():
    # crystal bells only: a soft chord blooming upward
    s = silence(0)
    for i, n in enumerate(["D5", "A5", "E6", "F#6", "A6", "D7"]):
        mix(s, [v * (1 - i * 0.08) for v in bell(note(n), 2.0, 0.6)], i * 0.11)
    return normalize(fade_tail(reverb(s, 0.35, 1.6), 0.4), 0.65)


def ping(f0, f1, glide, start, attack, hold, decay, amp, length):
    """a sine that slides from f0 to f1 over `glide` s, raised-cosine attack, holds, then decays (time constant `decay`)"""
    out = [0.0] * int(RATE * length)
    phase = 0.0
    for i in range(int(start * RATE), len(out)):
        t = i / RATE - start
        f = f0 + (f1 - f0) * min(1.0, t / glide) if glide > 0 else f0
        phase += 2 * math.pi * f / RATE
        if t < attack:
            env = 0.5 - 0.5 * math.cos(math.pi * t / attack)
        elif t < attack + hold:
            env = 1.0
        else:
            env = math.exp(-(t - attack - hold) / decay)
        out[i] = amp * env * math.sin(phase)
    return out


def click(start, length, amp, total, seed=3):
    """a tiny soft noise tick (the attack of a tap)"""
    rnd = random.Random(seed)
    out = [0.0] * int(RATE * total)
    n = int(length * RATE)
    for k in range(n):
        out[int(start * RATE) + k] = amp * rnd.uniform(-1, 1) * (1 - k / n)
    return lowpass(out, 0.5)


def add(*sounds):
    n = max(len(x) for x in sounds)
    return [sum(x[i] for x in sounds if i < len(x)) for i in range(n)]


def cursor_glass():
    # a very short glassy tik: a high ping sliding up a little, a softer one an octave below, a hint of a tap
    total = 0.1
    s = add(ping(10400, 11150, 0.014, 0.008, 0.005, 0.004, 0.0028, 1.0, total),
            ping(5250, 6250, 0.014, 0.008, 0.005, 0.003, 0.0025, 0.42, total),
            ping(7800, 7800, 0, 0.009, 0.004, 0.002, 0.0025, 0.09, total),
            click(0.011, 0.0015, 0.35, total))
    return normalize(s, 0.24)


def confirm_glass():
    # a brighter double: a 4 kHz tick, then a high bell that rings on very softly
    total = 0.32
    s = add(ping(3950, 3950, 0, 0.0, 0.002, 0.004, 0.005, 0.13, total),
            ping(4170, 4170, 0, 0.0, 0.002, 0.004, 0.005, 0.1, total),
            ping(10420, 10420, 0, 0.008, 0.004, 0.012, 0.0048, 1.0, total),
            ping(9200, 9200, 0, 0.008, 0.004, 0.010, 0.0055, 0.2, total),
            ping(10380, 10380, 0, 0.03, 0.01, 0.0, 0.05, 0.04, total))
    return normalize(s, 0.22)


SOUNDS = {f.__name__: f for f in (cursor_a, cursor_b, cursor_c, cursor_glass, confirm_glass, boot_a, boot_b, boot_c)}


def write(path, samples):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, v)) * 32767)) for v in samples))


if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[1] == "preview":
        folder = Path(sys.argv[2])
        folder.mkdir(parents=True, exist_ok=True)
        for name, make in SOUNDS.items():
            write(folder / f"{name}.wav", make())
            print("wrote", folder / f"{name}.wav")
    else:
        for target, name in CHOSEN.items():
            write(ROOT / "theme" / "Audio" / target, SOUNDS[name]())
            print(f"wrote theme/Audio/{target} ({name})")
