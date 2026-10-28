"""Run the visible v0.5 Windows player and record its real exploration route.

    python Scripts/verify_windows.py --route all
    python Scripts/verify_windows.py --route explorer --timeout 240

Only Python's standard library is required. FFmpeg is an external executable.
Explorer requests Unity's actual game framebuffer through a raw RGB24 pipe.
No Windows window capture, desktop access or title renaming is performed.
Previous evidence folders are moved intact to PreviousRuns before replacement.
"""
import argparse
import datetime as dt
import hashlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import time
import uuid


ROOT = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_PLAYER = ROOT / 'Builds/Windows-v0.5.0/SpaceEngineG.exe'
DEFAULT_OUTPUT = ROOT / 'Artifacts/V050'
DEFAULT_FFMPEG = pathlib.Path(
    'C:/Environment/python/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe')
ROUTES = {
    'flight': ('PlayerFlight', 'runtime-verification.json', ['--verify-flight']),
    'galaxy': ('PlayerGalaxy', 'galaxy-verification.json', ['--verify-galaxy']),
    'details': ('PlayerDetails', 'details-verification.json', ['--verify-details']),
    'explorer': ('PlayerExplorer', 'explorer-verification.json',
                 ['--verify-explorer', '--record-descent']),
}
WIDTH, HEIGHT, FPS = 1600, 900, 30
HIDDEN = getattr(subprocess, 'CREATE_NO_WINDOW', 0x08000000)


def utc(epoch=None):
    return dt.datetime.fromtimestamp(time.time() if epoch is None else epoch,
                                    dt.timezone.utc).isoformat()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def sha256(path):
    result = hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def prepare_directory(output_root, name):
    """Preserve prior controls/failures, without deleting evidence or arbitrary paths."""
    output_root = output_root.resolve()
    output_root.mkdir(parents=True, exist_ok=True)
    requested = output_root / name
    if requested.is_symlink() or (hasattr(requested, 'is_junction') and requested.is_junction()):
        raise RuntimeError('Refusing to move a linked evidence directory.')
    directory = requested.resolve()
    if directory.parent != output_root or directory.name != name:
        raise RuntimeError('Evidence directory must be a direct child of output root.')
    previous = None
    if directory.exists():
        if not directory.is_dir():
            raise RuntimeError('Evidence path is not a directory: ' + str(directory))
        stamp = dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
        previous = (output_root / 'PreviousRuns' / (stamp + '-' + uuid.uuid4().hex[:8]) / name).resolve()
        if not previous.is_relative_to(output_root):
            raise RuntimeError('Archive target escapes evidence root.')
        previous.parent.mkdir(parents=True, exist_ok=True)
        directory.rename(previous)
    directory.mkdir()
    return directory, previous


def remaining(deadline, maximum):
    return max(.1, min(maximum, deadline - time.monotonic()))


def duration_from_text(text):
    match = re.search(r'Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)', text)
    return int(match[1]) * 3600 + int(match[2]) * 60 + float(match[3]) if match else None


