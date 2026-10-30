#!/usr/bin/env python3
"""Offline/data + source-regression tests. NOT a substitute for C# compilation or Unity execution."""
import copy
import csv
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET
import authoring_tool as tool

ROOT = Path(__file__).resolve().parents[1]
CSV = ROOT / 'unity_project/Assets/Catenary'
SRC = ROOT / 'src/CatenaryDC'


def fixture_records():
    return [{'kind': 'header', 'schema_version': 3, 'scene_geometry_complete': False, 'assets': [{'name': 'catenary', 'sha256': 'a'}]},
            {'kind': 'track', 'id': 'T1', 'points_native_m': [{'x': 1, 'y': 2, 'z': 3}, {'x': 4, 'y': 2, 'z': 3}], 'native_frame_confirmed': False},
            {'kind': 'existing_scenery', 'authored_root': False, 'data': {'template_name': 'PoleFoundation'}},
            {'kind': 'footer', 'export_complete': True, 'record_errors': 0, 'scene_geometry_complete': False}]


def save_capture(path, records):
    path.write_text('\n'.join(tool.canonical(x) for x in records) + '\n', encoding='utf-8')


def sample_object(kind='substation'):
    data = {'$type': 'electric_sim.catenary.overhead_equipment+' + kind + ', electric_sim',
            'x': 1000000, 'z': 2000000, 'y': 100.0, 'orientation': {'x': 0, 'y': 0, 'z': 0, 'w': 1}}
    if kind == 'substation': data.update(map_location='TEST1500', supply_voltage=1650, maximum_load=4500, has_inverter=True)
    if kind == 'wire': data.update(wire_type='plain_single', substation='TEST1500', length=40, previous_pole_vertical_offset=0)
    return data


