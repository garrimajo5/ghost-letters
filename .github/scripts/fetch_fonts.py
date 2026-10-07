"""Скачивает вариативные TTF (Oswald, Golos Text) из репозитория google/fonts
и делает из них статические начертания — Flutter надёжно работает со статическими TTF."""
import os
import subprocess
import sys
import urllib.request

BASE = "https://raw.githubusercontent.com/google/fonts/main/ofl"
FAMILIES = {
    "Oswald": f"{BASE}/oswald/Oswald%5Bwght%5D.ttf",
    "GolosText": f"{BASE}/golostext/GolosText%5Bwght%5D.ttf",
}
WEIGHTS = [400, 500, 600, 700]


def main(out: str) -> None:
    os.makedirs(out, exist_ok=True)
    for name, url in FAMILIES.items():
        var = os.path.join(out, f"{name}-var.ttf")
        try:
            urllib.request.urlretrieve(url, var)
        except Exception as e:  # noqa: BLE001
            print(f"::error::{name}: {url}: {e}")
            sys.exit(1)
        for w in WEIGHTS:
            dst = os.path.join(out, f"{name}-{w}.ttf")
            subprocess.run(
                [sys.executable, "-m", "fontTools.varLib.instancer", var, f"wght={w}", "--static", "-o", dst],
                check=True,
            )
            print(dst, os.path.getsize(dst))
        os.remove(var)


if __name__ == "__main__":
    main(sys.argv[1])
