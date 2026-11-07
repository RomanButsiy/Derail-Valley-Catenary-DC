"""Source-only bridge tests. --source supplies the pinned author checkout."""
import argparse
import json
from pathlib import Path
import shutil
import tempfile
import unittest
import xml.etree.ElementTree as ET

import prepare_legacy_bridge as bridge

SOURCE = None
LEGACY_SOURCE = None


class GeneratorTests(unittest.TestCase):
    def setUp(self):
        if SOURCE is None:
            self.skipTest('Pass --source with the e0b78cb author checkout for integration tests.')
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.work = Path(self.temp.name)
        self.source = self.work/'upstream'
        manifest = json.loads((bridge.ROOT/'compat/Legacy2WE3/upstreams/e0b78cb.json').read_text())
        for name in manifest['source_files']:
            dest = self.source/name
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(SOURCE/name, dest)
        self.output = self.work/'controller'

    def prepare(self):
        return bridge.prepare(self.source, self.output, self.work/'not-built/CatenaryDC.dll')

    def test_new_source_preserves_author_controller(self):
        before = {p.relative_to(self.source):p.read_bytes() for p in self.source.rglob('*') if p.is_file()}
        report = self.prepare()
        self.assertEqual(report['upstream_commit'], bridge.UPSTREAM_REVISIONS['e0b78cb'])
        self.assertEqual(report['simulate_named_arguments_fixed'], 0)
        self.assertEqual(len(report['verified_files']), 49)
        self.assertFalse(report['compiled'])
        for folder in ('circuit', 'unit_A', 'unit_B', 'devices', 'utilities'):
            for path in (self.output/folder).rglob('*.cs'):
                if path.name == 'pantograph.cs':
                    continue  # Compared against the exact API-only transformation below.
                self.assertEqual(path.read_bytes(), (self.source/'WE6SIM'/path.relative_to(self.output)).read_bytes())
        for name, data in before.items():
            self.assertEqual((self.source/name).read_bytes(), data)

    def test_only_contact_and_lifecycle_adaptations(self):
        self.prepare()
        pan = (self.output/'devices/pantograph.cs').read_text()
        expected = (self.source/'WE6SIM/devices/pantograph.cs').read_text(encoding='utf-8-sig')
        expected = expected.replace('using electric_sim.catenary;', 'using CatenaryDC.Api;').replace(
            'overhead_equipment.system.relative_wire_height_and_voltage(', 'CatenaryApi.QueryRelativeContact(')
        self.assertEqual(pan, expected)
        self.assertEqual(pan.count('CatenaryApi.QueryRelativeContact('), 2)
        spawn = (self.output/'car_spawn_handler.cs').read_text()
        self.assertEqual(spawn.count('CatenaryApi.SimulationStep += simulate_all_units;'), 1)
        self.assertEqual(spawn.count('CatenaryApi.SimulationStep -= simulate_all_units;'), 1)
        self.assertIn('_spawner.CarSpawned -= on_car_spawned;', spawn)
        for forbidden in ('overhead_equipment', 'mow_follower', '_mow_', 'electric_sim.catenary;'):
            self.assertNotIn(forbidden, spawn)
        self.assertFalse((self.output/'catenary').exists())
        self.assertFalse((self.output/'utilities/CSV_struct.cs').exists())

    def test_new_project_and_dependency(self):
        self.prepare()
        root = ET.parse(self.output/'Legacy2WE3.csproj')
        self.assertEqual(root.findtext('./PropertyGroup/TargetFramework'), 'net48')
        self.assertEqual(root.findtext('./PropertyGroup/LangVersion'), '14.0')
        self.assertEqual(root.findtext('./PropertyGroup/Version'), '0.2.0')
        self.assertFalse(root.findall('.//Target[@Name="PostBuild"]'))
        self.assertTrue(all(r.findtext('Private')=='false' for r in root.findall('.//Reference')))
        info = json.loads((self.output/'info.json').read_text())
        self.assertEqual(info['Requirements'], ['DerailValleyCatenaryDC-0.2.0'])
        self.assertEqual(info['Id'], 'Catenary-DC')

    def test_edited_file_rejected_before_output(self):
        path = self.source/'WE6SIM/unit_A/unit_a_sim.cs'
        path.write_bytes(path.read_bytes()+b'\n// changed\n')
        with self.assertRaisesRegex(ValueError, 'Unsupported/edited'):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_missing_file_rejected(self):
        (self.source/'WE6SIM/circuit/circuit.cs').unlink()
        with self.assertRaisesRegex(ValueError, 'inventory'):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_extra_implementation_rejected(self):
        (self.source/'WE6SIM/devices/extra.cs').write_text('class Extra {}')
        with self.assertRaisesRegex(ValueError, 'inventory'):
            self.prepare()

    def test_windows_line_endings_accepted(self):
        for path in self.source.rglob('*'):
            if path.is_file():
                path.write_bytes(path.read_bytes().replace(b'\r\n', b'\n').replace(b'\n', b'\r\n'))
        self.assertEqual(self.prepare()['simulate_named_arguments_fixed'], 0)

    def test_refuses_reuse_of_output(self):
        self.prepare()
        with self.assertRaisesRegex(ValueError, 'new or empty'):
            self.prepare()

    def test_refuses_output_in_source(self):
        with self.assertRaisesRegex(ValueError, 'outside the original'):
            bridge.prepare(self.source, self.source/'generated', self.work/'core.dll')


class LegacyTests(unittest.TestCase):
    def test_old_revision_remains_explicit(self):
        if LEGACY_SOURCE is None:
            self.skipTest('Pass --legacy-source for the optional 4d1a6a6 compatibility test.')
        with tempfile.TemporaryDirectory() as temp:
            report = bridge.prepare(LEGACY_SOURCE, Path(temp)/'controller', Path(temp)/'core.dll', '4d1a6a6')
            self.assertEqual(report['simulate_named_arguments_fixed'], 2)
            self.assertEqual(report['upstream_commit'], bridge.UPSTREAM_REVISIONS['4d1a6a6'])
            caller = (Path(temp)/'controller/unit_A/unit_a_sim.cs').read_text()
            self.assertEqual(caller.count('permit_topology_changes:'), 2)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument('--source', type=Path)
    parser.add_argument('--legacy-source', type=Path)
    args, rest = parser.parse_known_args()
    SOURCE, LEGACY_SOURCE = args.source, args.legacy_source
    unittest.main(argv=['test_legacy_bridge.py', *rest])
