#!/usr/bin/env python3
"""Render reference PNGs and an offline gallery from validated Assimp assjson scenes.

Requires Pillow and numpy. No game, browser, network, GPU, or plotting service.
Example:
  python3 scripts/testkit/render_references.py --scenes ../testkit-scenes

Scenes must have been exported after the same MakeLeftHanded/Triangulate steps
as the importer. This renderer applies node transforms and diffuse texture UVs.
It intentionally uses a neutral orthographic camera, simple lighting, and
opaque double-sided surfaces; it is a model reference, never a game screenshot.
"""
from __future__ import annotations
import argparse
import html
import json
import math
from pathlib import Path
import time

import numpy as np
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[2]
DISCLAIMER = '模型参考渲染，非游戏截图；只对比几何和纹理，游戏光照/位置可不同'
FONT_CANDIDATES = [
    '/System/Library/Fonts/PingFang.ttc',
    '/System/Library/Fonts/STHeiti Light.ttc',
    '/System/Library/Fonts/Hiragino Sans GB.ttc',
    '/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc',
    'C:/Windows/Fonts/msyh.ttc',
]

def unit(a):
    a = np.asarray(a, dtype=np.float64)
    return a / max(float(np.linalg.norm(a)), 1e-12)


def find_font(explicit):
    if explicit:
        if not explicit.is_file():
            raise FileNotFoundError(explicit)
        return explicit
    for name in FONT_CANDIDATES:
        if Path(name).is_file():
            return Path(name)
    raise RuntimeError('A Chinese font is required; specify --font /path/to/CJK-font.ttf')


def props(material):
    return material.get('properties', [])


def material_data(material, model_dir):
    color = None
    texture = None
    texture_ref = None
    unresolved = []
    transform = np.array([0., 0., 1., 1., 0.])
    for prop in props(material):
        if prop['key'] == '$clr.diffuse':
            color = np.array(prop['value'][:3], dtype=float)
        if prop['key'] == '$tex.uvtrafo' and prop.get('semantic') == 1:
            transform = np.asarray(prop['value'], dtype=float)
        if prop['key'] != '$tex.file' or prop.get('semantic') != 1 or texture is not None:
            continue
        ref = prop['value'].replace('\\', '/')
        texture_ref = ref
        path = model_dir / ref
        if not path.is_file():
            # Old sample files may contain a machine-specific original path;
            # allow the accompanying basename as the plugin's Common picker does.
            path = model_dir / Path(ref).name
        if path.is_file():
            texture = np.asarray(Image.open(path).convert('RGB'), dtype=np.float64) / 255.0
        else:
            unresolved.append(ref)
    if color is None:
        color = np.ones(3) if texture is not None else np.array([.62, .68, .74])
    return {'color': np.clip(color, 0, 1), 'texture': texture,
            'texture_ref': texture_ref, 'unresolved': unresolved, 'uv_transform': transform}


def scene_meshes(scene):
    """Return each mesh instance in scene coordinates, preserving node placement."""
    instances = []
    def visit(node, parent):
        local = np.asarray(node.get('transformation', np.eye(4).ravel()), dtype=float).reshape(4, 4)
        matrix = parent @ local
        for index in node.get('meshes', []):
            mesh = scene['meshes'][index]
            vertices = np.asarray(mesh['vertices'], dtype=float).reshape(-1, 3)
            positions = (np.c_[vertices, np.ones(len(vertices))] @ matrix.T)[:, :3]
            faces = []
            for face in mesh.get('faces', []):
                if len(face) >= 3:
                    faces.extend((face[0], face[i], face[i + 1]) for i in range(1, len(face) - 1))
            if not faces:
                continue
            normals = None
            if mesh.get('normals'):
                normals = np.asarray(mesh['normals'], dtype=float).reshape(-1, 3)
                normals = normals @ np.linalg.pinv(matrix[:3, :3])
                normals /= np.maximum(np.linalg.norm(normals, axis=1)[:, None], 1e-12)
            uv = None
            channels = mesh.get('texturecoords', [])
            if channels:
                values = np.asarray(channels[0], dtype=float)
                # assjson serializes only the declared UV components, usually 2.
                components = len(values) // len(vertices)
                if components >= 2:
                    uv = values.reshape(len(vertices), components)[:, :2]
            instances.append({'positions': positions, 'triangles': np.asarray(faces, dtype=np.int32),
                              'normals': normals, 'uv': uv, 'material': mesh.get('materialindex', 0)})
        for child in node.get('children', []):
            visit(child, matrix)
    visit(scene['rootnode'], np.eye(4))
    if not instances:
        raise ValueError('Scene has no drawable triangle mesh instances')
    return instances


