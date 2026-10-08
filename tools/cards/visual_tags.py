"""Визуальные теги карт: цвет и форма предмета — в дополнение к смысловым тегам tags.json.

Предмет на карте лежит на бирюзовом «дымном» фоне. Отделяем его от фона по цвету,
затем считаем:
  * цвета — доли пикселей предмета по корзинам (red, orange, yellow, green, blue, purple,
    pink, brown, black, white, gray); берём цвета, которых не меньше 18%, но не больше двух;
  * форму — по маске предмета: вытянутость (главные оси), заполненность выпуклой оболочки и
    направление: shape-long, shape-round, shape-spiky, shape-tall, shape-wide, shape-diagonal.

Запуск: python tools/cards/visual_tags.py client/assets/cards  (дописывает теги в tags.json;
ранее добавленные визуальные теги заменяются, смысловые не трогаются).
"""

from __future__ import annotations

import colorsys
import json
import math
import os
import sys

import numpy as np
from scipy import ndimage
from PIL import Image

SIZE = 128
COLORS = ["red", "orange", "yellow", "green", "blue", "purple", "pink", "brown", "black", "white", "gray"]
SHAPES = ["shape-long", "shape-round", "shape-compact", "shape-spiky", "shape-tall", "shape-wide", "shape-diagonal"]
VISUAL = set(SHAPES)  # цвета не удаляем: часть из них проставлена вручную


def load(path: str) -> np.ndarray:
    img = Image.open(path).convert("RGB").resize((SIZE, SIZE), Image.BILINEAR)
    return np.asarray(img, dtype=np.float32) / 255.0


def hsv(rgb: np.ndarray) -> np.ndarray:
    flat = rgb.reshape(-1, 3)
    out = np.array([colorsys.rgb_to_hsv(*p) for p in flat], dtype=np.float32)
    return out.reshape(rgb.shape)


def foreground(rgb: np.ndarray) -> np.ndarray:
    """Маска предмета: всё, что не похоже на бирюзовый фон и белёсый дым."""
    h = hsv(rgb)
    hue, sat, val = h[..., 0] * 360, h[..., 1], h[..., 2]
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    teal = (hue > 160) & (hue < 215) & (sat > 0.08)
    # Дым: светлый, слабо насыщенный, с холодным оттенком (синий/зелёный заметно выше красного).
    smoke = (sat < 0.25) & (b - r > 0.04) & (g - r > 0.02)
    mask = ~(teal | smoke)
    # Убираем рамку: по краю карты предмета обычно нет.
    m = SIZE // 16
    mask[:m, :] = mask[-m:, :] = False
    mask[:, :m] = mask[:, -m:] = False
    return clean(mask)


def clean(mask: np.ndarray) -> np.ndarray:
    """Простейшая морфология: пиксель остаётся, если у него хотя бы 5 из 8 соседей — тоже предмет."""
    p = np.pad(mask, 1).astype(np.int8)
    n = sum(
        p[1 + dy : 1 + dy + mask.shape[0], 1 + dx : 1 + dx + mask.shape[1]]
        for dy in (-1, 0, 1)
        for dx in (-1, 0, 1)
        if dy or dx
    )
    mask = mask & (n >= 5)
    # Оставляем крупные куски: самый большой и те, что не меньше 15% от него (крошки дыма — прочь).
    labels, count = ndimage.label(mask)
    if count == 0:
        return mask
    sizes = ndimage.sum(mask, labels, range(1, count + 1))
    keep = [i + 1 for i, size in enumerate(sizes) if size >= 0.15 * sizes.max()]
    return np.isin(labels, keep)


