# -*- coding: utf-8 -*-
"""Template for MMDD 3's MmddExtMorphFunc_*.py user extension API.

The generator appends _register(targets). No MMDD files are patched. Only the
selected item's renderers are touched; actor-owned caches avoid global scene
references and preserve independent instances of the same model.
"""
from __future__ import unicode_literals

_TARGETS = {}


def _resolve(actor, key):
    from UnityEngine import SkinnedMeshRenderer
    renderer_name, shape_name = _TARGETS[key]
    item = actor.objctrl.objectItem
    if item is None:
        raise ValueError('AssetImport morph: the selected item no longer exists')
    cache = getattr(actor, '_assetimport_morph_cache', None)
    if cache is None:
        cache = {}
        actor._assetimport_morph_cache = cache
    cached = cache.get(key)
    if cached is not None:
        old_item, matches = cached
        if old_item == item and all(renderer and renderer.sharedMesh == mesh for renderer, mesh, index in matches):
            return matches
    matches = []
    for renderer in item.GetComponentsInChildren[SkinnedMeshRenderer](True):
        mesh = renderer.sharedMesh
        if renderer.name != renderer_name or mesh is None:
            continue
        for index in range(mesh.blendShapeCount):
            if mesh.GetBlendShapeName(index) == shape_name:
                matches.append((renderer, mesh, index))
    if not matches:
        raise ValueError('AssetImport morph: expected at least one %s / %s, found %d; '
                         'enable Import Blendshapes and disable Material per Renderer'
                         % (renderer_name, shape_name, len(matches)))
    # KK may split a large material mesh into several identically named chunks.
    cache[key] = (item, matches)
    return matches


def _make_functions(key):
    def read(actor):
        return [float(renderer.GetBlendShapeWeight(index)) for renderer, mesh, index in _resolve(actor, key)]

    def write(actor, value):
        from System import Single
        matches = _resolve(actor, key)
        # Playback supplies a scalar; MMDD reset supplies the original per-chunk
        # list returned by read(), preserving distinct initial values.
        values = value if isinstance(value, (tuple, list)) else [value] * len(matches)
        if len(values) != len(matches):
            raise ValueError('AssetImport morph: renderer chunk count changed; rebind the item')
        for (renderer, mesh, index), weight in zip(matches, values):
            renderer.SetBlendShapeWeight(index, Single(weight))

    return read, write


def _register(targets):
    _TARGETS.update(targets)
    for key in targets:
        read, write = _make_functions(key)
        globals()['get_assetimport_' + key] = read
        globals()['set_assetimport_' + key] = write
    _install_runtime()


def _install_runtime():
    # MMDD normally scans extensions only at startup. Refresh on a missing
    # AssetImport method, once per changed extension inventory, so importing a
    # second model during playback does not leave its FUNC morphs unbound.
    import mmddmorphfuncext as library
    if not getattr(library.getMethod, '_assetimport_lazy', False):
        import os
        original = library.getMethod
        seen = [None]

        def get_method(actor, name, install=True):
            ours = name and name.startswith(('get_assetimport_', 'set_assetimport_'))
            if ours and not hasattr(actor, name) and name not in library._userFuncs:
                folder = os.path.dirname(os.path.abspath(library.__file__))
                stamp = tuple(sorted((f, os.path.getmtime(os.path.join(folder, f)),
                                      os.path.getsize(os.path.join(folder, f)))
                                     for f in os.listdir(folder)
                                     if f.startswith('MmddExtMorphFunc_AssetImport_Pmx_') and f.endswith('.py')))
                if stamp != seen[0]:
                    seen[0] = stamp
                    library.loadUserFuncFiles()
            return original(actor, name, install)

        get_method._assetimport_lazy = True
        library.getMethod = get_method

    import vmdlib
    original_solver = vmdlib.VmdLib.mmdIKSolver
    if getattr(original_solver, '_assetimport_limits_version', 0) < 3:
        def solve(bone, target, chain, iterations, step, limits, debug='ik_foot_r'):
            if bone.name.startswith('assetimport_pmx_') and any(limit is not None for limit in limits) and not _ordinary_knee(limits):
                return _solve_pmx_ik(bone, target, chain, iterations, step, limits)
            return original_solver(bone, target, chain, iterations, step, limits, debug)
        solve._assetimport_limits = True
        # Older per-model extensions can be loaded before the shared runtime.
        # Supersede their solver once; their existing marker prevents downgrade
        # if an old extension is loaded after this one. Foreign solvers still
        # pass through the original callable.
        solve._assetimport_limits_version = 3
        vmdlib.VmdLib.mmdIKSolver = staticmethod(solve)
    _install_pmx_schedule(vmdlib.MotionController)


