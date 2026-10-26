"""Decode and sample the actual Explorer recording; do not claim manual frame review.

    python Scripts/review_video.py
    python Scripts/review_video.py --video path/to/descent.mp4 --report path/to/report.json

Requires Pillow and numpy. Uses --ffmpeg, PATH, or imageio_ffmpeg's bundled binary.
Every video frame is decoded for integrity/counts. Only approximately one frame
per second, plus two exact recorded screenshot frames, is retained for review.
"""
import argparse
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
PLAYER = ROOT / 'Artifacts/V040/PlayerExplorer'
DEFAULT_OUTPUT = ROOT / 'Artifacts/V040/VideoReview'
DEFAULT_FRAMES = ROOT / 'Artifacts/Local/VideoFrames'
# At 1600x900 this is x=512..1040, y=504..774: below the crosshair,
# between the side panels, and above the footer. The descent hides world labels.
CROP = (.32, .56, .65, .86)
HOLD_SCREENSHOTS = {
    'first-hold': '07-first-surface-hold.png',
    'second-hold': '09-return-surface-hold.png',
}
HIDDEN = getattr(subprocess, 'CREATE_NO_WINDOW', 0) if os.name == 'nt' else 0


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')


def relative(path):
    path = Path(path).resolve()
    return path.relative_to(ROOT).as_posix() if path.is_relative_to(ROOT) else str(path)


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def ffmpeg_binary(requested):
    if requested:
        return str(Path(requested).resolve())
    found = shutil.which('ffmpeg')
    if found:
        return found
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except ImportError as error:
        raise RuntimeError('Provide --ffmpeg or install imageio_ffmpeg.') from error


def run_ffmpeg(binary, arguments, log, timeout=600):
    command = [binary, '-hide_banner', '-nostdin', '-y', *arguments]
    result = subprocess.run(command, capture_output=True, text=True,
                            encoding='utf-8', errors='replace', timeout=timeout,
                            creationflags=HIDDEN)
    log.write_text(result.stderr, encoding='utf-8')
    if result.returncode:
        raise RuntimeError('FFmpeg failed (%s); inspect %s' % (result.returncode, log))
    return result


def decode_all(binary, video, directory):
    result = run_ffmpeg(binary, ['-xerror', '-i', str(video), '-map', '0:v:0',
        '-an', '-sn', '-dn', '-progress', 'pipe:1', '-nostats', '-f', 'null', os.devnull],
        directory / 'full-decode.log')
    progress = {}
    for line in result.stdout.splitlines():
        if '=' in line:
            key, value = line.split('=', 1)
            progress[key.strip()] = value.strip()
    count = int(progress.get('frame', 0))
    duration = float(progress.get('out_time_us', 0)) / 1e6
    stream = next((line.strip() for line in result.stderr.splitlines()
                   if 'Stream #' in line and 'Video:' in line), '')
    dimensions = re.search(r'\b(\d{2,6})x(\d{2,6})\b', stream)
    fps = re.search(r'([\d.]+) fps\b', stream)
    if count <= 0 or duration <= 0 or not dimensions:
        raise RuntimeError('Full decode produced no measurable video frames/duration/dimensions.')
    return {'exitCode': result.returncode, 'fullFrameCount': count,
        'durationSeconds': duration, 'width': int(dimensions[1]), 'height': int(dimensions[2]),
        'nominalFramesPerSecond': float(fps[1]) if fps else None,
        'decodedFramesPerSecond': count / duration, 'streamDescription': stream,
        'durationSource': 'FFmpeg full-decode final output timestamp; not wall-clock run time',
        'progressEnded': progress.get('progress') == 'end'}


def epoch(value):
    if isinstance(value, (int, float)) and math.isfinite(value):
        return float(value)
    if not isinstance(value, str) or not value:
        return None
    try:
        # Unity serializes seven fractional digits; datetime accepts this too.
        instant = dt.datetime.fromisoformat(value.replace('Z', '+00:00'))
        if instant.tzinfo is None:
            return None  # Do not silently interpret a local time as UTC.
        return instant.timestamp()
    except ValueError:
        return None


