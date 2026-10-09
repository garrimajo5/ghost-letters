"""Weighted small visual details, kept separately from the card's main subject.

python tools/cards/detail_tags.py client/assets/cards
Minor foreground colour patches (4–18%) are measured, not invented objects.
Hand-reviewed semantic details in details_manual.json override those detections.
"""
import json
import sys
from collections import Counter
from pathlib import Path

from visual_tags import load, foreground, color_name

LABELS = dict(zip(
    ['red', 'orange', 'yellow', 'green', 'blue', 'purple', 'pink', 'brown', 'black', 'white', 'gray'],
    ['красные акценты', 'оранжевые акценты', 'жёлтые акценты', 'зелёные акценты',
     'синие акценты', 'фиолетовые акценты', 'розовые акценты', 'коричневые акценты',
     'чёрные акценты', 'белые акценты', 'серые акценты']))


def details(rgb):
    pixels = rgb[foreground(rgb)]
    if len(pixels) < 80:
        return []
    counts = Counter(color_name(*map(float, p)) for p in pixels)
    return [dict(tag='accent-' + color, weight=round(n / len(pixels), 3), label=LABELS[color])
            for color, n in sorted(counts.items()) if 0.04 <= n / len(pixels) < 0.18]


def main(root):
    manual = json.loads((root / 'details_manual.json').read_text(encoding='utf-8'))
    tags = json.loads((root / 'tags.json').read_text(encoding='utf-8'))
    result = {}
    for card in sorted(tags):
        found = {d['tag']: d for d in details(load(str(root / (card + '.webp'))))}
        found.update({d['tag']: d for d in manual.get(card, [])})
        result[card] = list(found.values())
    lines = [json.dumps(card) + ': ' + json.dumps(marks, ensure_ascii=False) for card, marks in result.items()]
    (root / 'details.json').write_text('{\n  ' + ',\n  '.join(lines) + '\n}\n', encoding='utf-8')
    print(f'{len(result)} cards; {sum(len(v) for v in result.values())} weighted details')


if __name__ == '__main__':
    main(Path(sys.argv[1]))
