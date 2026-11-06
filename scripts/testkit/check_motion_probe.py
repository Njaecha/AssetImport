#!/usr/bin/env python3
"""Check real Assimp file/memory imports and the locally supplied MMDD 3.0 parsers.

Requires the official MMDD Lib directory; its source is NOT vendored. Unity types
used by data deserialization are doubles, not a simulated Unity runtime.
"""
import argparse
import ast
import configparser
import ctypes as C
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys
import tempfile
import traceback
import types


class Vector3:
    def __init__(self,x=0,y=0,z=0): self.x,self.y,self.z=x,y,z


class Quaternion:
    def __init__(self,x=0,y=0,z=0,w=1): self.x,self.y,self.z,self.w=x,y,z,w


def install_data_doubles():
    unity=types.ModuleType('UnityEngine')
    Vector3.zero=Vector3()
    Vector3.right=Vector3(1,0,0)
    Vector3.up=Vector3(0,1,0)
    Vector3.forward=Vector3(0,0,1)
    Quaternion.identity=Quaternion()
    unity.Vector3,unity.Quaternion=Vector3,Quaternion
    for name in ('Vector2','Color','Rect','GameObject','Transform','Mathf','Object'):
        setattr(unity,name,type(name,(),{}))
    sys.modules['UnityEngine']=unity
    system=types.ModuleType('System')
    for name in ('Single','Double'):setattr(system,name,float)
    for name in ('Byte','UInt16','UInt32','UInt64','Int16','Int32','Int64'):setattr(system,name,int)
    sys.modules['System']=system
    sys.modules['ConfigParser']=configparser


class Py2Bytes(bytes):
    """Adapt Python 2 binary-file string operations without changing the parser."""
    def strip(self,chars=None):
        if isinstance(chars,str):chars=chars.encode('latin1')
        return Py2Bytes(super().strip(chars))
    def startswith(self,prefix,*args):
        if isinstance(prefix,str):prefix=prefix.encode('latin1')
        return super().startswith(prefix,*args)


class Py2File:
    def __init__(self,*args):self.file=open(*args)
    def read(self,*args):return Py2Bytes(self.file.read(*args))
    def seek(self,*args):return self.file.seek(*args)
    def close(self):self.file.close()


def read_with_mmdd(lib,definition,vmd):
    install_data_doubles()
    spec=importlib.util.spec_from_file_location('probe_mmddbonedef',lib/'mmddbonedef.py')
    module=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    settings=module.loadBoneSettingJSON(str(definition))
    assert settings.boneType=='ITEM'
    assert settings.validateAll()==[]
    assert len(settings.boneDefineMap)==3
    # Execute the original loader and keyframe data class from the upstream AST.
    # Other vmdlib code requires live Studio objects and is deliberately excluded.
    tree=ast.parse((lib/'vmdlib.py').read_text(encoding='utf-8-sig'))
    loader=next(n for n in tree.body if isinstance(n,ast.ClassDef) and n.name=='VmdLib')
    method=next(n for n in loader.body if isinstance(n,ast.FunctionDef) and n.name=='loadVmd')
    loader.body=[method]
    keyframe=next(n for n in tree.body if isinstance(n,ast.ClassDef) and n.name=='VmdBoneKeyFrame')
    subset=ast.Module(body=[loader,keyframe],type_ignores=[])
    env={'Vector3':Vector3,'Quaternion':Quaternion,'struct':struct,'traceback':traceback,'open':Py2File}
    exec(compile(subset,str(lib/'vmdlib.py'),'exec'),env)
    result=env['VmdLib'].loadVmd(str(vmd))
    assert len(result)==5,result
    assert len(result[1])==15 and not any(result[2:])
    tracks={bd.vmdName:[] for bd in settings.boneDefineMap.values()}
    for frame in result[1]:
        assert frame.name in tracks
        tracks[frame.name].append(frame)
        q=frame.rotation
        assert abs(q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w-1)<1e-6
        for curve in frame.curveSet:
            assert all(abs(a-b)<1e-7 for a,b in zip(curve,[20/127,20/127,107/127,107/127]))
    assert all([f.frameNo for f in frames]==[0,30,60,90,120] for frames in tracks.values())
    assert tracks['全ての親'][2].position.y==4
    for name,angle in [('振り子',35),('先端',-20)]:
        assert abs(math.degrees(2*math.atan2(tracks[name][1].rotation.z,tracks[name][1].rotation.w))-angle)<1e-5
    return settings,tracks


