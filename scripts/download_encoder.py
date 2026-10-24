"""Download only the selected encoder; retain partial files and verify pinned identities."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

REPO = Path(__file__).resolve().parents[1]


def verify(path, spec):
    if path.stat().st_size != spec['bytes']:
        raise ValueError(f"Incorrect length: {path.name}")
    digest = hashlib.sha256()
    blob = hashlib.sha1(f"blob {spec['bytes']}\0".encode())
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
            blob.update(block)
    if spec.get('sha256') and digest.hexdigest() != spec['sha256']:
        raise ValueError(f"SHA-256 mismatch: {path.name}")
    if spec.get('gitBlobSha1') and blob.hexdigest() != spec['gitBlobSha1']:
        raise ValueError(f"Git blob mismatch: {path.name}")
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--proxy', required=True)
    args = parser.parse_args()
    manifest = json.loads((REPO / 'scripts/encoder-model.json').read_text())
    target = REPO / '.cache/models/siglip2-base-patch16-naflex'
    state = REPO / '.cache/encoder-setup'
    target.mkdir(parents=True, exist_ok=True)
    state.mkdir(parents=True, exist_ok=True)
    # Windows exclusive open: a second writer cannot acquire this lock.
    import msvcrt
    with (state / 'download.lock').open('a+b') as lock:
        lock.seek(0)
        msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        receipts = []
        for spec in manifest['files']:
            if (state / 'STOP').exists():
                raise RuntimeError('Stop requested; partial files preserved.')
            path = target / spec['name']
            if path.exists():
                receipts.append(dict(name=path.name, sha256=verify(path, spec), reused=True))
                continue
            partial = path.with_name(path.name + '.partial')
            initial = partial.stat().st_size if partial.exists() else 0
            if initial > spec['bytes']:
                raise ValueError('Oversized partial file preserved for inspection.')
            if initial < spec['bytes']:
                url = f"https://huggingface.co/{manifest['repository']}/resolve/{manifest['revision']}/{spec['name']}"
                with (state / (path.name + '.curl.json')).open('w') as out, (state / (path.name + '.curl.error')).open('w') as err:
                    process = subprocess.Popen(['curl.exe', '--disable', '--proxy', args.proxy,
                        '--noproxy', '', '--location', '--max-redirs', '5', '--proto', '=https',
                        '--proto-redir', '=https', '--fail', '--silent', '--show-error',
                        '--connect-timeout', '20', '--max-time', '3600', '--continue-at', '-',
                        '--max-filesize', str(spec['bytes']), '--output', str(partial),
                        '--write-out', '%{json}', url], stdout=out, stderr=err,
                        creationflags=subprocess.CREATE_NO_WINDOW)
                    try:
                        last = 0
                        while process.poll() is None:
                            if (state / 'STOP').exists():
                                raise RuntimeError('Stop requested; partial files preserved.')
                            if time.monotonic() - last > 20:
                                size = partial.stat().st_size if partial.exists() else 0
                                print(f'{path.name}: {size / 2**20:.1f}/{spec["bytes"] / 2**20:.1f} MiB', flush=True)
                                last = time.monotonic()
                            time.sleep(0.5)
                        if process.returncode:
                            raise RuntimeError(f'curl exit {process.returncode}; partial file preserved.')
                    finally:
                        if process.poll() is None:
                            process.terminate()
                            process.wait(timeout=10)
                stats = json.loads((state / (path.name + '.curl.json')).read_text())
                if stats.get('proxy_used') != 1 or stats.get('http_code') not in (200, 206):
                    raise RuntimeError('Unexpected download route/status.')
            digest = verify(partial, spec)
            os.rename(partial, path)
            receipts.append(dict(name=path.name, sha256=digest, bytes=spec['bytes'],
                                 reusedPartialBytes=initial, route='authorized normal proxy'))
            (state / 'download-receipts.json').write_text(json.dumps(receipts, indent=2))
        (state / 'download-receipts.json').write_text(json.dumps(receipts, indent=2))
        print('Selected encoder files verified.', flush=True)


if __name__ == '__main__':
    main()