class DataValidation(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tables = {}
        for p in CSV.glob('*.csv'):
            with p.open(encoding='utf-8-sig') as f: cls.tables[p.stem] = list(csv.DictReader(f))
    def check_objects(self, objects): return tool.validate_sources({'scenery_test.json': objects}, self.tables)
    def test_reference_layouts(self):
        sources = {p.name: tool.json_loads(p.read_text()) for p in (CSV/'Scenery').glob('*.json')}
        result = tool.validate_sources(sources, self.tables)
        self.assertTrue(result['data_valid'], result['errors'][:3]); self.assertEqual(result['objects'], 11069)
    def test_valid_substation_and_wire(self): self.assertTrue(self.check_objects([sample_object(),sample_object('wire')])['data_valid'])
    def test_missing_substation(self): self.assertFalse(self.check_objects([sample_object('wire')])['data_valid'])
    def test_duplicate_substation(self): self.assertFalse(self.check_objects([sample_object(),sample_object()])['data_valid'])
    def test_reserved_neutral_id(self):
        o=sample_object();o['map_location']='NEUTRAL';self.assertFalse(self.check_objects([o])['data_valid'])
    def test_bad_assembly(self):
        o=sample_object();o['$type']='X.Y, System';self.assertFalse(self.check_objects([o])['data_valid'])
    def test_bad_type(self):
        o=sample_object();o['$type']=o['$type'].replace('substation','anything');self.assertFalse(self.check_objects([o])['data_valid'])
    def test_zero_quaternion(self):
        o=sample_object();o['orientation']['w']=0;self.assertFalse(self.check_objects([o])['data_valid'])
    def test_nonfinite_coordinate(self):
        o=sample_object();o['y']=float('nan');self.assertFalse(self.check_objects([o])['data_valid'])
    def test_mm_mismatch_fraction(self):
        o=sample_object();o['x']=1234.5;self.assertFalse(self.check_objects([o])['data_valid'])
    def test_int_overflow(self):
        o=sample_object();o['x']=2**31;self.assertFalse(self.check_objects([o])['data_valid'])
    def test_reference_metadata(self):
        o=sample_object();o['$ref']='something';self.assertFalse(self.check_objects([o])['data_valid'])
    def test_nested_type_metadata(self):
        o=sample_object();o['orientation']['$type']='X';self.assertFalse(self.check_objects([o])['data_valid'])
    def test_invalid_wire_type(self):
        o=sample_object('wire');o['wire_type']='invisible_power';self.assertFalse(self.check_objects([sample_object(),o])['data_valid'])
    def test_invalid_length(self):
        for length in [0,-1,2001]:
            o=sample_object('wire');o['length']=length;self.assertFalse(self.check_objects([sample_object(),o])['data_valid'])
    def test_short_span_warning(self):
        o=sample_object('wire');o['length']=20;r=self.check_objects([sample_object(),o]);self.assertTrue(r['data_valid']);self.assertTrue(r['warnings'])
    def test_half_section_no_short_marker_warning(self):
        o=sample_object('wire');o['length']=20;o['wire_type']='plain_half1_single';r=self.check_objects([sample_object(),o]);self.assertTrue(r['data_valid']);self.assertFalse(r['warnings'])
    def test_invalid_cantilever_combination(self):
        o=sample_object('cantilever');o.update(steady_arm_type='not-a-model',cantilever_type='Middle',dual_wire=False)
        self.assertFalse(self.check_objects([o])['data_valid'])
    def test_duplicate_json_keys_rejected(self):
        with self.assertRaises(ValueError): tool.json_loads('{"x":1,"x":2}')
    def test_nonfinite_literal_rejected(self):
        with self.assertRaises(ValueError): tool.json_loads('{"y":NaN}')
    def test_no_input_mutation(self):
        objects=[sample_object(),sample_object('wire')];before=copy.deepcopy(objects);self.check_objects(objects);self.assertEqual(before,objects)


class CaptureProcessing(unittest.TestCase):
    def test_complete_capture(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'survey.jsonl';save_capture(p,fixture_records());self.assertEqual(len(tool.read_capture(p)),4)
    def test_partial_filename_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'survey.partial';save_capture(p,fixture_records())
            with self.assertRaises(ValueError):tool.read_capture(p)
    def test_missing_footer_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'survey.jsonl';save_capture(p,fixture_records()[:-1])
            with self.assertRaises(ValueError):tool.read_capture(p)
    def test_cancelled_route_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'route.jsonl';save_capture(p,[{'kind':'route_header'},{'kind':'route_footer','export_complete':False}])
            with self.assertRaises(ValueError):tool.read_capture(p)
    def test_same_track_observation_dedup(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'a.jsonl';q=Path(d)/'b.jsonl';save_capture(p,fixture_records());save_capture(q,fixture_records())
            r=tool.extract([p,q],Path(d)/'out');self.assertEqual(r['track_keys'],1);self.assertEqual(r['conflicts'],0)
    def test_track_geometry_conflict_preserved(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'a.jsonl';q=Path(d)/'b.jsonl';a=fixture_records();b=copy.deepcopy(a);b[1]['points_native_m'][0]['x']+=100
            save_capture(p,a);save_capture(q,b);r=tool.extract([p,q],Path(d)/'out');self.assertEqual(r['conflicts'],1)
            out=json.loads((Path(d)/'out/tracks_observations.json').read_text());self.assertEqual(len(out['T1']),2)
    def test_bundle_versions_must_match(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'a.jsonl';q=Path(d)/'b.jsonl';a=fixture_records();b=copy.deepcopy(a);b[0]['assets'][0]['sha256']='b';save_capture(p,a);save_capture(q,b)
            with self.assertRaises(ValueError):tool.extract([p,q],Path(d)/'out')
    def test_output_refuses_overwrite(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'a.jsonl';save_capture(p,fixture_records());out=Path(d)/'out';out.mkdir();(out/'important').write_text('keep')
            with self.assertRaises(ValueError):tool.extract([p],out)
            self.assertEqual((out/'important').read_text(),'keep')
    def test_untrusted_source_name_not_a_path(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'a.jsonl';a=fixture_records();a.insert(-1,{'kind':'source_layout','name':'../../unsafe.json','objects':[]});save_capture(p,a)
            tool.extract([p],Path(d)/'out');self.assertFalse((Path(d)/'unsafe.json').exists());self.assertEqual(len(list((Path(d)/'out/source_snapshots').glob('*.json'))),1)
    def test_generated_objects_not_converted_to_roots(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'a.jsonl';save_capture(p,fixture_records());tool.extract([p],Path(d)/'out')
            observations=(Path(d)/'out/scene_observations.jsonl').read_text();self.assertIn('"authored_root":false',observations)
            self.assertFalse(list((Path(d)/'out').glob('scenery_*.json')))
    def test_probe_stream(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'p.jsonl';save_capture(p,[{'kind':'probe_header'},{'kind':'footer','export_complete':True}]);self.assertEqual(len(tool.read_capture(p)),2)


class SourceSafety(unittest.TestCase):
    def test_core_stays_csharp12_net48_api1(self):
        root=ET.parse(SRC/'CatenaryDC.csproj').getroot();self.assertEqual(root.findtext('.//TargetFramework'),'net48');self.assertEqual(root.findtext('.//LangVersion'),'12.0');self.assertEqual(root.findtext('.//AssemblyVersion'),'0.1.0.0')
        self.assertIn('ApiVersion => 1',(SRC/'Api/CatenaryApi.cs').read_text())
    def test_debug_assignment_rewritten(self):
        s=(SRC/'catenary_editor/editor.cs').read_text();self.assertNotIn('closest_gantry?.stretch =',s);self.assertIn('if (closest_gantry != null) closest_gantry.stretch = gantry_stretch;',s)
    def test_no_csharp14_conditional_assignment_in_core(self):
        import re
        pattern=re.compile(r'\?\.[A-Za-z_]\w*\s*(?:[+\-*/]?=(?!=))')
        matches=[str(f) for f in SRC.rglob('*.cs') if pattern.search(f.read_text())];self.assertEqual(matches,[])
    def test_tools_not_debug_only(self):
        s=(SRC/'Authoring/WorldSurvey.cs').read_text();self.assertNotIn('#if DEBUG',s)
        settings=(SRC/'catenary_editor/editor_settings.cs').read_text();self.assertIn('overhead_equipment.draw_authoring_gui(m)',settings)
    def test_roots_exclude_implicit_children(self):
        s=(SRC/'Authoring/AuthoringTools.cs').read_text();self.assertIn('_survey_sources.ContainsKey(item) || !item.placed_procedurally',s);self.assertIn('if (!authoring_is_root(item))',s)
        core=(SRC/'catenary/overhead_equipment.cs').read_text();self.assertIn('_survey_sources[loaded_objects[i]]',core)
    def test_no_constructing_scenery_in_review(self):
        s=(SRC/'Authoring/SceneryReview.cs').read_text();self.assertNotIn('DeserializeObject',s);self.assertNotIn('Activator',s);self.assertNotIn('new pole',s)
    def test_only_safe_json_properties_exported(self):
        s=(SRC/'Authoring/AuthoringTools.cs').read_text();self.assertIn('GetCustomAttributes(typeof(JsonPropertyAttribute)',s);self.assertIn('Unexpected scenery data member',s)
    def test_partial_closed_on_world_unload(self):
        core=(SRC/'catenary/overhead_equipment.cs').read_text();self.assertIn('old.authoring_stop();',core)
        s=(SRC/'Authoring/WorldSurvey.cs').read_text();self.assertIn('survey_cleanup()',s);self.assertIn('File.Move(temporary, destination)',s)
    def test_surface_formats_and_mesh_limits(self):
        s=(SRC/'Authoring/WorldSurvey.cs').read_text();self.assertIn('scene_geometry_complete',s);self.assertIn('native_frame_confirmed',s);self.assertIn('survey_mesh_total_vertex_limit',s)
        self.assertIn('mesh.isReadable',s)
    def test_neutral_contact_does_not_query_power(self):
        files='\n'.join(p.read_text() for p in (SRC/'Authoring').glob('*.cs'))
        self.assertNotIn('.wire_voltage(',files);self.assertNotIn('.QueryRelativeContact(',files)
    def test_gui_restore(self):
        s=(SRC/'Authoring/AuthoringTools.cs').read_text();self.assertIn('finally { GUI.enabled = enabled; }',s)
    def test_physics_terrain_dependencies(self):
        s=(SRC/'CatenaryDC.csproj').read_text();self.assertIn('UnityEngine.PhysicsModule',s);self.assertIn('UnityEngine.TerrainModule',s)
    def test_route_coordinates_not_rail_claim(self):
        s=(SRC/'Authoring/AuthoringTools.cs').read_text();self.assertIn('["not_track_geometry"] = true',s);self.assertIn('authoring_stop_route(false)',s)
    def test_native_track_frame_not_guessed(self):
        s=(SRC/'Authoring/WorldSurvey.cs').read_text();self.assertIn('["native_frame_confirmed"] = false',s);self.assertIn('adjacency is not guessed',s)
    def test_runtime_and_tools_csv_keys_exist(self):
        text=(SRC/'Authoring/SceneryReview.cs').read_text()
        self.assertIn('definition["IsEndAnchor"]',text);self.assertIn('definition["FixedPart"]',text)
        self.assertNotIn('definition["EndAnchor"]',text);self.assertNotIn('definition["FixedTemplate"]',text)
    def test_legacy_controller_is_not_required_to_export(self):
        text='\n'.join(f.read_text() for f in (SRC/'Authoring').glob('*.cs'))
        self.assertNotIn('LocoSim.',text);self.assertNotIn('GaugeNeedleSmoothing',text)
    def test_bounds_selection(self):
        s=(SRC/'Authoring/AuthoringTools.cs').read_text();self.assertIn('bounds.ClosestPoint(_survey_player)',s)
    def test_no_preview_auto_import(self):
        s=(SRC/'Authoring/AuthoringTools.cs').read_text();self.assertIn('report["installed"] = false',s);self.assertNotIn('stuff_scenery(',s)


if __name__ == '__main__':
    unittest.main(verbosity=2)
