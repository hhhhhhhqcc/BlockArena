"""Original quiet environmental soundscapes; deterministic, standard-library only.

The wind/water beds use two cascaded low-pass filters. Discrete water, bird and
ice events provide identity instead of a permanent layer of high-frequency hiss.
Loop tails wrap into their starts, so no silence/pumping is added at the seam.
"""
from array import array
import argparse
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
SECONDS = 20
NAMES = ('ambient_courtyards', 'ambient_bog', 'ambient_frost', 'weather_rain',
         'weather_storm', 'weather_fog', 'weather_snow', 'weather_heat',
         'weather_sandstorm', 'weather_thunder')
RMS = dict(zip(NAMES, (.033, .044, .033, .053, .056, .026, .030, .023, .043, .080)))


def quiet_noise(rng, count, cutoff, gain, phase=0):
    """A cyclic, warm, twice low-passed bed with a ten-second breathing contour."""
    overlap = RATE
    alpha = 1 - math.exp(-TAU * cutoff / RATE)
    first = second = 0.0
    raw = array('d')
    for i in range(count + overlap):
        first += alpha * (rng.uniform(-1, 1) - first)
        second += alpha * (first - second)
        contour = .72 + .16 * math.sin(TAU * i / count * 2 + phase) + .12 * math.sin(TAU * i / count + .3)
        raw.append(second * gain * contour)
    for i in range(overlap):
        blend = .5 - .5 * math.cos(math.pi * i / overlap)
        raw[i] = raw[count + i] * (1 - blend) + raw[i] * blend
    return array('d', raw[:count])


def layer(destination, other, gain=1):
    for i, value in enumerate(other):
        destination[i] += value * gain


def event(destination, at, duration, frequency, gain, decay=4, bend=0, attack=.025, harmonics=()):
    """Softly struck damped resonance/chirp, including its tail across loop seams."""
    start = round(at * RATE)
    for j in range(round(duration * RATE)):
        t = j / RATE
        envelope = (1 - math.exp(-t / attack)) * math.exp(-decay * t)
        envelope *= min(1, max(0, (duration - t) / .12))
        angle = TAU * (frequency * t + bend * t * t * .5)
        value = math.sin(angle)
        for multiple, level in harmonics:
            value += level * math.sin(angle * multiple)
        destination[(start + j) % len(destination)] += value * envelope * gain


