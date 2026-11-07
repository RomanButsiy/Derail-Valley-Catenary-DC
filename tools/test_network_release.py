"""Regression checks for lost/duplicated geometry and broken split mappings."""
import json
from pathlib import Path
import shutil
import tempfile
import unittest

from verify_network_release import ROOT, SCENERY, verify


class NetworkReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'audit').mkdir()
        shutil.copy2(ROOT / 'audit/network_6m.json', self.root / 'audit/network_6m.json')
        shutil.copytree(ROOT / SCENERY, self.root / SCENERY)

    def edit_layout(self, change):
        path = self.root / SCENERY / 'scenery_GF.json'
        rows = json.loads(path.read_bytes())
        change(rows)
        path.write_text(json.dumps(rows))

    def test_accepted_network_passes(self):
        self.assertTrue(verify(self.root)['passed'])

    def test_changed_position_is_rejected(self):
        self.edit_layout(lambda rows: rows[1].update(y=rows[1]['y'] + .01))
        errors = verify(self.root)['errors']
        self.assertTrue(any('reconstruction differs' in e for e in errors))

    def test_duplicate_object_is_rejected(self):
        self.edit_layout(lambda rows: rows.append(rows[-1].copy()))
        errors = verify(self.root)['errors']
        self.assertTrue(any('multiplicities' in e for e in errors))

    def test_missing_object_is_rejected(self):
        self.edit_layout(lambda rows: rows.pop())
        self.assertFalse(verify(self.root)['passed'])

    def test_wrong_bundle_is_rejected(self):
        path = self.root / SCENERY / 'scenery_GF-SM[GF].json.meta'
        path.write_text(path.read_text().replace('assetBundleName: catenary', 'assetBundleName: wrong'))
        self.assertTrue(any('Unity metadata' in e for e in verify(self.root)['errors']))

    def test_duplicate_mapping_is_rejected(self):
        path = self.root / 'audit/network_6m.json'
        data = json.loads(path.read_bytes())
        data['source_mapping'].append(data['source_mapping'][0])
        path.write_text(json.dumps(data))
        self.assertTrue(any('Duplicate mapping' in e for e in verify(self.root)['errors']))

    def test_retired_monolith_is_rejected(self):
        folder = self.root / 'Scenery'
        folder.mkdir()
        (folder / 'Scenery_GF_Station_Transit.json').write_text('[]')
        self.assertTrue(any('Retired monolith' in e for e in verify(self.root)['errors']))


if __name__ == '__main__':
    unittest.main()
