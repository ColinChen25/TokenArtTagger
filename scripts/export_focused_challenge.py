"""Paired field/composition diagnostics and private review for a completed challenge."""
import argparse
from collections import Counter
import html
import os
import statistics
import subprocess

from encoder_common import REPO, checkpoint, file_hash, private_path, read_json
from export_retrieved_reference_review import read_completed_run
from train_encoder_probe import FIELDS, canonical_tags


def prediction(row):
    return row['prediction']['tags'] if row['outcome'] in ('complete', 'partial') and row.get('prediction') else None


def metrics(rows, fields=FIELDS):
    def correct(row, field):
        tags = prediction(row)
        return (tags is not None and tags[field] == row['expected'][field]
                and (field != 'weaponOrStyle' or tags[field] is not None or tags['role'] == 'generic'))
    scopes = {}
    for field in fields:
        issued = [row for row in rows if prediction(row) is not None and prediction(row)[field] is not None]
        scopes[field] = dict(correctAllAttempts=sum(correct(row, field) for row in rows),
            issuedNonNull=len(issued), correctIssued=sum(correct(row, field) for row in issued),
            abstained=sum(prediction(row) is not None and prediction(row)[field] is None
                          and not (field == 'weaponOrStyle' and prediction(row)['role'] == 'generic') for row in rows))
    durations = [row['elapsedSeconds'] for row in rows if row.get('elapsedSeconds') is not None]
    return dict(attempted=len(rows), validOutputs=sum(prediction(row) is not None for row in rows),
        exactScopeMatches=sum(all(correct(row, field) for field in fields) for row in rows),
        evaluatedFields=list(fields), fields=scopes, outcomes=dict(Counter(row['outcome'] for row in rows)),
        medianSavedRequestSeconds=statistics.median(durations) if durations else None)


def compose(control, role, race):
    if any(canonical_tags(row['expected']) != canonical_tags(control['expected']) for row in (role, race)):
        raise ValueError('Composition source labels disagree.')
    sources = [prediction(row) for row in (control, role, race)]
    tags = None
    if all(source is not None for source in sources):
        tags = dict(gender=sources[0]['gender'], role=sources[1]['role'],
                    weaponOrStyle=sources[1]['weaponOrStyle'], race=sources[2]['race'])
    return dict(sha256=control['sha256'], expected=control['expected'], hasGroundTruth=True,
                outcome='partial' if tags is not None else 'invalid_component',
                prediction={'tags': tags} if tags is not None else None,
                elapsedSeconds=sum(row['elapsedSeconds'] for row in (control, role, race))
                if all(row.get('elapsedSeconds') is not None for row in (control, role, race)) else None)


