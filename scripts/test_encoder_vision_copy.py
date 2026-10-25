"""Compare original and lossless vision-only CPU inference using existing MCP previews."""
import argparse
import subprocess
import sys
import uuid

from encoder_common import REPO, checkpoint, file_hash, private_path, read_json


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--inputs', required=True)
    parser.add_argument('--reference-features', required=True)
    parser.add_argument('--vision-model-directory', default='.cache/models/siglip2-base-patch16-naflex-vision-f32')
    parser.add_argument('--app-pid', type=int)
    args = parser.parse_args()
    inputs_path = private_path(args.inputs)
    reference = private_path(args.reference_features)
    model = private_path(args.vision_model_directory)
    inputs = read_json(inputs_path)
    root = REPO / '.cache/encoder-vision-copy-tests' / uuid.uuid4().hex
    root.mkdir(parents=True)
    # Reuse PNG bytes; inference never receives filename labels or original paths.
    selected = inputs['items'][:8]
    if len(selected) != 8:
        raise ValueError('The comparison requires eight existing MCP previews.')
    # A worker deliberately requires previews within the input manifest directory.
    # Put only the temporary manifest beside the already-exported images, leaving inputs.json intact.
    manifest = inputs_path.parent / ('vision-copy-test-' + root.name + '.json')
    checkpoint(manifest, dict(inputs, items=selected))
    runs = {}
    try:
        for name in ('original', 'vision-only'):
            output = root / name
            command = [sys.executable, str(REPO / 'scripts/extract_encoder_features.py'),
                       '--inputs', str(manifest), '--output', str(output)]
            if name == 'vision-only':
                command += ['--vision-model-directory', str(model)]
            if args.app_pid:
                command += ['--app-pid', str(args.app_pid)]
            result = subprocess.run(command, capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)
            status = read_json(output / 'status.json')
            if result.returncode or status['status'] != 'complete' or status['completedThisRun'] != 8:
                raise AssertionError((status, result.stderr.decode(errors='replace')))
            runs[name] = status
            print(name + ': eight images complete; worker exited.', flush=True)
        for row in selected:
            sha = row['sha256']
            expected = read_json(reference / 'items' / (sha + '.json'))
            for name in runs:
                actual = read_json(root / name / 'items' / (sha + '.json'))
                for key in ('sha256', 'previewSha256', 'features', 'phash', 'dhash'):
                    if actual[key] != expected[key]:
                        raise AssertionError('Inference changed: ' + key)
        receipt = dict(passed=True, images=8, exactVectors=True, workerProcessesExited=True,
                       originalFeatureFingerprint=read_json(reference / 'index.json')['fingerprint'],
                       visionCopyReceiptSha256=file_hash(model / 'vision-copy.json'), runs=runs)
        checkpoint(root / 'result.json', receipt)
        print({name: dict(osPrivateGiB=status['memory']['peakWorkerOsPrivateBytes']/2**30,
                         osWorkingGiB=status['memory']['peakWorkerOsWorkingSetBytes']/2**30,
                         combinedWorkingGiB=status['memory']['peakCombinedWorkingSetBytes']/2**30)
               for name, status in runs.items()})
        print('PASS: eight exact feature matches against the original complete cache; ' + str(root / 'result.json'))
    finally:
        manifest.unlink(missing_ok=True)


if __name__ == '__main__':
    main()
