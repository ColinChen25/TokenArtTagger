"""Opt-in real CPU encoder recovery checks using only disposable synthetic PNGs."""
import argparse
from pathlib import Path
import subprocess
import sys
import uuid

from PIL import Image
from encoder_common import REPO, checkpoint, file_hash, fingerprint, read_json


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--app-pid',type=int)
    args=parser.parse_args()
    root=REPO/'.cache/encoder-runtime-tests'/uuid.uuid4().hex
    inputs=root/'inputs'
    output=root/'features'
    inputs.mkdir(parents=True)
    settings=dict(version=1,source='disposable synthetic MCP-shaped PNG fixture',maxDimension=768)
    records=[]
    for name,color in [('red',(255,0,0)),('blue',(0,0,255))]:
        path=inputs/(name+'.png')
        Image.new('RGB',(96,160),color).save(path)
        sha=file_hash(path)
        records.append(dict(sha256=sha,preview=path.name,previewSha256=sha))
    checkpoint(inputs/'inputs.json',dict(settings=settings,fingerprint=fingerprint(settings),items=records))
    command=[sys.executable,str(REPO/'scripts/extract_encoder_features.py'),'--inputs',str(inputs/'inputs.json'),'--output',str(output)]
    if args.app_pid:
        command+=['--app-pid',str(args.app_pid)]

    def run(extra=(), expected='complete'):
        result=subprocess.run(command+list(extra),capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
        status=read_json(output/'status.json')
        if status['status']!=expected or ((result.returncode==0)!=(expected in ('complete','checkpointed'))):
            raise AssertionError((status,result.stderr.decode(errors='replace')))
        print(expected, status['completedThisRun'],flush=True)
        return status

    first=run(['--max-items','1'],'checkpointed')
    assert first['completedThisRun']==1
    first_path=output/'items'/f'{records[0]["sha256"]}.json'
    first_digest=file_hash(first_path)
    (output/'STOP').write_text('Owned recovery test marker')
    stopped=run(expected='paused')
    assert stopped['completedThisRun']==0 and file_hash(first_path)==first_digest
    (output/'STOP').unlink()
    resumed=run()
    assert resumed['completedThisRun']==1 and file_hash(first_path)==first_digest
    cached=run()
    assert cached['completedThisRun']==0 and 'loadSeconds' not in cached
    saved=read_json(first_path)
    corrupted=dict(saved,features=[0.]*768)
    checkpoint(first_path,corrupted)
    invalid=run(expected='failed')
    assert 'cache' in invalid['error'] and 'loadSeconds' not in invalid
    checkpoint(first_path,saved)
    receipt=dict(passed=True,checks=['checkpoint limit','STOP preserves completed result','resume only missing image',
                 'completed resume does not load weights','invalid cached vector rejected'],
                 first=first,resumed=resumed,cached=cached,workerProcessesExited=True)
    checkpoint(root/'result.json',receipt)
    print('PASS: real encoder recovery checks; '+str(root/'result.json'))


if __name__=='__main__':
    main()
