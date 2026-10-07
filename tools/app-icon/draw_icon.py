# Рисует иконку приложения (конверт с сургучной печатью-призраком) в 1024×1024: python3 draw_icon.py
from PIL import Image, ImageDraw, ImageFilter
S=4096
def icon(full_bg=True):
    im=Image.new('RGBA',(S,S),(0,0,0,0))
    # фон: ночной градиент
    bg=Image.new('RGBA',(S,S))
    d=ImageDraw.Draw(bg)
    for y in range(S):
        t=y/S
        c=(int(0x15+(0x0A-0x15)*t),int(0x23+(0x11-0x23)*t),int(0x35+(0x1B-0x35)*t),255)
        d.line([(0,y),(S,y)],fill=c)
    # мягкое голубое сияние за конвертом
    glow=Image.new('RGBA',(S,S),(0,0,0,0)); g=ImageDraw.Draw(glow)
    g.ellipse([S*0.18,S*0.16,S*0.82,S*0.80],fill=(0xA9,0xD4,0xEA,90))
    glow=glow.filter(ImageFilter.GaussianBlur(S*0.08))
    bg=Image.alpha_composite(bg,glow)
    im=bg
    d=ImageDraw.Draw(im)
    # конверт
    x0,y0,x1,y1=S*0.20,S*0.30,S*0.80,S*0.72
    r=S*0.04
    d.rounded_rectangle([x0,y0,x1,y1],radius=r,fill=(0xE3,0xA9,0x4B,255))
    # нижние клапаны
    d.polygon([(x0,y1-r*0.5),((x0+x1)/2,(y0+y1)/2+S*0.03),(x1,y1-r*0.5)],fill=(0xC9,0x8E,0x36,255))
    # верхний клапан
    d.polygon([(x0+r*0.3,y0+r*0.3),((x0+x1)/2,(y0+y1)/2+S*0.02),(x1-r*0.3,y0+r*0.3)],fill=(0xF2,0xC5,0x79,255))
    # сургучная печать-призрак
    cx,cy,R=(x0+x1)/2,(y0+y1)/2+S*0.02,S*0.095
    d.ellipse([cx-R,cy-R,cx+R,cy+R],fill=(0xB0,0x3E,0x33,255))
    # силуэт призрака
    gw,gh=R*1.0,R*1.25
    gx,gy=cx,cy-R*0.08
    d.ellipse([gx-gw/2,gy-gh/2,gx+gw/2,gy+gw/2-gh*0.1],fill=(0xE8,0xEE,0xF4,255))
    d.rectangle([gx-gw/2,gy-gh*0.1,gx+gw/2,gy+gh*0.42],fill=(0xE8,0xEE,0xF4,255))
    # волнистый низ
    n=3;w=gw/n
    for i in range(n):
        d.ellipse([gx-gw/2+i*w,gy+gh*0.42-w/2,gx-gw/2+(i+1)*w,gy+gh*0.42+w/2],fill=(0xE8,0xEE,0xF4,255) if i%2==0 else (0xB0,0x3E,0x33,255))
    # глаза
    er=gw*0.09
    for ex in (gx-gw*0.18,gx+gw*0.18):
        d.ellipse([ex-er,gy-er*1.3,ex+er,gy+er*1.3],fill=(0x0D,0x17,0x24,255))
    return im
im=icon()
big=im.resize((1024,1024),Image.LANCZOS)
big.save('icon_1024.png')
