#!/usr/bin/env python3
"""Run MMDD 3's real item bind / FUNC index / apply / reset with engine doubles.

This deliberately goes beyond JSON validation: reproduce the old BLEND gate,
then execute the user extension loader and actual MorphController methods.
Neither Unity animation nor the IronPython CLR bridge is simulated here.
"""
import argparse
import ast
import copy
import importlib.util
import importlib
import json
from pathlib import Path
import shutil
import sys
import tempfile
import traceback
import types


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


class Py2Dict(dict):
    def has_key(self, key):
        return key in self


class Mesh:
    def __init__(self, shapes):
        self.shapes = list(shapes)
        self.blendShapeCount = len(shapes)

    def GetBlendShapeName(self, index):
        return self.shapes[index]


class Renderer:
    def __init__(self, data, value=0):
        self.name = data['name']
        self.sharedMesh = Mesh(data['shapes'])
        self.weights = [float(value + i) for i in range(len(data['shapes']))]
        self.alive = True

    def __bool__(self):
        return self.alive

    def GetBlendShapeWeight(self, index):
        return self.weights[index]

    def SetBlendShapeWeight(self, index, value):
        self.weights[index] = value


class ComponentQuery:
    def __init__(self, item):
        self.item = item

    def __getitem__(self, kind):
        assert kind is Renderer
        return lambda include_inactive: self.item.renderers


class Item:
    def __init__(self, renderers):
        self.renderers = renderers
        self.GetComponentsInChildren = ComponentQuery(self)


