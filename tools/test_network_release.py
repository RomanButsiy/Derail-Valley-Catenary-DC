"""Regression checks for lost/duplicated geometry and broken split mappings."""
import json
import gzip
from pathlib import Path
import shutil
import tempfile
import unittest
import zipfile

from verify_network_release import ROOT, SCENERY, verify, sha, network_digest


class NetworkReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'audit').mkdir()
        shutil.copy2(ROOT / 'audit/network_6m.json', self.root / 'audit/network_6m.json')
        shutil.copy2(ROOT / 'audit/cp_pre_imw_mf.json.gz', self.root / 'audit/cp_pre_imw_mf.json.gz')
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

    def test_cp_reviewed_geometry_survives_inventory_refresh(self):
        path = self.root / SCENERY / 'scenery_CP.json'
        rows = json.loads(path.read_bytes())
        rows[-1]['y'] += .1
        path.write_text(json.dumps(rows))
        manifest = self.root / 'audit/network_6m.json'
        info = json.loads(manifest.read_bytes())
        info['assets'][path.name]['sha256'] = sha(path.read_bytes())
        layouts = {p.name: json.loads(p.read_bytes()) for p in (self.root / SCENERY).glob('*.json')}
        info['canonical_network_sha256'] = network_digest(layouts)
        manifest.write_text(json.dumps(info))
        self.assertTrue(any('Accepted CP payload differs' in e for e in verify(self.root)['errors']))

    def test_cp_archive_exact_comparison(self):
        manifest = self.root / 'audit/network_6m.json'
        info = json.loads(manifest.read_bytes())
        cp = info['cp_approaches']
        prior = json.loads(gzip.decompress((self.root / info['imw_mf']['cp_predecessor_path']).read_bytes()))
        archive_path = self.root / 'accepted-cp.zip'
        with zipfile.ZipFile(archive_path, 'w') as archive:
            for name, entry in cp['sources'].items():
                raw = prior[name].encode() if entry.get('superseded_by') else (self.root / SCENERY / entry['target']).read_bytes()
                archive.writestr(cp['accepted_archive_root'] + name, raw)
        cp['accepted_archive_sha256'] = sha(archive_path.read_bytes())
        manifest.write_text(json.dumps(info))
        result = verify(self.root, cp_accepted_zip=archive_path)
        self.assertTrue(result['passed'], result['errors'])
        self.assertTrue(result['cp_archive_comparison'])
        self.assertEqual(result['cp_approach_sources_verified'], 8)

    def test_changed_imw_payload_is_rejected_after_inventory_refresh(self):
        path = self.root / SCENERY / 'scenery_MF-Military.json'
        rows = json.loads(path.read_bytes()); rows[-1]['y'] += .1
        path.write_text(json.dumps(rows))
        manifest = self.root / 'audit/network_6m.json'
        info = json.loads(manifest.read_bytes())
        info['assets'][path.name]['sha256'] = sha(path.read_bytes())
        layouts = {p.name: json.loads(p.read_bytes()) for p in (self.root / SCENERY).glob('*.json')}
        info['canonical_network_sha256'] = network_digest(layouts)
        manifest.write_text(json.dumps(info))
        self.assertTrue(any('Accepted IMW-MF payload differs' in e for e in verify(self.root)['errors']))

    def test_cp_predecessor_corruption_is_rejected(self):
        (self.root / 'audit/cp_pre_imw_mf.json.gz').write_bytes(b'broken')
        self.assertTrue(any('Invalid CP predecessor' in e for e in verify(self.root)['errors']))

    def test_cp_supersession_cannot_hide_unrelated_geometry(self):
        manifest = self.root / 'audit/network_6m.json'
        info = json.loads(manifest.read_bytes())
        info['cp_approaches']['sources']['scenery_cp.json']['superseded_by'] = 'imw-mf-visual8'
        manifest.write_text(json.dumps(info))
        self.assertTrue(any('Invalid CP supersession' in e for e in verify(self.root)['errors']))

    def test_imw_archive_exact_comparison(self):
        manifest = self.root / 'audit/network_6m.json'
        info = json.loads(manifest.read_bytes()); imw = info['imw_mf']
        archive_path = self.root / 'accepted-imw.zip'
        with zipfile.ZipFile(archive_path, 'w') as archive:
            for name, entry in imw['sources'].items():
                archive.write(self.root / SCENERY / entry['target'], imw['accepted_archive_root'] + name)
        imw['accepted_archive_sha256'] = sha(archive_path.read_bytes())
        manifest.write_text(json.dumps(info))
        result = verify(self.root, imw_mf_accepted_zip=archive_path)
        self.assertTrue(result['passed'], result['errors'])
        self.assertEqual(result['imw_mf_sources_verified'], 12)
        self.assertTrue(result['imw_mf_archive_comparison'])

    def test_wrong_imw_archive_is_rejected(self):
        path = self.root / 'wrong.zip'; path.write_bytes(b'wrong')
        self.assertTrue(any('Expected the accepted IMW-MF' in e for e in verify(self.root, imw_mf_accepted_zip=path)['errors']))

    def test_wrong_cp_archive_is_rejected(self):
        archive_path = self.root / 'wrong.zip'
        archive_path.write_bytes(b'not the reviewed archive')
        result = verify(self.root, cp_accepted_zip=archive_path)
        self.assertTrue(any('Expected the accepted CP-' in e for e in result['errors']))


if __name__ == '__main__':
    unittest.main()
