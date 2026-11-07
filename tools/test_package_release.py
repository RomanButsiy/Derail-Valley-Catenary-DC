"""Release packaging checks with temporary synthetic bytes, never real mod builds."""
import json
from pathlib import Path
import shutil
import tempfile
import unittest
import zipfile

import package_release as release


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.core, self.bridge, self.output = (self.root/n for n in ('core', 'bridge', 'output'))
        for folder, names in ((self.core, release.CORE_FILES), (self.bridge, release.BRIDGE_FILES)):
            for name in names:
                path = folder/name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b'Packaging test fixture, NOT a built mod: '+name.encode())
            for name in ('LICENSE_CC0.txt', 'LICENSE_MIT.txt', 'NOTICE.md'):
                shutil.copy2(release.ROOT/name, folder/name)
        shutil.copy2(release.ROOT/'config/network.json', self.core/'network.json')
        self.write(self.core/'info.json', dict(Id='DerailValleyCatenaryDC', AssemblyName='CatenaryDC.dll', Version='0.2.0'))
        self.write(self.bridge/'info.json', dict(Id='Catenary-DC', AssemblyName='CatenaryDC.Legacy2WE3.dll', Version='0.2.0',
                                                Requirements=['DerailValleyCatenaryDC-0.2.0']))
        self.write(self.core/'build-info.json', dict(project='Derail-Valley-Catenary-DC', version='0.2.0', configuration='Release',
                   core_sha256=release.sha(self.core/'CatenaryDC.dll'), catenary_sha256=release.sha(self.core/'catenary'),
                   parts_sha256=release.sha(self.core/'catenary_parts'), scenery_revision=release.load(release.ROOT/'audit/network_6m.json')['revision']))
        self.write(self.bridge/'build-info.json', dict(project='Legacy2WE3 bridge', version='0.2.0', configuration='Release',
                   core_sha256=release.sha(self.core/'CatenaryDC.dll'), controller_sha256=release.sha(self.bridge/'CatenaryDC.Legacy2WE3.dll'),
                   upstream_commit=release.UPSTREAM, build_fix_revision=release.REVISION))
        self.write(self.bridge/'preparation.json', dict(bridge_version='0.2.0', compiled=True, catenary_engine_copied=False,
                   simulate_named_arguments_fixed=0, upstream_commit=release.UPSTREAM, build_fix_revision=release.REVISION))

    def write(self, path, data):
        # Windows PowerShell 5 writes these JSON receipts as UTF-8 with BOM.
        path.write_text(json.dumps(data, indent=2)+'\n', encoding='utf-8-sig')

    def change(self, path, **fields):
        data = release.load(path)
        data.update(fields)
        self.write(path, data)

    def package(self):
        return release.package(self.core, self.bridge, self.output)

    def test_matching_pair_contents_and_checksums(self):
        report = self.package()
        self.assertFalse(report['published'])
        self.assertFalse(report['runtime_tested_by_packager'])
        self.assertEqual(len(report['components']), 2)
        for component, folder, names in zip(report['components'], ('Derail-Valley-Catenary-DC', 'Derail-Valley-Catenary-DC-2WE3-Bridge'),
                                            (release.CORE_FILES, release.BRIDGE_FILES)):
            with zipfile.ZipFile(self.output/component['file']) as archive:
                self.assertEqual(set(archive.namelist()), {folder+'/'+n for n in names})
                for name, expected in component['contents'].items():
                    import hashlib
                    self.assertEqual(hashlib.sha256(archive.read(folder+'/'+name)).hexdigest(), expected)
        sums = (self.output/'SHA256SUMS.txt').read_text().splitlines()
        self.assertEqual(len(sums), 4)
        for line in sums:
            digest, name = line.split('  ', 1)
            self.assertEqual(release.sha(self.output/name), digest)

    def test_archives_reproducible(self):
        first = self.package()
        second = release.package(self.core, self.bridge, self.root/'another-output')
        self.assertEqual(first['components'], second['components'])

    def test_bridge_must_match_core(self):
        self.change(self.bridge/'build-info.json', core_sha256='0'*64)
        with self.assertRaisesRegex(ValueError, 'different core'):
            self.package()
        self.assertFalse(self.output.exists())

    def test_changed_binaries_or_bundles_rejected(self):
        for folder, name in ((self.core, 'CatenaryDC.dll'), (self.core, 'catenary'), (self.core, 'catenary_parts'),
                              (self.bridge, 'CatenaryDC.Legacy2WE3.dll')):
            with self.subTest(name=name):
                path = folder/name
                before = path.read_bytes()
                path.write_bytes(before+b'changed')
                with self.assertRaisesRegex(ValueError, 'hash mismatch'):
                    self.package()
                path.write_bytes(before)

    def test_debug_rejected(self):
        self.change(self.core/'build-info.json', configuration='Debug')
        with self.assertRaisesRegex(ValueError, 'Release builds'):
            self.package()

    def test_wrong_upstream_rejected(self):
        self.change(self.bridge/'build-info.json', upstream_commit='4d1a6a6')
        with self.assertRaisesRegex(ValueError, 'e0b78cb'):
            self.package()

    def test_extra_game_dll_or_override_rejected(self):
        for path in (self.bridge/'Assembly-CSharp.dll', self.core/'Scenery/scenery_gf_station_transit.json'):
            with self.subTest(path=path.name):
                path.write_bytes(b'unexpected')
                with self.assertRaisesRegex(ValueError, 'extra='):
                    self.package()
                path.unlink()

    def test_versions_must_match(self):
        self.change(self.bridge/'info.json', Version='0.1.0')
        with self.assertRaisesRegex(ValueError, 'versions differ'):
            self.package()

    def test_paths_must_be_redacted(self):
        self.change(self.bridge/'preparation.json', source='C:\\Users\\Example\\Author')
        with self.assertRaisesRegex(ValueError, 'local workspace paths'):
            self.package()

    def test_stale_notice_rejected(self):
        (self.bridge/'NOTICE.md').write_text('Old notice')
        with self.assertRaisesRegex(ValueError, 'stale release notice'):
            self.package()

    def test_stale_scenery_or_profile_rejected(self):
        self.change(self.core/'build-info.json', scenery_revision='old')
        with self.assertRaisesRegex(ValueError, 'scenery revision'):
            self.package()
        self.change(self.core/'build-info.json', scenery_revision=release.load(release.ROOT/'audit/network_6m.json')['revision'])
        self.change(self.core/'network.json', name='another-profile')
        with self.assertRaisesRegex(ValueError, 'network profile'):
            self.package()

    def test_existing_or_nested_output_rejected(self):
        with self.assertRaisesRegex(ValueError, 'separate'):
            release.package(self.core, self.bridge, self.core/'release')
        self.output.mkdir()
        (self.output/'keep.txt').write_text('Do not overwrite')
        with self.assertRaisesRegex(ValueError, 'new or empty'):
            self.package()
        self.assertEqual((self.output/'keep.txt').read_text(), 'Do not overwrite')


if __name__ == '__main__':
    unittest.main()
