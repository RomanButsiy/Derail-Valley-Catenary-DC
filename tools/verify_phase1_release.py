#!/usr/bin/env python3
"""Verify the current release when present; otherwise the historical Visual 4.

Standard library only. Does not regenerate, move, install or edit any object.
An optional original unified JSON supplies a second direct comparison.
"""
import argparse,collections,hashlib,json,re
from pathlib import Path

def canonical(value):return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False)
def digest(value):return hashlib.sha256(canonical(value).encode()).hexdigest()
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def verify(root,accepted=None):
 root=Path(root)
 if (root/'audit/network_6m.json').is_file():
  if accepted is not None:
   return {'passed':False,'errors':['Visual 4 is a historical baseline. For the current release use verify_network_release.py --accepted-zip Catenary_6m_Visual2.zip.']}
  from verify_network_release import verify as verify_current
  return verify_current(root)
 manifest=json.loads((root/'audit/phase1_hb_cs_cms_sm.json').read_text(encoding='utf-8'))
 errors=[];layouts={};objects=[]
 for item in manifest['scenery_files']:
  p=root/item['path'];meta=Path(str(p)+'.meta')
  if not p.is_file() or not meta.is_file():errors.append('Missing asset or meta: '+item['name']);continue
  if sha(p)!=item['sha256']:errors.append('Accepted file changed: '+item['name'])
  if sha(meta)!=item['meta_sha256']:errors.append('Unity metadata changed: '+item['name'])
  text=meta.read_text();match=re.search(r'^guid: ([0-9a-f]{32})$',text,re.M)
  if not match or match[1]!=item['guid'] or 'assetBundleName: catenary\n'not in text:errors.append('Invalid bundle metadata: '+item['name'])
  data=json.loads(p.read_text());layouts[item['name']]=data;objects.extend(data)
  if len(data)!=item['objects']:errors.append('Object count changed: '+item['name'])
  feeds=collections.Counter(o['substation']for o in data if '+wire,'in o['$type'])
  if dict(feeds)!=item['wire_feeds']:errors.append('Feed assignments changed: '+item['name'])
  if item['name'].startswith('scenery_[')and set(feeds)!={'NEUTRAL'}:errors.append('Energised wire in a neutral file: '+item['name'])
 if len(objects)!=manifest['object_count']:errors.append('Missing or extra objects')
 if digest(sorted(canonical(o)for o in objects))!=manifest['canonical_object_multiset_sha256']:errors.append('Values or multiplicities differ from accepted Visual 4')
 reconstructed=[None]*manifest['object_count'];used=set()
 for loc in manifest['object_map']:
  pair=(loc['file'],loc['index']);i=loc['visual4_index']
  if pair in used or not 0<=i<len(reconstructed)or reconstructed[i]is not None:errors.append('Duplicate mapping: '+str(loc));continue
  used.add(pair)
  try:reconstructed[i]=layouts[loc['file']][loc['index']]
  except (KeyError,IndexError):errors.append('Invalid object mapping: '+str(loc))
 if len(used)!=len(objects)or any(o is None for o in reconstructed):errors.append('Incomplete object mapping')
 if digest(reconstructed)!=manifest['canonical_original_order_sha256']:errors.append('Reconstructed payload differs from accepted Visual 4')
 if accepted is not None:
  original=json.loads(Path(accepted).read_text(encoding='utf-8-sig'))
  if original!=reconstructed:errors.append('Direct comparison with accepted JSON failed')
 supplies=[o for o in objects if '+substation,'in o['$type']]
 if len(supplies)!=1 or supplies[0]['map_location']!='CMS1500':errors.append('Expected exactly one new CMS1500 source')
 for old in manifest['retired_test_files']:
  for parent in [root/'Scenery',root/'unity_project/Assets/Catenary/Scenery']:
   if (parent/old).exists():errors.append('Retired unified test file would duplicate geometry: '+str(parent/old))
 return {'passed':not errors,'scenery_files':len(layouts),'accepted_objects':len(objects),'reconstructed_exactly':digest(reconstructed)==manifest['canonical_original_order_sha256'],'direct_accepted_json_compared':accepted is not None,'errors':errors}

if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1]);p.add_argument('--accepted-scenery',type=Path);args=p.parse_args()
 result=verify(args.root,args.accepted_scenery);print(json.dumps(result,ensure_ascii=False,indent=2));raise SystemExit(0 if result['passed']else 1)