def clock_mapping(report, metadata):
    native = metadata.get('native') or {}
    candidates = [
        ('native.firstFrameEpoch', native.get('firstFrameEpoch'), True),
        ('native.firstFrameUtc', native.get('firstFrameUtc'), True),
        ('firstFrameEpoch', metadata.get('firstFrameEpoch'), True),
        ('firstFrameUtc', metadata.get('firstFrameUtc'), True),
        ('native.captureStartedAtEpoch', native.get('captureStartedAtEpoch'), False),
        ('native.captureStartedAtUtc', native.get('captureStartedAtUtc'), False),
        ('native.startEpoch', native.get('startEpoch'), False),
        ('native.startUtc', native.get('startUtc'), False),
        ('captureStartedAtEpoch', metadata.get('captureStartedAtEpoch'), False),
        ('captureStartedAtUtc', metadata.get('captureStartedAtUtc'), False),
    ]
    source, start, first_frame = None, None, False
    for key, value, exact in candidates:
        parsed = epoch(value)
        if parsed is not None:
            source, start, first_frame = key, parsed, exact
            break
    route = epoch(report.get('utc'))
    if start is None or route is None:
        raise RuntimeError('A timezone-qualified report.utc and recording start/first-frame clock anchor are required.')
    uncertainty = native.get('timestampUncertaintySeconds', metadata.get('timestampUncertaintySeconds'))
    uncertainty = float(uncertainty) if isinstance(uncertainty, (int, float)) else (.1 if first_frame else 1.)
    if not math.isfinite(uncertainty) or uncertainty < 0:
        raise RuntimeError('Invalid recording clock uncertainty.')
    return {'videoPtsZeroEpoch': start, 'reportElapsedZeroEpoch': route,
        'reportToVideoOffsetSeconds': route - start, 'anchorSource': source,
        'anchorIsFirstRecordedFrame': first_frame,
        'estimatedTimestampUncertaintySeconds': uncertainty,
        'formula': 'videoSeconds = report.utc epoch + frames[].elapsed - videoPtsZeroEpoch',
        'limitation': 'Clock alignment is approximate; exact native screenshot videoFrameIndex takes priority.'}


def phase_groups(report, mapping):
    groups = []
    for frame in report.get('frames', []):
        elapsed = frame.get('elapsed')
        if not isinstance(elapsed, (int, float)) or not math.isfinite(elapsed):
            continue
        name = str(frame.get('phase', 'unknown'))
        if not groups or groups[-1]['phase'] != name:
            groups.append({'phase': name, 'elapsedStart': float(elapsed), 'elapsedEnd': float(elapsed),
                           'reportSamples': 0, 'settledSamples': 0, 'switches': 0})
        group = groups[-1]
        group['elapsedEnd'] = float(elapsed)
        group['reportSamples'] += 1
        group['settledSamples'] += bool(frame.get('settled'))
        group['switches'] += int(frame.get('switches', 0))
    for group in groups:
        group['videoStartSeconds'] = group['elapsedStart'] + mapping['reportToVideoOffsetSeconds']
        group['videoEndSeconds'] = group['elapsedEnd'] + mapping['reportToVideoOffsetSeconds']
    return groups


def phase_at(seconds, groups):
    for group in groups:
        if group['videoStartSeconds'] <= seconds <= group['videoEndSeconds']:
            return group['phase']
    return 'between-report-samples'


def crop_box(size):
    width, height = size
    return tuple(round(value * dimension) for value, dimension in
                 zip(CROP, (width, height, width, height)))


def pixels(image, crop=False):
    if crop:
        image = image.crop(crop_box(image.size))
    return np.asarray(image.convert('RGB'), dtype=np.uint8)


