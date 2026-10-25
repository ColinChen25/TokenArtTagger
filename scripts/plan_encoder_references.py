"""Select per-target references from training-only embeddings, without target labels."""
import argparse
import html
import os

from encoder_common import REPO, checkpoint, exclusive_lock, file_hash, fingerprint, private_path, read_json
from train_encoder_probe import canonical_tags


def choose_reference(target, rows, vectors, assignments, excluded, maximum_similarity=.90):
    import numpy as np
    positions = {row['sha256']: i for i, row in enumerate(rows)}
    if target not in positions or assignments[target]['split'] != 'development':
        raise ValueError('Reference experiment targets must be existing development images.')
    similarities = vectors @ vectors[positions[target]]
    candidates = [(float(similarities[i]), row['sha256']) for i, row in enumerate(rows)
                  if assignments[row['sha256']]['split'] == 'train'
                  and assignments[row['sha256']]['group'] != assignments[target]['group']
                  and row['sha256'] not in excluded and float(similarities[i]) < maximum_similarity]
    if not candidates:
        raise ValueError('No eligible independent training reference.')
    score, sha = sorted(candidates, key=lambda pair: (-pair[0], pair[1]))[0]
    return sha, score


def main():
    import numpy as np
    from threadpoolctl import threadpool_limits
    parser = argparse.ArgumentParser()
    for name in ('dataset', 'features', 'study', 'inputs', 'target-manifest', 'output'):
        parser.add_argument('--'+name, required=True)
    args = parser.parse_args()
    dataset, features, study, inputs = [private_path(getattr(args, name)) for name in ('dataset','features','study','inputs')]
    output = private_path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    with exclusive_lock(output/'plan.lock'), threadpool_limits(limits=2):
        if (output/'STOP').exists():
            raise RuntimeError('paused: STOP marker exists.')
        for directory in (dataset, features, study):
            if read_json(directory/'status.json')['status'] != 'complete':
                raise ValueError('Complete data, features and classifier study first.')
        summary = read_json(study/'summary.json')
        feature_index = read_json(features/'index.json')
        if read_json(study/'classifiers.json')['featureFingerprint'] != feature_index['fingerprint']:
            raise ValueError('Study and feature identities differ.')
        source = {row['sha256']: row for row in read_json(dataset/'index.json')}
        assignments = {row['sha256']: row for row in read_json(study/'splits.json')['assignments']}
        if set(assignments) != set(source) or set(source) != set(feature_index['hashes']):
            raise ValueError('Source, features and study hashes differ.')
        targets = [row['sha256'] for row in read_json(private_path(args.target_manifest))['items']]
        if len(targets) != len(set(targets)) or not 1 <= len(targets) <= 32:
            raise ValueError('Expected 1-32 distinct existing development targets.')
        rows = [read_json(features/'items'/f'{sha}.json') for sha in sorted(source)]
        from extract_encoder_features import valid_vector
        if any(row['fingerprint'] != feature_index['fingerprint'] or not valid_vector(row['features']) for row in rows):
            raise ValueError('Invalid feature cache.')
        vectors = np.asarray([row['features'] for row in rows], dtype=np.float32)
        items = []
        for target in targets:
            reference, cosine = choose_reference(target, rows, vectors, assignments, set(targets))
            # Labels and paths are joined only after the visual-only choice is fixed.
            items.append(dict(sha256=target, referenceSha256=reference, cosine=cosine,
                              targetPath=source[target]['path'], referencePath=source[reference]['path'],
                              childDirectory='runs/'+target, planFile='plans/'+target+'.json'))
        settings = dict(version=1, method='training-only cosine nearest below .90, excludes target groups and all targets',
                        featureFingerprint=feature_index['fingerprint'], studyFingerprint=summary['studyFingerprint'],
                        implementationSha256=file_hash(__file__), targets=targets,
                        selections=[dict(target=r['sha256'], reference=r['referenceSha256']) for r in items])
        identity = fingerprint(settings)
        if (output/'plan.json').exists() and read_json(output/'plan.json')['fingerprint'] != identity:
            raise ValueError('Reference selection changed; use a new experiment directory.')
        (output/'plans').mkdir(exist_ok=True)
        for item in items:
            records = [source[item[key]] for key in ('sha256','referenceSha256')]
            child = dict(targets=[item['targetPath']], references=[item['referencePath']],
                         sources=[dict(path=r['path'], sha256=r['sha256'], tags=canonical_tags(r['tags'])) for r in records])
            path = output/item['planFile']
            if path.exists() and read_json(path) != child:
                raise ValueError('Reference source labels changed; use a new experiment directory.')
            checkpoint(path, child)
        checkpoint(output/'plan.json', dict(fingerprint=identity, settings=settings, items=items,
                                            references=[dict(sha256=sha) for sha in sorted({r['referenceSha256'] for r in items})],
                                            automaticAcceptance=False, visualAuditRequired=True))
        cards = []
        for item in items:
            pictures = ''
            for key, title in (('sha256','Target'),('referenceSha256','Reference')):
                path = inputs/'images'/f'{item[key]}.png'
                if not path.is_file():
                    raise ValueError('Missing cached MCP preview.')
                pictures += '<figure><img src="'+html.escape(os.path.relpath(path,output).replace('\\','/'),quote=True)+'"><figcaption>'+title+'</figcaption></figure>'
            cards.append('<article>'+pictures+'<p>Cosine '+f'{item["cosine"]:.3f}'+'</p><small>'+item['sha256']+'</small></article>')
        (output/'reference-audit.html').write_text('<!doctype html><meta charset="utf-8"><title>Retrieved reference audit</title><style>body{font:16px system-ui}article{margin:20px}figure{display:inline-block}img{width:260px;height:340px;object-fit:contain}small{display:block}</style><h1>Check for related artwork before inference</h1><p>Label-blind visual selection. Similarity is not confidence.</p>'+''.join(cards),encoding='utf-8')
        print(f'Prepared {len(items)} training-only references. Visual audit required before running.')
        print([(r['sha256'][:10],r['referenceSha256'][:10],round(r['cosine'],3)) for r in items])


if __name__ == '__main__':
    main()