def validate_video(ffmpeg, ffprobe, directory, metadata, deadline):
    movie = directory / 'descent.mp4'
    metadata['videoBytes'] = movie.stat().st_size if movie.exists() else 0
    if metadata['videoBytes'] <= 1_000_000:
        raise RuntimeError('Explorer video is missing or smaller than 1 MB.')
    # A complete software decode verifies the finalized MP4 and every frame,
    # even when no separate ffprobe executable is installed.
    command = [str(ffmpeg), '-hide_banner', '-nostdin', '-xerror', '-progress', 'pipe:1',
               '-nostats', '-i', str(movie), '-map', '0:v:0', '-an', '-sn', '-dn',
               '-f', 'null', os.devnull]
    decode = subprocess.run(command, capture_output=True, text=True, encoding='utf-8',
                            errors='replace', creationflags=HIDDEN, timeout=remaining(deadline, 90))
    (directory / 'ffmpeg-decode.log').write_text(decode.stderr, encoding='utf-8')
    (directory / 'ffmpeg-decode-progress.txt').write_text(decode.stdout, encoding='utf-8')
    metadata['fullDecodeExitCode'] = decode.returncode
    if decode.returncode != 0:
        raise RuntimeError('Recorded video failed full decode; see ffmpeg-decode.log.')
    progress = dict(line.split('=', 1) for line in decode.stdout.splitlines() if '=' in line)
    metadata['decodedFrames'] = int(progress.get('frame', 0))
    metadata['decodedDurationSeconds'] = int(progress.get('out_time_us', 0)) / 1_000_000
    metadata['durationSeconds'] = duration_from_text(decode.stderr)
    video_line = next((line for line in decode.stderr.splitlines() if 'Video:' in line), '')
    dimensions = re.search(r'\b(\d{2,5})x(\d{2,5})\b', video_line)
    rate = re.search(r'\b(\d+(?:\.\d+)?) fps\b', video_line)
    metadata['videoWidth'] = int(dimensions[1]) if dimensions else 0
    metadata['videoHeight'] = int(dimensions[2]) if dimensions else 0
    metadata['framesPerSecond'] = float(rate[1]) if rate else 0
    metadata['videoCodec'] = 'h264' if 'Video: h264' in video_line else video_line.strip()
    metadata['pixelFormat'] = 'yuv420p' if 'yuv420p' in video_line else ''
    if ffprobe:
        probe_command = [str(ffprobe), '-v', 'error', '-show_format', '-show_streams', '-of', 'json', str(movie)]
        probe = subprocess.run(probe_command, capture_output=True, text=True, encoding='utf-8',
                               errors='replace', creationflags=HIDDEN, timeout=remaining(deadline, 30))
        metadata['ffprobeExitCode'] = probe.returncode
        if probe.returncode != 0:
            raise RuntimeError('ffprobe failed: ' + probe.stderr)
        parsed = json.loads(probe.stdout)
        write_json(directory / 'ffprobe.json', parsed)
        metadata['durationSeconds'] = float(parsed['format']['duration'])
        metadata['ffprobeExecutable'] = str(ffprobe)
    else:
        metadata['ffprobeExecutable'] = None
        metadata['durationSource'] = 'FFmpeg input-container duration; full decode independently checked'
    if metadata['durationSeconds'] is None or metadata['durationSeconds'] <= 20:
        raise RuntimeError('Explorer recording must contain more than 20 seconds.')
    if (metadata['videoWidth'], metadata['videoHeight']) != (WIDTH, HEIGHT):
        raise RuntimeError('Capture does not have the requested 1600x900 player client dimensions.')
    if abs(metadata['framesPerSecond'] - FPS) > .01 or metadata['decodedFrames'] < 600:
        raise RuntimeError('Capture is not a complete 30 fps stream.')
    if metadata['videoCodec'] != 'h264' or metadata['pixelFormat'] != 'yuv420p':
        raise RuntimeError('Capture did not use the requested H.264/yuv420p format.')
    metadata['videoSha256'] = sha256(movie)


