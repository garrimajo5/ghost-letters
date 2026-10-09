"""Reproducible trailer renderer. Run: python trailer/build.py --all

No API or network needed. All editorial decisions live in scenes.json.
"""
import argparse
import functools
import json
import math
import os
from pathlib import Path
import subprocess
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageOps, ImageEnhance
import imageio_ffmpeg
from music_edit import build_music, timeline_markers

ROOT = Path(__file__).resolve().parent
CFG = json.loads((ROOT / 'scenes.json').read_text(encoding='utf-8'))
FF = os.environ.get('FFMPEG', imageio_ffmpeg.get_ffmpeg_exe())
FPS = CFG['fps']
WORK = ROOT / '.work'
OUT = ROOT / 'out'
WORK.mkdir(exist_ok=True)
OUT.mkdir(exist_ok=True)
R = Image.Resampling.LANCZOS

def run(args):
    subprocess.run([FF, '-y', '-v', 'error', *map(str, args)], check=True)

@functools.lru_cache(maxsize=100)
def original(path):
    im=Image.open(ROOT / path).convert('RGBA')
    crop=CFG.get('asset_crops',{}).get(path)
    if crop: im=im.crop(tuple(round(v*(im.width if i%2==0 else im.height)) for i,v in enumerate(crop)))
    return im

@functools.lru_cache(maxsize=150)
def fitted(path, maxw, maxh, device=False):
    im = original(path).copy()
    # Protect small game illustrations from excessive enlargement.
    scale = min(maxw / im.width, maxh / im.height, 1.6 if not device else 1)
    im = im.resize((round(im.width * scale), round(im.height * scale)), R)
    if device:
        border = 10
        mask = Image.new('L', im.size)
        ImageDraw.Draw(mask).rounded_rectangle((0,0,im.width-1,im.height-1),radius=26,fill=255)
        im.putalpha(mask)
        frame = Image.new('RGBA',(im.width+20,im.height+20))
        ImageDraw.Draw(frame).rounded_rectangle((0,0,frame.width-1,frame.height-1),radius=34,fill='#0A111B',outline='#2B4260',width=3)
        frame.alpha_composite(im,(border,border))
        return frame
    return im

def paste_center(canvas, asset, cx, cy, alpha=1, angle=0):
    if angle: asset = asset.rotate(angle, Image.Resampling.BICUBIC, expand=True)
    if alpha < 1:
        asset = asset.copy()
        asset.putalpha(asset.getchannel('A').point(lambda x: int(x*max(0,alpha))))
    canvas.alpha_composite(asset,(round(cx-asset.width/2),round(cy-asset.height/2)))

def ease(x):
    x = max(0,min(1,x)); return x*x*(3-2*x)

@functools.lru_cache(maxsize=50)
def font(size, heading=True):
    return ImageFont.truetype(str(ROOT / CFG['fonts']['heading' if heading else 'body']),size)

def tracked(draw, text, center, y, size, color, spacing=2):
    f=font(size); widths=[draw.textlength(ch,font=f) for ch in text]
    x=center-(sum(widths)+spacing*(len(text)-1))/2
    for ch,w in zip(text,widths): draw.text((x,y),ch,font=f,fill=color);x+=w+spacing

@functools.lru_cache(maxsize=50)
def title_image(lines, width, size, color):
    layer=Image.new('RGBA',(width,size*len(lines)*2+70));d=ImageDraw.Draw(layer)
    # Configured line breaks are fixed; shrink only to respect the 5% safe zone.
    while max(d.textlength(line,font=font(size))+2*len(line) for line in lines)>width*.89: size-=1
    for i,line in enumerate(lines):tracked(d,line,width/2,8+i*(size+15),size,color)
    return layer

