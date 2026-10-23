#!/usr/bin/env python3
"""Levels every sound in theme/Audio so they sit together: each file's loudest 50 ms (RMS) is brought to its group's
target. Short menu clicks and longer cues then feel equally loud. Run after replacing or regenerating a sound
(tools/make_sounds.py runs it at the end). Pure Python, 16-bit PCM wav only.

python3 tools/level_sounds.py          -> levels theme/Audio in place and prints each change
"""
import math
import struct
import wave
from pathlib import Path

AUDIO = Path(__file__).resolve().parent.parent / "theme" / "Audio"

# dBFS of the loudest 50 ms; anything not listed uses the menu level
MENU = -28.0
TARGETS = {
    "Key.wav": -31.0,           # typing and switches: the quietest
    "Alert.wav": -26.0,         # asking before something can't be undone
    "Success.wav": -26.0,
    "LoginSelect.wav": -25.0,
    "Boot.wav": -25.0,
    "GameStarting.wav": -24.0,  # moments: a touch louder than the clicks
    "GameReady.wav": -24.0,
}


def read(path):
    with wave.open(str(path), "rb") as w:
        params = w.getparams()
        frames = w.readframes(w.getnframes())
    samples = list(struct.unpack("<%dh" % (len(frames) // 2), frames))
    return params, samples


def loudest(samples, channels, rate):
    window = int(rate * 0.05) * channels
    best = 0.0
    step = max(channels, window // 4)
    for start in range(0, max(1, len(samples) - window), step):
        chunk = samples[start:start + window]
        if not chunk:
            break
        rms = math.sqrt(sum(v * v for v in chunk) / len(chunk)) / 32768
        best = max(best, rms)
    return 20 * math.log10(best) if best > 0 else -120.0


def level(path, target):
    params, samples = read(path)
    now = loudest(samples, params.nchannels, params.framerate)
    gain = target - now
    if abs(gain) < 0.3:
        return now, 0.0
    factor = 10 ** (gain / 20)
    peak = max(abs(v) for v in samples) or 1
    factor = min(factor, 32000 / peak)   # never clip
    out = [int(max(-32768, min(32767, round(v * factor)))) for v in samples]
    # Playnite keeps the theme's sounds open; a file in use can still be renamed, so move it aside first
    aside = path.with_name(path.name + ".old")
    try:
        if aside.exists():
            aside.unlink()
        path.rename(aside)
    except OSError:
        pass
    with wave.open(str(path), "wb") as w:
        w.setparams(params)
        w.writeframes(struct.pack("<%dh" % len(out), *out))
    return now, 20 * math.log10(factor)


def main():
    for path in sorted(AUDIO.glob("*.wav")):
        target = TARGETS.get(path.name, MENU)
        try:
            before, gain = level(path, target)
        except (wave.Error, struct.error) as e:
            print(f"skipped {path.name}: {e}")
            continue
        print(f"{path.name}: loudest {before:6.1f} dB -> {target:5.1f} ({gain:+.1f} dB)")
    for old in AUDIO.glob("*.old"):
        try:
            old.unlink()
        except OSError:
            pass   # still open in Playnite: removed next time


if __name__ == "__main__":
    main()
