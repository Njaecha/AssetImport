"""Execute official MMDD 3 indexing/playback/pose methods with engine doubles.

Set ASSETIMPORT_MMDD_LIB to an existing MMDD Lib directory. The official source
is read locally, never copied into this repository or an installation package.
"""
import ast
import contextlib
import importlib.util
import io
import math
import os
from pathlib import Path
import sys
import types
import unittest

from test_pmx_runtime import Vector, Quaternion

ROOT = Path(__file__).resolve().parents[2]
LIB = Path(os.environ.get('ASSETIMPORT_MMDD_LIB', str(ROOT / '.build-downloads/mmdd3/MMDD v3.0 KK/BepInEx/plugins/Console/Lib')))


class Py2Dict(dict):
    def has_key(self, key):
        return key in self


class Adjustment:
    def __init__(self, base):
        self.basePosition = base

    def GetAdjustedPosition(self, position):
        return self.basePosition + position

    def GetAdjustedRotation(self, rotation):
        return rotation


def slerp(a, b, rate):
    dot = a.x*b.x + a.y*b.y + a.z*b.z + a.w*b.w
    if dot < 0:
        b, dot = Quaternion(-b.x, -b.y, -b.z, -b.w), -dot
    if dot > .999999:
        values = [getattr(a, axis)+(getattr(b, axis)-getattr(a, axis))*rate for axis in ('x','y','z','w')]
        length = math.sqrt(sum(value*value for value in values))
        return Quaternion(*(value/length for value in values))
    angle = math.acos(min(1, dot))
    left, right = math.sin((1-rate)*angle)/math.sin(angle), math.sin(rate*angle)/math.sin(angle)
    return Quaternion(*(getattr(a, axis)*left + getattr(b, axis)*right for axis in ('x','y','z','w')))


class Utility:
    def __init__(self):
        self.position_tracks = 0

    def SetTransformLocalPosition(self, transform, value):
        transform.localPosition = value

    def SetTransformLocalRotation(self, transform, value):
        transform.localRotation = value

    def SetWorkBoneIndexTrackLocalPosition(self, target, source, rate):
        self.position_tracks += 1
        a, b = target.localPosition, source.localPosition
        target.localPosition = Vector(a.x+b.x*rate, a.y+b.y*rate, a.z+b.z*rate)

    def SetWorkBoneIndexTrackLocalRotation(self, target, source, rate):
        target.localRotation = slerp(Quaternion.identity, source.localRotation, rate) * target.localRotation


