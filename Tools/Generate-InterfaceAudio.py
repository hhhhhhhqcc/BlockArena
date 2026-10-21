#!/usr/bin/env python3
"""Original deterministic UI and roulette cues, synthesized without external samples."""
from array import array
import hashlib
import json
import math
from pathlib import Path
import random
import re
import wave

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets' / 'Resources' / 'ArenaAudio'
RATE = 24000
TAU = math.tau
SOUNDS = {'ui_hover': .06, 'ui_toggle': .18, 'route_node': .22,
          'skill_select': .48, 'draw_tick': .11, 'map_draw': .62,
          'weather_draw': .72, 'tactics_start': .65}


def make(name, seconds):
    rng = random.Random('BlockArena/0.9.2/' + name)
    samples = array('h')
    for frame in range(round(seconds * RATE)):
        t = frame / RATE
        u = t / seconds
        fade = min(1, t / .006) * min(1, (seconds - t) / .04)
        noise = rng.uniform(-1, 1)
        if name == 'draw_tick':
            value = (.28 * math.sin(TAU * (820 * t - 900 * t * t)) + .10 * noise) * math.exp(-38 * t)
        elif name == 'ui_hover':
            value = .18 * math.sin(TAU * 1174.66 * t) * math.exp(-65 * t)
        elif name == 'ui_toggle':
            value = (.20 * math.sin(TAU * (420 * t + 620 * t * t)) + .05 * math.sin(TAU * 1260 * t)) * math.exp(-17 * t)
        elif name == 'route_node':
            value = (.22 * math.sin(TAU * 660 * t) + .10 * math.sin(TAU * 990 * t)) * math.exp(-15 * t)
        elif name == 'skill_select':
            value = sum(.13 * math.sin(TAU * hz * max(0, t - i * .065)) * math.exp(-7 * max(0, t - i * .065))
                        for i, hz in enumerate((523.25, 659.25, 783.99)) if t >= i * .065)
        elif name == 'map_draw':
            value = (.16 * math.sin(TAU * (140 * t + 210 * t * t)) + .06 * noise) * math.sin(math.pi * u) ** .7
        elif name == 'weather_draw':
            value = (.12 * math.sin(TAU * (392 * t + 110 * t * t)) + .08 * math.sin(TAU * 784 * t) + .05 * noise) * math.sin(math.pi * u)
        else:
            value = sum(.13 * math.sin(TAU * hz * max(0, t - i * .1)) * math.exp(-5 * max(0, t - i * .1))
                        for i, hz in enumerate((392, 523.25, 783.99)) if t >= i * .1)
        samples.append(round(math.tanh(value) * fade * 24000))
    path = OUT / (name + '.wav')
    with wave.open(str(path), 'wb') as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(samples.tobytes())
    meta = path.with_suffix('.wav.meta')
    if not meta.exists():
        guid = hashlib.md5(('blockarena/original-audio/' + name).encode()).hexdigest()
        template = (OUT / 'ritual_rise.wav.meta').read_text(encoding='utf-8')
        meta.write_text(re.sub(r'guid: [a-f0-9]+', 'guid: ' + guid, template, count=1), encoding='utf-8')
    peak = max(abs(value) for value in samples) / 32768
    return {'file': path.name, 'duration_seconds': seconds, 'sample_rate': RATE,
            'channels': 1, 'sample_bits': 16, 'peak': round(peak, 7),
            'rms': round(math.sqrt(sum(value * value for value in samples) / len(samples)) / 32768, 7),
            'clipped_samples': sum(abs(value) >= 32767 for value in samples),
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}


if __name__ == '__main__':
    OUT.mkdir(parents=True, exist_ok=True)
    assets = [make(name, seconds) for name, seconds in SOUNDS.items()]
    report = {'generator': 'Tools/Generate-InterfaceAudio.py',
              'provenance': 'Original procedural synthesis; Python standard library; no third-party samples.',
              'assets': assets}
    (OUT / 'interface-manifest.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    for asset in assets:
        assert asset['clipped_samples'] == 0
        print(asset['file'], 'peak', asset['peak'], asset['sha256'])
