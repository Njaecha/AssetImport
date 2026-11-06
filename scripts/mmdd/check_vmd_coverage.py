#!/usr/bin/env python3
"""Parse a VMD with MMDD 3's real loader and report name coverage for PMX maps.

This checks data compatibility, not game playback or deformation. Input model
and motion assets stay outside the repository and release packages.
"""
import argparse
import ast
import json
import math
from pathlib import Path
import struct
import sys
import traceback


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--mmdd-lib', type=Path, required=True)
    p.add_argument('--vmd', type=Path, required=True)
    p.add_argument('--mapping', type=Path, action='append', required=True)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'testkit'))
    from check_motion_probe import Vector3, Quaternion, Py2File, install_data_doubles
    install_data_doubles()
    source = a.mmdd_lib / 'vmdlib.py'
    tree = ast.parse(source.read_text(encoding='utf-8-sig'))
    names = ('VmdLib', 'VmdBoneKeyFrame', 'VmdMorphKeyFrame', 'VmdCameraKeyFrame', 'VmdIkVisibleKeyFrame')
    classes = [n for n in tree.body if isinstance(n, ast.ClassDef) and n.name in names]
    loader = next(n for n in classes if n.name == 'VmdLib')
    loader.body = [n for n in loader.body if isinstance(n, ast.FunctionDef) and n.name == 'loadVmd']
    env = dict(Vector3=Vector3, Quaternion=Quaternion, struct=struct, math=math, traceback=traceback, open=Py2File)
    exec(compile(ast.Module(body=classes, type_ignores=[]), str(source), 'exec'), env)
    parsed = env['VmdLib'].loadVmd(str(a.vmd))
    assert len(parsed) == 5, parsed
    bones, morphs = parsed[1:3]
    # Independent fixed-record reader verifies the upstream parser's track names.
    data = a.vmd.read_bytes()
    pos = 50 if data[:30].startswith(b'Vocaloid Motion Data 0002') else 40
    independently_read = []
    for size, actual in ((111, bones), (23, morphs)):
        count = struct.unpack_from('<I', data, pos)[0]
        pos += 4
        assert count == len(actual)
        tracks = {data[pos + i * size:pos + i * size + 15].split(b'\0')[0].decode('shift_jis') for i in range(count)}
        assert tracks == {k.name for k in actual}
        independently_read.append(tracks)
        pos += count * size
    motion_names, morph_names = independently_read
    animated_bones = {k.name for k in bones if any(abs(v) > 1e-6 for v in
        (k.position.x, k.position.y, k.position.z, k.rotation.x, k.rotation.y, k.rotation.z))}
    animated_morphs = {k.name for k in morphs if abs(k.weight) > 1e-6}
    core = {'センター', '上半身', '下半身', '首', '頭', '両目'}
    core |= {side + part for side in ('左', '右') for part in ('肩', '腕', 'ひじ', '手首', '足', 'ひざ', '足首', '足ＩＫ', 'つま先ＩＫ')}
    result = dict(bone_keys=len(bones), morph_keys=len(morphs), bone_tracks=len(motion_names),
                  morph_tracks=len(morph_names), last_frame=max(k.frameNo for k in bones),
                  validation='Official MMDD 3 parser + independent record/name check; no Unity playback', models=[])
    for file in a.mapping:
        mapping = json.loads(file.read_text())
        report = json.loads(file.with_suffix('.report.txt').read_text())
        mb = {b['vmdName'] for b in mapping['bones']}
        mm = {m['vmdName'] for m in mapping['morphs']}
        coverage = dict(description=mapping['general']['description'],
                        mapped_bone_tracks=sorted(motion_names & mb),
                        unmapped_bone_tracks=sorted(motion_names - mb),
                        mapped_morph_tracks=sorted(morph_names & mm),
                        unmapped_morph_tracks=sorted(morph_names - mm),
                        animated_unmapped_bones=sorted(animated_bones - mb),
                        animated_unmapped_morphs=sorted(animated_morphs - mm),
                        core_motion_tracks_missing=sorted((core & motion_names) - mb),
                        unsupported_or_disabled_model_morphs=report.get('unmapped_morphs', []))
        result['models'].append(coverage)
        print('%s: bones %d/%d, morphs %d/%d' % (file.name, len(motion_names & mb), len(motion_names), len(morph_names & mm), len(morph_names)))
        print('Core motion tracks missing:', coverage['core_motion_tracks_missing'])
        print('Unmapped tracks with nonzero motion: %d bones, %d morphs; full names in output report.' %
              (len(coverage['animated_unmapped_bones']), len(coverage['animated_unmapped_morphs'])))
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print('PASS: %d bone keys, %d morph keys, frame %d; offline parsing only.' % (len(bones), len(morphs), result['last_frame']))


if __name__ == '__main__':
    main()