def color_name(r: float, g: float, b: float) -> str:
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    hue = h * 360
    if v < 0.18:
        return "black"
    if s < 0.18:
        return "white" if v > 0.75 else "gray" if v > 0.3 else "black"
    if hue < 15 or hue >= 340:
        return "red" if v > 0.35 else "brown"
    if hue < 40:
        return "orange" if v > 0.6 and s > 0.45 else "brown"
    if hue < 70:
        return "yellow" if v > 0.5 else "brown"
    if hue < 165:
        return "green"
    if hue < 255:
        return "blue"
    if hue < 290:
        return "purple"
    return "pink"


def colors(rgb: np.ndarray, mask: np.ndarray) -> list[str]:
    pixels = rgb[mask]
    if len(pixels) < 30:
        return []
    counts: dict[str, int] = {}
    for p in pixels[:: max(1, len(pixels) // 1500)]:
        name = color_name(*map(float, p))
        counts[name] = counts.get(name, 0) + 1
    total = sum(counts.values())
    ranked = sorted(counts.items(), key=lambda kv: -kv[1])
    return [name for name, n in ranked if n / total >= 0.18][:2]


def hull_area(points: np.ndarray) -> float:
    """Площадь выпуклой оболочки (монотонная цепь Эндрю)."""
    pts = sorted(set(map(tuple, points.tolist())))
    if len(pts) < 3:
        return float(len(pts))

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower: list = []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    upper: list = []
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    hull = lower[:-1] + upper[:-1]
    area = 0.0
    for i in range(len(hull)):
        x1, y1 = hull[i]
        x2, y2 = hull[(i + 1) % len(hull)]
        area += x1 * y2 - x2 * y1
    return abs(area) / 2


def shapes(mask: np.ndarray) -> list[str]:
    ys, xs = np.nonzero(mask)
    if len(xs) < 60:
        return []
    pts = np.stack([xs, ys], axis=1).astype(np.float64)
    cov = np.cov((pts - pts.mean(axis=0)).T)
    evals, evecs = np.linalg.eigh(cov)
    elong = math.sqrt(max(evals[1], 1e-6) / max(evals[0], 1e-6))
    solidity = len(xs) / max(hull_area(pts), 1.0)
    angle = abs(math.degrees(math.atan2(evecs[1, 1], evecs[0, 1]))) % 180  # 0 — горизонталь, 90 — вертикаль

    tags = []
    if elong >= 2.6:
        tags.append("shape-long")
    if solidity < 0.65 and elong < 2:
        tags.append("shape-spiky")
    elif elong < 1.4 and solidity > 0.75:
        # Круглое — если заполняет описанный круг вокруг центра; иначе просто «плотное».
        reach = np.sqrt(((pts - pts.mean(axis=0)) ** 2).sum(axis=1)).max()
        circle = len(xs) / (math.pi * reach * reach)
        tags.append("shape-round" if circle > 0.6 else "shape-compact")
    if elong >= 1.5:
        if 60 <= angle <= 120:
            tags.append("shape-tall")
        elif angle <= 25 or angle >= 155:
            tags.append("shape-wide")
        else:
            tags.append("shape-diagonal")
    return tags


def visual_tags(path: str) -> list[str]:
    rgb = load(path)
    mask = foreground(rgb)
    return colors(rgb, mask) + shapes(mask)


def merge(existing: list[str], visual: list[str]) -> list[str]:
    kept = [t for t in existing if t not in VISUAL]
    return kept + [t for t in visual if t not in kept]


def main(folder: str) -> None:
    tags_path = os.path.join(folder, "tags.json")
    with open(tags_path, encoding="utf-8") as f:
        tags: dict[str, list[str]] = json.load(f)
    for card in sorted(tags):
        image = os.path.join(folder, card + ".webp")
        if os.path.exists(image):
            tags[card] = merge(tags[card], visual_tags(image))
    with open(tags_path, "w", encoding="utf-8") as f:
        f.write("{\n")
        f.write(",\n".join(f" {json.dumps(k)}: {json.dumps(v, ensure_ascii=False)}" for k, v in tags.items()))
        f.write("\n}\n")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "client/assets/cards")
