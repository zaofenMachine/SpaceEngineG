"""Screen real Explorer recording frames for dark terrain-region candidates.

Examples (never modifies/re-encodes the input movie):
  python Scripts/scan_surface_video.py --player-dir Artifacts/V040/PlayerExplorer --known-negative-frame 60 --label v040-control
  python Scripts/scan_surface_video.py --player-dir Artifacts/V050/PlayerExplorer --label v050-first
  python Scripts/scan_surface_video.py --player-dir path/to/new/cohort --surface-start-stage 00-return-home.png

All source frames between native 05-bookmarks and 06-descent-start are decoded
at their original frame indices. A dark ROI is only a candidate, not proof of a
missing planet. The initial system overview is intentionally dark. With no exact
ReturnHome marker, the report's first 600km settling sample supplies a guarded
post-return bound; frames before it remain ambiguous, including a known control.
Metrics, lossless frame extracts and contact sheets aid review. They are neither
automatic visual acceptance nor human frame-by-frame review. Requires numpy/Pillow
and FFmpeg (PATH, --ffmpeg, or imageio_ffmpeg). No Unity invocation is performed.
"""
import argparse
from collections import Counter
import datetime as dt
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageOps


ROOT = Path(__file__).resolve().parent.parent
DEFAULT_ROI = (.32, .56, .65, .86)
HIDDEN = getattr(subprocess, 'CREATE_NO_WINDOW', 0) if os.name == 'nt' else 0


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')


def relative(path):
    path = Path(path).resolve()
    return path.relative_to(ROOT).as_posix() if path.is_relative_to(ROOT) else str(path)


def sha256(path):
    result = hashlib.sha256()
    with path.open('rb') as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b''):
            result.update(chunk)
    return result.hexdigest()


def ffmpeg_binary(requested):
    if requested:
        return str(Path(requested).resolve())
    found = shutil.which('ffmpeg')
    if found:
        return found
    import imageio_ffmpeg
    return imageio_ffmpeg.get_ffmpeg_exe()


def timestamp(value):
    if isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value):
        return float(value)
    if isinstance(value, str):
        try:
            instant = dt.datetime.fromisoformat(value.replace('Z', '+00:00'))
            return instant.timestamp() if instant.tzinfo is not None else None
        except ValueError:
            pass
    return None


def stage_named(stages, name):
    stem = Path(name).stem.lower()
    matches = [s for s in stages if Path(str(s.get('screenshot', '')).replace('\\', '/')).stem.lower() == stem]
    if len(matches) != 1:
        raise ValueError('Exactly one native stage required: ' + name)
    return matches[0]


def stage_index(stage, count):
    index = stage.get('videoFrameIndex')
    if not isinstance(index, int) or isinstance(index, bool) or not 0 <= index < count:
        raise ValueError('Invalid zero-based native videoFrameIndex: ' + str(stage))
    return index


def roi_box(width, height, normalized):
    if len(normalized) != 4 or not all(math.isfinite(x) and 0 <= x <= 1 for x in normalized):
        raise ValueError('ROI must contain four finite normalized coordinates in [0,1].')
    x0, y0, x1, y1 = [round(v * s) for v, s in zip(normalized, (width, height, width, height))]
    if x1 <= x0 or y1 <= y0:
        raise ValueError('ROI must have positive width and height.')
    return x0, y0, x1, y1


