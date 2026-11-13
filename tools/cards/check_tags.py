"""Validate the reviewed catalog and rank controls with CardTags' production formula.

Default mode fails on structural errors AND unmet full-catalog rank requirements.
--structure-only skips ranking acceptance (useful for checking data independently).
--report PATH saves all top-ten neighbors, pair scores, and rank failures as JSON.
Tie order is deterministic by card ID; tied ranks are also reported explicitly.
"""
import argparse
import json
import math
import re
from pathlib import Path
from retag_cards import ACTIONS, PROPERTIES

ROOT = Path(__file__).resolve().parents[2] / 'client/assets/cards'
COLORS = set('red orange yellow green blue purple pink brown black white gray'.split())
UNTRUSTED = {'shape-diagonal', 'shape-compact', 'shape-spiky'}


def group(t):
    return 1 if t.startswith('shape-') else 2 if t in COLORS or (t.startswith(('accent-', 'dominant-')) and t.split('-', 1)[1] in COLORS) else 0


def features(tags, details, associations, secondary=.35, scale=.5):
    result = {}
    for card, values in tags.items():
        removed = {d['tag'] for d in details.get(card, [])}
        main = set(values) - removed
        main -= {'shape-diagonal', 'shape-compact'}
        if not main.intersection({'sharp','cactus','thorn','spike','star','starfish'}):
            main.discard('shape-spiky')
        own = {t: 1.0 for t in main}
        for d in associations.get(card, []):
            if group(d['tag']) == 0 and d['tag'] not in own:
                own[d['tag']] = min(.95, d['weight']) * secondary * scale
        result[card] = own
    return result


def similarity(x, y):
    def match(g):
        a = {t:v for t,v in x.items() if group(t)==g and (g!=2 or t.startswith('dominant-'))}
        b = {t:v for t,v in y.items() if group(t)==g and (g!=2 or t.startswith('dominant-'))}
        union = sum(max(a.get(t,0),b.get(t,0)) for t in a.keys()|b.keys())
        confidence = min(max(a.values(),default=0), max(b.values(),default=0))
        return confidence * sum(min(a[t],b[t]) for t in a.keys()&b.keys()) / union if union else 0
    meaning = match(0)
    fallback = .05 if meaning >= .25 else 1-meaning
    return .8*meaning + .15*fallback*match(1) + .05*fallback*match(2)


def control_report(data, names):
    def cid(n):return f'orig_{n:04}'
    queries = [706,575,421,402,435,523,106,427,134]
    rankings = {cid(q):sorted(((k,similarity(data[cid(q)],v)) for k,v in data.items() if k!=cid(q)),key=lambda z:(-z[1],z[0])) for q in queries}
    checks = []
    def rank(a,b,limit):
        ranked=rankings[cid(a)]; position=next(i+1 for i,(k,_) in enumerate(ranked) if k==cid(b))
        score=similarity(data[cid(a)],data[cid(b)])
        tied_rank=1+sum(s>score+1e-12 for _,s in ranked)
        checks.append(dict(kind='rank',source=cid(a),target=cid(b),rank=position,tied_rank=tied_rank,limit=limit,passed=position<=limit,score=score))
    def closer(a,b,c,allow_equal=False):
        good=similarity(data[cid(a)],data[cid(b)]);bad=similarity(data[cid(a)],data[cid(c)])
        checks.append(dict(kind='comparison',source=cid(a),target=cid(b),distractor=cid(c),score=good,distractor_score=bad,passed=good>=bad if allow_equal else good>bad))
    rank(706,628,3);closer(706,628,339)
    rank(575,628,5);rank(575,279,5)
    rank(421,27,5);rank(402,27,5)
    closer(435,27,117,True);closer(435,27,395,True)
    for q in (523,106):
        closer(q,15,268);closer(q,15,628)
    rank(427,134,3);rank(134,427,3)
    for q in (427,134):
        closer(q,134 if q==427 else 427,120)
        pos=next(i+1 for i,(k,_) in enumerate(rankings[cid(q)]) if k==cid(120))
        checks.append(dict(kind='far',source=cid(q),target=cid(120),rank=pos,limit=10,passed=pos>10))
    return dict(checks=checks,top10={c:[dict(card=k,name=names[k][0],score=round(s,8)) for k,s in v[:10]] for c,v in rankings.items()})


