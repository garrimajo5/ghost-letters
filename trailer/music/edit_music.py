import numpy as np, subprocess, wave, json
P,ph=(0.46425, 0.056); bar=lambda n: ph+4*P*n
sr=48000
x=np.frombuffer(subprocess.run(['ffmpeg','-v','error','-i','source_first_launch_suno.mp3','-ac','2','-ar',str(sr),'-f','f32le','-'],capture_output=True).stdout,np.float32).reshape(-1,2).astype(np.float64)
segs=[(0.0,bar(18)),(bar(94),bar(116)),(bar(155),len(x)/sr)]
X=int(0.04*sr); out=None; marks=[]
for a,b in segs:
  s=x[int(a*sr):int(b*sr)].copy()
  if out is None: out=s; marks.append(0.0); continue
  t=np.linspace(0,np.pi/2,X)[:,None]
  # кроссфейд 40 мс прямо на сильной доле
  tail=out[-X:]*np.cos(t); head=s[:X]*np.sin(t)
  start=len(out)-X; out=np.concatenate([out[:-X],tail+head,s[X:]]); marks.append(start/sr)
print('cuts at', [round(m,2) for m in marks], 'len', round(len(out)/sr,2))
pcm=(np.clip(out,-1,1)*32767).astype('<i2')
with wave.open('edit.wav','wb') as w: w.setnchannels(2); w.setsampwidth(2); w.setframerate(sr); w.writeframes(pcm.tobytes())
json.dump({'segments_source_seconds':[[round(a,3),round(b,3)] for a,b in segs],'cuts_in_edit':marks,'bpm':60/P,'beat':P,'first_downbeat_source':ph},open('edit.json','w'),indent=1)
