#!/usr/bin/env python3
"""Read PMX 2.0 skeleton/physics metadata without importing model geometry.

This is an offline mapping helper, not a PMX importer for the game. No model
content is included in this source. PMX 2.1 soft bodies are deliberately rejected.
"""
import struct
from collections import Counter


class Reader:
    def __init__(self, data):
        self.data = data
        self.pos = 0

    def take(self, size):
        if size < 0 or size > len(self.data) - self.pos:
            raise ValueError('Truncated PMX at byte %d' % self.pos)
        result = self.data[self.pos:self.pos + size]
        self.pos += size
        return result

    def unpack(self, fmt):
        return struct.unpack('<' + fmt, self.take(struct.calcsize('<' + fmt)))

    def one(self, fmt):
        return self.unpack(fmt)[0]

    def count(self):
        n = self.one('i')
        if n < 0 or n > len(self.data):
            raise ValueError('Invalid PMX count')
        return n

    def text(self):
        return self.take(self.count()).decode(self.encoding)

    def index(self, kind):
        size = self.sizes[kind]
        return self.one({1: 'b', 2: 'h', 4: 'i'}[size])


def read_pmx(data):
    r = Reader(data)
    if r.take(4) != b'PMX ':
        raise ValueError('Not a PMX file')
    version = r.one('f')
    if abs(version - 2.0) > .001:
        raise ValueError('This helper currently supports PMX 2.0 only')
    header = r.take(r.one('B'))
    if len(header) != 8 or header[0] not in (0, 1) or header[1] > 4:
        raise ValueError('Unsupported PMX header')
    r.encoding = 'utf-16-le' if header[0] == 0 else 'utf-8'
    r.sizes = dict(zip(('vertex', 'texture', 'material', 'bone', 'morph', 'rigid'), header[2:]))
    if any(size not in (1, 2, 4) for size in r.sizes.values()):
        raise ValueError('Unsupported PMX index size')
    model = dict(zip(('name', 'english_name', 'comment', 'english_comment'), (r.text() for _ in range(4))))
    model['version'] = round(version, 1)
    model['vertices'] = r.count()
    weighted = Counter()
    deform = Counter()
    for _ in range(model['vertices']):
        r.take(32 + 16 * header[1])  # position, normal, UV, additional UV
        mode = r.one('B')
        deform[mode] += 1
        if mode == 0:
            indices, weights = [r.index('bone')], [1.0]
        elif mode in (1, 3):
            indices = [r.index('bone'), r.index('bone')]
            w = r.one('f')
            weights = [w, 1 - w]
            if mode == 3:
                r.take(36)  # SDEF C/R0/R1, not reproduced by this tool
        elif mode == 2:
            indices = [r.index('bone') for _ in range(4)]
            weights = r.unpack('4f')
        else:
            raise ValueError('Unsupported PMX skinning mode %d' % mode)
        for idx, w in zip(indices, weights):
            if w > 0:
                weighted[idx] += 1
        r.take(4)  # edge scale
    model['skinning_modes'] = dict(deform)
    model['indices'] = r.count()
    if model['indices'] % 3:
        raise ValueError('Non-triangle PMX face data')
    r.take(model['indices'] * r.sizes['vertex'])
    model['textures'] = [r.text() for _ in range(r.count())]
    materials = []
    for _ in range(r.count()):
        name, english = r.text(), r.text()
        r.take(65)  # colors, power, flags, edge
        tex, sphere = r.index('texture'), r.index('texture')
        sphere_mode, common_toon = r.unpack('2B')
        toon = r.one('B') if common_toon else r.index('texture')
        memo, count = r.text(), r.count()
        materials.append({'name': name, 'texture': tex, 'indices': count})
    model['materials'] = materials
    bones = []
    for i in range(r.count()):
        b = {'index': i, 'name': r.text(), 'english_name': r.text(),
             'position': list(r.unpack('3f')), 'parent': r.index('bone'),
             'layer': r.one('i'), 'flags': r.one('H'), 'weighted_vertices': weighted[i]}
        flags = b['flags']
        if flags & 1:
            b['tail_index'] = r.index('bone')
        else:
            b['tail_offset'] = list(r.unpack('3f'))
        if flags & 0x300:
            b['grant'] = {'parent': r.index('bone'), 'weight': r.one('f'),
                          'rotation': bool(flags & 0x100), 'position': bool(flags & 0x200),
                          'local': bool(flags & 0x80)}
        if flags & 0x400:
            b['fixed_axis'] = list(r.unpack('3f'))
        if flags & 0x800:
            b['local_x'] = list(r.unpack('3f'))
            b['local_z'] = list(r.unpack('3f'))
        if flags & 0x2000:
            b['external_parent'] = r.one('i')
        if flags & 0x20:
            ik = {'target': r.index('bone'), 'iterations': r.one('i'),
                  'angle': r.one('f'), 'links': []}
            for _ in range(r.count()):
                link = {'bone': r.index('bone')}
                if r.one('B'):
                    link['min'] = list(r.unpack('3f'))
                    link['max'] = list(r.unpack('3f'))
                ik['links'].append(link)
            b['ik'] = ik
        bones.append(b)
    if any(i < 0 or i >= len(bones) for i in weighted):
        raise ValueError('Invalid weighted bone index')
    model['bones'] = bones
    morphs = []
    offset_sizes = {0: r.sizes['morph'] + 4, 1: r.sizes['vertex'] + 12,
                    2: r.sizes['bone'] + 28, 8: r.sizes['material'] + 113}
    offset_sizes.update({i: r.sizes['vertex'] + 16 for i in range(3, 8)})
    for _ in range(r.count()):
        name, english = r.text(), r.text()
        panel, mode = r.unpack('2B')
        count = r.count()
        if mode not in offset_sizes:
            raise ValueError('Unsupported PMX morph mode %d' % mode)
        r.take(count * offset_sizes[mode])
        morphs.append({'name': name, 'mode': mode, 'offsets': count})
    model['morphs'] = morphs
    for _ in range(r.count()):
        r.text(); r.text(); r.take(1)
        for _ in range(r.count()):
            kind = r.one('B')
            if kind not in (0, 1):
                raise ValueError('Invalid display-frame element')
            r.index('bone' if kind == 0 else 'morph')
    rigid = []
    for i in range(r.count()):
        b = {'index': i, 'name': r.text(), 'english_name': r.text(), 'bone': r.index('bone'),
             'group': r.one('B'), 'mask': r.one('H'), 'shape': r.one('B'),
             'size': list(r.unpack('3f')), 'position': list(r.unpack('3f')),
             'rotation': list(r.unpack('3f')), 'mass': r.one('f'),
             'linear_damping': r.one('f'), 'angular_damping': r.one('f'),
             'restitution': r.one('f'), 'friction': r.one('f'), 'mode': r.one('B')}
        rigid.append(b)
    model['rigid_bodies'] = rigid
    joints = []
    for i in range(r.count()):
        j = {'name': r.text(), 'english_name': r.text(), 'type': r.one('B'),
             'a': r.index('rigid'), 'b': r.index('rigid')}
        if j['type'] != 0:
            raise ValueError('Unsupported PMX joint type')
        for key in ('position', 'rotation', 'move_min', 'move_max', 'rot_min', 'rot_max', 'move_spring', 'rot_spring'):
            j[key] = list(r.unpack('3f'))
        joints.append(j)
    model['joints'] = joints
    if r.pos != len(data):
        raise ValueError('Unexpected trailing PMX data: %d bytes' % (len(data) - r.pos))
    return model
