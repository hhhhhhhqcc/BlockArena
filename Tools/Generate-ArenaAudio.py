#!/usr/bin/env python3
"""Compose BlockArena's original music and effects using only the Python stdlib.

No samples, external synthesizer, network access or third-party packages are used.
Run from any directory; paths default to this script's Unity project. Existing
Unity GUIDs are retained. The generated PCM/loop measurements live in manifest.json.
"""

import argparse
from array import array
from functools import lru_cache
import hashlib
import json
import math
from pathlib import Path
import random
import sys
import wave


SAMPLE_RATE = 24000
BPM = 96
BEAT = 60.0 / BPM
BARS = 16
MUSIC_SECONDS = BARS * 4 * BEAT
TAU = 2.0 * math.pi
SEED = 187321


def frequency(midi):
    return 440.0 * 2.0 ** ((midi - 69) / 12.0)


def smoothstep(value):
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)


@lru_cache(maxsize=512)
def tone(midi, duration, instrument):
    """A small band-limited sine-family synth with zero-valued note boundaries."""
    frames = max(2, round(duration * SAMPLE_RATE))
    note = array("f", [0.0]) * frames
    hz = frequency(midi)
    step = TAU * hz / SAMPLE_RATE
    if instrument == "pad":
        attack, release = .42, .65
    elif instrument == "bass":
        attack, release = .012, .12
    elif instrument == "glass":
        attack, release = .009, .22
    else:
        attack, release = .006, .13
    for i in range(frames):
        t = i / SAMPLE_RATE
        remaining = (frames - 1 - i) / SAMPLE_RATE
        envelope = smoothstep(t / attack) * smoothstep(remaining / release)
        phase = step * i
        if instrument == "pad":
            vibrato = .004 * math.sin(TAU * .31 * t)
            value = (.72 * math.sin(phase + vibrato) +
                     .18 * math.sin(phase * 2.0 + .18) +
                     .10 * math.sin(phase * .5))
            envelope *= .86 + .14 * math.sin(math.pi * t / duration)
        elif instrument == "bass":
            value = .82 * math.sin(phase) + .13 * math.sin(phase * 2) + .05 * math.sin(phase * 3)
            envelope *= math.exp(-1.35 * t / duration)
        elif instrument == "glass":
            # Inharmonic bell partials decay faster than the tuned fundamental.
            value = (.72 * math.sin(phase) * math.exp(-2.2 * t) +
                     .18 * math.sin(phase * 2.01) * math.exp(-5.2 * t) +
                     .10 * math.sin(phase * 3.98) * math.exp(-8 * t))
        else:
            value = (.70 * math.sin(phase) + .22 * math.sin(phase * 2) + .08 * math.sin(phase * 3))
            envelope *= math.exp(-3.8 * t / duration)
        note[i] = value * envelope
    return note


