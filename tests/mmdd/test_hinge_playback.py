"""Run generated crouches through official MMDD indexing, IK and D-bone grants."""
import contextlib
import io
import math
from pathlib import Path
import random
import sys
import types
import unittest

from test_pmx_runtime import Vector, Quaternion
from test_position_grants import LIB

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/mmdd'))
from check_pmx_motion import controller


@unittest.skipUnless((LIB / 'vmdlib.py').is_file(), 'Official MMDD Lib required for playback checks')
class HingePlaybackChecks(unittest.TestCase):
    def test_unkeyed_ik_and_appended_goal_reach_target_without_frame_history(self):
        previous = {name: sys.modules.get(name) for name in ('UnityEngine','vmdlib','mmddmorphfuncext')}
        try:
            for keyed in (False,True):
                for appended in (False,True):
                    for scale in (.0832,1,3):
                        with self.subTest(keyed=keyed,appended=appended,scale=scale):
                            names = ['joint','hinge','tip','goal','source']
                            bones = [dict(index=i,name=names[i],parent=p,position=position,flags=6,layer=0)
                                     for i,(p,position) in enumerate([
                                         (-1,[0,2,0]),(0,[0,1,0]),(1,[0,0,0]),(-1,[0,.8,-.6]),(-1,[0,0,0])])]
                            bones[3]['layer'] = 1
                            bones[3]['ik'] = dict(target=2,iterations=64,angle=math.radians(30),
                                links=[dict(bone=1,min=[-math.pi,0,0],max=[math.radians(.5),0,0]),dict(bone=0)])
                            if appended:
                                bones[3]['grant'] = dict(parent=4,weight=1,rotation=False,position=True,local=False)
                            def key(frame,z=0):
                                return types.SimpleNamespace(frameNo=frame,position=Vector(0,0,z),rotation=Quaternion(),
                                    xCurve=[0,0,1,1],yCurve=[0,0,1,1],zCurve=[0,0,1,1],rCurve=[0,0,1,1])
                            motion = {'source':[key(0,-.4),key(10,.2)]}
                            if keyed: motion['goal'] = [key(0)]
                            c,mapping = controller(dict(bones=bones,morphs=[],rigid_bodies=[]),motion,LIB,
                                                   ROOT/'scripts/mmdd/item_morph_functions.py',scale)
                            mapped = {d['vmdName']:d['name'] for d in mapping['bones']}
                            transform = lambda name: c.getDummyBones(mapped[name]).transform
                            snapshots = {}
                            for frame in (0,10,5,0,5,10):
                                c.setFrame(frame)
                                self.assertLess((transform('tip').position-transform('goal').position).sqrMagnitude,1e-10)
                                if frame in snapshots:
                                    self.assertLess(Quaternion.Angle(transform('hinge').localRotation,snapshots[frame]),1e-4)
                                snapshots[frame] = transform('hinge').localRotation
                            # Disabling IK must still reset unkeyed links and apply the goal's grant.
                            slot = c.ikSolverIndex[mapped['goal']]
                            slot[2] = types.SimpleNamespace(enable=True,tempRequest=None,length=1,valueList=[False])
                            c.setFrame(0)
                            self.assertLess(Quaternion.Angle(transform('hinge').localRotation,Quaternion()),1e-4)
                            self.assertGreater((transform('tip').position-transform('goal').position).sqrMagnitude,.1*scale*scale)
                            slot[2] = None
                            c.updatePose()
                            self.assertLess((transform('tip').position-transform('goal').position).sqrMagnitude,1e-10)
        finally:
            for name,module in previous.items():
                if module is None: sys.modules.pop(name,None)
                else: sys.modules[name] = module

    def test_turning_crouch_does_not_snap_to_the_opposite_knee_stop(self):
        previous = {name: sys.modules.get(name) for name in ('UnityEngine','vmdlib','mmddmorphfuncext')}
        try:
            for yaw in (0,90,170):
                for scale in (.0832,1,3):
                    with self.subTest(yaw=yaw,scale=scale):
                        names = ['turning_%d' % i for i in range(9)]
                        parents = [-1,0,1,2,3,0,1,6,7]
                        heights = [0,1,1,.55,.15,0,1,.55,.15]
                        bones = [dict(index=i,name=names[i],parent=parents[i],position=[0,heights[i],0],
                                      flags=6,layer=0 if i<5 else 1 if i==5 else 2) for i in range(9)]
                        bones[5]['ik'] = dict(target=4,iterations=80,angle=2,
                                              links=[dict(bone=3,min=[-math.pi,0,0],max=[math.radians(.5),0,0]),dict(bone=2)])
                        for i in range(6,9):
                            bones[i]['grant'] = dict(parent=i-4,weight=1,rotation=True,position=False,local=False)
                        model = dict(bones=bones,morphs=[],rigid_bodies=[])
                        def key(frame,position=None,rotation=None):
                            return types.SimpleNamespace(frameNo=frame,position=position or Vector(),rotation=rotation or Quaternion(),
                                                         xCurve=[0,0,1,1],yCurve=[0,0,1,1],zCurve=[0,0,1,1],rCurve=[0,0,1,1])
                        motion = {name:[key(0)] for name in names[:6]}
                        motion[names[0]] = [key(0,rotation=Quaternion.Euler(0,yaw,0))]
                        motion[names[2]] = [key(0,rotation=Quaternion.Euler(-120,0,0))]
                        motion[names[3]] = [key(0,rotation=Quaternion.Euler(-90,0,0))]
                        motion[names[5]] = [key(0,Vector(0,.4,-.1)),key(10,Vector(0,.2,.1))]
                        c,mapping = controller(model,motion,LIB,ROOT/'scripts/mmdd/item_morph_functions.py',scale)
                        mapped = {d['vmdName']:d['name'] for d in mapping['bones']}
                        def transform(i):
                            return c.getDummyBones(mapped[names[i]]).transform
                        expected = {}
                        for frame in (0,5,10,5,0,10):
                            c.setFrame(frame)
                            self.assertLess((transform(4).position-transform(5).position).sqrMagnitude,1.1e-6,'frame %s' % frame)
                            self.assertLess((transform(8).position-transform(4).position).sqrMagnitude,1e-12)
                            self.assertLess((transform(1).position-Vector(0,scale,0)).sqrMagnitude,1e-12)
                            if frame in expected:
                                self.assertLess((transform(8).position-expected[frame]).sqrMagnitude,1e-12)
                            expected[frame] = transform(8).position
                        with contextlib.redirect_stdout(io.StringIO()):
                            c.buildWorkBoneIndex()
                        c.setFrame(0)
                        self.assertLess((transform(8).position-expected[0]).sqrMagnitude,1e-12)
                        c.resetPose()
                        c.setFrame(10)
                        self.assertLess((transform(8).position-expected[10]).sqrMagnitude,1e-12)
        finally:
            for name,module in previous.items():
                if module is None:
                    sys.modules.pop(name,None)
                else:
                    sys.modules[name] = module

    def test_crouch_seek_reset_and_deform_followers(self):
        previous = {name: sys.modules.get(name) for name in ('UnityEngine','vmdlib','mmddmorphfuncext')}
        try:
            for seed in range(8):
                for scale in (.0832,1,3):
                    with self.subTest(seed=seed,scale=scale):
                        order = list(range(9))
                        random.Random(seed).shuffle(order)
                        index = {old:new for new,old in enumerate(order)}
                        names = ['generated_%d_%d' % (seed,i) for i in range(9)]
                        parents = [-1,0,1,2,3,0,1,6,7]
                        heights = [0,1,1,.5,0,0,1,.5,0]
                        bones = [dict(index=index[i],name=names[i],parent=index[parents[i]] if parents[i]>=0 else -1,
                                      position=[0,heights[i],0],flags=6,layer=0 if i<5 else 1 if i==5 else 2)
                                 for i in range(9)]
                        bones[5]['ik'] = dict(target=index[4],iterations=80,angle=2,
                                              links=[dict(bone=index[3],min=[-math.pi,0,0],max=[math.radians(.5),0,0]),dict(bone=index[2])])
                        for i in range(6,9):
                            bones[i]['grant'] = dict(parent=index[i-4],weight=1,rotation=True,position=False,local=False)
                        model = dict(bones=[bones[i] for i in order],morphs=[],rigid_bodies=[])
                        def key(frame,position=None,rotation=None):
                            return types.SimpleNamespace(frameNo=frame,position=position or Vector(),rotation=rotation or Quaternion(),
                                                         xCurve=[0,0,1,1],yCurve=[0,0,1,1],zCurve=[0,0,1,1],rCurve=[0,0,1,1])
                        motion = {name:[key(0),key(10)] for name in names[:6]}
                        motion[names[0]] = [key(0,rotation=Quaternion.Euler(0,25,0))]
                        motion[names[1]] = [key(0,Vector(0,-.3,0)),key(10,Vector(0,-.7,0))]
                        motion[names[2]] = [key(0,rotation=Quaternion.Euler(50,0,0))]
                        motion[names[3]] = [key(0,rotation=Quaternion.Euler(-100,0,0))]
                        c,mapping = controller(model,motion,LIB,ROOT/'scripts/mmdd/item_morph_functions.py',scale)
                        mapped = {d['vmdName']:d['name'] for d in mapping['bones']}
                        def transform(i):
                            return c.getDummyBones(mapped[names[i]]).transform
                        first = {}
                        for frame in (0,10,5,10,0,5):
                            c.setFrame(frame)
                            self.assertLess((transform(4).position-transform(5).position).sqrMagnitude,1.1e-6)
                            self.assertLess((transform(8).position-transform(4).position).sqrMagnitude,1e-12)
                            self.assertGreater(transform(4).position.y,-.0011)
                            if frame in first:
                                self.assertLess((transform(8).position-first[frame]).sqrMagnitude,1e-12)
                            first[frame] = transform(8).position
                        with contextlib.redirect_stdout(io.StringIO()):
                            c.buildWorkBoneIndex()
                        c.setFrame(10)
                        self.assertLess((transform(8).position-first[10]).sqrMagnitude,1e-12)
                        c.resetPose()
                        c.setFrame(0)
                        self.assertLess((transform(8).position-first[0]).sqrMagnitude,1e-12)
        finally:
            for name,module in previous.items():
                if module is None:
                    sys.modules.pop(name,None)
                else:
                    sys.modules[name] = module


if __name__ == '__main__':
    unittest.main()
