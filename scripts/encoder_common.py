"""Private-artifact boundaries and checkpoint helpers shared by encoder experiments."""
import contextlib
import hashlib
import json
import os
from pathlib import Path
import time

REPO = Path(__file__).resolve().parents[1]


def private_path(value):
    path = Path(value).resolve()
    if not path.is_relative_to(REPO / '.cache'):
        raise ValueError('Private artifacts must remain inside repository .cache.')
    return path


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def fingerprint(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def file_hash(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def checkpoint(path, value):
    path = Path(path)
    temp = path.with_name(path.name + '.tmp')
    temp.write_text(json.dumps(value, indent=2, allow_nan=False), encoding='utf-8')
    for attempt in range(40):
        try:
            os.replace(temp, path)
            return
        except PermissionError:
            if attempt == 39:
                raise
            time.sleep(.05)


@contextlib.contextmanager
def exclusive_lock(path):
    import msvcrt
    with Path(path).open('a+b') as stream:
        stream.seek(0)
        msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
        try:
            yield
        finally:
            stream.seek(0)
            msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)


@contextlib.contextmanager
def study_status(output):
    import psutil
    state = dict(status='running', workerProcessId=os.getpid(), workerCreated=psutil.Process().create_time(), cleanupComplete=False)
    checkpoint(output / 'status.json', state)
    try:
        yield state
        state['status'] = 'complete'
    except Exception as error:
        state.update(status='paused' if str(error).startswith('paused:') else 'failed', error=str(error))
        raise
    finally:
        state.update(cleanupComplete=True, finishedUtc=time.time())
        checkpoint(output / 'status.json', state)
