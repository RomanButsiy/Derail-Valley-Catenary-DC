#!/usr/bin/env python3
"""Read-only offline inspection/extraction of CatenaryDC authoring exports (Python 3.10+).
No Unity deserialization or game files are modified. No third-party packages required.
"""
from __future__ import annotations
import argparse
import collections
import csv
import hashlib
import io
import json
import math
from pathlib import Path
import sys


def unique_object(pairs):
    out = {}
    for key, value in pairs:
        if key in out:
            raise ValueError(f'Duplicate JSON property: {key}')
        out[key] = value
    return out


def json_loads(text):
    return json.loads(text.lstrip('\ufeff'), object_pairs_hook=unique_object,
                      parse_constant=lambda x: (_ for _ in ()).throw(ValueError('Non-finite JSON constant: ' + x)))


def read_capture(path: Path):
    """Complete file only. A completed stream may still report missing/errored objects."""
    if path.name.endswith('.partial'):
        raise ValueError(f'Incomplete export cannot be used: {path}')
    result = []
    with path.open(encoding='utf-8-sig') as stream:
        for number, line in enumerate(stream, 1):
            if not line.strip():
                continue
            try:
                record = json_loads(line)
                if not isinstance(record, dict):
                    raise ValueError('A record must be an object')
                result.append(record)
            except (ValueError, TypeError) as exc:
                raise ValueError(f'{path.name}:{number}: {exc}') from exc
    if len(result) < 2 or result[0].get('kind') not in {'header', 'catalog_header', 'route_header', 'probe_header'}:
        raise ValueError(f'Missing recognized header: {path.name}')
    if result[-1].get('kind') not in {'footer', 'route_footer'} or result[-1].get('export_complete') is not True:
        raise ValueError(f'Missing successful completion footer: {path.name}')
    return result


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False, allow_nan=False)


def signature(value):
    return hashlib.sha256(canonical(value).encode('utf-8')).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')


def summarize(paths):
    reports = []
    for path in paths:
        data = read_capture(path)
        counts = dict(collections.Counter(x.get('kind', '<missing>') for x in data))
        reports.append({'file': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                        'record_counts': counts, 'header': data[0], 'footer': data[-1],
                        'note': 'export_complete means file completion, NOT full map/mesh/track coverage.'})
    return reports


def extract(paths, output):
    if output.exists() and any(output.iterdir()):
        raise ValueError('Output directory must be empty; no source or installed files will be overwritten.')
    # Resolve all inputs before writing anything, so rejected input cannot silently produce a final package.
    loaded = [(p, read_capture(p)) for p in paths]
    bundle_hashes = collections.defaultdict(set)
    for _, data in loaded:
        for item in data[0].get('assets', []):
            if item.get('name') in {'catenary', 'catenary_parts'} and item.get('sha256'):
                bundle_hashes[item['name']].add(item['sha256'])
    if any(len(v) > 1 for v in bundle_hashes.values()):
        raise ValueError('Captures contain different catenary/parts bundle hashes. Keep different source versions separate.')
    output.mkdir(parents=True, exist_ok=True)
    tracks = collections.defaultdict(list)
    definitions, sources, seen_sources = {}, {}, {}
    conflicts, capture_reports = [], []
    scene_path = output / 'scene_observations.jsonl'
    with scene_path.open('w', encoding='utf-8') as out:
        for file_index, (path, data) in enumerate(loaded):
            capture_id = f'{file_index:03d}-{path.stem}'
            capture_reports.append({'capture': capture_id, 'file': path.name, 'header': data[0], 'footer': data[-1]})
            for record in data[1:-1]:
                kind = record.get('kind')
                if kind == 'track':
                    key = record.get('id') or ('path:' + record.get('path', '<unidentified>'))
                    geometry_hash = signature(record.get('points_native_m', []))
                    # Preserve observations and native coordinate frames; do not auto-snap or merge switches.
                    if not any(x['geometry_sha256'] == geometry_hash for x in tracks[key]):
                        tracks[key].append({'capture': capture_id, 'geometry_sha256': geometry_hash, 'record': record})
                elif kind == 'definition_csv':
                    name = record['name']
                    if name not in {'poles', 'wires', 'gantries', 'cantilevers', 'side_poles', 'miscellaneous'}:
                        raise ValueError('Unexpected definition name: ' + str(name))
                    if name in definitions and definitions[name] != record['text']:
                        raise ValueError('Different CSV definitions across captures: ' + name)
                    definitions[name] = record['text']
                elif kind == 'source_layout':
                    name = record['name']
                    if name in seen_sources and seen_sources[name] != signature(record['objects']):
                        conflicts.append({'source': name, 'capture': capture_id, 'reason': 'Source layout changed between captures'})
                    seen_sources[name] = signature(record['objects'])
                    # Original source names may contain '[CP]![IMW]' or UserData/. Never use them as output paths.
                    key = hashlib.sha256((name + ':' + seen_sources[name]).encode()).hexdigest()[:24]
                    sources[key] = {'source_name': name, 'capture': capture_id, 'objects': record['objects']}
                else:
                    out.write(canonical({'capture': capture_id, 'record': record}) + '\n')
    write_json(output / 'tracks_observations.json', dict(tracks))
    defs_dir = output / 'definitions'; defs_dir.mkdir()
    for name, text in definitions.items():
        (defs_dir / (name + '.csv')).write_text(text, encoding='utf-8')
    source_dir = output / 'source_snapshots'; source_dir.mkdir()
    for key, data in sources.items():
        write_json(source_dir / (key + '.json'), data)
    for key, observations in tracks.items():
        if len(observations) > 1:
            conflicts.append({'track': key, 'reason': 'Multiple native pointset geometries; check origin/frame/streaming', 'observations': len(observations)})
    report = {'schema_version': 1, 'captures': capture_reports, 'track_keys': len(tracks), 'conflicts': conflicts,
              'source_snapshots': len(sources), 'definitions': sorted(definitions), 'topology_certified': False,
              'notes': ['Native pointsets have NOT been transformed automatically.',
                        'Track connections are raw observed members, not certified adjacency.',
                        'Scene bounds/meshes are loaded-only; absence is not proof of free space.',
                        'Generated child objects remain survey records and MUST NOT be reimported as authored roots.',
                        'No ready-to-install scenery or electrical design is produced by extraction.']}
    write_json(output / 'extraction_report.json', report)
    return {'output': str(output), 'track_keys': len(tracks), 'conflicts': len(conflicts), 'captures': len(loaded)}


