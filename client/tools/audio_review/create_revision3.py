from pathlib import Path
ROOT=Path(__file__).resolve().parent
# Reuse only rendering helpers, without regenerating any approved v2 previews.
source=(ROOT/'create_previews.py').read_text(encoding='utf-8-sig')
exec(compile(source.split('# Original chamber theme')[0],str(ROOT/'create_previews.py'),'exec'))
OUT=ROOT/'previews_v3'; OUT.mkdir(exist_ok=True)
rng=np.random.default_rng(810263)

def soften(x,cutoff=2300):
 return sosfilt(butter(2,cutoff,fs=SR,output='sos'),x,axis=0)
def soft_strings(notes,duration):
 x=soften(render(notes,40,duration,79),1400)
 envelope=np.ones(len(x))
 for at,pitch,hold,vel in notes:
  a=int(at*SR); b=min(len(x),int((at+hold)*SR))
  fade=min(int(1.2*SR),(b-a)//2)
  envelope[a:a+fade]*=np.sin(np.linspace(0,np.pi/2,fade))**2
  envelope[b-fade:b]*=np.linspace(1,.05,fade)
 return room(x*envelope[:,None],.9,4.2)
def droplets(name,duration,notes,strings,label,level):
 # Irregular isolated soft piano notes. No clock, percussion or repeating ostinato.
 piano=soften(render(notes,0,duration,57))
 wet=room(piano,.9,4.3)
 # Low, diffuse echoes rather than another distinct piano strike.
 for delay,gain in [( .29,.11),(.71,.07),(1.13,.035)]:
  shifted=np.zeros_like(wet)
  n=int(delay*SR)
  shifted[n:]=piano[:-n,::-1]*gain
  wet+=soften(shifted,1700)
 strings=soft_strings(strings,duration)
 pp=max(np.max(np.abs(wet)),1e-8)
 sp=max(np.max(np.abs(strings)),1e-8)
 x=wet+strings*(pp/sp)*.075
 # A long soft exit is only for this listening preview.
 fade=int(5.5*SR)
 x[-fade:]*=np.linspace(1,0,fade)[:,None]
 save(name,x,label,target=level)

main=[
 (1.0,74,3.8,39),(4.6,69,3.2,35),(8.4,77,4.0,37),
 (12.9,76,3.7,34),(16.2,74,4.2,36),(20.8,72,3.6,34),
 (24.2,69,4.0,35),(28.7,70,3.8,34),(32.3,74,4.2,37),
 (36.9,69,3.6,34),(40.2,65,4.1,33),(44.8,67,3.9,35),
 (48.4,69,4,34),(52.9,74,3.8,34)]
droplets('01_main_droplets_v3',62,main,
 [(7,62,9,32),(20,65,9,30),(33,62,9,31),(46,57,9,28)],
 'Главная тема: мягкие редкие капли пианино, без часов',.38)

game=[
 (1.4,69,4.2,34),(6.2,65,4.2,32),(10.1,74,4.2,35),
 (15.3,72,4.3,31),(19.8,69,4.1,33),(24.5,67,4.5,32),
 (29.7,70,4.3,34),(34.1,69,4.3,31),(39.2,65,4.3,32),
 (44.6,62,4.4,31),(49.8,69,4.2,30)]
droplets('02_investigation_droplets_v3',60,game,
 [(5,50,10,28),(20,53,10,27),(35,57,11,27)],
 'Фон партии: ещё реже и тише, мягкое эхо пианино',.32)

# A cheerful D-major flourish, buoyant offbeats and a clear major resolution.
duration=5.2
melody=[(0,62,.32,59),(.2,66,.32,62),(.4,69,.35,65),
 (.7,74,.38,68),(1.05,73,.28,58),(1.27,74,.32,64),
 (1.6,78,.45,68),(2.08,76,.35,62),(2.45,74,1.7,65)]
chords=[(0,50,.55,48),(.7,57,.42,44),(1.4,59,.5,47),(2.4,50,1.7,48),
 (2.46,62,1.7,46),(2.48,66,1.7,47),(2.50,69,1.7,45)]
p=render(melody+chords,0,duration,55)
plucks=render([(0,62,.2,51),(.35,69,.2,48),(.7,66,.2,50),
 (1.05,69,.2,49),(1.4,71,.2,52),(1.75,74,.2,50),(2.1,69,.2,49),
 (2.45,74,.45,55)],45,duration,77)
x=room(p+.32*plucks,.48,1.7)
fade=int(.7*SR);x[-fade:]*=np.linspace(1,0,fade)[:,None]
save('14_victory_happy_v3',x,'Победа: светлый мажор, бодрый подъём и радостное завершение',.49,'128k')

for item in manifest:
 path=OUT/item['file']
 raw=subprocess.check_output([imageio_ffmpeg.get_ffmpeg_exe(),'-v','error','-i',str(path),'-f','f32le','-ar',str(SR),'-ac','2','-'])
 decoded=np.frombuffer(raw,dtype='<f4')
 assert np.isfinite(decoded).all() and .001<np.max(np.abs(decoded))<.99
 print('Verified:',path.name,'decoded peak',round(float(np.max(np.abs(decoded))),3))
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')

