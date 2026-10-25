"""Offline, label-blind, sequential CPU features from verified MCP PNG previews."""
import argparse
import ctypes
import importlib.metadata
import math
import os
from pathlib import Path
import threading
import time

from encoder_common import REPO, checkpoint, exclusive_lock, file_hash, fingerprint, private_path, read_json


class MemoryStatus(ctypes.Structure):
    _fields_ = [('length', ctypes.c_ulong), ('load', ctypes.c_ulong)] + [
        (name, ctypes.c_ulonglong) for name in ('total', 'available', 'commit_total',
        'commit_available', 'virtual_total', 'virtual_available', 'extended')]


def memory_status():
    value = MemoryStatus()
    value.length = ctypes.sizeof(value)
    if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(value)):
        raise ctypes.WinError()
    return value


class Monitor:
    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *_):
        self.finished.set()
        self.thread.join(timeout=3)

    def __init__(self, app_pid):
        import psutil
        self.psutil = psutil
        self.process = psutil.Process()
        self.harness = []
        # Include our venv redirector and dedicated PowerShell launcher when present.
        ancestor = self.process.parent()
        for _ in range(3):
            if ancestor is None:
                break
            name = ancestor.name().lower()
            if name not in ('python.exe', 'powershell.exe', 'pwsh.exe'):
                break
            self.harness.append((ancestor, ancestor.create_time()))
            if name in ('powershell.exe', 'pwsh.exe'):
                break
            ancestor = ancestor.parent()
        self.app = psutil.Process(app_pid) if app_pid else None
        self.app_created = self.app.create_time() if self.app else None
        self.finished = threading.Event()
        self.reason = None
        self.stats = dict(peakWorkerTreeBytes=0, peakWorkerPrivateBytes=0, peakCombinedWorkingSetBytes=0, peakHarnessBytes=0,
                          peakWorkerOsWorkingSetBytes=0, peakWorkerOsPrivateBytes=0,
                          minimumAvailableBytes=2**64, samples=0, appProcessId=app_pid,
                          appCreated=self.app_created, totalPhysicalBytes=memory_status().total,
                          harnessIdentities=[dict(pid=p.pid, created=c) for p,c in self.harness])
        self.thread = threading.Thread(target=self.run, daemon=True)

    def sample(self):
        total = private = os_working = os_private = 0
        for process in [self.process] + self.process.children(recursive=True):
            try:
                info = process.memory_info()
                total += info.rss
                private += info.private
                os_working += info.peak_wset
                os_private += info.peak_pagefile
            except self.psutil.NoSuchProcess:
                pass
        app = 0
        if self.app:
            try:
                if self.app.create_time() != self.app_created:
                    raise RuntimeError('App process identity changed.')
                app = self.app.memory_info().rss
            except self.psutil.NoSuchProcess:
                raise RuntimeError('App process exited during measurement.')
        memory = memory_status()
        harness = 0
        for process, created in self.harness:
            if process.create_time() != created:
                raise RuntimeError('Harness process identity changed.')
            harness += process.memory_info().rss
        self.stats['peakHarnessBytes'] = max(harness, self.stats['peakHarnessBytes'])
        self.stats['peakWorkerTreeBytes'] = max(total, self.stats['peakWorkerTreeBytes'])
        self.stats['peakWorkerPrivateBytes'] = max(private, self.stats['peakWorkerPrivateBytes'])
        # Sum-of-process high-water marks is an upper bound, not a concurrent tree peak.
        self.stats['peakWorkerOsWorkingSetBytes'] = max(os_working, self.stats['peakWorkerOsWorkingSetBytes'])
        self.stats['peakWorkerOsPrivateBytes'] = max(os_private, self.stats['peakWorkerOsPrivateBytes'])
        self.stats['peakCombinedWorkingSetBytes'] = max(total + harness + app, self.stats['peakCombinedWorkingSetBytes'])
        self.stats['minimumAvailableBytes'] = min(memory.available, self.stats['minimumAvailableBytes'])
        self.stats['samples'] += 1
        if total + harness > 4 * 2**30 or os_private > 4 * 2**30 or memory.available < 4 * 2**30 or memory.commit_available < 4 * 2**30:
            self.reason = 'paused: memory pressure; no current vector committed.'

    def run(self):
        while not self.finished.is_set():
            try:
                self.sample()
            except Exception as error:
                self.reason = str(error)
            self.finished.wait(.2)

    def check(self, stop):
        if stop.exists():
            raise RuntimeError('paused: STOP marker exists.')
        if self.reason:
            raise RuntimeError(self.reason)