def make_background(w,h):
    yy,xx=np.mgrid[0:h,0:w];glow=np.exp(-((xx-w*.55)**2/(w*.48)**2+(yy-h*.48)**2/(h*.6)**2))
    rng=np.random.default_rng(71)
    grain=rng.normal(0,.65,(h,w))
    pixels=np.stack([10+glow*7+grain,17+glow*15+grain,27+glow*22+grain],axis=-1).clip(0,255).astype('uint8')
    base=Image.fromarray(pixels).convert('RGBA')
    fog=Image.fromarray((rng.random((45,80))*255).astype('uint8')).resize((w+400,h+200),Image.Resampling.BICUBIC).filter(ImageFilter.GaussianBlur(45))
    fog.putalpha(fog.point(lambda p:int(p*.19)))
    tint=Image.new('RGBA',fog.size,(132,173,183,0));tint.putalpha(fog.getchannel('A'))
    return base,tint

def motion_files():
    for c in [4,6,8,11,12]:
        frames=ROOT/f'captures/motion/C{c}/000.png'
        movie=ROOT/f'captures/C{c}-motion.mp4'
        if frames.exists() and (not movie.exists() or movie.stat().st_size==0 or frames.stat().st_mtime>movie.stat().st_mtime):
            run(['-framerate','12','-i',frames.parent/'%03d.png','-vf','pad=ceil(iw/2)*2:ceil(ih/2)*2','-c:v','libx264','-crf','21','-pix_fmt','yuv420p','-movflags','+faststart',movie])

@functools.lru_cache(maxsize=5)
def motion_reader(path,w,h):
    crop=CFG.get('asset_crops',{}).get(path)
    crop_tag='-'.join(map(str,crop or []))
    dest=WORK/(Path(path).stem+f'-{w}x{h}-{int((ROOT/path).stat().st_mtime)}-{crop_tag}')
    dest.mkdir(exist_ok=True)
    if not (dest/'047.png').exists():
        crop_filter=f'crop=iw*{crop[2]-crop[0]}:ih*{crop[3]-crop[1]}:iw*{crop[0]}:ih*{crop[1]},' if crop else ''
        run(['-i',ROOT/path,'-vf',crop_filter+f'fps=12,scale={w}:{h}:force_original_aspect_ratio=decrease','-start_number','0',dest/'%03d.png'])
    return dest

