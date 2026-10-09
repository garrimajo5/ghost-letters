from pathlib import Path
ROOT=Path(__file__).resolve().parent
source=(ROOT/'create_revision4.py').read_text(encoding='utf-8-sig')
exec(compile(source.split('\nmain=[')[0],str(ROOT/'create_revision4.py'),'exec'))
OUT=ROOT/'previews_v5';OUT.mkdir(exist_ok=True)
rng=np.random.default_rng(810265)
manifest=[]

def singing(notes,duration,style=0,detune=0):
 t=np.arange(int(SR*duration))/SR
 times=[0,.22]; pitches=[notes[0][1]-.13+detune,notes[0][1]+detune]
 for at,p in notes[1:]:
  times.extend([max(times[-1]+.01,at-.12),at+.16])
  pitches.extend([pitches[-1],p+detune])
 times.append(duration);pitches.append(pitches[-1])
 midi=np.interp(t,times,pitches)
 vibdepth=[.045,.085,.055][style]
 vibrato=(vibdepth*np.sin(2*np.pi*(4.8+style*.13)*t)+.018*np.sin(2*np.pi*.47*t))*np.minimum(t/1.3,1)
 f=440*2**((midi+vibrato-69)/12)
 phase=2*np.pi*np.cumsum(f)/SR
 morph=np.sin(np.pi*t/duration)**1.5
 if style==0:
  f1=320+250*morph;f2=920+260*morph;f3=2800
 elif style==1:
  f1=420+360*morph;f2=1080+290*morph;f3=2950
 else:
  f1=300+170*morph;f2=900+160*morph;f3=2700
 y=np.zeros_like(t)
 for h in range(1,30):
  hz=h*f
  color=(.017+1.25*np.exp(-.5*((hz-f1)/135)**2)
    +.6*np.exp(-.5*((hz-f2)/185)**2)+.105*np.exp(-.5*((hz-f3)/260)**2))
  y+=np.sin(h*phase+.06*h)*color/h**1.62
 y+=noise(duration,950,5100)*[.014,.02,.018][style]
 attack=np.sin(np.minimum(t/[.9,.7,1.2][style],1)*np.pi/2)**2
 release=np.sin(np.minimum((duration-t)/1.15,1)*np.pi/2)**2
 env=attack*release*(.88+.12*morph)*(1+.035*np.sin(2*np.pi*.62*t))
 y*=env
 return stereo(y/max(abs(y).max(),1e-8))

def backdrop(notes,duration,roomlen):
 x=render(notes,0,duration,pan=57)
 x=sosfilt(butter(2,2100,fs=SR,output='sos'),x,axis=0)
 x=room(x,.8,roomlen)
 x*=.30/max(abs(x).max(),1e-8)
 return x

def make(name,label,piano,phrases,style,game=False,harmonies=None):
 duration=48
 x=backdrop(piano,duration,[3.2,4.1,4.8][style])
 voice=np.zeros_like(x)
 for at,notes,hold in phrases:
  place(voice,singing(notes,hold,style),at,pan=[-.06,.08,-.15][style])
 if harmonies:
  for at,notes,hold in harmonies:
   place(voice,singing(notes,hold,2,detune=.035),at,.31,pan=.24)
 voice=room(voice,[.64,.88,1.05][style],[2.9,4.4,5.0][style])
 level=([.075,.073,.08][style] if not game else [.046,.043,.050][style])
 voice*=level/max(abs(voice).max(),1e-8)
 x+=voice
 n=int(5*SR);x[-n:]*=np.linspace(1,0,n)[:,None]
 save(name,x,label,target=float(abs(x).max()))
 # Verify complete decoding and sane sample levels.
 decoded=decode(OUT/(name+'.mp3'))
 assert len(decoded)==int(duration*SR)
 assert np.isfinite(decoded).all() and .01<abs(decoded).max()<.9
 assert (OUT/(name+'.mp3')).stat().st_size<2_000_000

