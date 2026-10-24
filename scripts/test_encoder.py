import ctypes
import math
import subprocess
import threading
import unittest
import uuid
import numpy as np
from encoder_common import REPO, checkpoint, fingerprint, private_path, read_json
from extract_encoder_features import valid_vector
from train_encoder_probe import assign_splits, decode, fit_probe, labels, make_groups, measure, predict, wilson_lower
from export_encoder_review import paired_comparison


class EncoderChecks(unittest.TestCase):
    def test_private_boundaries(self):
        self.assertTrue(private_path(REPO/'.cache/test').is_relative_to(REPO/'.cache'))
        with self.assertRaises(ValueError):
            private_path(REPO/'.cache/../outside')

    def test_vector_checks(self):
        self.assertTrue(valid_vector([1.] + [0.]*767))
        for values in ([0.]*768, [1.]*768, [math.nan]+[0.]*767, [1.]+[0.]*766):
            self.assertFalse(valid_vector(values))

    def test_identity_settings(self):
        self.assertEqual(fingerprint({'a':1,'b':None}),fingerprint({'b':None,'a':1}))
        self.assertNotEqual(fingerprint({'a':1}),fingerprint({'a':1,'b':None}))

    def test_related_variants_never_cross_split(self):
        rows=[dict(sha256=str(i)*64,phash='f'*16,dhash='f'*16) for i in range(4)]
        vectors=np.array([[1,0,0],[.99,.1,0],[.98,.2,0],[0,0,1]],dtype=np.float32)
        vectors/=np.linalg.norm(vectors,axis=1,keepdims=True)
        groups,edges,_=make_groups(rows,vectors)
        self.assertEqual(len(set(groups.values())),2)
        split=assign_splits(groups,{rows[1]['sha256']})
        self.assertEqual({split[row['sha256']] for row in rows[:3]},{'development'})
        self.assertNotEqual(groups[rows[3]['sha256']],groups[rows[0]['sha256']])
        self.assertEqual(split,assign_splits(groups,{rows[1]['sha256']}))

    def test_role_style_and_null_roundtrip(self):
        for role,style in [('generic',None),('caster','wizard'),('melee','blade')]:
            tags=dict(gender='female',role=role,weaponOrStyle=style,race='human')
            self.assertEqual(decode(labels(tags)),tags)

    def test_linear_probe_and_baseline_denominators(self):
        truth=[dict(gender='male',race='human',role='generic',weaponOrStyle=None),dict(gender='female',race='elf',role='caster',weaponOrStyle='wizard')]*8
        x=np.array([[1.,0.],[0.,1.]]*8)
        predictions,scores=predict(fit_probe(x,truth),x)
        self.assertEqual(measure(truth,predictions)['fullMatches'],16)
        self.assertEqual(measure(truth,[truth[0]]*16)['fullMatches'],8)
        self.assertEqual(measure(truth,predictions)['fields']['weaponOrStyle']['total'],16)
        self.assertTrue(all(0<=s['roleStyle']<=1 for s in scores))

    def test_single_class_probe(self):
        truth=[dict(gender='male',race='human',role='generic',weaponOrStyle=None)]*3
        x=np.eye(3)
        predictions,_=predict(fit_probe(x,truth),x)
        self.assertEqual(predictions,truth)

    def test_paired_invalid_result_and_null_style(self):
        tags=dict(gender='male',race='human',role='generic',weaponOrStyle=None)
        baseline=[dict(sha256='a'*64,expected=tags,outcome='invalid_output',prediction=None,hasGroundTruth=True)]
        candidate=[dict(sha256='a'*64,expected=tags,prediction=tags)]
        result=paired_comparison(baseline,candidate)
        self.assertEqual(result['paired'],1)
        self.assertEqual(result['baseline']['fields']['weaponOrStyle']['correct'],0)
        self.assertEqual(result['candidate']['fullMatches'],1)
        self.assertEqual(result['improved'],1)
        with self.assertRaises(ValueError):
            paired_comparison(baseline,candidate+candidate)
        altered=dict(candidate[0],expected=dict(tags,race='elf'))
        with self.assertRaises(ValueError):
            paired_comparison(baseline,[altered])

    def test_small_sample_not_confident(self):
        self.assertLess(wilson_lower(6,6),.95)
        self.assertLess(wilson_lower(20,20),.95)
        self.assertGreater(wilson_lower(100,100),.95)

    def test_checkpoint_reader_lock_retries(self):
        root=REPO/'.cache/encoder-tests'/uuid.uuid4().hex
        root.mkdir(parents=True)
        target=root/'checkpoint.json'
        checkpoint(target,{'version':1})
        create=ctypes.windll.kernel32.CreateFileW
        create.restype=ctypes.c_void_p
        create.argtypes=[ctypes.c_wchar_p,ctypes.c_ulong,ctypes.c_ulong,ctypes.c_void_p,ctypes.c_ulong,ctypes.c_ulong,ctypes.c_void_p]
        handle=create(str(target),0x80000000,1,None,3,0,None)
        self.assertNotEqual(handle,ctypes.c_void_p(-1).value)
        close=ctypes.windll.kernel32.CloseHandle
        close.argtypes=[ctypes.c_void_p]
        timer=threading.Timer(1.2,lambda:close(handle))
        timer.start()
        script=root/'write.ps1'
        script.write_text("Import-Module '"+str(REPO/'scripts/LocalAiRuntime.psm1')+"' -Force\nWrite-AiCheckpoint @{version=2} '"+str(target)+"'\n")
        result=subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(script)],capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
        timer.join()
        self.assertEqual(result.returncode,0,result.stderr.decode(errors='replace'))
        self.assertEqual(read_json(target),{'version':2})
        self.assertEqual(read_json(str(target)+'.previous'),{'version':1})


if __name__=='__main__':
    unittest.main()