def boundary(report, native, stages, first, last, fps, args):
    """Do not infer a precise ReturnHome transition from a dark picture itself."""
    if args.surface_start_frame is not None:
        if not first < args.surface_start_frame <= last:
            raise ValueError('Explicit surface-start frame must lie after the bookmarks stage and at/before descent-start.')
        return {'source': 'explicit caller-confirmed source frame', 'exact': True,
                'confirmedStartFrame': args.surface_start_frame,
                'note': 'The caller must independently verify this scene boundary; pixel thresholds do not establish it.'}
    if args.surface_start_stage:
        stage = stage_named(stages, args.surface_start_stage)
        index = stage_index(stage, native['encodedFrames'])
        if not first < index <= last:
            raise ValueError('Explicit surface-start stage is outside this transition interval.')
        return {'source': 'explicit native EOF stage', 'exact': True, 'confirmedStartFrame': index,
                'stage': stage, 'note': 'Caller supplied a stage known to follow ReturnHome; a filename alone is not a scene oracle.'}
    marked = [s for s in stages if Path(str(s.get('screenshot', '')).replace('\\', '/')).stem.lower()
              == '05b-same-frame-home-reload']
    if len(marked) > 1:
        raise ValueError('Duplicate same-frame home-reload markers.')
    if marked:
        index = stage_index(marked[0], native['encodedFrames'])
        if not first < index <= last:
            raise ValueError('Same-frame home-reload marker is outside the transition interval.')
        return {'source': 'native EOF 05b-same-frame-home-reload marker', 'exact': True,
                'confirmedStartFrame': index, 'stage': marked[0],
                'note': 'Audited Explorer route marks this EOF immediately after ReturnHome/SetAltitude, before WaitForTerrain. It identifies a required surface view without inferring the boundary from image brightness.'}
    route_epoch = timestamp(report.get('utc'))
    video_epoch = timestamp(native.get('firstFrameEpoch'))
    if video_epoch is None:
        video_epoch = timestamp(native.get('firstFrameUtc'))
    uncertainty = native.get('timestampUncertaintySeconds', .1)
    if not isinstance(uncertainty, (int, float)) or not math.isfinite(uncertainty) or uncertainty < 0:
        raise ValueError('Invalid native timestamp uncertainty.')
    if route_epoch is None or video_epoch is None:
        return {'source': 'unavailable', 'exact': False, 'confirmedStartFrame': last,
                'note': 'No usable report/native clock mapping. Only the documented descent-start stage is a known surface view.'}
    offset = route_epoch - video_epoch
    matches = []
    for frame in report.get('frames', []):
        elapsed, clearance = frame.get('elapsed'), frame.get('clearance')
        if not isinstance(elapsed, (int, float)) or not math.isfinite(elapsed):
            continue
        if not isinstance(clearance, (int, float)) or not math.isfinite(clearance):
            continue
        seconds = float(elapsed) + offset
        if (frame.get('phase') == 'settling' and first / fps < seconds < last / fps and
                abs(clearance - args.expected_clearance) <= args.clearance_tolerance):
            matches.append((seconds, frame))
    if not matches:
        return {'source': 'no matched post-return report sample', 'exact': False,
                'confirmedStartFrame': last, 'reportToVideoOffsetSeconds': offset,
                'note': 'Do not classify dark overview/transition frames as a missing surface. Only descent-start is confirmed.'}
    seconds, sample = min(matches, key=lambda item: item[0])
    # The report sample occurs after ReturnHome/SetAltitude in the audited route.
    # Adding uncertainty AND one output frame avoids attributing pre-render frames
    # to that scene. This can deliberately leave an early missing frame ambiguous.
    guarded = min(last, max(first + 1, math.ceil((seconds + uncertainty) * fps) + 1))
    return {'source': 'first matching report settling sample after bookmarks', 'exact': False,
            'confirmedStartFrame': guarded, 'sampleEstimatedVideoSeconds': seconds,
            'sampleEstimatedSourceFrame': seconds * fps, 'sample': sample,
            'reportToVideoOffsetSeconds': offset, 'timestampUncertaintySeconds': uncertainty,
            'guardSeconds': uncertainty + 1 / fps,
            'note': 'Known route: bookmarks overview -> restore bookmark -> ReturnHome -> 600km -> WaitForTerrain. The exact first new-world EOF was not marked. Earlier frames are ambiguous, not automatic bugs.'}


def scene_scope(index, first, last, bound):
    if index == first:
        return 'documented_bookmarks_overview'
    if index == last:
        return 'documented_descent_start'
    if index >= bound['confirmedStartFrame']:
        return 'confirmed_post_return_surface' if bound['exact'] else 'guarded_post_return_surface'
    return 'unresolved_overview_or_return_transition'


def pixel_metrics(rgb, args):
    signal = rgb.astype(np.float32) / 255
    # Display-referred RGB luma proxy, NOT physical linear radiance/photometry.
    luma = signal @ np.array([.2126, .7152, .0722], dtype=np.float32)
    fraction = float(np.mean(rgb.max(axis=2) > args.black_threshold))
    p95 = float(np.percentile(luma, 95))
    return {'meanRgb8Bit': rgb.mean(axis=(0, 1)).tolist(),
            'meanLuma': float(luma.mean()), 'medianLuma': float(np.median(luma)),
            'p95Luma': p95, 'p99Luma': float(np.percentile(luma, 99)),
            'lumaStd': float(luma.std()), 'nonBlackFraction': fraction,
            'darkCandidate': fraction < args.minimum_nonblack_fraction and p95 < args.maximum_dark_p95}