def rasterize(instances, materials, width, height, sample_id):
    # Reference camera, not the game's camera. Positive z depth is toward viewer.
    direction = unit([4.4, 3.2, -6.2])
    if sample_id == 'ms3d':
        # The source spheres partly overlap; a front view reveals both silhouettes.
        direction = unit([0., .05, -1.])
    # Nearly planar samples deserve a face-on view instead of a near-edge view.
    if sample_id == 'smd':
        p = instances[0]['positions'][instances[0]['triangles'][0]]
        direction = unit(np.cross(p[1] - p[0], p[2] - p[0]))
        if direction[2] > 0:
            direction *= -1
    up = np.array([0., 1., 0.])
    if abs(np.dot(direction, up)) > .95:
        up = np.array([0., 0., 1.])
    right = unit(np.cross(direction, up))
    camera_up = unit(np.cross(right, direction))
    basis = np.column_stack([right, camera_up, direction])
    all_positions = np.concatenate([x['positions'] for x in instances])
    center = (all_positions.min(0) + all_positions.max(0)) * .5
    projected = (all_positions - center) @ basis
    lo, hi = projected[:, :2].min(0), projected[:, :2].max(0)
    extent = np.maximum(hi - lo, 1e-8)
    scale = min(width * .79 / extent[0], height * .80 / extent[1])
    midpoint = (lo + hi) * .5
    canvas = np.empty((height, width, 3), dtype=np.float64)
    # Subtle neutral background, no fabricated room or floor geometry.
    ygrad = np.linspace(0, 1, height)[:, None, None]
    canvas[:] = np.array([.967, .975, .983]) * (1-ygrad) + np.array([.897, .925, .952]) * ygrad
    depth = np.full((height, width), -np.inf)
    light = unit(direction + camera_up * .75 - right * .35)
    visible_pixels = 0
    for instance in instances:
        points = (instance['positions'] - center) @ basis
        screen = np.c_[(points[:, 0] - midpoint[0]) * scale + width / 2,
                       height / 2 - (points[:, 1] - midpoint[1]) * scale,
                       points[:, 2]]
        mat = materials[instance['material']]
        tex = mat['texture']
        uv = instance['uv']
        normals = instance['normals']
        for tri in instance['triangles']:
            p = screen[tri]
            minx, maxx = max(0, math.floor(p[:, 0].min())), min(width - 1, math.ceil(p[:, 0].max()))
            miny, maxy = max(0, math.floor(p[:, 1].min())), min(height - 1, math.ceil(p[:, 1].max()))
            if minx > maxx or miny > maxy:
                continue
            ax, ay = p[0, :2]; bx, by = p[1, :2]; cx, cy = p[2, :2]
            denominator = (by-cy)*(ax-cx)+(cx-bx)*(ay-cy)
            if abs(denominator) < 1e-10:
                continue
            xx, yy = np.meshgrid(np.arange(minx, maxx + 1) + .5, np.arange(miny, maxy + 1) + .5)
            w0 = ((by-cy)*(xx-cx)+(cx-bx)*(yy-cy))/denominator
            w1 = ((cy-ay)*(xx-cx)+(ax-cx)*(yy-cy))/denominator
            w2 = 1 - w0 - w1
            z = w0*p[0, 2]+w1*p[1, 2]+w2*p[2, 2]
            local_depth = depth[miny:maxy + 1, minx:maxx + 1]
            mask = (w0 >= -1e-7) & (w1 >= -1e-7) & (w2 >= -1e-7) & (z > local_depth)
            if not mask.any():
                continue
            # Use interpolated normals when supplied; otherwise flat face shading.
            if normals is None:
                q = instance['positions'][tri]
                n = unit(np.cross(q[1]-q[0], q[2]-q[0]))
                if np.dot(n, direction) < 0:
                    n *= -1
                shade = .48 + .52 * max(float(np.dot(n, light)), 0)
            else:
                n = w0[..., None]*normals[tri[0]]+w1[..., None]*normals[tri[1]]+w2[..., None]*normals[tri[2]]
                n /= np.maximum(np.linalg.norm(n, axis=2)[..., None], 1e-12)
                n *= np.where(n @ direction < 0, -1., 1.)[..., None]
                shade = (.48 + .52*np.maximum(n @ light, 0))[..., None]
            color = mat['color']
            if tex is not None and uv is not None:
                st = w0[..., None]*uv[tri[0]]+w1[..., None]*uv[tri[1]]+w2[..., None]*uv[tri[2]]
                tr = mat['uv_transform']
                if len(tr) >= 5:
                    st = st * tr[2:4]
                    angle = tr[4]
                    rot = np.array([[math.cos(angle), -math.sin(angle)], [math.sin(angle), math.cos(angle)]])
                    st = (st - .5) @ rot.T + .5 + tr[:2]
                u = np.mod(st[..., 0], 1)
                v = np.mod(st[..., 1], 1)
                tx = np.minimum((u*tex.shape[1]).astype(int), tex.shape[1]-1)
                ty = np.minimum(((1-v)*tex.shape[0]).astype(int), tex.shape[0]-1)
                color = tex[ty, tx] * color
            rgb = np.clip(color * shade, 0, 1)
            local_color = canvas[miny:maxy + 1, minx:maxx + 1]
            if np.ndim(rgb) == 1:
                local_color[mask] = rgb
            else:
                local_color[mask] = rgb[mask]
            local_depth[mask] = z[mask]
            visible_pixels += int(mask.sum())
    if not np.isfinite(depth).any():
        raise ValueError('No model pixels rendered')
    image = Image.fromarray(np.rint(canvas * 255).astype(np.uint8), 'RGB')
    return image, {'instances': len(instances), 'triangles': sum(len(m['triangles']) for m in instances),
                   'bounds_min': all_positions.min(0).tolist(), 'bounds_max': all_positions.max(0).tolist(),
                   'model_pixels': int(np.isfinite(depth).sum())}


