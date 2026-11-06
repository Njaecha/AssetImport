#!/usr/bin/env python3
"""Replay a local PMX/VMD through MMDD indexing with numerical engine doubles.

This measures joint targets and D-bone propagation, not Unity skinning/physics.
Private models, motions and reports are not distributed with this tool.
"""
import argparse
import ast
from collections import defaultdict
import contextlib
import hashlib
import importlib.util
import io
import json
import math
from pathlib import Path
import struct
import sys
import types

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tests/mmdd'))
from test_pmx_runtime import Vector, Quaternion
from test_position_grants import Py2Dict, slerp
from pmx_metadata import read_pmx
from generate_pmx_mapping import create


class Transform:
    def __init__(self, position, parent=None):
        self.parent, self.children = parent, []
        if parent:
            parent.children.append(self)
        self._position, self._rotation, self._world = position, Quaternion(), None

    def invalidate(self):
        self._world = None
        for child in self.children:
            child.invalidate()

    @property
    def localPosition(self):
        return self._position

    @localPosition.setter
    def localPosition(self, value):
        self._position = value
        self.invalidate()

    @property
    def localRotation(self):
        return self._rotation

    @localRotation.setter
    def localRotation(self, value):
        # Match Transform's unit quaternion storage. Real VMD inputs can carry
        # non-unit quaternion samples; letting their length scale vectors would
        # invent shrinking limbs in a numerical double.
        length = math.sqrt(value.x**2+value.y**2+value.z**2+value.w**2)
        self._rotation = Quaternion(value.x/length,value.y/length,value.z/length,value.w/length) if length>1e-12 else Quaternion()
        self.invalidate()

    def world(self):
        if self._world is None:
            if self.parent:
                p, q = self.parent.world()
                self._world = p + q * self._position, q * self._rotation
            else:
                self._world = self._position, self._rotation
        return self._world

    position = property(lambda self: self.world()[0])

    @property
    def rotation(self):
        return self.world()[1]

    @rotation.setter
    def rotation(self, value):
        self.localRotation = Quaternion.Inverse(self.parent.rotation) * value if self.parent else value


class Adjustment:
    def __init__(self, position, scale):
        self.basePosition, self.scale = position, scale

    def GetAdjustedPosition(self, value):
        return self.basePosition + Vector(-value.x*self.scale, value.y*self.scale, -value.z*self.scale)

    def GetAdjustedRotation(self, value):
        # Conjugation by the PMX import's 180-degree Y basis rotation.
        return Quaternion(-value.x, value.y, -value.z, value.w)


class WorkData(types.SimpleNamespace):
    # Unity vectors are value data. Keep interpolation scratch storage separate
    # from keyframe input; plain Python object aliasing would mutate the VMD.
    @property
    def wPosition(self):
        return self._working_position

    @wPosition.setter
    def wPosition(self, value):
        self._working_position = Vector(value.x,value.y,value.z)


def install_math():
    Vector.zero, Vector.right, Vector.up, Vector.forward = Vector(), Vector(1,0,0), Vector(0,1,0), Vector(0,0,1)
    Quaternion.identity = Quaternion()
    Quaternion.Slerp = staticmethod(slerp)
    Quaternion.SlerpUnclamped = staticmethod(slerp)
    Vector.Dot = staticmethod(lambda a, b: a.x*b.x+a.y*b.y+a.z*b.z)
    Vector.Lerp = staticmethod(lambda a, b, t: Vector(a.x+(b.x-a.x)*t, a.y+(b.y-a.y)*t, a.z+(b.z-a.z)*t))
    Vector.normalized = property(lambda v: Vector(*(c / math.sqrt(v.sqrMagnitude) for c in (v.x,v.y,v.z))) if v.sqrMagnitude > 1e-20 else Vector())
    def set_vector(vector, x, y, z):
        vector.x, vector.y, vector.z = x, y, z
    Vector.Set = set_vector


