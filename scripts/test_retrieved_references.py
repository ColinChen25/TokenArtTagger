"""Driver preflight failures must not start inference or require a real image library."""
import subprocess
import uuid

from encoder_common import REPO, checkpoint, read_json


def check_decode_failure_report(root):
    from PIL import Image
    study=root/'report';inputs=root/'inputs';baseline=root/'baseline';fixed=root/'fixed'
    inputs.joinpath('images').mkdir(parents=True)
    target='a'*64;reference='b'*64
    for sha in (target,reference):
        Image.new('RGB',(2,2),'red').save(inputs/'images'/f'{sha}.png')
    tags=dict(gender='female',role='generic',weaponOrStyle=None,race='human')
    def run(directory,failed=False,refs=()):
        (directory/'items').mkdir(parents=True)
        entry=dict(sha256=target,path='target.png',expected=tags,hasGroundTruth=True)
        row=dict(entry,fingerprint='fixture',outcome='decode_error' if failed else 'complete',
                 prediction=None if failed else dict(tags=tags,evidence='<script>escaped</script>',uncertainty=''),elapsedSeconds=None)
        checkpoint(directory/'manifest.json',dict(fingerprint='fixture',root='fixture',items=[entry],references=list(refs)))
        checkpoint(directory/'status.json',dict(status='complete',cleanupComplete=True))
        checkpoint(directory/'items/a.json',row)
    run(baseline);run(fixed)
    run(study/'runs/a',True,[dict(sha256=reference)])
    checkpoint(study/'driver-status.json',dict(status='complete',cleanupComplete=True,fingerprint='fixture'))
    checkpoint(study/'plan.json',dict(fingerprint='fixture',items=[dict(sha256=target,referenceSha256=reference,childDirectory='runs/a')]))
    command=[str(REPO/'.tools/encoder-venv/Scripts/python.exe'),str(REPO/'scripts/export_retrieved_reference_review.py'),
             '--study',str(study),'--inputs',str(inputs),'--baseline',str(baseline),'--fixed-reference',str(fixed)]
    result=subprocess.run(command,capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
    assert result.returncode==0,result.stderr.decode(errors='replace')
    summary=read_json(study/'summary.json')
    assert summary['againstBaseline']['paired']==1 and summary['againstBaseline']['candidate']['fullMatches']==0
    assert summary['outcomes']==dict(decode_error=1) and summary['medianSeconds'] is None and summary['maxPromptTokens'] is None
    assert not summary['automaticAcceptance'] and not summary['finalTestEvaluated']


def main():
    root=REPO/'.cache/retrieved-reference-tests'/uuid.uuid4().hex
    for scenario,expected in [('stop','paused:'),('unaudited','current visual audit'),('related','visually reviewed'),
                              ('escape','escapes output'),('changed-child','differs from the visually audited'),
                              ('changed-hash','source snapshot differs')]:
        output=root/scenario
        (output/'plans').mkdir(parents=True)
        item=dict(sha256='a'*64,referenceSha256='b'*64,targetPath='target.png',referencePath='reference.png',childDirectory='runs/a',planFile='plans/a.json')
        audit=dict(planFingerprint='fixture',items=[dict(sha256='a'*64,referenceSha256='b'*64,related=False)])
        selection=dict(targets=['target.png'],references=['reference.png'],sources=[dict(path='target.png',sha256='a'*64),dict(path='reference.png',sha256='b'*64)])
        if scenario=='stop': (output/'STOP').write_text('Owned fixture marker')
        if scenario=='unaudited': audit['planFingerprint']='stale'
        if scenario=='related': audit['items'][0]['related']=True
        if scenario=='escape': item['childDirectory']='../outside'
        if scenario=='changed-child': selection['references']=['different.png']
        if scenario=='changed-hash': selection['sources'][0]['sha256']='c'*64
        checkpoint(output/'plan.json',dict(fingerprint='fixture',items=[item]))
        checkpoint(output/'visual-audit.json',audit)
        checkpoint(output/'plans/a.json',selection)
        command=['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(REPO/'scripts/Invoke-LocalAiRetrievedReferences.ps1'),'-Instance','0','-OutputDirectory',str(output)]
        result=subprocess.run(command,capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
        state=read_json(output/'driver-status.json')
        assert result.returncode and expected in state['error'],(scenario,state,result.stderr.decode(errors='replace'))
        assert state['cleanupComplete'] and not (output/'runs').exists()
    check_decode_failure_report(root)
    checkpoint(root/'result.json',dict(passed=True,checks=7,noInference=True))
    print('PASS: six driver preflight failures and paired decode-failure report, no inference; '+str(root))


if __name__=='__main__':
    main()
