from pathlib import Path
import sys, json, subprocess
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'tools'))
import numpy as np
from scipy.signal import butter,sosfilt,fftconvolve
from tinysoundfont import Synth
import imageio_ffmpeg
SR=44100
OUT=ROOT/'previews'; OUT.mkdir(exist_ok=True)
rng=np.random.default_rng(81026)
sf=str(ROOT/'GeneralUser-GS.sf2')
manifest=[]
def stereo(x):
 return np.column_stack((x,x)) if x.ndim==1 else x
def place(x,y,at,gain=1,pan=0):
 y=stereo(y).copy()*gain
 y[:,0]*=np.sqrt(1-pan); y[:,1]*=np.sqrt(1+pan)
 a=max(0,int(at*SR)); n=min(len(y),len(x)-a)
 if n>0:x[a:a+n]+=y[:n]
def room(x,wet=.2,length=2.4):
 ir=np.zeros((int(length*SR),2))
 for c in range(2):
  for sec,amp in [(.041,.23),(.079,.18),(.127,.12),(.193,.10)]:
   ir[int((sec+c*.007)*SR),c]=amp
  n=len(ir)-int(.22*SR)
  tail=rng.normal(size=n)
  tail=sosfilt(butter(2,2900,fs=SR,output='sos'),tail)
  tail*=np.exp(-np.arange(n)/SR*3.8/length)*.005
  ir[int(.22*SR):,c]+=tail
 return x+wet*np.column_stack([fftconvolve(x[:,c],ir[:,c])[:len(x)] for c in range(2)])
def render(notes,program,duration,pan=64):
 s=Synth(gain=-9,samplerate=SR); ident=s.sfload(sf)
 s.program_select(0,ident,0,program)
 s.control_change(0,10,pan)
 events=[]
 for at,pitch,hold,vel in notes:
  events.extend([(int(at*SR),1,pitch,vel),(int((at+hold)*SR),0,pitch,0)])
 events.sort()
 n=int(duration*SR); x=np.zeros((n,2),dtype=np.float32); pos=0
 for sample,kind,pitch,vel in events+[(n,2,0,0)]:
  sample=min(n,max(pos,sample))
  if sample>pos:
   x[pos:sample]=np.frombuffer(s.generate(sample-pos),dtype=np.float32).reshape(-1,2)
   pos=sample
  if kind==1:s.noteon(0,pitch,vel)
  elif kind==0:s.noteoff(0,pitch)
 return x
def noise(duration,lo,hi):
 n=int(duration*SR)
 return sosfilt(butter(2,[lo,hi],btype='band',fs=SR,output='sos'),rng.normal(size=n))
def clock(high=True):
 t=np.arange(int(.095*SR))/SR
 x=(np.sin(2*np.pi*(1750 if high else 1120)*t)*np.exp(-t*105)
  +.5*np.sin(2*np.pi*520*t)*np.exp(-t*70))
 x+=noise(.095,1000,7000)*np.exp(-t*190)*.7
 x*=np.minimum(t/.0015,1)
 return stereo(x*.09)
def airy(duration,rising=False):
 x=noise(duration,550,4600)
 env=np.sin(np.linspace(0,np.pi,len(x)))**1.7
 if rising: env*=np.linspace(.05,1,len(x))
 return stereo(x*env*.12)
def bell(freq,duration=1):
 t=np.arange(int(duration*SR))/SR
 x=sum(a*np.sin(2*np.pi*freq*h*t)*np.exp(-t*d) for h,a,d in [(1,1,4),(2.71,.18,7),(4.12,.07,13)])
 x*=(1-np.exp(-t*170))
 x[-int(.04*SR):]*=np.linspace(1,0,int(.04*SR))
 return stereo(x*.12)