def _apply_grant(transform, source, rest, mode, rate):
    from UnityEngine import Quaternion, Vector3
    if 'r' in mode:
        transform.localRotation = transform.localRotation * Quaternion.SlerpUnclamped(
            Quaternion.identity, source.localRotation, rate)
    if 'p' in mode:
        # PMX inherits the animated offset, including earlier grants, without
        # the source's rest position. The bound rest includes import/item scale.
        position, delta = transform.localPosition, source.localPosition - rest
        transform.localPosition = Vector3(position.x + delta.x * rate,
                                          position.y + delta.y * rate,
                                          position.z + delta.z * rate)


class _PmxGrantStep(object):
    # Use MMDD's indexed callback slot for the PMX grant, then a separate entry
    # for IK. A VMD IK-off key must not turn off the grant itself.
    needGetFrame = needPosition = needRotation = needTrackRot = needTrackPos = False
    needIK = ikDefaultEnable = True
    ikSolverIndex = (None, None, None)

    def __init__(self, controller, name):
        self.bonename = name + '__pmx_grant'
        self.mode, self.rate, source = controller.boneDefineMap[name].trackData
        self.transform = controller.getDummyBones(name).transform
        self.source = controller.getDummyBones(source).transform
        self.rest = controller.boneDefineMap[source].adjustment.basePosition

    def solveFunc(self):
        _apply_grant(self.transform, self.source, self.rest, self.mode, self.rate)


class _PmxIkStep(object):
    needGetFrame = needPosition = needRotation = needTrackRot = needTrackPos = False
    needIK = True

    def __init__(self, controller, name):
        self.bonename = name + '__pmx_ik'
        # Keep the list itself: MMDD fills its visibility slot after building
        # the bone index, when it imports the VMD IK/show keyframes.
        self.ikSolverIndex = controller.ikSolverIndex[name]
        self.ikDefaultEnable = self.ikSolverIndex[0].defaultEnable
        self.solveFunc = self.ikSolverIndex[3]


class _PmxRestStep(object):
    needGetFrame = needTrackRot = needTrackPos = needIK = False

    def __init__(self, controller, name):
        from UnityEngine import Quaternion
        definition = controller.boneDefineMap[name]
        self.bonename = name
        self.transform = controller.getDummyBones(name).transform
        self.needRotation, self.needPosition = definition.rotatable, definition.movable
        self.wRotation = definition.adjustment.GetAdjustedRotation(Quaternion.identity)
        self.wPosition = definition.adjustment.basePosition
        self.poseData = controller.poseData[name]


def _schedule_pmx(controller):
    ranks = dict((name, i) for i, name in enumerate(controller.boneNameList))
    owned = set(name for name in controller.boneNameList
                if name.startswith('assetimport_pmx_') and controller.boneDefineMap[name].enable)
    if not owned:
        return
    # An older per-model extension may already wrap the index builder. Its
    # position correction is replaced by the complete grant, not run twice.
    original = [entry for entry in controller.workBoneIndexBuffer
                if not (entry.bonename.endswith('__position_rest') and
                        entry.bonename[:-len('__position_rest')] in owned)]
    indexed = set(entry.bonename for entry in original)
    missing = [name for name in controller.boneNameList if name in owned and name not in indexed]
    result = []

    def append(entry):
        result.append(entry)
        name = entry.bonename
        if name not in owned:
            return
        definition = controller.boneDefineMap[name]
        entry.needTrackRot = entry.needTrackPos = entry.needIK = False
        if definition.isTrack:
            result.append(_PmxGrantStep(controller, name))
        if definition.isIK and controller.ikSolverIndex[name][0].enable:
            result.append(_PmxIkStep(controller, name))

    # Retain the mapping order and any unrelated extension entries. Include
    # unkeyed PMX bones so their IK runs and prior-frame IK rotations reset.
    cursor = 0
    for entry in original:
        rank = ranks.get(entry.bonename, -1)
        while cursor < len(missing) and ranks[missing[cursor]] < rank:
            append(_PmxRestStep(controller, missing[cursor]))
            cursor += 1
        append(entry)
    for name in missing[cursor:]:
        append(_PmxRestStep(controller, name))
    controller.workBoneIndexBuffer = result


