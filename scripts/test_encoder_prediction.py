"""Model-free checks plus reproduction of already-scored private development predictions."""
import copy
import subprocess
import sys
import unittest
import uuid

from encoder_common import REPO, checkpoint, read_json
from predict_encoder_probe import feature_space, numeric_predict


class PredictionTests(unittest.TestCase):
    def test_parameter_roundtrip(self):
        import numpy as np
        from sklearn.linear_model import LogisticRegression
        rng = np.random.default_rng(42)
        x = rng.normal(size=(24, 768)).astype(np.float32)
        x /= np.linalg.norm(x, axis=1, keepdims=True)
        models = {}
        fitted = {}
        for name, labels in {'gender': ['female', 'male'], 'race': ['elf', 'human', 'dwarf'],
                             'roleStyle': ['caster|bard', 'range|bow', 'generic|']}.items():
            model = LogisticRegression(C=10).fit(x, [labels[i % len(labels)] for i in range(len(x))])
            fitted[name] = model
            models[name] = dict(classes=model.classes_.tolist(), coefficients=model.coef_.tolist(), intercept=model.intercept_.tolist())
        tags, scores = numeric_predict(models, x)
        for i, row in enumerate(tags):
            self.assertEqual(row['gender'], fitted['gender'].predict(x[i:i+1])[0])
            self.assertEqual(row['race'], fitted['race'].predict(x[i:i+1])[0])
            self.assertEqual(row['role']+'|'+(row['weaponOrStyle'] or ''), fitted['roleStyle'].predict(x[i:i+1])[0])
            for name in models:
                self.assertAlmostEqual(scores[i][name], fitted[name].predict_proba(x[i:i+1]).max(), places=12)
        constant = {name: {'constant': label} for name, label in
                    {'gender': 'female', 'race': 'elf', 'roleStyle': 'generic|'}.items()}
        self.assertIsNone(numeric_predict(constant, x)[0][0]['weaponOrStyle'])
        models['gender']['coefficients'][0][0] = float('nan')
        with self.assertRaisesRegex(ValueError, 'Invalid numeric'):
            numeric_predict(models, x)

    def test_feature_space(self):
        base = dict(version=1, model={'files': [{'name': 'model.safetensors', 'sha256': 'a'*64}]},
                    previewSettings=dict(version=1, source='MCP view_image PNG', maxDimension=768, datasetFingerprint='dataset-a'),
                    inputFingerprint='inputs-a', maxPatches=256, alphaBackground='white', dtype='float32',
                    threads=2, device='cpu', feature='normalized', packages={'torch': 'version'})
        new = copy.deepcopy(base)
        new['inputFingerprint'] = 'inputs-b'
        new['previewSettings']['datasetFingerprint'] = 'dataset-b'
        new['visionOnlyCopy'] = {'sourceSha256': 'a'*64, 'checkpoint': 'lossless-copy'}
        self.assertEqual(feature_space(base), feature_space(new))
        for key, value in [('maxPatches', 128), ('dtype', 'float16'), ('alphaBackground', 'black')]:
            changed = copy.deepcopy(new)
            changed[key] = value
            self.assertNotEqual(feature_space(base), feature_space(changed))
        new['previewSettings']['maxDimension'] = 512
        self.assertNotEqual(feature_space(base), feature_space(new))
        new['visionOnlyCopy']['sourceSha256'] = 'b'*64
        with self.assertRaisesRegex(ValueError, 'different source'):
            feature_space(new)
        with self.assertRaisesRegex(ValueError, 'Unknown encoder'):
            feature_space(base | {'unrecognizedPreprocessing': True})


def verify_saved_study():
    study = REPO/'.cache/encoder-study/probe-v3'
    features = REPO/'.cache/encoder-features/filename-v1'
    if not (study/'classifiers.json').exists():
        print('Private reproduction skipped: cached probe-v3 is unavailable.')
        return
    from threadpoolctl import threadpool_limits
    expected = read_json(study/'predictions.json')
    if any(row['split'] not in ('calibration', 'development') for row in expected):
        raise AssertionError('Reproduction must use already-scored data only.')
    vectors = [read_json(features/'items'/f'{row["sha256"]}.json')['features'] for row in expected]
    with threadpool_limits(limits=2):
        predictions, scores = numeric_predict(read_json(study/'classifiers.json')['models'], vectors)
    max_error = max(abs(score[name]-row['scores'][name]) for score, row in zip(scores, expected) for name in score)
    assert predictions == [row['prediction'] for row in expected] and max_error < 1e-12
    root = REPO/'.cache/encoder-prediction-tests'/uuid.uuid4().hex
    root.mkdir(parents=True)
    # An index including any reserved image must fail before writing suggestions.
    blocked = subprocess.run([sys.executable, str(REPO/'scripts/predict_encoder_probe.py'),
        '--study', str(study), '--training-features', str(features), '--features', str(features),
        '--output', str(root/'reserved-rejection')], capture_output=True, text=True)
    assert blocked.returncode != 0 and 'Reserved final-test images' in blocked.stderr
    assert not (root/'reserved-rejection/suggestions.json').exists()
    # When the unlabeled pilot exists, exercise its portable suggestion pipeline and review guards.
    pilot = REPO/'.cache/encoder-features/backlog-pilot-v1'
    review_checks = []
    if (pilot/'index.json').exists():
        suggestions = root/'pilot-suggestions'
        command = [sys.executable, str(REPO/'scripts/predict_encoder_probe.py'), '--study', str(study),
                   '--training-features', str(features), '--features', str(pilot), '--output', str(suggestions)]
        for _ in range(2):
            completed = subprocess.run(command, capture_output=True, text=True)
            assert completed.returncode == 0, completed.stderr
        original = read_json(suggestions/'suggestions.json')
        assert len(original['items']) == 4 and all(row['reviewRequired'] and not row['automaticAcceptance'] for row in original['items'])
        exporter = [sys.executable, str(REPO/'scripts/export_encoder_suggestions.py'), '--suggestions', str(suggestions),
                    '--features', str(pilot), '--inputs', str(REPO/'.cache/encoder-inputs/backlog-pilot-v1')]
        completed = subprocess.run(exporter, capture_output=True, text=True)
        assert completed.returncode == 0, completed.stderr
        review_checks.append('four verified previews')
        for case in ('duplicate', 'automatic', 'wrong-targets'):
            changed = copy.deepcopy(original)
            if case == 'duplicate':
                changed['items'].append(changed['items'][0])
            elif case == 'automatic':
                changed['items'][0]['automaticAcceptance'] = True
            else:
                changed['items'][0]['sha256'] = '0'*64
            checkpoint(suggestions/'suggestions.json', changed)
            completed = subprocess.run(exporter, capture_output=True, text=True)
            assert completed.returncode != 0, 'Invalid review accepted: '+case
            review_checks.append(case+' rejected')
        checkpoint(suggestions/'suggestions.json', original)
    checkpoint(root/'result.json', dict(passed=True, reproduced=len(expected), maximumScoreDifference=max_error,
                                      reservedTestRejected=True, reviewChecks=review_checks, inference=False))
    print(f'PASS: {len(expected)} saved predictions reproduced; reserved test rejected. Evidence: {root}')


if __name__ == '__main__':
    program = unittest.main(exit=False)
    if not program.result.wasSuccessful():
        sys.exit(1)
    verify_saved_study()
