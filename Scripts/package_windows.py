"""Package the verified Windows player; preserve previous releases and evidence."""
import hashlib
import json
import pathlib
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
VERSION = '0.5.0'
BUILD = ROOT / 'Builds' / ('Windows-v' + VERSION)
OUT = ROOT / 'Artifacts' / 'V050'
ARCHIVE = ROOT / 'Builds' / ('SpaceEngineG-' + VERSION + '-windows-x64.zip')


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def acceptance(folder, filename, collection, stage_key):
    directory = OUT / folder
    report = load(directory / filename)
    if not report['passed'] or report['errors']:
        raise RuntimeError('Standalone acceptance failed: ' + folder)
    if (directory / 'exit-code.txt').read_text().strip() != '0':
        raise RuntimeError('Player did not exit successfully: ' + folder)
    for stage in report[collection]:
        screenshot = directory / (stage[stage_key] + '.png')
        if not screenshot.is_file() or screenshot.stat().st_size < 10000:
            raise RuntimeError('Missing or empty rendered screenshot: ' + str(screenshot))
    return report


def explorer_acceptance():
    directory = OUT / 'PlayerExplorer'
    report = load(directory / 'explorer-verification.json')
    if not report['passed'] or report['errors'] or (directory / 'exit-code.txt').read_text().strip() != '0':
        raise RuntimeError('Standalone explorer/continuous descent acceptance failed.')
    if report['stationaryChanges'] or report['stationaryFrames'] < 100 or report['holdSeconds'] < 8:
        raise RuntimeError('Both actual stationary holds must preserve settled terrain.')
    if report['returnedPositionError'] >= .01 or len(report['frames']) < 100:
        raise RuntimeError('Missing continuous return flight/position evidence.')
    if len(report['screenshots']) < 10:
        raise RuntimeError('Missing explorer and wall-time flight screenshots.')
    for name in report['screenshots']:
        screenshot = directory / name
        if not screenshot.is_file() or screenshot.stat().st_size < 10000:
            raise RuntimeError('Missing explorer rendered screenshot: ' + name)
    capture = load(directory / 'capture-metadata.json')
    if not capture['passed'] or capture['durationSeconds'] < 20 or capture.get('backend') != 'Unity framebuffer ReadPixels + rawvideo pipe':
        raise RuntimeError('Missing actual game framebuffer continuous video capture.')
    if capture['fullDecodeExitCode'] or capture['encoderExitCode'] or capture['decodedFrames'] != capture['native']['encodedFrames']:
        raise RuntimeError('Incomplete native framebuffer video stream.')
    comparisons=capture.get('stagePngComparisons', [])
    if len(comparisons)<5 or not all(item['passed'] for item in comparisons):
        raise RuntimeError('Video must match the actual 06-09 screenshot frames in content and orientation.')
    if (directory / 'descent.mp4').stat().st_size < 1000000:
        raise RuntimeError('Empty continuous flight video.')
    return report, capture


