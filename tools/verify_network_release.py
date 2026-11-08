#!/usr/bin/env python3
"""Verify the accepted GF / 6 m and CP approach integrations, without Unity.

Checks serialized values and multiplicities, metadata, supplies and the mapping
back to every accepted overlay. This is not a physical clearance or game test.
"""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import re
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SCENERY = Path('unity_project/Assets/Catenary/Scenery')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'),
                      ensure_ascii=False, allow_nan=False)


def network_digest(layouts):
    rows = sorted(canonical(obj) for layout in layouts.values() for obj in layout)
    return sha(('\n'.join(rows) + '\n').encode())


def verify(root=ROOT, accepted_zip=None, cp_accepted_zip=None):
    root = Path(root)
    info = json.loads((root / 'audit/network_6m.json').read_bytes())
    folder = root / SCENERY
    errors = []
    layouts = {}
    for p in folder.glob('scenery_*.json'):
        try:
            value = json.loads(p.read_bytes())
            if not isinstance(value, list):
                raise ValueError('Expected an array')
            layouts[p.name] = value
        except (ValueError, UnicodeError) as ex:
            errors.append('Unreadable layout: ' + p.name + ': ' + str(ex))
    if set(layouts) != set(info['assets']):
        errors.append('Missing or unexpected scenery files')
    if len({n.lower() for n in layouts}) != len(layouts):
        errors.append('Case-insensitive scenery basename collision')
    guids = []
    for name, entry in info['assets'].items():
        if name not in layouts:
            continue
        rows = layouts[name]
        if sha((folder / name).read_bytes()) != entry['sha256'] or len(rows) != entry['objects']:
            errors.append('Changed accepted layout: ' + name)
        feeds = dict(collections.Counter(o['substation'] for o in rows if 'substation' in o))
        if feeds != entry['wire_feeds']:
            errors.append('Changed feed assignments: ' + name)
        meta = folder / (name + '.meta')
        if not meta.is_file():
            errors.append('Missing Unity metadata: ' + name)
            continue
        text = meta.read_text()
        match = re.search(r'^guid: ([0-9a-f]{32})$', text, re.M)
        if (sha(meta.read_bytes()) != entry['meta_sha256'] or not match
                or match[1] != entry['guid']
                or not re.search(r'^  assetBundleName: catenary\s*$', text, re.M)):
            errors.append('Changed Unity metadata: ' + name)
        if match:
            guids.append(match[1])
    if len(set(guids)) != len(guids):
        errors.append('Duplicate Unity GUID')
    total = sum(map(len, layouts.values()))
    if total != info['total_objects'] or len(layouts) != info['total_layouts']:
        errors.append('Unexpected total object/layout count')
    if network_digest(layouts) != info['canonical_network_sha256']:
        errors.append('Network values or multiplicities differ from accepted release')
    sources = collections.Counter(o['map_location'] for rows in layouts.values()
                                  for o in rows if 'map_location' in o)
    if any(n != 1 for n in sources.values()):
        errors.append('Duplicate substation definition')
    feeds = {o['substation'] for rows in layouts.values() for o in rows if 'substation' in o}
    if feeds - set(sources) - {'NEUTRAL'}:
        errors.append('Unknown wire substation')
    for name in info['retired_monoliths']:
        for parent in (folder, root / 'Scenery'):
            if any(p.name.lower() == name for p in parent.glob('*.json')):
                errors.append('Retired monolith duplicates bundled geometry: ' + name)

    # Rebuild every original overlay in its original order. No regenerated
    # geometry, coordinate rounding, sets or deduplication are involved.
    restored = {n: [None] * item['objects'] for n, item in info['accepted_sources'].items()}
    used_targets = set()
    for run in info['source_mapping']:
        try:
            source = restored[run['source']]
            target = layouts[run['target']]
            a, b, count = run['source_start'], run['target_start'], run['count']
            if count <= 0 or a < 0 or b < 0 or a + count > len(source) or b + count > len(target):
                raise ValueError('Invalid mapping bounds')
            for offset in range(count):
                key = (run['target'], b + offset)
                if source[a + offset] is not None or key in used_targets:
                    raise ValueError('Duplicate mapping')
                source[a + offset] = target[b + offset]
                used_targets.add(key)
        except (KeyError, ValueError, TypeError) as ex:
            errors.append('Invalid source mapping: ' + str(ex))
    for name, rows in restored.items():
        if any(o is None for o in rows) or sha(canonical(rows).encode()) != info['accepted_sources'][name]['canonical_sha256']:
            errors.append('Accepted overlay reconstruction differs: ' + name)

    if accepted_zip is not None:
        accepted_zip = Path(accepted_zip)
        if sha(accepted_zip.read_bytes()) != info['accepted_archive_sha256']:
            errors.append('Expected the accepted Catenary_6m_Visual2.zip')
        else:
            with zipfile.ZipFile(accepted_zip) as archive:
                prefix = info['accepted_archive_root']
                members = [n[len(prefix):] for n in archive.namelist()
                           if n.startswith(prefix) and n.endswith('.json')]
                if sorted(members) != sorted(restored):
                    errors.append('Unexpected accepted archive scenery inventory')
                for name, rows in restored.items():
                    data = archive.read(prefix + name)
                    if sha(data) != info['accepted_sources'][name]['sha256'] or json.loads(data) != rows:
                        errors.append('Direct accepted archive comparison differs: ' + name)
    # CP Visual3 uses eight complete layouts, so no repartitioning or object
    # regeneration is needed. Preserve its reviewed bytes as well as values.
    cp = info.get('cp_approaches', {})
    cp_sources = cp.get('sources', {})
    for name, entry in cp_sources.items():
        target = entry['target']
        rows = layouts.get(target)
        if (rows is None or len(rows) != entry['objects']
                or sha(canonical(rows).encode()) != entry['canonical_sha256']
                or sha((folder / target).read_bytes()) != entry['sha256']):
            errors.append('Accepted CP payload differs: ' + name)
    if cp_accepted_zip is not None:
        archive_path = Path(cp_accepted_zip)
        if not cp or sha(archive_path.read_bytes()) != cp['accepted_archive_sha256']:
            errors.append('Expected the accepted CP-Approaches-Visual3.zip')
        else:
            with zipfile.ZipFile(archive_path) as archive:
                prefix = cp['accepted_archive_root']
                names = [n[len(prefix):] for n in archive.namelist()
                         if n.startswith(prefix) and n.endswith('.json')]
                if sorted(names) != sorted(cp_sources):
                    errors.append('Unexpected accepted CP archive scenery inventory')
                for name, entry in cp_sources.items():
                    if name not in names:
                        continue
                    target = folder / entry['target']
                    if not target.is_file() or archive.read(prefix + name) != target.read_bytes():
                        errors.append('Direct accepted CP archive comparison differs: ' + name)
    return dict(passed=not errors, revision=info['revision'], layouts=len(layouts),
                objects=total, reconstructed_overlays=len(restored),
                direct_archive_comparison=accepted_zip is not None,
                cp_approach_sources_verified=len(cp_sources),
                cp_archive_comparison=cp_accepted_zip is not None, errors=errors)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=ROOT)
    parser.add_argument('--accepted-zip', type=Path)
    parser.add_argument('--cp-accepted-zip', type=Path)
    args = parser.parse_args()
    result = verify(args.root, args.accepted_zip, args.cp_accepted_zip)
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result['passed'] else 1)
