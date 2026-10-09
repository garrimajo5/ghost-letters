"""Check the brief and export contact sheets from the encoded MP4s."""
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
from PIL import Image, ImageDraw, ImageFont
import build as b

PROBE=os.environ.get('FFPROBE') or shutil.which('ffprobe') or str(b.WORK/'tools/ffprobe.exe')
markers=b.timeline_markers(b.CFG,b.ROOT)
beats=[bar+k*markers['beat_seconds'] for bar in markers['bars'] for k in range(4)]

def loudness(path):
    result=subprocess.run([b.FF,'-hide_banner','-nostats','-i',str(path),'-af','ebur128=peak=true','-f','null','-'],capture_output=True,text=True,check=True)
    summary=result.stderr.split('Summary:')[-1]
    return {'integrated_lufs':float(re.search(r'I:\s*([-\d.]+) LUFS',summary)[1]),'true_peak_dbfs':float(re.search(r'Peak:\s*([-\d.]+)',summary)[1])}

def faststart(path):
    atoms=[]
    with path.open('rb') as f:
        while True:
            head=f.read(8)
            if len(head)<8:break
            size,kind=struct.unpack('>I4s',head);header=8
            if size==1:size=struct.unpack('>Q',f.read(8))[0];header=16
            if size==0:break
            atoms.append(kind.decode('ascii'));f.seek(size-header,1)
    return atoms.index('moov')<atoms.index('mdat')

def night_levels(path):
    edit=b.CFG.get('music_edit')
    if not edit:return {}
    cut=edit['at'];resume=cut+edit['pause_seconds']
    def rms(start,duration):
        data=subprocess.check_output([b.FF,'-v','error','-ss',str(start),'-i',str(path),
                                      '-t',str(duration),'-vn','-ac','1','-f','f32le','-'])
        samples=b.np.frombuffer(data,dtype='<f4')
        assert len(samples)>0
        return round(float(20*b.np.log10(max(1e-10,b.np.sqrt(b.np.mean(samples**2))))),2)
    levels={'before_fade_dbfs':rms(edit['fade_out_start']-.4,.3),
            'fade_tail_dbfs':rms(cut-.3,.2),
            'night_pause_dbfs':rms(cut+.2,edit['pause_seconds']-.4),
            'resume_dbfs':rms(resume+.05,.4)}
    assert levels['fade_tail_dbfs']<levels['before_fade_dbfs']-12,levels
    assert levels['night_pause_dbfs']<-80,levels
    assert levels['resume_dbfs']>-45,levels
    return levels

def verify():
    events=[]
    for s in b.CFG['scenes']:
        if s['start']:
            events.append((f"scene {s['id']:02}",s['start'],round(s['start']*b.FPS)))
        for k,step in enumerate(s.get('steps',[])):
            events.append((f"scene {s['id']:02} beat {k+1}",step['at'],math.ceil(step['at']*b.FPS)))
        if s.get('black_after'):events.append(('black beat',s['black_after'],math.ceil(s['black_after']*b.FPS)))
    rows=[]
    print('scene                    planned    frame time nearest beat   error ms')
    for name,planned,frame in events:
        nearest=min(beats,key=lambda x:abs(x-planned));actual=frame/b.FPS;error=abs(actual-nearest)
        assert abs(planned-nearest)<.00001,(name,planned,nearest)
        assert error<=1/b.FPS+1e-6,(name,error)
        rows.append({'event':name,'planned':planned,'frame':frame,'actual_seconds':actual,'nearest_beat':nearest,'error_ms':round(error*1000,3)})
        print(f'{name:24} {planned:8.3f} {actual:11.3f} {nearest:12.3f} {error*1000:10.3f}')
    assert all(t['end']-t['start']>=1.5 for t in b.CFG['titles'])
    source=b.ROOT/b.CFG['music']
    edited=b.build_music(b.CFG,b.ROOT,b.WORK,b.FF)
    report={'timing':rows,'max_error_ms':max(r['error_ms'] for r in rows),'music_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'music_loudness':loudness(source),'edited_music_loudness':loudness(edited),'music_edit':b.CFG.get('music_edit'),'outputs':[]}
    for orient in ['16x9','9x16']:
        path=b.OUT/f'ghost-letters-trailer-{orient}.mp4'
        if not path.exists():continue
        data=json.loads(subprocess.check_output([PROBE,'-v','error','-show_streams','-show_format','-of','json',str(path)]))
        video=next(s for s in data['streams'] if s['codec_type']=='video');audio=next(s for s in data['streams'] if s['codec_type']=='audio')
        expected=(1920,1080) if orient=='16x9' else (1080,1920)
        assert (video['width'],video['height'])==expected
        assert video['r_frame_rate']=='30/1' and video['codec_name']=='h264' and video['pix_fmt']=='yuv420p'
        assert audio['codec_name']=='aac' and 180000<int(audio['bit_rate'])<205000
        duration=float(data['format']['duration']);assert abs(duration-b.CFG['duration'])<=1/b.FPS+.001
        assert path.stat().st_size<=40000000
        assert faststart(path)
        level=loudness(path);assert -14.5<=level['integrated_lufs']<=-13.5
        subprocess.run([b.FF,'-v','error','-i',str(path),'-f','null','-'],check=True)
        thumb=(400,225) if orient=='16x9' else (225,400)
        sheet=Image.new('RGB',(thumb[0]*5,(thumb[1]+32)*math.ceil(len(b.CFG['scenes'])/5)),'#0D1724');d=ImageDraw.Draw(sheet)
        framesdir=b.OUT/f'frames-{orient}';framesdir.mkdir(exist_ok=True)
        for i,s in enumerate(b.CFG['scenes']):
            mid=(s['start']+s['end'])/2
            filename=framesdir/f'{s["id"]:02}.jpg'
            b.run(['-ss',mid,'-i',path,'-frames:v','1','-q:v','2',filename])
            im=Image.open(filename);im.thumbnail(thumb,Image.Resampling.LANCZOS)
            x=i%5*thumb[0];y=i//5*(thumb[1]+32);sheet.paste(im,(x,y))
            d.text((x+7,y+thumb[1]+6),f'{s["id"]:02} | {mid:05.2f}s',fill='#E3A94B',font=b.font(16,False))
        sheet.save(b.OUT/('contact_sheet.jpg' if orient=='16x9' else 'contact_sheet_9x16.jpg'),quality=92)
        last=b.WORK/f'last-{orient}.png';b.run(['-ss',b.CFG['black_at']+.1,'-i',path,'-frames:v','1',last])
        assert max(Image.open(last).convert('RGB').getextrema()[c][1] for c in range(3))<=3
        report['outputs'].append({'file':path.name,'bytes':path.stat().st_size,'duration':duration,'dimensions':expected,'fps':video['r_frame_rate'],'video_codec':video['codec_name'],'pixel_format':video['pix_fmt'],'audio_bitrate':audio['bit_rate'],'faststart':True,'black_tail':True,'night_levels':night_levels(path),**level})
    assert len(report['outputs'])==2,'Both formats required for this delivery'
    (b.OUT/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report['outputs'],indent=2))

if __name__=='__main__':verify()
