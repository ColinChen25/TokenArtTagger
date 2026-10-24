"""Development-only grouped linear probe. Final-test groups stay unevaluated."""
import argparse
from collections import Counter, defaultdict
import html
import os

from encoder_common import checkpoint, exclusive_lock, fingerprint, private_path, read_json, study_status

FIELDS = ('gender', 'role', 'weaponOrStyle', 'race')


def make_groups(records, vectors):
    """Conservative, label-blind variant screening; not a verified duplicate detector."""
    import numpy as np
    parent = list(range(len(records)))

    def find(i):
        while i != parent[i]:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    edges = []
    similarity = vectors @ vectors.T
    for i in range(len(records)):
        for j in np.flatnonzero(similarity[i, i+1:] >= .80) + i + 1:
            cosine = float(similarity[i, j])
            ph = (int(records[i]['phash'], 16) ^ int(records[j]['phash'], 16)).bit_count()
            dh = (int(records[i]['dhash'], 16) ^ int(records[j]['dhash'], 16)).bit_count()
            if cosine >= .90 or (cosine >= .80 and ph <= 12) or (cosine >= .85 and dh <= 10):
                parent[find(int(j))] = find(i)
                edges.append(dict(a=records[i]['sha256'], b=records[j]['sha256'], cosine=cosine, phashDistance=ph, dhashDistance=dh))
    members = defaultdict(list)
    for i, row in enumerate(records):
        members[find(i)].append(row['sha256'])
    groups = {sha: min(group) for group in members.values() for sha in group}
    return groups, edges, similarity


def assign_splits(groups, development):
    dev_groups = {groups[sha] for sha in development if sha in groups}
    assignments = {}
    for sha, group in groups.items():
        fraction = int(fingerprint(['group-split-v1', group])[:8], 16) / 2**32
        assignments[sha] = ('development' if group in dev_groups else
                            'train' if fraction < .6 else 'calibration' if fraction < .8 else 'reserved_test')
    return assignments


def labels(tags):
    return dict(gender=tags['gender'], race=tags['race'], roleStyle=tags['role']+'|'+(tags['weaponOrStyle'] or ''))


def decode(values):
    role, style = values['roleStyle'].split('|')
    return dict(gender=values['gender'], race=values['race'], role=role, weaponOrStyle=style or None)


def measure(truth, predictions):
    total = len(truth)
    def matches(a,b,field):
        return b is not None and a[field] == b[field] and (field != 'weaponOrStyle' or b[field] is not None or b['role'] == 'generic')
    per_class={}
    for field in FIELDS:
        entries=[]
        for label in sorted({a[field] for a in truth},key=str):
            support=sum(a[field]==label for a in truth)
            issued=sum(b is not None and b[field]==label and (label is not None or b['role']=='generic') for b in predictions)
            correct=sum(a[field]==label and matches(a,b,field) for a,b in zip(truth,predictions))
            entries.append(dict(tag=label,support=support,predicted=issued,correct=correct,
                                precision=correct/issued if issued else None,recall=correct/support))
        per_class[field]=entries
    return dict(total=total, fullMatches=sum(a == b for a, b in zip(truth, predictions)),
                fields={field: dict(correct=sum(matches(a,b,field) for a,b in zip(truth,predictions)), total=total)
                        for field in FIELDS},perClass=per_class)


def wilson_lower(correct, total):
    if not total:
        return 0.
    p = correct/total
    z = 1.96
    return (p+z*z/(2*total)-z*((p*(1-p)+z*z/(4*total))/total)**.5)/(1+z*z/total)


def fit_probe(x, targets):
    from sklearn.dummy import DummyClassifier
    from sklearn.linear_model import LogisticRegression
    models = {}
    for name in ('gender', 'roleStyle', 'race'):
        y = [labels(tags)[name] for tags in targets]
        model = LogisticRegression(C=10, max_iter=2000, solver='lbfgs', random_state=42) if len(set(y)) > 1 else DummyClassifier(strategy='most_frequent')
        model.fit(x, y)
        if hasattr(model, 'n_iter_') and max(model.n_iter_) >= 2000:
            raise RuntimeError('Linear classifier did not converge.')
        models[name] = model
    return models


def predict(models, x):
    import numpy as np
    values = {name: model.predict_proba(x) for name, model in models.items()}
    predictions = []
    scores = []
    for i in range(len(x)):
        selected = {name: str(models[name].classes_[np.argmax(probs[i])]) for name, probs in values.items()}
        predictions.append(decode(selected))
        scores.append({name: float(max(probs[i])) for name, probs in values.items()})
    return predictions, scores