@unittest.skipUnless((LIB / 'vmdlib.py').is_file(), 'Official MMDD Lib required for playback checks')
class PositionGrantChecks(unittest.TestCase):
    def setUp(self):
        self.modules = {name: sys.modules.get(name) for name in ('UnityEngine','vmdlib','mmddmorphfuncext')}
        Quaternion.identity, Vector.zero = Quaternion(), Vector()
        Quaternion.SlerpUnclamped = staticmethod(slerp)
        tree = ast.parse((LIB / 'vmdlib.py').read_text(encoding='utf-8-sig'))
        cls = next(n for n in tree.body if isinstance(n, ast.ClassDef) and n.name == 'MotionController')
        methods = {'processTrack','processIK','buildWorkBoneIndex','initPose','resetPose','updatePose',
                   'setFrame','getRangeIndexValue','getWorkBoneIndex'}
        cls.body = [n for n in cls.body if isinstance(n, ast.FunctionDef) and n.name in methods]
        self.utility, self.optimized = Utility(), False
        env = {'Quaternion': Quaternion, 'Vector3': Vector, 'GData': types.SimpleNamespace,
               'UseUtility': lambda: self.optimized, 'GetUtility': lambda: self.utility}
        exec(compile(ast.Module(body=[cls], type_ignores=[]), str(LIB / 'vmdlib.py'), 'exec'), env)
        self.controller_type = env['MotionController']
        self.native_methods = {name: getattr(self.controller_type,name)
                               for name in ('processTrack','processIK','buildWorkBoneIndex','updatePose')}
        lib = type('VmdLib', (), {'mmdIKSolver': staticmethod(lambda *a: None)})
        sys.modules['vmdlib'] = types.SimpleNamespace(VmdLib=lib, MotionController=self.controller_type)
        sys.modules['UnityEngine'] = types.SimpleNamespace(Quaternion=Quaternion, Vector3=Vector)
        sys.modules['mmddmorphfuncext'] = types.SimpleNamespace(getMethod=lambda *a: None, _userFuncs={})
        spec = importlib.util.spec_from_file_location('position_runtime', ROOT / 'scripts/mmdd/item_morph_functions.py')
        self.runtime = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.runtime)
        self.runtime._register({})

    def tearDown(self):
        for name, module in self.modules.items():
            if module is None: sys.modules.pop(name, None)
            else: sys.modules[name] = module

    def controller(self, rate=.2, scale=1, keyed=True, ik=False):
        c = self.controller_type()
        names = ['assetimport_pmx_%04d' % i for i in range(3)] + ['foreign_track']
        c.boneNameList, c.boneDefineMap, c.dummyBones = names, {}, {}
        bases = [Vector(10*scale,-2*scale,8*scale), Vector(-3*scale,5*scale,9*scale), Vector(2*scale,7*scale,-4*scale), Vector(1,2,3)]
        for i, (name, base) in enumerate(zip(names, bases)):
            c.boneDefineMap[name] = types.SimpleNamespace(enable=True, vmdName=name, movable=True,
                rotatable=True, isTrack=i>0, trackData=('rp' if i==1 else 'p', rate if i==1 else -.5, names[max(0,i-1)]),
                isLimitAxis=False, isIK=ik and i==1, adjustment=Adjustment(base))
            c.dummyBones[name] = types.SimpleNamespace(transform=types.SimpleNamespace(localPosition=base, localRotation=Quaternion()))
        c.getDummyBones = lambda name: c.dummyBones[name]
        c.boneKeyFrames = Py2Dict({name:[types.SimpleNamespace(frameNo=0),types.SimpleNamespace(frameNo=10)] for name in names[:2]} if keyed else {})
        c.ikvKeyframes, c.ikBoneList, c.ikSolverIndex = [], [], {}
        c.ik_calls = 0
        if ik:
            def solve():
                c.ik_calls += 1
            c.ikSolverIndex[names[1]] = [types.SimpleNamespace(enable=True, defaultEnable=True), None, None, solve]
            c.ikBoneList = [names[1]]
        c.CharName = 'generated'
        c.processTwistDisperse = lambda: None
        c.initPose()
        c.playbackSpeed, c.delay, c.differentiation, c.useVisibleFrame = 1, 0, False, False
        c.ociChar = types.SimpleNamespace(treeNodeObject=types.SimpleNamespace(visible=True))
        c.updateOnlyWhenVisible, c.Baked, c.indexMode, c.HighHeels = False, False, True, None
        c.twistDisperse = []
        def frame(frame_no, entry):
            own = .1 if entry.bonename == names[1] else 1
            entry.wPosition = Vector(frame_no*own*scale, -frame_no*own*scale, .5*frame_no*own*scale)
            entry.wRotation = Quaternion.AngleAxis(frame_no*own, Vector(0,0,1))
        c.getVmdBoneFrameIndexed = frame
        with contextlib.redirect_stdout(io.StringIO()): c.buildWorkBoneIndex()
        return c, names, bases

    def vector_equal(self, a, b):
        self.assertLess((a-b).sqrMagnitude, 1e-10)

    def test_indexed_offsets_chains_seek_and_native_utility(self):
        for optimized in (False, True):
            self.optimized = optimized
            for rate in (.2, -1, 1.5):
                for scale in (.0832, 3):
                    c, names, bases = self.controller(rate, scale, ik=True)
                    for frame in (0,10,10,2,0):
                        c.setFrame(frame)
                        delta = Vector(frame*scale*(.1+rate), -frame*scale*(.1+rate), .5*frame*scale*(.1+rate))
                        first = bases[1] + delta
                        second = bases[2] + Vector(-.5*delta.x,-.5*delta.y,-.5*delta.z)
                        self.vector_equal(c.getDummyBones(names[1]).transform.localPosition, first)
                        self.vector_equal(c.getDummyBones(names[2]).transform.localPosition, second)
                        # Unrelated MMDD mappings retain stock absolute-position tracking.
                        self.vector_equal(c.getDummyBones(names[3]).transform.localPosition,
                                          bases[3] + Vector(-.5*second.x,-.5*second.y,-.5*second.z))
                        self.assertAlmostEqual(Quaternion.Angle(c.getDummyBones(names[1]).transform.localRotation,
                                               Quaternion.AngleAxis(frame*(.1+rate), Vector(0,0,1))), 0, places=4)
                    self.assertEqual(c.ik_calls, 5, 'grant correction must not replace or duplicate real IK')
                    entry_names = [e.bonename for e in c.workBoneIndexBuffer]
                    self.assertEqual(len(entry_names), len(set(entry_names)))
                    self.assertTrue(all(n in entry_names for n in names))
                    self.assertEqual(c.getWorkBoneIndex(names[1]).bonename, names[1])
        self.assertGreater(self.utility.position_tracks, 0, 'native utility path remains enabled')

    def test_pose_reset_and_unkeyed_rest_pose(self):
        c, names, bases = self.controller(keyed=False)
        for _ in range(3):
            c.setFrame(10)
            for i in (0,1,2): self.vector_equal(c.getDummyBones(names[i]).transform.localPosition, bases[i])
        c.poseData[names[0]][1] = bases[0] + Vector(2,3,4)
        c.poseData[names[1]][1] = bases[1] + Vector(1,2,3)
        for _ in range(3):
            c.updatePose()
            self.vector_equal(c.getDummyBones(names[1]).transform.localPosition, bases[1]+Vector(1.4,2.6,3.8))
        c.resetPose()
        c.updatePose()
        for i in (0,1,2): self.vector_equal(c.getDummyBones(names[i]).transform.localPosition, bases[i])

    def test_ik_visibility_does_not_disable_position_grant(self):
        c, names, bases = self.controller(ik=True)
        c.ikSolverIndex[names[1]][2] = types.SimpleNamespace(enable=True, tempRequest=None, length=1, valueList=[False])
        with contextlib.redirect_stdout(io.StringIO()): c.buildWorkBoneIndex()
        c.setFrame(10)
        self.assertEqual(c.ik_calls, 0)
        self.vector_equal(c.getDummyBones(names[1]).transform.localPosition, bases[1]+Vector(3,-3,1.5))

    def test_controller_isolation_reload_and_index_rebuild(self):
        a, names, bases = self.controller()
        b, _, other_bases = self.controller(scale=5)
        method = self.controller_type.buildWorkBoneIndex
        self.runtime._register({})
        self.assertIs(method, self.controller_type.buildWorkBoneIndex)
        a.setFrame(10)
        b.setFrame(2)
        self.vector_equal(a.getDummyBones(names[1]).transform.localPosition, bases[1]+Vector(3,-3,1.5))
        self.vector_equal(b.getDummyBones(names[1]).transform.localPosition, other_bases[1]+Vector(3,-3,1.5))
        entry_names = [e.bonename for e in a.workBoneIndexBuffer]
        with contextlib.redirect_stdout(io.StringIO()): a.buildWorkBoneIndex()
        self.assertEqual([e.bonename for e in a.workBoneIndexBuffer], entry_names, 'rebuild must not accumulate entries')
        a.setFrame(0)
        self.vector_equal(a.getDummyBones(names[1]).transform.localPosition, bases[1])
        a.boneDefineMap[names[1]].enable = False
        with contextlib.redirect_stdout(io.StringIO()): a.buildWorkBoneIndex()
        self.assertFalse(any(e.bonename.startswith(names[1]) for e in a.workBoneIndexBuffer))

    def test_python2_unbound_method_reload_marker(self):
        class LegacyMethod:
            def __init__(self, function): self.function = function

            def __get__(self, instance, owner):
                if instance is None: return types.SimpleNamespace(im_func=self.function)
                return self.function.__get__(instance, owner)
        c, names, bases = self.controller()
        track = self.controller_type.processTrack
        build = self.controller_type.buildWorkBoneIndex
        self.controller_type.processTrack = LegacyMethod(track)
        self.controller_type.buildWorkBoneIndex = LegacyMethod(build)
        self.runtime._register({})
        self.assertIs(self.controller_type.processTrack.im_func, track)
        self.assertIs(self.controller_type.buildWorkBoneIndex.im_func, build)
        c.setFrame(10)
        self.vector_equal(c.getDummyBones(names[1]).transform.localPosition, bases[1]+Vector(3,-3,1.5))

    def test_old_position_extension_before_and_after_schedule_install(self):
        controller_type = self.controller_type
        for name, method in self.native_methods.items(): setattr(controller_type,name,method)

        # Reproduce the previous extension's public hooks, including its extra
        # indexed callback. No private models or archived source are required.
        def install_old():
            def is_position(c,name):
                return name.startswith('assetimport_pmx_') and c.boneDefineMap[name].isTrack and 'p' in c.boneDefineMap[name].trackData[0]
            def subtract_rest(c,name):
                _,rate,source = c.boneDefineMap[name].trackData
                t = c.getDummyBones(name).transform
                t.localPosition = t.localPosition-c.boneDefineMap[source].adjustment.basePosition*rate
            original_track = controller_type.processTrack
            if not getattr(original_track,'_assetimport_position_grants',False):
                def track(c,name):
                    original_track(c,name)
                    if is_position(c,name): subtract_rest(c,name)
                track._assetimport_position_grants = True
                controller_type.processTrack = track
            original_build = controller_type.buildWorkBoneIndex
            if not getattr(original_build,'_assetimport_position_grants',False):
                def build(c):
                    original_build(c)
                    entries = []
                    for e in c.workBoneIndexBuffer:
                        entries.append(e)
                        if is_position(c,e.bonename):
                            entries.append(types.SimpleNamespace(bonename=e.bonename+'__position_rest',
                                needGetFrame=False,needPosition=False,needRotation=False,needTrackRot=False,needTrackPos=False,
                                needIK=True,ikDefaultEnable=True,ikSolverIndex=(None,None,None),
                                solveFunc=lambda name=e.bonename: subtract_rest(c,name)))
                    c.workBoneIndexBuffer = entries
                build._assetimport_position_grants = True
                controller_type.buildWorkBoneIndex = build
        install_old()
        self.runtime._register({})
        installed = (controller_type.processTrack,controller_type.buildWorkBoneIndex)
        install_old()
        self.assertEqual(installed,(controller_type.processTrack,controller_type.buildWorkBoneIndex))
        c,names,bases = self.controller(ik=True)
        self.assertFalse(any(e.bonename.endswith('__position_rest') for e in c.workBoneIndexBuffer))
        for _ in range(2):
            c.setFrame(10)
            self.vector_equal(c.getDummyBones(names[1]).transform.localPosition,bases[1]+Vector(3,-3,1.5))
            c.updatePose()
            self.vector_equal(c.getDummyBones(names[1]).transform.localPosition,bases[1]+Vector(3,-3,1.5))
        self.assertEqual(c.ik_calls,4)

    def test_foreign_ik_keeps_stock_order_and_missing_track_behavior(self):
        c,names,bases = self.controller()
        foreign = names[3]
        observed = []
        c.boneDefineMap[foreign].isIK = True
        c.ikBoneList = [foreign]
        c.ikSolverIndex[foreign] = [types.SimpleNamespace(enable=True,defaultEnable=True),None,None,
            lambda: observed.append(c.getDummyBones(foreign).transform.localPosition)]
        with contextlib.redirect_stdout(io.StringIO()): c.buildWorkBoneIndex()
        c.setFrame(10)
        c.updatePose()
        self.assertEqual(len(observed),2)
        for position in observed: self.vector_equal(position,bases[3])
        c.boneDefineMap[foreign].isTrack = False
        with contextlib.redirect_stdout(io.StringIO()): c.buildWorkBoneIndex()
        self.assertNotIn(foreign,[e.bonename for e in c.workBoneIndexBuffer])

    def test_append_rotation_composes_after_own_animation(self):
        for optimized in (False, True):
            self.optimized = optimized
            for rate in (.2, 1, -1, 1.5):
                with self.subTest(optimized=optimized, rate=rate):
                    c, names, _ = self.controller(rate=rate)
                    def frame(frame_no, entry):
                        entry.wPosition = Vector()
                        axis = Vector(0,1,0) if entry.bonename == names[0] else Vector(1,0,0)
                        entry.wRotation = Quaternion.AngleAxis(frame_no*9, axis)
                    c.getVmdBoneFrameIndexed = frame
                    for f in (10,0,4,10):
                        c.setFrame(f)
                        expected = Quaternion.AngleAxis(f*9,Vector(1,0,0)) * Quaternion.AngleAxis(f*9*rate,Vector(0,1,0))
                        self.assertLess(Quaternion.Angle(c.getDummyBones(names[1]).transform.localRotation,expected),1e-4)
                    c.poseData[names[0]][0] = Quaternion.AngleAxis(90,Vector(0,1,0))
                    c.poseData[names[1]][0] = Quaternion.AngleAxis(90,Vector(1,0,0))
                    for _ in range(2):
                        c.updatePose()
                        self.assertLess(Quaternion.Angle(c.getDummyBones(names[1]).transform.localRotation,
                                        Quaternion.AngleAxis(90,Vector(1,0,0))*Quaternion.AngleAxis(90*rate,Vector(0,1,0))),1e-4)

    def test_ik_receives_final_grant_in_playback_and_pose(self):
        for optimized in (False, True):
            self.optimized = optimized
            c, names, bases = self.controller(ik=True)
            observed = []
            c.ikSolverIndex[names[1]][3] = lambda: observed.append(c.getDummyBones(names[1]).transform.localPosition)
            with contextlib.redirect_stdout(io.StringIO()): c.buildWorkBoneIndex()
            c.setFrame(10)
            self.assertEqual(len(observed),1)
            self.vector_equal(observed[0],bases[1]+Vector(3,-3,1.5))
            c.poseData[names[0]][1] = bases[0]+Vector(2,3,4)
            c.poseData[names[1]][1] = bases[1]+Vector(1,2,3)
            c.updatePose()
            self.assertEqual(len(observed),2)
            self.vector_equal(observed[-1],bases[1]+Vector(1.4,2.6,3.8))

    def test_unkeyed_ik_is_indexed_and_obeys_later_visibility_changes(self):
        c, names, _ = self.controller(keyed=False,ik=True)
        c.boneDefineMap[names[1]].isTrack = False
        c.boneDefineMap[names[1]].trackData = None
        with contextlib.redirect_stdout(io.StringIO()): c.buildWorkBoneIndex()
        self.assertEqual(len(c.boneKeyFrames),0,'do not fabricate animation tracks')
        c.setFrame(0)
        self.assertEqual(c.ik_calls,1)
        # MMDD fills the range-index slot after importing bone keyframes.
        c.ikSolverIndex[names[1]][2] = types.SimpleNamespace(enable=True,tempRequest=None,length=1,valueList=[False])
        c.setFrame(10)
        self.assertEqual(c.ik_calls,1)
        c.ikSolverIndex[names[1]][2] = None
        c.setFrame(0)
        self.assertEqual(c.ik_calls,2)
        c.ikSolverIndex[names[1]][0].enable = False
        with contextlib.redirect_stdout(io.StringIO()): c.buildWorkBoneIndex()
        c.setFrame(0)
        self.assertEqual(c.ik_calls,2)

    def test_unkeyed_rotations_reset_between_frames(self):
        c, names, _ = self.controller(keyed=False,ik=True)
        c.boneDefineMap[names[1]].isTrack = False
        c.boneDefineMap[names[1]].trackData = None
        c.boneDefineMap[names[0]].rotatable = True
        def solve():
            t = c.getDummyBones(names[0]).transform
            t.localRotation = Quaternion.AngleAxis(30,Vector(1,0,0))*t.localRotation
        c.ikSolverIndex[names[1]][3] = solve
        with contextlib.redirect_stdout(io.StringIO()): c.buildWorkBoneIndex()
        for frame in (0,10,0,4,4):
            c.setFrame(frame)
            self.assertLess(Quaternion.Angle(c.getDummyBones(names[0]).transform.localRotation,
                                            Quaternion.AngleAxis(30,Vector(1,0,0))),1e-4)


if __name__ == '__main__':
    unittest.main()
