"""Скачивает статические TTF нужных начертаний через CSS API Google Fonts."""
import os
import re
import sys
import urllib.request

FAMILIES = {"Oswald": [400, 500, 600, 700], "Golos Text": [400, 500, 600, 700]}
# Старый User-Agent — тогда Google Fonts отдаёт truetype без разбиения на подмножества.
UA = "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/534.30 (KHTML, like Gecko) Safari/534.30"


def get(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def main(out: str) -> None:
    os.makedirs(out, exist_ok=True)
    for family, weights in FAMILIES.items():
        for w in weights:
            css = get(f"https://fonts.googleapis.com/css?family={family.replace(' ', '+')}:{w}&subset=cyrillic,latin").decode()
            urls = re.findall(r"url\((https://[^)]+)\)", css)
            if not urls:
                sys.exit(f"нет url для {family} {w}:\n{css}")
            data = get(urls[0])
            if data[:4] not in (b"\x00\x01\x00\x00", b"true", b"OTTO"):
                sys.exit(f"{family} {w}: не TTF ({data[:4]!r}) из {urls[0]}")
            name = f"{family.replace(' ', '')}-{w}.ttf"
            with open(os.path.join(out, name), "wb") as f:
                f.write(data)
            print(name, len(data))


if __name__ == "__main__":
    main(sys.argv[1])