def save(name,x,label,target=.66,rate='160k'):
 x=stereo(x)
 peak=np.max(np.abs(x))
 if peak: x=x/peak*target
 # Gentle edge fades, preview endings deliberately fade rather than looping.
 fade=min(int((.002 if len(x)<5*SR else .025)*SR),len(x)//4)
 x[:fade]*=np.linspace(0,1,fade)[:,None]
 x[-fade:]*=np.linspace(1,0,fade)[:,None]
 assert np.isfinite(x).all() and np.max(np.abs(x))<1
 path=OUT/(name+'.mp3')
 subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(),'-y','-v','error','-f','f32le','-ar',str(SR),'-ac','2','-i','pipe:0','-c:a','libmp3lame','-b:a',rate,'-write_xing','1',str(path)],input=x.astype('<f4').tobytes(),check=True)
 manifest.append(dict(file=path.name,event=label,seconds=round(len(x)/SR,2),bytes=path.stat().st_size))
 print(path.name,manifest[-1]['seconds'],manifest[-1]['bytes'])

# Original chamber theme in D minor, slow 6/8, with clock-only opening.
duration=72
piano=[]
chords=[[50,57,62,65,69,76],[46,53,62,65,69,74],[43,50,58,62,64,69],[45,52,61,64,67,70],
        [50,57,62,65,69,76],[46,53,60,65,69,74],[43,50,58,62,65,69],[45,52,61,64,67,70],
        [50,57,62,65,69,74],[50,57,62,64,65,69]]
for bar,chord in enumerate(chords):
 at=6+bar*6
 piano.append((at,chord[0],4.8,43+min(bar*2,12)))
 for j,idx in enumerate([2,3,4,3,5,4]):
  piano.append((at+.72+j*.75+float(rng.uniform(-.025,.025)),chord[idx],1.55,39+min(bar*2,14)+(4 if j==0 else 0)))
p=room(render(piano,0,duration,pan=53),.7,3.3)
p*=np.clip((np.arange(len(p))/SR-5)/8,0,1)[:,None]
vnotes=[(18,74,4.6,51),(23,72,2.5,48),(26,70,3.4,52),(30,69,4.3,50),
 (35,73,2.3,48),(38,74,4.7,57),(43.2,77,2.4,55),(46,76,4.8,54),
 (51.2,74,2.1,51),(54,73,3.9,53),(58.3,69,2.7,47),(62,74,4.8,45)]
