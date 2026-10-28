"""Validate the existing, isolated .validation-unity-v050 copy without using the main Editor.

    python Scripts/run_unity_batch.py --mode editmode
    python Scripts/run_unity_batch.py --mode playmode
    python Scripts/run_unity_batch.py --mode graphics
    python Scripts/run_unity_batch.py --mode build

This script never copies/synchronizes source, removes Unity lock files, or stops an
existing Editor. Assets/Packages/ProjectSettings must match before and after a run,
except the explicitly audited Unity-derived runtime-settings reference list.
GPU rendering remains enabled. Test success requires a fresh, genuinely Passed
NUnit XML, successful process exit and unchanged matching source manifests.
"""
import argparse
import ctypes
import datetime as dt
import difflib
import hashlib
import json
import os
import pathlib
import re
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET


ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'SpaceEngineG'
ISOLATED = ROOT / '.validation-unity-v050'
OUT = ROOT / 'Artifacts/V050'
LOCAL = ROOT / 'Artifacts/Local'
SOURCE_FOLDERS = ('Assets', 'Packages', 'ProjectSettings')
HIDDEN = getattr(subprocess, 'CREATE_NO_WINDOW', 0x08000000)
DERIVED_SETTINGS_PATH = 'Assets/Settings/UniversalRenderPipelineGlobalSettings.asset'
DERIVED_SETTINGS_FIELD = 'm_Settings.m_RuntimeSettings.m_List'
DERIVED_SETTINGS_REASON = ('Unity derives this Player runtime-settings reference list from m_SettingsList during '
                           'build and clears it in the Editor. Only this exact anchored list is normalized; '
                           'the settings definitions and every byte outside the list remain compared.')


def utc():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temporary.replace(path)


def normalize_generated_list(data, relative_path):
    """Recognize one exact Unity YAML subtree; unknown shapes retain their raw hash."""
    if relative_path != DERIVED_SETTINGS_PATH:
        return data, None
    lines = data.splitlines(keepends=True)
    settings = [i for i, line in enumerate(lines) if re.fullmatch(rb'  m_Settings:\r?\n', line)]
    rejected = {'field': DERIVED_SETTINGS_FIELD, 'recognized': False,
                'reason': 'Unknown or ambiguous YAML shape; no normalization was applied.'}
    if len(settings) != 1:
        return data, rejected
    parent_start = settings[0] + 1
    parent_end = next((i for i in range(parent_start, len(lines))
                       if re.match(rb' {0,2}[^ \t\r\n]', lines[i])), len(lines))
    runtime = [i for i in range(parent_start, parent_end)
               if re.fullmatch(rb'    m_RuntimeSettings:\r?\n', lines[i])]
    if len(runtime) != 1:
        return data, rejected
    start = runtime[0] + 1
    if start >= len(lines):
        return data, rejected
    if re.fullmatch(rb'      m_List: \[\]\r?\n', lines[start]):
        end = start + 1
    elif re.fullmatch(rb'      m_List:\r?\n', lines[start]):
        end = start + 1
        while end < len(lines) and re.fullmatch(rb'      - rid: -?\d+\r?\n', lines[end]):
            end += 1
        if end == start + 1:
            return data, rejected
    else:
        return data, rejected
    # A nested field, comment or unexpected continuation must never be swallowed.
    if end < len(lines) and not re.match(rb' {0,4}[^ \t\r\n]', lines[end]):
        return data, rejected
    if end > parent_end:
        return data, rejected
    original = b''.join(lines[start:end])
    newline = b'\r\n' if lines[runtime[0]].endswith(b'\r\n') else b'\n'
    canonical = b'      m_List: []' + newline
    normalized = b''.join(lines[:start]) + canonical + b''.join(lines[end:])
    return normalized, {'field': DERIVED_SETTINGS_FIELD, 'recognized': True,
                        'reason': DERIVED_SETTINGS_REASON,
                        'rawField': original.decode('ascii'),
                        'canonicalField': canonical.decode('ascii'),
                        'referenceCount': end - start - 1}