def read_exact(pipe, size):
    chunks, remaining = [], size
    while remaining:
        value = pipe.read(remaining)
        if not value:
            if remaining == size:
                return None
            raise RuntimeError('Incomplete raw ROI frame at EOF.')
        chunks.append(value)
        remaining -= len(value)
    return b''.join(chunks)


def scan_frames(binary, video, first, last, box, fps, bound, args, directory):
    x0, y0, x1, y1 = box
    width, height = x1 - x0, y1 - y0
    filter_graph = 'select=between(n\\,%d\\,%d),crop=%d:%d:%d:%d,showinfo' % (first, last, width, height, x0, y0)
    command = [binary, '-hide_banner', '-nostdin', '-xerror', '-i', str(video), '-map', '0:v:0',
               '-an', '-sn', '-dn', '-vf', filter_graph, '-fps_mode', 'passthrough',
               '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1']
    rows = []
    with (directory / 'roi-decode.log').open('wb') as log:
        process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=log, creationflags=HIDDEN)
        try:
            while True:
                raw = read_exact(process.stdout, width * height * 3)
                if raw is None:
                    break
                index = first + len(rows)
                if index > last:
                    raise RuntimeError('More ROI frames than the source-index interval; unexpected frame duplication.')
                image = np.frombuffer(raw, dtype=np.uint8).reshape((height, width, 3))
                rows.append({'sourceFrameIndex': index, 'videoSecondsByNativeCfr': index / fps,
                    'sceneScope': scene_scope(index, first, last, bound),
                    'knownNegativeControl': index in args.known_negative_frame,
                    **pixel_metrics(image, args)})
            code = process.wait(timeout=60)
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=15)
            process.stdout.close()
    if code:
        raise RuntimeError('FFmpeg ROI decode failed; see roi-decode.log.')
    if len(rows) != last - first + 1:
        raise RuntimeError('Decoded interval length differs from native source frame indices.')
    text = (directory / 'roi-decode.log').read_text(encoding='utf-8', errors='replace')
    pts = [float(value) for value in re.findall(r'\bpts_time:([\d.eE+\-]+)', text)]
    if len(pts) != len(rows):
        raise RuntimeError('Source PTS count differs from raw ROI frame count.')
    for row, seconds in zip(rows, pts):
        row['sourcePtsSeconds'] = seconds
    return rows, command


def image_indices(rows, first, last, args):
    candidates = [row['sourceFrameIndex'] for row in rows if row['darkCandidate']]
    if len(candidates) > args.max_candidate_images:
        positions = np.linspace(0, len(candidates) - 1, args.max_candidate_images).round().astype(int)
        candidates = [candidates[i] for i in positions]
    # Keep objective progression context even when no candidate crosses a threshold.
    # Contact sheets must not hide a false negative merely because it was bright.
    chosen = {first, last, *range(first, last + 1, args.context_stride)}
    chosen.update(args.inspect_frame)
    for index in candidates + args.known_negative_frame:
        for neighbor in range(index - args.context_frames, index + args.context_frames + 1):
            if first <= neighbor <= last:
                chosen.add(neighbor)
    return sorted(chosen)


def extract_images(binary, video, indices, directory):
    image_dir = directory / 'frames'
    image_dir.mkdir()
    expression = '+'.join('eq(n\\,%d)' % i for i in indices)
    command = [binary, '-hide_banner', '-nostdin', '-xerror', '-i', str(video), '-map', '0:v:0',
               '-an', '-sn', '-dn', '-vf', 'select=' + expression, '-fps_mode', 'passthrough',
               '-frames:v', str(len(indices)), '-start_number', '0', str(image_dir / 'extract-%05d.png')]
    result = subprocess.run(command, capture_output=True, creationflags=HIDDEN, timeout=300)
    (directory / 'frame-extraction.log').write_bytes(result.stderr)
    files = sorted(image_dir.glob('extract-*.png'))
    if result.returncode or len(files) != len(indices):
        raise RuntimeError('Could not extract all candidate/context source PNGs.')
    paths = {}
    for source, index in zip(files, indices):
        path = image_dir / ('frame-%06d.png' % index)
        source.rename(path)
        paths[index] = path
    return paths


