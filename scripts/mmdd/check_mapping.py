#!/usr/bin/env python3
"""Validate a direct-PMX mapping against MMDD 3.0 and the real importer report."""
import argparse
import ast
import struct
import traceback
import importlib.util
import json
from pathlib import Path
import sys

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--mapping',type=Path,required=True)
p.add_argument('--scene-report',type=Path,required=True)
p.add_argument('--mmdd-lib',type=Path,required=True)
p.add_argument('--vmd',type=Path)
a=p.parse_args()
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'testkit'))
from check_motion_probe import install_data_doubles
install_data_doubles()
spec=importlib.util.spec_from_file_location('mmdd_mapping_check',a.mmdd_lib/'mmddbonedef.py')
mmdd=importlib.util.module_from_spec(spec);spec.loader.exec_module(mmdd)
settings=mmdd.loadBoneSettingJSON(str(a.mapping))
errors=settings.validateAll()
assert not errors,errors
scene=json.loads(a.scene_report.read_text())
actual_bones=set(scene['boneNames'])
for bd in settings.boneDefineMap.values():
 assert bd.weightBone in actual_bones,bd.weightBone
 assert bd.positionData.target in actual_bones,bd.positionData.target
renderers={r['name']:set(r['shapes']) for r in scene['renderers']}
report=json.loads(a.mapping.with_suffix('.report.txt').read_text())
for md in settings.morphDefineMap.values():
 assert md.type=='FUNC', 'MMDD 3 does not bind BLEND morphs to ITEM'
 for get_name,set_name,value_type,low,high in md.funcList:
  key=get_name[len('get_assetimport_'):]
  assert get_name=='get_assetimport_'+key and set_name=='set_assetimport_'+key
  assert value_type=='range'
  renderer,shape=report['morph_targets'][key]
  assert renderer in renderers and shape in renderers[renderer],(renderer,shape)
  assert low==0 and high==100
assert not set(report['excluded_from_mmdd']) & {bd.weightBone for bd in settings.boneDefineMap.values()}
print('PASS: official MMDD 3 JSON validator, %d actual bone targets, %d IK solvers, %d morph targets, no physics-bone sync conflicts.'%(len(settings.boneDefineMap),len(settings.ikSettingMap),len(settings.morphDefineMap)))

if a.vmd:
 from check_motion_probe import Vector3, Quaternion, Py2File
 tree=ast.parse((a.mmdd_lib/'vmdlib.py').read_text(encoding='utf-8-sig'))
 classes=[n for n in tree.body if isinstance(n,ast.ClassDef) and n.name in ('VmdLib','VmdBoneKeyFrame','VmdMorphKeyFrame')]
 loader=next(n for n in classes if n.name=='VmdLib')
 loader.body=[n for n in loader.body if isinstance(n,ast.FunctionDef) and n.name=='loadVmd']
 env={'Vector3':Vector3,'Quaternion':Quaternion,'struct':struct,'traceback':traceback,'open':Py2File}
 exec(compile(ast.Module(body=classes,type_ignores=[]),str(a.mmdd_lib/'vmdlib.py'),'exec'),env)
 result=env['VmdLib'].loadVmd(str(a.vmd))
 assert len(result)==5,result
 motion_names={bd.vmdName for bd in settings.boneDefineMap.values()}
 morph_names={md.vmdName for md in settings.morphDefineMap.values()}
 assert all(k.name in motion_names for k in result[1])
 assert all(k.name in morph_names for k in result[2])
 assert len(result[1])==36 and len(result[2])==9
 assert len({k.name for k in result[1]})==4
 print('PASS: official VMD parser reads 4 mapped bone tracks / 36 keys and 1 mapped morph / 9 keys.')