def export(study, inputs, baseline=None, annotations=None):
    state = read_json(study/'driver-status.json')
    saved_settings = read_json(study/'challenge-settings.json')
    if state['status'] != 'complete' or not state['cleanupComplete'] or state['fingerprint'] != saved_settings['fingerprint']:
        raise ValueError('Complete all challenge arms and cleanup before exporting.')
    runs, manifests = {}, {}
    for arm in ('control', 'role-style', 'race'):
        rows, manifest = read_completed_run(study/arm)
        if not all(row['hasGroundTruth'] for row in rows):
            raise ValueError('The challenge requires authoritative target labels.')
        if any(manifest.get('references', [])):
            raise ValueError('This report compares single-image runs only.')
        runs[arm] = {row['sha256']: row for row in rows}
        manifests[arm] = manifest
    hashes = set(runs['control'])
    if any(set(rows) != hashes for rows in runs.values()) or len(hashes) != saved_settings['settings']['taggedCount']:
        raise ValueError('Challenge arms do not cover the same target set.')
    cached = {name: {sha for sha, row in rows.items() if row.get('reusedFrom')} for name, rows in runs.items()}
    if len({tuple(sorted(values)) for values in cached.values()}) != 1:
        raise ValueError('Challenge arms reused different cohorts.')
    reused = cached['control']
    cohorts = {'all': hashes, 'previousFocusedTargets': reused, 'additionalDevelopmentTargets': hashes-reused}
    summaries = {}
    for name, selection in cohorts.items():
        summaries[name] = {
            'control': metrics([runs['control'][sha] for sha in sorted(selection)]),
            'focusedRoleStyle': metrics([runs['role-style'][sha] for sha in sorted(selection)], ('role', 'weaponOrStyle')),
            'focusedRace': metrics([runs['race'][sha] for sha in sorted(selection)], ('race',)),
            'composition': metrics([compose(*(runs[arm][sha] for arm in ('control', 'role-style', 'race'))) for sha in sorted(selection)])}
    paired = {}
    for arm, focus in (('role-style', 'role-style'), ('race', 'race')):
        command = ['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
                   str(REPO/'scripts/Compare-LocalAiBenchmark.ps1'), '-BaselineDirectory', str(study/'control'),
                   '-CandidateDirectory', str(study/arm), '-Focus', focus]
        completed = subprocess.run(command, capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)
        if completed.returncode:
            raise RuntimeError(completed.stderr.decode(errors='replace'))
        paired[arm] = read_json(study/arm/'comparison.json')
        if paired[arm]['paired'] != len(hashes):
            raise ValueError('Paired comparison omitted targets.')
    if baseline:
        before, _ = read_completed_run(baseline)
        by_hash = {row['sha256']: row for row in before}
        if not hashes <= set(by_hash):
            raise ValueError('Original baseline does not cover the challenge.')
        for sha in hashes:
            if canonical_tags(by_hash[sha]['expected']) != canonical_tags(runs['control'][sha]['expected']):
                raise ValueError('Original baseline labels disagree.')
        summaries['all']['originalBaseline'] = metrics([by_hash[sha] for sha in sorted(hashes)])
    groups = {}
    if annotations:
        by_hash = {row['sha256']: row for row in read_json(annotations)}
        for group in sorted({g for row in by_hash.values() for g in row['groups']}):
            selection = hashes & {sha for sha, row in by_hash.items() if group in row['groups']}
            groups[group] = {arm: metrics([rows[sha] for sha in sorted(selection)],
                ('role', 'weaponOrStyle') if arm == 'role-style' else ('race',) if arm == 'race' else FIELDS)
                for arm, rows in runs.items()}
    memory = {}
    for arm in runs:
        sessions = [read_json(path) for path in (study/arm).glob('session-*.json')]
        sessions = [row for row in sessions if row.get('completedThisRun', 0) and 'peakWorkingSetBytes' in row]
        memory[arm] = {field: max((row[field] for row in sessions), default=None) for field in
                      ('peakWorkingSetBytes', 'peakPrivateBytes', 'peakCombinedWorkingSetBytes')}
    report = dict(fingerprint=state['fingerprint'], cohorts=summaries, paired=paired, contentGroups=groups,
        newInferenceSessionMemory=memory, automaticAcceptance=False, finalTestEvaluated=False,
        caveats=['All images were previously exposed development data; no final-test claim.',
                 'Composition reuses three saved responses and is not a new end-to-end model run.',
                 'Saved request timings include earlier cached answers; composition time sums three requests.',
                 'Required null fields outside the question scope are not abstentions.',
                 'No classifier confidence or acceptance threshold has been calibrated.'])
    previews = read_json(inputs/'inputs.json')
    by_hash = {row['sha256']: row for row in previews['items']}
    if any(manifest['settings']['maxDimension'] != previews['settings']['maxDimension'] for manifest in manifests.values()):
        raise ValueError('Review preview dimensions differ from inference settings.')
    def tags_row(label, value):
        return '<tr><th>'+label+'</th>'+''.join('<td>'+html.escape(str(value.get(field) or '-'))+'</td>' for field in FIELDS)+'</tr>'
    cards = []
    for sha in sorted(hashes):
        preview = by_hash[sha]
        path = private_path(inputs/preview['preview'])
        if not path.is_relative_to(inputs) or file_hash(path) != preview['previewSha256'] or path.read_bytes()[:8] != b'\x89PNG\r\n\x1a\n':
            raise ValueError('Review PNG differs from the verified MCP preview.')
        src = html.escape(os.path.relpath(path, study).replace('\\', '/'), quote=True)
        table = tags_row('Filename', runs['control'][sha]['expected'])
        evidence = []
        for arm in runs:
            row = runs[arm][sha]
            table += tags_row(html.escape(arm), prediction(row) or {})
            evidence.append('<p><b>'+html.escape(arm)+':</b> '+html.escape(row['outcome'])+'; '+
                            html.escape((row.get('prediction') or {}).get('evidence', '') or row.get('reason', '') or '')+'</p>')
        combined = compose(*(runs[arm][sha] for arm in ('control', 'role-style', 'race')))
        table += tags_row('Composed fields', prediction(combined) or {})
        cohort = 'Earlier eight-image pilot' if sha in reused else 'Additional development target'
        cards.append('<article><img loading="lazy" src="'+src+'" alt="Target artwork"><h2>'+sha[:12]+
                     '</h2><p>'+cohort+'</p><table><tr><th>Source</th><th>Gender</th><th>Role</th><th>Style</th><th>Race</th></tr>'+
                     table+'</table><details><summary>Model evidence and outcomes</summary>'+''.join(evidence)+'</details></article>')
    page='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Focused-field challenge review</title><style>body{font:16px/1.5 system-ui,sans-serif;background:#f5f4ef;color:#202b35;margin:24px}main{max-width:1100px;margin:auto}article{padding:20px;border:1px solid #d8dde0;background:white;margin:22px 0;border-radius:10px;overflow:auto}img{width:100%;height:360px;object-fit:contain;background:#e8eaed}table{width:100%;border-collapse:collapse}th,td{text-align:left;border-bottom:1px solid #ddd;padding:8px}h2{font-size:17px}details{margin-top:16px}.notice{background:#fff1cf;padding:15px}</style><main><h1>Focused-field development challenge</h1><p class="notice">Review-only results from previously exposed images. Filename labels remain authoritative. No app tags or filenames were changed. Blank fields outside a question's scope were deliberately unrequested; composition is an analysis of saved answers.</p>'''+''.join(cards)+'</main></html>'
    (study/'review.html').write_text(page, encoding='utf-8')
    checkpoint(study/'summary.json', report)
    checkpoint(study/'review-validation.json', dict(cards=len(cards), verifiedPngReferences=len(cards),
               remoteResources=False, scripts=False, browserRendered=False))
    print({key: {name: value['exactScopeMatches'] for name, value in cohort.items()} for key, cohort in summaries.items()})


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--study', required=True)
    parser.add_argument('--inputs', required=True)
    parser.add_argument('--baseline')
    parser.add_argument('--annotations')
    args = parser.parse_args()
    export(private_path(args.study), private_path(args.inputs),
           private_path(args.baseline) if args.baseline else None,
           private_path(args.annotations) if args.annotations else None)
