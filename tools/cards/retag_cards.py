"""Rebuild reviewed card meanings; no image/name guessing or similarity tuning.

retag_review.csv records an individual visual review of all 761 images.
Profiles only expand repeated literal features; each row supplies the subject,
visible colors/shapes, distinguishing features and one secondary association.
Run from any directory: python tools/cards/retag_cards.py
"""
import csv
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'client/assets/cards'
PROFILES = {
'glass-timer': 'time measure hold-in-hand container hollow glass transparent fragile',
'ritual-doll': 'magic ritual body hold-in-hand stab soft',
'scarecrow': 'figure protect garden bird fabric soft',
'adult-toy': 'toy hold-in-hand play intimate rubber elastic',
'ammo': 'weapon shoot hard metal soldier danger',
'amphibian': 'animal swim jump wet river soft',
'appliance': 'electric hard use kitchen tool',
'ball': 'sport play hold-in-hand throw sportsman hard',
'bedding': 'lie-on rest soft bedroom home',
'beverage': 'drink pour liquid food kitchen water',
'bird': 'animal bird fly light-weight feather walk',
'blade': 'tool hold-in-hand cut sharp hard',
'board-game': 'game play hold-in-hand count hard table',
'body': 'anatomy touch fragile biological medical',
'book': 'read hold-in-hand paper light-weight information',
'bowling': 'sport play roll hit hard sportsman',
'boxing': 'sport wear hit hold-in-hand soft sportsman',
'building': 'building enter shelter hard architecture',
'carry': 'container hold-in-hand carry open-close light-weight',
'celestial': 'space orbit reflect hard astronomy night',
'cleaning': 'tool hold-in-hand clean hygiene light-weight',
'clothing': 'clothes wear soft fabric body fashion',
'club': 'weapon hold-in-hand hit hard swing handle',
'construction': 'build hold-in-hand hard material work',
'cord': 'tie pull flexible hold-in-hand cord',
'cosmetic': 'hold-in-hand apply beauty bathroom soft',
'costume': 'wear play costume body light-weight',
'craft': 'tool hold-in-hand make light-weight craft',
'doll': 'toy play hold-in-hand child body',
'electric': 'electric use hard device energy',
'enclosure': 'container open-close store hard',
'entry': 'open-close enter hard building barrier',
'explosive': 'weapon ignite burn hard danger',
'eyewear': 'wear look-through transparent vision light-weight',
'fantasy-creature': 'animal move magic hard fantasy',
'fastener': 'hold-in-hand attach open-close hard tool',
'fire': 'burn heat light hot wood',
'fish': 'animal swim wet soft water',
'float-item': 'float water light-weight hold-in-hand fishing',
'flower': 'plant grow smell fragile garden decoration',
'fluid': 'liquid pour wet container hollow',
'food-ingredient': 'food cook hold-in-hand dry kitchen',
'footwear': 'clothes wear walk soft body',
'frozen': 'water cold hard melt wet',
'fuel': 'burn heat ignite dry fuel',
'fungus': 'grow soft forest biological mushroom',
'furniture': 'hard home place-on furniture',
'granular': 'powder pour dry hold-in-hand granular',
'gun': 'weapon hold-in-hand shoot hard aim handle',
'hand-tool': 'tool hold-in-hand use hard handle',
'human': 'person move wear soft body',
'inflatable': 'toy inflatable elastic soft float',
'insect': 'animal insect crawl light-weight biological',
'instrument': 'music play sound hard musician',
'jewelry': 'wear decorate jewelry hard ornament',
'lamp': 'light hard night lamp illuminate',
'landscape': 'walk explore hard nature landscape',
'locking': 'lock open-close hard security tool',
'mammal': 'animal walk eat soft biological',
'marine': 'animal water sea wet move',
'mask': 'wear protect light-weight body face',
'meal': 'food eat cook kitchen edible',
'measuring': 'measure read hold-in-hand hard instrument',
'medical-kit': 'medical open-close carry hard hospital',
'medical-tool': 'medical hold-in-hand use hospital light-weight',
'medicine': 'medical apply heal hospital light-weight',
'microbe': 'biological microscopic infect multiply medical',
'mirror': 'look-at reflect fragile transparent glass',
'money-item': 'money count pay hold-in-hand hard',
'optics': 'look-through glass transparent hard instrument',
'ornament': 'decorate display ornament fragile art',
'pet': 'animal walk eat soft pet',
'picture': 'art look-at display paper light-weight',
'plant': 'plant grow biological nature fragile',
'plant-food': 'food eat plant biological edible',
'plumbing': 'water pour hard tool hollow',
'recording': 'sound play record hard device',
'reptile': 'animal crawl biological cold-blooded nature',
'rescue': 'protect rescue hard safety tool',
'ride-sport': 'sport ride balance hard sportsman',
'sanitary': 'water wash hard bathroom hygiene',
'screen': 'electric read look-at hard device',
'sculpture': 'art decorate display hard statue',
'seat': 'furniture sit-on rest hard home',
'shelter': 'enter shelter lie-on light-weight travel',
'signage': 'read display information light-weight sign',
'smoking': 'hold-in-hand burn inhale hot smoke',
'sport-swing': 'sport sportsman hold-in-hand swing hard handle',
'sport-target': 'sport play aim hard sportsman',
'sport-weight': 'sport hold-in-hand lift heavy hard gym',
'stone': 'stone hard hold-in-hand heavy nature',
'sweet': 'food eat sweet kitchen edible',
'throw-sport': 'sport throw hold-in-hand fly hard sportsman',
'timer': 'time measure read hard instrument',
'toy': 'toy play hold-in-hand child light-weight',
'trace': 'trace follow look-at fragile evidence',
'trap': 'trap catch hold-in-hand hard danger',
'utensil': 'kitchen tool hold-in-hand cook hard',
'vehicle': 'transport ride move hard travel',
'vehicle-part': 'transport hard move rotate car',
'vessel': 'container pour hold-in-hand hard hollow',
'water-source': 'water liquid wet pour nature',
'water-toy': 'toy rubber soft elastic float water play child',
'weather': 'weather water float wet nature',
'window': 'look-through open-close transparent hard building',
'writing-surface': 'write read hold-in-hand light-weight information',
'writing-tool': 'write hold-in-hand hard tool information',
}