KINDS = {'wire', 'pole', 'cantilever', 'gantry', 'side_rail_pole', 'substation', 'miscellaneous_object'}
PREFIXES = ('electric_sim.catenary.overhead_equipment+', 'CatenaryDC.catenary.overhead_equipment+')


def validate_sources(sources, tables):
    errors, warnings, pending_wires = [], [], []
    seen_substations, signatures = {'NEUTRAL'}, set()
    count = 0
    def fail(message):
        raise ValueError(message)
    def number(obj, key, integer=False):
        v = obj.get(key)
        if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v):
            fail('Finite numeric field required: ' + key)
        if integer and (not isinstance(v, int) or not -(2**31) <= v < 2**31):
            fail('Int32 field required: ' + key)
        return v
    def text(obj, key):
        v = obj.get(key)
        if not isinstance(v, str) or not v.strip(): fail('Nonempty string required: ' + key)
        return v
    def row(table, predicate):
        return next((r for r in tables[table] if predicate(r)), None) or fail('Unknown model combination in ' + table)
    def no_metadata(value, root=False):
        if isinstance(value, dict):
            for k, v in value.items():
                if k in {'$id', '$ref', '$values'} or (k == '$type' and not root): fail('Unexpected metadata: ' + k)
                no_metadata(v)
        elif isinstance(value, list):
            for v in value: no_metadata(v)
    for source, objects in sorted(sources.items()):
        if not isinstance(objects, list):
            errors.append({'source': source, 'index': -1, 'message': 'Expected root array'}); continue
        for index, obj in enumerate(objects):
            count += 1
            try:
                if not isinstance(obj, dict): fail('Object expected')
                no_metadata(obj, root=True)
                typename = text(obj, '$type').split(',')
                if len(typename) < 2 or typename[1].strip() not in {'electric_sim', 'CatenaryDC'}: fail('Unapproved assembly')
                prefix = next((p for p in PREFIXES if typename[0].startswith(p)), None)
                if not prefix: fail('Unapproved type prefix')
                kind = typename[0][len(prefix):]
                if kind not in KINDS: fail('Unapproved object class')
                number(obj, 'x', True); number(obj, 'z', True); number(obj, 'y')
                q = obj.get('orientation')
                if not isinstance(q, dict): fail('Quaternion object required')
                norm = sum(number(q, axis)**2 for axis in ('x', 'y', 'z', 'w'))
                if abs(norm - 1) > .02: fail('Non-unit quaternion')
                obj_hash = signature(obj)
                if obj_hash in signatures: warnings.append({'source': source, 'index': index, 'message': 'Exact duplicate object'})
                signatures.add(obj_hash)
                if kind == 'wire':
                    typ = text(obj, 'wire_type'); length = number(obj, 'length'); number(obj, 'previous_pole_vertical_offset')
                    feed = text(obj, 'substation')
                    d = row('wires', lambda r: r['Type'] == typ)
                    if not 0 < length <= 2000: fail('Wire length out of range')
                    if d['IsEndAnchor'].lower() == 'true' and length <= 4: fail('Anchor non-contact tail is too long for span')
                    if d['FixedPart'].strip() and length <= float(d['FixedPartLength']): fail('Span shorter than fixed model')
                    if typ.startswith('plain_') and not typ.startswith('plain_half') and length < 25:
                        warnings.append({'source': source, 'index': index, 'message': 'Short plain span has different Debug marker appearance'})
                    pending_wires.append((source, index, feed))
                elif kind == 'pole': row('poles', lambda r: r['Kind'] == obj.get('kind', 'default') and r['Placement'] == obj.get('pole_type'))
                elif kind == 'side_rail_pole': row('side_poles', lambda r: r['Kind'] == obj.get('kind', 'SideRail'))
                elif kind == 'cantilever':
                    if not isinstance(obj.get('dual_wire', False), bool): fail('dual_wire must be boolean')
                    row('cantilevers', lambda r: r['SteadyArmType'] == obj.get('steady_arm_type') and r['CantileverType'] == obj.get('cantilever_type') and (r['DualWire'].lower() == 'true') == obj.get('dual_wire', False))
                elif kind == 'gantry':
                    tracks = number(obj, 'tracks', True)
                    k = obj.get('kind') or ('invisible' if tracks == 0 else 'slim' if 1 <= tracks <= 4 else 'truss' if tracks == 6 else '<invalid>')
                    row('gantries', lambda r: r['Kind'] == k and int(r['Tracks']) == tracks)
                    if 'stretch' in obj and number(obj, 'stretch') <= 0: fail('Invalid gantry stretch')
                elif kind == 'substation':
                    name = text(obj, 'map_location')
                    if name in seen_substations: fail('Duplicate/reserved substation: ' + name)
                    seen_substations.add(name)
                    if number(obj, 'supply_voltage') < 0 or number(obj, 'maximum_load') <= 0: fail('Invalid voltage/load')
                elif kind == 'miscellaneous_object':
                    t = text(obj, 'template_name')
                    if t == 'GantryArrow': warnings.append({'source': source, 'index': index, 'message': 'Diagnostic marker'})
                    else: row('miscellaneous', lambda r: r['Template'] == t)
            except (ValueError, KeyError, TypeError, OverflowError) as exc:
                errors.append({'source': source, 'index': index, 'message': str(exc)})
    for source, index, feed in pending_wires:
        if feed not in seen_substations: errors.append({'source': source, 'index': index, 'message': 'Unknown substation: ' + feed})
    return {'data_valid': not errors, 'objects': count, 'errors': errors, 'warnings': warnings,
            'not_checked': ['wire-run mechanics', 'registration forces', 'neutral isolation', 'clearances', 'game rendering'],
            'scene_changed': False}


