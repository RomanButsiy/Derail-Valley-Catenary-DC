#!/usr/bin/env python3
"""Package a matching pair of Release ready-to-install folders for GitHub.

Does not build, install, publish, or bundle CCL/models/game DLLs. Validates the
existing build receipts and hashes, then creates two separate UMM ZIP files.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
UPSTREAM = 'e0b78cb7943ca4d7cde112065b863139dae0c7b0'
REVISION = 'legacy2we3-upstream.2'
COMMON = {'info.json', 'build-info.json', 'LICENSE_CC0.txt', 'LICENSE_MIT.txt', 'NOTICE.md'}
CORE_FILES = COMMON | {'CatenaryDC.dll', 'catenary', 'catenary_parts', 'network.json', 'Scenery/README.md'}
BRIDGE_FILES = COMMON | {'CatenaryDC.Legacy2WE3.dll', 'preparation.json'}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def inventory(folder, expected):
    require(folder.is_dir(), 'Missing ready-to-install directory: ' + str(folder))
    paths = list(folder.rglob('*'))
    require(not any(p.is_symlink() for p in paths), 'Ready folders must not contain symbolic links.')
    found = {p.relative_to(folder).as_posix() for p in paths if p.is_file()}
    require(found == expected, 'Unexpected ready-folder contents: missing=' + str(sorted(expected-found))
            + '; extra=' + str(sorted(found-expected)))
    require(all((folder / n).stat().st_size for n in expected), 'Empty release input file.')


def check_hash(folder, name, expected):
    require(sha(folder/name) == expected, 'Build receipt hash mismatch: ' + name)


def validate(core, bridge):
    inventory(core, CORE_FILES)
    inventory(bridge, BRIDGE_FILES)
    ci, bi = load(core/'info.json'), load(bridge/'info.json')
    cb, bb = load(core/'build-info.json'), load(bridge/'build-info.json')
    prep = load(bridge/'preparation.json')
    require(ci['Id'] == 'DerailValleyCatenaryDC' and ci['AssemblyName'] == 'CatenaryDC.dll', 'Wrong core mod identity.')
    require(bi['Id'] == 'Catenary-DC' and bi['AssemblyName'] == 'CatenaryDC.Legacy2WE3.dll', 'Wrong bridge mod identity.')
    require(cb['project'] == 'Derail-Valley-Catenary-DC' and bb['project'] == 'Legacy2WE3 bridge', 'Wrong build receipts.')
    require(cb['configuration'] == bb['configuration'] == 'Release', 'Publish Release builds for both components.')
    require(re.fullmatch(r'\d+\.\d+\.\d+', ci['Version']) is not None, 'Invalid core version.')
    require(ci['Version'] == cb['version'] == bi['Version'] == bb['version'] == prep['bridge_version'], 'Component versions differ.')
    require(bi['Requirements'] == ['DerailValleyCatenaryDC-0.2.0'], 'Unexpected bridge core dependency.')
    require(bb['upstream_commit'] == prep['upstream_commit'] == UPSTREAM, 'Expected the e0b78cb bridge.')
    require(bb['build_fix_revision'] == prep['build_fix_revision'] == REVISION, 'Bridge generator/builder mismatch.')
    require(prep['compiled'] is True and prep['catenary_engine_copied'] is False, 'Expected a compiled controller-only bridge.')
    require(prep['simulate_named_arguments_fixed'] == 0, 'Obsolete solver fix applied to e0b78cb.')
    require('source' not in prep and 'core_dll_requested' not in prep, 'Public bridge metadata still contains local workspace paths.')
    require(cb['scenery_revision'] == load(ROOT/'audit/network_6m.json')['revision'], 'Core scenery revision is stale.')
    for name, key in [('CatenaryDC.dll', 'core_sha256'), ('catenary', 'catenary_sha256'), ('catenary_parts', 'parts_sha256')]:
        check_hash(core, name, cb[key])
    check_hash(bridge, 'CatenaryDC.Legacy2WE3.dll', bb['controller_sha256'])
    require(bb['core_sha256'] == cb['core_sha256'], 'Bridge was built against a different core DLL.')
    require(load(core/'network.json') == load(ROOT/'config/network.json'), 'Unexpected network profile in release core.')
    for folder in (core, bridge):
        for name in ('LICENSE_CC0.txt', 'LICENSE_MIT.txt', 'NOTICE.md'):
            require((folder/name).read_bytes() == (ROOT/name).read_bytes(), 'Missing or stale release notice: ' + name)
    return ci['Version'], cb, bb


def make_zip(source, files, target, mod_folder):
    # Fixed archive timestamps and sorted paths make packaging reproducible for
    # the same binaries/receipts. Each ZIP contains ONE UMM mod root.
    with zipfile.ZipFile(target, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in sorted(files):
            entry = zipfile.ZipInfo(mod_folder + '/' + name, date_time=(1980, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            archive.writestr(entry, (source/name).read_bytes())


def package(core, bridge, output):
    core, bridge, output = Path(core).resolve(), Path(bridge).resolve(), Path(output).resolve()
    for source in (core, bridge):
        require(output != source and source not in output.parents and output not in source.parents,
                'Release output must be separate from both ready folders.')
    require(not output.exists() or (output.is_dir() and not any(output.iterdir())), 'Output must be new or empty.')
    version, cb, bb = validate(core, bridge)
    output.mkdir(parents=True, exist_ok=True)
    components = []
    for source, files, name in [(core, CORE_FILES, 'Derail-Valley-Catenary-DC'),
                                (bridge, BRIDGE_FILES, 'Derail-Valley-Catenary-DC-2WE3-Bridge')]:
        archive = output / (name + '-' + version + '.zip')
        make_zip(source, files, archive, name)
        components.append(dict(file=archive.name, sha256=sha(archive),
                               contents={n:sha(source/n) for n in sorted(files)}))
    (output/'INSTALL_UK.md').write_bytes((ROOT/'docs/INSTALL_RELEASE_UK.md').read_bytes())
    commit, dirty = None, None
    try:
        commit = subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', 'HEAD'], stderr=subprocess.DEVNULL, text=True).strip()
        dirty = bool(subprocess.check_output(['git', '-C', str(ROOT), 'status', '--porcelain', '--untracked-files=no'], text=True))
    except (OSError, subprocess.CalledProcessError):
        pass
    report = dict(version=version, upstream_commit=UPSTREAM, packaging_checkout_commit=commit,
                  packaging_checkout_dirty=dirty, core_sha256=cb['core_sha256'],
                  controller_sha256=bb['controller_sha256'], scenery_revision=cb['scenery_revision'],
                  components=components, ccl_and_vehicle_models_included=False,
                  runtime_tested_by_packager=False, published=False)
    (output/'release.json').write_text(json.dumps(report, indent=2)+'\n')
    files = sorted(p for p in output.iterdir() if p.is_file())
    (output/'SHA256SUMS.txt').write_text(''.join(sha(p)+'  '+p.name+'\n' for p in files))
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--core', required=True, type=Path)
    parser.add_argument('--bridge', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    try:
        result = package(args.core, args.bridge, args.output)
        print('Release files prepared: ' + str(args.output.resolve()))
        for item in result['components']:
            print(item['file'])
        print('Nothing installed or published. Complete the game smoke test before publishing.')
    except (ValueError, OSError, KeyError) as error:
        parser.exit(1, 'STOPPED: '+str(error)+'\n')