def draw_wrapped(draw, xy, text, font, fill, maxwidth, spacing=5):
    x, y = xy
    line = ''
    for char in text:
        if char == '\n' or (line and draw.textlength(line + char, font=font) > maxwidth):
            draw.text((x, y), line, font=font, fill=fill)
            y += font.size + spacing
            line = '' if char == '\n' else char
        else:
            line += char
    if line:
        draw.text((x, y), line, font=font, fill=fill)
        y += font.size + spacing
    return y


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--scenes', type=Path, required=True, help='Directory containing <demo-id>.assjson')
    parser.add_argument('--manifest', type=Path, default=REPO/'.local/testkit/models.json')
    parser.add_argument('--output', type=Path, default=REPO/'.local/testkit/Reference')
    parser.add_argument('--font', type=Path, help='Chinese-capable .ttf/.ttc font; auto-detects common OS fonts')
    parser.add_argument('--width', type=int, default=800)
    parser.add_argument('--height', type=int, default=660)
    parser.add_argument('--only', nargs='*', help='Render selected ids, useful during editing; complete gallery still references all ids')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    fontfile = find_font(args.font)
    font = lambda size: ImageFont.truetype(str(fontfile), size)
    manifest = json.loads(args.manifest.read_text(encoding='utf-8'))
    demos = manifest['demos']
    version = manifest.get('plugin', '')
    summary = []
    if args.only and (args.output/'render-report.json').is_file():
        previous = json.loads((args.output/'render-report.json').read_text(encoding='utf-8'))
        summary = [item for item in previous.get('images', []) if item['id'] not in args.only]
    W, H = args.width, args.height
    for demo in demos:
        if args.only and demo['id'] not in args.only:
            continue
        started = time.perf_counter()
        scene = json.loads((args.scenes/(demo['id']+'.assjson')).read_text())
        entry = args.manifest.parent/demo['entry']
        mats = [material_data(m, entry.parent) for m in scene['materials']]
        for mat in mats:
            if mat['unresolved']:
                raise FileNotFoundError(f'{demo["id"]}: unresolved textures {mat["unresolved"]}')
        instances = scene_meshes(scene)
        # Render slightly larger, then downsample for cleaner silhouettes.
        view_height = H-188
        frame, stats = rasterize(instances, mats, W*3//2, view_height*3//2, demo['id'])
        frame = frame.resize((W, view_height), Image.Resampling.LANCZOS)
        card = Image.new('RGB', (W, H), '#ffffff')
        draw = ImageDraw.Draw(card)
        draw.text((25, 16), demo['label'], font=font(31), fill='#152a42')
        if demo['id'] == 'lws':
            draw.rounded_rectangle((192, 17, W-25, 54), radius=6, fill='#ffebe1', outline='#bf4f25', width=2)
            draw.text((205, 23), f'{version} 已知失败；目标参考', font=font(21), fill='#a33310')
        tag = '纹理与几何' if demo['texture_expected'] else '几何 / 纯色材质'
        draw.text((26, 59), f'{tag}  ·  {stats["triangles"]:,} 三角面  ·  {len(mats)} 材质', font=font(16), fill='#4b6076')
        card.paste(frame, (0, 94))
        draw = ImageDraw.Draw(card)
        draw.line((25, H-82, W-25, H-82), fill='#dce4eb', width=1)
        draw.text((25, H-74), '模型参考渲染，非游戏截图。', font=font(18), fill='#243e57')
        draw.text((25, H-49), '只对比几何和纹理，游戏光照 / 位置可不同。', font=font(16), fill='#506379')
        note = '本图双面显示；游戏中三角片背面可能不可见。' if demo['id']=='smd' else '中性光照 · 居中取景 · 双面显示'
        if demo['id'] == 'lws':
            note = f'目标外观：立方体；{version} 读取 LWS 关联几何失败。'
        draw.text((25, H-25), note, font=font(13), fill='#6c7e90')
        card.save(args.output/(demo['id']+'.png'))
        summary.append(dict(id=demo['id'], image=demo['id']+'.png', **stats,
                            textures=[m['texture_ref'] for m in mats if m['texture_ref']],
                            elapsed_seconds=round(time.perf_counter()-started, 3)))
        print(f'{demo["id"]}: {stats["triangles"]:,} triangles; {time.perf_counter()-started:.1f}s', flush=True)
    # An overview and the offline page are built only when all PNGs are available.
    missing = [d['id'] for d in demos if not (args.output/(d['id']+'.png')).is_file()]
    if missing:
        print('Gallery pending: missing '+', '.join(missing))
    else:
        cols, gap, tw, th = 4, 18, 360, 297
        rows = math.ceil(len(demos)/cols)
        overview = Image.new('RGB', (cols*tw+(cols+1)*gap, 124+rows*(th+gap)+25), '#eaf0f6')
        od = ImageDraw.Draw(overview)
        od.text((gap, 20), f'AssetImport {version} · {len(set(d["format"] for d in demos))} 格式 / {len(demos)} 个模型参考', font=font(33), fill='#132b42')
        od.text((gap, 69), DISCLAIMER, font=font(19), fill='#3f586e')
        for i, demo in enumerate(demos):
            tile = Image.open(args.output/(demo['id']+'.png')).resize((tw, th), Image.Resampling.LANCZOS)
            overview.paste(tile, (gap+(i%cols)*(tw+gap), 112+(i//cols)*(th+gap)))
        overview.save(args.output/'overview.png')
        gallery(demos, args.output)
    order = {demo['id']: index for index, demo in enumerate(demos)}
    summary = sorted((item for item in summary if item['id'] in order), key=lambda item: order[item['id']])
    report = {'renderer':'Pillow/numpy orthographic software rasterizer', 'disclaimer':DISCLAIMER,
              'assjson_source':'AssimpNet 5.0.0-beta1 + native Assimp v5.0.1; MakeLeftHanded/Triangulate',
              'limitations':['Not game screenshots or a Unity rendering test', 'Opaque, double-sided reference shading',
                             'Reference camera auto-centers each model; no source animation, source lights, skinning or game shader simulation'],
              'images':summary}
    (args.output/'render-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n')


def gallery(demos, output):
    cards=[]
    for demo in demos:
        esc=html.escape
        texture='应显示纹理' if demo['texture_expected'] else '无需贴图：检查几何或纯色材质'
        if demo['id'] == 'lws':
            texture = '已知失败 · 图片仅表示模型的目标外观'
        notes=demo.get('notes','')
        if demo['id']=='smd':
            notes += ' 参考图使用双面显示；游戏内单面三角片从背面可能不可见。'
        cards.append(f'''<article data-label="{esc(demo['label'].lower())}" data-texture="{str(demo['texture_expected']).lower()}">
<a href="{esc(demo['id'])}.png" target="_blank"><img src="{esc(demo['id'])}.png" alt="{esc(demo['label'])} 模型参考渲染" loading="lazy"></a>
<div class="body"><h2>{esc(demo['label'])}</h2><p class="tag">{texture}</p><p>{esc(demo['expected'])}</p><p class="notes">{esc(notes)}</p>
<p class="path">入口：<a href="../{esc(demo['entry'])}">{esc(demo['entry'])}</a></p></div></article>''')
    content='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>AssetImport 模型参考图</title><style>
:root{font-family:system-ui,-apple-system,"Microsoft YaHei",sans-serif;color:#19334c;background:#edf2f7}*{box-sizing:border-box}body{margin:0}header,main{max-width:1400px;margin:auto;padding:28px}header{padding-bottom:10px}h1{font-size:30px;margin:0 0 14px}header p{line-height:1.7;max-width:1000px;margin:8px 0}.notice{border-left:4px solid #327ca3;padding:10px 14px;background:#fff;border-radius:5px}.tools{display:flex;flex-wrap:wrap;gap:12px;margin-top:18px}input,select,a.button{border:1px solid #cad6e2;background:white;padding:10px 13px;border-radius:7px;font:inherit;color:inherit}input{flex:1;min-width:190px}a{color:#176482}a.button{text-decoration:none}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(310px,1fr));gap:24px}article{background:white;border:1px solid #dce5ed;border-radius:10px;overflow:hidden;box-shadow:0 4px 12px #23415c09}article img{display:block;width:100%;height:auto}.body{padding:18px 20px 22px}h2{margin:0;font-size:22px}.tag{font-size:13px;color:#167060}.body p{line-height:1.65;margin:10px 0}.notes{font-size:13px;color:#62788d}.path{font-size:12px;overflow-wrap:anywhere}footer{padding:24px 28px 40px;max-width:1400px;margin:auto;color:#62788d;font-size:13px;line-height:1.7}article[hidden]{display:none}</style>
<header><h1>AssetImport · 模型参考图</h1><p class="notice">模型参考渲染，非游戏截图；只对比几何和纹理，游戏光照/位置可不同。</p>
<p>19 个样例覆盖文件选择器中的 18 种格式，FBX 分别提供 ASCII 和二进制。图像由 Assimp 读取的模型数据生成，使用中性光照、自动居中和双面显示。点击图片查看原图；此页可直接离线打开。</p>
<p>STL、PLY 等没有贴图的样例出现白色或默认材质属正常，不能据此判断导入失败。参考图不验证动画、蒙皮、游戏着色器或卡片保存。</p>
<div class="tools"><input id="q" type="search" placeholder="查找格式，例如 FBX、IFC、OBJ" aria-label="查找格式"><select id="filter" aria-label="材质类型"><option value="all">全部样例</option><option value="true">应有纹理</option><option value="false">几何 / 纯色材质</option></select><a class="button" href="overview.png" target="_blank">打开总览图</a></div></header><main class="grid">
'''+''.join(cards)+'''</main><footer>软件参考渲染：Pillow / numpy；几何与材质来自 AssimpNet 5.0.0-beta1 + Assimp v5.0.1 读取结果。所有模型按各自尺寸自动缩放取景；图片之间的大小不代表实际单位或游戏导入比例。<br>源模型出处与许可证见测试包中的来源说明。渲染报告：<a href="render-report.json">render-report.json</a>。</footer>
<script>const q=document.querySelector('#q'),f=document.querySelector('#filter');function filter(){const text=q.value.trim().toLowerCase();document.querySelectorAll('article').forEach(a=>a.hidden=!(a.dataset.label.includes(text)&&(f.value==='all'||a.dataset.texture===f.value)))}q.addEventListener('input',filter);f.addEventListener('change',filter);</script></html>'''
    (output/'index.html').write_text(content,encoding='utf-8')

if __name__=='__main__':
    main()