def digest(path, relative_path=None):
    before = path.stat()
    result = hashlib.sha256()
    generated_data = bytearray() if relative_path == DERIVED_SETTINGS_PATH else None
    with path.open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            result.update(block)
            if generated_data is not None:
                generated_data.extend(block)
    after = path.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise RuntimeError('Source changed while hashing: ' + str(path))
    raw_hash = result.hexdigest()
    comparison_hash = raw_hash
    normalization = None
    if generated_data is not None:
        normalized, normalization = normalize_generated_list(bytes(generated_data), relative_path)
        comparison_hash = hashlib.sha256(normalized).hexdigest()
    entry = {'bytes': after.st_size, 'sha256': comparison_hash,
             'rawSha256': raw_hash, 'comparisonSha256': comparison_hash}
    if normalization is not None:
        entry['generatedField'] = normalization
    return entry


def manifest(project):
    files = {}
    for folder in SOURCE_FOLDERS:
        directory = project / folder
        if not directory.is_dir():
            raise RuntimeError('Missing source directory: ' + str(directory))
        for base, directories, names in os.walk(directory, followlinks=False):
            directories.sort()
            for name in sorted(directories + names):
                candidate = pathlib.Path(base) / name
                if candidate.is_symlink() or (hasattr(candidate, 'is_junction') and candidate.is_junction()):
                    raise RuntimeError('Source manifest does not follow linked files/directories: ' + str(candidate))
            for name in sorted(names):
                path = pathlib.Path(base) / name
                if path.is_file():
                    relative_path = path.relative_to(project).as_posix()
                    files[relative_path] = digest(path, relative_path)
    packed = json.dumps({name: entry['comparisonSha256'] for name, entry in files.items()},
                        sort_keys=True, separators=(',', ':')).encode('utf-8')
    raw_packed = json.dumps({name: entry['rawSha256'] for name, entry in files.items()},
                            sort_keys=True, separators=(',', ':')).encode('utf-8')
    return {'sha256': hashlib.sha256(packed).hexdigest(), 'fileCount': len(files),
            'comparisonSha256': hashlib.sha256(packed).hexdigest(),
            'rawSha256': hashlib.sha256(raw_packed).hexdigest(),
            'bytes': sum(item['bytes'] for item in files.values()), 'files': files}


def differences(expected, actual):
    a, b = expected['files'], actual['files']
    return [{'path': path, 'kind': 'missing' if path not in b else 'extra' if path not in a else 'different',
             'expected': a.get(path), 'actual': b.get(path)}
            for path in sorted(a.keys() | b.keys())
            if path not in a or path not in b or a[path]['comparisonSha256'] != b[path]['comparisonSha256']]


def generated_field_differences(expected, actual, comparison):
    """Retain the exact ignored YAML diff, including its original reference values."""
    a, b = expected['files'].get(DERIVED_SETTINGS_PATH), actual['files'].get(DERIVED_SETTINGS_PATH)
    if a is None or b is None or a['rawSha256'] == b['rawSha256']:
        return []
    left, right = a.get('generatedField', {}), b.get('generatedField', {})
    recognized = left.get('recognized') is True and right.get('recognized') is True
    ignored = recognized and a['comparisonSha256'] == b['comparisonSha256']
    raw_diff = ''.join(difflib.unified_diff(left.get('rawField', '').splitlines(keepends=True),
                                          right.get('rawField', '').splitlines(keepends=True),
                                          fromfile='expected/' + DERIVED_SETTINGS_PATH,
                                          tofile='actual/' + DERIVED_SETTINGS_PATH))
    return [{'comparison': comparison, 'path': DERIVED_SETTINGS_PATH, 'field': DERIVED_SETTINGS_FIELD,
             'reason': DERIVED_SETTINGS_REASON, 'recognized': recognized, 'ignored': ignored,
             'expectedRawSha256': a['rawSha256'], 'actualRawSha256': b['rawSha256'],
             'expectedRawField': left.get('rawField'), 'actualRawField': right.get('rawField'),
             'rawFieldUnifiedDiff': raw_diff}]


