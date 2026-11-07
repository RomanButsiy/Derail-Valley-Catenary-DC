#!/usr/bin/env python3
"""Prepare OPTIONAL 2WE3 controller in an ignored build workspace, never in the core.
Defaults to the pinned e0b78cb upstream controller; 4d1a6a6 remains opt-in.
No binaries or locomotive implementation are included in the catenary repository.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ALLOWED_DIRS = ('devices', 'circuit', 'unit_A', 'unit_B', 'utilities')
# The stock 2WE3 CCL pack declares AdditionalDependencies: Catenary-DC.
# Only this optional controller provides that legacy ID; it contains no OCS engine.
BRIDGE_ID = 'Catenary-DC'
BUILD_FIX_REVISION = 'legacy2we3-upstream.2'
LEGACY_LANGUAGE_VERSION = '14.0'
BRIDGE_VERSION = '0.2.0'
UPSTREAM_REVISIONS = {
    'e0b78cb': 'e0b78cb7943ca4d7cde112065b863139dae0c7b0',
    '4d1a6a6': '4d1a6a691415b6f08f9a76fbafdc8357485e277a',
}

def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()

def replace_once(text: str, old: str, new: str, label: str) -> str:
    if text.count(old) != 1:
        raise ValueError(f'Unexpected upstream structure: {label}. No original file will be changed.')
    return text.replace(old, new, 1)


def fix_simulate_named_arguments(caller: str, callee: str) -> str:
    """Repair two stale named arguments in the verified upstream working copy.

    Do not change solver implementation, call order, or true/false values.
    Checking the callee as well prevents guessing against another revision.
    """
    signature = 'public async Task<bool> simulate(bool permit_topology_changes)'
    if callee.count(signature) != 1:
        raise ValueError('Unexpected upstream circuit.simulate signature; refusing to guess parameter semantics.')
    for value in ('true', 'false'):
        caller = replace_once(
            caller,
            f'_circuit.simulate(permit_commutation: {value})',
            f'_circuit.simulate(permit_topology_changes: {value})',
            f'circuit.simulate named argument ({value})')
    if 'permit_commutation:' in caller:
        raise ValueError('Unrecognized permit_commutation named call remains in unit_a_sim.cs.')
    return caller


def verify_source(source: Path, upstream_revision: str) -> dict:
    """Verify the complete selected inventory before creating generated output."""
    if upstream_revision not in UPSTREAM_REVISIONS:
        raise ValueError('Unsupported upstream revision: ' + upstream_revision)
    selected = [source/'WE6SIM'/f for f in ('Main.cs','car_spawn_handler.cs','electric_sim.csproj')]
    selected.append(source/'unity_project/Assets/_CCL_CARS/WE6A/WE6_pack.asset')
    for folder in ALLOWED_DIRS:
        if not (source/'WE6SIM'/folder).is_dir():
            raise ValueError('Select the upstream root containing WE6SIM and unity_project.')
        selected.extend(sorted((source/'WE6SIM'/folder).rglob('*.cs')))
    if upstream_revision == 'e0b78cb':
        known = json.loads((ROOT/'compat/Legacy2WE3/upstreams/e0b78cb.json').read_text())['source_files']
    else:
        original = json.loads((ROOT/'audit/upstream.json').read_text())['source_files']
        fixed = {'WE6SIM/Main.cs', 'WE6SIM/car_spawn_handler.cs', 'WE6SIM/electric_sim.csproj',
                 'unity_project/Assets/_CCL_CARS/WE6A/WE6_pack.asset'}
        known = {name: {'sha256': value} for name, value in original.items()
                 if name in fixed or (name.endswith('.cs') and any(name.startswith('WE6SIM/'+d+'/') for d in ALLOWED_DIRS))}
    names = {p.relative_to(source).as_posix() for p in selected if p.is_file()}
    if names != set(known):
        raise ValueError('Upstream file inventory differs: missing=' + str(sorted(set(known)-names))
                         + '; extra=' + str(sorted(names-set(known))))
    fingerprints = {}
    for path in selected:
        relative = path.relative_to(source).as_posix()
        data = path.read_bytes()
        actual = hashlib.sha256(data).hexdigest()
        entry = known[relative]
        # Git for Windows can convert LF to CRLF. Accept that conversion only;
        # BOM, content and whitespace edits still fail the pinned content hash.
        normalized = hashlib.sha256(data.replace(b'\r\n', b'\n')).hexdigest()
        if actual != entry['sha256'] and normalized != entry.get('lf_sha256'):
            raise ValueError(f'Unsupported/edited upstream file: {relative}; expected {upstream_revision}.')
        fingerprints[relative] = actual
    return fingerprints


def prepare(source: Path, output: Path, core_dll: Path, upstream_revision: str = 'e0b78cb') -> dict:
    source, output, core_dll = source.resolve(), output.resolve(), core_dll.resolve()
    if source == output or source in output.parents:
        raise ValueError('Output must be outside the original upstream directory.')
    if output == ROOT or output in ROOT.parents:
        raise ValueError('Output cannot replace the catenary repository.')
    if ROOT in output.parents and ROOT/'artifacts' not in output.parents:
        raise ValueError('Inside this repository, generated controller source is allowed only under artifacts/.')
    if output.exists() and any(output.iterdir()):
        raise ValueError('Output must be a new or empty directory.')
    fingerprints = verify_source(source, upstream_revision)
    output.mkdir(parents=True, exist_ok=True)
    # Source files are COPIED, never patched in the caller\'s checkout.
    copied = 0
    for folder in ALLOWED_DIRS:
        for path in sorted((source/'WE6SIM'/folder).rglob('*.cs')):
            if folder == 'utilities' and path.name == 'CSV_struct.cs':
                continue  # catenary-only helper; do not keep the original Mono.Csv dependency.
            dest = output/path.relative_to(source/'WE6SIM')
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, dest)
            copied += 1
    for name in ('Main.cs','editor_settings.cs'):
        shutil.copy2(ROOT/'compat/Legacy2WE3/templates'/name, output/name)
    # Only the older snapshot needs the named-argument fix. In e0b78cb the
    # signature and both callers already agree; preserve the entire solver and
    # unit_A implementation, including new gauges and explicit delta-time.
    caller_path = output/'unit_A/unit_a_sim.cs'
    caller = caller_path.read_text(encoding='utf-8-sig')
    callee = (output/'circuit/circuit.cs').read_text(encoding='utf-8-sig')
    named_arguments_fixed = 0
    if upstream_revision == '4d1a6a6':
        caller_path.write_text(fix_simulate_named_arguments(caller, callee), encoding='utf-8')
        named_arguments_fixed = 2
    elif (callee.count('public async Task simulate(bool permit_commutation)') != 1
          or any(caller.count(f'_circuit.simulate(permit_commutation: {v})') != 1 for v in ('true', 'false'))):
        raise ValueError('Unexpected e0b78cb solver contract.')

    pantograph = output/'devices/pantograph.cs'
    text = pantograph.read_text(encoding='utf-8-sig')
    text = text.replace('using electric_sim.catenary;', 'using CatenaryDC.Api;')
    before = 'overhead_equipment.system.relative_wire_height_and_voltage('
    if text.count(before) != 2:
        raise ValueError('Expected exactly two collector contact queries.')
    text = text.replace(before, 'CatenaryApi.QueryRelativeContact(')
    pantograph.write_text(text, encoding='utf-8')

    spawn = (source/'WE6SIM/car_spawn_handler.cs').read_text(encoding='utf-8-sig')
    spawn = spawn.replace('using electric_sim.catenary;', 'using CatenaryDC.Api;')
    spawn = re.sub(r'#if DEBUG\s+using electric_sim.catenary_editor;\s+#endif\s*', '', spawn)
    spawn = re.sub(r'#if DEBUG\s+private static TrainCar\?\s+_mow_vehicle.*?#endif', '', spawn, flags=re.S)
    spawn = re.sub(r'#if DEBUG\s+if \(\(vehicle.carType == TrainCarType.LocoDM1U.*?#endif', '', spawn, flags=re.S)
    spawn = re.sub(r'#if DEBUG\s+if \(_mow_vehicle == vehicle\).*?#endif', '', spawn, flags=re.S)
    spawn = re.sub(r'#if DEBUG\s+if \(_mow_vehicle != null\).*?#endif', '', spawn, flags=re.S)
    pattern = r'    public static void Postfix\(CarSpawner __instance\)\s*\{.*?\n    \}'
    new_postfix = '''    private static CarSpawner? _spawner;
    public static void Postfix(CarSpawner __instance)
    {
        session_end();
        _spawner = __instance;
        _spawner.CarSpawned += on_car_spawned;
        CatenaryApi.SimulationStep += simulate_all_units;
        UnloadWatcher.UnloadRequested += session_end;
    }'''
    spawn, count = re.subn(pattern, lambda _: new_postfix, spawn, count=1, flags=re.S)
    if count != 1:
        raise ValueError('Upstream CarSpawner hook changed.')
    spawn = replace_once(spawn, '    private static void session_end()\n    {', '''    private static void session_end()
    {
        CatenaryApi.SimulationStep -= simulate_all_units;
        UnloadWatcher.UnloadRequested -= session_end;
        if (_spawner != null) _spawner.CarSpawned -= on_car_spawned;
        _spawner = null;''', 'world teardown')
    spawn = replace_once(spawn, '        if (vehicle == null || !vehicle.IsLoco)',
        '        if (vehicle == null || !vehicle.IsLoco || _all_A_units.ContainsKey(vehicle) || _all_B_units.ContainsKey(vehicle))', 'duplicate spawn protection')
    if '_mow_' in spawn or 'overhead_equipment' in spawn:
        raise ValueError('A catenary lifecycle dependency remains in the adapter.')
    (output/'car_spawn_handler.cs').write_text(spawn, encoding='utf-8')

    original = ET.parse(source/'WE6SIM/electric_sim.csproj')
    references = [x.attrib['Include'] for x in original.findall('.//Reference')]
    root = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = ET.SubElement(root, 'PropertyGroup')
    for key, val in {'TargetFramework':'net48', 'LangVersion':LEGACY_LANGUAGE_VERSION, 'Nullable':'enable',
                     'AssemblyName':'CatenaryDC.Legacy2WE3', 'RootNamespace':'electric_sim',
                     'Version':BRIDGE_VERSION, 'NoWarn':'$(NoWarn);CS8981',
                     'GameManagedPath':'$(GameDirectory)/DerailValley_Data/Managed'}.items():
        ET.SubElement(props,key).text=val
    packages=ET.SubElement(root,'ItemGroup')
    for name, version in [('UnityModManager','0.27.2'),('Nullable','1.3.1'),
                          ('Microsoft.CSharp','4.7.0'),('Microsoft.NETFramework.ReferenceAssemblies','1.0.3')]:
        ET.SubElement(packages,'PackageReference',Include=name,Version=version,
                      **({'PrivateAssets':'all'} if name in ('Nullable','Microsoft.NETFramework.ReferenceAssemblies') else {}))
    refs=ET.SubElement(root,'ItemGroup')
    for name in references:
        entry=ET.SubElement(refs,'Reference',Include=name)
        ET.SubElement(entry,'HintPath').text='$(GameManagedPath)/'+name+'.dll'
        ET.SubElement(entry,'Private').text='false'
    entry=ET.SubElement(refs,'Reference',Include='CatenaryDC')
    ET.SubElement(entry,'HintPath').text='$(CatenaryCoreDll)'
    ET.SubElement(entry,'Private').text='false'
    target=ET.SubElement(root,'Target',Name='CheckDependencies',BeforeTargets='ResolveReferences')
    ET.SubElement(target,'Error',Condition="!Exists('$(CatenaryCoreDll)')",Text='Pass CatenaryCoreDll pointing at the newly built core DLL.')
    ET.SubElement(target,'Error',Condition="!Exists('$(GameManagedPath)/Assembly-CSharp.dll')",Text='Select the game root using GameDirectory.')
    ET.indent(root, space='  ')
    ET.ElementTree(root).write(output/'Legacy2WE3.csproj',encoding='utf-8',xml_declaration=False)
    info={'Id':BRIDGE_ID,'DisplayName':'Catenary DC — optional 2WE3 bridge','Version':BRIDGE_VERSION,
          'ManagerVersion':'0.27.3','AssemblyName':'CatenaryDC.Legacy2WE3.dll','EntryMethod':'electric_sim.Main.Load',
          'Author':'Knsgf vehicle implementation; compatibility adaptation by RomanButsiy',
          'Requirements':['DerailValleyCatenaryDC-0.2.0']}
    (output/'info.json').write_text(json.dumps(info,indent=2)+'\n')
    report={'upstream_commit':UPSTREAM_REVISIONS[upstream_revision],
            'upstream_revision':upstream_revision,'bridge_version':BRIDGE_VERSION,
            'ccl_pack_version':'2.3.7',
            'source':str(source),'core_dll_requested':str(core_dll),
            'verified_files':fingerprints,'copied_implementation_cs':copied,
            'contact_queries_redirected':2,'catenary_engine_copied':False,
            'legacy_umm_id':'Catenary-DC','core_umm_id':'DerailValleyCatenaryDC',
            'build_fix_revision':BUILD_FIX_REVISION,
            'language_version':LEGACY_LANGUAGE_VERSION,'target_framework':'net48',
            'simulate_named_arguments_fixed':named_arguments_fixed,
            'upstream_gauges_preserved':True,
            'solver_body_changed':False,'compiled':False,'runtime_tested':False}
    (output/'preparation.json').write_text(json.dumps(report,indent=2)+'\n')
    return report

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--core-dll',type=Path,required=True)
    parser.add_argument('--upstream-revision',choices=tuple(UPSTREAM_REVISIONS),default='e0b78cb')
    args=parser.parse_args()
    try:
        report=prepare(args.source,args.output,args.core_dll,args.upstream_revision)
        print(f'Prepared optional bridge; {len(report["verified_files"])} upstream files verified. Nothing installed.')
        print(f'Compatibility fix: {BUILD_FIX_REVISION}; C# {LEGACY_LANGUAGE_VERSION}; target net48.')
        print(f'Upstream: {report["upstream_commit"]}; named-argument fixes: {report["simulate_named_arguments_fixed"]}.')
    except (ValueError,OSError,KeyError) as error:
        parser.exit(1, 'STOPPED: '+str(error)+'\n')
