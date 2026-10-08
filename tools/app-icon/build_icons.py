"""Иконка приложения «светящийся призрак» (вариант A): рисует SVG в Chromium (Playwright) и раскладывает
размеры для Android (обычная + адаптивная: фон и передний план), iOS и веб.

Запуск из корня репозитория: python3 tools/app-icon/build_icons.py
"""
import asyncio
import io
import os

from PIL import Image
from playwright.async_api import async_playwright

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CLIENT = os.path.join(ROOT, "client")

GHOST = ("M512 210c-150 0-250 115-250 265v300c0 22 24 34 42 21l55-40 55 44c13 10 31 10 44 0l54-44 54 44c13 10 31 10 44 0"
         "l55-44 55 40c18 13 42 1 42-21V475c0-150-100-265-250-265z")
DEFS = ('<defs><linearGradient id="n" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1A2B42"/>'
        '<stop offset="1" stop-color="#0A111B"/></linearGradient><radialGradient id="g" cx="0.5" cy="0.45" r="0.45">'
        '<stop offset="0" stop-color="#A9D4EA" stop-opacity="0.55"/><stop offset="1" stop-color="#A9D4EA" stop-opacity="0"/>'
        '</radialGradient></defs>')
SKY = ('<rect width="1024" height="1024" fill="url(#n)"/><circle cx="512" cy="470" r="420" fill="url(#g)"/>'
       '<circle cx="250" cy="230" r="5" fill="#E8EEF4" opacity=".8"/><circle cx="800" cy="300" r="4" fill="#E8EEF4" opacity=".7"/>'
       '<circle cx="760" cy="780" r="3" fill="#E8EEF4" opacity=".6"/>')


def ghost(scale: float) -> str:
    body = (f'<path d="{GHOST}" fill="#E8EEF4"/><ellipse cx="440" cy="470" rx="34" ry="46" fill="#0D1724"/>'
            '<ellipse cx="584" cy="470" rx="34" ry="46" fill="#0D1724"/>'
            '<ellipse cx="512" cy="560" rx="22" ry="28" fill="#0D1724" opacity=".85"/>')
    return f'<g transform="translate(512 505) scale({scale}) translate(-512 -505)">{body}</g>'


def svg(inner: str) -> str:
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">{DEFS}{inner}</svg>'


VARIANTS = {
    "full": svg(SKY + ghost(1.0)),                # иконка целиком (iOS, старый Android, веб)
    "background": svg(SKY),                       # фон адаптивной иконки Android
    "foreground": svg(ghost(0.56)),               # призрак в безопасной зоне адаптивной иконки (прозрачный фон)
}


async def render() -> dict:
    out = {}
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        page = await browser.new_page(viewport={"width": 1024, "height": 1024})
        for name, markup in VARIANTS.items():
            await page.set_content(f'<html><body style="margin:0;background:transparent">{markup}</body></html>')
            png = await page.locator("svg").screenshot(omit_background=True)
            out[name] = Image.open(io.BytesIO(png)).convert("RGBA")
        await browser.close()
    return out


def save(img: Image.Image, size: int, path: str, opaque: bool) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    im = img.resize((size, size), Image.LANCZOS)
    (im.convert("RGB") if opaque else im).save(path)


def main() -> None:
    images = asyncio.run(render())
    full, bg, fg = images["full"], images["background"], images["foreground"]
    save(full, 1024, os.path.join(ROOT, "tools/app-icon/icon_1024.png"), True)

    res = os.path.join(CLIENT, "android/app/src/main/res")
    for density, launcher, adaptive in [("mdpi", 48, 108), ("hdpi", 72, 162), ("xhdpi", 96, 216), ("xxhdpi", 144, 324), ("xxxhdpi", 192, 432)]:
        folder = os.path.join(res, f"mipmap-{density}")
        save(full, launcher, os.path.join(folder, "ic_launcher.png"), True)
        save(fg, adaptive, os.path.join(folder, "ic_launcher_foreground.png"), False)
        save(bg, adaptive, os.path.join(folder, "ic_launcher_background.png"), True)

    ios = os.path.join(CLIENT, "ios/Runner/Assets.xcassets/AppIcon.appiconset")
    for name in os.listdir(ios):
        if name.startswith("Icon-App-") and name.endswith(".png"):
            spec = name[len("Icon-App-"):-4]          # например 83.5x83.5@2x
            points, scale = spec.split("@")
            size = round(float(points.split("x")[0]) * int(scale.rstrip("x")))
            save(full, size, os.path.join(ios, name), True)  # iOS: без прозрачности

    web = os.path.join(CLIENT, "web")
    save(full, 32, os.path.join(web, "favicon.png"), True)
    for name, size in [("Icon-192.png", 192), ("Icon-512.png", 512), ("Icon-maskable-192.png", 192),
                       ("Icon-maskable-512.png", 512), ("apple-touch-icon.png", 180)]:
        save(full, size, os.path.join(web, "icons", name), True)


if __name__ == "__main__":
    main()