def record_comparison(audit, expected_key, actual_key, result_key):
    expected, actual = audit[expected_key], audit[actual_key]
    audit[result_key] = differences(expected, actual)
    changes = generated_field_differences(expected, actual, result_key)
    audit['generatedFieldDifferences'].extend(changes)
    audit['ignoredDerivedFields'].extend(change for change in changes if change['ignored'])


def ensure_idle_project(project):
    lock = project / 'Temp/UnityLockfile'
    if not lock.exists():
        return
    # Read-only exclusive-open probe. Never delete a lock or inspect/terminate
    # any other process, including the main Editor and prior batch PID59748.
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32,
                                  ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
    kernel.CreateFileW.restype = ctypes.c_void_p
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    kernel.CloseHandle.restype = ctypes.c_int
    handle = kernel.CreateFileW(str(lock), 0x80000000, 0, None, 3, 0, None)
    if handle == ctypes.c_void_p(-1).value:
        raise RuntimeError('The isolated project is already in use or its lock cannot be opened exclusively. '
                           'Existing Unity processes were left untouched. Windows error ' + str(ctypes.get_last_error()))
    kernel.CloseHandle(handle)


def archive_file(path, archive):
    if not path.exists():
        return
    archive.mkdir(parents=True, exist_ok=True)
    path.rename(archive / path.name)


def fresh(path, started):
    if not path.is_file() or path.stat().st_mtime < started - 1:
        raise RuntimeError('Missing or stale output: ' + str(path))


def validate_xml(path, mode, pid, started):
    fresh(path, started)
    root = ET.parse(path).getroot()
    if root.tag != 'test-run':
        raise RuntimeError('Expected genuine NUnit test-run XML.')
    cases = root.findall('.//test-case')
    summary = {
        'total': int(root.get('total', '0')), 'passed': int(root.get('passed', '0')),
        'failed': int(root.get('failed', '0')), 'skipped': int(root.get('skipped', '0')),
        'inconclusive': int(root.get('inconclusive', '0')),
        'durationSeconds': float(root.get('duration', '0')), 'resultState': root.get('result'),
    }
    failures = [{'fullName': case.get('fullname', case.get('name')), 'state': case.get('result'),
                 'message': case.findtext('failure/message'), 'stackTrace': case.findtext('failure/stack-trace')}
                for case in cases if case.get('result') != 'Passed']
    pids = {value.get('value') for value in root.findall('.//property') if value.get('name') == '_PID'}
    platforms = {value.get('value') for value in root.findall('.//property') if value.get('name') == 'platform'}
    assembly_paths = [suite.get('fullname', '') for suite in root.findall('.//test-suite') if suite.get('type') == 'Assembly']
    isolated_normalized = str(ISOLATED.resolve()).replace('\\', '/').lower().rstrip('/') + '/'
    origin_matches = bool(assembly_paths) and all(
        value.replace('\\', '/').lower().startswith(isolated_normalized) for value in assembly_paths)
    expected_platform = {'editmode': 'EditMode', 'playmode': 'PlayMode'}[mode]
    passed = (root.get('result') == 'Passed' and summary['total'] > 0
              and summary['total'] == summary['passed'] == len(cases)
              and summary['failed'] == summary['skipped'] == summary['inconclusive'] == 0
              and not failures and origin_matches
              and (not pids or pids == {str(pid)})
              and (not platforms or platforms == {expected_platform}))
    return {'passed': passed, 'summary': summary, 'results': failures,
            'xml': str(path), 'xmlSha256': digest(path)['sha256'],
            'assemblyPaths': assembly_paths, 'assemblyPathsMatchIsolatedProject': origin_matches,
            'recordedPids': sorted(pids), 'recordedPlatforms': sorted(platforms)}


