"""Numerical CCD checks with transform doubles, not Unity/IronPython playback."""
import importlib.util
import math
from pathlib import Path
import sys
import types
import unittest


class Vector:
    def __init__(self, x=0, y=0, z=0):
        self.x, self.y, self.z = x, y, z

    def __add__(self, other):
        return Vector(self.x + other.x, self.y + other.y, self.z + other.z)

    def __sub__(self, other):
        return Vector(self.x - other.x, self.y - other.y, self.z - other.z)

    @property
    def sqrMagnitude(self):
        return self.x**2 + self.y**2 + self.z**2

    @staticmethod
    def Cross(a, b):
        return Vector(a.y*b.z-a.z*b.y, a.z*b.x-a.x*b.z, a.x*b.y-a.y*b.x)


class Quaternion:
    def __init__(self, x=0, y=0, z=0, w=1):
        self.x, self.y, self.z, self.w = x, y, z, w

    def __mul__(self, b):
        if isinstance(b, Vector):
            q = self * Quaternion(b.x, b.y, b.z, 0) * self.Inverse(self)
            return Vector(q.x, q.y, q.z)
        a = self
        return Quaternion(a.w*b.x+a.x*b.w+a.y*b.z-a.z*b.y,
                          a.w*b.y-a.x*b.z+a.y*b.w+a.z*b.x,
                          a.w*b.z+a.x*b.y-a.y*b.x+a.z*b.w,
                          a.w*b.w-a.x*b.x-a.y*b.y-a.z*b.z)

    @staticmethod
    def Inverse(q):
        return Quaternion(-q.x, -q.y, -q.z, q.w)

    @staticmethod
    def AngleAxis(degrees, axis):
        if axis.sqrMagnitude < 1e-20:
            return Quaternion()
        half = math.radians(degrees) / 2
        scale = math.sin(half) / math.sqrt(axis.sqrMagnitude)
        return Quaternion(axis.x*scale, axis.y*scale, axis.z*scale, math.cos(half))

    @staticmethod
    def Euler(x, y, z):
        # Unity's documented Z, X, Y order.
        return (Quaternion.AngleAxis(y, Vector(0, 1, 0)) *
                Quaternion.AngleAxis(x, Vector(1, 0, 0)) *
                Quaternion.AngleAxis(z, Vector(0, 0, 1)))

    @property
    def eulerAngles(self):
        x, y, z, w = self.x, self.y, self.z, self.w
        return Vector(math.degrees(math.asin(max(-1, min(1, 2*(w*x-y*z))))) % 360,
                      math.degrees(math.atan2(2*(x*z+w*y), 1-2*(x*x+y*y))) % 360,
                      math.degrees(math.atan2(2*(x*y+w*z), 1-2*(x*x+z*z))) % 360)

    @staticmethod
    def Angle(a, b):
        dot = abs(a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w)
        return math.degrees(2*math.acos(min(1, dot)))


class Transform:
    def __init__(self, position=None, parent=None, rotation=None):
        self.offset = position or Vector()
        self.parent = parent
        self.local = rotation or Quaternion()

    @property
    def position(self):
        if self.parent:
            return self.parent.position + self.parent.rotation * self.offset
        return self.offset

    @property
    def rotation(self):
        return self.parent.rotation * self.local if self.parent else self.local

    @rotation.setter
    def rotation(self, value):
        self.local = Quaternion.Inverse(self.parent.rotation) * value if self.parent else value


def from_to(a, b):
    dot = (a.x*b.x+a.y*b.y+a.z*b.z) / math.sqrt(a.sqrMagnitude*b.sqrMagnitude)
    return math.degrees(math.acos(max(-1, min(1, dot)))), Vector.Cross(a, b)


