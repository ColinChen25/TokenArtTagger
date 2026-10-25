"""Private static development review, paired baseline counts and content-group audit."""
import argparse
from collections import defaultdict
import html
import os
from pathlib import Path
from encoder_common import checkpoint, private_path, read_json
from train_encoder_probe import FIELDS, canonical_tags, measure, tags_equal


def paired_comparison(baseline, candidate):
    left={};right={}
    for rows,lookup in ((baseline,left),(candidate,right)):
        for row in rows:
            if not row.get('hasGroundTruth',True):
                continue
            if row['sha256'] in lookup:
                raise ValueError('Duplicate content in comparison.')
            lookup[row['sha256']]=row
    shared=sorted(set(left)&set(right))
    truth=[];before=[];after=[]
    for sha in shared:
        a,b=left[sha],right[sha]
        if canonical_tags(a['expected'])!=canonical_tags(b['expected']):
            raise ValueError('Conflicting authoritative labels in comparison.')
        truth.append(canonical_tags(a['expected']))
        # The strict benchmark outcome governs validity; keep invalid/refused attempts in the denominator.
        before.append(a['prediction']['tags'] if a['outcome'] in ('complete','partial') and a.get('prediction') else None)
        after.append(b['prediction'])
    return dict(paired=len(shared),baselineOnly=len(set(left)-set(right)),candidateOnly=len(set(right)-set(left)),
                baseline=measure(truth,before),candidate=measure(truth,after),
                improved=sum(not tags_equal(t,a) and tags_equal(t,b) for t,a,b in zip(truth,before,after)),
                regressed=sum(tags_equal(t,a) and not tags_equal(t,b) for t,a,b in zip(truth,before,after)),
                evaluation='previously exposed development images; not a final test')


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--study',required=True)
    parser.add_argument('--inputs',required=True)
    parser.add_argument('--baseline')
    parser.add_argument('--annotations')
    args=parser.parse_args()
    study=private_path(args.study);inputs=private_path(args.inputs)
    summary=read_json(study/'summary.json')
    if summary['status']!='complete' or read_json(study/'status.json')['status']!='complete':
        raise ValueError('Complete the study before exporting its review.')
    predictions=read_json(study/'predictions.json')
    if any(r['split'] not in ('development','calibration') for r in predictions):
        raise ValueError('Unexpected reserved-test or training predictions.')
    paired=None
    if args.baseline:
        baseline_dir=private_path(args.baseline)
        baseline_status=read_json(baseline_dir/'status.json')
        if baseline_status['status']!='complete' or not baseline_status['cleanupComplete']:
            raise ValueError('Baseline must be a completed clean run.')
        baseline=[read_json(path) for path in (baseline_dir/'items').glob('*.json')]
        paired=paired_comparison(baseline,[r for r in predictions if r['split']=='development'])
        checkpoint(study/'qwen-comparison.json',paired)
    groups={}
    if args.annotations:
        lookup={r['sha256']:r for r in predictions}
        collected=defaultdict(list)
        for entry in read_json(private_path(args.annotations)):
            if entry['sha256'] in lookup:
                for group in entry['groups']:
                    collected[group].append(lookup[entry['sha256']])
        for name,rows in collected.items():
            groups[name]=dict(metrics=measure([r['expected'] for r in rows],[r['prediction'] for r in rows]),
                              processingFailures=0,contentGate=False,
                              limitation='Small overlapping development groups; no general content-accuracy claim.')
        checkpoint(study/'content-group-summary.json',groups)
    def tags(value):
        return ' / '.join(str(value[field]) if value[field] is not None else '-' for field in FIELDS)
    def image(sha):
        path=inputs/'images'/f'{sha}.png'
        if not path.is_file():
            raise ValueError('Missing review preview.')
        relative=os.path.relpath(path,study).replace('\\','/')
        return '<img loading="lazy" src="'+html.escape(relative,quote=True)+'">'
    cards=[]
    for row in sorted(predictions,key=lambda r:(r['split'],tags_equal(r['expected'],r['prediction']),-min(r['scores'].values()),r['sha256'])):
        correct=tags_equal(row['expected'],row['prediction'])
        score=', '.join(f'{name}: {value:.2f}' for name,value in row['scores'].items())
        cards.append('<article class="'+('correct' if correct else 'incorrect')+'"><div class="pictures"><figure>'+image(row['sha256'])+'<figcaption>Target</figcaption></figure><figure>'+image(row['nearestTrainingHash'])+'<figcaption>Nearest training example</figcaption></figure></div><h2>'+html.escape(row['split'])+': '+('Full match' if correct else 'Mismatch')+'</h2><p><b>Filename:</b> '+html.escape(tags(row['expected']))+'</p><p><b>Suggested:</b> '+html.escape(tags(row['prediction']))+'</p><p>Uncalibrated scores: '+html.escape(score)+'</p><small>'+row['sha256']+'</small></article>')
    heading='<h1>Encoder classifier development review</h1><p>Review-only output. Final-test images are reserved and absent. Filename labels are authoritative. Scores are not calibrated probabilities; the variant audit is incomplete.</p>'
    if paired:
        heading+=f'<p>Same {paired["paired"]} development targets: Qwen full matches {paired["baseline"]["fullMatches"]}; encoder classifier {paired["candidate"]["fullMatches"]}.</p>'
    document='<!doctype html><html lang="en"><meta charset="utf-8"><title>Encoder development review</title><style>body{font:16px system-ui;background:#f3f4f5;margin:24px;color:#17202b}article{background:white;border-left:5px solid #b96d12;padding:16px;margin:16px 0;max-width:900px}article.correct{border-color:#24815b}.pictures{display:flex;gap:20px}figure{margin:0}img{width:260px;height:340px;object-fit:contain;background:#f5f5f5}h2{font-size:18px}small{overflow-wrap:anywhere;color:#555}</style>'+heading+''.join(cards)+'</html>'
    (study/'review.html').write_text(document,encoding='utf-8')
    print(f'Exported {len(cards)} development/calibration review cards.')
    if paired:
        print(paired)


if __name__=='__main__':
    main()