class Mix:
    def __init__(self, seconds, loop=False):
        self.frames = round(seconds * SAMPLE_RATE)
        self.left = array("f", [0.0]) * self.frames
        self.right = array("f", [0.0]) * self.frames
        self.loop = loop

    def add(self, signal, at, gain=1.0, pan=0.0):
        start = round(at * SAMPLE_RATE)
        left = gain * math.sqrt((1.0 - pan) * .5)
        right = gain * math.sqrt((1.0 + pan) * .5)
        frames = self.frames
        if self.loop:
            # Release tails wrap into the next phrase, rather than being truncated.
            for index, value in enumerate(signal):
                destination = (start + index) % frames
                self.left[destination] += value * left
                self.right[destination] += value * right
        else:
            for index, value in enumerate(signal):
                destination = start + index
                if 0 <= destination < frames:
                    self.left[destination] += value * left
                    self.right[destination] += value * right

    def note(self, midi, at, duration, gain, instrument="pluck", pan=0.0, echo=False):
        signal = tone(midi, round(duration, 5), instrument)
        self.add(signal, at, gain, pan)
        if echo:
            self.add(signal, at + .75 * BEAT, gain * .16, -pan)
            self.add(signal, at + 1.5 * BEAT, gain * .065, pan)

    def export(self, path, target_peak, target_rms=None):
        peak = max(max(map(abs, self.left)), max(map(abs, self.right)), 1e-9)
        power = sum(value * value for value in self.left) + sum(value * value for value in self.right)
        raw_rms = math.sqrt(power / (self.frames * 2))
        gain = target_peak / peak
        if target_rms is not None and raw_rms > 0:
            gain = min(gain, target_rms / raw_rms)
        pcm = array("h")
        peak_pcm, sum_squares = 0, 0
        for index in range(self.frames):
            for source in (self.left, self.right):
                value = round(source[index] * gain * 32767)
                # The normalization above leaves >6 dB of per-asset headroom.
                if abs(value) > 32767:
                    raise ValueError("PCM clipping in " + path.name)
                peak_pcm = max(peak_pcm, abs(value))
                sum_squares += value * value
                pcm.append(value)
        if sys.byteorder != "little":
            pcm.byteswap()
        with wave.open(str(path), "wb") as output:
            output.setnchannels(2)
            output.setsampwidth(2)
            output.setframerate(SAMPLE_RATE)
            output.writeframes(pcm.tobytes())
        # Evaluate the encoded signal, not the pre-quantized float mix.
        if sys.byteorder != "little":
            pcm.byteswap()
        jumps = [abs(pcm[channel] - pcm[-2 + channel]) / 32768 for channel in (0, 1)]
        neighbor = [abs(pcm[2 + channel] - pcm[channel]) / 32768 for channel in (0, 1)]
        rms = math.sqrt(sum_squares / len(pcm)) / 32768
        return {
            "file": path.name, "duration_seconds": round(self.frames / SAMPLE_RATE, 6),
            "sample_rate": SAMPLE_RATE, "channels": 2, "sample_bits": 16,
            "peak": round(peak_pcm / 32768, 7), "rms": round(rms, 7),
            "peak_dbfs": round(20 * math.log10(max(1e-9, peak_pcm / 32768)), 3),
            "rms_dbfs": round(20 * math.log10(max(1e-9, rms)), 3),
            "clipped_samples": 0, "loop": self.loop,
            "loop_boundary_delta": round(max(jumps), 7),
            "first_neighbor_delta": round(max(neighbor), 7),
            "bytes": path.stat().st_size,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        }


@lru_cache(maxsize=16)
def percussion(kind):
    seconds = {"kick": .25, "snare": .18, "hat": .07, "shaker": .10}[kind]
    frames = round(seconds * SAMPLE_RATE)
    signal = array("f", [0.0]) * frames
    rng = random.Random(SEED + sum(map(ord, kind)))
    phase, previous_noise, low_noise = 0.0, 0.0, 0.0
    for i in range(frames):
        t = i / SAMPLE_RATE
        edge = smoothstep(t / .003) * smoothstep((seconds - t) / .025)
        noise = rng.uniform(-1, 1)
        low_noise += .22 * (noise - low_noise)
        if kind == "kick":
            phase += TAU * (48 + 85 * math.exp(-t * 38)) / SAMPLE_RATE
            value = math.sin(phase) * math.exp(-t * 19) + low_noise * .12 * math.exp(-t * 65)
        elif kind == "snare":
            value = (.65 * low_noise + .25 * math.sin(TAU * 175 * t)) * math.exp(-t * 24)
        else:
            value = (noise - previous_noise) * .35 * math.exp(-t * (65 if kind == "hat" else 40))
        previous_noise = noise
        signal[i] = value * edge
    return signal


