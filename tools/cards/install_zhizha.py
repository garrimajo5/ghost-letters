"""Copy the approved Zhizha deck into the built-in game assets without replacing other sets."""
import json
import shutil
from pathlib import Path

import retag_cards

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'client/assets/cards'
DRAFT = ROOT / 'docs/card-sets'

def read(path):
    return json.loads(path.read_text(encoding='utf-8'))

def write(path, value):
    if path.name == 'cards.json':
        text = json.dumps(value, ensure_ascii=False, indent=1) + '\n'
    elif path.name == 'card_sets.json':
        text = json.dumps(value, ensure_ascii=False, indent=1) + '\n'
    else:
        text = '{\n' + ',\n'.join('  ' + json.dumps(k, ensure_ascii=False) + ': ' + json.dumps(v, ensure_ascii=False) for k,v in value.items()) + '\n}\n'
    path.write_text(text, encoding='utf-8')

def main():
    deck = read(DRAFT / 'zhizha.json')
    cards = deck['cards']
    assert len(cards) == 200
    manifest = read(ASSETS / 'cards.json')
    entry = dict(code='zhizha', title='Жижа', cards=[dict(id=c['id'], file=c['id'] + '.webp') for c in cards])
    manifest['sets'] = [s for s in manifest['sets'] if s['code'] != 'zhizha'] + [entry]
    ids = [c['id'] for s in manifest['sets'] for c in s['cards']]
    assert len(ids) == len(set(ids))
    for c in cards:
        name = c['id'] + '.webp'
        shutil.copyfile(DRAFT / 'zhizha' / name, ASSETS / name)
    shutil.copyfile(DRAFT / 'zhizha-set-icon.png', ROOT / 'client/assets/images/zhizha-set-icon.png')
    for filename, key in [('names.json', 'names'), ('tags.json', 'tags'), ('associations.json', 'associations')]:
        data = read(ASSETS / filename)
        data.update({c['id']: c[key] for c in cards})
        write(ASSETS / filename, data)
    for filename in ['details.json', 'details_manual.json']:
        data = read(ASSETS / filename)
        for c in cards:
            data.setdefault(c['id'], [])
        write(ASSETS / filename, data)
    write(ASSETS / 'cards.json', manifest)
    sets_path = Path(__file__).with_name('card_sets.json')
    sets = read(sets_path)
    sets.update({c['id']: 'zhizha' for c in cards})
    write(sets_path, sets)
    labels_path = Path(__file__).with_name('retag_labels.json')
    labels = read(labels_path)
    for tag, label in deck['new_tag_labels'].items():
        labels.setdefault(tag, label)
    write(labels_path, labels)
    retag_cards.write_vocabulary(read(ASSETS / 'tags.json'), read(ASSETS / 'associations.json'), labels)
    print(f'Installed {len(cards)} Zhizha cards; catalog has {len(ids)} cards in {len(manifest["sets"])} sets.')

if __name__ == '__main__':
    main()