def validate(root, tags, details, manual, associations):
    errors=[]
    expected={p.stem for p in root.glob('orig_*.webp')}
    if len(expected)!=761:errors.append(f'Expected 761 images, got {len(expected)}')
    vocab=set(re.findall(r'^\| `([^`]+)` \|', (root/'vocabulary.md').read_text('utf-8'),re.M))
    for name,data in [('tags',tags),('details',details),('details_manual',manual),('associations',associations)]:
        if set(data)!=expected:errors.append(f'{name}: key coverage differs from images')
        for card,values in data.items():
            keys=values if name=='tags' else [d['tag'] for d in values]
            if len(keys)!=len(set(keys)):errors.append(f'{name}/{card}: duplicate tags')
            for t in keys:
                if t not in vocab:errors.append(f'{name}/{card}: unknown tag {t}')
                if not re.fullmatch(r'[a-z]+(?:-[a-z]+)*',t):errors.append(f'{name}/{card}: invalid kebab-case {t}')
            if name!='tags':
                for d in values:
                    if set(d)!={'tag','weight','label'} or not d['label'].strip() or not isinstance(d['weight'],(float,int)) or isinstance(d['weight'],bool) or not math.isfinite(d['weight']) or not 0<d['weight']<=1:
                        errors.append(f'{name}/{card}: invalid weighted entry {d}')
                    if name=='associations' and (group(d['tag'])!=0 or d['tag'] in tags.get(card, []) or d['weight'] not in (.3,.5,.8,.9)):
                        errors.append(f'{name}/{card}: duplicate, nonsemantic, or invalid association weight')
    for card,values in tags.items():
        groups=[[t for t in values if group(t)==g] for g in range(3)]
        if not 6<=len(groups[0])<=14 or not 1<=len(groups[1])<=2 or not 1<=len(groups[2])<=2:errors.append(f'{card}: group counts {[len(g) for g in groups]}')
        if not ACTIONS.intersection(values) or not PROPERTIES.intersection(values):errors.append(f'{card}: missing action/property')
        if UNTRUSTED.intersection(values):errors.append(f'{card}: unreliable shape')
        if set(values)&{d['tag'] for d in details.get(card,[])}:errors.append(f'{card}: detail shadows main tag')
        for d in manual.get(card,[]):
            if d not in details.get(card,[]):errors.append(f'{card}: manual detail not merged')
    return errors


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=Path,default=ROOT);p.add_argument('--report',type=Path);p.add_argument('--structure-only',action='store_true');a=p.parse_args()
    def read(f):return json.loads((a.root/f).read_text('utf-8'))
    tags,details,manual,assoc=(read(f) for f in ('tags.json','details.json','details_manual.json','associations.json'))
    errors=validate(a.root,tags,details,manual,assoc)
    report=control_report(features(tags,details,assoc),read('names.json')) if not errors else {'checks': [], 'top10': {}}
    report['structural_errors']=errors
    report['scoring']={'method':'CardTags.Similarity(a,b)', 'attention':[.8,.15,.05], 'secondary_meanings':.35, 'association_scale':.5, 'details_attention':0, 'candidate_scope':'all 761 cards except the query', 'tie_break':'card ID ascending; tied_rank also reported'}
    report['stats']={'cards':len(tags),'tag_assignments':sum(map(len,tags.values())),'unique_main_tags':len(set(t for v in tags.values() for t in v)), 'group_assignments':{str(g):sum(group(t)==g for v in tags.values() for t in v) for g in range(3)}}
    if a.report:a.report.parent.mkdir(parents=True,exist_ok=True);a.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report['stats'],ensure_ascii=False))
    for e in errors:print('ERROR',e)
    if not a.structure_only:
        for c in report['checks']:print('PASS' if c['passed'] else 'FAIL',json.dumps(c,ensure_ascii=False))
        for card,top in report['top10'].items():print(card,' '.join(f"{r['card']}:{r['score']:.4f}" for r in top))
    print(f"Structure: {len(errors)} errors; controls: {sum(c['passed'] for c in report['checks'])}/{len(report['checks'])}")
    return 1 if errors or (not a.structure_only and any(not c['passed'] for c in report['checks'])) else 0

if __name__=='__main__':raise SystemExit(main())
