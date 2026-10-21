#!/usr/bin/env python3
"""Original deterministic PCM map emergence cues; no external sound samples."""
from array import array
import hashlib
import math
from pathlib import Path
import random
import wave

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'Assets'/'Resources'/'ArenaAudio'
RATE=24000
TAU=math.tau

def cue(name,seconds):
    rng=random.Random(91021+sum(map(ord,name)))
    samples=array('h')
    slow=0.0
    for n in range(round(RATE*seconds)):
        t=n/RATE
        u=t/seconds
        noise=rng.uniform(-1,1)
        slow=slow*.985+noise*.015
        fade=min(1,t/.055)*min(1,(seconds-t)/.23)
        if name=='map_rise_stone':
            envelope=(.4+.6*math.sin(math.pi*u))
            stone=max(0,math.sin(t*TAU*4.3))**12*math.exp(-1.6*u)
            value=(.25*math.sin(TAU*(42*t+5*t*t))+.34*slow*6+noise*stone*.19)*envelope
        elif name=='map_rise_bog':
            bubble=max(0,math.sin(t*TAU*3.2))**9
            value=.19*math.sin(TAU*39*t)+.40*slow*5+.21*bubble*math.sin(TAU*(130*t+36*math.sin(t*5)))
            value*=.45+.55*math.sin(math.pi*u)
        elif name=='map_rise_frost':
            cracks=max(0,math.sin(t*TAU*5.7))**20*math.exp(-u)
            shimmer=(math.sin(TAU*784*t)+.4*math.sin(TAU*1176*t))*.05
            value=(.16*math.sin(TAU*48*t)+slow*2.6+noise*cracks*.16+shimmer)*(.45+.55*math.sin(math.pi*u))
        elif name=='map_lock':
            envelope=math.exp(-7*t)
            value=(.35*math.sin(TAU*(54*t-9*t*t))+.12*noise+.15*math.sin(TAU*99*t))*envelope
        else:
            envelope=math.sin(math.pi*u)**.7
            value=(.15*math.sin(TAU*523.25*t)+.11*math.sin(TAU*659.25*t)+.08*math.sin(TAU*783.99*t))*envelope
        samples.append(round(math.tanh(value*1.1)*fade*23000))
    path=OUT/(name+'.wav')
    with wave.open(str(path),'wb') as w:
        w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(samples.tobytes())
    meta=path.with_suffix('.wav.meta')
    if not meta.exists():
        guid=hashlib.md5(('blockarena/original-audio/'+name).encode()).hexdigest()
        template=(OUT/'ritual_rise.wav.meta').read_text(encoding='utf-8')
        import re
        template=re.sub(r'guid: [a-f0-9]+','guid: '+guid,template,count=1)
        meta.write_text(template,encoding='utf-8')
    print(name, len(samples), 'samples', hashlib.sha256(path.read_bytes()).hexdigest())

if __name__=='__main__':
    OUT.mkdir(parents=True,exist_ok=True)
    for name,seconds in [('map_rise_stone',3.7),('map_rise_bog',3.7),('map_rise_frost',3.7),('map_lock',.65),('weather_reveal',.9)]:
        cue(name,seconds)