class RuntimeChecks(unittest.TestCase):
    def test_inactive_solver_never_reads_or_changes_transforms(self):
        # Missing transform attributes make unintended access fail immediately.
        for iterations, step, chain in ((0, 20, [object()]), (5, 0, [object()]), (5, 20, [])):
            self.runtime._solve_pmx_ik(object(), object(), chain, iterations, step, [None])

    def setUp(self):
        self.modules = dict(sys.modules)
        Vector.right, Vector.up = Vector(1, 0, 0), Vector(0, 1, 0)
        Quaternion.identity = Quaternion()
        self.stock_calls = []
        lib = type('VmdLib', (), {'rotateVectorFromTo': staticmethod(from_to),
                                 'mmdIKSolver': staticmethod(lambda *args: self.stock_calls.append(args))})
        sys.modules['UnityEngine'] = types.SimpleNamespace(Vector3=Vector, Quaternion=Quaternion)
        controller = type('MotionController', (), {'processTrack': lambda *a: None, 'buildWorkBoneIndex': lambda *a: None,
                                                 'processIK': lambda *a: None, 'updatePose': lambda *a: None})
        sys.modules['vmdlib'] = types.SimpleNamespace(VmdLib=lib, MotionController=controller)
        self.library = types.SimpleNamespace(getMethod=lambda *args: None, _userFuncs={})
        sys.modules['mmddmorphfuncext'] = self.library
        path = Path(__file__).resolve().parents[2] / 'scripts/mmdd/item_morph_functions.py'
        spec = importlib.util.spec_from_file_location('pmx_runtime_test', path)
        self.runtime = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.runtime)
        self.runtime._register({})
        self.solve = lib.mmdIKSolver

    def tearDown(self):
        for name in ('UnityEngine', 'vmdlib', 'mmddmorphfuncext'):
            if name in self.modules:
                sys.modules[name] = self.modules[name]
            else:
                sys.modules.pop(name, None)

    def chain(self, goal, parent_rotation=None):
        parent = Transform(rotation=parent_rotation)
        pivot = Transform(parent=parent)
        tip = types.SimpleNamespace(transform=Transform(Vector(0, 0, 1), pivot))
        bone = types.SimpleNamespace(name='assetimport_pmx_0015', transform=Transform(parent.rotation * goal))
        return bone, tip, [types.SimpleNamespace(transform=pivot)]

    def test_toe_rotates_on_two_axes_under_rotated_parent(self):
        goal = Quaternion.Euler(-12, 35, 0) * Vector(0, 0, 1)
        bone, tip, chain = self.chain(goal, Quaternion.Euler(15, -25, 10))
        self.solve(bone, tip, chain, 40, 20, [[[-35, 20], [-60, 60], [0, 0]]])
        self.assertLess((bone.transform.position-tip.transform.position).sqrMagnitude, .00001)
        self.assertEqual(self.stock_calls, [])
        euler = chain[0].transform.local.eulerAngles
        self.assertAlmostEqual(self.runtime._clamp_angle(euler.z, [-180, 180]), 0, places=4)
        self.assertAlmostEqual(euler.y, 35, delta=.2)

    def test_limits_and_step_budget(self):
        goal = Quaternion.Euler(0, 80, 0) * Vector(0, 0, 1)
        bone, tip, chain = self.chain(goal)
        limits = [[[0, 0], [-20, 20], [0, 0]]]
        self.solve(bone, tip, chain, 1, 5, limits)
        self.assertAlmostEqual(chain[0].transform.local.eulerAngles.y, 5)
        self.solve(bone, tip, chain, 100, 5, limits)
        self.assertAlmostEqual(chain[0].transform.local.eulerAngles.y, 20)

    def test_deep_crouch_hinges_across_ninety_degrees(self):
        axes = (Vector(1,0,0), Vector(0,1,0), Vector(0,0,1))
        for axis, unit in enumerate(axes):
            for sign in (-1,1):
                for desired in (85,95,120,150):
                    with self.subTest(axis=axis, sign=sign, desired=desired):
                        parent = Transform(rotation=Quaternion.Euler(15,25,-10))
                        offset = Vector(0,0,-.5) if axis == 1 else Vector(0,-.5,0)
                        base = Vector(0,1,0)
                        hip = Transform(base,parent,Quaternion.AngleAxis(-sign*50,unit))
                        knee = Transform(offset,hip,Quaternion.AngleAxis(sign*100,unit))
                        tip = types.SimpleNamespace(transform=Transform(offset,knee))
                        a = Quaternion.AngleAxis(-sign*desired/2,unit)
                        b = Quaternion.AngleAxis(sign*desired,unit)
                        goal = base+a*offset+(a*b)*offset
                        bone = types.SimpleNamespace(name='assetimport_pmx_shuffled_hinge',transform=Transform(parent.rotation*goal))
                        bounds = [[0,0] for _ in range(3)]
                        bounds[axis] = [-.5,180] if sign == 1 else [-180,.5]
                        self.solve(bone,tip,[types.SimpleNamespace(transform=knee),types.SimpleNamespace(transform=hip)],
                                   80,114.6,[bounds,None])
                        self.assertLess((bone.transform.position-tip.transform.position).sqrMagnitude, 1.1e-6)
                        self.assertLess(Quaternion.Angle(knee.local, b), .3)

    def test_hinge_limits_step_and_unreachable_off_plane_target(self):
        parent = Transform(rotation=Quaternion.Euler(-30,15,45))
        joint = Transform(parent=parent,rotation=Quaternion.Euler(100,0,0))
        source = parent.rotation * (joint.local * Vector(0,0,1))
        dest = parent.rotation * (Quaternion.Euler(145,0,0)*Vector(0,0,1)+Vector(.7,0,0))
        limits = [[-.5,130],[0,0],[0,0]]
        result = self.runtime._hinge_rotation(joint,source,dest,0,limits,5)
        self.assertLess(Quaternion.Angle(Quaternion.Inverse(parent.rotation)*result,Quaternion.Euler(105,0,0)),1e-5)
        result = self.runtime._hinge_rotation(joint,source,dest,0,limits,90)
        self.assertLess(Quaternion.Angle(Quaternion.Inverse(parent.rotation)*result,Quaternion.Euler(130,0,0)),1e-5)
        for angle in (170,185,195):
            joint.local = Quaternion.Euler(angle,0,0)
            source = parent.rotation*(joint.local*Vector(0,0,1))
            dest = parent.rotation*(Quaternion.Euler(190,0,0)*Vector(0,0,1))
            result = self.runtime._hinge_rotation(joint,source,dest,0,[[170,200],[0,0],[0,0]],30)
            self.assertLess(Quaternion.Angle(Quaternion.Inverse(parent.rotation)*result,Quaternion.Euler(190,0,0)),1e-5)
        self.assertIsNone(self.runtime._hinge_axis([[-20,120],[5,5],[0,0]]), 'nonzero locked axes are not a zero-axis hinge')

    def test_hinge_step_cannot_wrap_across_the_forbidden_arc(self):
        for axis, unit in enumerate((Vector(1,0,0),Vector(0,1,0),Vector(0,0,1))):
            for sign in (-1,1):
                for quaternion_sign in (-1,1):
                    for initial, goal, expected in ((0,-120,-.5),(180,300,180)):
                        with self.subTest(axis=axis,sign=sign,quaternion_sign=quaternion_sign,initial=initial):
                            parent = Transform(rotation=Quaternion.Euler(-30,15,45))
                            rotation = Quaternion.AngleAxis(sign*initial,unit)
                            rotation = Quaternion(*(quaternion_sign*getattr(rotation,c) for c in ('x','y','z','w')))
                            joint = Transform(parent=parent,rotation=rotation)
                            offset = Vector(0,1,0) if axis == 2 else Vector(0,0,1)
                            source = joint.rotation*offset
                            dest = parent.rotation*(Quaternion.AngleAxis(sign*goal,unit)*offset)
                            limits = [[0,0] for _ in range(3)]
                            limits[axis] = [-.5,180] if sign == 1 else [-180,.5]
                            result = self.runtime._hinge_rotation(joint,source,dest,axis,limits,114.6)
                            self.assertLessEqual(Quaternion.Angle(joint.rotation,result),114.6+1e-5)
                            wanted = parent.rotation*Quaternion.AngleAxis(sign*expected,unit)
                            self.assertLess(Quaternion.Angle(wanted,result),1e-5)

    def test_two_link_reach_with_bent_rest_offsets_and_step_limits(self):
        for axis, unit in enumerate((Vector(1,0,0),Vector(0,1,0),Vector(0,0,1))):
            for sign in (-1,1):
                for bend in (45,135,175):
                    with self.subTest(axis=axis,sign=sign,bend=bend):
                        parent = Transform(rotation=Quaternion.Euler(15,-35,60))
                        # Unequal segments with both in-plane and axial rest offsets.
                        upper = Vector(.02,-.45,.03) if axis != 1 else Vector(.03,.02,-.45)
                        lower = Vector(-.01,-.4,-.02) if axis != 1 else Vector(-.02,-.01,-.4)
                        base = Vector(.2,1,-.1)
                        hip = Transform(base,parent,Quaternion.AngleAxis(sign*120,unit))
                        knee = Transform(upper,hip,Quaternion.AngleAxis(sign*90,unit))
                        tip = types.SimpleNamespace(transform=Transform(lower,knee))
                        a,b = Quaternion.AngleAxis(-sign*35,unit),Quaternion.AngleAxis(sign*bend,unit)
                        goal = base+a*upper+(a*b)*lower
                        bone = types.SimpleNamespace(name='assetimport_pmx_reach',transform=Transform(parent.rotation*goal))
                        chain = [types.SimpleNamespace(transform=knee),types.SimpleNamespace(transform=hip)]
                        bounds = [[0,0] for _ in range(3)]
                        bounds[axis] = [-.5,180] if sign == 1 else [-180,.5]
                        before = [joint.transform.local for joint in chain]
                        self.solve(bone,tip,chain,1,5,[bounds,None])
                        for previous,joint in zip(before,chain):
                            self.assertLessEqual(Quaternion.Angle(previous,joint.transform.local),5+1e-5)
                        self.solve(bone,tip,chain,80,30,[bounds,None])
                        self.assertLess((tip.transform.position-bone.transform.position).sqrMagnitude,1.1e-6)
                        current = math.degrees(2*math.atan2((knee.local.x,knee.local.y,knee.local.z)[axis],knee.local.w))
                        self.assertAlmostEqual(current,self.runtime._clamp_angle(current,bounds[axis]),places=5)

    def test_two_link_reach_rejects_nonadjacent_chain(self):
        hip = Transform()
        knee = Transform(Vector(0,-.5,0),hip)
        extra = Transform(Vector(0,-.1,0),knee)
        tip = types.SimpleNamespace(transform=Transform(Vector(0,-.4,0),extra))
        chain = [types.SimpleNamespace(transform=knee),types.SimpleNamespace(transform=hip)]
        self.assertIsNone(self.runtime._two_link_hinge_rotation(tip,chain,0,[[-.5,180],[0,0],[0,0]],Vector(0,-.8,0),30))

    def test_two_link_unreachable_and_joint_coincident_goals(self):
        for distance, expected in ((2,20),(.1,140),(.6,None),(0,140)):
            with self.subTest(distance=distance):
                hip = Transform()
                knee = Transform(Vector(0,-.6,0),hip,Quaternion.Euler(60,0,0))
                tip = types.SimpleNamespace(transform=Transform(Vector(0,-.4,0),knee))
                chain = [types.SimpleNamespace(transform=knee),types.SimpleNamespace(transform=hip)]
                bone = types.SimpleNamespace(name='assetimport_pmx_reach',transform=Transform(Vector(0,-distance,0)))
                self.solve(bone,tip,chain,80,15,[[[20,140],[0,0],[0,0]],None])
                error = math.sqrt((tip.transform.position-bone.transform.position).sqrMagnitude)
                if expected is None:
                    self.assertLess(error,.0011)
                else:
                    self.assertLess(Quaternion.Angle(knee.local,Quaternion.Euler(expected,0,0)),1e-5)
                    reach = math.sqrt(.6**2+.4**2+2*.6*.4*math.cos(math.radians(expected)))
                    self.assertAlmostEqual(error,abs(reach-distance),places=5)

    def test_old_model_extension_cannot_block_new_solver_or_downgrade_it(self):
        lib = sys.modules['vmdlib'].VmdLib
        old_calls = []
        def old_solver(*args):
            old_calls.append(args)
        old_solver._assetimport_limits = True
        old_solver._assetimport_limits_version = 2
        lib.mmdIKSolver = staticmethod(old_solver)
        self.runtime._register({})
        current = lib.mmdIKSolver
        self.assertEqual(current._assetimport_limits_version,3)
        bone, tip, chain = self.chain(Vector(0,0,1))
        current(bone,tip,chain,2,20,[[[-.5,180],[0,0],[0,0]]])
        self.assertEqual(old_calls,[])
        bone.name = 'foreign_plugin'
        current(bone,tip,chain,2,20,[[[-.5,180],[0,0],[0,0]]])
        self.assertEqual(len(old_calls),1)
        self.runtime._register({})
        self.assertIs(lib.mmdIKSolver,current)
        # This is the legacy installer's guard, including on late model imports.
        self.assertTrue(current._assetimport_limits)
        self.assertFalse(current._assetimport_limits_version < 2, 'v2 extensions must not replace the newer solver')

    def test_antiparallel_and_degenerate(self):
        bone, tip, chain = self.chain(Vector(0, 0, -1))
        self.solve(bone, tip, chain, 4, 10, [[[-180, 180], [-180, 180], [-180, 180]]])
        self.assertGreater(Quaternion.Angle(chain[0].transform.rotation, Quaternion()), 0)
        tip.transform.offset = Vector()
        self.solve(bone, tip, chain, 40, 20, [[[0, 0], [-60, 60], [0, 0]]])

    def test_stock_models_and_knees_are_untouched(self):
        bone, tip, chain = self.chain(Vector(0, 0, 1))
        bone.name = 'other_plugin_ik'
        self.solve(bone, tip, chain, 3, 10, [[[0, 0], [-60, 60], [0, 0]]])
        bone.name = 'assetimport_pmx_0014'
        self.solve(bone, tip, chain, 40, 57, [[[.5, 180], [0, 0], [0, 0]], None])
        self.assertEqual(len(self.stock_calls), 2)
        method = self.library.getMethod
        self.runtime._register({})
        self.assertIs(method, self.library.getMethod, 'Reload must not stack discovery hooks')

    def test_angle_wrap(self):
        self.assertEqual(self.runtime._clamp_angle(350, [-20, 20]), -10)
        self.assertEqual(self.runtime._clamp_angle(190, [170, 200]), 190)
        self.assertEqual(self.runtime._clamp_angle(50, [0, 0]), 0)


if __name__ == '__main__':
    unittest.main()