def render_frame(t, scene, dims, bg, fog):
    w,h=dims;vertical=h>w;local=t-scene['start'];dur=scene['end']-scene['start'];q=local/dur
    canvas=bg.copy()
    # Slow fog drift is independent from the foreground parallax.
    canvas.alpha_composite(fog,(-200+int(55*math.sin(t*.14)),-100+int(30*math.cos(t*.09))))
    kind=scene['kind'];assets=scene.get('assets',[])
    active=assets
    if 'steps' in scene:
        active=[]
        for step in scene['steps']:
            if t>=step['at']:active=step['assets']
    cy=h*.57 if vertical else h*.56
    if kind=='city':
        # Wide establishing shot; portrait keeps the central mansion and street.
        city_h=round(h*.74) if vertical else h
        source=original(active[0])
        assert max(w/source.width,city_h/source.height)*1.025<=1.6
        image=ImageOps.fit(source,(w,city_h),R,centering=(.54,.5))
        zoom=1+.025*ease(q)
        image=image.resize((round(w*zoom),round(city_h*zoom)),Image.Resampling.BICUBIC)
        image=ImageEnhance.Brightness(image).enhance(.9-.32*ease(q))
        paste_center(canvas,image,w*.5,cy if vertical else h*.5,alpha=ease(local/.22))
        canvas.alpha_composite(fog,(-200+int(55*math.sin(t*.14)),-100+int(30*math.cos(t*.09))))
    elif kind in ('cards','rolefan','recap'):
        n=len(active)
        for i,path in enumerate(active):
            device=path.startswith('captures/')
            if n==1:
                mw,mh=(w*.79,h*.63) if vertical else (w*.62,h*.72)
                cx=w*.5;yy=cy
            else:
                mw,mh=(w*.55,h*.37) if vertical else (w*.28,h*.54)
                cx=w*(.5+(i-(n-1)/2)*(.19 if vertical else .2));yy=cy+(i%2)*35
            im=fitted(path,round(mw),round(mh),device)
            z=1+.035*q
            # A maximum 1.6x bound also includes the Ken Burns motion.
            if not device:z=min(z,original(path).width*1.6/im.width,original(path).height*1.6/im.height)
            if z!=1:im=im.resize((int(im.width*z),int(im.height*z)),Image.Resampling.BICUBIC)
            angle=(i-(n-1)/2)*6+math.sin(t*.3+i)*1.2 if n>1 else 0
            paste_center(canvas,im,cx+math.sin(t*.35+i)*10,yy,angle=angle)
    elif kind in ('screen','devices'):
        n=len(active)
        for i,path in enumerate(active):
            wide=original(path).width>original(path).height if not path.endswith('.mp4') else False
            mw,mh=(w*.82,h*.64) if vertical else (w*.77 if wide else w*.4,h*.68)
            if n>1:mw,mh=(w*.78,h*.38) if vertical else (w*.5,h*.70)
            cx=w*.5 if vertical or n==1 else w*(.31+i*.4)
            yy=(h*(.37+i*.39) if vertical and n>1 else cy)
            if path.endswith('.mp4'):
                # 12 fps native UI recording is played in time, never speed changed.
                source=original(path.replace('-motion.mp4','.png'))
                crop=CFG.get('asset_crops',{}).get(path)
                sw,sh=source.size
                if crop:sw,sh=sw*(crop[2]-crop[0]),sh*(crop[3]-crop[1])
                sc=min(mw/sw,mh/sh)
                dest=motion_reader(path,int(sw*sc),int(sh*sc))
                frame=max(0,min(47,int(local*12)))
                movieframe=Image.open(dest/f'{frame:03d}.png').convert('RGBA')
                pad=Image.new('RGBA',(movieframe.width+16,movieframe.height+16),'#0A111B');pad.alpha_composite(movieframe,(8,8))
                ImageDraw.Draw(pad).rounded_rectangle((0,0,pad.width-1,pad.height-1),radius=22,outline='#2B4260',width=3)
                im=pad
            else:im=fitted(path,round(mw),round(mh),True)
            paste_center(canvas,im,cx,yy+math.sin(t*.25)*7)
    elif kind=='tokens':
        for i,path in enumerate(active):
            im=fitted(path,250 if vertical else 290,290)
            cx=w*(.32+.36*(i%2)) if vertical else w*(.23+i*.18)
            yy=h*(.4+.22*(i//2)) if vertical else cy
            paste_center(canvas,im,cx,yy)
    elif kind in ('logo','outro'):
        icon=fitted(CFG['icon'],380 if vertical else 390,390)
        pulse=1+.04*max(0,1-abs(t-CFG['final_punch'])/.18)
        icon=icon.resize((int(icon.width*pulse),int(icon.height*pulse)),Image.Resampling.BICUBIC)
        paste_center(canvas,icon,w*.5,h*(.33 if vertical else .36))
        lines=tuple(CFG['logo']) if vertical else (' '.join(CFG['logo']),)
        title=title_image(lines,w,94 if vertical else 100,'#E3A94B')
        canvas.alpha_composite(title,(0,int(h*(.49 if vertical else .57))))
    # Final envelope flies toward the camera, exactly before the drop.
    if scene.get('envelope') and t>=scene['envelope']:
        a=(t-scene['envelope'])/(scene['end']-scene['envelope'])
        im=fitted(CFG['envelope'],576,576)
        paste_center(canvas,im,w*.5,cy-h*.08*a,alpha=ease(a))
    if scene.get('ghost') or kind=='breath':
        d=ImageDraw.Draw(canvas)
        for i in range(32):
            xx=w*(.5+.28*math.sin(i*6.17+t*.13));yy=h*(.52+.28*math.cos(i*3.1+t*.16));rr=2+(i%3)
            d.ellipse((xx-rr,yy-rr,xx+rr,yy+rr),fill=(169,212,234,110))
    # Three-frame accents, only at beat-aligned editorial cuts.
    if scene.get('flash') and local<3/FPS:
        canvas=Image.blend(canvas,Image.new('RGBA',(w,h),'#A9D4EA'),.30*(1-local/(3/FPS)))
    for title in CFG['titles']:
        if not title['start']<=t<title['end']:continue
        a=ease((t-title['start'])/.18)*ease((title['end']-t)/.16)
        lines=tuple(title['vertical'] if vertical and 'vertical' in title else title['lines'])
        layer=title_image(lines,w,64 if vertical else 67,title.get('color','#E8EEF4')).copy()
        layer.putalpha(layer.getchannel('A').point(lambda x:int(x*a)))
        pos=title.get('position','top')
        y=int(h*(.12 if vertical else .075)) if pos=='top' else int(h*(.74 if vertical else .78))
        canvas.alpha_composite(layer,(0,y+round(12*(1-a))))
    if kind=='intro':
        canvas=Image.blend(Image.new('RGBA',(w,h),'black'),canvas,ease(t/2.4))
        im=fitted(CFG['mailbox'],520,520);paste_center(canvas,im,w*.5,cy+30*(1-q),alpha=ease((t-.8)/2))
    if scene.get('black_after') and t>=scene['black_after']:
        # Keep the question readable through the following cut (global titles).
        black=Image.new('RGBA',(w,h),'black')
        for title in CFG['titles']:
            if title['start']<=t<title['end']:
                layer=title_image(tuple(title.get('vertical',title['lines']) if vertical else title['lines']),w,67,'#E8EEF4');black.alpha_composite(layer,(0,int(h*.4)))
        canvas=black
    if t>CFG['fade_to_black_start']:
        alpha=max(0,1-(t-CFG['fade_to_black_start'])/(CFG['black_at']-CFG['fade_to_black_start']))
        canvas=Image.blend(Image.new('RGBA',(w,h),'black'),canvas,alpha)
    return canvas.convert('RGB')

def make_preview(output):
    preview=CFG.get('preview')
    if preview:
        run(['-ss',preview['start'],'-i',output,'-t',preview['end']-preview['start'],
             '-c:v','libx264','-preset','fast','-crf','21','-pix_fmt','yuv420p',
             '-c:a','aac','-b:a','192k','-movflags','+faststart',OUT/'night-transition-preview.mp4'])

def build(orientation):
    dims=(1920,1080) if orientation=='16x9' else (1080,1920)
    bg,fog=make_background(*dims)
    video=WORK/f'{orientation}-silent.mp4'
    proc=subprocess.Popen([FF,'-y','-v','error','-f','rawvideo','-pix_fmt','rgb24','-s',f'{dims[0]}x{dims[1]}','-r',str(FPS),'-i','-','-an','-c:v','libx264','-preset','fast','-crf','23','-maxrate','3000k','-bufsize','6000k','-pix_fmt','yuv420p','-threads','4',str(video)],stdin=subprocess.PIPE)
    frames=round(CFG['duration']*FPS);si=0
    try:
        for frame in range(frames):
            t=frame/FPS
            if si+1<len(CFG['scenes']) and frame>=round(CFG['scenes'][si+1]['start']*FPS):si+=1
            im=render_frame(t,CFG['scenes'][si],dims,bg,fog)
            proc.stdin.write(im.tobytes())
            if frame%300==0:print(f'{orientation} {frame}/{frames}',flush=True)
    finally:proc.stdin.close()
    if proc.wait():raise RuntimeError('Video encoder failed')
    output=OUT/f'ghost-letters-trailer-{orientation}.mp4'
    music=build_music(CFG,ROOT,WORK,FF)
    run(['-i',video,'-i',music,'-map','0:v','-map','1:a','-c:v','copy','-c:a','aac','-b:a','192k','-t',CFG['duration'],'-map_metadata','-1','-movflags','+faststart',output])
    print(output,flush=True)
    if orientation=='16x9':make_preview(output)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--all',action='store_true');parser.add_argument('--format',default='16x9',choices=['16x9','9x16']);args=parser.parse_args()
    motion_files()
    (OUT/'timeline_markers.json').write_text(json.dumps(timeline_markers(CFG,ROOT),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for orientation in (['16x9','9x16'] if args.all else [args.format]):build(orientation)
