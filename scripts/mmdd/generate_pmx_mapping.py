#!/usr/bin/env python3
"""Build an MMDD 3.0 ITEM mapping for AssetImport 4.2 direct PMX import.

Input is a local PMX, never bundled. Handles non-local rotation/position grants and per-axis IK
through the AssetImport runtime extension. Physics descendants are left to Dynamic Bone. Generic
local grants / external-parent transforms need a dedicated implementation.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
from pmx_metadata import read_pmx


def target(b):
    return 'PMX_%04d_%s' % (b['index'], b['name'])


def morph_target_key(renderer, shape):
    return 'v2_' + hashlib.sha256((renderer + '\0' + shape).encode('utf-8')).hexdigest()[:16]


def morph_targets(mapping, renderer_report):
    targets = {}
    names = {m['vmdName'] for m in mapping['morphs']}
    for renderer in renderer_report['renderers']:
        for shape in renderer['shapes']:
            if shape in names:
                targets[morph_target_key(renderer['name'], shape)] = [renderer['name'], shape]
    return targets


def inactive_ik_reason(bone):
    ik = bone.get('ik')
    if ik is None:
        return None
    if ik['iterations'] == 0:
        return 'zero iterations'
    if ik['angle'] == 0:
        return 'zero step angle'
    if not ik['links']:
        return 'empty chain'
    return None


def has_active_ik(bone):
    return 'ik' in bone and inactive_ik_reason(bone) is None


def animation_plan(bones):
    by_id = {b['index']: b for b in bones}
    grants = {i for i, b in by_id.items() if b.get('grant', {}).get('parent', -1) >= 0
              and b.get('grant', {}).get('weight', 0) != 0}
    iks = {i for i, b in by_id.items() if has_active_ik(b)}
    edges = {i: {b['parent']} if b['parent'] in by_id else set() for i, b in by_id.items()}
    reverse = {i: set() for i in by_id}
    for i, b in by_id.items():
        if i in grants:
            edges[i].add(b['grant']['parent'])
        if i in iks:
            edges[i] |= {b['ik']['target']} | {link['bone'] for link in b['ik']['links']}
        edges[i] &= by_id.keys()
        for dependency in edges[i]:
            reverse[dependency].add(i)
    visited, order = set(), []
    for root in by_id:
        stack = [(root, False)]
        while stack:
            node, finished = stack.pop()
            if finished:
                order.append(node)
            elif node not in visited:
                visited.add(node)
                stack.append((node, True))
                stack.extend((child, False) for child in edges[node] if child not in visited)
    component = {}
    for root in reversed(order):
        if root in component:
            continue
        stack = [root]
        component[root] = root
        while stack:
            for parent in reverse[stack.pop()]:
                if parent not in component:
                    component[parent] = root
                    stack.append(parent)
    disabled = []
    for i, b in by_id.items():
        operations = [('append', grants, [b['grant']['parent']])] if i in grants else []
        if i in iks:
            operations.append(('IK', iks, [b['ik']['target']] + [link['bone'] for link in b['ik']['links']]))
        for operation, enabled, dependencies in operations:
            if any(dependency in component and component[i] == component[dependency] for dependency in dependencies):
                enabled.remove(i)
                disabled.append({'index': i, 'name': b['name'], 'operation': operation,
                                 'reason': 'cyclic ' + operation + ' dependency'})
    return grants, iks, disabled


def create(model, label, renderer_report=None, dynamic_roots=()):
    bones = model['bones']
    by_id = {b['index']: b for b in bones}
    active_grants, active_ik, disabled_constraints = animation_plan(bones)
    physics = {r['bone'] for r in model['rigid_bodies'] if r['mode'] > 0 and r['bone'] >= 0}
    excluded = set(physics) | set(dynamic_roots)
    while True:
        more = {b['index'] for b in bones if b['parent'] in excluded
                or (b['index'] in active_grants and b['grant']['parent'] in excluded)
                or (b['index'] in active_ik and ({b['ik']['target']} | {l['bone'] for l in b['ik']['links']}) & excluded)}
        if more.issubset(excluded):
            break
        excluded |= more
    included = set(by_id) - excluded
    dependencies = {}
    warnings = ['Ignored append flag without a source bone: ' + b['name'] for b in bones
                if 'grant' in b and b['grant']['parent'] == -1 and b['index'] in included]
    warnings += ['Physics append follower excluded from MMDD: ' + b['name'] for b in bones
                 if b['index'] in active_grants and b['grant']['parent'] in excluded]
    for i in included:
        b = by_id[i]
        dep = {b['parent']} if b['parent'] >= 0 else set()
        if i in active_grants:
            g = b['grant']
            if g['local']:
                raise ValueError('Unsupported local grant: ' + b['name'])
            dep.add(g['parent'])
        if i in active_ik:
            ik = b['ik']
            if ik['target'] < 0 or not 1 <= ik['iterations'] <= 10000 or not math.isfinite(ik['angle']) or ik['angle'] <= 0:
                raise ValueError('Invalid/unsupported active PMX IK: ' + target(b))
            dep |= {b['ik']['target']} | {l['bone'] for l in b['ik']['links']}
        if not dep.issubset(included):
            raise ValueError('Animation depends on an excluded physics bone: ' + b['name'])
        dependencies[i] = dep
    disabled_constraints = [item for item in disabled_constraints if item['index'] in included]
    warnings += ['Disabled cyclic ' + item['operation'] + '; bone retained: ' + target(by_id[item['index']])
                 for item in disabled_constraints]
    ordered = []
    while len(ordered) != len(included):
        ready = sorted((i for i in included - set(ordered) if dependencies[i].issubset(ordered)), key=lambda i: (by_id[i]['layer'], i))
        if not ready:raise ValueError('Cyclic animation dependencies')
        # Preserve PMX order among ready operations, especially IK before D-bone grants.
        ordered.append(ready[0])
    def key(i):return 'assetimport_pmx_%04d' % i
    inactive_ik = [{'index': i, 'name': by_id[i]['name'], 'reason': inactive_ik_reason(by_id[i]),
                    'iterations': by_id[i]['ik']['iterations'], 'step_angle_radians': by_id[i]['ik']['angle'],
                    'link_count': len(by_id[i]['ik']['links'])}
                   for i in ordered if inactive_ik_reason(by_id[i]) is not None]
    warnings += ['Inactive IK solver omitted; bone retained: ' + target(by_id[item['index']]) +
                 ' (' + item['reason'] + ').' for item in inactive_ik]
    definitions = []
    ik_count = 0
    for i in ordered:
        b = by_id[i]
        name = b['name']
        d = {'name': key(i), 'vmdName': name, 'group': 'PMX bones',
             'positionData': {'mode': 'LOCATE_AT_TARGET', 'target': target(b)},
             'movable': bool(b['flags'] & 4), 'rotatable': bool(b['flags'] & 2),
             'weightBone': target(b), 'weightSyncMode': 'GLOBAL',
             'adjustment': {'rotAdjAuto': False, 'rotAxisAuto': False}}
        if b['parent'] >= 0:d['parent'] = key(b['parent'])
        if i in active_grants:
            g = b['grant']
            mode = ('r' if g['rotation'] else '') + ('p' if g['position'] else '')
            d.update(isTrack=True, trackData=[mode, g['weight'], key(g['parent'])])
            if g['rotation']: d['rotatable'] = True
            if g['position']: d['movable'] = True
        if 'fixed_axis' in b:
            axis = b['fixed_axis']
            # Both vectors are in the converted (-X,Y,-Z) coordinate system.
            axis = [-axis[0], axis[1], -axis[2]]
            d.update(isLimitAxis=True, limitAxisData=[axis, axis])
        if i in active_ik:
            ik = b['ik']
            # The AssetImport runtime extension applies per-axis limits to
            # these explicitly namespaced bones; stock MMDD only limits knees.
            for link in ik['links']:
                if 'min' in link and any(a > b for a, b in zip(link['min'], link['max'])):
                    raise ValueError('Inverted PMX IK limits: ' + name)
            limits = []
            for link in ik['links']:
                if 'min' not in link:limits.append(None)
                else:
                    limits.append([sorted([math.degrees(sign*link['min'][axis]), math.degrees(sign*link['max'][axis])])
                                   for axis, sign in enumerate((-1, 1, -1))])
            d.update(isIK=True, ikData=[key(ik['target'])] + [key(l['bone']) for l in ik['links']],
                     ikSolverSetting={'name': key(i), 'enable': True, 'solver': 'MMDIKSolver',
                                      'iterations': ik['iterations'], 'stepAngle': math.degrees(ik['angle']), 'limits': limits})
            ik_count += 1
        definitions.append(d)
    morphs = []
    if renderer_report:
        for idx, morph in enumerate(model['morphs']):
            entries = [[name, morph['name'], 0, 100] for name in dict.fromkeys(r['name'] for r in renderer_report['renderers'] if morph['name'] in r['shapes'])]
            if not entries:continue
            funcs = []
            for renderer, shape, low, high in entries:
                key = morph_target_key(renderer, shape)
                funcs.append(['get_assetimport_' + key, 'set_assetimport_' + key, 'range', low, high])
            # MMDD 3 rejects BLEND on ITEM at bindChara even if KKPE is present.
            # Its user FUNC extension API works with props and restores originals.
            morphs.append({'name': 'morph_%04d' % idx, 'vmdName': morph['name'], 'group': 'PMX vertex morphs',
                           'enable': True, 'type': 'FUNC', 'funcList': funcs})
    result = {'general': {'description': label + ' / AssetImport PMX offline mapping / scale 1',
                          'boneType': 'ITEM', 'enableAutoRotAdj': False, 'overwriteDefaultBlend': False},
              'bones': definitions, 'morphs': morphs}
    dynamic_roots = [b for b in bones if b['index'] in physics and b['parent'] not in physics]
    report = {'label': label, 'mapped_bones': len(definitions),
              'rotation_grants': sum(d.get('isTrack', False) and 'r' in d['trackData'][0] for d in definitions),
              'position_grants': sum(d.get('isTrack', False) and 'p' in d['trackData'][0] for d in definitions),
              'ik_solvers': ik_count, 'inactive_ik': inactive_ik, 'disabled_constraints': disabled_constraints, 'mapped_morphs': len(morphs),
              'excluded_from_mmdd': [target(by_id[i]) for i in sorted(excluded)],
              'physics_chain_roots': [target(b) for b in dynamic_roots],
              'unit_scale': .0832, 'import_scale_required': 1, 'mmdd_axis_scale_required': .0832,
              'warnings': warnings + ['Requires the AssetImport MMDD runtime extension for append transforms, unkeyed IK and per-axis IK limits',
                           'PMX rigid bodies/joints not simulated']}
    return result, report


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('pmx', type=Path)
    p.add_argument('--label', required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--renderer-report', type=Path)
    p.add_argument('--morph-extension-dir', type=Path, help='MMDD Lib directory (parent of mmdd); defaults beside mapping')
    a = p.parse_args()
    data = a.pmx.read_bytes()
    m = read_pmx(data)
    renderers = json.loads(a.renderer_report.read_text()) if a.renderer_report else None
    mapping, report = create(m, a.label, renderers)
    report['source_sha256'] = hashlib.sha256(data).hexdigest()
    a.output.parent.mkdir(parents=True, exist_ok=True)
    if mapping['morphs'] or report['ik_solvers'] or report['position_grants'] or report['rotation_grants']:
        ext_dir = a.morph_extension_dir or a.output.parent
        ext_dir.mkdir(parents=True, exist_ok=True)
        ext_name = 'MmddExtMorphFunc_AssetImport_Pmx_' + report['source_sha256'][:12] + '.py'
        targets = morph_targets(mapping, renderers) if renderers else {}
        template = Path(__file__).with_name('item_morph_functions.py').read_text(encoding='utf-8')
        (ext_dir / ext_name).write_text(template + '\n\n_register(' + json.dumps(targets, ensure_ascii=True, indent=2) + ')\n', encoding='utf-8')
        report['morph_extension'] = ext_name
        report['morph_targets'] = targets
    # MMDD scans every .json in this directory as a mapping definition.
    # Keep diagnostic metadata out of that scan.
    for path, obj in ((a.output, mapping), (a.output.with_suffix('.report.txt'), report)):
        path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k:report[k] for k in ('mapped_bones','rotation_grants','ik_solvers','mapped_morphs')},ensure_ascii=False))
