#!/usr/bin/env python3
"""Generate original small house test assets; no third-party model content."""
from pathlib import Path
import struct, json, zlib, math, shutil, argparse, subprocess
from xml.sax.saxutils import escape
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--assimp',type=Path,required=True,help='Assimp 5.0.1 CLI used for original-model binary FBX export')
parser.add_argument('--output',type=Path,required=True,help='Empty output directory for regenerated original fixtures')
args=parser.parse_args()
ROOT=args.output.resolve()
ROOT.mkdir(parents=True,exist_ok=True)
MATERIALS=[('CheckerWalls',(1,1,1,1)),('OrangeRoof',(0.85,0.22,0.045,1)),('DarkChimney',(0.10,0.14,0.18,1))]
# Each part has separate vertices at surface boundaries, yielding unambiguous flat normals and UVs.
parts=[dict(name=n,material=i,positions=[],normals=[],uvs=[],triangles=[]) for i,n in enumerate(['Walls','Roof','Chimney'])]
def face(part, pts, uv=None):
    if uv is None: uv=[(0,0),(1,0),(1,1),(0,1)][:len(pts)]
    a,b,c=pts[:3];u=[b[i]-a[i] for i in range(3)];v=[c[i]-a[i] for i in range(3)]
    n=(u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]);l=math.sqrt(sum(x*x for x in n));n=tuple(x/l for x in n)
    start=len(part['positions']);part['positions']+=list(pts);part['normals'] += [n]*len(pts);part['uvs']+=list(uv)
    part['triangles'] += [(start,start+i,start+i+1) for i in range(1,len(pts)-1)]
def box(p,x0,x1,y0,y1,z0,z1):
    face(p,[(x0,y0,z1),(x1,y0,z1),(x1,y1,z1),(x0,y1,z1)])
    face(p,[(x1,y0,z0),(x0,y0,z0),(x0,y1,z0),(x1,y1,z0)])
    face(p,[(x0,y0,z0),(x0,y0,z1),(x0,y1,z1),(x0,y1,z0)])
    face(p,[(x1,y0,z1),(x1,y0,z0),(x1,y1,z0),(x1,y1,z1)])
    face(p,[(x0,y1,z1),(x1,y1,z1),(x1,y1,z0),(x0,y1,z0)])
    face(p,[(x0,y0,z0),(x1,y0,z0),(x1,y0,z1),(x0,y0,z1)])
