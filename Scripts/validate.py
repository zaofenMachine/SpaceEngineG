"""Reproduce Unity tests/graphics evidence through the connected local Editor.

Use UNITY_MCP_URL to select the loopback endpoint. Requires Python 3 standard library.
"""
import argparse
import json
import pathlib
import time
from unity_rpc import rpc

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / 'Artifacts' / 'V020'

def call(name, arguments):
    envelope = rpc('tools/call', {'name': name, 'arguments': arguments})
    if 'error' in envelope:
        raise RuntimeError(envelope['error'])
    result = envelope['result']
    value = result.get('structuredContent')
    if value is None:
        value = json.loads(next(c['text'] for c in result['content'] if c['type']=='text'))
    if isinstance(value.get('result'), dict) and 'success' in value['result']:
        value = value['result']
    if value.get('success') is False:
        raise RuntimeError(value)
    return value

def tests(mode):
    job = call('run_tests', {'mode':mode,'include_failed_tests':True,'init_timeout':120000})['data']['job_id']
    deadline=time.monotonic()+600
    while time.monotonic()<deadline:
        result=call('get_test_job', {'job_id':job,'include_failed_tests':True,'wait_timeout':30})
        data=result['data']
        if data['status'] in ('succeeded','failed','cancelled'):
            path=OUT/(mode.lower()+'-tests.json')
            path.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
            print(mode, data.get('result',{}).get('summary',{}),flush=True)
            if data['status']!='succeeded':raise RuntimeError('Unity tests failed; see '+str(path))
            return
    raise TimeoutError('Unity test runner did not finish; inspect Editor state.')

def graphics():
    for menu,filename in [('Space Engine/Validate Shaders','shaders.json'),
                          ('Space Engine/Validate Atmosphere Pixels','atmosphere-pixels.json'),
                          ('Space Engine/Validate Celestial Pixels','CelestialPixels/celestial-pixels.json')]:
        call('execute_menu_item', {'menu_path':menu})
        result=json.loads((OUT/filename).read_text(encoding='utf-8-sig'))
        print(filename,'PASS' if result['passed'] else 'FAIL',flush=True)
        if not result['passed']:raise RuntimeError('Graphics validation failed: '+filename)

def flight():
    path=OUT/'Flight'/'runtime-verification.json'
    old=path.stat().st_mtime_ns if path.exists() else 0
    call('manage_editor', {'action':'stop'})
    call('execute_menu_item', {'menu_path':'Space Engine/Run Rendered Flight Acceptance'})
    deadline=time.monotonic()+240
    while time.monotonic()<deadline:
        if path.exists() and path.stat().st_mtime_ns>old:
            result=json.loads(path.read_text(encoding='utf-8-sig'))
            print('Rendered route', 'PASS' if result['passed'] else 'FAIL', flush=True)
            if not result['passed']:raise RuntimeError('Rendered route failed: '+result['failure'])
            return
        time.sleep(2)
    raise TimeoutError('Rendered route did not finish; inspect Editor and runtime logs.')

def galaxy():
    path=OUT/'Galaxy'/'galaxy-verification.json'
    old=path.stat().st_mtime_ns if path.exists() else 0
    call('manage_editor', {'action':'stop'})
    call('execute_menu_item', {'menu_path':'Space Engine/Run Galaxy And Body Acceptance'})
    deadline=time.monotonic()+300
    while time.monotonic()<deadline:
        if path.exists() and path.stat().st_mtime_ns>old:
            result=json.loads(path.read_text(encoding='utf-8-sig'))
            print('Galaxy expedition','PASS' if result['passed'] else 'FAIL',flush=True)
            if not result['passed']:raise RuntimeError('Galaxy render expedition failed; inspect report.')
            return
        time.sleep(2)
    raise TimeoutError('Galaxy expedition did not finish.')

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tests',action='store_true')
    parser.add_argument('--graphics',action='store_true')
    parser.add_argument('--flight',action='store_true')
    parser.add_argument('--galaxy',action='store_true')
    args=parser.parse_args()
    OUT.mkdir(parents=True,exist_ok=True)
    if args.tests: tests('EditMode');tests('PlayMode')
    if args.graphics:graphics()
    if args.flight:flight()
    if args.galaxy:galaxy()
    if not any(vars(args).values()):parser.print_help()
