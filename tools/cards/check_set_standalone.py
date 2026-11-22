"""Compare a standalone clue deck with the four built-in decks using CardTags scores."""
import argparse
import json
import random
from pathlib import Path
from statistics import median

import check_tags
import retag_cards


THEMES = {
    'Город и архитектура': 'street building architecture',
    'Дом, мебель и ночлег': 'home furniture bedroom shelter',
    'Украшения и мода': 'jewelry ornament fashion',
    'Тело и медицина': 'body anatomy medical hospital',
    'Вера, магия и обряды': 'religion ritual magic',
    'Время, письмо и запись': 'time write record',
    'Уход за собой и запахи': 'hygiene bathroom smell',
    'Охрана и спасение': 'security safety rescue',
    'Кухня и еда': 'kitchen cook food',
    'Природа и животные': 'nature animal plant',
    'Спорт и соревнования': 'sport sportsman',
    'Ферма': 'farm',
    'Замки и запирание': 'lock',
    'Порошки': 'powder',
    'Знаки и вывески': 'sign',
    'Энергия и топливо': 'energy fuel',
    'Праздники': 'party',
    'Зрение и осязание': 'vision touch',
}


def measure(ids, tags, features, trials):
    scores = [[0.0] * len(ids) for _ in ids]
    for i, key in enumerate(ids):
        for j in range(i):
            scores[i][j] = scores[j][i] = check_tags.similarity(features[key], features[ids[j]])
    # Ten different cards: five clues in a row (target first) and a five-card hand.
    # Count a hand if at least one letter favors the target over all four distractors.
    rng = random.Random(25371)
    successes = 0
    for _ in range(trials):
        target, *rest = rng.sample(range(len(ids)), 10)
        others, hand = rest[:4], rest[4:]
        successes += any(scores[letter][target] > max(scores[letter][alt] for alt in others) + 1e-10
                         for letter in hand)
    third = {card: sorted(scores[i], reverse=True)[2] for i, card in enumerate(ids)}
    weakest = min(ids, key=third.get)
    return {
        'cards': len(ids),
        'theme_counts': {name: sum(bool(set(words.split()).intersection(tags[k])) for k in ids)
                         for name, words in THEMES.items()},
        'third_neighbor_median': round(median(third.values()), 4),
        'weakest_third_neighbor': {'id': weakest, 'score': round(third[weakest], 4)},
        'hands_with_strict_clue': successes,
        'trials': trials,
        'hand_fraction': round(successes / trials, 4),
    }


def compare(draft_path, baseline, trials):
    draft = json.loads(draft_path.read_text(encoding='utf-8'))['cards']
    tags = {c['id']: c['tags'] for c in draft}
    ids = list(tags)
    if not 10 <= baseline < len(ids) or trials < 1:
        raise ValueError('Baseline must contain at least ten cards and precede the last card. Trials must be positive.')
    features = check_tags.features(tags, {}, {c['id']: c['associations'] for c in draft})
    old_tags = retag_cards.load('tags.json')
    old = check_tags.features(old_tags, retag_cards.load('details.json'), retag_cards.load('associations.json'))
    sets = json.loads(Path(__file__).with_name('card_sets.json').read_text(encoding='utf-8'))
    sets = {k:v for k,v in sets.items() if k not in tags}
    return {
        'theme_tags': THEMES,
        'seed': 25371,
        'baseline': measure(ids[:baseline], tags, features, trials),
        'standalone': measure(ids, tags, features, trials),
        'official': {s: measure(sorted(k for k, v in sets.items() if v == s), old_tags, old, trials)
                     for s in sorted(set(sets.values()))},
    }


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('draft', type=Path)
    parser.add_argument('--baseline', type=int, required=True)
    parser.add_argument('--trials', type=int, default=20000)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    result = json.dumps(compare(args.draft, args.baseline, args.trials), ensure_ascii=False, indent=2)
    if args.report:
        args.report.write_text(result + '\n', encoding='utf-8')
    else:
        print(result)
