"""Local art-review loop: sync source into the isolated project, build Builds/Dev, run --showcase.

    python Scripts/dev_preview.py                 # sync + build + capture
    python Scripts/dev_preview.py --skip-build    # capture with the existing Builds/Dev player
    python Scripts/dev_preview.py --route verify-galaxy

Screenshots land in Artifacts/Local/Showcase/<UTC stamp>/ (git-ignored). This is an iteration
tool, not release evidence: run_unity_batch.py and the acceptance routes remain authoritative.
The isolated copy is mirrored from SpaceEngineG/{Assets,Packages,ProjectSettings}; it never
touches the main project's Library or a running Editor.
"""
import argparse
import datetime as dt
import pathlib
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'SpaceEngineG'
ISOLATED = ROOT / '.validation-unity-v050'
LOCAL = ROOT / 'Artifacts/Local'
PLAYER = ROOT / 'Builds/Dev/SpaceEngineG.exe'
EDITOR = pathlib.Path('C:/Program Files/Unity/Hub/Editor/6000.5.7f1/Editor/Unity.exe')


def sync():
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        result = subprocess.run(['robocopy', str(SOURCE / folder), str(ISOLATED / folder), '/MIR',
                                 '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:2', '/W:1'], capture_output=True, text=True)
        if result.returncode >= 8:
            sys.exit('robocopy failed for %s (%d)\n%s' % (folder, result.returncode, result.stdout))


def build():
    lock = ISOLATED / 'Temp/UnityLockfile'
    if lock.exists():
        try:
            lock.unlink()
        except OSError:
            sys.exit('The isolated project is open in another Unity process: ' + str(lock))
    log = LOCAL / 'dev-build.log'
    started = time.time()
    code = subprocess.run([str(EDITOR), '-batchmode', '-projectPath', str(ISOLATED), '-logFile', str(log),
                           '-executeMethod', 'SpaceEngine.Editor.ExplorerBuild.BuildDevPreview', '-quit']).returncode
    text = log.read_text(encoding='utf-8', errors='replace') if log.exists() else ''
    errors = [line for line in text.splitlines() if ': error CS' in line or 'Shader error' in line or 'error:' in line.lower() and 'shader' in line.lower()]
    print('build exit %d in %.0fs' % (code, time.time() - started))
    for line in errors[:40]:
        print('  ' + line)
    if code != 0 or not PLAYER.exists():
        tail = '\n'.join(text.splitlines()[-60:])
        sys.exit('Build failed; see ' + str(log) + '\n' + tail)


def capture(route):
    stamp = dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    out = LOCAL / 'Showcase' / (stamp + '-' + route)
    out.mkdir(parents=True, exist_ok=True)
    log = out / 'Player.log'
    args = [str(PLAYER), '--' + route, '--quit-after-verification', '--evidence-path', str(out),
            '-screen-width', '1600', '-screen-height', '900', '-screen-fullscreen', '0', '-logFile', str(log)]
    started = time.time()
    try:
        code = subprocess.run(args, timeout=900).returncode
    except subprocess.TimeoutExpired:
        code = -1
    print('player exit %d in %.0fs -> %s' % (code, time.time() - started, out))
    if log.exists():
        lines = log.read_text(encoding='utf-8', errors='replace').splitlines()
        bad = [l for l in lines if 'Exception' in l or 'Error' in l and 'Shader' in l or 'error' in l.lower() and 'shader' in l.lower()]
        for line in bad[:30]:
            print('  ' + line)
    print('\n'.join(sorted(p.name for p in out.glob('*.png'))))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--skip-build', action='store_true')
    parser.add_argument('--route', default='showcase')
    args = parser.parse_args()
    LOCAL.mkdir(parents=True, exist_ok=True)
    if not args.skip_build:
        sync()
        build()
    capture(args.route)


if __name__ == '__main__':
    main()
