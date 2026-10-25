"""End-to-end grouped probe with synthetic feature/label fixtures; no model download."""
import subprocess
import sys
import uuid
import numpy as np
from PIL import Image
from encoder_common import REPO, checkpoint, file_hash, read_json


def main():
    root=REPO/'.cache/encoder-study-tests'/uuid.uuid4().hex
    dataset=root/'dataset';features=root/'features';inputs=root/'inputs';output=root/'study'
    for path in (dataset,features/'items',inputs/'images'):
        path.mkdir(parents=True,exist_ok=True)
    rng=np.random.default_rng(42)
    records=[];hashes=[]
    for i in range(160):
        temporary=inputs/f'{i}.png'
        Image.new('RGB',(4,4),(i,255-i,50)).save(temporary)
        sha=file_hash(temporary)
        temporary.rename(inputs/'images'/f'{sha}.png')
        tags=dict(gender='female' if i%2 else 'male',race='human' if i%2 else 'elf',role='generic' if i%2 else 'caster',weaponOrStyle=None if i%2 else 'wizard')
        records.append(dict(sha256=sha,tags=tags));hashes.append(sha)
        vector=rng.normal(0,.025,768)
        vector[i%2]=1
        vector/=np.linalg.norm(vector)
        checkpoint(features/'items'/f'{sha}.json',dict(sha256=sha,fingerprint='synthetic-v1',features=vector.tolist(),phash=f'{i*100000000000000003 % 2**64:016x}',dhash=f'{i*100000000000000019 % 2**64:016x}'))
    checkpoint(dataset/'index.json',records)
    checkpoint(dataset/'status.json',dict(status='complete'))
    checkpoint(features/'index.json',dict(fingerprint='synthetic-v1',hashes=hashes))
    checkpoint(features/'status.json',dict(status='complete'))
    checkpoint(root/'development.json',dict(items=[dict(sha256=sha) for sha in hashes[:8]]))
    command=[sys.executable,str(REPO/'scripts/train_encoder_probe.py'),'--dataset',str(dataset),'--features',str(features),'--inputs',str(inputs),'--output',str(output),'--development-manifest',str(root/'development.json')]
    result=subprocess.run(command,capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise AssertionError(result.stderr.decode(errors='replace'))
    summary=read_json(output/'summary.json')
    assert summary['status']=='complete' and not summary['finalTestEvaluated'] and not summary['automaticAcceptance']
    assert sum(summary['counts'].values())==160 and summary['counts']['reserved_test']>0
    predictions=read_json(output/'predictions.json')
    assert not any(row['split'] in ('train','reserved_test') for row in predictions)
    assignments=read_json(output/'splits.json')['assignments']
    assert all(row['split']=='development' for row in assignments if row['sha256'] in hashes[:8])
    assert summary['metrics']['development']['linear']['fullMatches']==summary['counts']['development']
    assert read_json(output/'status.json')['cleanupComplete']
    # A label correction must not silently overwrite an experiment with identical image bytes.
    records[0]['tags']['race']='human' if records[0]['tags']['race']=='elf' else 'elf'
    checkpoint(dataset/'index.json',records)
    changed=subprocess.run(command,capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
    assert changed.returncode!=0 and 'Training labels' in read_json(output/'status.json')['error']
    assert read_json(output/'summary.json')['studyFingerprint']==summary['studyFingerprint']
    print('PASS: whole grouped study, known-development isolation, reserved-test untouched, complete metrics; '+str(root))


if __name__=='__main__':
    main()