def image_metrics(array):
    data = array.astype(np.float32) / 255
    luminance = data @ np.array([.2126, .7152, .0722], dtype=np.float32)
    return {'rgbMinimum8Bit': array.min(axis=(0, 1)).tolist(),
        'rgbMaximum8Bit': array.max(axis=(0, 1)).tolist(),
        'rgbMean8Bit': array.mean(axis=(0, 1)).tolist(),
        'rgbStd8Bit': array.std(axis=(0, 1)).tolist(),
        'luminanceP01': float(np.percentile(luminance, 1)),
        'luminanceP99': float(np.percentile(luminance, 99)),
        'nonBlackPixelFraction': float(np.mean(array.max(axis=2) > 3)),
        'nonWhitePixelFraction': float(np.mean(array.min(axis=2) < 252))}


def difference(first, second):
    change = (first.astype(np.float32) - second.astype(np.float32)) / 255
    absolute = np.abs(change)
    mse = float(np.mean(change * change))
    return {'meanAbsoluteRgb': float(absolute.mean()), 'rootMeanSquareRgb': math.sqrt(mse),
        'maximumAbsoluteRgb': float(absolute.max()),
        'p99AbsoluteRgb': float(np.percentile(absolute, 99)),
        'pixelFractionAnyChannelOver2Of255': float(np.mean(absolute.max(axis=2) > 2 / 255)),
        'psnrDb': -10 * math.log10(mse) if mse > 0 else None,
        'identicalAfterDecode': mse == 0}


def sample_seconds(binary, video, directory, review, groups):
    # select records the original source PTS with showinfo; an fps filter would
    # replace it with a synthetic integer-second timestamp on variable-rate clips.
    result = run_ffmpeg(binary, ['-i', str(video), '-map', '0:v:0', '-an',
        '-vf', "select='isnan(prev_selected_t)+gte(t-prev_selected_t,1)',showinfo",
        '-fps_mode', 'vfr', '-start_number', '0', str(directory / 'frame-%05d.png')],
        review / 'sample-extraction.log')
    times = [float(value) for value in re.findall(r'\bpts_time:([\d.eE+\-]+)', result.stderr)]
    files = sorted(directory.glob('frame-*.png'))
    if not files or len(times) != len(files):
        raise RuntimeError('Selected-frame PTS count differs from extracted PNG count.')
    samples = []
    for path, seconds in zip(files, times):
        with Image.open(path) as image:
            samples.append({'videoSeconds': seconds, 'estimatedPhase': phase_at(seconds, groups),
                'file': relative(path), 'wholeImage': image_metrics(pixels(image)),
                'terrainCrop': image_metrics(pixels(image, True))})
    return files, samples


def timecode(seconds):
    millis = round(max(0, seconds) * 1000)
    seconds, ms = divmod(millis, 1000)
    minutes, sec = divmod(seconds, 60)
    hour, minute = divmod(minutes, 60)
    return '%02d:%02d:%02d.%03d' % (hour, minute, sec, ms)


