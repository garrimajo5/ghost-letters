"""Original Ghost Letters audio, dedicated to CC0-1.0.
Reproduce with Python 3, numpy and imageio-ffmpeg:
  python -m pip install numpy imageio-ffmpeg
  python client/tools/generate_audio.py
No samples, soundfonts, recordings, or third-party compositions are used.
"""
from pathlib import Path
import subprocess
import numpy as np
import imageio_ffmpeg

OUT = Path(__file__).resolve().parents[1] / "assets" / "audio"
SR = 44100
rng = np.random.default_rng(7319)

def save(name, data, rate):
    path = OUT / name
    path.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([
        imageio_ffmpeg.get_ffmpeg_exe(), "-hide_banner", "-loglevel", "error", "-y",
        "-f", "f32le", "-ar", str(SR), "-ac", str(data.shape[1]),
        "-i", "pipe:0", "-c:a", "libmp3lame", "-b:a", rate, "-write_xing", "1", str(path),
    ], input=data.astype("<f4").tobytes(), check=True)
    limit = 2_000_000 if name.startswith("music/") else 100_000
    assert path.stat().st_size < limit
    print(f"{name}: {path.stat().st_size} bytes; peak {np.max(np.abs(data)):.3f}")

def bell(freq, length=3.5):
    t = np.arange(int(SR*length))/SR
    attack = 1-np.exp(-t*90)
    return attack*(np.sin(2*np.pi*freq*t)*np.exp(-t*1.6)
        + .22*np.sin(2*np.pi*freq*2.003*t)*np.exp(-t*3)
        + .07*np.sin(2*np.pi*freq*3.98*t)*np.exp(-t*6))

def add_wrap(x, note, at, pan):
    idx = (np.arange(len(note))+int(at*SR)) % len(x)
    x[idx,0] += note*np.sqrt((1-pan)/2)
    x[idx,1] += note*np.sqrt((1+pan)/2)

def music(name, tense=False):
    # All continuous oscillators have an integer number of cycles in the loop.
    duration=48
    n=duration*SR
    t=np.arange(n)/SR
    x=np.zeros((n,2))
    for i,f in enumerate(([65.406,98,130.813,155.563] if tense else [65.406,98,146.832,174.614])):
        f=round(f*duration)/duration
        pad=(np.sin(2*np.pi*f*t)+.17*np.sin(2*np.pi*2*f*t))
        breath=.7+.3*np.cos(2*np.pi*(i+1)*t/duration)
        for ch in range(2):
            x[:,ch] += .035*pad*breath*(1+.06*np.sin(2*np.pi*(ch+1)*t/duration))
    # Quiet, band-limited air; periodic FFT filtering preserves the loop seam.
    air=rng.normal(0,1,n)
    spectrum=np.fft.rfft(air)
    freq=np.fft.rfftfreq(n,1/SR)
    spectrum *= np.exp(-(freq/1800)**2)*(1-np.exp(-(freq/350)**2))
    air=np.fft.irfft(spectrum,n)
    x += air[:,None]*.008
    motif=([60,63,67,62,58,63,65,62] if tense else [60,67,70,74,67,63,65,62])
    for i,midi in enumerate(motif):
        at=i*6+1.2
        note=bell(440*2**((midi-69)/12))*.075
        pan=(-.35 if i%2 else .35)
        add_wrap(x,note,at,pan)
        add_wrap(x,note*.24,at+.37,-pan)
        add_wrap(x,note*.10,at+.79,pan)
    if tense:
        for i in range(24):
            tt=np.arange(int(SR*.10))/SR
            tick=np.sin(2*np.pi*820*tt)*np.exp(-tt*75)*(1-np.exp(-tt*400))*.018
            add_wrap(x,tick,i*2+.5,(-.25 if i%2 else .25))
    # Source seam check: no artificial fade-out/gap is inserted.
    assert np.max(np.abs(x[0]-x[-1])) < .02
    save("music/"+name+".mp3",x,"96k")

music("menu")
music("game",True)
cues={
 "phase": ([220,293.66],.65),
 "yourTurn": ([392,523.25],.9),
 "letterSent": ([293.66,349.23],.48),
 "reveal": ([329.63,493.88,659.25],1.0),
 "vanish": ([293.66,196],.8),
 "chat": ([523.25],.24),
 "tick": ([660],.075),
 "timeUp": ([330,220],.65),
 "vote": ([349.23],.32),
 "correct": ([329.63,392,523.25],1.1),
 "incorrect": ([246.94,196],.85),
 "victory": ([261.63,329.63,392,523.25],2.3),
 "defeat": ([293.66,246.94,196],2.1),
}
for name,(notes,duration) in cues.items():
    n=int(SR*duration)
    x=np.zeros(n)
    for i,f in enumerate(notes):
        offset=int(i*duration*.22*SR)
        tone=bell(f,duration)[:n-offset]
        x[offset:] += tone*.13
    fade=min(int(.04*SR),n//3)
    x[-fade:]*=np.linspace(1,0,fade)
    save("sfx/"+name+".mp3",x[:,None],"64k")

