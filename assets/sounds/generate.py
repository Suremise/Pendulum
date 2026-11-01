"""Draws Pendulum's alert sounds: chime.wav, bell.wav and alert.wav.

    python assets/sounds/generate.py

Build-only (Python 3.10+, standard library only). Each sound is made here from sine waves, so
they're Pendulum's own, under its MIT licence, and the same bytes come out every run. They're
written to Pendulum.App/Resources/Sounds/, keeping the names reminders save, so a reminder that
plays "chime.wav" keeps a chime.
"""

from __future__ import annotations

import math
import struct
import wave
from pathlib import Path

RATE = 44100
PEAK = 0.8  # -2 dBFS: loud enough to notice, with room before clipping.
OUT = Path(__file__).resolve().parents[2] / "Pendulum.App" / "Resources" / "Sounds"


def tone(length: float, partials: list[tuple[float, float, float]], start: float = 0.0, attack: float = 0.004) -> list[float]:
    """A struck tone: (frequency, amplitude, decay time) partials, each fading exponentially,
    after a short attack so it doesn't click."""
    samples = []
    for i in range(int(length * RATE)):
        t = i / RATE - start
        if t < 0:
            samples.append(0.0)
            continue
        rise = min(t / attack, 1.0)
        samples.append(rise * sum(a * math.exp(-t / d) * math.sin(2 * math.pi * f * t) for f, a, d in partials))
    return samples


def mix(*tracks: list[float]) -> list[float]:
    length = max(len(t) for t in tracks)
    return [sum(t[i] for t in tracks if i < len(t)) for i in range(length)]


def fade_out(samples: list[float], seconds: float = 0.03) -> list[float]:
    """The last moment fades to silence, so the sound ends cleanly."""
    n = int(seconds * RATE)
    return [s * min(1.0, (len(samples) - i) / n) for i, s in enumerate(samples)]


def write(name: str, samples: list[float]) -> None:
    peak = max(abs(s) for s in samples) or 1.0
    frames = b"".join(struct.pack("<h", round(s / peak * PEAK * 32767)) for s in fade_out(samples))
    with wave.open(str(OUT / name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(frames)
    print(f"{name}: {len(samples) / RATE:.2f} s")


def struck(f: float) -> list[tuple[float, float, float]]:
    """A soft struck note: the fundamental and two quiet overtones."""
    return [(f, 1.0, 0.28), (2 * f, 0.25, 0.12), (3 * f, 0.08, 0.06)]


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)

    # Chime: two notes, high then lower, as a doorbell's ding-dong (E6, then C6).
    write("chime.wav", mix(tone(0.75, struck(1318.5)), tone(0.75, struck(1046.5), start=0.24)))

    # Bell: one strike with a bell's inharmonic overtones, the higher ones dying first (A5).
    f = 880.0
    bell = [(f, 1.0, 0.45), (2.0 * f, 0.55, 0.3), (2.4 * f, 0.35, 0.22), (3.0 * f, 0.2, 0.16), (4.2 * f, 0.15, 0.1), (5.4 * f, 0.08, 0.07)]
    write("bell.wav", tone(0.9, bell, attack=0.002))

    # Alert: three short beeps (1 kHz with a little edge from its third harmonic).
    beep = [(1000.0, 1.0, 0.12), (3000.0, 0.15, 0.05)]
    write("alert.wav", mix(*(tone(0.78, beep, start=s) for s in (0.0, 0.22, 0.44))))


if __name__ == "__main__":
    main()