def _install_pmx_schedule(controller_type):
    # IronPython 2 exposes unbound method objects rather than plain functions.
    def function(method):
        return getattr(method, 'im_func', method)

    original_ik = function(controller_type.processIK)
    original_track = function(controller_type.processTrack)
    if not getattr(original_track, '_assetimport_schedule_version', 0):
        def process_track(controller, name):
            if not name.startswith('assetimport_pmx_'):
                return original_track(controller, name)
            _PmxGrantStep(controller, name).solveFunc()
            deferred = getattr(controller, '_assetimport_deferred_ik', None)
            if deferred is not None and name in deferred:
                deferred.remove(name)
                original_ik(controller, name)
        # Retain the previous marker so old extensions loaded later cannot
        # wrap this method with the obsolete rest-offset correction.
        process_track._assetimport_position_grants = True
        process_track._assetimport_schedule_version = 1
        controller_type.processTrack = process_track

    if not getattr(original_ik, '_assetimport_schedule_version', 0):
        def process_ik(controller, name):
            deferred = getattr(controller, '_assetimport_deferred_ik', None)
            if (deferred is not None and name.startswith('assetimport_pmx_') and
                    controller.boneDefineMap[name].isTrack):
                deferred.add(name)
                return
            return original_ik(controller, name)
        process_ik._assetimport_schedule_version = 1
        controller_type.processIK = process_ik

    original_pose = function(controller_type.updatePose)
    if not getattr(original_pose, '_assetimport_schedule_version', 0):
        def update_pose(controller):
            previous = getattr(controller, '_assetimport_deferred_ik', None)
            controller._assetimport_deferred_ik = set()
            try:
                return original_pose(controller)
            finally:
                controller._assetimport_deferred_ik = previous
        update_pose._assetimport_schedule_version = 1
        controller_type.updatePose = update_pose

    original_build = function(controller_type.buildWorkBoneIndex)
    if not getattr(original_build, '_assetimport_schedule_version', 0):
        def build_index(controller):
            result = original_build(controller)
            _schedule_pmx(controller)
            return result
        build_index._assetimport_position_grants = True
        build_index._assetimport_schedule_version = 1
        controller_type.buildWorkBoneIndex = build_index


def _ordinary_knee(limits):
    if len(limits) != 2 or limits[0] is None or limits[1] is not None:
        return False
    expected = ((0.5, 180.0), (0.0, 0.0), (0.0, 0.0))
    return all(abs(limits[0][axis][side] - expected[axis][side]) < 0.001
               for axis in range(3) for side in range(2))


def _clamp_angle(angle, bounds):
    # Choose the equivalent Euler angle nearest the permitted interval. This
    # avoids a discontinuity at 0/360 and also accepts ranges crossing 180.
    low, high = bounds
    angle = (angle + 180.0) % 360.0 - 180.0
    candidates = (angle - 360.0, angle, angle + 360.0)
    best = min(candidates, key=lambda v: abs(v - min(high, max(low, v))))
    return min(high, max(low, best))


def _hinge_axis(bounds):
    if bounds is None:
        return None
    free = [i for i in range(3) if abs(bounds[i][1] - bounds[i][0]) > 1e-6]
    if len(free) == 1 and all(abs(value) < 1e-6 for i in range(3) if i != free[0] for value in bounds[i]):
        return free[0]
    return None


def _hinge_rotation(transform, source, dest, axis, bounds, step):
    # A hinge has one signed angle. Unity's Z-X-Y Euler decomposition can encode
    # X=120 as (60,180,180); zeroing the locked axes would turn a deep bend into
    # X=60. Solve in the hinge plane and clamp its quaternion twist instead.
    import math
    from UnityEngine import Quaternion, Vector3
    parent = transform.parent.rotation if transform.parent else Quaternion.identity
    unit = Vector3(1 if axis == 0 else 0, 1 if axis == 1 else 0, 1 if axis == 2 else 0)
    world_axis = parent * unit
    def dot(a, b):
        return a.x*b.x + a.y*b.y + a.z*b.z
    source_axis, dest_axis = dot(source, world_axis), dot(dest, world_axis)
    if source.sqrMagnitude - source_axis*source_axis < 1e-12 or dest.sqrMagnitude - dest_axis*dest_axis < 1e-12:
        return transform.rotation
    delta = math.degrees(math.atan2(dot(world_axis, Vector3.Cross(source, dest)),
                                   dot(source, dest) - source_axis*dest_axis))
    local = Quaternion.Inverse(parent) * transform.rotation
    current = math.degrees(2 * math.atan2((local.x, local.y, local.z)[axis], local.w))
    # Unwrap the stored orientation into the joint interval, then clip the step
    # along that interval. Wrapping the requested angle instead can select the
    # opposite stop: -114 degrees in [-0.5, 180] becomes 246, then snaps to 180.
    # That both exceeds the step budget and can trap a two-link chain folded shut.
    current = _clamp_angle(current, bounds[axis])
    low, high = bounds[axis]
    angle = min(high, max(low, current + min(step, max(-step, delta))))
    return parent * Quaternion.AngleAxis(angle, unit)