box(parts[0],-1,1,0,1.2,-.7,.7)
# Off-centre ridge and offset chimney make mirrored axes/rotations easy to notice.
face(parts[1],[(-1.1,1.2,.8),(1.1,1.2,.8),(-.35,2,.8)],[(0,0),(1,0),(.35,1)])
face(parts[1],[(1.1,1.2,-.8),(-1.1,1.2,-.8),(-.35,2,-.8)],[(0,0),(1,0),(.65,1)])
face(parts[1],[(-1.1,1.2,-.8),(-1.1,1.2,.8),(-.35,2,.8),(-.35,2,-.8)])
face(parts[1],[(-.35,2,-.8),(-.35,2,.8),(1.1,1.2,.8),(1.1,1.2,-.8)])
face(parts[1],[(-1.1,1.2,-.8),(1.1,1.2,-.8),(1.1,1.2,.8),(-1.1,1.2,.8)])
box(parts[2],.4,.68,1.35,2.15,-.28,0)
def png_chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
def png():
    pixels=[]
    for y in range(64):
        row=bytearray([0])
        for x in range(64):
            col=(20,175,190) if ((x//8+y//8)%2)==0 else (250,231,168)
            # One red corner provides a recognizable UV orientation marker.
            if x<8 and y<8:col=(225,55,75)
            row.extend(col)
        pixels.append(bytes(row))
    return b'\x89PNG\r\n\x1a\n'+png_chunk(b'IHDR',struct.pack('>IIBBBBB',64,64,8,2,0,0,0))+png_chunk(b'IDAT',zlib.compress(b''.join(pixels)))+png_chunk(b'IEND',b'')
TEX=png(); records=[]
def folder(name,texture):
    p=ROOT/name;p.mkdir(exist_ok=True)
    if texture:(p/'checker.png').write_bytes(TEX)
    return p
EXPECT='小房子：青绿/米黄棋盘墙面，橙色不对称斜屋顶，屋顶右侧有深灰烟囱。屋脊偏左，烟囱偏右。'
def record(path,fmt,texture,notes='',extra=None):
    files=sorted(str(p.relative_to(ROOT)) for p in path.parent.iterdir() if p.is_file())
    rec=dict(entry=str(path.relative_to(ROOT)),format=fmt,expected_shape=EXPECT if texture else '与彩色小房子相同的几何轮廓：长方体墙体、不对称斜屋顶、偏置烟囱；只测试几何。',texture_expected=texture,texture='checker.png' if texture else None,expected_materials=3 if texture else 1,files=files,source={'kind':'original_generated','author':'PoeraWork AssetImport demo fixture','license':'CC0-1.0','generator':'generate.py'},notes=notes,bounds_y_up=[[-1.1,0,-.8],[1.1,2.15,.8]],triangles=sum(len(p['triangles']) for p in parts))
    if extra:rec.update(extra)
    records.append(rec)
def f(values):return ' '.join(format(x,'.7g') for x in values)
def flatten(values):return [x for row in values for x in row]
# OBJ and MTL.
p=folder('01_OBJ',True);out=['# Original AssetImport demo house','mtllib house.mtl'];offset=1
for part in parts:
    out+=['o '+part['name']]+['v '+f(v) for v in part['positions']]+['vt '+f(v) for v in part['uvs']]+['vn '+f(v) for v in part['normals']]+['usemtl '+MATERIALS[part['material']][0]]
    for tri in part['triangles']:out.append('f '+' '.join(f'{v+offset}/{v+offset}/{v+offset}' for v in tri))
    offset+=len(part['positions'])
(p/'house.obj').write_text('\n'.join(out)+'\n')
mtl=[]
for i,(name,color) in enumerate(MATERIALS):
    mtl+=['newmtl '+name,'Ka 0 0 0','Kd '+f(color[:3]),'Ks 0 0 0','d 1','illum 1']
    if i==0:mtl+=['map_Kd checker.png']
    mtl+=['']
(p/'house.mtl').write_text('\n'.join(mtl));record(p/'house.obj','obj',True)
# Collada 1.4.1, explicit Y_UP and local images.
p=folder('04_DAE',True)
x=['<?xml version="1.0" encoding="utf-8"?>','<COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">','<asset><contributor><author>PoeraWork AssetImport demo fixture</author></contributor><created>2026-10-01T00:00:00Z</created><modified>2026-10-01T00:00:00Z</modified><unit meter="1" name="meter"/><up_axis>Y_UP</up_axis></asset>','<library_images><image id="checker"><init_from>checker.png</init_from></image></library_images>','<library_effects>']
for i,(name,color) in enumerate(MATERIALS):
    x += [f'<effect id="effect{i}"><profile_COMMON>']
    if i==0:x+=['<newparam sid="surface"><surface type="2D"><init_from>checker</init_from></surface></newparam><newparam sid="sampler"><sampler2D><source>surface</source></sampler2D></newparam>']
    diffuse='<texture texture="sampler" texcoord="UVMap"/>' if i==0 else '<color>'+f(color)+'</color>'
    x += ['<technique sid="common"><lambert><diffuse>'+diffuse+'</diffuse></lambert></technique></profile_COMMON></effect>']
x+=['</library_effects><library_materials>']+[f'<material id="mat{i}" name="{n}"><instance_effect url="#effect{i}"/></material>' for i,(n,c) in enumerate(MATERIALS)]+['</library_materials><library_geometries>']
for i,part in enumerate(parts):
    x+=[f'<geometry id="mesh{i}" name="{part["name"]}"><mesh>']
    for suffix,values,params in [('pos',part['positions'],['X','Y','Z']),('norm',part['normals'],['X','Y','Z']),('uv',part['uvs'],['S','T'])]:
        ident=f'mesh{i}-{suffix}';x += [f'<source id="{ident}"><float_array id="{ident}-array" count="{len(values)*len(params)}">{f(flatten(values))}</float_array><technique_common><accessor source="#{ident}-array" count="{len(values)}" stride="{len(params)}">'+''.join(f'<param name="{a}" type="float"/>' for a in params)+'</accessor></technique_common></source>']
    x+=[f'<vertices id="verts{i}"><input semantic="POSITION" source="#mesh{i}-pos"/></vertices>',f'<triangles count="{len(part["triangles"])}" material="material{i}"><input semantic="VERTEX" source="#verts{i}" offset="0"/><input semantic="NORMAL" source="#mesh{i}-norm" offset="1"/><input semantic="TEXCOORD" source="#mesh{i}-uv" offset="2" set="0"/><p>']
    x+=[' '.join(str(v) for tri in part['triangles'] for q in tri for v in [q,q,q]),'</p></triangles></mesh></geometry>']
x+=['</library_geometries><library_visual_scenes><visual_scene id="Scene" name="House">']
for i,part in enumerate(parts):x+=[f'<node id="node{i}" name="{part["name"]}" type="NODE"><instance_geometry url="#mesh{i}"><bind_material><technique_common><instance_material symbol="material{i}" target="#mat{i}"><bind_vertex_input semantic="UVMap" input_semantic="TEXCOORD" input_set="0"/></instance_material></technique_common></bind_material></instance_geometry></node>']
x+=['</visual_scene></library_visual_scenes><scene><instance_visual_scene url="#Scene"/></scene></COLLADA>'];(p/'house.dae').write_text('\n'.join(x));record(p/'house.dae','dae',True)
# glTF 2.0 and GLB, buffer views aligned to 4 bytes, indexed triangles.
def gltf(inline_image):
    data=bytearray();views=[];access=[]
    def add_blob(blob,target=None):
        while len(data)%4:data.append(0)
        view={'buffer':0,'byteOffset':len(data),'byteLength':len(blob)}
        if target:view['target']=target
        views.append(view);data.extend(blob);return len(views)-1
    def add_array(rows,ct,typ,target,limits=False):
        flat=flatten(rows) if isinstance(rows[0],(list,tuple)) else rows
        view=add_blob(struct.pack('<'+('f' if ct==5126 else 'H')*len(flat),*flat),target)
        a={'bufferView':view,'componentType':ct,'count':len(rows),'type':typ}
        if limits:a.update(min=[min(r[k] for r in rows) for k in range(3)],max=[max(r[k] for r in rows) for k in range(3)])
        access.append(a);return len(access)-1
    meshes=[]
    for part in parts:
        # glTF v=0 is image top; flip OBJ's bottom-origin v for the same marker orientation.
        attrs={'POSITION':add_array(part['positions'],5126,'VEC3',34962,True),'NORMAL':add_array(part['normals'],5126,'VEC3',34962),'TEXCOORD_0':add_array([(u,1-v) for u,v in part['uvs']],5126,'VEC2',34962)}
        meshes.append({'name':part['name'],'primitives':[{'attributes':attrs,'indices':add_array(flatten(part['triangles']),5123,'SCALAR',34963),'material':part['material'],'mode':4}]})
    image={'bufferView':add_blob(TEX),'mimeType':'image/png'} if inline_image else {'uri':'checker.png'}
    mats=[]
    for i,(name,col) in enumerate(MATERIALS):
        pbr={'baseColorFactor':list(col),'metallicFactor':0,'roughnessFactor':1}
        if i==0:pbr['baseColorTexture']={'index':0,'texCoord':0}
        mats.append({'name':name,'pbrMetallicRoughness':pbr})
    doc={'asset':{'version':'2.0','generator':'PoeraWork original demo generator'},'scene':0,'scenes':[{'name':'CheckerHouse','nodes':list(range(3))}],'nodes':[{'name':p['name'],'mesh':i} for i,p in enumerate(parts)],'meshes':meshes,'materials':mats,'textures':[{'source':0,'sampler':0}],'samplers':[{'magFilter':9728,'minFilter':9728,'wrapS':10497,'wrapT':10497}],'images':[image],'accessors':access,'bufferViews':views,'buffers':[{'byteLength':len(data)}]}
    return doc,bytes(data)
p=folder('05_GLTF',True);doc,data=gltf(False);doc['buffers'][0]['uri']='house.bin';(p/'house.bin').write_bytes(data);(p/'house.gltf').write_text(json.dumps(doc,indent=2)+'\n');record(p/'house.gltf','gltf',True,notes='保留同目录 house.bin 和 checker.png；验证 glTF 外置缓冲与外置纹理。')
p=folder('06_GLB',False);doc,data=gltf(True);js=json.dumps(doc,separators=(',',':')).encode();js+=b' '*((-len(js))%4);data+=b'\0'*((-len(data))%4);blob=struct.pack('<III',0x46546c67,2,12+8+len(js)+8+len(data))+struct.pack('<II',len(js),0x4e4f534a)+js+struct.pack('<II',len(data),0x004e4942)+data;(p/'house.glb').write_bytes(blob);record(p/'house.glb','glb',True,notes='checker.png 嵌入 GLB；无需外部贴图。',extra={'texture':'embedded PNG'})
# 3DS chunks; the file stores Z-up coordinates, converted from the common Y-up source.
def ch(i,b):return struct.pack('<HI',i,len(b)+6)+b
def cstr(s):return s.encode('ascii')+b'\0'
p=folder('07_3DS',True);editor=[]
for i,(name,col) in enumerate(MATERIALS):
    mat=ch(0xa000,cstr(name))+ch(0xa020,ch(0x0011,bytes(round(v*255) for v in col[:3])))
    if i==0:mat+=ch(0xa200,ch(0x0030,struct.pack('<H',100))+ch(0xa300,cstr('checker.png')))
    editor.append(ch(0xafff,mat))
for part in parts:
    vs=[(x,-z,y) for x,y,z in part['positions']]
    vertices=ch(0x4110,struct.pack('<H',len(vs))+struct.pack('<'+'f'*len(flatten(vs)),*flatten(vs)))
    tris=part['triangles'];facebytes=struct.pack('<H',len(tris))+b''.join(struct.pack('<4H',*tri,7) for tri in tris)
    facebytes+=ch(0x4130,cstr(MATERIALS[part['material']][0])+struct.pack('<H',len(tris))+struct.pack('<'+'H'*len(tris),*range(len(tris))))
    uvs=part['uvs'];tex=ch(0x4140,struct.pack('<H',len(uvs))+struct.pack('<'+'f'*len(flatten(uvs)),*flatten(uvs)))
    editor.append(ch(0x4000,cstr(part['name'])+ch(0x4100,vertices+ch(0x4120,facebytes)+tex)))
(p/'house.3ds').write_bytes(ch(0x4d4d,ch(0x0002,struct.pack('<I',3))+ch(0x3d3d,ch(0x3d3e,struct.pack('<I',3))+b''.join(editor))));record(p/'house.3ds','3ds',True,notes='3DS 按格式惯例保存 Z-up；导入后应是烟囱朝上的房子。')
# Geometry-only PLY and STL deliberately contain no materials/texture.
vertices=[];triangles=[]
for part in parts:
    offset=len(vertices);vertices+=part['positions'];triangles += [tuple(i+offset for i in tri) for tri in part['triangles']]
p=folder('08_PLY',False);head=['ply','format ascii 1.0','comment Original AssetImport geometry-only house',f'element vertex {len(vertices)}','property float x','property float y','property float z',f'element face {len(triangles)}','property list uchar int vertex_indices','end_header'];(p/'house.ply').write_text('\n'.join(head+[' '.join(str(v) for v in row) for row in vertices]+['3 '+' '.join(str(v) for v in tri) for tri in triangles])+'\n');record(p/'house.ply','ply',False,notes='无材质/贴图，仅检查几何。白色/默认材质正常，不代表导入失败。')
p=folder('09_STL',False);b=bytearray(b'Original AssetImport geometry-only checker house'.ljust(80,b' ')+struct.pack('<I',len(triangles)))
for part in parts:
    for tri in part['triangles']:b.extend(struct.pack('<12fH',*part['normals'][tri[0]],*flatten([part['positions'][i] for i in tri]),0))
(p/'house.stl').write_bytes(b);record(p/'house.stl','stl',False,notes='标准二进制 STL，无材质/贴图，仅检查几何。白色/默认材质正常，不代表导入失败。')
# FBX 7.4 ASCII. Explicit per-control-point normals/UV and one material per geometry.
p=folder('02_FBX_ASCII',True)
floats=lambda rows:','.join(format(v,'.7g') for v in flatten(rows))
x=['; FBX 7.4.0 project file','; Original geometry generated for AssetImport tests','FBXHeaderExtension:  {','  FBXHeaderVersion: 1003','  FBXVersion: 7400','  Creator: "PoeraWork demo generator"','}', 'GlobalSettings:  {','  Version: 1000','  Properties70:  {','    P: "UpAxis", "int", "Integer", "",1','    P: "UpAxisSign", "int", "Integer", "",1','    P: "FrontAxis", "int", "Integer", "",2','    P: "FrontAxisSign", "int", "Integer", "",-1','    P: "CoordAxis", "int", "Integer", "",0','    P: "CoordAxisSign", "int", "Integer", "",1','    P: "UnitScaleFactor", "double", "Number", "",100','  }','}','Documents:  {',' Count: 1',' Document: 1, "Scene", "Scene" {','  RootNode: 0',' }','}','Definitions:  {',' Version: 100',' Count: 11',' ObjectType: "Geometry" { Count: 3 }',' ObjectType: "Model" { Count: 3 }',' ObjectType: "Material" { Count: 3 }',' ObjectType: "Texture" { Count: 1 }',' ObjectType: "Video" { Count: 1 }','}','Objects:  {']
for i,part in enumerate(parts):
    idx=[]
    for a,b,c in part['triangles']:idx += [a,b,-c-1]
    x += [f' Geometry: {100+i}, "Geometry::{part["name"]}", "Mesh" {{',f'  Vertices: *{len(part["positions"])*3} {{ a: {floats(part["positions"])} }}',f'  PolygonVertexIndex: *{len(idx)} {{ a: '+','.join(map(str,idx))+' }','  GeometryVersion: 124','  LayerElementNormal: 0 {','   Version: 101','   Name: ""','   MappingInformationType: "ByVertice"','   ReferenceInformationType: "Direct"',f'   Normals: *{len(part["normals"])*3} {{ a: {floats(part["normals"])} }}','  }','  LayerElementUV: 0 {','   Version: 101','   Name: "UVMap"','   MappingInformationType: "ByVertice"','   ReferenceInformationType: "Direct"',f'   UV: *{len(part["uvs"])*2} {{ a: {floats(part["uvs"])} }}','  }','  LayerElementMaterial: 0 {','   Version: 101','   Name: ""','   MappingInformationType: "AllSame"','   ReferenceInformationType: "IndexToDirect"','   Materials: *1 { a: 0 }','  }','  Layer: 0 {','   Version: 100','   LayerElement: { Type: "LayerElementNormal" TypedIndex: 0 }','   LayerElement: { Type: "LayerElementMaterial" TypedIndex: 0 }','   LayerElement: { Type: "LayerElementUV" TypedIndex: 0 }','  }',' }',f' Model: {200+i}, "Model::{part["name"]}", "Mesh" {{','  Version: 232','  Properties70:  {','   P: "Lcl Translation", "Lcl Translation", "", "A",0,0,0','   P: "Lcl Rotation", "Lcl Rotation", "", "A",0,0,0','   P: "Lcl Scaling", "Lcl Scaling", "", "A",1,1,1','  }','  Shading: T','  Culling: "CullingOff"',' }']
for i,(name,col) in enumerate(MATERIALS):x+=[f' Material: {300+i}, "Material::{name}", "" {{','  Version: 102','  ShadingModel: "lambert"','  MultiLayer: 0','  Properties70:  {','   P: "DiffuseColor", "Color", "", "A",'+','.join(str(c) for c in col[:3]),'   P: "DiffuseFactor", "Number", "", "A",1','  }',' }']
x+=[' Texture: 400, "Texture::Checker", "" {','  Type: "TextureVideoClip"','  Version: 202','  TextureName: "Texture::Checker"','  Media: "Video::Checker"','  FileName: "checker.png"','  RelativeFilename: "checker.png"','  ModelUVTranslation: 0,0','  ModelUVScaling: 1,1','  Texture_Alpha_Source: "None"','  Cropping: 0,0,0,0',' }',' Video: 500, "Video::Checker", "Clip" {','  Type: "Clip"','  Properties70: {','   P: "Path", "KString", "XRefUrl", "", "checker.png"','  }','  UseMipMap: 0','  Filename: "checker.png"','  RelativeFilename: "checker.png"',' }','}','Connections: {']
for i in range(3):x += [f' C: "OO",{200+i},0',f' C: "OO",{100+i},{200+i}',f' C: "OO",{300+i},{200+i}']
x+=[' C: "OP",400,300,"DiffuseColor"',' C: "OO",500,400','}'];(p/'house.fbx').write_text('\n'.join(x)+'\n');record(p/'house.fbx','fbx-ascii',True,notes='FBX 7.4 ASCII，UV + 外置 checker.png；与 binary FBX 分开验证。')
# Binary FBX is exported from the same original OBJ with the matching Assimp version.
if args.assimp.is_file():
    p=folder('03_FBX_BINARY',True)
    subprocess.run([str(args.assimp.resolve()),'export',str(ROOT/'01_OBJ/house.obj'),str(p/'house.fbx'),'-ffbx'],check=True,capture_output=True)
    record(p/'house.fbx','fbx-binary',True,notes='FBX binary，使用原生 Assimp v5.0.1 从本套自有 OBJ 导出；UV + 外置 checker.png。')
    records[-1]['source']['conversion']='Assimp v5.0.1 (8f0c6b04), assimp export 01_OBJ/house.obj 03_FBX_BINARY/house.fbx -ffbx'
else:
    print('Binary FBX not regenerated: pass --assimp /path/to/Assimp-5.0.1-cli')
records.sort(key=lambda a:a['entry'])
(ROOT/'source-house.json').write_text(json.dumps({'materials':MATERIALS,'parts':parts,'y_up':True},indent=2)+'\n')
(ROOT/'manifest.json').write_text(json.dumps({'title':'Original checker house common-format demos','assets':records},ensure_ascii=False,indent=2)+'\n')
(ROOT/'LICENSE.txt').write_text('All geometry, UV data, checker.png, generator code and descriptions in this demo set are original fixtures created for PoeraWork AssetImport.\nDedicated to the public domain under CC0 1.0 Universal (CC0-1.0).\nhttps://creativecommons.org/publicdomain/zero/1.0/\n')
print(f'Generated {len(records)} import entries; {len(vertices)} vertices, {len(triangles)} triangles in source house')
