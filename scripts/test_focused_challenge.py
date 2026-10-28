"""Check focused-score denominators, abstention and a completed synthetic report; no inference."""
import copy
from pathlib import Path
import uuid

from encoder_common import REPO, checkpoint, file_hash, read_json
from export_focused_challenge import compose, export, metrics


def main():
    from PIL import Image
    root = REPO/'.cache/focused-challenge-tests'/uuid.uuid4().hex
    inputs = root/'inputs'
    (inputs/'images').mkdir(parents=True)
    entries = []
    rows = []
    for i, sha in enumerate(('a'*64, 'b'*64)):
        tags = dict(gender='female' if i == 0 else 'male', role='caster' if i == 0 else 'generic',
                    weaponOrStyle='cleric' if i == 0 else None, race='human' if i == 0 else 'elf')
        entries.append(dict(path=f'fixture-{i}.png', sha256=sha, hasGroundTruth=True, expected=tags))
        rows.append(dict(entries[-1], fingerprint='fixture', outcome='complete', elapsedSeconds=10,
                         prediction=dict(tags=dict(tags), evidence='<script>escaped text</script>', uncertainty='')))
        Image.new('RGB', (8, 8), ('red', 'blue')[i]).save(inputs/'images'/f'{sha}.png')
    rows[0]['outcome'] = 'invalid_output'
    rows[0]['reusedFrom'] = {'fingerprint': 'earlier-fixture'}
    role = copy.deepcopy(rows)
    race = copy.deepcopy(rows)
    for row in role:
        row['outcome'] = 'partial'
        row['prediction']['tags']['gender'] = row['prediction']['tags']['race'] = None
    for i, row in enumerate(race):
        row['outcome'] = 'partial'
        row['prediction']['tags']['gender'] = row['prediction']['tags']['role'] = row['prediction']['tags']['weaponOrStyle'] = None
        if i == 0:
            row['prediction']['tags']['race'] = None
    assert metrics(rows)['exactScopeMatches'] == 1
    m = metrics(role, ('role', 'weaponOrStyle'))
    assert m['exactScopeMatches'] == 2 and m['fields']['weaponOrStyle']['abstained'] == 0 and 'gender' not in m['fields']
    m = metrics(race, ('race',))
    assert m['exactScopeMatches'] == 1 and m['fields']['race']['abstained'] == 1
    combined = [compose(*triple) for triple in zip(rows, role, race)]
    assert combined[0]['outcome'] == 'invalid_component' and metrics(combined)['exactScopeMatches'] == 1
    for name, arm in (('control', rows), ('role-style', role), ('race', race)):
        directory = root/name
        (directory/'items').mkdir(parents=True)
        checkpoint(directory/'manifest.json', dict(root='fixture-library', fingerprint='fixture',
            items=entries, references=[], settings=dict(focus='all' if name == 'control' else name, maxDimension=768)))
        checkpoint(directory/'status.json', dict(status='complete', cleanupComplete=True))
        for row in arm:
            checkpoint(directory/'items'/f'{row["sha256"]}.json', row)
    checkpoint(root/'challenge-settings.json', dict(fingerprint='challenge-fixture', settings=dict(taggedCount=2)))
    checkpoint(root/'driver-status.json', dict(status='complete', cleanupComplete=True, fingerprint='challenge-fixture'))
    checkpoint(inputs/'inputs.json', dict(settings=dict(maxDimension=768), items=[dict(sha256=row['sha256'],
        preview='images/'+row['sha256']+'.png', previewSha256=file_hash(inputs/'images'/f'{row["sha256"]}.png')) for row in rows]))
    export(root, inputs)
    report = read_json(root/'summary.json')
    assert report['cohorts']['all']['composition']['exactScopeMatches'] == 1
    assert report['cohorts']['previousFocusedTargets']['control']['attempted'] == 1
    assert report['cohorts']['additionalDevelopmentTargets']['control']['attempted'] == 1
    assert report['paired']['role-style']['improvedFocusedMatches'] == 1
    assert all(value is None for arm in report['newInferenceSessionMemory'].values() for value in arm.values())
    page = (root/'review.html').read_text(encoding='utf-8')
    assert '<script>' not in page and '&lt;script&gt;' in page and page.count('<img ') == 2
    # All arms must reuse exactly the same earlier cohort.
    race[0].pop('reusedFrom')
    checkpoint(root/'race/items'/f'{race[0]["sha256"]}.json', race[0])
    try:
        export(root, inputs)
        raise AssertionError('Unequal reuse cohorts accepted.')
    except ValueError as error:
        assert 'different cohorts' in str(error)
    checkpoint(root/'result.json', dict(passed=True, inference=False, denominators=True, abstention=True,
        genericNotAbstention=True, compositionFailuresRetained=True, pairedCohorts=True, escapedHtml=True))
    print(f'PASS: focused metrics and paired static review; no inference. Evidence: {root}')


if __name__ == '__main__':
    main()
