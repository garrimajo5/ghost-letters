"""Смоук веб-версии: страница грузится в Chromium размером с iPhone, Flutter рисует первый кадр, без ошибок JS."""
import sys

from playwright.sync_api import sync_playwright

url = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8000/"
errors = []
with sync_playwright() as p:
    browser = p.chromium.launch()
    page = browser.new_page(viewport={"width": 390, "height": 844}, device_scale_factor=2, is_mobile=True, has_touch=True)
    page.on("pageerror", lambda e: errors.append(str(e)))
    page.on("console", lambda m: m.type == "error" and errors.append(m.text))
    page.goto(url)
    try:
        page.wait_for_selector("#loading", state="detached", timeout=90_000)
    except Exception:
        errors.append("Flutter не нарисовал первый кадр за 90 с")
    page.wait_for_timeout(2000)
    page.screenshot(path="web-smoke.png")
    browser.close()

# Шрифты-запасники и т.п. с CDN в CI могут не грузиться — это не ошибки приложения.
errors = [e for e in errors if "fonts.gstatic.com" not in e and "Failed to load resource" not in e]
for e in errors:
    print(f"::error title=Веб-версия::{e}")
sys.exit(1 if errors else 0)