# A material declared on a reviewed row replaces the generic fabric default.
MATERIALS = set('metal wood glass stone paper fabric leather rubber plastic ceramic clay bone fur wool straw wax'.split())
PROPERTIES = set('liquid powder gas soft hard fragile transparent elastic sticky hot cold wet sharp heavy light-weight inflatable dry slippery flexible microscopic cold-blooded hollow edible granular flat long toxic open-top'.split())
ACTIONS = set('hold-in-hand swing hit cut stab shoot throw pour drink eat wear ride sit-on lie-on open-close lock write read measure play clean wash cook heat cool light burn tie pull push climb swim fly float ring count walk grow enter carry store touch look-through look-at decorate display build apply make use attach ignite melt move jump roll rest orbit reflect explore aim place-on inhale record protect rescue lift balance follow catch rotate pay heal infect multiply draw print sew grind spin speak hear kiss smell think slide run scratch dig twist blow sing carve rock knit crawl illuminate spray drill'.split())


def load(name):
    return json.loads((ROOT/name).read_text(encoding='utf-8'))


def save(name, data):
    lines = [json.dumps(k) + ': ' + json.dumps(v, ensure_ascii=False) for k,v in data.items()]
    (ROOT/name).write_text('{\n  '+',\n  '.join(lines)+'\n}\n', encoding='utf-8')


def reviewed_tags(row):
    extra = row['features'].split()
    base = PROFILES[row['profile']].split()
    if 'clay' in extra:
        base = [t for t in base if t != 'paper']
    if MATERIALS.intersection(extra):
        base = [t for t in base if t != 'fabric']
    # Explicit material/texture has priority over broad profile defaults.
    if 'soft' in extra or 'elastic' in extra:
        base = [t for t in base if t != 'hard']
    if 'hard' in extra or 'metal' in extra:
        base = [t for t in base if t not in ('soft', 'fragile')]
    if 'plastic' in extra or 'wood' in extra:
        base = [t for t in base if t != 'fragile']
    meaning = list(dict.fromkeys([row['subject'], *base, *extra]))
    # Source rows explicitly provide shapes and colors, never contour heuristics.
    return meaning + ['shape-'+s for s in row['shapes'].split()] + ['dominant-'+c for c in row['colors'].split()]


def write_vocabulary(tags, associations, labels):
    examples = {}
    for card, values in tags.items():
        for tag in values:
            examples.setdefault(tag, []).append(card)
    for data in (load('details.json'), load('details_manual.json'), associations):
        for card, marks in data.items():
            for mark in marks:
                cards = examples.setdefault(mark['tag'], [])
                if card not in cards:
                    cards.append(card)
    def category(tag):
        if tag.startswith('shape-'): return 'форма'
        if tag.startswith(('dominant-', 'accent-')): return 'цвет'
        if tag in ACTIONS: return 'действие'
        if tag in PROPERTIES: return 'свойство'
        if tag in MATERIALS: return 'материал'
        return 'предмет / контекст / ассоциация'
    intro = Path(__file__).with_name('retag_vocabulary_intro.md').read_text(encoding='utf-8')
    table = '| Тег | Группа | Значение | Примеры карт |\n| --- | --- | --- | --- |\n'
    table += ''.join(f'| `{tag}` | {category(tag)} | {labels[tag]} | '
                     + ', '.join(f'`{card}`' for card in cards[:3]) + ' |\n'
                     for tag, cards in sorted(examples.items()))
    (ROOT/'vocabulary.md').write_text(intro+table, encoding='utf-8')


def main():
    with Path(__file__).with_name('retag_review.csv').open(encoding='utf-8') as source:
        rows = list(csv.DictReader(source, delimiter=';'))
    labels = json.loads(Path(__file__).with_name('retag_labels.json').read_text(encoding='utf-8'))
    tags = {r['card']: reviewed_tags(r) for r in rows}
    associations = {}
    for row in rows:
        card = row['card']
        # A probable association is secondary, never a duplicate of visible data.
        marks = [entry.split(':') for entry in row['association'].split()]
        associations[card] = [dict(tag=mark[0], weight=float(mark[1]) if len(mark)>1 else .5, label=labels[mark[0]]) for mark in marks if mark[0] not in tags[card]]
    save('tags.json', tags)
    save('associations.json', associations)
    write_vocabulary(tags, associations, labels)

if __name__ == '__main__':
    main()