def compare_stages(ffmpeg, directory, native, metadata, deadline):
    """Compare real stage PNGs with their native EOF video-frame addresses."""
    wanted = {'06-descent-start.png', '07-first-surface-hold.png',
              '08-continuous-ascent.png', '09-return-surface-hold.png'}
    stages = [stage for stage in native.get('stages', []) if stage['screenshot'] in wanted]
    if {stage['screenshot'] for stage in stages} != wanted:
        raise RuntimeError('Native video did not mark all four descent/hold/return screenshot frames.')
    comparisons = []
    for stage in stages:
        index = int(stage['videoFrameIndex'])
        if not 0 <= index < native['encodedFrames']:
            raise RuntimeError('Native stage video-frame index lies outside the encoded stream.')
        commands = [
            [str(ffmpeg), '-v', 'error', '-nostdin', '-i', str(directory / 'descent.mp4'),
             '-vf', 'select=eq(n\\,%d)' % index, '-frames:v', '1', '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1'],
            [str(ffmpeg), '-v', 'error', '-nostdin', '-i', str(directory / stage['screenshot']),
             '-frames:v', '1', '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1'],
        ]
        images = []
        for command in commands:
            decoded = subprocess.run(command, capture_output=True, creationflags=HIDDEN,
                                     timeout=remaining(deadline, 45))
            if decoded.returncode or len(decoded.stdout) != WIDTH * HEIGHT * 3:
                raise RuntimeError('Could not decode a complete RGB24 video/PNG stage comparison: ' + stage['screenshot'])
            images.append(decoded.stdout)
        video, screenshot = images
        error = sum(abs(a - b) for a, b in zip(video, screenshot)) / (len(video) * 255)
        stride = WIDTH * 3
        flipped = b''.join(screenshot[row * stride:(row + 1) * stride] for row in range(HEIGHT - 1, -1, -1))
        flipped_error = sum(abs(a - b) for a, b in zip(video, flipped)) / (len(video) * 255)
        passed = error < .035 and error < flipped_error * .6
        comparisons.append({**stage, 'passed': passed, 'meanRgbError': error,
                            'upsideDownMeanRgbError': flipped_error,
                            'meanRgbErrorLimit': .035, 'orientationRatioLimit': .6})
    metadata['stagePngComparisons'] = comparisons
    metadata['coversDescentStartScreenshot'] = any(s['screenshot'] == '06-descent-start.png' and s['passed'] for s in comparisons)
    metadata['coversReturnScreenshot'] = any(s['screenshot'] == '09-return-surface-hold.png' and s['passed'] for s in comparisons)
    if not all(stage['passed'] for stage in comparisons):
        raise RuntimeError('Actual video and stage PNG differ in orientation/color/content; inspect stagePngComparisons.')


def validate_report(directory, route, report_name):
    path = directory / report_name
    report = json.loads(path.read_text(encoding='utf-8-sig'))
    if not report.get('passed') or report.get('errors') != 0:
        raise RuntimeError('Player acceptance reported failure: ' + str(path))
    version = report.get('applicationVersion', report.get('version'))
    if version != '0.5.0' or not report.get('buildGuid', '').strip('0'):
        raise RuntimeError('Report does not identify the v0.5.0 standalone build.')
    if route == 'explorer':
        names = report['screenshots']
        if not {'06-descent-start.png', '07-first-surface-hold.png', '08-continuous-ascent.png',
                '09-return-surface-hold.png'}.issubset(names):
            raise RuntimeError('Explorer report is missing complete descent/return milestones.')
    elif route == 'details':
        names = report['screenshots']
        if len(names) < 19:
            raise RuntimeError('Missing full details page/category/legacy captures.')
    elif route == 'flight':
        names = [entry['stage'] + '.png' for entry in report['samples']]
    else:
        names = [entry['name'] + '.png' for entry in report['stages']]
    if not names:
        raise RuntimeError('Player reported no screenshot evidence.')
    for name in names:
        screenshot = (directory / name).resolve()
        if screenshot.parent != directory or screenshot.suffix != '.png' or not screenshot.is_file() or screenshot.stat().st_size < 10000:
            raise RuntimeError('Missing or invalid screenshot: ' + name)
    return report