class Prop:
    def __init__(self, item):
        self.objctrl = types.SimpleNamespace(objectItem=item)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--mapping', type=Path, required=True)
    p.add_argument('--scene-report', type=Path, required=True)
    p.add_argument('--mmdd-lib', type=Path, required=True)
    p.add_argument('--extension', type=Path, required=True)
    a = p.parse_args()
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'testkit'))
    from check_motion_probe import install_data_doubles, Vector3, Quaternion
    install_data_doubles()
    sys.modules['UnityEngine'].SkinnedMeshRenderer = Renderer
    studio = types.ModuleType('Studio')
    studio.OCIChar = type('OCIChar', (), {})
    studio.OCIItem = type('OCIItem', (), {})
    sys.modules['Studio'] = studio
    pe = types.ModuleType('extplugins')
    pe.AIPE = lambda: types.SimpleNamespace(isDetected=True)
    sys.modules['extplugins'] = pe

    data = json.loads(a.mapping.read_text())
    report = json.loads(a.scene_report.read_text())
    renderers = [Renderer(r, 3 + i) for i, r in enumerate(report['renderers'])]
    # A KK material mesh can be split into multiple same-name renderer chunks.
    # Always exercise this, even when the supplied model is below that limit.
    duplicate = next(r for r in report['renderers'] if r['shapes'])
    renderers.append(Renderer(duplicate, 87))
    actor = Prop(Item(renderers))
    # Existing actors may still carry methods installed by 4.2.1. The new bridge
    # uses v2 names and cannot accidentally reuse scalar return/cache contracts.
    for morph in data['morphs']:
        for get_name, set_name, *_ in morph['funcList']:
            assert '_assetimport_v2_' in get_name
            setattr(actor, get_name.replace('_v2_', '_'), lambda: (_ for _ in ()).throw(AssertionError('old bridge reused')))
            setattr(actor, set_name.replace('_v2_', '_'), lambda value: (_ for _ in ()).throw(AssertionError('old bridge reused')))
    tree = ast.parse((a.mmdd_lib / 'vmdlib.py').read_text(encoding='utf-8-sig'))
    cls = next(n for n in tree.body if isinstance(n, ast.ClassDef) and n.name == 'MorphController')
    methods = {'MorphDefine', 'bindChara', 'buildMorphIndex', 'setFuncFrameIndexed',
               'resetAllFuncMorph', 'resetAllBlendMorph', 'resetAllBoneMorph'}
    cls.body = [n for n in cls.body if isinstance(n, (ast.FunctionDef, ast.ClassDef)) and n.name in methods]
    lib = next(n for n in tree.body if isinstance(n, ast.ClassDef) and n.name == 'VmdLib')
    lib.body = [n for n in lib.body if isinstance(n, ast.FunctionDef) and n.name in ('getScaledValue', 'interpolateRangeValue')]
    env = {'Prop': Prop, 'Actor': type('Actor', (), {}), 'GData': types.SimpleNamespace,
           'Vector3': Vector3, 'Quaternion': Quaternion, 'Single': float, 'Double': float,
           'copy': copy, 'traceback': traceback}
    exec(compile(ast.Module(body=[lib, cls], type_ignores=[]), str(a.mmdd_lib / 'vmdlib.py'), 'exec'), env)
    # Only solver installation is needed here; numerical IK has separate checks.
    env['VmdLib'].mmdIKSolver = staticmethod(lambda *args: None)
    motion = type('MotionController', (), {'processTrack': lambda *a: None, 'buildWorkBoneIndex': lambda *a: None,
                                         'processIK': lambda *a: None, 'updatePose': lambda *a: None})
    sys.modules['vmdlib'] = types.SimpleNamespace(VmdLib=env['VmdLib'], MotionController=motion)
    controller = env['MorphController']()
    controller.motionCtrl = types.SimpleNamespace(boneType='item', ociChar=actor.objctrl)
    controller.morphDefineMap = {'old': types.SimpleNamespace(type='blend')}
    try:
        controller.bindChara(actor)
        raise AssertionError('Old ITEM + BLEND must reproduce the reported failure')
    except Exception as e:
        assert 'Cannot import blend type morph' in str(e), str(e)
    print('PASS: reproduced original ITEM + BLEND failure with XXPE detected.')

    controller.morphDefineMap = {}
    for d in data['morphs']:
        assert d['type'] == 'FUNC'
        md = controller.MorphDefine(d['name'], 'func', True)
        md.funcList = d['funcList']
        controller.morphDefineMap[d['vmdName']] = md
    controller.bindChara(actor)
    controller.morphNameList = list(controller.morphDefineMap)
    controller.morphKeyFrames = Py2Dict((name, [types.SimpleNamespace(frameNo=0), types.SimpleNamespace(frameNo=30)])
                                      for name in controller.morphNameList)

    # Copy original upstream loader unmodified, beside the generated extension.
    with tempfile.TemporaryDirectory() as folder:
        folder = Path(folder)
        shutil.copy2(a.mmdd_lib / 'mmddmorphfuncext.py', folder)
        shutil.copy2(a.extension, folder)
        sys.path.insert(0, str(folder))
        extension_lib = load_module('mmddmorphfuncext', folder / 'mmddmorphfuncext.py')
        # Python 2's MethodType takes a third class argument; no logic is changed.
        extension_lib.types = types.SimpleNamespace(MethodType=lambda fn, obj, cls: types.MethodType(fn, obj))
        extension_lib.reload = importlib.reload
        extension_lib.loadUserFuncFiles()
        before = [list(r.weights) for r in renderers]
        controller.buildMorphIndex()
        assert len(controller.funcIndexBuffer) == len(data['morphs']) > 0
        assert len(controller.blendIndexBuffer) == 0
        for mi in controller.funcIndexBuffer:
            for weight in (0, .5, 1, 0):
                mi.weight = weight
                controller.setFuncFrameIndexed(mi, True)
                for get_fn, _, _, _ in mi.solvedFuncList:
                    assert all(abs(value - weight * 100) < 1e-6 for value in get_fn())
        controller.resetAllFuncMorph()
        assert [r.weights for r in renderers] == before, 'Unbinding must restore distinct original weights'
        controller.buildMorphIndex()
        assert len(controller.funcIndexBuffer) == len(data['morphs'])

        # Same model in a second item must have independent targets / weights.
        other = Prop(Item([Renderer(r, 9) for r in report['renderers']]))
        d = data['morphs'][0]['funcList'][0]
        get2 = extension_lib.getMethod(other, d[0])
        set2 = extension_lib.getMethod(other, d[1])
        old = get2()
        set2(71)
        assert all(value == 71 for value in get2()) and [r.weights for r in renderers] == before
        set2(old)

        # Import a different model AFTER MMDD startup and the first actor's
        # playback/index creation. The stock loader never scans this file.
        late_key = 'v2_late_model'
        template = Path(__file__).with_name('item_morph_functions.py').read_text(encoding='utf-8')
        late_file = folder / 'MmddExtMorphFunc_AssetImport_Pmx_Late.py'
        late_file.write_text(template + '\n_register(' + repr({late_key: [duplicate['name'], duplicate['shapes'][0]]}) + ')\n', encoding='utf-8')
        assert 'get_assetimport_' + late_key not in extension_lib._userFuncs
        late_set = extension_lib.getMethod(other, 'set_assetimport_' + late_key)
        late_get = extension_lib.getMethod(other, 'get_assetimport_' + late_key)
        assert late_set and late_get, 'Late model must bind without restarting MMDD'
        late_set(37)
        assert all(value == 37 for value in late_get())
        assert [r.weights for r in renderers] == before, 'Late binding must not change first actor'
        controller.buildMorphIndex()
        assert len(controller.funcIndexBuffer) == len(data['morphs']), 'First actor remains bound after refresh'

        # Rebuilding the item hierarchy invalidates the cached renderer.
        for r in other.objctrl.objectItem.renderers:
            r.alive = False
        other.objctrl.objectItem.renderers = [Renderer(r, 12) for r in report['renderers']]
        set2(42)
        assert all(value == 42 for value in get2())
        other.objctrl.objectItem.renderers = []
        for _, cached in getattr(other, '_assetimport_morph_cache').items():
            for renderer, mesh, index in cached[1]:
                renderer.alive = False
        try:
            get2()
            raise AssertionError('Missing renderer must report a clear failure')
        except ValueError as e:
            assert 'expected at least one' in str(e)
    print('PASS: official ITEM bind, user FUNC discovery, %d morphs indexed/applied/reset, duplicate chunks, independent items and cache rebuild; engine doubles only.' % len(data['morphs']))


if __name__ == '__main__':
    main()
