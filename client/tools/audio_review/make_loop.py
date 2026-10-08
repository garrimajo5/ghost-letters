import sys, numpy as np, subprocess
# Бесшовная петля: склейка там, где конец трека звучит как начало (точки нашёл спектральный поиск).
src, out_wav, a_s, b_s = sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4])
sr=44100
raw=subprocess.run(['ffmpeg','-v','error','-i',src,'-ac','2','-ar',str(sr),'-f','f32le','-'],capture_output=True).stdout
x=np.frombuffer(raw,np.float32).reshape(-1,2).astype(np.float64)
mono=x.mean(1)
# Refine b to phase-align with a: onset envelope cross-correlation (±40 ms) over 2 s windows.
def env(s):
  h=64; e=np.sqrt(np.convolve(s**2,np.ones(h)/h,'same'))[::8]; d=np.maximum(np.diff(e,prepend=e[0]),0); return d-d.mean()
a=int(a_s*sr); b=int(b_s*sr); L=2*sr; R=int(0.04*sr)
ref=env(mono[a:a+L]); best=None
for sh in range(-R,R+1,8):
  c=env(mono[b+sh:b+sh+L]); v=float(ref@c)/(np.linalg.norm(ref)*np.linalg.norm(c)+1e-9)
  if best is None or v>best[0]: best=(v,sh)
b+=best[1]; print('refined shift ms', round(best[1]/sr*1000,1), 'corr', round(best[0],3))
C=int(2.5*sr)
t=np.linspace(0,np.pi/2,C)[:,None]
loop=x[a+C:b+C].copy()
# склейка там, где конец и начало звучат похоже: x[b:b+C] ≈ x[a:a+C]
loop[-C:]=x[b:b+C]*np.cos(t)+x[a:a+C]*np.sin(t)
# wrap: last sample is x[a+C-1], first is x[a+C]
print('wrap jump', np.abs(loop[-1]-loop[0]).max(), 'typical step', np.abs(np.diff(loop[:sr],axis=0)).mean())
print('length s', round(len(loop)/sr,2))
import wave
pcm=(np.clip(loop,-1,1)*32767).astype('<i2')
with wave.open(out_wav,'wb') as w: w.setnchannels(2); w.setsampwidth(2); w.setframerate(sr); w.writeframes(pcm.tobytes())
