"""Read-only PCM/spectrum regression for the quiet environmental soundscapes.

The game and generation script do not need NumPy. This developer-only verifier
uses the installed NumPy FFT to measure the complete decoded PCM rather than a
small or cherry-picked excerpt. It never opens an audio device.
"""
import hashlib
import json
from pathlib import Path
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets' / 'Resources' / 'ArenaAudio'
CHECKS = ROOT / '.checks' / '0.11.0'
NAMES = ('ambient_courtyards', 'ambient_bog', 'ambient_frost', 'weather_rain',
         'weather_storm', 'weather_fog', 'weather_snow', 'weather_heat',
         'weather_sandstorm', 'weather_thunder')


def verify():
    rows = []
    for name in NAMES:
        path = OUT / (name + '.wav')
        with wave.open(str(path), 'rb') as source:
            rate = source.getframerate()
            assert source.getnchannels() == 1 and source.getsampwidth() == 2 and rate == 24000, name
            pcm = np.frombuffer(source.readframes(source.getnframes()), dtype='<i2')
        samples = pcm.astype(float) / 32768
        assert np.isfinite(samples).all(), name
        rms = float(np.sqrt(np.mean(samples * samples)))
        peak = float(np.max(np.abs(samples)))
        derivative_rms = float(np.sqrt(np.mean(np.diff(samples) ** 2)))
        boundary = float(abs(samples[0] - samples[-1]))
        spectrum = np.abs(np.fft.rfft(samples)) ** 2
        frequency = np.fft.rfftfreq(len(samples), 1 / rate)
        energy = spectrum.sum()
        high2 = float(spectrum[frequency >= 2000].sum() / energy)
        high4 = float(spectrum[frequency >= 4000].sum() / energy)
        assert .005 < rms < .09 and peak < .43, name
        assert abs(float(samples.mean())) < .00001, name
        assert not np.any(np.abs(pcm.astype(int)) >= 32767), name
        assert high2 < .012 and high4 < .002, name
        if name != 'weather_thunder':
            assert boundary < .012 and boundary < derivative_rms * 3, name
            assert len(samples) / rate == 20, name
        else:
            assert abs(samples[0]) < .0001 and abs(samples[-1]) < .0001, name
        row = dict(name=name, seconds=len(samples) / rate, rms=rms, peak=peak,
                   clippedSamples=int(np.sum(np.abs(pcm.astype(int)) >= 32767)),
                   above2khzRatio=high2, above4khzRatio=high4,
                   spectralCentroidHz=float((frequency * spectrum).sum() / energy),
                   boundaryStep=boundary, adjacentDifferenceRms=derivative_rms,
                   boundaryToDerivativeRms=boundary / derivative_rms,
                   sha256=hashlib.sha256(path.read_bytes()).hexdigest())
        rows.append(row)
        print(name, 'RMS', round(rms, 5), 'peak', round(peak, 5), '>4kHz', round(high4 * 100, 5), '%')
    CHECKS.mkdir(parents=True, exist_ok=True)
    (CHECKS / 'audio-spectrum.json').write_text(json.dumps(dict(success=True, clips=rows), indent=2), encoding='utf-8')
    print('ENVIRONMENT_AUDIO_VERIFIED', len(rows), 'complete PCM streams; no physical playback claimed')


if __name__ == '__main__':
    verify()