def read_motion(path):
    data = path.read_bytes()
    if not data.startswith(b'Vocaloid Motion Data 0002'):
        raise ValueError('Expected VMD 0002')
    count = struct.unpack_from('<I', data, 50)[0]
    if 54 + count*111 > len(data):
        raise ValueError('Truncated VMD bone table')
    frames = defaultdict(list)
    undecodable = set()
    for i in range(count):
        offset = 54 + i*111
        raw = data[offset:offset+15].split(b'\0')[0]
        try:
            name = raw.decode('shift_jis')
        except UnicodeDecodeError:
            # A 15-byte VMD name may end in half a multibyte character. Keep it
            # distinguishable and report it; never guess a mapped bone name.
            name = raw.decode('shift_jis', errors='replace')
            undecodable.add(name)
        frame, x, y, z, qx, qy, qz, qw = struct.unpack_from('<I7f', data, offset+15)
        record = types.SimpleNamespace(frameNo=frame, position=Vector(x,y,z), rotation=Quaternion(qx,qy,qz,qw))
        for channel, key in enumerate(('xCurve','yCurve','zCurve','rCurve')):
            setattr(record, key, [data[offset+47+channel*16+j*4]/127.0 for j in range(4)])
        frames[name].append(record)
    for track in frames.values():
        track.sort(key=lambda key: key.frameNo)
    # The probe evaluates enabled IK. Reject unsupported switch tracks instead
    # of silently comparing poses under the wrong solver state.
    offset = 54 + count*111
    for size in (23,61,28,9):  # morph, camera, light, self-shadow
        if offset == len(data):
            return frames, sorted(undecodable), []
        count = struct.unpack_from('<I',data,offset)[0]
        offset += 4 + count*size
        if offset > len(data):
            raise ValueError('Truncated VMD section')
    disabled = []
    if offset < len(data):
        count = struct.unpack_from('<I',data,offset)[0]
        offset += 4
        for _ in range(count):
            frame,visible,entries = struct.unpack_from('<IBI',data,offset)
            offset += 9
            for _ in range(entries):
                name = data[offset:offset+20].split(b'\0')[0].decode('shift_jis',errors='replace')
                if not data[offset+20]:
                    disabled.append((frame,name))
                offset += 21
    if offset != len(data):
        raise ValueError('Unexpected VMD trailing data')
    return frames, sorted(undecodable), disabled