def soundscape(name):
    rng = random.Random('BlockArena/environment/0.11/' + name)
    count = RATE * SECONDS
    if name == 'ambient_courtyards':
        values = quiet_noise(rng, count, 190, .90)
        for at, pitch in ((2.2, 1080), (8.8, 1240), (16.4, 960)):
            event(values, at, .42, pitch, .035, 8, 260, .035)
            event(values, at + .20, .34, pitch * 1.08, .020, 10, -130, .025)
        event(values, 12.4, 1.5, 210, .012, 3, harmonics=((2, .25),))
    elif name == 'ambient_bog':
        values = quiet_noise(rng, count, 380, .85)
        layer(values, quiet_noise(rng, count, 75, .25, 1.2))
        for at, pitch in ((.7, 730), (2.9, 590), (5.4, 890), (11.8, 660), (14.1, 810), (18.2, 610)):
            event(values, at, .65, pitch, .048, 7, -190, .006, ((1.7, .12),))
        for at in (7.8, 15.9):
            event(values, at, .6, 186, .022, 6, -35, .035, ((2, .18),))
            event(values, at + .34, .5, 171, .018, 7, -25, .035)
    elif name == 'ambient_frost':
        values = quiet_noise(rng, count, 135, .95)
        for at, pitch in ((3.5, 340), (12.7, 295), (18.9, 390)):
            event(values, at, 1.6, pitch, .026, 2.8, -6, .045, ((1.52, .22), (2.08, .10)))
    elif name == 'weather_rain':
        values = quiet_noise(rng, count, 570, 1.1)
        layer(values, quiet_noise(rng, count, 95, .24, .8))
        for _ in range(28):
            event(values, rng.uniform(0, SECONDS), .25, rng.uniform(580, 1250), .024, 16, -220, .004)
    elif name == 'weather_storm':
        values = quiet_noise(rng, count, 430, 1.05)
        layer(values, quiet_noise(rng, count, 80, .55, 1.1))
        for _ in range(18):
            event(values, rng.uniform(0, SECONDS), .3, rng.uniform(470, 1050), .026, 14, -180, .007)
    elif name == 'weather_fog':
        values = quiet_noise(rng, count, 105, .9)
        event(values, 9.3, 2.2, 160, .006, 1.8, -2, .2, ((1.48, .10),))
    elif name == 'weather_snow':
        values = quiet_noise(rng, count, 165, .9)
        for at, pitch in ((5.2, 510), (13.3, 670)):
            event(values, at, .65, pitch, .010, 5, -9, .045)
    elif name == 'weather_heat':
        values = quiet_noise(rng, count, 120, .7)
    elif name == 'weather_sandstorm':
        values = quiet_noise(rng, count, 290, .95)
        layer(values, quiet_noise(rng, count, 65, .5, 1.8))
    else:
        count = RATE * 5
        values = quiet_noise(rng, count, 110, 2)
        layer(values, quiet_noise(rng, count, 360, .32, 1.4))
        for i in range(count):
            t = i / RATE
            onset = 1 - math.exp(-t * 7)
            tail = min(1, max(0, (5 - t) / .8))
            rolls = .72 + .20 * math.sin(t * 5.1) + .08 * math.sin(t * 12.3)
            values[i] *= onset * math.exp(-t * .7) * rolls * tail
    mean = sum(values) / len(values)
    values = array('d', (value - mean for value in values))
    if name == 'weather_thunder':
        for i in range(min(200, len(values))):
            values[i] *= i / 200
            values[-i-1] *= i / 200
    rms = math.sqrt(sum(value * value for value in values) / len(values))
    gain = min(RMS[name] / rms, (.42 if name == 'weather_thunder' else .30) / max(map(abs, values)))
    return array('h', (round(value * gain * 32767) for value in values))


def write_clip(name, output):
    data = soundscape(name)
    path = output / (name + '.wav')
    with wave.open(str(path), 'wb') as writer:
        writer.setnchannels(1)
        writer.setsampwidth(2)
        writer.setframerate(RATE)
        writer.writeframes(data.tobytes())
    meta = path.with_suffix('.wav.meta')
    if not meta.exists():
        template = (OUT / 'map_rise_stone.wav.meta').read_text(encoding='utf-8-sig')
        guid = hashlib.md5(('blockarena/original-audio/' + name).encode()).hexdigest()
        meta.write_text(re.sub(r'guid: [a-f0-9]+', 'guid: ' + guid, template, count=1), encoding='utf-8')
    samples = [value / 32768 for value in data]
    peak = max(map(abs, samples))
    rms = math.sqrt(sum(value * value for value in samples) / len(samples))
    return dict(name=name, samples=len(data), rate=RATE, seconds=len(data) / RATE,
                rms=round(rms, 8), peak=round(peak, 8), clippedSamples=sum(abs(value) >= 32767 for value in data),
                loop=name != 'weather_thunder', boundaryStep=round(abs(samples[-1] - samples[0]), 8),
                sha256=hashlib.sha256(path.read_bytes()).hexdigest())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=OUT)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    report = [write_clip(name, args.output) for name in NAMES]
    (ROOT / '.checks' / '0.11.0').mkdir(parents=True, exist_ok=True)
    (ROOT / '.checks' / '0.11.0' / 'environment-audio.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('ENVIRONMENT_AUDIO_OK', len(report), 'original quiet soundscapes')


if __name__ == '__main__':
    main()
