#!/usr/bin/env python3
"""Batch-check local PMX files with production C#, an independent reader and MMDD.

No private assets are bundled. Reports are offline evidence, not game validation.
Each run gets a new output directory so old successes cannot mask current failures.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import sys
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts/mmdd'))
from generate_pmx_mapping import create
from pmx_metadata import read_pmx


def run(command, log):
    env = dict(os.environ, PYTHONIOENCODING='utf-8')
    with log.open('w', encoding='utf-8') as output:
        result = subprocess.run([str(arg) for arg in command], cwd=ROOT, env=env,
                                stdout=output, stderr=subprocess.STDOUT, timeout=300)
    if result.returncode:
        raise RuntimeError('Exit %d; see %s' % (result.returncode, log))


def equivalent(left, right):
    if isinstance(left, dict) and isinstance(right, dict):
        return left.keys() == right.keys() and all(equivalent(value, right[key]) for key, value in left.items())
    if isinstance(left, list) and isinstance(right, list):
        return len(left) == len(right) and all(equivalent(a, b) for a, b in zip(left, right))
    if isinstance(left, (int, float)) and isinstance(right, (int, float)):
        # C# evaluates PMX float32 arithmetic before serializing JSON; Python uses doubles.
        return math.isclose(left, right, rel_tol=1e-6, abs_tol=1e-7)
    return left == right


def check_model(path, digest, folder, args):
    folder.mkdir()
    run([args.dotnet, args.test_dll, path, folder], folder / 'production.log')
    scene = json.loads((folder / 'pmx-validation.json').read_text(encoding='utf-8'))
    mapping_path, = folder.glob('item_AssetImport_PMX_*.json')
    mapping = json.loads(mapping_path.read_text(encoding='utf-8'))
    report = json.loads(mapping_path.with_suffix('.report.txt').read_text(encoding='utf-8'))
    model = read_pmx(path.read_bytes())
    expected, independent_report = create(model, path.stem, scene)
    # No filename-dependent expectations. Compare every supplied model with the
    # independent binary reader and exporter, including surviving operations.
    counts = {'vertices': model['vertices'], 'bones': len(model['bones']),
              'rigidBodies': len(model['rigid_bodies']), 'joints': len(model['joints'])}
    for key, value in counts.items():
        if scene[key] != value:
            raise AssertionError('Independent metadata mismatch: ' + key)
    if report['source_sha256'] != digest:
        raise AssertionError('Source hash mismatch')
    if not equivalent(mapping['bones'], expected['bones']):
        raise AssertionError('C#/Python bone mapping mismatch')
    # Morph IDs/order are local to each exporter. Match motion names and every
    # actual renderer binding instead, without assuming PMX order == mesh order.
    def morphs(data):
        return {m['vmdName']: {key: sorted(value) if key == 'funcList' else value
                              for key, value in m.items() if key != 'name'} for m in data['morphs']}
    if not equivalent(morphs(mapping), morphs(expected)):
        raise AssertionError('C#/Python morph mapping mismatch')
    for key in ('inactive_ik', 'disabled_constraints', 'excluded_from_mmdd'):
        def normalize(items):
            return sorted(items, key=lambda item: json.dumps(item, sort_keys=True))
        if not equivalent(normalize(report[key]), normalize(independent_report[key])):
            raise AssertionError('C#/Python diagnostic mismatch: ' + key)
    if args.mmdd_lib:
        common = ['--mapping', mapping_path, '--scene-report', folder / 'pmx-validation.json',
                  '--mmdd-lib', args.mmdd_lib]
        run([sys.executable, ROOT / 'scripts/mmdd/check_mapping.py', *common], folder / 'mmdd-validator.log')
        extension, = folder.glob('MmddExtMorphFunc_AssetImport_Pmx_*.py')
        run([sys.executable, ROOT / 'scripts/mmdd/check_item_morph_runtime.py', *common,
             '--extension', extension], folder / 'mmdd-runtime.log')
    return dict(counts, mapped_bones=report['mapped_bones'], ik_solvers=report['ik_solvers'],
                mapped_morphs=report['mapped_morphs'], inactive_ik=report['inactive_ik'],
                disabled_constraints=report['disabled_constraints'], morph_warnings=scene['morphWarnings'],
                missing_textures=scene['missingTextures'], official_mmdd='PASS (engine doubles)' if args.mmdd_lib else 'NOT RUN')


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model-root', type=Path, action='append', required=True,
                        help='Local directory (recursive) or PMX file; repeat to add more')
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--test-dll', type=Path, default=ROOT / 'tests/AssetImport.Pmx.Tests/bin/Release/net8.0/AssetImport.Pmx.Tests.dll')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/pmx-corpus')
    parser.add_argument('--mmdd-lib', type=Path, help='Optional official MMDD 3 Console/Lib directory')
    args = parser.parse_args()
    args.test_dll = args.test_dll.resolve()
    if args.mmdd_lib:
        args.mmdd_lib = args.mmdd_lib.resolve()
    paths = set()
    for source in args.model_root:
        if not source.exists():
            parser.error('Missing model root: ' + str(source))
        paths.update(path.resolve() for path in ([source] if source.is_file() else source.rglob('*'))
                     if path.is_file() and path.suffix.lower() == '.pmx')
    if not paths:
        parser.error('No PMX models found')
    unique = {}
    for path in sorted(paths):
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        unique.setdefault(digest, []).append(path)
    output = args.output.resolve() / datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    output.mkdir(parents=True)
    results = []
    for digest, copies in unique.items():
        path = copies[0]
        record = {'source': str(path), 'sha256': digest, 'copies': [str(p) for p in copies],
                  'windows_game': 'NOT RUN'}
        try:
            record.update(check_model(path, digest, output / digest[:16], args), status='PASS')
        except (OSError, ValueError, KeyError, RuntimeError, AssertionError, subprocess.TimeoutExpired) as error:
            record.update(status='FAIL', error=str(error))
        results.append(record)
        (output / 'summary.json').write_text(json.dumps(results, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print('%s: %s%s' % (record['status'], path, ': ' + record['error'] if 'error' in record else ''), flush=True)
    failed = sum(result['status'] != 'PASS' for result in results)
    print('%d/%d passed; %s' % (len(results) - failed, len(results), output / 'summary.json'))
    return int(failed > 0)


if __name__ == '__main__':
    sys.exit(main())
