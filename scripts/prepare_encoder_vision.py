"""Build and verify a private, lossless vision-only copy of the selected cached checkpoint."""
import argparse
import os
import shutil

from encoder_common import REPO, checkpoint, exclusive_lock, file_hash, private_path, read_json, study_status


def verify_copy(directory, specification):
    directory = private_path(directory)
    receipt = read_json(directory / 'vision-copy.json')
    source = next(row for row in specification['files'] if row['name'] == 'model.safetensors')
    if (receipt.get('version') != 1 or receipt.get('sourceSha256') != source['sha256']
            or receipt.get('tensorCount') != 208 or receipt.get('parameters') != 92930304
            or not receipt.get('allTensorsExactlyEqual')):
        raise ValueError('Vision-only checkpoint provenance is invalid.')
    expected_names = {row['name'] for row in specification['files']}
    if set(receipt['files']) != expected_names:
        raise ValueError('Vision-only checkpoint file set differs.')
    for name, expected in receipt['files'].items():
        path = directory / name
        if path.stat().st_size != expected['bytes'] or file_hash(path) != expected['sha256']:
            raise ValueError('Vision-only checkpoint changed: ' + name)
    return receipt


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', default='.cache/models/siglip2-base-patch16-naflex-vision-f32')
    parser.add_argument('--app-pid', type=int)
    args = parser.parse_args()
    source = REPO / '.cache/models/siglip2-base-patch16-naflex'
    output = private_path(args.output)
    if output == source or output in source.parents or source in output.parents:
        raise ValueError('Derived checkpoint must be separate from the original cache.')
    output.mkdir(parents=True, exist_ok=True)
    from extract_encoder_features import Monitor, memory_status
    with exclusive_lock(output / 'worker.lock'), study_status(output) as status, Monitor(args.app_pid) as monitor:
        monitor.process.nice(monitor.psutil.BELOW_NORMAL_PRIORITY_CLASS)
        monitor.check(output / 'STOP')
        if memory_status().available < 8 * 2**30:
            raise RuntimeError('paused: Need 8 GiB available for checkpoint preparation.')
        specification = read_json(REPO / 'scripts/encoder-model.json')
        from download_encoder import verify
        for spec in specification['files']:
            verify(source / spec['name'], spec)
        if (output / 'vision-copy.json').exists():
            verify_copy(output, specification)
            status.update(reused=True, memory=monitor.stats.copy())
            print('Verified existing lossless vision checkpoint.')
            return
        os.environ.update(CUDA_VISIBLE_DEVICES='-1', OMP_NUM_THREADS='2', MKL_NUM_THREADS='2')
        import torch
        from safetensors import safe_open
        from safetensors.torch import save_file
        if torch.version.cuda is not None:
            raise RuntimeError('Expected CPU-only torch build.')
        torch.set_num_threads(2)
        torch.set_num_interop_threads(1)
        temporary = output / 'model.safetensors.tmp'
        with safe_open(source / 'model.safetensors', framework='pt', device='cpu') as original:
            keys = sorted(key for key in original.keys() if key.startswith('vision_model.'))
            if len(keys) != 208:
                raise ValueError('Unexpected pinned vision key set.')
            tensors = {}
            for key in keys:
                monitor.check(output / 'STOP')
                tensors[key] = original.get_tensor(key)
                if tensors[key].dtype != torch.float32:
                    raise ValueError('Expected float32 source; no conversion is allowed.')
            parameters = sum(t.numel() for t in tensors.values())
            if parameters != 92930304:
                raise ValueError('Unexpected vision parameter count.')
            save_file(tensors, temporary, metadata={'format': 'pt'})
            monitor.sample()
            monitor.check(output / 'STOP')
            with safe_open(temporary, framework='pt', device='cpu') as derived:
                if set(derived.keys()) != set(keys):
                    raise ValueError('Derived tensor keys differ.')
                for key in keys:
                    monitor.check(output / 'STOP')
                    actual = derived.get_tensor(key)
                    expected = tensors[key]
                    if actual.dtype != expected.dtype or actual.shape != expected.shape or not torch.equal(actual, expected):
                        raise ValueError('Derived tensor differs: ' + key)
                    del actual
            del tensors, expected
        os.replace(temporary, output / 'model.safetensors')
        for spec in specification['files']:
            if spec['name'] != 'model.safetensors':
                shutil.copyfile(source / spec['name'], output / spec['name'])
                verify(output / spec['name'], spec)
        files = {spec['name']: dict(bytes=(output / spec['name']).stat().st_size,
                                   sha256=file_hash(output / spec['name'])) for spec in specification['files']}
        monitor.sample()
        monitor.check(output / 'STOP')
        receipt = dict(version=1, sourceSha256=specification['files'][0]['sha256'],
                       tensorCount=len(keys), parameters=parameters, allTensorsExactlyEqual=True,
                       conversion='none; original float32 vision tensors only', files=files,
                       implementationSha256=file_hash(__file__), memory=monitor.stats.copy())
        checkpoint(output / 'vision-copy.json', receipt)
        verify_copy(output, specification)
        status.update(reused=False, memory=monitor.stats.copy())
        print(f'Verified {len(keys)} identical vision tensors; {files["model.safetensors"]["bytes"]} bytes.')


if __name__ == '__main__':
    main()