def piano(times,pitches,vel=34):
 return [(t,p,3.8,vel+(i%3-1)*2) for i,(t,p) in enumerate(zip(times,pitches))]

# A: intimate falling folk lullaby; mostly stepwise descending endings.
make('01A_lullaby_v5','1A — Колыбельная у окна',
 piano([.7,4.3,8.7,12.4,16.9,21,25.4,29.3,33.8,38,41.6],[74,69,77,74,72,69,70,74,69,65,74]),
 [(4.8,[(0,69),(2.8,67),(4.1,65),(6.0,67),(6.6,65)],8.6),
  (17.5,[(0,65),(2.6,67),(4.6,69),(6.5,67)],8.8),
  (30,[(0,67),(2.9,65),(4.9,64),(5.5,62)],9.8)],0)

make('02A_quiet_lullaby_v5','2A — Тихое расследование',
 piano([1,6.2,11.8,17.2,22.6,28,33.8,39.3],[69,65,74,72,69,67,65,62],32),
 [(7.0,[(0,65),(3.8,64),(5.1,62)],8.8),
  (22.5,[(0,67),(3.6,65),(5.7,64),(6.2,62)],9.0),
  (36,[(0,65),(3.0,62)],7.3)],0,True)

# B: open sustained pentatonic phrases with restrained grace notes.
make('01B_distant_folk_v5','1B — Далёкий напев',
 piano([1,4.7,9.2,13.6,18,22.4,26.8,31.1,35.6,40],[74,77,69,74,79,77,74,72,69,74],33),
 [(5.2,[(0,67),(1.7,69),(2.2,72),(5.3,69),(7.1,67)],9.7),
  (19.3,[(0,69),(2.6,72),(3.2,74),(6.4,72),(7.0,69)],10.0),
  (33.2,[(0,67),(2.0,69),(2.6,67),(4.4,65),(6.0,62)],9.4)],1)

make('02B_mist_folk_v5','2B — Напев в тумане',
 piano([1.2,6.9,12.1,17.7,23.4,28.9,34.4,40],[67,74,69,72,67,69,65,62],31),
 [(8,[(0,67),(3.2,69),(3.8,67),(6.1,65)],10.0),
  (24.5,[(0,69),(3.6,67),(6,65)],9.8),
  (37.8,[(0,65),(2.6,62)],6.6)],1,True)

# C: quiet voice with a distant consonant supporting line.
make('01C_two_voices_v5','1C — Два голоса',
 piano([.8,5.0,9.4,13.7,18.4,22.7,27.1,31.5,35.9,40],[74,69,77,74,70,69,74,72,65,74],33),
 [(5.8,[(0,69),(3.2,67),(5.3,65)],9.5),
  (19.7,[(0,70),(3.5,69),(6,67)],10),
  (34,[(0,67),(2.8,65),(5,62)],8.8)],2,
 harmonies=[(6.2,[(0,62),(3.2,62),(5.3,62)],9.3),
 (20.1,[(0,62),(3.5,65),(6,62)],9.6),
 (34.4,[(0,62),(2.8,62),(5,57)],8.6)])

make('02C_far_voices_v5','2C — Далёкие голоса',
 piano([1.3,7.2,13.1,19,24.9,30.8,36.7,41],[69,65,74,69,67,65,62,69],31),
 [(8.1,[(0,65),(4,62)],10.5),
  (25,[(0,67),(3.8,65),(6.2,62)],10.6),
  (39,[(0,65),(2.8,62)],6.3)],2,True,
 harmonies=[(8.5,[(0,57),(4,57)],10.1),
 (25.4,[(0,62),(3.8,57),(6.2,57)],10.2)])
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
(OUT/'NOTES.txt').write_text('Six original standalone listening previews. Voice is procedurally synthesized, wordless, female-style formant singing, not a recording of a vocalist. GeneralUser GS piano as documented in ../INSTRUMENT-LICENSE.txt. No game repository changes. Each clip is 48 seconds with a preview fade-out.',encoding='utf-8')

