from pathlib import Path
ROOT=Path(__file__).resolve().parent
source=(ROOT/'create_previews.py').read_text(encoding='utf-8-sig')
exec(compile(source.split('# Original chamber theme')[0],str(ROOT/'create_previews.py'),'exec'))
OUT=ROOT/'previews_v4';OUT.mkdir(exist_ok=True)
rng=np.random.default_rng(810264)

def vocal_phrase(notes,duration,air=.02):
 t=np.arange(int(SR*duration))/SR
 # Connected notes with small scoops; one continuous breath per phrase.
 times=[0,.18];pitches=[notes[0][1]-.28,notes[0][1]]
 for at,pitch in notes:
  if at>0:
   times.extend([max(times[-1]+.001,at-.13),at+.16])
   pitches.extend([pitches[-1],pitch])
 times.append(duration);pitches.append(pitches[-1])
 midi=np.interp(t,times,pitches)
 vibrato=(.075*np.sin(2*np.pi*5.1*t)+.015*np.sin(2*np.pi*.43*t))*np.minimum(t/1.1,1)
 freq=440*2**((midi+vibrato-69)/12)
 phase=2*np.pi*np.cumsum(freq)/SR
 # Smooth vowel morph: oo -> ah -> oo. Formants emulate a light female voice.
 vowel=np.sin(np.pi*t/duration)**1.8
 f1=360+430*vowel;f2=920+390*vowel;f3=2850+180*vowel
 y=np.zeros_like(t)
 for h in range(1,35):
  hz=h*freq
  color=(.02+1.25*np.exp(-.5*((hz-f1)/125)**2)
    +.62*np.exp(-.5*((hz-f2)/170)**2)+.14*np.exp(-.5*((hz-f3)/240)**2))
  y+=np.sin(h*phase+.06*h)*color/h**1.6
 breath=noise(duration,900,5400)
 y+=breath*air
 attack=np.sin(np.minimum(t/.85,1)*np.pi/2)**2
 release=np.sin(np.minimum((duration-t)/1.1,1)*np.pi/2)**2
 env=attack*release*(.86+.14*np.sin(np.pi*t/duration))*(1+.035*np.sin(2*np.pi*.73*t))
 y*=env
 y/=max(np.max(np.abs(y)),1e-8)
 return stereo(y)

def decode(path):
 raw=subprocess.check_output([imageio_ffmpeg.get_ffmpeg_exe(),'-v','error','-i',str(path),'-f','f32le','-ar',str(SR),'-ac','2','-'])
 return np.frombuffer(raw,dtype='<f4').reshape(-1,2).copy()

def add_vocals(source_file,name,phrases,level,label):
 base=decode(ROOT/'previews_v3'/source_file)
 voice=np.zeros_like(base)
 for at,notes,length in phrases:
  phrase=vocal_phrase(notes,length)
  place(voice,phrase,at,1,pan=.08)
 voice=room(voice,.95,4.6)
 # A soft reflected answer on the other side, without a rhythmic echo.
 shift=int(.137*SR)
 voice[shift:]+=voice[:-shift,::-1]*.12
 voice*=level/max(np.max(np.abs(voice)),1e-8)
 voice[-int(5*SR):]*=np.linspace(1,0,int(5*SR))[:,None]
 mix=base+voice
 # Keep the previous piano level, only add the quiet vocal layer.
 save(name,mix,label,target=float(np.max(np.abs(mix))))
main=[
 (6.6,[(0,69),(2.5,67),(3.4,65),(5.7,67),(6.2,69)],8.1),
 (19.2,[(0,65),(2.9,67),(4.1,69),(6.0,67)],8.4),
 (31.4,[(0,70),(2.6,69),(4.0,67),(6.1,65)],8.5),
 (44.0,[(0,67),(2.7,65),(4.1,64),(4.6,62)],9.2)]
game=[
 (8.0,[(0,65),(3.5,67),(5.3,65)],8.8),
 (23.1,[(0,67),(3.1,69),(5.1,67),(6.1,65)],9.1),
 (39.2,[(0,65),(3.4,64),(4.0,62)],10.2)]
add_vocals('01_main_droplets_v3.mp3','01_main_folk_vocal_v4',main,.077,
 'Главная тема — редкое пианино и синтезированный женский фолк-вокализ без слов')
add_vocals('02_investigation_droplets_v3.mp3','02_investigation_folk_vocal_v4',game,.052,
 'Фон партии — мягкое пианино и более далёкий синтезированный вокализ')
for item in manifest:
 x=decode(OUT/item['file'])
 assert np.isfinite(x).all() and .01<abs(x).max()<.98
 assert item['bytes']<2_000_000
 print('Verified',item['file'],'peak',round(float(abs(x).max()),3))
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
(OUT/'VOCAL.txt').write_text('Original procedurally synthesized wordless female-style formant vocal. No singer recording, voice cloning or lyrics used. Piano/string bed is the previously reviewed v3 composition. Standalone previews; no game changes.',encoding='utf-8')