# Original 8-bar motif; no existing melody, recording or sound font is used.
# Entries are beat offset, MIDI pitch and sounding duration in beats.
MELODY = [
    [(0, 74, .65), (1, 69, .4), (1.75, 72, .7), (3, 77, .75)],
    [(.5, 76, .6), (1.5, 72, .55), (2.5, 69, .9)],
    [(0, 70, .7), (1.25, 74, .45), (2, 77, .6), (3.25, 76, .45)],
    [(.25, 72, 1), (2, 69, .6), (3, 67, .6)],
    [(0, 69, .65), (1, 72, .5), (2, 76, .8), (3.25, 79, .45)],
    [(.5, 77, .7), (1.75, 76, .45), (2.5, 72, 1)],
    [(0, 76, .65), (1, 74, .6), (2.25, 69, .4), (3, 72, .5)],
    [(.25, 74, 1.15), (2.5, 69, .55)],
]
CHORDS = [(50, 57, 60, 65), (50, 57, 62, 65),
          (46, 53, 58, 62), (46, 53, 57, 62),
          (53, 60, 64, 69), (53, 60, 65, 69),
          (48, 55, 60, 64), (48, 55, 62, 67)]


def compose(arrangement):
    mix = Mix(MUSIC_SECONDS, loop=True)
    menu, battle = arrangement == "menu", arrangement == "battle"
    for bar in range(BARS):
        at = bar * 4 * BEAT
        chord = CHORDS[bar % 8]
        for voice, pitch in enumerate(chord):
            mix.note(pitch, at - .16, 4 * BEAT + .62,
                     .039 if menu else .029, "pad", (voice - 1.5) * .34)
        for offset, pitch, length in MELODY[bar % 8]:
            # A higher answering note marks the second half of the phrase.
            pitch += 12 if bar >= 8 and bar % 4 == 3 else 0
            mix.note(pitch, at + offset * BEAT, length * BEAT + .52,
                     .145 if menu else .125, "glass", -.12 if bar % 2 else .12, echo=True)
        if menu:
            if bar % 2 == 0:
                mix.note(chord[0] - 12, at, BEAT * 2.7, .055, "bass", 0)
            mix.note(chord[2] + 12, at + 2.5 * BEAT, .65, .035, "glass", -.52, echo=True)
            continue
        for beat in range(4):
            bass_pitch = chord[0] - 12 + (7 if beat == 3 and battle else 0)
            mix.note(bass_pitch, at + beat * BEAT, BEAT * (.65 if battle else .9),
                     .10 if battle else .075, "bass")
            if battle or beat % 2 == 0:
                mix.add(percussion("kick"), at + beat * BEAT, .13 if battle else .075)
            if beat in (1, 3):
                mix.add(percussion("snare"), at + beat * BEAT, .09 if battle else .035, .08)
            for half in (0, 1):
                if battle or half == 1:
                    mix.add(percussion("hat" if battle else "shaker"),
                            at + (beat + half * .5) * BEAT, .032 if battle else .022, -.3 if half else .3)
            # Quiet, arpeggiated pixels supply motion under the main bell melody.
            arp_pitch = chord[(beat + bar) % 4] + 12
            mix.note(arp_pitch, at + (beat + .5) * BEAT, .34,
                     .042 if battle else .031, "pluck", -.38 if beat % 2 else .38)
        if battle and bar % 4 == 3:
            mix.note(chord[2], at + 3.5 * BEAT, .27, .055, "pluck", -.2)
    return mix


def texture(seconds, kind, seed):
    """Filtered noise, sweeps and resonances; every transient has soft boundaries."""
    frames = round(seconds * SAMPLE_RATE)
    rng = random.Random(SEED + seed)
    signal = array("f", [0.0]) * frames
    previous, low, phase = 0.0, 0.0, 0.0
    for i in range(frames):
        t, progress = i / SAMPLE_RATE, i / (frames - 1)
        noise = rng.uniform(-1, 1)
        low += .09 * (noise - low)
        high = noise - previous
        previous = noise
        edge = smoothstep(t / .006) * smoothstep((seconds - t) / .045)
        if kind == "rise":
            hz = 48 + 96 * progress * progress
            phase += TAU * hz / SAMPLE_RATE
            envelope = math.sin(math.pi * progress) ** .7
            value = (.48 * math.sin(phase) + .22 * math.sin(phase * 1.5) + low * 1.7) * envelope
        elif kind == "charge":
            hz = 220 * 2 ** (2 * progress)
            phase += TAU * hz / SAMPLE_RATE
            value = (.55 * math.sin(phase) + .15 * math.sin(phase * 2)) * math.sin(math.pi * progress)
        elif kind == "launch":
            hz = 760 * math.exp(-progress * 2.7) + 65
            phase += TAU * hz / SAMPLE_RATE
            value = (.55 * math.sin(phase) + .23 * high + .4 * low) * math.exp(-progress * 4)
        elif kind in ("hit", "explode"):
            hz = 115 * math.exp(-progress * 4) + 38
            phase += TAU * hz / SAMPLE_RATE
            value = (.62 * math.sin(phase) + 1.25 * low + .07 * high) * math.exp(-progress * 5.5)
        elif kind == "stone":
            value = (.70 * low + .10 * high + .24 * math.sin(TAU * 160 * t)) * math.exp(-progress * 7)
        else:
            value = (.40 * high + .7 * low) * math.sin(math.pi * progress) ** 2 * math.exp(-progress * 1.8)
        signal[i] = value * edge
    return signal