def controller(model, motion, lib_path, extension, scale):
    install_math()
    tree = ast.parse((lib_path / 'vmdlib.py').read_text(encoding='utf-8-sig'))
    library = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == 'VmdLib')
    cls = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == 'MotionController')
    methods = {'processTrack','processIK','buildWorkBoneIndex','initPose','resetPose','updatePose',
               'setFrame','getRangeIndexValue','getWorkBoneIndex','getVmdBoneFrameIndexed'}
    cls.body = [node for node in cls.body if isinstance(node, ast.FunctionDef) and node.name in methods]
    env = dict(Quaternion=Quaternion, Vector3=Vector, Vector2=Vector, GData=WorkData,
               math=math, UseUtility=lambda: False,
               Mathf=types.SimpleNamespace(Acos=lambda x: math.acos(max(-1,min(1,x))), Atan=math.atan, Rad2Deg=180/math.pi))
    exec(compile(ast.Module(body=[library,cls],type_ignores=[]), str(lib_path/'vmdlib.py'), 'exec'), env)
    vmdlib = types.SimpleNamespace(VmdLib=env['VmdLib'], MotionController=env['MotionController'])
    sys.modules['vmdlib'] = vmdlib
    sys.modules['UnityEngine'] = types.SimpleNamespace(Quaternion=Quaternion, Vector3=Vector)
    sys.modules['mmddmorphfuncext'] = types.SimpleNamespace(getMethod=lambda *args: None, _userFuncs={})
    spec = importlib.util.spec_from_file_location('pmx_motion_extension', extension)
    runtime = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runtime)
    runtime._register({})
    mapping, _ = create(model, 'motion probe')
    c = vmdlib.MotionController()
    c.boneNameList, c.boneDefineMap, c.dummyBones = [], {}, {}
    c.ikSolverIndex, c.ikBoneList = {}, []
    positions = {b['index']: Vector(-b['position'][0]*scale,b['position'][1]*scale,-b['position'][2]*scale) for b in model['bones']}
    for definition in mapping['bones']:
        name = definition['name']
        index = int(name.rsplit('_',1)[1])
        bone = model['bones'][index]
        parent_name = definition.get('parent')
        parent = c.dummyBones[parent_name].transform if parent_name else None
        rest = positions[index] - positions[bone['parent']] if parent else positions[index]
        c.dummyBones[name] = types.SimpleNamespace(name=name, transform=Transform(rest,parent))
        c.boneNameList.append(name)
        fields = dict(enable=True,isTrack=False,isIK=False,isLimitAxis=False,trackData=None,adjustment=Adjustment(rest,scale))
        fields.update(definition)
        fields['adjustment'] = Adjustment(rest,scale)
        # Fixed-axis handling is outside this foot-target probe.
        fields['isLimitAxis'] = False
        c.boneDefineMap[name] = types.SimpleNamespace(**fields)
    c.getDummyBones = lambda name: c.dummyBones[name]
    for definition in mapping['bones']:
        if not definition.get('isIK'):
            continue
        name, ik = definition['name'], definition['ikSolverSetting']
        target, *chain = definition['ikData']
        def solve(name=name, target=target, chain=chain, ik=ik):
            vmdlib.VmdLib.mmdIKSolver(c.getDummyBones(name), c.getDummyBones(target),
                                    [c.getDummyBones(link) for link in chain], ik['iterations'], ik['stepAngle'], ik['limits'])
        c.ikSolverIndex[name] = [types.SimpleNamespace(enable=True,defaultEnable=True),None,None,solve]
        c.ikBoneList.append(name)
    c.boneKeyFrames = Py2Dict({name: keys for name, keys in motion.items() if name in {d['vmdName'] for d in mapping['bones']}})
    c.ikvKeyframes, c.CharName = [], 'offline PMX motion'
    c.processTwistDisperse = lambda: None
    c.initPose()
    c.playbackSpeed, c.delay, c.differentiation, c.useVisibleFrame = 1, 0, False, False
    c.ociChar = types.SimpleNamespace(treeNodeObject=types.SimpleNamespace(visible=True))
    c.updateOnlyWhenVisible, c.Baked, c.indexMode, c.HighHeels = False, False, True, None
    c.twistDisperse, c.interpolateConsecutive = [], True
    with contextlib.redirect_stdout(io.StringIO()):
        c.buildWorkBoneIndex()
    return c, mapping


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pmx', type=Path, required=True)
    parser.add_argument('--vmd', type=Path, required=True)
    parser.add_argument('--mmdd-lib', type=Path, required=True)
    parser.add_argument('--extension', type=Path, default=ROOT/'scripts/mmdd/item_morph_functions.py')
    parser.add_argument('--frames', default='1800:2120:5', help='Inclusive start:end:step')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    model = read_pmx(args.pmx.read_bytes())
    motion, undecodable, disabled = read_motion(args.vmd)
    c, mapping = controller(model,motion,args.mmdd_lib,args.extension,.0832)
    ik_names = {d['vmdName'] for d in mapping['bones'] if d.get('isIK')}
    if any(name in ik_names for _,name in disabled):
        parser.error('This probe does not evaluate VMD IK-off tracks: ' + str(disabled))
    names = {d['vmdName']: d['name'] for d in mapping['bones']}
    definitions = {d['name']: d for d in mapping['bones']}
    start,end,step = (int(v) for v in args.frames.split(':'))
    if start < 0 or end < start or step <= 0:
        parser.error('Frames must have 0 <= start <= end and step > 0')
    records = []
    for frame in range(start,end+1,step):
        c.setFrame(frame)
        row = {'frame':frame,'solvers':[]}
        for name in c.ikBoneList:
            definition = definitions[name]
            target_name = definition['ikData'][0]
            target, goal = c.getDummyBones(target_name).transform, c.getDummyBones(name).transform
            row['solvers'].append(dict(name=definition['vmdName'],target=definitions[target_name]['vmdName'],
                                       error=math.sqrt((target.position-goal.position).sqrMagnitude),
                                       target_y=target.position.y,goal_y=goal.position.y))
        row['positions'] = {name:[c.getDummyBones(names[name]).transform.position.x,c.getDummyBones(names[name]).transform.position.y,c.getDummyBones(names[name]).transform.position.z]
                            for name in ('センター','下半身','左足首','右足首','左足首D','右足首D') if name in names}
        records.append(row)
    report = dict(model=str(args.pmx),motion=str(args.vmd),undecodable_track_names=undecodable,
                  model_sha256=hashlib.sha256(args.pmx.read_bytes()).hexdigest(),
                  motion_sha256=hashlib.sha256(args.vmd.read_bytes()).hexdigest(),
                  extension_sha256=hashlib.sha256(args.extension.read_bytes()).hexdigest(),
                  limitations='Numerical doubles, default transforms, all IK enabled, fixed-axis ignored; no skinning/physics/Unity/CLR', frames=records)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2,allow_nan=False)+'\n',encoding='utf-8')
    for name in ('左足ＩＫ','右足ＩＫ'):
        samples = [(row['frame'],entry) for row in records for entry in row['solvers'] if entry['name']==name]
        if samples:
            worst = max(samples,key=lambda pair:pair[1]['error'])
            print(name,'worst',worst,'mean',sum(entry['error'] for _,entry in samples)/len(samples))
    print(args.output)


if __name__ == '__main__':
    main()
