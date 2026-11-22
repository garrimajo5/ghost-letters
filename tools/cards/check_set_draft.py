"""Inspect a concept set without adding unfinished cards to the game manifest.

This checks literal tag structure and estimates associations using the production
similarity mirror. Scores are design diagnostics, not a proof of game balance.
"""
import argparse
from collections import Counter
import json
from pathlib import Path

import check_tags
import retag_cards


def inspect(path):
    draft = json.loads(path.read_text(encoding='utf-8'))
    cards = draft['cards']
    tags = {c['id']: c['tags'] for c in cards}
    errors = []
    if len(tags) != len(cards):
        errors.append('Duplicate card IDs')
    existing_tags = retag_cards.load('tags.json')
    existing_names = retag_cards.load('names.json')
    # An installed draft is compared with the other sets, not with itself.
    existing_tags = {k:v for k,v in existing_tags.items() if k not in tags}
    existing_names = {k:v for k,v in existing_names.items() if k not in tags}
    subjects = {ts[0] for ts in existing_tags.values()}
    aliases = {n.casefold().replace('ё', 'е') for ns in existing_names.values() for n in ns}
    for c in cards:
        ts = c['tags']
        if c['id'] in existing_tags or ts[0] in subjects:
            errors.append(f"{c['id']}: existing ID or main subject")
        if len(ts) != len(set(ts)):
            errors.append(f"{c['id']}: duplicate tags")
        counts = [sum(check_tags.group(t) == g for t in ts) for g in range(3)]
        if not (6 <= counts[0] <= 14 and 1 <= counts[1] <= 2 and 1 <= counts[2] <= 2):
            errors.append(f"{c['id']}: invalid group counts {counts}")
        if not retag_cards.ACTIONS.intersection(ts) or not retag_cards.PROPERTIES.intersection(ts):
            errors.append(f"{c['id']}: missing action/property")
        if any(n.casefold().replace('ё', 'е') in aliases for n in c['names']):
            errors.append(f"{c['id']}: name already used")
        if any(a['tag'] in ts for a in c['associations']):
            errors.append(f"{c['id']}: association repeats literal feature")
    old = check_tags.features(existing_tags, retag_cards.load('details.json'),
                              retag_cards.load('associations.json'))
    new = check_tags.features(tags, {}, {c['id']: c['associations'] for c in cards})
    neighbors = {}
    for card, features in new.items():
        nearest = sorted(((k, check_tags.similarity(features, v)) for k, v in old.items()),
                         key=lambda pair: (-pair[1], pair[0]))[:3]
        neighbors[card] = [dict(id=k, name=existing_names[k][0], score=round(s, 4)) for k, s in nearest]
    internal = sorted((dict(a=a, b=b, score=round(check_tags.similarity(new[a], new[b]), 4))
                       for a in new for b in new if a < b), key=lambda p: -p['score'])[:5]
    return dict(errors=errors, card_count=len(cards),
                limitation='Scores use tags and associations. This check does not inspect illustrations or simulate full games.',
                dominant_colors=dict(Counter(t for ts in tags.values() for t in ts if t.startswith('dominant-'))),
                nearest_existing=neighbors, closest_internal_pairs=internal)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('draft', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    report = inspect(args.draft)
    if args.report:
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))
    raise SystemExit(bool(report['errors']))