def import_native(native,fbx,mode,dest):
    lib=C.CDLL(str(native))
    def bind(name,restype,*argtypes):
        f=getattr(lib,name);f.restype=restype;f.argtypes=argtypes;return f
    version=[bind(n,C.c_uint)() for n in ('aiGetVersionMajor','aiGetVersionMinor','aiGetVersionRevision')]
    assert version==[5,0,0x8f0c6b04],version  # v5.0.1 has no aiGetVersionPatch API.
    props=bind('aiCreatePropertyStore',C.c_void_p)()
    release_props=bind('aiReleasePropertyStore',None,C.c_void_p)
    release=bind('aiReleaseImport',None,C.c_void_p)
    scene=None
    try:
        bind('aiSetImportPropertyInteger',None,C.c_void_p,C.c_char_p,C.c_int)(props,b'IMPORT_REMOVE_EMPTY_BONES',0)
        flags=0x4|0x8  # MakeLeftHanded | Triangulate, exactly AssetImport.Load().
        if mode=='memory':
            data=fbx.read_bytes()
            scene=bind('aiImportFileFromMemoryWithProperties',C.c_void_p,C.c_char_p,C.c_uint,C.c_uint,C.c_char_p,C.c_void_p)(data,len(data),flags,b'fbx',props)
        else:
            scene=bind('aiImportFileExWithProperties',C.c_void_p,C.c_char_p,C.c_uint,C.c_void_p,C.c_void_p)(str(fbx).encode(),flags,None,props)
        if not scene:raise ValueError(bind('aiGetErrorString',C.c_char_p)().decode())
        result=bind('aiExportScene',C.c_int,C.c_void_p,C.c_char_p,C.c_char_p,C.c_uint)(scene,b'assjson',str(dest).encode(),0)
        assert result==0
        return json.loads(dest.read_text())
    finally:
        if scene:release(scene)
        release_props(props)


def multiply(a,b):
    return [sum(a[r*4+k]*b[k*4+c] for k in range(4)) for r in range(4) for c in range(4)]


def transform(m,p):
    v=list(p)+[1]
    return tuple(sum(m[r*4+k]*v[k] for k in range(4)) for r in range(3))


IDENTITY=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]


def check_scene(scene,settings):
    nodes={}
    def walk(n,parent=None,world=IDENTITY):
        assert n['name'] not in nodes,'Probe needs unambiguous bone names'
        world=multiply(world,n['transformation'])
        nodes[n['name']]=(n,parent,world)
        for ch in n.get('children',[]):walk(ch,n['name'],world)
    walk(scene['rootnode'])
    targets={bd.weightBone for bd in settings.boneDefineMap.values()}
    assert targets.issubset(nodes)
    for bd in settings.boneDefineMap.values():
        assert bd.positionData.target==bd.weightBone
        if bd.parent:
            assert nodes[bd.weightBone][1]==settings.boneDefineMap[bd.parent].weightBone
    assert len(scene['meshes'])==1
    mesh=scene['meshes'][0]
    assert len(mesh['faces'])==72
    assert {b['name'] for b in mesh['bones']}==targets
    sums=[0.]*(len(mesh['vertices'])//3)
    for b in mesh['bones']:
        skin=multiply(nodes[b['name']][2],b['offsetmatrix'])
        for vertex,weight in b['weights']:
            sums[vertex]+=weight
            p=mesh['vertices'][vertex*3:vertex*3+3]
            assert max(abs(a-v) for a,v in zip(transform(skin,p),p))<1e-5,'Bind-pose drift'
    assert all(abs(w-1)<1e-6 for w in sums),'Unweighted or multiply weighted vertex'
    uv=mesh['texturecoords'][0]
    # Assimp JSON UV is 2 components for this FBX, with one atlas tile per part.
    assert len(uv)==len(sums)*2
    assert {round(uv[i],3) for i in range(0,len(uv),2)}=={.125,.375,.625,.875}
    textures=[p['value'] for m in scene['materials'] for p in m['properties'] if p['key']=='$tex.file' and p['semantic']==1]
    assert textures==['probe_colors.png']
    return {'vertices_after_import':len(sums),'triangles':72,'weighted_bones':len(targets),'all_vertices_weighted':True,'bind_pose':'PASS','texture_uv':'PASS'}


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--kit',type=Path,required=True)
    p.add_argument('--native',type=Path,required=True)
    p.add_argument('--mmdd-lib',type=Path,required=True)
    a=p.parse_args()
    model=a.kit/'UserData/AssetImport/MMDD3Probe'
    settings,tracks=read_with_mmdd(a.mmdd_lib,a.kit/'BepInEx/plugins/Console/Lib/mmdd/item_AssetImport_Probe_v1.json',model/'probe.vmd')
    checks={}
    with tempfile.TemporaryDirectory(prefix='assetimport-mmdd-') as tmp:
        scenes=[]
        for mode in ('file','memory'):
            scene=import_native(a.native.resolve(),model/'probe.fbx',mode,Path(tmp)/(mode+'.json'))
            checks[mode]=check_scene(scene,settings)
            scenes.append(scene)
        assert scenes[0]==scenes[1],'File and memory import differ'
    report={'result':'OFFLINE PASS','native':'Assimp 5.0.1','postprocess':'MakeLeftHanded | Triangulate',
        'native_imports':checks,'mmdd_json':'MMDD 3.0 original loadBoneSettingJSON + validateAll',
        'mmdd_vmd':'MMDD 3.0 original loadVmd AST; Python 2 file-string shim; Unity data doubles',
        'tracks':{k:len(v) for k,v in tracks.items()},'upstream_sha256':{n:hashlib.sha256((a.mmdd_lib/n).read_bytes()).hexdigest() for n in ('mmddbonedef.py','vmdlib.py')},
        'not_tested':['Unity rendering','BepInEx/IronPython integration','MMDD runtime bone sync','save/reload','IK','physics','Timeline','real Pmx2Fbx output']}
    (a.kit/'offline-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False,indent=2))


if __name__=='__main__':main()