def validate_graphics(started):
    source = (SOURCE / 'Assets/SpaceEngine/Editor/BatchValidation.cs').read_text(encoding='utf-8-sig')
    names = re.findall(r'\{\s*"Validate [^"]+"\s*,\s*"([^"]+\.json)"\s*\}', source)
    if not names or len(names) != len(set(names)):
        raise RuntimeError('Could not identify unique graphics reports from the verified BatchValidation source.')
    reports = []
    for name in names:
        path = (OUT / name).resolve()
        if not path.is_relative_to(OUT.resolve()):
            raise RuntimeError('Graphics report path escapes evidence directory.')
        fresh(path, started)
        report = json.loads(path.read_text(encoding='utf-8-sig'))
        reports.append({'path': name, 'passed': report.get('passed') is True,
                        'sha256': digest(path)['sha256']})
    return {'passed': all(item['passed'] for item in reports), 'reports': reports}


def validate_build(started, bee_before=None, capture_before=False):
    """Audit actual Bee inputs without retaining unrelated JSON configuration."""
    bee_directory = ISOLATED / 'Library/Bee'
    candidates, parsed = [], []
    for candidate in sorted(bee_directory.glob('Player*-inputdata.json')):
        before = candidate.stat()
        data = candidate.read_bytes()
        after = candidate.stat()
        if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
            raise RuntimeError('Bee input changed while reading: ' + str(candidate))
        entry = {'path': str(candidate.resolve()), 'bytes': len(data),
                 'sha256': hashlib.sha256(data).hexdigest(), 'mtimeNs': after.st_mtime_ns,
                 'modifiedUtc': dt.datetime.fromtimestamp(after.st_mtime, dt.timezone.utc).isoformat()}
        candidates.append(entry)
        if not capture_before:
            try:
                parsed.append((entry, json.loads(data.decode('utf-8-sig'))))
            except (UnicodeError, ValueError) as error:
                entry['parseError'] = str(error)
    snapshot = {'utc': utc(), 'files': candidates}
    if capture_before:
        return snapshot

    executable = (ROOT / 'Builds/Windows-v0.5.0/SpaceEngineG.exe').resolve()
    report_path = OUT / 'player-services.json'
    service_names = ('EnableUnityConnect', 'EnablePerformanceReporting', 'EnableAnalytics',
                     'EnableCrashReporting', 'EnableInsights')
    report = {'passed': False, 'utc': utc(),
              'buildStartedUtc': dt.datetime.fromtimestamp(started, dt.timezone.utc).isoformat(),
              'isolatedProject': str(ISOLATED.resolve()), 'expectedDestinationPath': str(executable),
              'before': bee_before, 'after': snapshot, 'requiredServiceKeys': list(service_names),
              'matches': [], 'services': {}, 'errors': [], 'serviceFlagsPassed': False}
    if not isinstance(bee_before, dict) or not isinstance(bee_before.get('files'), list):
        report['errors'].append('No pre-build Bee input snapshot was recorded.')
    for entry in candidates:
        if 'parseError' in entry:
            report['errors'].append('Cannot prove output identity for malformed Bee input: ' + entry['path'])
    for entry, document in parsed:
        config = document.get('PlayerBuildProgramLibrary.Data.PlayerBuildConfig') if isinstance(document, dict) else None
        if not isinstance(config, dict) or not isinstance(config.get('DestinationPath'), str):
            report['errors'].append('Missing typed PlayerBuildConfig/DestinationPath: ' + entry['path'])
            continue
        destination = pathlib.Path(config['DestinationPath'])
        if not destination.is_absolute():
            report['errors'].append('Bee DestinationPath is not absolute: ' + entry['path'])
            continue
        resolved_destination = destination.resolve()
        entry['destinationPath'] = config['DestinationPath']
        entry['resolvedDestinationPath'] = str(resolved_destination)
        entry['architecture'] = config.get('Architecture')
        if os.path.normcase(str(resolved_destination)) == os.path.normcase(str(executable)):
            report['matches'].append(dict(entry))
            services = config.get('Services')
            if isinstance(services, dict):
                # Never serialize the full Bee configuration, or unrelated service values.
                report['services'] = {name: services[name] for name in service_names if name in services}
                report['missingServiceKeys'] = [name for name in service_names if name not in services]
                report['unexpectedServiceKeys'] = sorted(set(services) - set(service_names))
                report['serviceFlagsPassed'] = (not report['missingServiceKeys']
                                               and not report['unexpectedServiceKeys']
                                               and all(services[name] is False for name in service_names))
            else:
                report['errors'].append('Matching Bee input has no Services object.')
    if len(report['matches']) != 1:
        report['errors'].append('Expected exactly one Bee input for this executable; found ' + str(len(report['matches'])))
    else:
        matched = report['matches'][0]
        report['matchedBeeInput'] = matched
        report['architecture'] = matched['architecture']
        previous = next((entry for entry in (bee_before or {}).get('files', [])
                         if entry.get('path') == matched['path']), None)
        report['matchedInputBefore'] = previous
        report['matchedInputAfter'] = matched
        report['inputContentReused'] = previous is not None and previous['sha256'] == matched['sha256']
    if not report['serviceFlagsPassed']:
        report['errors'].append('All five service keys must exist and be boolean false; no extra service keys are accepted.')

    path = OUT / 'build.txt'
    required = ['SpaceEngineG.exe', 'UnityPlayer.dll', 'SpaceEngineG_Data/globalgamemanagers']
    complete = all((executable.parent / name).is_file() and (executable.parent / name).stat().st_size > 0
                   for name in required)
    build_passed = False
    try:
        fresh(path, started)
        text = path.read_text(encoding='utf-8-sig')
        build_passed = bool(re.search(r'^Result:\s*Succeeded\s*$', text, re.MULTILINE)
                            and re.search(r'^Errors:\s*0\s*$', text, re.MULTILINE) and complete)
        report['buildReportSha256'] = digest(path)['sha256']
    except (OSError, RuntimeError) as error:
        report['errors'].append(str(error))
    report['buildReport'] = str(path)
    report['buildReportPassed'] = build_passed
    report['completePlayerFiles'] = complete
    report['passed'] = build_passed and report['serviceFlagsPassed'] and not report['errors']
    write_json(report_path, report)
    return {'passed': report['passed'], 'buildReport': str(path),
            'buildReportSha256': report.get('buildReportSha256'),
            'executable': str(executable),
            'executableSha256': digest(executable)['sha256'] if executable.is_file() else None,
            'completePlayerFiles': complete, 'playerServicesReport': str(report_path),
            'playerServicesReportSha256': digest(report_path)['sha256'],
            'servicesPassed': report['serviceFlagsPassed'] and not report['errors'], 'services': report['services'],
            'errors': report['errors']}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--mode', choices=['editmode', 'playmode', 'graphics', 'build'], required=True)
    parser.add_argument('--unity-version', default='6000.5.7f1')
    parser.add_argument('--unity-editor', type=pathlib.Path, help='Optional absolute Unity.exe override.')
    parser.add_argument('--timeout', type=float, default=1800, help='Maximum seconds for the process this script launches.')
    args = parser.parse_args()
    if os.name != 'nt':
        parser.error('This Windows Unity launcher requires Windows.')
    if args.timeout <= 0:
        parser.error('--timeout must be positive.')
    if ISOLATED.resolve().parent != ROOT or ISOLATED.resolve().name != '.validation-unity-v050':
        parser.error('The isolated project must be the exact existing direct repository child.')
    editor = (args.unity_editor or pathlib.Path('C:/Program Files/Unity/Hub/Editor') /
              args.unity_version / 'Editor/Unity.exe').resolve()
    if not editor.is_file():
        parser.error('Unity executable not found: ' + str(editor))
    LOCAL.mkdir(parents=True, exist_ok=True)
    launch_lock = LOCAL / 'v050-isolated-launch.lock'
    try:
        lock_fd = os.open(launch_lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
    except FileExistsError:
        parser.error('An isolated launcher lock already exists; no Unity process was touched: ' + str(launch_lock))
    os.write(lock_fd, (str(os.getpid()) + '\n').encode('ascii'))
    os.close(lock_fd)
    mode = args.mode
    run_id = dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + mode + '-' + uuid.uuid4().hex[:8]
    archive = OUT / 'IsolatedValidation' / run_id
    manifest_path = OUT / ('isolated-' + mode + '-manifest.json')
    result_path = OUT / (mode + '-tests.json' if mode in ('editmode', 'playmode') else mode + '-batch.json')
    xml_path = OUT / ('final-' + mode + '.xml')
    log_path = LOCAL / ('v050-' + mode + '-isolated.log')
    audit = {'passed': False, 'utcStarted': utc(), 'runId': run_id, 'mode': mode,
             'sourceProject': str(SOURCE.resolve()), 'isolatedProject': str(ISOLATED.resolve()),
             'unityExecutable': str(editor), 'gpuDisabled': False, 'errors': [],
             'derivedFieldComparisonPolicy': {'path': DERIVED_SETTINGS_PATH, 'field': DERIVED_SETTINGS_FIELD,
                                             'reason': DERIVED_SETTINGS_REASON,
                                             'unknownShapes': 'Compared as raw bytes; no normalization.'},
             'generatedFieldDifferences': [], 'ignoredDerivedFields': []}
    process = None
    claimed_project = False
    verification = None
    try:
        ensure_idle_project(ISOLATED)
        claimed_project = True
        OUT.mkdir(parents=True, exist_ok=True)
        for path in (result_path, log_path, xml_path if mode in ('editmode', 'playmode') else manifest_path):
            archive_file(path, archive / 'previous')
        if mode == 'build':
            archive_file(OUT / 'player-services.json', archive / 'previous')
        audit['sourceBefore'] = manifest(SOURCE)
        audit['isolatedBefore'] = manifest(ISOLATED)
        record_comparison(audit, 'sourceBefore', 'isolatedBefore', 'differencesBefore')
        linked = [name for name in audit['sourceBefore']['files']
                  if (ISOLATED / name).exists() and os.path.samefile(SOURCE / name, ISOLATED / name)]
        audit['sharedSourceFileIdentities'] = linked
        if audit['differencesBefore'] or linked:
            raise RuntimeError('Source/isolation mismatch or shared hard-linked source files. '
                               'See manifest; nothing was synchronized/deleted and Unity was not launched.')
        version_text = (ISOLATED / 'ProjectSettings/ProjectVersion.txt').read_text(encoding='utf-8-sig')
        if not args.unity_editor and not re.search(r'^m_EditorVersion:\s*' + re.escape(args.unity_version) + r'\s*$', version_text, re.MULTILINE):
            raise RuntimeError('Isolated project Unity version does not match the selected Editor.')
        command = [str(editor), '-batchmode', '-projectPath', str(ISOLATED.resolve()), '-logFile', str(log_path.resolve())]
        if mode in ('editmode', 'playmode'):
            command += ['-runTests', '-testPlatform', {'editmode': 'EditMode', 'playmode': 'PlayMode'}[mode],
                        '-testResults', str(xml_path.resolve())]
        else:
            method = 'SpaceEngine.Editor.BatchValidation.RunGraphics' if mode == 'graphics' else 'SpaceEngine.Editor.ExplorerBuild.BuildWindows'
            command += ['-executeMethod', method, '-quit']
        audit['command'] = command
        revision = subprocess.run(['git', '--no-optional-locks', 'rev-parse', 'HEAD'], cwd=ROOT,
                                  capture_output=True, text=True, creationflags=HIDDEN, timeout=15)
        if revision.returncode == 0:
            audit['sourceGitRevision'] = revision.stdout.strip()
        ensure_idle_project(ISOLATED)
        if mode == 'build':
            audit['beeInputsBeforeBuild'] = validate_build(None, capture_before=True)
        write_json(manifest_path, audit)
        started = time.time()
        started_monotonic = time.monotonic()
        process = subprocess.Popen(command, cwd=ROOT, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=HIDDEN)
        audit['launchedPid'] = process.pid
        audit['processStartedAtUtc'] = utc()
        print(mode + ': Unity PID ' + str(process.pid) + ', GPU enabled, isolated project ' + str(ISOLATED), flush=True)
        next_progress = started_monotonic + 30
        while process.poll() is None:
            now = time.monotonic()
            if now - started_monotonic >= args.timeout:
                raise TimeoutError('Isolated Unity exceeded timeout; only the process launched by this run will be terminated.')
            if now >= next_progress:
                print(mode + ': still running, ' + str(round(now - started_monotonic)) + ' seconds; log ' + str(log_path), flush=True)
                next_progress = now + 30
            time.sleep(1)
        audit['processExitCode'] = process.returncode
        audit['elapsedSeconds'] = time.monotonic() - started_monotonic
        audit['sourceAfter'] = manifest(SOURCE)
        audit['isolatedAfter'] = manifest(ISOLATED)
        record_comparison(audit, 'sourceBefore', 'sourceAfter', 'sourceChangesDuringRun')
        record_comparison(audit, 'isolatedBefore', 'isolatedAfter', 'isolatedChangesDuringRun')
        record_comparison(audit, 'sourceAfter', 'isolatedAfter', 'differencesAfter')
        if mode in ('editmode', 'playmode'):
            verification = validate_xml(xml_path, mode, process.pid, started)
        elif mode == 'graphics':
            verification = validate_graphics(started)
        else:
            verification = validate_build(started, audit.get('beeInputsBeforeBuild'))
        audit['verification'] = verification
        if audit['sourceChangesDuringRun'] or audit['isolatedChangesDuringRun'] or audit['differencesAfter']:
            raise RuntimeError('Assets/Packages/ProjectSettings changed during validation. '
                               'This includes Unity automatic ProjectSettings edits; review the recorded differences explicitly. No synchronization was performed.')
        if process.returncode != 0 or not verification['passed']:
            raise RuntimeError('Unity process or actual validation result did not pass.')
        audit['passed'] = True
    except Exception as error:
        audit['errors'].append(str(error))
    except KeyboardInterrupt:
        audit['errors'].append('Interrupted by user.')
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
            audit['processExitCode'] = process.returncode
        if process is not None and 'sourceAfter' not in audit:
            try:
                audit['sourceAfter'] = manifest(SOURCE)
                audit['isolatedAfter'] = manifest(ISOLATED)
                record_comparison(audit, 'sourceBefore', 'sourceAfter', 'sourceChangesDuringRun')
                record_comparison(audit, 'isolatedBefore', 'isolatedAfter', 'isolatedChangesDuringRun')
                record_comparison(audit, 'sourceAfter', 'isolatedAfter', 'differencesAfter')
            except Exception as error:
                audit['errors'].append('Post-run manifest could not be completed: ' + str(error))
        audit['utcFinished'] = utc()
        # In-use-project rejection leaves another run's XML/log/result files alone.
        if claimed_project:
            write_json(manifest_path, audit)
            write_json(archive / 'manifest.json', audit)
            result = {'success': audit['passed'], 'message': 'Isolated Unity validation ' + ('passed' if audit['passed'] else 'failed'),
                      'data': {'status': 'succeeded' if audit['passed'] else 'failed',
                               'mode': {'editmode': 'EditMode', 'playmode': 'PlayMode'}.get(mode, mode),
                               'isolatedProject': str(ISOLATED.resolve()), 'manifest': str(manifest_path),
                               'processExitCode': audit.get('processExitCode'), 'errors': audit['errors'],
                               'result': verification or {}}}
            write_json(result_path, result)
        else:
            write_json(LOCAL / ('v050-' + mode + '-isolated-preflight-error.json'), audit)
        launch_lock.unlink()
    print(mode + ': ' + ('PASS' if audit['passed'] else 'FAIL ' + '; '.join(audit['errors'])), flush=True)
    return 0 if audit['passed'] else 2


if __name__ == '__main__':
    raise SystemExit(main())
