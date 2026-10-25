"""Apply saved numeric classifiers to compatible CPU features; review-only, no retraining."""
import argparse
import re

from encoder_common import checkpoint, exclusive_lock, file_hash, fingerprint, private_path, read_json, study_status


def feature_space(settings):
    """Separate dataset/cache provenance from the mathematical feature representation."""
    expected = {'version', 'model', 'previewSettings', 'inputFingerprint', 'maxPatches',
                'alphaBackground', 'dtype', 'threads', 'device', 'feature', 'packages'}
    if set(settings) - expected - {'visionOnlyCopy'} or not expected <= set(settings):
        raise ValueError('Unknown encoder settings contract.')
    preview = settings['previewSettings']
    if set(preview) - {'version', 'source', 'maxDimension', 'datasetFingerprint'}:
        raise ValueError('Unknown preview settings contract.')
    model = settings['model']
    source = [entry for entry in model['files'] if entry['name'] == 'model.safetensors']
    if len(source) != 1:
        raise ValueError('Ambiguous original model checkpoint.')
    if 'visionOnlyCopy' in settings and settings['visionOnlyCopy']['sourceSha256'] != source[0]['sha256']:
        raise ValueError('Vision copy has a different source checkpoint.')
    # The feature worker verifies the lossless copy; model packaging is not a feature change.
    return {key: value for key, value in settings.items()
            if key not in ('inputFingerprint', 'previewSettings', 'visionOnlyCopy')} | {
                'previewSettings': {key: preview[key] for key in ('version', 'source', 'maxDimension')}}


def numeric_predict(models, vectors):
    import numpy as np
    from scipy.special import expit, softmax
    from train_encoder_probe import decode
    if set(models) != {'gender', 'roleStyle', 'race'}:
        raise ValueError('Expected exactly three saved classifiers.')
    vectors = np.asarray(vectors, dtype=np.float64)
    if vectors.ndim != 2 or vectors.shape[1] != 768 or not np.isfinite(vectors).all():
        raise ValueError('Invalid classifier feature matrix.')
    choices, scores = [], []
    probabilities = {}
    classes_by_name = {}
    for name, model in models.items():
        if set(model) == {'constant'} and isinstance(model['constant'], str):
            classes = [model['constant']]
            probs = np.ones((len(vectors), 1))
        else:
            if set(model) != {'classes', 'coefficients', 'intercept'}:
                raise ValueError('Unexpected numeric classifier parameters.')
            classes = model['classes']
            coefficients = np.asarray(model['coefficients'], dtype=np.float64)
            intercept = np.asarray(model['intercept'], dtype=np.float64)
            rows = 1 if len(classes) == 2 else len(classes)
            if (len(classes) < 2 or len(classes) != len(set(classes))
                    or any(not isinstance(label, str) for label in classes)
                    or coefficients.shape != (rows, 768) or intercept.shape != (rows,)
                    or not np.isfinite(coefficients).all() or not np.isfinite(intercept).all()):
                raise ValueError('Invalid numeric classifier dimensions/values.')
            logits = vectors @ coefficients.T + intercept
            if len(classes) == 2:
                positive = expit(logits[:, 0])
                probs = np.column_stack((1-positive, positive))
            else:
                probs = softmax(logits, axis=1)
        probabilities[name], classes_by_name[name] = probs, classes
    for i in range(len(vectors)):
        choices.append(decode({name: classes_by_name[name][int(probs[i].argmax())]
                               for name, probs in probabilities.items()}))
        scores.append({name: float(probs[i].max()) for name, probs in probabilities.items()})
    return choices, scores