if __name__ == '__main__':
    flight = acceptance('PlayerFlight', 'runtime-verification.json', 'samples', 'stage')
    galaxy = acceptance('PlayerGalaxy', 'galaxy-verification.json', 'stages', 'name')
    explorer, capture = explorer_acceptance()
    details=load(OUT / 'PlayerDetails/details-verification.json')
    if not details['passed'] or details['errors'] or (OUT / 'PlayerDetails/exit-code.txt').read_text().strip() != '0':
        raise RuntimeError('Details acceptance must pass.')
    if len(details['screenshots'])<21 or not all((OUT / 'PlayerDetails' / name).is_file() for name in details['screenshots']):
        raise RuntimeError('Details screenshot coverage is incomplete.')
    if flight['applicationVersion'] != VERSION or galaxy['version'] != VERSION or explorer['version'] != VERSION or details['version'] != VERSION:
        raise RuntimeError('Evidence belongs to another player version.')
    if len({flight['buildGuid'],galaxy['buildGuid'],explorer['buildGuid'],details['buildGuid']}) != 1 or not flight['buildGuid'].strip('0'):
        raise RuntimeError('All four routes must use the same standalone build.')
    for mode in ('editmode', 'playmode'):
        if load(OUT / (mode + '-tests.json'))['data']['status'] != 'succeeded':
            raise RuntimeError('Unity tests must pass before packaging: ' + mode)
        final = ET.parse(OUT / ('final-' + mode + '.xml')).getroot()
        if final.get('result') != 'Passed' or int(final.get('failed', '1')):
            raise RuntimeError('Final source CLI tests must pass before packaging: ' + mode)
    for path in ('shaders.json', 'atmosphere-pixels.json', 'CelestialPixels/celestial-pixels.json',
                 'TerrainPixels/terrain-pixels.json', 'CloudPixels/cloud-pixels.json',
                 'GroundPixels/ground-pixels.json', 'GasHorizonPixels/gas-horizon-pixels.json',
                 'GroundAtmospherePixels/ground-pixels.json',
                 'GalaxyPixels/galaxy-pixels.json', 'GalaxyClarity/galaxy-clarity.json',
                 'TerrainLodPixels/terrain-lod-pixels.json', 'ChineseGlyphPixels/glyph-pixels.json', 'LoadingPixels/loading-pixels.json'):
        if not load(OUT / path)['passed']:
            raise RuntimeError('Graphics validation must pass: ' + path)
    if galaxy['movingViewFrames'] < 120 or galaxy['movingViewRecomputations'] < 118:
        raise RuntimeError('Galaxy performance evidence must include uncached moving-view frames.')
    for name in ('SpaceEngineG.exe', 'UnityPlayer.dll', 'SpaceEngineG_Data/globalgamemanagers',
                 'ThirdPartyNotices/OFL-NotoSansCJK.txt', 'ThirdPartyNotices/NOTICE-NotoSansCJK.txt'):
        if not (BUILD / name).is_file():
            raise RuntimeError('Incomplete Unity player: ' + name)
    revision = (OUT / 'source-revision.txt').read_text().strip()
    subprocess.check_call(['git', 'cat-file', '-e', revision + '^{commit}'], cwd=ROOT)
    files = sorted(p for p in BUILD.rglob('*') if p.is_file()
                   and not any('DoNotShip' in part or 'ButDontShipItWithYourGame' in part for part in p.parts)
                   and p.suffix.lower() != '.pdb')
    with zipfile.ZipFile(ARCHIVE, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in files:
            archive.write(path, 'SpaceEngineG/' + path.relative_to(BUILD).as_posix())
        archive.writestr('SpaceEngineG/START-HERE.txt',
            'SpaceEngineG ' + VERSION + '\n\n运行 SpaceEngineG.exe。界面与帮助为中文。\n'
            '右键拖动观察；WASD 飞行；Q/E 升降；Z/X 翻滚；滚轮或 +/- 调速；Shift 加速。\n'
            '单击天体选择；F3 搜索/筛选；C 居中；Y 跟踪；Alt+右键环绕目标。\n'
            'G 连续飞往并降落；R 连续上升；F 显式跳转观察；Tab 下一颗行星/卫星。\n'
            'O 轨道开关；L 标签开关；V 恒星系总览；F2/I 天体详情（概览、轨道、环境、系统与来源）。\n'
            'F6 书签；Ctrl+B 保存书签；F7 截图。\n'
            'T 恒星；N 跳转邻近恒星系；M 银河；Home 起点；B 中央视界参考面。\n'
            'F5/F9 保存/恢复飞行；Esc 关闭面板或取消导航；Space 暂停时间；[ ] 调整时间倍率。\n'
            '搜索涵盖当前恒星系和已解析邻域，可用单元地址扩展。\n'
            '气态/冰巨行星没有固体降落面。新世界使用生成版本3；原flight-v2存档按旧生成器恢复，新存档写flight-v3。\n'
            '第1代存档保留，请使用0.1.0读取。字体授权见ThirdPartyNotices。\n')
        archive.writestr('SpaceEngineG/SOURCE-REVISION.txt', revision + '\n')
    with zipfile.ZipFile(ARCHIVE) as archive:
        bad = archive.testzip()
        if bad:
            raise RuntimeError('Corrupt ZIP entry ' + bad)
    manifest = {
        'version': VERSION, 'sourceRevision': revision, 'buildGuid': flight['buildGuid'],
        'verifiedGpu': flight['gpu'], 'graphicsApi': flight['graphicsApi'],
        'fileCount': len(files), 'uncompressedBytes': sum(p.stat().st_size for p in files),
        'archive': 'Builds/' + ARCHIVE.name, 'archiveBytes': ARCHIVE.stat().st_size,
        'archiveSha256': digest(ARCHIVE), 'executableSha256': digest(BUILD / 'SpaceEngineG.exe'),
        'unityPlayerSha256': digest(BUILD / 'UnityPlayer.dll'),
        'runtimeAssemblySha256': {name: digest(BUILD / 'SpaceEngineG_Data' / 'Managed' / (name + '.dll'))
            for name in ('SpaceEngine.Core', 'SpaceEngine.Terrain', 'SpaceEngine.Rendering', 'SpaceEngine.Runtime')},
        'evidence': ['Artifacts/V050/PlayerFlight', 'Artifacts/V050/PlayerGalaxy', 'Artifacts/V050/PlayerExplorer', 'Artifacts/V050/PlayerDetails'],
        'continuousVideo': {'path': 'Artifacts/V050/PlayerExplorer/descent.mp4',
            'sha256': digest(OUT / 'PlayerExplorer/descent.mp4'), 'durationSeconds': capture['durationSeconds'],
            'stationaryFrames': explorer['stationaryFrames'], 'stationaryChanges': explorer['stationaryChanges'],
            'returnedPositionErrorMetres': explorer['returnedPositionError']},
        'notes': 'Source revision recorded before build. Screenshot review is documented separately. '
                 'Unity debug folders and PDBs remain locally but are excluded from this archive.'}
    (OUT / 'package-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest, ensure_ascii=False, indent=2))