def run_route(args, route, ffprobe):
    folder, report_name, switches = ROUTES[route]
    directory, previous = prepare_directory(args.output, folder)
    command = [str(args.player), *switches, '--quit-after-verification', '--evidence-path', str(directory),
               '-screen-fullscreen', '0', '-screen-width', str(WIDTH), '-screen-height', str(HEIGHT),
               '-logFile', str(directory / 'Player.log')]
    if route == 'explorer':
        command += ['--video-encoder', str(args.ffmpeg), '--video-output', str(directory / 'descent.mp4')]
    metadata = {
        'passed': False, 'route': route, 'startedAtUtc': utc(), 'playerExecutable': str(args.player),
        'executableSha256': sha256(args.player), 'playerCommand': command, 'timeoutSeconds': args.timeout,
        'evidenceDirectory': str(directory), 'previousEvidenceDirectory': str(previous) if previous else None,
        'playerPid': None, 'playerExitCode': None, 'failures': [],
    }
    capture = {
        'passed': False, 'durationSeconds': None, 'capturedWindowTitle': '', 'playerPid': None,
        'backend': 'Unity framebuffer ReadPixels + rawvideo pipe',
        'captureBackend': 'Unity framebuffer ReadPixels + rawvideo pipe',
        'windowTitleScope': 'Application.productName identifier only; no Windows window or desktop capture',
        'desktopCaptureAllowed': False, 'requestedFramesPerSecond': FPS,
        'requestedWidth': WIDTH, 'requestedHeight': HEIGHT, 'failures': [],
    }
    player = None
    interrupted = False
    started = time.monotonic()
    deadline = started + args.timeout
    print(route + ': starting visible player -> ' + str(directory), flush=True)
    try:
        with (directory / 'launcher-output.log').open('wb') as launcher_log:
            player = subprocess.Popen(command, cwd=args.player.parent, stdout=launcher_log,
                                      stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)
            metadata['playerPid'] = capture['playerPid'] = player.pid
            while player.poll() is None:
                if time.monotonic() >= deadline:
                    raise TimeoutError('Player route exceeded ' + str(args.timeout) + ' seconds.')
                time.sleep(.1)
            metadata['playerExitCode'] = player.returncode
            if player.returncode != 0:
                raise RuntimeError('Player exited with code ' + str(player.returncode))
            report = validate_report(directory, route, report_name)
            metadata.update({'buildGuid': report['buildGuid'], 'reportPassed': True})
            if not (directory / 'Player.log').is_file():
                raise RuntimeError('Unity did not write the requested Player.log.')
            if route == 'explorer':
                native = json.loads((directory / 'native-video.json').read_text(encoding='utf-8-sig'))
                capture['native'] = native
                capture.update({
                    'capturedWindowTitle': native.get('productName', ''),
                    'encoderExitCode': native.get('encoderExitCode'),
                    'encodedFrames': native.get('encodedFrames'),
                    'actualReadFrames': native.get('actualReadFrames'),
                    'repeatedFrames': native.get('repeatedFrames'),
                    'firstFrameUtc': native.get('firstFrameUtc'),
                    'firstFrameEpoch': native.get('firstFrameEpoch'),
                    'captureStartedAtUtc': native.get('firstFrameUtc'),
                    'captureStartedAtEpoch': native.get('firstFrameEpoch'),
                    'timestampUncertaintySeconds': native.get('timestampUncertaintySeconds'),
                    'playerExitCode': player.returncode,
                })
                if not native.get('passed') or native.get('encoderExitCode') != 0 or native.get('playerPid') != player.pid:
                    raise RuntimeError('Native framebuffer recording failed or belongs to another player PID.')
                if native.get('backend') != capture['backend'] or not capture['capturedWindowTitle']:
                    raise RuntimeError('Native recording does not identify the actual game-framebuffer backend.')
                if native.get('encodedFrames') != native.get('actualReadFrames', 0) + native.get('repeatedFrames', 0):
                    raise RuntimeError('Native actual/duplicated frame accounting is inconsistent.')
                validate_video(args.ffmpeg, ffprobe, directory, capture, deadline)
                if capture['decodedFrames'] != native['encodedFrames']:
                    raise RuntimeError('Decoded video frames differ from the native raw pipe frame count.')
                if abs(capture['durationSeconds'] - native['durationSeconds']) > 2 / FPS:
                    raise RuntimeError('Container duration differs from the native frame timeline.')
                compare_stages(args.ffmpeg, directory, native, capture, deadline)
                if time.monotonic() > deadline:
                    raise TimeoutError('Route and video verification exceeded the overall timeout.')
                capture['passed'] = True
            metadata['passed'] = True
    except KeyboardInterrupt:
        interrupted = True
        metadata['failures'].append('Interrupted by user.')
    except Exception as error:
        metadata['failures'].append(str(error))
    finally:
        if player is not None and player.poll() is None:
            player.terminate()
            try:
                player.wait(timeout=5)
            except subprocess.TimeoutExpired:
                player.kill()
                player.wait(timeout=5)
        if player is not None:
            metadata['playerExitCode'] = player.returncode
            (directory / 'exit-code.txt').write_text(str(player.returncode) + '\n', encoding='utf-8')
        else:
            (directory / 'exit-code.txt').write_text('not-started\n', encoding='utf-8')
        metadata['finishedAtUtc'] = utc()
        metadata['elapsedSeconds'] = time.monotonic() - started
        if route == 'explorer':
            capture['failures'] = list(metadata['failures'])
            capture['passed'] = capture['passed'] and metadata['passed'] and not capture['failures']
            write_json(directory / 'capture-metadata.json', capture)
        write_json(directory / 'run-metadata.json', metadata)
    print(route + ': ' + ('PASS' if metadata['passed'] else 'FAIL ' + '; '.join(metadata['failures'])), flush=True)
    if interrupted:
        raise KeyboardInterrupt
    return metadata


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--route', choices=['all', *ROUTES], default='all')
    parser.add_argument('--timeout', type=float, default=240, help='Player wall-time limit per route, in seconds.')
    parser.add_argument('--player', type=pathlib.Path, default=DEFAULT_PLAYER)
    parser.add_argument('--output', type=pathlib.Path, default=DEFAULT_OUTPUT)
    parser.add_argument('--ffmpeg', type=pathlib.Path, default=DEFAULT_FFMPEG)
    parser.add_argument('--ffprobe', type=pathlib.Path, help='Optional; full video decode also works without ffprobe.')
    args = parser.parse_args()
    if os.name != 'nt':
        parser.error('This launcher requires Windows and a visible interactive desktop.')
    if args.timeout <= 0:
        parser.error('--timeout must be positive.')
    args.player, args.output, args.ffmpeg = args.player.resolve(), args.output.resolve(), args.ffmpeg.resolve()
    if not args.player.is_file():
        parser.error('Build the v0.5.0 player first; executable missing: ' + str(args.player))
    if args.route in ('all', 'explorer') and not args.ffmpeg.is_file():
        parser.error('FFmpeg executable missing: ' + str(args.ffmpeg))
    ffprobe = args.ffprobe.resolve() if args.ffprobe else args.ffmpeg.with_name('ffprobe.exe')
    if args.ffprobe and not ffprobe.is_file():
        parser.error('--ffprobe executable does not exist.')
    if not ffprobe.is_file():
        found = shutil.which('ffprobe')
        ffprobe = pathlib.Path(found).resolve() if found else None
    results = []
    try:
        for route in ROUTES if args.route == 'all' else [args.route]:
            results.append(run_route(args, route, ffprobe))
    except KeyboardInterrupt:
        write_json(args.output / 'windows-verification.json', {'passed': False, 'utc': utc(),
            'interrupted': True, 'routes': results})
        return 130
    passed = bool(results) and all(result['passed'] for result in results)
    guids = {result.get('buildGuid') for result in results if result['passed']}
    if len(guids) > 1:
        passed = False
        print('FAIL: routes used different standalone build GUIDs.', file=sys.stderr)
    write_json(args.output / 'windows-verification.json', {
        'passed': passed, 'utc': utc(), 'routes': results,
        'scope': 'Visible standalone routes; Explorer samples actual Unity framebuffer at30fps, repeats missing slots, and checks full decode plus stage PNG orientation/color.',
    })
    return 0 if passed else 2


if __name__ == '__main__':
    raise SystemExit(main())
