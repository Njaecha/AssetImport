"""Offline exporter dependency regressions, independent of private PMX files."""
import random
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'scripts/mmdd'))
from generate_pmx_mapping import create


class DependencyTests(unittest.TestCase):
    def test_cyclic_constraints_against_independent_reachability_oracle(self):
        for seed in range(48):
            with self.subTest(seed=seed):
                rng = random.Random(seed)
                count = 18
                order = list(range(count))
                rng.shuffle(order)
                index = {old: new for new, old in enumerate(order)}
                bones = []
                edges = [[False] * count for _ in range(count)]
                operations = []
                for i in range(count):
                    parent = rng.randrange(i) if i else -1
                    bone = {'index': index[i], 'name': 'cycle_%d_%d' % (seed, i),
                            'parent': index[parent] if parent >= 0 else -1, 'flags': 6, 'layer': 0}
                    if parent >= 0:
                        edges[i][parent] = True
                    if rng.randrange(2):
                        source = rng.randrange(count)
                        bone['grant'] = {'parent': index[source], 'weight': .5,
                                         'rotation': True, 'position': False, 'local': False}
                        edges[i][source] = True
                        operations.append((i, 'append', [source]))
                    if rng.randrange(3) == 0:
                        target, link = rng.randrange(count), rng.randrange(count)
                        bone['ik'] = {'target': index[target], 'iterations': 4, 'angle': 1,
                                      'links': [{'bone': index[link]}]}
                        edges[i][target] = edges[i][link] = True
                        operations.append((i, 'IK', [target, link]))
                    bones.append(bone)
                # Transitive closure is independent of the exporter's SCC algorithm.
                for k in range(count):
                    for i in range(count):
                        for j in range(count):
                            edges[i][j] |= edges[i][k] and edges[k][j]
                expected = {(index[i], kind) for i, kind, dependencies in operations
                            if any(dependency == i or edges[dependency][i] for dependency in dependencies)}
                mapping, report = create({'bones': [bones[i] for i in order], 'morphs': [],
                                          'rigid_bodies': []}, 'generated cyclic constraints')
                self.assertEqual(len(mapping['bones']), count)
                self.assertEqual({(item['index'], item['operation']) for item in report['disabled_constraints']}, expected)
                by_name = {bone['vmdName']: bone for bone in mapping['bones']}
                for i, kind, _ in operations:
                    self.assertEqual(by_name[bones[i]['name']].get('isTrack' if kind == 'append' else 'isIK', False),
                                     (index[i], kind) not in expected)

    def test_inactive_ik_preserves_bones_and_ignores_solver_dependencies(self):
        for seed in range(24):
            for reason in ('angle', 'iterations', 'links'):
                for physics in (False, True):
                    with self.subTest(seed=seed, reason=reason, physics=physics):
                        order = list(range(7))
                        random.Random(seed).shuffle(order)
                        index = {old: new for new, old in enumerate(order)}
                        bones = [{'index': index[i], 'name': 'inactive_%d_%d' % (seed, i),
                                  'parent': -1 if i == 0 else index[0], 'flags': 6, 'layer': 0}
                                 for i in range(7)]
                        bones[2]['parent'] = index[1]  # Would falsely cycle if inactive IK had dependencies.
                        for i, target_bone, link in ((1, 2, 3), (4, 6, 5)):
                            bones[i]['ik'] = {'target': index[target_bone], 'iterations': 4,
                                              'angle': 1, 'links': [{'bone': index[link]}]}
                        bones[1]['ik'][reason] = [] if reason == 'links' else 0
                        model = {'bones': [bones[i] for i in order], 'morphs': [],
                                 'rigid_bodies': [{'mode': 1, 'bone': index[3]}] if physics else []}
                        mapping, report = create(model, 'synthetic no-op IK')
                        helper = next(b for b in mapping['bones'] if b['vmdName'] == bones[1]['name'])
                        self.assertNotIn('isIK', helper)
                        self.assertTrue(helper['movable'] and helper['rotatable'])
                        self.assertEqual(report['ik_solvers'], 1)
                        self.assertEqual(len(mapping['bones']), 6 if physics else 7)
                        self.assertEqual(len(report['inactive_ik']), 1)
                        self.assertEqual(report['inactive_ik'][0]['index'], index[1])

    def test_nonlocal_position_and_combined_grants(self):
        for rotation in (False, True):
            for weight in (-.25, .2, 1.5):
                model = {'bones': [
                    {'index': 0, 'name': 'source', 'parent': -1, 'flags': 6, 'layer': 0},
                    {'index': 1, 'name': 'follower', 'parent': -1, 'flags': 0, 'layer': 0,
                     'grant': {'parent': 0, 'weight': weight, 'position': True, 'rotation': rotation, 'local': False}}
                ], 'morphs': [], 'rigid_bodies': []}
                mapping, report = create(model, 'generated position grant')
                follower = mapping['bones'][1]
                self.assertEqual(follower['trackData'], ['rp' if rotation else 'p', weight, 'assetimport_pmx_0000'])
                self.assertTrue(follower['movable'])
                self.assertEqual(follower['rotatable'], rotation)
                self.assertEqual(report['position_grants'], 1)
                self.assertEqual(report['rotation_grants'], int(rotation))
                model['bones'][1]['grant']['local'] = True
                with self.assertRaisesRegex(ValueError, 'Unsupported local grant'):
                    create(model, 'local semantics cannot use position-only fix')

    def test_transitive_physics_ik_append_closure(self):
        for seed in range(24):
            for selected in (False, True):
                for target_dependency in (False, True):
                    with self.subTest(seed=seed, selected=selected, target=target_dependency):
                        order = list(range(10))
                        rng = random.Random(seed)
                        rng.shuffle(order)
                        index = {old: new for new, old in enumerate(order)}
                        bones = [{'index': index[i], 'name': 'generated_%d_%d' % (seed, i),
                                  'parent': -1 if i == 0 else index[0], 'flags': 6,
                                  'layer': rng.randrange(4)} for i in range(10)]
                        bones[2]['parent'] = index[1]
                        for i, dependency in ((3, 2), (6, 5)):
                            bones[i]['ik'] = {'target': index[dependency if target_dependency else 8],
                                             'links': [{'bone': index[8 if target_dependency else dependency]}],
                                             'iterations': 4, 'angle': 1}
                        bones[4]['parent'] = index[3]
                        for i, source in ((5, 4), (7, 6)):
                            bones[i]['grant'] = {'parent': index[source], 'weight': .5,
                                                 'position': True, 'local': True}
                        bones[9]['grant'] = {'parent': index[7], 'weight': 0, 'position': False, 'local': False}
                        model = {'bones': [bones[i] for i in order], 'morphs': [],
                                 'rigid_bodies': [] if selected else [{'mode': 1, 'bone': index[1]}]}
                        kwargs = {'dynamic_roots': [index[1]]} if selected else {}
                        mapping, report = create(model, 'generated graph', **kwargs)
                        self.assertEqual({b['vmdName'] for b in mapping['bones']},
                                         {bones[i]['name'] for i in (0, 8, 9)})
                        self.assertEqual(report['ik_solvers'], 0)
                        self.assertEqual(sum('Physics append follower' in w for w in report['warnings']), 2)


if __name__ == '__main__':
    unittest.main()
