#!/usr/bin/env python3
"""Build an original skinned FBX / VMD / MMDD 3.0 ITEM compatibility probe.

No MMDD, VNGE, game binaries or third-party models are redistributed.
The FBX is converted from an original glTF using Assimp 5.0.1, not Pmx2Fbx.
"""
import argparse
import base64
import hashlib
import json
import math
from pathlib import Path
import shutil
import struct
import subprocess
import zipfile
import zlib


BONES = [
    ("probe_root", "全ての親", "Bone 全ての親", None, (0, 0, 0)),
    ("probe_pivot", "振り子", "Bone 振り子", 0, (0, 1.6, 0)),
    ("probe_tip", "先端", "Bone 先端", 1, (0, .8, 0)),
]
FRAMES = [0, 30, 60, 90, 120]
ANGLES = [[0, 0, 0, 0, 0], [0, 35, 0, -35, 0], [0, -20, 0, 20, 0]]


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def texture_png():
    # One tiny atlas avoids material-color differences between game shaders.
    colors = [(90, 105, 120), (230, 68, 56), (30, 145, 210), (250, 195, 40)]
    raw = b"".join(b"\0" + b"".join(bytes(colors[x // 16]) for x in range(64)) for _ in range(16))
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 64, 16, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


def geometry():
    positions, normals, uvs, joints, weights, indices = [], [], [], [], [], []
    def box(lo, hi, bone, color):
        x, y, z = lo
        X, Y, Z = hi
        faces = [([(x,y,Z),(X,y,Z),(X,Y,Z),(x,Y,Z)],(0,0,1)),
                 ([(X,y,z),(x,y,z),(x,Y,z),(X,Y,z)],(0,0,-1)),
                 ([(x,y,z),(x,y,Z),(x,Y,Z),(x,Y,z)],(-1,0,0)),
                 ([(X,y,Z),(X,y,z),(X,Y,z),(X,Y,Z)],(1,0,0)),
                 ([(x,Y,Z),(X,Y,Z),(X,Y,z),(x,Y,z)],(0,1,0)),
                 ([(x,y,z),(X,y,z),(X,y,Z),(x,y,Z)],(0,-1,0))]
        for pts, normal in faces:
            n = len(positions)
            positions.extend(pts)
            normals.extend([normal] * 4)
            uvs.extend([((color + .5) / 4, .5)] * 4)
            joints.extend([(bone, 0, 0, 0)] * 4)
            weights.extend([(1, 0, 0, 0)] * 4)
            indices.extend([n,n+1,n+2,n,n+2,n+3])
    box((-.55,-.12,-.2),(.55,0,.2),0,0)
    box((-.5,0,-.1),(-.4,1.7,.1),0,0)
    box((-.5,1.6,-.1),(.12,1.7,.1),0,0)
    box((-.07,.8,-.07),(.07,1.6,.07),1,1)
    box((-.06,.15,-.06),(.06,.8,.06),2,2)
    box((-.15,.05,-.11),(.15,.25,.11),2,3)
    return positions, normals, uvs, joints, weights, indices


def write_gltf(path):
    positions, normals, uvs, joints, weights, indices = geometry()
    data = bytearray()
    views, accessors = [], []
    def accessor(rows, fmt, component, kind, bounds=False):
        data.extend(b"\0" * (-len(data) % 4))
        offset = len(data)
        data.extend(b"".join(struct.pack("<" + fmt, *row) for row in rows))
        views.append({"buffer": 0, "byteOffset": offset, "byteLength": len(data) - offset})
        a = {"bufferView": len(views)-1, "componentType": component, "count": len(rows), "type": kind}
        if bounds:
            a["min"] = list(map(min, zip(*rows)))
            a["max"] = list(map(max, zip(*rows)))
        accessors.append(a)
        return len(accessors)-1
    attrs = {"POSITION": accessor(positions,"3f",5126,"VEC3",True),
             "NORMAL": accessor(normals,"3f",5126,"VEC3"),
             "TEXCOORD_0": accessor(uvs,"2f",5126,"VEC2"),
             "JOINTS_0": accessor(joints,"4H",5123,"VEC4"),
             "WEIGHTS_0": accessor(weights,"4f",5126,"VEC4")}
    ix = accessor([(i,) for i in indices],"H",5123,"SCALAR")
    # glTF matrices use column-major storage. All mesh vertices are in model space.
    ibm = []
    for _,_,_,_,(_,y,_) in BONES:
        ibm.append((1,0,0,0, 0,1,0,0, 0,0,1,0, 0,-y,0,1))
    inverse = accessor(ibm,"16f",5126,"MAT4")
    doc = {"asset":{"version":"2.0","generator":"PoeraWork original MMDD probe v1"},
           "scene":0,"scenes":[{"nodes":[0,3]}],
           "nodes":[{"name":BONES[0][2],"children":[1]},
                    {"name":BONES[1][2],"translation":[0,1.6,0],"children":[2]},
                    {"name":BONES[2][2],"translation":[0,-.8,0]},
                    {"name":"ProbeMesh","mesh":0,"skin":0}],
           "skins":[{"joints":[0,1,2],"skeleton":0,"inverseBindMatrices":inverse}],
           "meshes":[{"name":"ProbeMesh","primitives":[{"attributes":attrs,"indices":ix,"material":0}]}],
           "materials":[{"name":"ProbeColors","pbrMetallicRoughness":{"baseColorTexture":{"index":0},"metallicFactor":0,"roughnessFactor":1},"doubleSided":True}],
           "textures":[{"source":0}],"images":[{"uri":"probe_colors.png"}],
           "bufferViews":views,"accessors":accessors,
           "buffers":[{"byteLength":len(data),"uri":"data:application/octet-stream;base64,"+base64.b64encode(data).decode("ascii")}]}
    write_json(path, doc)
    return len(positions), len(indices)//3


def write_vmd(path):
    def sjis(text, size):
        encoded = text.encode("shift_jis")
        if len(encoded) > size:
            raise ValueError("VMD field overflow: " + text)
        return encoded.ljust(size,b"\0")
    data = bytearray(b"Vocaloid Motion Data 0002".ljust(30,b"\0") + sjis("AssetImportProbe",20))
    data.extend(struct.pack("<I",len(BONES)*len(FRAMES)))
    # VMD stores 4 channels in interleaved bytes; the following 3 blocks repeat
    # the first block shifted by 1, 2 and 3 bytes (standard VMD layout).
    curve = bytes([20]*8 + [107]*8)
    interpolation = b"".join(curve[i:] + b"\0"*i for i in range(4))
    for bone, (_,name,_,_,_) in enumerate(BONES):
        for i, frame in enumerate(FRAMES):
            angle = math.radians(ANGLES[bone][i])/2
            y = 4.0 if bone == 0 and frame == 60 else 0.0
            data.extend(sjis(name,15))
            data.extend(struct.pack("<I3f4f",frame,0,y,0,0,0,math.sin(angle),math.cos(angle)))
            data.extend(interpolation)
    # Morph, camera, light, self-shadow, and IK/visibility sections are empty.
    data.extend(struct.pack("<5I",0,0,0,0,0))
    path.write_bytes(data)


def bone_definition():
    bones = []
    for key, vmd, target, parent, _ in BONES:
        b = {"name":key,"vmdName":vmd,"group":"AssetImport Probe",
             "positionData":{"mode":"LOCATE_AT_TARGET","target":target},
             "movable":True,"rotatable":True,"weightBone":target,"weightSyncMode":"GLOBAL",
             "adjustment":{"rotAdjAuto":False,"rotAxisAuto":False}}
        if parent is not None:
            b["parent"] = BONES[parent][0]
        bones.append(b)
    return {"general":{"description":"AssetImport FBX Probe v1 - 3 bones / ITEM",
                       "boneType":"ITEM","enableAutoRotAdj":False,"overwriteDefaultBlend":False},"bones":bones}




def reference_html():
    panels = []
    for i, frame in enumerate(FRAMES):
        lift = 4*.0832 if frame == 60 else 0
        # MMDD default axis conversion negates Z Euler rotation.
        a, b = -ANGLES[1][i], -ANGLES[2][i]
        def rotate(x,y,deg):
            t=math.radians(deg)
            return x*math.cos(t)-y*math.sin(t),x*math.sin(t)+y*math.cos(t)
        x1,y1=rotate(0,-.8,a)
        x2,y2=rotate(0,-.65,a+b)
        points=[(0,1.6+lift),(x1,1.6+lift+y1),(x1+x2,1.6+lift+y1+y2)]
        def p(v):return (130+95*v[0],230-95*v[1])
        A,B,C=map(p,points)
        def line(u,v,color,width):return f'<line x1="{u[0]:.2f}" y1="{u[1]:.2f}" x2="{v[0]:.2f}" y2="{v[1]:.2f}" stroke="{color}" stroke-width="{width}"/>'
        stand=[(-.5,lift),(-.5,1.65+lift),(0,1.65+lift)]
        svg='<svg viewBox="0 0 260 260" role="img" aria-label="Frame '+str(frame)+'">'
        svg+=line(p((-.55,lift)),p((.55,lift)),"#5a6978",14)
        svg+=line(p(stand[0]),p(stand[1]),"#5a6978",9)+line(p(stand[1]),p(stand[2]),"#5a6978",9)
        svg+=line(A,B,"#e64438",13)+line(B,C,"#1e91d2",11)
        svg+=f'<rect x="{C[0]-14.25:.2f}" y="{C[1]-9.5:.2f}" width="28.5" height="19" transform="rotate({-a-b:g} {C[0]:.2f} {C[1]:.2f})" fill="#fac328"/>'
        svg+='</svg>'
        label = "回原位" if frame in (0,120) else "弯折" if frame in (30,90) else "整体上移"
        panels.append(f'<article><b>{frame} 帧 · {frame/30:g} 秒</b>{svg}<p>{label}</p></article>')
    return '''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>FBX / MMDD 3.0 动作对照</title><style>body{font:17px/1.7 system-ui,sans-serif;background:#f3f5f8;color:#243245;max-width:1150px;margin:40px auto;padding:0 20px}h1{font-size:28px}section{display:flex;flex-wrap:wrap;gap:12px}article{background:white;border-radius:12px;padding:16px;flex:1;min-width:170px;text-align:center}svg{width:100%;max-width:260px}pre{white-space:pre-wrap;background:white;padding:24px;border-radius:12px;font:15px/1.7 system-ui,sans-serif}.note{color:#5b6777}</style>
<h1>FBX 骨骼 → MMDD 3.0 物件播放</h1><p>红色上摆杆、蓝色下摆杆、黄色末端。4 秒一个往返；灰色支架只随根骨位移。</p>
<p class="note">离线姿态示意，并非游戏截图。透视、材质和左右方向可能随相机变化；60 帧的上移幅度取决于 MMDD Axis Scale。</p><section>''' + ''.join(panels) + '</section><pre>' + INSTRUCTIONS + '</pre></html>'


def build(assimp, output):
    root=output.resolve()
    root.mkdir(parents=True,exist_ok=True)
    model=root/'UserData/AssetImport/MMDD3Probe'
    model.mkdir(parents=True,exist_ok=True)
    source=root/'source'
    source.mkdir(exist_ok=True)
    (source/'probe_colors.png').write_bytes(texture_png())
    vertices,triangles=write_gltf(source/'probe.gltf')
    subprocess.run([str(assimp.resolve()),'export',str(source/'probe.gltf'),str(model/'probe.fbx'),'-ffbx'],check=True,capture_output=True)
    shutil.copy2(source/'probe_colors.png',model/'probe_colors.png')
    write_vmd(model/'probe.vmd')
    define=root/'BepInEx/plugins/Console/Lib/mmdd/item_AssetImport_Probe_v1.json'
    write_json(define,bone_definition())
    (model/'expected.html').write_text(reference_html(),encoding='utf-8')
    (model/'collect_logs.cmd').write_bytes(b'@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0collect_logs.ps1"\r\npause\r\n')
    script='''$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if (!(Test-Path (Join-Path $gameRoot 'CharaStudio.exe'))) { throw 'Copy UserData into the KK game root before collecting logs.' }
$report = Join-Path $PSScriptRoot ('logs-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $report | Out-Null
$found = @()
foreach ($relative in @('BepInEx/LogOutput.log','output_log.txt','CharaStudio_Data/output_log.txt')) {
  $file = Join-Path $gameRoot $relative
  if (Test-Path $file) {
    Copy-Item -LiteralPath $file -Destination (Join-Path $report ($relative -replace '[/\\\\]','_'))
    $found += 'FOUND ' + $relative
  } else { $found += 'MISSING ' + $relative }
}
$found | Out-File -Encoding UTF8 (Join-Path $report 'files.txt')
Write-Host ('Report: ' + $report)
'''
    (model/'collect_logs.ps1').write_text(script,encoding='utf-8-sig')
    write_json(root/'manifest.json',{'id':'MMDD3-FBX-Probe-v1','fixture':'original glTF converted with Assimp 5.0.1, not Pmx2Fbx',
        'source_vertices':vertices,'triangles':triangles,'bones':[b[2] for b in BONES],
        'vmd_tracks':[b[1] for b in BONES],'vmd_frames':FRAMES,'required':{'AssetImport':'4.1.4','MMDD':'3.0','VNGE':'45.1'},
        'excluded':['PMX import','IK','Morph','physics','arbitrary rigs','Timeline']})
    (root/'素材许可.txt').write_text('本包原创测试模型、贴图、VMD 和示意图以 CC0-1.0 提供。\n不含第三方模型、MMDD/VNGE 源码或游戏 DLL。\n源码生成器在 PoeraWork/AssetImport 的 scripts/testkit/generate_motion_probe.py。\n',encoding='utf-8-sig')
    return root


def package(root, archive):
    sums=[]
    for p in sorted(root.rglob('*')):
        if p.is_file() and p.name != 'SHA256SUMS.txt':
            sums.append(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.relative_to(root).as_posix())
    (root/'SHA256SUMS.txt').write_text('\n'.join(sums)+'\n',encoding='utf-8')
    archive.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
        for p in sorted(root.rglob('*')):
            if p.is_file():z.write(p,p.relative_to(root).as_posix())


if __name__=='__main__':
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--assimp',type=Path,required=True)
    ap.add_argument('--output',type=Path,required=True)
    ap.add_argument('--zip',type=Path)
    args=ap.parse_args()
    result=build(args.assimp,args.output)
    if args.zip:package(result,args.zip)
    print(result)