def validate_command(candidate, capture):
    records = read_capture(capture)
    definitions = {r['name']: list(csv.DictReader(io.StringIO(r['text'].lstrip('\ufeff')))) for r in records if r.get('kind') == 'definition_csv'}
    needed = {'poles', 'wires', 'gantries', 'side_poles', 'cantilevers', 'miscellaneous'}
    if not needed <= definitions.keys(): raise ValueError('World capture lacks six CSV definitions')
    sources = {r['name']: r['objects'] for r in records if r.get('kind') == 'source_layout'}
    if not sources: raise ValueError('Use a world survey with source_layout records, not a catalog or a route recording')
    # Candidate basename is the same replacement key as the runtime external scenery loader.
    sources[candidate.name] = json_loads(candidate.read_text(encoding='utf-8-sig'))
    return validate_sources(sources, definitions)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    inspect = sub.add_parser('inspect'); inspect.add_argument('captures', nargs='+', type=Path)
    merge = sub.add_parser('extract'); merge.add_argument('captures', nargs='+', type=Path); merge.add_argument('--out', type=Path, required=True)
    validate = sub.add_parser('validate'); validate.add_argument('candidate', type=Path); validate.add_argument('--world', type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.command == 'inspect': result = summarize(args.captures)
        elif args.command == 'extract': result = extract(args.captures, args.out)
        else: result = validate_command(args.candidate, args.world)
        print(json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False))
        return int(isinstance(result, dict) and result.get('data_valid') is False)
    except (ValueError, OSError) as exc:
        print('STOPPED: ' + str(exc), file=sys.stderr); return 2


if __name__ == '__main__':
    raise SystemExit(main())