def effect(name):
    lengths = {
        "click": .15, "purchase": .72, "coin": .48, "pickup": .30, "place": .27,
        "recruit": 1.12, "battle_start": 1.35, "attack": .22,
        "ritual_rise": 1.25, "ritual_charge": .86, "ritual_launch": .65,
        "commander_hit": .70, "victory": 2.8, "defeat": 2.6,
        "combat_arrow": .23, "combat_hit": .20, "combat_explode": .48,
        "combat_death": .42, "combat_heal": .58, "error": .32,
    }
    mix = Mix(lengths[name])
    seed = sum(map(ord, name))
    if name == "click":
        mix.note(84, 0, .10, .85, "pluck")
        mix.note(91, .025, .09, .22, "glass")
    elif name in ("purchase", "coin", "recruit"):
        notes = [74, 81, 86] if name != "recruit" else [69, 74, 77, 81]
        spacing = .085 if name != "recruit" else .13
        for index, pitch in enumerate(notes):
            mix.note(pitch + (12 if name == "coin" else 0), index * spacing,
                     .30 if name == "coin" else .46, .50, "glass", (index - 1) * .15)
    elif name in ("pickup", "place"):
        if name == "pickup":
            mix.note(62, 0, .15, .7, "pluck")
            mix.note(69, .06, .20, .5, "glass")
        else:
            mix.add(texture(.23, "stone", seed), 0, .85)
            mix.note(50, .01, .20, .35, "bass")
    elif name == "battle_start":
        for index, pitch in enumerate((62, 69, 74, 77)):
            mix.note(pitch, index * .12, .72, .48, "pluck", (index - 1.5) * .08)
        mix.add(percussion("kick"), .36, .45)
    elif name in ("ritual_rise", "ritual_charge", "ritual_launch", "commander_hit"):
        kind = {"ritual_rise": "rise", "ritual_charge": "charge",
                "ritual_launch": "launch", "commander_hit": "hit"}[name]
        mix.add(texture(lengths[name], kind, seed), 0, .85)
        if name == "ritual_rise":
            for index, pitch in enumerate((50, 57, 62)):
                mix.note(pitch, .26 + index * .17, .55, .25, "glass", (index - 1) * .22)
        elif name == "ritual_charge":
            for index, pitch in enumerate((74, 77, 81, 86)):
                mix.note(pitch, .04 + index * .12, .32, .25, "glass", (index - 1.5) * .12)
        elif name == "commander_hit":
            mix.note(38, 0, .58, .52, "bass")
            mix.add(texture(.26, "stone", seed + 1), 0, .25)
    elif name in ("victory", "defeat"):
        progression = ((62, 66, 69), (67, 71, 74), (62, 69, 74)) if name == "victory" else \
                      ((65, 69, 72), (64, 67, 71), (50, 57, 62))
        for index, chord in enumerate(progression):
            for voice, pitch in enumerate(chord):
                mix.note(pitch, index * .42 + voice * .045, 1.40, .33,
                         "glass" if name == "victory" else "pad", (voice - 1) * .25)
        if name == "victory":
            mix.note(86, 1.12, 1.25, .25, "glass", .12)
    elif name == "combat_heal":
        mix.note(81, 0, .36, .6, "glass", -.12)
        mix.note(86, .1, .42, .45, "glass", .12)
    elif name == "combat_death":
        mix.add(texture(.30, "stone", seed), 0, .55)
        mix.note(74, .01, .22, .35, "pluck", -.12)
        mix.note(62, .08, .25, .30, "pluck", .12)
    elif name == "error":
        mix.note(58, 0, .18, .6, "pluck")
        mix.note(55, .10, .18, .5, "pluck")
    else:
        kind = "explode" if name == "combat_explode" else "stone" if name == "combat_hit" else "swish"
        mix.add(texture(lengths[name] - .015, kind, seed), 0, 1)
    return mix