def main():
    import numpy as np
    from threadpoolctl import threadpool_limits
    parser = argparse.ArgumentParser()
    parser.add_argument('--dataset', required=True)
    parser.add_argument('--features', required=True)
    parser.add_argument('--inputs', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--development-manifest', action='append', required=True)
    parser.add_argument('--app-pid', type=int)
    args = parser.parse_args()
    output = private_path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    from extract_encoder_features import Monitor
    with exclusive_lock(output / 'worker.lock'), threadpool_limits(limits=2), study_status(output), Monitor(args.app_pid) as monitor:
        monitor.sample()
        monitor.check(output / 'STOP')
        dataset = private_path(args.dataset)
        features = private_path(args.features)
        inputs = private_path(args.inputs)
        if read_json(dataset/'status.json')['status'] != 'complete' or read_json(features/'status.json')['status'] != 'complete':
            raise ValueError('Complete source and feature exports first.')
        index = read_json(dataset / 'index.json')
        by_hash = {}
        for row in index:
            sha = row['sha256']
            if sha in by_hash and by_hash[sha]['tags'] != row['tags']:
                raise ValueError('Conflicting filename labels for identical content.')
            by_hash[sha] = row
        feat_index = read_json(features / 'index.json')
        if len(set(feat_index['hashes'])) != len(feat_index['hashes']) or set(feat_index['hashes']) != set(by_hash):
            raise ValueError('Dataset and feature identities differ or duplicate.')
        from extract_encoder_features import valid_vector
        rows = [read_json(features/'items'/f'{sha}.json') for sha in sorted(by_hash)]
        if any(row['fingerprint'] != feat_index['fingerprint'] or not valid_vector(row['features']) for row in rows):
            raise ValueError('Feature identity or vector invalid.')
        x = np.asarray([row['features'] for row in rows], dtype=np.float32)
        truth = [by_hash[row['sha256']]['tags'] for row in rows]
        development = set()
        for path in args.development_manifest:
            manifest = read_json(private_path(path))
            for row in manifest.get('items', []) + manifest.get('references', []):
                if row.get('sha256'):
                    development.add(row['sha256'])
        groups, edges, similarity = make_groups(rows, x)
        monitor.check(output / 'STOP')
        splits = assign_splits(groups, development)
        assignments = [dict(sha256=row['sha256'], group=groups[row['sha256']], split=splits[row['sha256']]) for row in rows]
        split_identity = fingerprint(dict(features=feat_index['fingerprint'], assignments=assignments, version=1))
        if (output/'splits.json').exists() and read_json(output/'splits.json')['fingerprint'] != split_identity:
            raise ValueError('Partition changed; use a new experiment directory.')
        checkpoint(output/'splits.json', dict(fingerprint=split_identity, assignments=assignments,
                   thresholds=dict(cosine=.90, phashAtCosine80=12, dhashAtCosine85=10), auditComplete=False))
        checkpoint(output/'variant-edges.json', edges)
        positions = {name: [i for i, row in enumerate(rows) if splits[row['sha256']] == name]
                     for name in ('train', 'calibration', 'development', 'reserved_test')}
        if any(not positions[name] for name in positions):
            raise ValueError('One partition is empty.')
        train = positions['train']
        models = fit_probe(x[train], [truth[i] for i in train])
        monitor.check(output / 'STOP')
        majority = {name: Counter(labels(truth[i])[name] for i in train).most_common(1)[0][0]
                    for name in models}
        support = {name: Counter(labels(truth[i])[name] for i in train) for name in models}
        group_support={name:{label:len({groups[rows[i]['sha256']] for i in train if labels(truth[i])[name]==label})
                             for label in counts} for name,counts in support.items()}
        results = []
        metrics = {}
        for split in ('calibration', 'development'):
            selected = positions[split]
            predictions, scores = predict(models, x[selected])
            nearest = np.argmax(x[selected] @ x[train].T, axis=1)
            expected = [truth[i] for i in selected]
            metrics[split] = dict(linear=measure(expected, predictions),
                nearestNeighbor=measure(expected, [truth[train[i]] for i in nearest]),
                majority=measure(expected, [decode(majority) for i in selected]))
            for offset, i in enumerate(selected):
                results.append(dict(sha256=rows[i]['sha256'], group=groups[rows[i]['sha256']], split=split, expected=truth[i],
                    prediction=predictions[offset], scores=scores[offset], automaticAcceptance=False,
                    trainingSupport={name:support[name].get(labels(predictions[offset])[name],0) for name in models},
                    trainingGroupSupport={name:group_support[name].get(labels(predictions[offset])[name],0) for name in models},
                    nearestTrainingHash=rows[train[nearest[offset]]]['sha256']))
        # Threshold fitting is exploratory; these scores are not calibrated probabilities.
        thresholds = {}
        calibration = [r for r in results if r['split'] == 'calibration']
        for name in models:
            thresholds[name] = {}
            for label in models[name].classes_:
                chosen = None
                for threshold in np.arange(.5, 1, .01):
                    issued = [r for r in calibration if labels(r['prediction'])[name] == label and r['scores'][name] >= threshold]
                    correct = sum(labels(r['expected'])[name] == label for r in issued)
                    grouped=defaultdict(list)
                    for row in issued:
                        grouped[row['group']].append(labels(row['expected'])[name]==label)
                    correct_groups=sum(all(values) for values in grouped.values())
                    lower=wilson_lower(correct_groups,len(grouped))
                    if len(grouped) >= 20 and lower >= .95 and group_support[name][label] >= 20:
                        chosen = dict(threshold=float(threshold), correct=correct, issued=len(issued),
                                      correctGroups=correct_groups,issuedGroups=len(grouped),groupWilsonLower=lower)
                        break
                thresholds[name][str(label)] = chosen
        # Surface closest remaining cross-partition pairs for visual review before final testing.
        audit = []
        for i, row in enumerate(rows):
            other = [j for j in range(len(rows)) if splits[row['sha256']] != splits[rows[j]['sha256']]]
            j = max(other, key=lambda j: similarity[i,j])
            if i < j or similarity[i,j] >= .84:
                audit.append(dict(a=row['sha256'], b=rows[j]['sha256'], cosine=float(similarity[i,j]),
                                  aSplit=splits[row['sha256']], bSplit=splits[rows[j]['sha256']]))
        audit.sort(key=lambda pair: -pair['cosine'])
        checkpoint(output/'cross-partition-audit.json', audit)
        cards=[]
        for pair in audit[:200]:
            images=''.join('<img loading="lazy" src="'+html.escape(os.path.relpath(inputs/'images'/f'{pair[k]}.png',output).replace('\\','/'),quote=True)+'">' for k in ('a','b'))
            cards.append(f'<article>{images}<p>Similarity {pair["cosine"]:.3f}; {pair["aSplit"]} / {pair["bSplit"]}</p><small>{pair["a"][:12]} / {pair["b"][:12]}</small></article>')
        (output/'variant-review.html').write_text('<!doctype html><meta charset="utf-8"><title>Variant audit</title><style>body{font:16px sans-serif;background:#eee}article{display:inline-block;margin:8px;padding:12px;background:white}img{width:220px;height:300px;object-fit:contain}</style><h1>Cross-partition variant candidates</h1><p>Private diagnostic; visual audit incomplete. Final test remains reserved.</p>'+''.join(cards),encoding='utf-8')
        # Save numeric parameters as JSON, not executable pickle objects.
        parameters={}
        for name, model in models.items():
            if hasattr(model,'coef_'):
                parameters[name]=dict(classes=model.classes_.tolist(),coefficients=model.coef_.tolist(),intercept=model.intercept_.tolist())
            else:
                parameters[name]=dict(constant=str(model.classes_[0]))
        monitor.check(output / 'STOP')
        checkpoint(output/'classifiers.json',dict(featureFingerprint=feat_index['fingerprint'],splitFingerprint=split_identity,models=parameters,automaticAcceptance=False))
        checkpoint(output/'predictions.json',results)
        checkpoint(output/'threshold-exploration.json',dict(thresholds=thresholds,calibrated=False,automaticAcceptance=False))
        summary=dict(status='complete',finalTestEvaluated=False,automaticAcceptance=False,variantAuditComplete=False,
            splitFingerprint=split_identity,counts={name:len(values) for name,values in positions.items()},
            groups=len(set(groups.values())),variantEdges=len(edges),largestGroup=max(Counter(groups.values()).values()),
            metrics=metrics,trainingClassCounts=support,trainingGroupClassCounts=group_support,memory=monitor.stats.copy(),
            caveats=['Development and calibration metrics only; final test untouched.',
                     'Variant screening is heuristic and requires visual audit.',
                     'Rare classes have inadequate evidence; no automatic acceptance.',
                     'Scores and threshold exploration are not calibrated reliability estimates.'])
        checkpoint(output/'summary.json',summary)
        print(summary['counts'])
        print({split:{model:{k:v for k,v in scores.items() if k!='perClass'} for model,scores in values.items()} for split,values in summary['metrics'].items()})


if __name__ == '__main__':
    main()