v=render(vnotes,40,duration,pan=78)
env=np.ones(len(v))*.85
# Fade each bowed phrase in/out to soften MIDI attacks.
for at,pitch,hold,vel in vnotes:
 a=int(at*SR); b=min(len(v),int((at+hold)*SR))
 fade=min(int(.55*SR),(b-a)//2)
 env[a:a+fade]*=np.linspace(.08,1,fade)
 env[b-fade:b]*=np.linspace(1,.18,fade)
v=room(v*env[:,None],.8,3.8)
x=p+v*.48
for i in range(88):
 at=.3+i*.75
 place(x,clock(i%2==0),at,gain=.8 if at<6 else .40,pan=-.23 if i%2 else .23)
x[-int(6*SR):]*=np.linspace(1,0,int(6*SR))[:,None]
save('01_main_clock_piano_violin_v2',x,'Главное меню и лобби — часы, затем пианино и скрипка')

# Sparse investigation bed: familiar harmonic language, less foreground melody.
duration=54; pn=[]; vn=[]
for bar,chord in enumerate(chords[:8]):
 at=2+bar*6
 pn.append((at,chord[0],4,43))
 for j,idx in enumerate([2,4,3,5]):pn.append((at+.8+j*1.13,chord[idx],2,38+j%2*4))
 if bar%2==0:vn.append((at+2,chord[3]+12,6.8,42))
x=room(render(pn,0,duration,52),.65,3)+room(render(vn,40,duration,77),.8,3.8)*.30
x[:int(3*SR)]*=np.linspace(0,1,int(3*SR))[:,None]
x[-int(6*SR):]*=np.linspace(1,0,int(6*SR))[:,None]
save('02_investigation_v2',x,'Фон партии — тихое расследование',target=.55)
def empty(sec):return np.zeros((int(sec*SR),2))
x=empty(1.15);place(x,bell(293.66,.9),.03,.48);place(x,clock(False),0,.35)
save('03_phase_v2',room(x,.35),'Смена фазы',.30,'128k')
x=render([(0,69,.28,48),(.22,74,.7,57)],0,1.7)
save('04_your_turn_v2',room(x,.5),'Начинается ваш ход',.43,'128k')
x=empty(.85)
r=noise(.45,800,6200)*np.sin(np.linspace(0,np.pi,int(.45*SR)))**2*.09
place(x,r,0);place(x,clock(False),.42,.8)
save('05_letter_sent_v2',room(x,.12,.6),'Своё письмо отправлено',.28,'128k')
x=empty(1.7);place(x,airy(.55,True),0,.45)
place(x,bell(587.33,1.2),.4,.42);place(x,bell(880,1),.56,.18)
save('06_reveal_v2',room(x,.65),'Призрак открыл подсказки или ваше письмо',.36,'128k')
x=empty(1.35);place(x,airy(.95),0,.7)
t=np.arange(int(.85*SR))/SR
place(x,np.sin(2*np.pi*(260*t-60*t*t))*np.sin(np.pi*np.arange(len(t))/len(t))*.025,.12)
save('07_vanish_v2',room(x,.35),'Письмо исчезло',.26,'128k')
x=empty(.55);place(x,bell(783.99,.45),0,.3)
save('08_chat_v2',x,'Новое сообщение при свёрнутом чате',.22,'128k')
x=empty(.22);place(x,clock(True),.01,.7)
save('09_timer_tick_v2',x,'Каждая из последних десяти секунд',.20,'128k')
x=empty(1.3);place(x,clock(False),0,.6);place(x,bell(392,1),.12,.3)
save('10_timer_end_v2',room(x,.3),'Время обсуждения закончилось',.34,'128k')
x=empty(.5);place(x,clock(False),0,.8);place(x,bell(440,.4),.035,.1)
save('11_vote_v2',room(x,.2,.5),'Ваш голос принят',.25,'128k')
x=render([(0,62,.65,48),(.14,65,.7,46),(.3,69,.9,52)],0,2.0)
save('12_correct_v2',room(x,.6),'Ряд угадан верно',.4,'128k')
x=render([(0,64,.55,46),(.25,61,.8,45),(.25,57,.8,35)],0,1.9)
save('13_incorrect_v2',room(x,.55),'Ряд угадан неверно',.35,'128k')
x=render([(0,50,2,48),(.08,62,1.3,52),(.48,65,1.5,53),(.92,69,1.6,54),(1.4,74,1.8,54)],0,4.2)
x+=render([(.6,74,2.4,45)],40,4.2,77)*.25
save('14_victory_v2',room(x,.65),'Победа — спокойное разрешение тайны',.46,'128k')
x=render([(0,50,2.2,43),(.12,69,1,48),(.72,65,1.2,45),(1.4,62,1.6,43)],0,4.2)
x+=render([(.5,61,2.1,34)],40,4.2,77)*.17
save('15_defeat_v2',room(x,.6),'Поражение — незавершённая история',.40,'128k')
x=empty(.18);place(x,clock(False),0,.35)
save('16_button_v2',x,'Тихое подтверждение кнопки «Готово» или карты',.15,'128k')
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
# A single comparison reel with gaps, in the same order as the individual effects.
reel=[]; markers=[]; cursor=0
for item in manifest[2:]:
 raw=subprocess.check_output([imageio_ffmpeg.get_ffmpeg_exe(),'-v','error','-i',str(OUT/item['file']),'-f','f32le','-ar',str(SR),'-ac','2','-'])
 audio=np.frombuffer(raw,dtype='<f4').reshape(-1,2)
 markers.append(f"{cursor:.1f}s — {item['event']}")
 reel.extend([audio,np.zeros((int(.8*SR),2))]);cursor+=len(audio)/SR+.8
save('00_all_effects_v2',np.concatenate(reel),'Все эффекты по порядку',.46,'128k')
(OUT/'LISTENING_ORDER.txt').write_text('\n'.join(markers),encoding='utf-8')
(ROOT/'SOURCES.txt').write_text('Original compositions and sound designs, Ghost Letters review v2, 2026-10-08.\nPiano and violin rendered with GeneralUser GS 2.0.3 by S. Christian Collins.\nSource: https://github.com/mrbumpy409/GeneralUser-GS\nLicense: INSTRUMENT-LICENSE.txt (permits private and commercial music creation).\nClock, paper and airy effects: original procedural synthesis.\nThese are standalone review previews; game repository was not modified.\n',encoding='utf-8')


