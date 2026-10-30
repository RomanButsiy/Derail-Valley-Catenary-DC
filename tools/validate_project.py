#!/usr/bin/env python3
"""Offline source/data checks. NOT a C# compiler, Unity export or game test.
Standard library only. Run from any directory. --upstream enables byte comparison
and optional adapter preparation against the supplied author's source snapshot.
"""
from __future__ import annotations
import argparse
import collections
import csv
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import re
import tempfile
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
CHECKS=[]
def check(ok, label, detail=None):
    CHECKS.append({'check':label,'pass':bool(ok),'detail':detail})
    print(('PASS ' if ok else 'FAIL ')+label)
def load(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def literal_balance(text):
    # Lexical sanity only; intentionally not advertised as syntax compilation.
    text=re.sub(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*.*?\*/',' ',text,flags=re.S)
    stack=[];close={')':'(',']':'[','}':'{'}
    for c in text:
        if c in '([{': stack.append(c)
        elif c in close:
            if not stack or stack.pop()!=close[c]:return False
    return not stack

def run(upstream=None):
    source=ROOT/'src/CatenaryDC'; assets=ROOT/'unity_project/Assets'; cat=assets/'Catenary'
    provenance=load(ROOT/'audit/upstream.json'); original=provenance['source_files']
    info=load(ROOT/'info.json'); project=ET.parse(source/'CatenaryDC.csproj')
    check(provenance['zip_commit_comment']=='4d1a6a691415b6f08f9a76fbafdc8357485e277a','Pinned uploaded upstream snapshot')
    check(info['Id']=='DerailValleyCatenaryDC' and info['AssemblyName']=='CatenaryDC.dll' and info['EntryMethod']=='CatenaryDC.Main.Load','Independent UMM/assembly identity')
    check(not info.get('Repository'),'No upstream auto-updater URL inherited')
    check(project.findtext('./PropertyGroup/TargetFramework')=='net48','Game target remains .NET Framework 4.8')
    check(not project.findall('.//Target[@Name="PostBuild"]'),'No legacy post-build install/packaging hook')
    refs=project.findall('.//Reference')
    check(all('$(GameManagedPath)' in (r.findtext('HintPath') or '') for r in refs),'All core game references are configurable, not author E: paths',len(refs))
    check(all(r.findtext('Private')=='false' for r in refs),'No game reference marked CopyLocal')
    cs=list(source.rglob('*.cs')); all_cs='\n'.join(p.read_text(encoding='utf-8-sig') for p in cs)
    check(not any((ROOT/p).exists() for p in ['WE6SIM','we6.blend','Tests/TM_rig.cs','unity_project/Assets/_CCL_CARS','unity_project/Assets/CarCreator']),'Locomotive project, CCL tools/models and traction tests removed')
    check(not any((source/p).exists() for p in ['devices','unit_A','unit_B','circuit']),'Core has no locomotive implementation directories')
    check(not re.search(r'^using\s+(?:electric_sim|LocoSim)',all_cs,re.M),'Core has no old engine or locomotive namespace imports')
    check('car_spawn_handler.simulate_all_units' not in all_cs and 'diagnostics?.Value' not in all_cs,'No locomotive ticking or telemetry in core')
    check(all(literal_balance(p.read_text(encoding='utf-8-sig')) for p in cs),'C# lexical brace/quote sanity (NOT compilation)')
    preprocessor=[]
    for p in cs:
        stack=[]
        for ln in p.read_text().splitlines():
            if re.match(r'\s*#if\b',ln):stack.append(ln)
            elif re.match(r'\s*#endif\b',ln):
                if not stack:preprocessor.append(str(p));break
                stack.pop()
        if stack:preprocessor.append(str(p))
    check(not preprocessor,'Balanced conditional compilation blocks',preprocessor)
    retained=list(cat.rglob('*')); retained=[p for p in retained if p.is_file()]
    check(all(original.get(p.relative_to(ROOT).as_posix())==sha(p) for p in retained),'All retained catenary assets/meta files byte-identical to upload',len(retained))
    check(all(original.get(name)==sha(ROOT/name) for name in ['LICENSE_CC0.txt','LICENSE_MIT.txt']),'Both upstream license texts preserved byte-for-byte')
    check((ROOT/'NOTICE.md').is_file(),'Attribution and scope notice included')
    guids={}; duplicates=[]
    for p in assets.rglob('*.meta'):
        m=re.search(r'^guid:\s*([0-9a-f]{32})\s*$',p.read_text(errors='replace'),re.M)
        if m:
            if m[1] in guids:duplicates.append(m[1])
            guids[m[1]]=p
    unresolved=[]
    for p in list(assets.rglob('*'))+list((ROOT/'unity_project/ProjectSettings').rglob('*')):
        if not p.is_file() or p.suffix not in {'.prefab','.mat','.asset','.meta','.unity'}:continue
        for g in re.findall(r'guid:\s*([0-9a-f]{32})',p.read_text(errors='replace')):
            if g.startswith('0000000000000000'):continue
            if g not in guids:unresolved.append((p.relative_to(ROOT).as_posix(),g))
    check(not duplicates,'Unique retained Unity GUIDs',duplicates)
    check(not unresolved,'Unity YAML references resolve inside retained assets or built-ins',unresolved)
    check('m_Scenes: []' in (ROOT/'unity_project/ProjectSettings/EditorBuildSettings.asset').read_text(),'Removed locomotive scenes from Unity build list')
    check('2019.4.41f2' in (ROOT/'unity_project/ProjectSettings/ProjectVersion.txt').read_text(),'Pinned supplied Unity Editor version')
    tables={}
    for name in ['poles','wires','cantilevers','gantries','side_poles','miscellaneous']:
        tables[name]=list(csv.DictReader((cat/(name+'.csv')).open(encoding='utf-8-sig',newline='')))
        check(len(tables[name])>0 and all(None not in r and None not in r.values() for r in tables[name]),'CSV schema '+name,len(tables[name]))
    missing=[]
    for name,rows in tables.items():
        for row in rows:
            for col in ['Asset','FixedPartAsset']:
                if row.get(col) and not (cat/(row[col]+'.prefab')).is_file():missing.append((name,row[col]))
    check(not missing,'CSV prefab assets all present',missing)
    check({'plain_half1_single','plain_half2_single','wall_anchor_half_single'}<={x['Type'] for x in tables['wires']},'New half-section families retained')
    check(any(x['SteadyArmType']=='intermediate' for x in tables['cantilevers']),'Special intermediate steady-arm assets retained')
    layouts=sorted((cat/'Scenery').glob('*.json')); objects=[]; malformed=[]
    prefix='electric_sim.catenary.overhead_equipment+'
    allowed={'wire','pole','cantilever','gantry','substation','side_rail_pole','miscellaneous_object'}
    for path in layouts:
        for index,obj in enumerate(load(path)):
            t=obj.get('$type','');kind=t.split('+')[-1].split(',')[0]
            if not t.startswith(prefix) or kind not in allowed:malformed.append((path.name,index,t))
            objects.append((path.name,index,kind,obj))
    check(len(layouts)==31,'31 upstream scenery files retained (reference profile)',len(layouts))
    check(not malformed,'Only known legacy scenery types in reference layouts',malformed)
    definitions=collections.Counter(o['map_location'] for _,_,k,o in objects if k=='substation')
    check(all(n==1 for n in definitions.values()),'Unique source identifiers',dict(definitions))
    wires=[o for _,_,k,o in objects if k=='wire']; unknown=sorted({o['substation'] for o in wires}-set(definitions)-{'NEUTRAL'})
    check(not unknown,'Every reference wire supply exists',unknown)
    check(all(math.isfinite(o['length']) and 0<o['length']<=2000 and math.isfinite(o['previous_pole_vertical_offset']) for o in wires),'Reference wire lengths/heights pass new input guards')
    fixed={r['Type']:float(r['FixedPartLength']) for r in tables['wires']}
    check(all(o['length']>fixed.get(o['wire_type'],0) for o in wires),'Anchor models fit their serialized span length')
    enums={'wire':('wire_type',{r['Type'] for r in tables['wires']}),'pole':('pole_type',{r['Placement'] for r in tables['poles']}),'side_rail_pole':('kind',{r['Kind'] for r in tables['side_poles']}),'miscellaneous_object':('template_name',{r['Template'] for r in tables['miscellaneous']}|{'GantryArrow'})}
    for kind,(field,values) in enums.items():
        bad=[(f,i,o.get(field)) for f,i,k,o in objects if k==kind and o.get(field) not in values]
        if kind=='side_rail_pole':
            bad=[(f,i,o.get('kind')) for f,i,k,o in objects if k==kind and o.get('kind','SideRail') not in values]
        check(not bad,'Scenery type references: '+kind,bad[:5])
    combos={(r['SteadyArmType'],r['CantileverType'],r['DualWire'].lower()=='true') for r in tables['cantilevers']}
    bad=[(f,i) for f,i,k,o in objects if k=='cantilever' and (o.get('steady_arm_type'),o.get('cantilever_type'),o.get('dual_wire',False)) not in combos]
    check(not bad,'All retained cantilever model combinations exist',bad[:10])
    profile=load(ROOT/'config/network.json')
    check(profile['included_scenery'] is None and profile['name']=='author-reference','Default is explicitly author-reference, not unbuilt coast route')
    check(not list((ROOT/'Scenery').glob('*.json')),'No hidden route/override installed by default')
    plan=load(ROOT/'plans/phase1_hb_cs_cms.json')
    check(plan['status']=='planned' and not plan['geometry_present'] and plan['station_yards'] is False,'Phase 1 is mainline-only and not falsely marked implemented')
    wire=(source/'catenary/wire.cs').read_text(); sub=(source/'catenary/substation.cs').read_text(); engine=(source/'catenary/overhead_equipment.cs').read_text()
    check('#else\n            return _wire_sections[wire_type].template;\n#endif' in wire,'Release get_template fallback retained (CS0571)')
    check('? "ShortWireMarker"' in wire,'Newest supplied Debug short-wire behavior retained')
    check('public override bool reveal()' in sub and '#else\n            return false;\n#endif' in sub,'Release hides substation marker, not its power state')
    check('ElectricalMath.Distance' in sub and 'dz * dz' in (source/'Power/ElectricalMath.cs').read_text(),'Voltage distance accounts for Z squared')
    check('this.maximum_load = maximum_load;' in sub,'Serialization retains nominal load limit (no multiplier compounding)')
    check('Task.Delay' not in sub and 'SubstationState' in sub,'Core breaker restoration is per-world main-thread state')
    check('CatenaryApi.RaiseSimulationStep();' in (source/'catenary/OCS_ticker.cs').read_text(),'Vehicle-neutral pre-substation simulation event')
    binder=(source/'Serialization/SceneryBinder.cs').read_text()
    check('SerializationBinder = new SceneryBinder()' in binder and 'Unsupported scenery class' in binder,'Restrictive legacy JSON type migration binder')
    check('dispose(saveEditor: false)' in engine and '_loaded_bundles.Add(catenary_assets)' in engine,'Partial initialization rollback owns loaded bundles')
    check('WorldMoved -= old.floating_origin_shift' in engine and '_system = null' in engine,'World teardown clears origin listener and singleton')
    check('all_wires.Max(w => w.length)' in engine,'Contact search radius includes the longest serialized span')
    check('GameObject.Destroy(_monitor)' in (source/'catenary_editor/mow_follower.cs').read_text(),'Debug monitor removed when editor vehicle is disposed')
    check('tunnel_ranges.Count > 0' in (source/'catenary/edit_support.cs').read_text(),'Empty tunnel range diagnostic guarded')
    export=(assets/'Editor/CatenaryBundleBuild.cs').read_text()
    check('string[] names = { "catenary", "catenary_parts" }' in export and 'GetAssetPathsFromAssetBundle' in export,'Unity exporter explicitly builds only two catenary bundles')
    check('catenarydc-export.json' in export and 'SHA256.Create' in export,'Unity exporter creates source/bundle hash receipt')
    build=(ROOT/'tools/Build.ps1').read_text()
    check('Compare-Object' in build and 'source_files' in build and 'Sha $full' in build,'Build checks both content hashes and additions/deletions after export')
    check('ready-to-install' in build and 'OutputDirectory must be OUTSIDE' in build and 'No automatic installation' in build,'Build stages outside game; does not deploy')
    check('File]::Replace' not in build,'No problematic installer File.Replace/$null path in build script')
    check(not any(p.suffix.lower() in {'.dll','.pdb','.exe'} for p in ROOT.rglob('*') if p.is_file()),'No game DLLs or generated binaries in source tree')
    check('geometry_present' in (ROOT/'plans/phase1_hb_cs_cms.json').read_text(),'Plan and runtime payload are separate')
    if upstream:
        upstream=upstream.resolve()
        check(all(sha(upstream/p.relative_to(ROOT))==sha(p) for p in retained),'Direct input-directory comparison: catenary assets unchanged')
        spec=importlib.util.spec_from_file_location('bridge_prepare',ROOT/'tools/prepare_legacy_bridge.py');mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)
        with tempfile.TemporaryDirectory() as temp:
            out=Path(temp)/'controller'; report=mod.prepare(upstream,out,Path(temp)/'CatenaryDC.dll')
            check(report['contact_queries_redirected']==2,'Optional bridge: both collector queries migrated')
            check(not (out/'catenary').exists() and not (out/'catenary_editor').exists(),'Optional bridge contains no second network/editor engine')
            text='\n'.join(p.read_text(encoding='utf-8-sig') for p in out.rglob('*.cs'))
            check('overhead_equipment' not in text,'Optional bridge has no old OCS calls')
            spawn=(out/'car_spawn_handler.cs').read_text()
            check('CatenaryApi.SimulationStep += simulate_all_units' in spawn and 'CatenaryApi.SimulationStep -= simulate_all_units' in spawn,'Optional bridge subscribes/unsubscribes single core tick')
            binfo=load(out/'info.json')
            check(binfo['Id']=='Catenary-DC' and binfo['Requirements']==['DerailValleyCatenaryDC-0.1.0'],'Optional controller preserves the CCL pack dependency ID')
            check(all(literal_balance(p.read_text(encoding='utf-8-sig')) for p in out.rglob('*.cs')),'Prepared adapter lexical sanity (NOT compilation)')
            unchanged=all(sha(upstream/k)==v for k,v in report['verified_files'].items())
            check(unchanged,'Adapter preparation did not modify original source',len(report['verified_files']))
            try: mod.prepare(upstream,out,Path(temp)/'CatenaryDC.dll');rejected=False
            except ValueError:rejected=True
            check(rejected,'Adapter preparation refuses nonempty output')
    result={'project':'Derail-Valley-Catenary-DC','version':info['Version'],'source_commit':provenance['zip_commit_comment'],
            'passed':sum(c['pass'] for c in CHECKS),'failed':sum(not c['pass'] for c in CHECKS),'checks':CHECKS,
            'scenery_files':len(layouts),'object_counts':dict(collections.Counter(k for _,_,k,_ in objects)),
            'retained_asset_files':len(retained),'core_csharp_files':len(cs),
            'maximum_reference_wire_length_m':max(o['length'] for o in wires),
            'csharp_compiled':False,'csharp_logic_tests_executed':False,'unity_export_executed':False,
            'powershell_executed':False,'game_tested':False,'phase1_route_built':False}
    (ROOT/'audit').mkdir(exist_ok=True)
    (ROOT/'audit/offline_validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    print(f'RESULT: {result["passed"]}/{len(CHECKS)} offline checks passed. C#/Unity/game NOT executed.')
    return int(result['failed']>0)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--upstream',type=Path)
    args=p.parse_args();raise SystemExit(run(args.upstream))
