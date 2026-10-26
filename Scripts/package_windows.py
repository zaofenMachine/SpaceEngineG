"""Package the verified Windows player; preserve previous releases and evidence."""
import hashlib
import json
import pathlib
import subprocess
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
VERSION = '0.3.0'
BUILD = ROOT / 'Builds' / ('Windows-v' + VERSION)
OUT = ROOT / 'Artifacts' / 'V030'
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


if __name__ == '__main__':
    flight = acceptance('PlayerFlight', 'runtime-verification.json', 'samples', 'stage')
    galaxy = acceptance('PlayerGalaxy', 'galaxy-verification.json', 'stages', 'name')
    if flight['applicationVersion'] != VERSION or galaxy['version'] != VERSION:
        raise RuntimeError('Evidence belongs to another player version.')
    if flight['buildGuid'] != galaxy['buildGuid'] or not flight['buildGuid'].strip('0'):
        raise RuntimeError('Both routes must use the same standalone build.')
    for mode in ('editmode', 'playmode'):
        if load(OUT / (mode + '-tests.json'))['data']['status'] != 'succeeded':
            raise RuntimeError('Unity tests must pass before packaging: ' + mode)
    for path in ('shaders.json', 'atmosphere-pixels.json', 'CelestialPixels/celestial-pixels.json',
                 'TerrainPixels/terrain-pixels.json', 'CloudPixels/cloud-pixels.json',
                 'GroundPixels/ground-pixels.json', 'GasHorizonPixels/gas-horizon-pixels.json',
                 'GroundAtmospherePixels/ground-pixels.json',
                 'GalaxyPixels/galaxy-pixels.json'):
        if not load(OUT / path)['passed']:
            raise RuntimeError('Graphics validation must pass: ' + path)
    if galaxy['movingViewFrames'] < 120 or galaxy['movingViewRecomputations'] < 118:
        raise RuntimeError('Galaxy performance evidence must include uncached moving-view frames.')
    for name in ('SpaceEngineG.exe', 'UnityPlayer.dll', 'SpaceEngineG_Data/globalgamemanagers'):
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
            'SpaceEngineG ' + VERSION + '\n\nRun SpaceEngineG.exe.\n'
            'RMB: look; WASD: fly; Q/E: down/up; wheel: speed; Shift: boost.\n'
            'G: continuous approach; R: ascent; F: frame body; Tab: next planet/moon.\n'
            'T: frame star; N: nearby occupied system; M: galaxy overview; Home: home; B: central horizon.\n'
            'F5/F9: save/restore generation-v2 flight; Esc: cancel navigation.\n'
            'Gas/ice giants have no solid landing surface. Galaxy kernels represent 3D populations.\n'
            'Legacy generation-v1 saves are retained for the 0.1.0 program.\n'
            'Full controls are available in FLIGHT CONTROLS inside the program.\n')
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
        'evidence': ['Artifacts/V030/PlayerFlight', 'Artifacts/V030/PlayerGalaxy'],
        'notes': 'Source revision recorded before build. Screenshot review is documented separately. '
                 'Unity debug folders and PDBs remain locally but are excluded from this archive.'}
    (OUT / 'package-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest, ensure_ascii=False, indent=2))
