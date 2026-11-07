#!/usr/bin/env python3
"""Check the current network release, or historical GF Visual 3 on older trees."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SCENERY = Path('unity_project/Assets/Catenary/Scenery')


def network_digest(layouts):
    """Ignore asset boundaries and order, but retain every value and duplicate."""
    rows = sorted(json.dumps(obj, sort_keys=True, separators=(',', ':'),
                             ensure_ascii=False, allow_nan=False)
                  for layout in layouts.values() for obj in layout)
    return hashlib.sha256(('\n'.join(rows) + '\n').encode()).hexdigest()


def verify(root, accepted_zip=None):
    root = Path(root)
    if (root / 'audit/network_6m.json').is_file():
        from verify_network_release import verify as verify_current
        return verify_current(root, accepted_zip)
    info = json.loads((root / 'audit/phase2_gf.json').read_text())
    folder = root / SCENERY
    layouts = {p.name: json.loads(p.read_bytes()) for p in folder.glob('scenery_*.json')}
    errors = []
    if len(layouts) != info['total_layouts'] or sum(map(len, layouts.values())) != info['total_objects']:
        errors.append('Unexpected number of bundled assets or objects')
    for name, entry in {**info['added_assets'], **info['revised_assets']}.items():
        path = folder / name
        meta = folder / (name + '.meta')
        if name not in layouts or not meta.is_file():
            errors.append('Missing JSON or metadata: ' + name)
            continue
        if len(layouts[name]) != entry['objects'] or hashlib.sha256(path.read_bytes()).hexdigest() != entry['sha256']:
            errors.append('Changed accepted layout: ' + name)
        text = meta.read_text()
        if ('guid: ' + entry['guid']) not in text.splitlines() or not re.search(r'^  assetBundleName: catenary\s*$', text, re.M):
            errors.append('Invalid bundle metadata: ' + name)
    digest = network_digest(layouts)
    if digest != info['canonical_network_sha256']:
        errors.append('Bundled objects differ from the Visual 3 network')
    if accepted_zip is not None:
        if hashlib.sha256(accepted_zip.read_bytes()).hexdigest() != info['accepted_archive_sha256']:
            errors.append('Expected the approved GF Visual 3 archive')
        else:
            # Reproduce the case-insensitive external override loader on the
            # pre-split key set. The archive replaces all six revised base files.
            reference = {n.lower(): v for n, v in layouts.items() if n not in info['added_assets']}
            with zipfile.ZipFile(accepted_zip) as archive:
                for name in info['superseded_external_files']:
                    reference[name] = json.loads(archive.read('GF-Phase2-Visual3/Scenery/' + name))
            if network_digest(reference) != digest:
                errors.append('Direct comparison with accepted external overrides failed')
    return {'passed': not errors, 'objects': sum(map(len, layouts.values())),
            'layouts': len(layouts), 'direct_archive_comparison': accepted_zip is not None,
            'errors': errors}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--accepted-zip', type=Path)
    result = verify(ROOT, parser.parse_args().accepted_zip)
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result['passed'] else 1)
