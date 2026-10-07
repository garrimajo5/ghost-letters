#!/usr/bin/env python3
"""Нарезка спрайт-листов улик из папки Resource в отдельные карты + manifest.

Пример:
    python3 tools/cards/cut_cards.py --resource D:/AI/GhostLetters/Resource --out client/assets/cards

Что делает:
  * режет каждый лист из sheets.json по сетке cols x rows;
  * пропускает пустые (почти чёрные/однотонные) ячейки;
  * пропускает рубашки — ячейки, похожие на эталоны из back_refs;
  * убирает дубликаты (одна и та же карта на разных листах);
  * сохраняет WebP size x size и cards.json (все карты — в наборе "original").
Нужен только Pillow.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from dataclasses import dataclass

from PIL import Image, ImageStat

Image.MAX_IMAGE_PIXELS = None

EMPTY_MEAN = 14      # средняя яркость ниже — пустая ячейка
EMPTY_STDDEV = 10    # и почти без деталей
BACK_DISTANCE = 30   # из 256 бит: ближе — считаем рубашкой
DUP_DISTANCE = 18    # из 256 бит: ближе — дубликат


def dhash(img: Image.Image, size: int = 16) -> int:
    """Разностный хеш 256 бит, устойчив к масштабу и сжатию."""
    g = img.convert("L").resize((size + 1, size), Image.LANCZOS)
    px = g.tobytes()  # одно значение яркости на пиксель
    bits = 0
    for y in range(size):
        row = px[y * (size + 1):(y + 1) * (size + 1)]
        for x in range(size):
            bits = (bits << 1) | (1 if row[x] > row[x + 1] else 0)
    return bits


def hamming(a: int, b: int) -> int:
    return bin(a ^ b).count("1")


def is_empty(img: Image.Image) -> bool:
    st = ImageStat.Stat(img.convert("L"))
    return st.mean[0] < EMPTY_MEAN and st.stddev[0] < EMPTY_STDDEV


@dataclass
class Card:
    hash: int
    image: Image.Image
    sheet: str
    col: int
    row: int
    cell_px: int


def cut_sheet(path: str, cols: int, rows: int):
    with Image.open(path) as src:
        im = src.convert("RGB")
    w, h = im.size
    cw, ch = w / cols, h / rows
    for r in range(rows):
        for c in range(cols):
            box = (round(c * cw), round(r * ch), round((c + 1) * cw), round((r + 1) * ch))
            yield c, r, im.crop(box), int(min(cw, ch))


def main(argv: list[str] | None = None) -> int:
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--resource", required=True, help="папка Resource со спрайт-листами")
    ap.add_argument("--out", required=True, help="куда сложить карты и cards.json")
    ap.add_argument("--catalog", default=os.path.join(here, "sheets.json"))
    ap.add_argument("--size", type=int, default=512)
    ap.add_argument("--quality", type=int, default=82)
    args = ap.parse_args(argv)

    with open(args.catalog, encoding="utf-8") as f:
        catalog = json.load(f)

    backs = []
    for ref in catalog.get("back_refs", []):
        p = os.path.join(args.resource, ref)
        if os.path.exists(p):
            with Image.open(p) as b:
                backs.append(dhash(b))
        else:
            print(f"! нет эталона рубашки: {ref}", file=sys.stderr)

    kept: list[Card] = []
    stats = {"cells": 0, "empty": 0, "backs": 0, "duplicates": 0}
    for sheet in catalog["sheets"]:
        path = os.path.join(args.resource, sheet["file"])
        if not os.path.exists(path):
            print(f"! нет листа: {sheet['file']}", file=sys.stderr)
            continue
        for c, r, cell, cell_px in cut_sheet(path, sheet["cols"], sheet["rows"]):
            stats["cells"] += 1
            if is_empty(cell):
                stats["empty"] += 1
                continue
            hsh = dhash(cell)
            if any(hamming(hsh, b) <= BACK_DISTANCE for b in backs):
                stats["backs"] += 1
                continue
            dup = next((k for k in kept if hamming(hsh, k.hash) <= DUP_DISTANCE), None)
            if dup is not None:
                stats["duplicates"] += 1
                if cell_px > dup.cell_px:  # оставляем копию в лучшем разрешении
                    dup.image, dup.sheet, dup.col, dup.row, dup.cell_px = cell, sheet["file"], c, r, cell_px
                continue
            kept.append(Card(hsh, cell, sheet["file"], c, r, cell_px))

    os.makedirs(args.out, exist_ok=True)
    cards = []
    for i, card in enumerate(kept, start=1):
        card_id = f"orig_{i:04d}"
        name = f"{card_id}.webp"
        img = card.image.resize((args.size, args.size), Image.LANCZOS)
        img.save(os.path.join(args.out, name), "WEBP", quality=args.quality, method=6)
        cards.append({"id": card_id, "file": name,
                      "source": {"sheet": card.sheet, "col": card.col, "row": card.row}})

    manifest = {"version": 1, "sets": [{"code": "original", "title": "Оригинальный", "cards": cards}]}
    with open(os.path.join(args.out, "cards.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1)

    print(f"ячеек {stats['cells']}, пустых {stats['empty']}, рубашек {stats['backs']}, "
          f"дубликатов {stats['duplicates']}, карт {len(cards)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
