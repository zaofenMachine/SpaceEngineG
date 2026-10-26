"""Package the already-built, verified Windows explorer without Unity debug folders."""
import hashlib
import json
import pathlib
import subprocess
import zipfile

ROOT=pathlib.Path(__file__).resolve().parent.parent
BUILD=ROOT/'Builds'/'Windows'
ARCHIVE=ROOT/'Builds'/'SpaceEngineG-0.1.0-windows-x64.zip'

def digest(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda:f.read(1024*1024),b''):h.update(block)
    return h.hexdigest()

if __name__=='__main__':
    report=json.loads((ROOT/'Artifacts'/'Player'/'runtime-verification.json').read_text(encoding='utf-8-sig'))
    if not report['passed']:raise RuntimeError('The standalone acceptance route must pass before packaging.')
    if (ROOT/'Artifacts'/'Player'/'exit-code.txt').read_text().strip()!='0':raise RuntimeError('Player did not exit successfully.')
    for stage in ('01-interstellar','02-orbit','03-atmosphere','04-low-altitude','05-surface','06-second-system'):
        if not (ROOT/'Artifacts'/'Player'/(stage+'.png')).exists():raise RuntimeError('Missing rendered screenshot '+stage)
    for name in ('SpaceEngineG.exe','UnityPlayer.dll','SpaceEngineG_Data/globalgamemanagers'):
        if not (BUILD/name).is_file():raise RuntimeError('Incomplete Unity player: '+name)
    files=[p for p in BUILD.rglob('*') if p.is_file() and not any('DoNotShip' in part or 'ButDontShipItWithYourGame' in part for part in p.parts) and p.suffix.lower()!='.pdb']
    with zipfile.ZipFile(ARCHIVE,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        for path in files:archive.write(path,'SpaceEngineG/'+path.relative_to(BUILD).as_posix())
        archive.writestr('SpaceEngineG/START-HERE.txt','SpaceEngineG 0.1.0\n\nRun SpaceEngineG.exe.\nRMB: look; WASD: fly; Q/E: down/up; wheel: speed; Shift: boost.\nG: continuous descent; R: ascent; F: orbit view; Tab: next planet; N: explicit star-system jump.\nF5/F9: save/restore; Esc: cancel autopilot.\nFull controls are available in FLIGHT CONTROLS inside the program.\n')
    with zipfile.ZipFile(ARCHIVE) as archive:
        bad=archive.testzip()
        if bad:raise RuntimeError('Corrupt ZIP entry '+bad)
    revision=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    manifest={'version':'0.1.0','sourceRevision':revision,'buildGuid':report['buildGuid'],'verifiedGpu':report['gpu'],
        'graphicsApi':report['graphicsApi'],'fileCount':len(files),'uncompressedBytes':sum(p.stat().st_size for p in files),
        'archive':'Builds/'+ARCHIVE.name,'archiveBytes':ARCHIVE.stat().st_size,'archiveSha256':digest(ARCHIVE),
        'executableSha256':digest(BUILD/'SpaceEngineG.exe'),'unityPlayerSha256':digest(BUILD/'UnityPlayer.dll'),
        'runtimeAssemblySha256':{name:digest(BUILD/'SpaceEngineG_Data'/'Managed'/(name+'.dll')) for name in ('SpaceEngine.Core','SpaceEngine.Terrain','SpaceEngine.Rendering','SpaceEngine.Runtime')},
        'notes':'Source revision identifies the committed runtime used by this local build. Unity-generated debug folders and PDB files remain in the build directory but are excluded from the distributable ZIP.'}
    (ROOT/'Artifacts'/'Player'/'package-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(manifest,ensure_ascii=False,indent=2))