def valid_vector(values, dimensions=768):
    return (len(values) == dimensions and all(math.isfinite(x) for x in values)
            and abs(sum(x*x for x in values) - 1) < .0001)


def image_hashes(image):
    """Variant-screening signals on the app preview, never original image decoding."""
    import numpy as np
    from scipy.fft import dctn
    gray = image.convert('L')
    tiny = np.asarray(gray.resize((9, 8)), dtype=np.float32)
    dhash = sum(int(bit) << i for i, bit in enumerate((tiny[:, 1:] > tiny[:, :-1]).ravel()))
    low = dctn(np.asarray(gray.resize((32, 32)), dtype=np.float32), norm='ortho')[:8, :8]
    median = np.median(low.ravel()[1:])
    phash = sum(int(bit) << i for i, bit in enumerate((low > median).ravel()))
    return f'{phash:016x}', f'{dhash:016x}'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--inputs', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--max-items', type=int, default=100000)
    parser.add_argument('--app-pid', type=int)
    args = parser.parse_args()
    inputs_path = private_path(args.inputs)
    output = private_path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    (output / 'items').mkdir(exist_ok=True)
    stop = output / 'STOP'
    with exclusive_lock(output / 'worker.lock'):
        report = dict(status='starting', workerProcessId=os.getpid(), completedThisRun=0, cleanupComplete=False)
        monitor = Monitor(args.app_pid)
        report['workerCreated'] = monitor.process.create_time()
        monitor.process.nice(monitor.psutil.BELOW_NORMAL_PRIORITY_CLASS)
        monitor.thread.start()
        session = output / f'session-{time.time_ns()}.json'
        try:
            monitor.check(stop)
            if memory_status().available < 8 * 2**30:
                raise RuntimeError('paused: Need 8 GiB available before loading encoder.')
            inputs = read_json(inputs_path)
            model_spec = read_json(REPO / 'scripts/encoder-model.json')
            model_dir = REPO / '.cache/models/siglip2-base-patch16-naflex'
            from download_encoder import verify
            for spec in model_spec['files']:
                verify(model_dir / spec['name'], spec)
            settings = dict(version=1, model=model_spec, previewSettings=inputs['settings'],
                            inputFingerprint=inputs['fingerprint'], maxPatches=256,
                            alphaBackground='white', dtype='float32', threads=2, device='cpu',
                            feature='Siglip2VisionModel.pooler_output normalized L2',
                            packages={name: importlib.metadata.version(name) for name in
                                      ('torch', 'transformers', 'safetensors', 'numpy', 'Pillow', 'scipy')})
            identity = fingerprint(settings)
            if (output / 'settings.json').exists() and read_json(output / 'settings.json')['fingerprint'] != identity:
                raise ValueError('Encoder settings changed; use a new output directory.')
            checkpoint(output / 'settings.json', dict(settings=settings, fingerprint=identity))
            # No downloads, tokenizer, label prompt, text tower, or remote model code.
            os.environ.update(HF_HUB_OFFLINE='1', TRANSFORMERS_OFFLINE='1', HF_HUB_DISABLE_TELEMETRY='1',
                              CUDA_VISIBLE_DEVICES='-1', OMP_NUM_THREADS='2', MKL_NUM_THREADS='2')
            report.update(status='running', fingerprint=identity, total=len(inputs['items']))
            checkpoint(output / 'status.json', report)
            import torch
            from PIL import Image
            from transformers import Siglip2ImageProcessor, Siglip2VisionModel
            if torch.version.cuda is not None:
                raise RuntimeError('Expected CPU-only torch build.')
            torch.set_num_threads(2)
            torch.set_num_interop_threads(1)
            model = None
            processor = None
            completed = []
            for record in inputs['items']:
                monitor.check(stop)
                source_hash = record['sha256']
                if len(source_hash) != 64 or any(c not in '0123456789abcdef' for c in source_hash):
                    raise ValueError('Invalid source content hash.')
                path = private_path(inputs_path.parent / record['preview'])
                if not path.is_relative_to(inputs_path.parent) or file_hash(path) != record['previewSha256']:
                    raise ValueError('MCP preview cache changed.')
                target = output / 'items' / f'{source_hash}.json'
                if target.exists():
                    cached = read_json(target)
                    if (cached['fingerprint'] != identity or cached['sha256'] != source_hash
                            or cached['previewSha256'] != record['previewSha256'] or not valid_vector(cached['features'])):
                        raise ValueError('Invalid feature cache identity/vector.')
                    completed.append(source_hash)
                    continue
                if report['completedThisRun'] >= args.max_items:
                    report['status'] = 'checkpointed'
                    break
                if model is None:
                    start = time.monotonic()
                    model, info = Siglip2VisionModel.from_pretrained(model_dir, local_files_only=True,
                        attn_implementation='sdpa', dtype=torch.float32, output_loading_info=True)
                    if info['missing_keys'] or info['mismatched_keys'] or info['error_msgs']:
                        raise RuntimeError(f'Incomplete vision weights: {info}')
                    if any(not (key.startswith('text_model.') or key in ('logit_scale', 'logit_bias')) for key in info['unexpected_keys']):
                        raise RuntimeError('Unexpected non-text checkpoint keys.')
                    model.eval().requires_grad_(False)
                    if any(p.device.type != 'cpu' or p.dtype != torch.float32 for p in model.parameters()):
                        raise RuntimeError('Expected all vision weights on CPU in float32.')
                    processor = Siglip2ImageProcessor.from_pretrained(model_dir, local_files_only=True)
                    report.update(loadSeconds=time.monotonic()-start, visionParameters=sum(p.numel() for p in model.parameters()),
                                  missingWeights=0, discardedNonVisionKeys=len(info['unexpected_keys']))
                    monitor.check(stop)
                start = time.monotonic()
                with Image.open(path) as preview:
                    if preview.format != 'PNG' or max(preview.size) > inputs['settings']['maxDimension']:
                        raise ValueError('Expected bounded MCP PNG preview.')
                    rgba = preview.convert('RGBA')
                    image = Image.new('RGBA', rgba.size, (255, 255, 255, 255))
                    image.alpha_composite(rgba)
                    image = image.convert('RGB')
                    phash, dhash = image_hashes(image)
                    tensors = processor(images=image, return_tensors='pt', max_num_patches=256)
                with torch.inference_mode():
                    vector = model(**tensors).pooler_output[0].float()
                    vector = (vector / vector.norm()).tolist()
                monitor.check(stop)
                if file_hash(path) != record['previewSha256'] or not valid_vector(vector):
                    raise ValueError('Preview changed during inference or invalid feature vector.')
                result = dict(sha256=source_hash, previewSha256=record['previewSha256'], fingerprint=identity,
                              features=vector, phash=phash, dhash=dhash, seconds=time.monotonic()-start)
                checkpoint(target, result)
                completed.append(source_hash)
                report['completedThisRun'] += 1
                report.update(completed=len(completed), memory=monitor.stats.copy())
                checkpoint(output / 'status.json', report)
                if len(completed) % 50 == 0:
                    print(f'Encoded {len(completed)}/{len(inputs["items"])} images.', flush=True)
            if len(completed) == len(inputs['items']):
                report['status'] = 'complete'
            checkpoint(output / 'index.json', dict(fingerprint=identity, hashes=completed))
        except Exception as error:
            report.update(status='paused' if str(error).startswith('paused:') else 'failed', error=str(error))
            raise
        finally:
            monitor.finished.set()
            monitor.thread.join(timeout=3)
            report.update(memory=monitor.stats.copy(), cleanupComplete=True, finishedUtc=time.time())
            checkpoint(output / 'status.json', report)
            checkpoint(session, report)
            print(f'Feature checkpoint: {output / "status.json"}', flush=True)


if __name__ == '__main__':
    main()