def contact_sheets(files, samples, directory):
    font = ImageFont.load_default(size=17)
    sheets = []
    for start in range(0, len(files), 16):
        sheet = Image.new('RGB', (1600, 900), 'black')
        draw = ImageDraw.Draw(sheet)
        for cell, (path, sample) in enumerate(zip(files[start:start + 16], samples[start:start + 16])):
            left, top = cell % 4 * 400, cell // 4 * 225
            with Image.open(path) as image:
                thumb = ImageOps.contain(image.convert('RGB'), (400, 225), Image.Resampling.LANCZOS)
            sheet.paste(thumb, (left + (400 - thumb.width) // 2, top + (225 - thumb.height) // 2))
            caption = timecode(sample['videoSeconds']) + ' | ' + sample['estimatedPhase']
            draw.rectangle((left, top, left + 399, top + 26), fill='black')
            draw.text((left + 6, top + 4), caption, font=font, fill=(235, 245, 255))
        path = directory / ('contact-sheet-%03d.png' % (start // 16 + 1))
        sheet.save(path)
        sheets.append({'file': relative(path), 'firstSample': start,
            'sampleCount': min(16, len(files) - start), 'columns': 4, 'rows': 4,
            'cellWidth': 400, 'cellHeight': 225})
    return sheets


def summarize_hold(group, files, samples, uncertainty):
    # Exclude phase boundaries and clock uncertainty, then compare only terrain.
    trim = max(.25, uncertainty + .1)
    indices = [i for i, sample in enumerate(samples)
               if group['videoStartSeconds'] + trim <= sample['videoSeconds'] <= group['videoEndSeconds'] - trim]
    result = {**group, 'boundaryTrimSeconds': trim, 'sampleIndices': indices,
        'sampleSeconds': [samples[i]['videoSeconds'] for i in indices], 'consecutiveCropDifferences': []}
    reference = previous = None
    for index in indices:
        with Image.open(files[index]) as image:
            current = pixels(image, True)
        if reference is None:
            reference = current
        if previous is not None:
            result['consecutiveCropDifferences'].append(difference(previous, current))
        previous = current
    result['firstToLastCropDifference'] = difference(reference, previous) if reference is not None else None
    result['maximumConsecutiveCropMae'] = max(
        (item['meanAbsoluteRgb'] for item in result['consecutiveCropDifferences']), default=None)
    result['interpretation'] = 'Sampled RGB changes include H.264 quantization; this is neither an all-frame check nor a visual pass/fail.'
    return result


def stage_for(metadata, screenshot):
    stages = (metadata.get('native') or {}).get('stages', metadata.get('stages', []))
    for stage in stages:
        name = str(stage.get('screenshot', '')).replace('\\', '/').rsplit('/', 1)[-1]
        if Path(name).stem == Path(screenshot).stem:
            return stage
    return None


def comparison_sheet(reference, video, path, heading):
    # Preserve full context above a 2x nearest-pixel terrain enlargement. This
    # exposes flips/channel swaps without confusing them with changing HUD text.
    font = ImageFont.load_default(size=19)
    reference_crop = reference.crop(crop_box(reference.size))
    video_crop = video.crop(crop_box(video.size))
    width = max(1600, reference_crop.width * 4)
    context_height = round(800 * reference.height / reference.width)
    canvas = Image.new('RGB', (width, 72 + context_height + reference_crop.height * 2), (8, 12, 16))
    draw = ImageDraw.Draw(canvas)
    draw.text((12, 8), 'PNG reference | ' + heading, font=font, fill='white')
    draw.text((width // 2 + 12, 8), 'Decoded RGB video frame', font=font, fill='white')
    for index, image in enumerate((reference, video)):
        context = ImageOps.contain(image, (width // 2, context_height), Image.Resampling.LANCZOS)
        canvas.paste(context, (index * (width // 2), 36))
    crop_top = 72 + context_height
    draw.text((12, crop_top - 28), 'Same HUD-free terrain rectangle, 2x pixels', font=font, fill='white')
    for index, crop in enumerate((reference_crop, video_crop)):
        canvas.paste(crop.resize((crop.width * 2, crop.height * 2), Image.Resampling.NEAREST),
                     (index * (width // 2), crop_top))
    canvas.save(path)


def compare_screenshot(binary, video, native, group, screenshot, report_path, directory, decoded):
    reference_path = report_path.parent / screenshot
    if not reference_path.is_file():
        raise RuntimeError('Missing same-run screenshot: ' + str(reference_path))
    stage = stage_for(native, screenshot)
    index = stage.get('videoFrameIndex') if stage else None
    fps = (native.get('native') or {}).get('framesPerSecond') or decoded['nominalFramesPerSecond']
    if isinstance(index, int) and not isinstance(index, bool) and 0 <= index < decoded['fullFrameCount']:
        source = 'native.stages.videoFrameIndex (exact recorded frame index)'
        seconds = index / float(fps) if fps else None
        arguments = ['-i', str(video), '-vf', 'select=eq(n\\,%s)' % index, '-fps_mode', 'vfr']
    else:
        source = 'estimated report hold end minus 0.1s; screenshot is written just after the hold'
        seconds = max(0, min(decoded['durationSeconds'] - .05, group['videoEndSeconds'] - .1))
        arguments = ['-ss', '%.6f' % seconds, '-i', str(video)]
        index = None
    frame_path = directory / (Path(screenshot).stem + '-video.png')
    run_ffmpeg(binary, [*arguments, '-frames:v', '1', str(frame_path)],
               directory / (Path(screenshot).stem + '-extract.log'))
    with Image.open(reference_path) as reference_image, Image.open(frame_path) as frame_image:
        reference = reference_image.convert('RGB')
        frame = frame_image.convert('RGB')
        original_video_size = frame.size
        normalized = frame.size != reference.size
        if normalized:
            frame = frame.resize(reference.size, Image.Resampling.BILINEAR)
        target, actual = pixels(reference, True), pixels(frame, True)
        # A whole-frame vertical flip moves sky into the ground crop. Flip the
        # complete decoded image before applying the fixed reference rectangle.
        flipped_crop = pixels(frame.transpose(Image.Transpose.FLIP_TOP_BOTTOM), True)
        transforms = {'normalRgb': actual, 'verticalFlip': flipped_crop,
                      'redBlueSwap': actual[..., ::-1], 'verticalFlipAndRedBlueSwap': flipped_crop[..., ::-1]}
        errors = {name: difference(target, candidate) for name, candidate in transforms.items()}
        ranked = sorted(errors, key=lambda name: errors[name]['meanAbsoluteRgb'])
        sheet = directory / (Path(screenshot).stem + '-comparison.png')
        comparison_sheet(reference, frame, sheet, screenshot)
        return {'phase': group['phase'], 'screenshot': relative(reference_path),
            'videoFrame': relative(frame_path), 'comparisonSheet': relative(sheet),
            'selectionSource': source, 'videoFrameIndex': index, 'videoSeconds': seconds,
            'offsetFromEstimatedHoldEndSeconds': seconds - group['videoEndSeconds'] if seconds is not None else None,
            'referenceSize': list(reference.size), 'videoSize': list(original_video_size),
            'videoResizedForComparison': normalized, 'cropPixels': list(crop_box(reference.size)),
            'referenceCrop': image_metrics(target), 'videoCrop': image_metrics(actual),
            'orientationAndColorDiagnostics': errors, 'lowestErrorTransform': ranked[0],
            'bestVersusSecondBestMaeGap': errors[ranked[1]]['meanAbsoluteRgb'] - errors[ranked[0]]['meanAbsoluteRgb'],
            'interpretation': 'Lossy video is not expected to equal PNG. Low-texture crops can make orientation inconclusive. Open both full context and enlarged crops; no human visual result is inferred.'}


def review(args):
    video, report_path, metadata_path = (Path(value).resolve() for value in (args.video, args.report, args.metadata))
    for path in (video, report_path, metadata_path):
        if not path.is_file():
            raise FileNotFoundError(path)
    report, metadata = read_json(report_path), read_json(metadata_path)
    sha = digest(video)
    stamp = dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ')
    run_id = stamp + '-' + sha[:8]
    output = Path(args.output).resolve()
    directory = output / run_id
    frames = Path(args.frames).resolve() / run_id
    directory.mkdir(parents=True, exist_ok=False)
    frames.mkdir(parents=True, exist_ok=False)
    result = {'utc': dt.datetime.now(dt.timezone.utc).isoformat(), 'scope':
        'Full-stream decode plus approximately one RGB sample per second and two same-run screenshot comparisons. Metrics and contact sheets are sampling aids; no human frame-by-frame review is claimed.',
        'manualFrameByFrameReviewed': False, 'sampledVisualMetricsOnly': True,
        'video': relative(video), 'videoBytes': video.stat().st_size, 'videoSha256': sha,
        'sourceReport': relative(report_path), 'sourceCaptureMetadata': relative(metadata_path),
        'reportVersion': report.get('version'), 'reportBuildGuid': report.get('buildGuid'),
        'captureBackend': metadata.get('captureBackend'), 'captureMetadataPassed': metadata.get('passed'),
        'terrainCropNormalized': list(CROP), 'cropScope':
        'Fixed rectangle excludes the default 16:9 HUD side panels, target ribbon, footer and crosshair. It is not a generic UI/overlay detector.',
        'sampleDirectory': relative(frames), 'reviewDirectory': relative(directory),
        'pipelineSucceeded': False, 'failures': [], 'warnings': []}
    try:
        binary = ffmpeg_binary(args.ffmpeg)
        result['ffmpegExecutable'] = binary
        decoded = decode_all(binary, video, directory)
        result.update(decoded)
        result['structurallyNonemptyVideo'] = video.stat().st_size > 0 and decoded['fullFrameCount'] > 0
        mapping = clock_mapping(report, metadata)
        result['clockMapping'] = mapping
        groups = phase_groups(report, mapping)
        result['estimatedPhases'] = groups
        if not mapping['anchorIsFirstRecordedFrame']:
            result['warnings'].append('Only capture-start time is available; phase alignment may include startup/encoder latency.')
        files, samples = sample_seconds(binary, video, frames, directory, groups)
        result['samples'] = samples
        result['sampleCount'] = len(samples)
        result['sampleIntervalSeconds'] = 1
        result['samplingRule'] = 'First source frame, then first source frame at least one second after the previous selected source PTS; actual PTS recorded.'
        result['contactSheets'] = contact_sheets(files, samples, directory)
        result['sampledColorRange8Bit'] = {
            'minimum': [min(s['wholeImage']['rgbMinimum8Bit'][c] for s in samples) for c in range(3)],
            'maximum': [max(s['wholeImage']['rgbMaximum8Bit'][c] for s in samples) for c in range(3)],
            'note': 'RGB24 decoded values; FFmpeg performs the source video range/matrix conversion. This range alone does not establish correct exposure/color.'}
        holds = [group for group in groups if group['phase'] in HOLD_SCREENSHOTS]
        result['holdCropMetrics'] = [summarize_hold(group, files, samples,
            mapping['estimatedTimestampUncertaintySeconds']) for group in holds]
        result['screenshotComparisons'] = []
        for group in holds:
            result['screenshotComparisons'].append(compare_screenshot(binary, video, metadata, group,
                HOLD_SCREENSHOTS[group['phase']], report_path, directory, decoded))
        if {group['phase'] for group in holds} != set(HOLD_SCREENSHOTS):
            result['failures'].append('Report does not cover both stationary hold phases.')
        for hold in result['holdCropMetrics']:
            if len(hold['sampleIndices']) < 2:
                result['failures'].append('Fewer than two interior sampled crop frames for ' + hold['phase'])
        native = metadata.get('native') or {}
        expected = native.get('encodedFrames')
        if isinstance(expected, int) and expected != decoded['fullFrameCount']:
            result['failures'].append('Decoded frame count differs from native encodedFrames.')
        result['nativeSampling'] = {key: native.get(key) for key in
            ('framesPerSecond', 'encodedFrames', 'actualReadFrames', 'repeatedFrames', 'timestampUncertaintySeconds')}
        result['pipelineSucceeded'] = not result['failures']
    except Exception as error:
        result['failures'].append(type(error).__name__ + ': ' + str(error))
    result['interpretation'] = 'pipelineSucceeded concerns extraction/decode/coverage only. View contact sheets and comparison PNGs before accepting orientation, color, terrain stability or visual quality. Every-second images cannot exclude subsecond LOD pops.'
    write_json(directory / 'video-review.json', result)
    write_json(output / 'video-review.json', result)
    print(json.dumps({'pipelineSucceeded': result['pipelineSucceeded'], 'report': relative(output / 'video-review.json'),
        'samples': result.get('sampleCount', 0), 'fullFrameCount': result.get('fullFrameCount'),
        'failures': result['failures']}, ensure_ascii=False))
    return 0 if result['pipelineSucceeded'] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--video', default=str(PLAYER / 'descent.mp4'))
    parser.add_argument('--report', default=str(PLAYER / 'explorer-verification.json'))
    parser.add_argument('--metadata', default=str(PLAYER / 'capture-metadata.json'))
    parser.add_argument('--output', default=str(DEFAULT_OUTPUT))
    parser.add_argument('--frames', default=str(DEFAULT_FRAMES))
    parser.add_argument('--ffmpeg')
    return review(parser.parse_args())


if __name__ == '__main__':
    sys.exit(main())