def _two_link_hinge_rotation(target, chain, axis, bounds, goal, step):
    # A free proximal joint can aim the entire two-segment chain at the goal.
    # Solve the hinge's reach first, avoiding CCD's fully folded local minimum.
    # Use the actual segment vectors, including a bent rest pose or offsets
    # along the hinge axis. This is independent of bone names and world axes.
    import math
    from UnityEngine import Quaternion, Vector3
    hinge, proximal = chain[0].transform, chain[1].transform
    tip = target.transform
    if hinge.parent != proximal or tip.parent != hinge:
        return None
    unit = Vector3(1 if axis == 0 else 0, 1 if axis == 1 else 0, 1 if axis == 2 else 0)
    upper = Quaternion.Inverse(proximal.rotation) * (hinge.position - proximal.position)
    lower = Quaternion.Inverse(hinge.rotation) * (tip.position - hinge.position)
    def dot(a, b):
        return a.x*b.x + a.y*b.y + a.z*b.z
    axial = dot(upper, unit) * dot(lower, unit)
    cosine = dot(upper, lower) - axial
    sine = dot(upper, Vector3.Cross(unit, lower))
    radius = math.sqrt(cosine*cosine + sine*sine)
    if radius < 1e-12:
        return None
    distance2 = (goal - proximal.position).sqrMagnitude
    base = upper.sqrMagnitude + lower.sqrMagnitude + 2*axial
    phase = math.atan2(sine, cosine)
    bend = math.acos(min(1.0, max(-1.0, (distance2-base)/(2*radius))))
    local = Quaternion.Inverse(proximal.rotation) * hinge.rotation
    current = _clamp_angle(math.degrees(2*math.atan2((local.x,local.y,local.z)[axis],local.w)), bounds[axis])
    candidates = [_clamp_angle(math.degrees(phase+sign*bend), bounds[axis]) for sign in (-1,1)]
    # If the requested reach is outside the permitted interval, extrema inside
    # that interval can be closer than either clipped equation root.
    candidates += [_clamp_angle(math.degrees(phase)+turn, bounds[axis]) for turn in (0,180)]
    candidates += list(bounds[axis])
    def score(angle):
        radians = math.radians(angle)
        reach = base + 2*(cosine*math.cos(radians) + sine*math.sin(radians))
        return abs(reach-distance2)
    best_error = min(score(angle) for angle in candidates)
    tolerance = max(upper.sqrMagnitude + lower.sqrMagnitude, 1e-12)*1e-10
    desired = min((angle for angle in candidates if score(angle) <= best_error+tolerance),
                  key=lambda angle: abs(angle-current))
    angle = current + min(step, max(-step, desired-current))
    return proximal.rotation * Quaternion.AngleAxis(angle, unit)


def _solve_pmx_ik(bone, target, chain, iterations, step, limits):
    # Scoped CCD for non-knee PMX constraints. MMDD's stock MMD solver treats
    # every limited link as a positive-X knee, including two-axis toe joints.
    if iterations <= 0 or step <= 0 or not chain:
        return
    from UnityEngine import Quaternion, Vector3
    from vmdlib import VmdLib
    goal = bone.transform.position
    hinges = [_hinge_axis(bounds) for bounds in limits]
    two_link = len(chain) == 2 and hinges[0] is not None and limits[1] is None
    for _ in range(iterations):
        moved = False
        for link, bounds, hinge in zip(chain, limits, hinges):
            end = target.transform.position
            if (goal - end).sqrMagnitude < 0.000001:
                return
            transform = link.transform
            source, dest = end - transform.position, goal - transform.position
            if hinge is not None:
                result = _two_link_hinge_rotation(target, chain, hinge, bounds, goal, step) if two_link else None
                if result is None:
                    if source.sqrMagnitude < 1e-12 or dest.sqrMagnitude < 1e-12:
                        continue
                    result = _hinge_rotation(transform, source, dest, hinge, bounds, step)
                if Quaternion.Angle(transform.rotation, result) > 1e-6:
                    moved = True
                    transform.rotation = result
                continue
            if source.sqrMagnitude < 1e-12 or dest.sqrMagnitude < 1e-12:
                continue
            angle, axis = VmdLib.rotateVectorFromTo(source, dest)
            if angle < 1e-6:
                continue
            if axis.sqrMagnitude < 1e-12:
                # Antiparallel directions have no unique cross-product axis.
                axis = Vector3.Cross(source, Vector3.right)
                if axis.sqrMagnitude < 1e-12:
                    axis = Vector3.Cross(source, Vector3.up)
            result = Quaternion.AngleAxis(min(angle, step), axis) * transform.rotation
            if bounds is not None:
                parent = transform.parent.rotation if transform.parent else Quaternion.identity
                local = Quaternion.Inverse(parent) * result
                euler = local.eulerAngles
                result = parent * Quaternion.Euler(_clamp_angle(euler.x, bounds[0]),
                                                   _clamp_angle(euler.y, bounds[1]),
                                                   _clamp_angle(euler.z, bounds[2]))
            if Quaternion.Angle(transform.rotation, result) > 1e-6:
                moved = True
                transform.rotation = result
        if not moved:
            return