def read_features(directory):
    from extract_encoder_features import valid_vector
    state = read_json(directory/'status.json')
    saved = read_json(directory/'settings.json')
    index = read_json(directory/'index.json')
    if (state['status'] != 'complete' or not state['cleanupComplete']
            or saved['fingerprint'] != fingerprint(saved['settings'])
            or index['fingerprint'] != saved['fingerprint'] or state['fingerprint'] != saved['fingerprint']):
        raise ValueError('Feature cache is incomplete or has inconsistent provenance.')
    hashes = index['hashes']
    if not hashes or len(hashes) != len(set(hashes)) or any(not re.fullmatch('[a-f0-9]{64}', h) for h in hashes):
        raise ValueError('Feature index has invalid or duplicate hashes.')
    return saved, hashes, valid_vector


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--study', required=True)
    parser.add_argument('--training-features', required=True)
    parser.add_argument('--features', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    study, training, features, output = map(private_path,
        (args.study, args.training_features, args.features, args.output))
    output.mkdir(parents=True, exist_ok=True)
    with exclusive_lock(output/'worker.lock'), study_status(output) as state:
        if (output/'STOP').exists():
            raise RuntimeError('paused: STOP marker exists.')
        status = read_json(study/'status.json')
        saved = read_json(study/'classifiers.json')
        settings = read_json(study/'study-settings.json')
        splits = read_json(study/'splits.json')
        summary = read_json(study/'summary.json')
        source, training_hashes, _ = read_features(training)
        target, hashes, valid_vector = read_features(features)
        if (status['status'] != 'complete' or not status['cleanupComplete']
                or saved['studyFingerprint'] != settings['fingerprint']
                or settings['fingerprint'] != fingerprint(settings['settings'])
                or saved['splitFingerprint'] != splits['fingerprint']
                or saved['featureFingerprint'] != source['fingerprint']
                or summary['studyFingerprint'] != saved['studyFingerprint']):
            raise ValueError('Classifier study provenance is inconsistent.')
        space = feature_space(source['settings'])
        if feature_space(target['settings']) != space:
            raise ValueError('New features do not match the trained feature space.')
        assignments = splits['assignments']
        if (len(assignments) != len(training_hashes) or {row['sha256'] for row in assignments} != set(training_hashes)
                or any(row['split'] not in ('train', 'calibration', 'development', 'reserved_test') for row in assignments)
                or fingerprint(dict(features=source['fingerprint'], assignments=assignments, version=1)) != splits['fingerprint']):
            raise ValueError('Saved partition is inconsistent with training features.')
        reserved = {row['sha256'] for row in assignments if row['split'] == 'reserved_test'}
        if reserved.intersection(hashes):
            raise ValueError('Reserved final-test images cannot be predicted by this review workflow.')
        rows = []
        for sha in hashes:
            row = read_json(features/'items'/f'{sha}.json')
            if row['sha256'] != sha or row['fingerprint'] != target['fingerprint'] or not valid_vector(row['features']):
                raise ValueError('Invalid target feature record.')
            rows.append(row)
        identity = dict(version=1, study=saved['studyFingerprint'],
                        classifierSha256=file_hash(study/'classifiers.json'),
                        featureSpaceFingerprint=fingerprint(space), targetFeatureFingerprint=target['fingerprint'],
                        targetVectorsFingerprint=fingerprint(rows), implementationSha256=file_hash(__file__))
        run_fingerprint = fingerprint(identity)
        if (output/'settings.json').exists() and read_json(output/'settings.json')['fingerprint'] != run_fingerprint:
            raise ValueError('Suggestion inputs changed; use a new output directory.')
        checkpoint(output/'settings.json', dict(fingerprint=run_fingerprint, settings=identity))
        from threadpoolctl import threadpool_limits
        with threadpool_limits(limits=2):
            predictions, scores = numeric_predict(saved['models'], [row['features'] for row in rows])
        result = [dict(sha256=sha, prediction=tags, scores=score, scoresCalibrated=False,
                       reviewRequired=True, automaticAcceptance=False,
                       trainingSupport={name: summary['trainingClassCounts'][name].get(label, 0)
                           for name, label in {'gender': tags['gender'], 'race': tags['race'],
                                               'roleStyle': tags['role']+'|'+(tags['weaponOrStyle'] or '')}.items()})
                  for sha, tags, score in zip(hashes, predictions, scores)]
        if (output/'STOP').exists():
            raise RuntimeError('paused: Stop requested before saving suggestions.')
        checkpoint(output/'suggestions.json', dict(fingerprint=run_fingerprint, items=result,
            automaticAcceptance=False, scoresCalibrated=False, finalTestEvaluated=False,
            caveats=['Closed-set classifiers cannot identify classes absent from training.',
                     'All suggestions require review; scores and model agreement are not calibrated confidence.']))
        state.update(total=len(result), fingerprint=run_fingerprint, finalTestEvaluated=False,
                     automaticAcceptance=False)
        print(f'Review-only suggestions: {output / "suggestions.json"}')


if __name__ == '__main__':
    main()