def guid_for(key):
    return hashlib.sha256(("BlockArena original audio v1/" + key).encode("utf-8")).hexdigest()[:32]


def folder_meta(path, key):
    meta = Path(str(path) + ".meta")
    if not meta.exists():
        meta.write_text("fileFormatVersion: 2\nguid: " + guid_for(key) +
                        "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n"
                        "  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")


def audio_meta(path, music):
    meta = Path(str(path) + ".meta")
    if meta.exists():
        return
    # PCM is already compact at 24 kHz. Keep uncompressed to avoid loop encoder gaps.
    meta.write_text("fileFormatVersion: 2\nguid: " + guid_for(path.name) + "\nAudioImporter:\n"
                    "  externalObjects: {}\n  serializedVersion: 7\n  defaultSettings:\n"
                    "    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n"
                    "    sampleRateOverride: 24000\n    compressionFormat: 0\n    quality: 1\n"
                    "    conversionMode: 0\n    preloadAudioData: 1\n  platformSettingOverrides: {}\n"
                    "  forceToMono: 0\n  normalize: 0\n  ambisonic: 0\n  loadInBackground: 0\n"
                    "  preloadAudioData: 1\n  userData: Original procedural composition; no third-party samples\n"
                    "  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path,
                        default=Path(__file__).resolve().parents[1] / "Assets" / "Resources" / "ArenaAudio")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    folder_meta(output.parent, "Resources")
    folder_meta(output, "ArenaAudio")
    assets = []
    for arrangement in ("menu", "prep", "battle"):
        name = "music_" + arrangement
        path = output / (name + ".wav")
        measurement = compose(arrangement).export(path, .52, .105 if arrangement != "battle" else .115)
        audio_meta(path, True)
        assets.append(measurement)
        print(json.dumps(measurement), flush=True)
    for name in ("click", "purchase", "coin", "pickup", "place", "recruit", "battle_start", "attack",
                 "ritual_rise", "ritual_charge", "ritual_launch", "commander_hit", "victory", "defeat",
                 "combat_arrow", "combat_hit", "combat_explode", "combat_death", "combat_heal", "error"):
        path = output / (name + ".wav")
        measurement = effect(name).export(path, .48)
        audio_meta(path, False)
        assets.append(measurement)
        print(json.dumps(measurement), flush=True)
    manifest = {
        "title": "BlockArena — Luminous Clearing", "generator": "Tools/Generate-ArenaAudio.py",
        "provenance": "Original procedural composition and synthesis, Python standard library only. No third-party samples.",
        "seed": SEED, "bpm": BPM, "music_bars": BARS,
        "music_seconds": MUSIC_SECONDS, "assets": assets,
        "total_pcm_file_bytes": sum(asset["bytes"] for asset in assets),
    }
    manifest_path = output / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    json_meta = Path(str(manifest_path) + ".meta")
    if not json_meta.exists():
        json_meta.write_text("fileFormatVersion: 2\nguid: " + guid_for("manifest.json") +
                             "\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n"
                             "  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")
    print("Generated %d clips, %.2f MiB" % (len(assets), manifest["total_pcm_file_bytes"] / 1048576), flush=True)


if __name__ == "__main__":
    main()