def contact_sheets(paths, rows, box, directory):
    metrics = {row['sourceFrameIndex']: row for row in rows}
    font = ImageFont.load_default(size=16)
    indices = list(paths)
    sheets = []
    for start in range(0, len(indices), 16):
        image = Image.new('RGB', (1600, 960), (8, 12, 16))
        draw = ImageDraw.Draw(image)
        for offset, index in enumerate(indices[start:start + 16]):
            left, top = offset % 4 * 400, offset // 4 * 240
            with Image.open(paths[index]) as source:
                context = source.convert('RGB')
                painter = ImageDraw.Draw(context)
                painter.rectangle(box, outline=(255, 180, 30), width=3)
                thumb = ImageOps.contain(context, (400, 210), Image.Resampling.LANCZOS)
            image.paste(thumb, (left, top + 30))
            row = metrics[index]
            caption = 'f%d %.3fs nonblack %.3f' % (index, row['sourcePtsSeconds'], row['nonBlackFraction'])
            color = (255, 180, 120) if row['darkCandidate'] else (190, 230, 220)
            draw.text((left + 4, top + 4), caption, fill=color, font=font)
        path = directory / ('candidate-context-%03d.png' % (start // 16 + 1))
        image.save(path)
        sheets.append({'file': relative(path), 'sourceFrameIndices': indices[start:start + 16],
                       'note': 'Orange rectangle marks the measured ROI. Original full-frame PNGs are preserved separately.'})
    return sheets


def runs_of_candidates(rows):
    runs = []
    for row in rows:
        if not row['darkCandidate']:
            continue
        index = row['sourceFrameIndex']
        if not runs or index != runs[-1]['lastFrame'] + 1 or row['sceneScope'] != runs[-1]['sceneScope']:
            runs.append({'firstFrame': index, 'lastFrame': index, 'frames': 1, 'sceneScope': row['sceneScope']})
        else:
            runs[-1]['lastFrame'] = index
            runs[-1]['frames'] += 1
    return runs


def scan(args):
    player_dir = Path(args.player_dir).resolve()
    video = Path(args.video).resolve() if args.video else player_dir / 'descent.mp4'
    meta_path = Path(args.metadata).resolve() if args.metadata else video.parent / 'capture-metadata.json'
    report_path = Path(args.report).resolve() if args.report else video.parent / 'explorer-verification.json'
    for path in (video, meta_path, report_path):
        if not path.is_file():
            raise FileNotFoundError(path)
    metadata, report = read_json(meta_path), read_json(report_path)
    native = metadata.get('native') or {}
    if native.get('backend') != 'Unity framebuffer ReadPixels + rawvideo pipe':
        raise ValueError('This source-index scanner requires the native Unity framebuffer recording metadata.')
    count, width, height = (native.get(key) for key in ('encodedFrames', 'width', 'height'))
    if not all(isinstance(x, int) and not isinstance(x, bool) and x > 0 for x in (count, width, height)):
        raise ValueError('Native encoded frame count and dimensions are required.')
    fps = native.get('framesPerSecond')
    if not isinstance(fps, (int, float)) or not math.isfinite(fps) or fps <= 0:
        raise ValueError('A finite native CFR frame rate is required.')
    stages = native.get('stages') or []
    first = stage_index(stage_named(stages, args.start_stage), count)
    last = stage_index(stage_named(stages, args.end_stage), count)
    if last <= first:
        raise ValueError('Descent-start must follow bookmarks.')
    if any(index < first or index > last for index in args.known_negative_frame + args.inspect_frame):
        raise ValueError('Known control or requested inspection frame lies outside the scanned transition interval.')
    digest = sha256(video)
    label = re.sub(r'[^A-Za-z0-9_.-]+', '-', args.label).strip('-') or 'scan'
    run = dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ') + '-' + label + '-' + digest[:8]
    output = Path(args.output).resolve()
    directory = output / run
    directory.mkdir(parents=True, exist_ok=False)
    result = {'utc': dt.datetime.now(dt.timezone.utc).isoformat(), 'pipelineSucceeded': False,
        'automaticVisualPass': None, 'manualFrameByFrameReviewed': False,
        'scope': 'Every source frame in the native bookmarks-to-descent-start interval, not every frame of the whole movie. Fixed display-RGB ROI screening only; intentional overview/space darkness is not automatically a bug.',
        'video': relative(video), 'videoSha256': digest, 'videoBytes': video.stat().st_size,
        'analysisScriptSha256': sha256(Path(__file__).resolve()),
        'captureMetadata': relative(meta_path), 'captureMetadataSha256': sha256(meta_path),
        'sourceReport': relative(report_path), 'sourceReportSha256': sha256(report_path),
        'buildGuid': report.get('buildGuid'), 'generationVersion': report.get('version'),
        'capturePassed': metadata.get('passed'), 'nativePassed': native.get('passed'),
        'nativeEncodedFrames': count, 'nativeDurationSeconds': native.get('durationSeconds'),
        'nativeCfrFramesPerSecond': fps, 'sourceWidth': width, 'sourceHeight': height,
        'sourceIndexConvention': 'Zero-based decoder n and native.stages.videoFrameIndex. No rate conversion or frame deletion.',
        'stages': {'start': stage_named(stages, args.start_stage), 'end': stage_named(stages, args.end_stage)},
        'roiNormalized': list(args.roi), 'thresholds': {'blackChannel8Bit': args.black_threshold,
            'minimumNonBlackFraction': args.minimum_nonblack_fraction, 'maximumDarkP95': args.maximum_dark_p95},
        'metricColorScope': 'FFmpeg RGB24 output; Rec.709-weighted display-signal luma proxy. Not linear radiance or absolute photometry.',
        'warnings': [], 'failures': []}
    try:
        if not metadata.get('passed') or not native.get('passed'):
            result['warnings'].append('Input capture was not marked passed. Decode success does not change capture acceptance.')
        if (width, height) != (1600, 900):
            result['warnings'].append('Default ROI was designed for the 1600x900 HUD; independently verify overlay exclusion at this resolution.')
        binary = ffmpeg_binary(args.ffmpeg)
        box = roi_box(width, height, args.roi)
        bound = boundary(report, native, stages, first, last, fps, args)
        result.update({'ffmpegExecutable': binary, 'roiPixels': list(box), 'returnHomeBoundary': bound})
        rows, command = scan_frames(binary, video, first, last, box, fps, bound, args, directory)
        result['roiDecodeCommand'] = command
        result['frames'] = rows
        result['scannedFrameCount'] = len(rows)
        result['darkCandidateCountsBySceneScope'] = dict(Counter(row['sceneScope'] for row in rows if row['darkCandidate']))
        result['darkCandidateRuns'] = runs_of_candidates(rows)
        result['knownNegativeControls'] = [row for row in rows if row['knownNegativeControl']]
        max_pts_error = max(abs(row['sourcePtsSeconds'] - row['videoSecondsByNativeCfr']) for row in rows)
        result['maximumSourcePtsVersusNativeCfrSeconds'] = max_pts_error
        if max_pts_error > .5 / fps:
            result['warnings'].append('Source PTS differs from native index/fps. Approximate report-time scene mapping must be reviewed.')
        if not bound['exact']:
            result['warnings'].append('Exact ReturnHome EOF is unavailable. Early dark candidates remain ambiguous; absence of later candidates cannot prove the first transition frames were fixed.')
        paths = extract_images(binary, video, image_indices(rows, first, last, args), directory)
        result['extractedFrames'] = [{'sourceFrameIndex': index, 'file': relative(path)} for index, path in paths.items()]
        result['contactSheets'] = contact_sheets(paths, rows, box, directory)
        result['candidateImageSelection'] = {'maximumCandidateCenters': args.max_candidate_images,
            'contextFramesEachSide': args.context_frames,
            'progressionContextStrideFrames': args.context_stride, 'explicitInspectionFrames': args.inspect_frame,
            'note': 'If needed, candidate centers are evenly subsampled for sheets; every frame metric remains in the JSON. Known controls, caller-requested inspection frames, regular progression context and interval endpoints are always included.'}
        if sha256(video) != digest or sha256(meta_path) != result['captureMetadataSha256'] or sha256(report_path) != result['sourceReportSha256']:
            raise RuntimeError('Input cohort changed during analysis. Do not combine evidence from different runs.')
        result['pipelineSucceeded'] = True
    except Exception as error:
        result['failures'].append(type(error).__name__ + ': ' + str(error))
    result['interpretation'] = 'pipelineSucceeded means extraction/metadata processing succeeded, never that visual quality passed. Dark pixels can be legitimate sky, occlusion or an overlay; bright pixels can still show wrong/loading geometry. Open original frames and contact sheets. This automatic pixel scan is not human frame-by-frame review.'
    write_json(directory / 'surface-video-scan.json', result)
    write_json(output / ('latest-' + label + '.json'), result)
    print(json.dumps({'pipelineSucceeded': result['pipelineSucceeded'], 'report': relative(directory / 'surface-video-scan.json'),
        'frames': result.get('scannedFrameCount', 0), 'darkCandidates': result.get('darkCandidateCountsBySceneScope', {}),
        'knownNegativeCandidates': [row['sourceFrameIndex'] for row in result.get('knownNegativeControls', []) if row['darkCandidate']],
        'failures': result['failures']}, ensure_ascii=False))
    return 0 if result['pipelineSucceeded'] else 1


def self_test():
    args = argparse.Namespace(black_threshold=8, minimum_nonblack_fraction=.02, maximum_dark_p95=.025)
    assert pixel_metrics(np.zeros((10, 10, 3), np.uint8), args)['darkCandidate']
    assert not pixel_metrics(np.full((10, 10, 3), 90, np.uint8), args)['darkCandidate']
    assert roi_box(1600, 900, DEFAULT_ROI) == (512, 504, 1040, 774)
    assert timestamp('2026-10-28T05:22:21.6903934Z') is not None
    bound = {'confirmedStartFrame': 65, 'exact': False}
    assert scene_scope(56, 56, 106, bound) == 'documented_bookmarks_overview'
    assert scene_scope(60, 56, 106, bound) == 'unresolved_overview_or_return_transition'
    assert scene_scope(65, 56, 106, bound) == 'guarded_post_return_surface'
    assert scene_scope(106, 56, 106, bound) == 'documented_descent_start'
    marker_args = argparse.Namespace(surface_start_frame=None, surface_start_stage=None)
    marked = boundary({}, {'encodedFrames': 110}, [{'screenshot': '05b-same-frame-home-reload.png', 'videoFrameIndex': 60}],
                      56, 106, 30, marker_args)
    assert marked['exact'] and marked['confirmedStartFrame'] == 60
    assert scene_scope(60, 56, 106, marked) == 'confirmed_post_return_surface'
    print('surface-video scanner helper checks passed')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--player-dir', default=str(ROOT / 'Artifacts/V050/PlayerExplorer'))
    parser.add_argument('--video')
    parser.add_argument('--metadata')
    parser.add_argument('--report')
    parser.add_argument('--output', default=str(ROOT / 'Artifacts/V050/SurfaceVideoScans'))
    parser.add_argument('--label', default='scan')
    parser.add_argument('--ffmpeg')
    parser.add_argument('--start-stage', default='05-bookmarks-cn.png')
    parser.add_argument('--end-stage', default='06-descent-start.png')
    parser.add_argument('--surface-start-stage')
    parser.add_argument('--surface-start-frame', type=int)
    parser.add_argument('--expected-clearance', type=float, default=600000)
    parser.add_argument('--clearance-tolerance', type=float, default=50000)
    parser.add_argument('--roi', nargs=4, type=float, default=DEFAULT_ROI)
    parser.add_argument('--black-threshold', type=int, default=8)
    parser.add_argument('--minimum-nonblack-fraction', type=float, default=.02)
    parser.add_argument('--maximum-dark-p95', type=float, default=.025)
    parser.add_argument('--max-candidate-images', type=int, default=16)
    parser.add_argument('--context-frames', type=int, default=1)
    parser.add_argument('--context-stride', type=int, default=5)
    parser.add_argument('--inspect-frame', type=int, action='append', default=[])
    parser.add_argument('--known-negative-frame', type=int, action='append', default=[])
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    if args.max_candidate_images < 1 or args.context_frames < 0 or args.context_stride < 1:
        parser.error('Candidate count must be positive and context frames nonnegative.')
    if not 0 <= args.black_threshold <= 255 or not 0 <= args.minimum_nonblack_fraction <= 1 or not 0 <= args.maximum_dark_p95 <= 1:
        parser.error('Pixel thresholds are outside their valid ranges.')
    if not math.isfinite(args.expected_clearance) or not math.isfinite(args.clearance_tolerance) or args.clearance_tolerance < 0:
        parser.error('Expected clearance and tolerance must be finite, and tolerance nonnegative.')
    if args.surface_start_stage and args.surface_start_frame is not None:
        parser.error('Specify a surface-start stage OR frame, not both.')
    return scan(args)


if __name__ == '__main__':
    sys.exit(main())
