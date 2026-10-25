"""Aggregate completed one-target benchmark runs using the established paired comparison."""
import argparse
import html
import os
import statistics
import subprocess

from encoder_common import REPO, checkpoint, private_path, read_json
from export_encoder_review import paired_comparison
from train_encoder_probe import FIELDS, canonical_tags


def read_completed_run(directory):
    directory = private_path(directory)
    status = read_json(directory/'status.json')
    if status['status'] != 'complete' or not status['cleanupComplete']:
        raise ValueError('Finish and clean up every source run first.')
    manifest = read_json(directory/'manifest.json')
    entries = {row['sha256']: row for row in manifest['items']}
    records = [read_json(path) for path in (directory/'items').glob('*.json')]
    if len(records) != len(entries) or len({r['sha256'] for r in records}) != len(records):
        raise ValueError('Duplicate or missing benchmark result.')
    for row in records:
        entry = entries.get(row['sha256'])
        if (entry is None or row['fingerprint'] != manifest['fingerprint'] or row['path'] != entry['path']
                or canonical_tags(row['expected']) != canonical_tags(entry['expected'])):
            raise ValueError('Benchmark result differs from its manifest.')
    return records, manifest


def main():
    parser = argparse.ArgumentParser()
    for name in ('study','inputs','baseline','fixed-reference'):
        parser.add_argument('--'+name, required=True)
    args = parser.parse_args()
    study, inputs, baseline, fixed = [private_path(getattr(args,name)) for name in ('study','inputs','baseline','fixed_reference')]
    status = read_json(study/'driver-status.json')
    plan = read_json(study/'plan.json')
    if status['status'] != 'complete' or not status['cleanupComplete'] or status['fingerprint'] != plan['fingerprint']:
        raise ValueError('Complete the current retrieval experiment first.')
    before, _ = read_completed_run(baseline)
    fixed_rows, _ = read_completed_run(fixed)
    results = []
    reference_tags = {}
    session_memory = []
    for item in plan['items']:
        child = private_path(study/item['childDirectory'])
        if not child.is_relative_to(study):
            raise ValueError('Child run escapes study.')
        rows, manifest = read_completed_run(child)
        if len(rows) != 1 or rows[0]['sha256'] != item['sha256'] or [r['sha256'] for r in manifest['references']] != [item['referenceSha256']]:
            raise ValueError('Completed child differs from the retrieval plan.')
        reference_tags[item['sha256']] = manifest['references'][0].get('tags')
        # Reuse the established comparator for each run; retain both comparison receipts.
        for name, directory in (('baseline',baseline),('fixed-reference',fixed)):
            command=['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',
                     str(REPO/'scripts/Compare-LocalAiBenchmark.ps1'),'-BaselineDirectory',str(directory),'-CandidateDirectory',str(child)]
            result=subprocess.run(command,capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
            if result.returncode:
                raise RuntimeError(result.stderr.decode(errors='replace'))
            checkpoint(child/('comparison-'+name+'.json'),read_json(child/'comparison.json'))
        results += rows
        for path in child.glob('session-*.json'):
            session = read_json(path)
            if session.get('completedThisRun',0) and 'peakWorkingSetBytes' in session:
                session_memory.append(session)
    candidate = [dict(row,prediction=row['prediction']['tags'] if row['outcome'] in ('complete','partial') and row.get('prediction') else None) for row in results]
    paired = paired_comparison(before,candidate)
    fixed_comparison = paired_comparison(fixed_rows,candidate)
    if paired['paired'] != len(plan['items']) or fixed_comparison['paired'] != len(plan['items']):
        raise ValueError('Comparisons do not cover exactly the declared target set.')
    from collections import Counter
    durations=[row['elapsedSeconds'] for row in results if row.get('elapsedSeconds') is not None]
    prompts=[row['response']['prompt_eval_count'] for row in results if row.get('response')]
    report = dict(planFingerprint=plan['fingerprint'], automaticAcceptance=False, finalTestEvaluated=False,
                  againstBaseline=paired, againstFixedReference=fixed_comparison,
                  outcomes=dict(Counter(row['outcome'] for row in results)),
                  medianSeconds=statistics.median(durations) if durations else None,
                  maxPromptTokens=max(prompts,default=None),
                  memory={name:max((row[name] for row in session_memory),default=None) for name in
                          ('peakWorkingSetBytes','peakPrivateBytes','peakCombinedWorkingSetBytes')})
    checkpoint(study/'summary.json',report)
    def image(sha,title):
        path=inputs/'images'/f'{sha}.png'
        if not path.is_file():
            raise ValueError('Missing cached MCP review image.')
        return '<figure><img src="'+html.escape(os.path.relpath(path,study).replace('\\','/'),quote=True)+'"><figcaption>'+title+'</figcaption></figure>'
    def tags(value):
        return 'No valid suggestion' if value is None else ' / '.join(str(value[k]) if value[k] is not None else '-' for k in FIELDS)
    cards=[]
    for item,row,converted in zip(plan['items'],results,candidate):
        cards.append('<article>'+image(item['sha256'],'Target')+image(item['referenceSha256'],'Retrieved reference')+
                     '<p>Target filename: '+html.escape(tags(row['expected']))+'</p><p>Reference filename: '+html.escape(tags(reference_tags[item['sha256']]))+
                     '</p><p>Suggestion: '+html.escape(tags(converted['prediction']))+
                     '</p><p>Outcome: '+html.escape(row['outcome'])+'</p><p>'+html.escape((row.get('prediction') or {}).get('evidence',''))+
                     '</p><p>'+html.escape(row.get('reason') or (row.get('prediction') or {}).get('uncertainty',''))+'</p></article>')
    document='<!doctype html><meta charset="utf-8"><title>Retrieved reference results</title><style>body{font:16px system-ui;background:#f5f5f5;margin:24px}article{padding:16px;background:white;margin:20px 0}figure{display:inline-block}img{width:260px;height:340px;object-fit:contain}</style><h1>Retrieved reference development test</h1><p>'+str(len(results))+' previously exposed targets; review only. Final test remains unscored. Similarity is not confidence.</p>'+''.join(cards)
    (study/'review.html').write_text(document,encoding='utf-8')
    print(dict(targets=paired['paired'],baseline=paired['baseline']['fullMatches'],
               fixed=fixed_comparison['baseline']['fullMatches'],retrieved=paired['candidate']['fullMatches'],
               outcomes=report['outcomes'],medianSeconds=report['medianSeconds'],maxPromptTokens=report['maxPromptTokens']))


if __name__ == '__main__':
    main()
