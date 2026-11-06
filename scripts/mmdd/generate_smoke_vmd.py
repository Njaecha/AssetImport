#!/usr/bin/env python3
"""Original 8-second MMD motion: nod/blink, arm, left foot IK, root lift."""
import argparse
import math
from pathlib import Path
import struct


def create():
    def name(text,n):
        raw=text.encode('shift_jis')
        if len(raw)>n:raise ValueError(text)
        return raw.ljust(n,b'\0')
    frames=list(range(0,241,30))
    tracks=['頭','左腕','左足ＩＫ','全ての親']
    data=bytearray(b'Vocaloid Motion Data 0002'.ljust(30,b'\0')+name('PMX smoke',20))
    data+=struct.pack('<I',len(frames)*len(tracks))
    curve=bytes([20]*8+[107]*8)
    interpolation=b''.join(curve[i:]+b'\0'*i for i in range(4))
    for track in tracks:
        for frame in frames:
            p=(0,0,0);q=(0,0,0,1)
            if track=='頭' and frame==30:
                t=math.radians(12)/2;q=(math.sin(t),0,0,math.cos(t))
            if track=='左腕' and frame==90:
                t=math.radians(30)/2;q=(0,0,math.sin(t),math.cos(t))
            if track=='左足ＩＫ' and frame==150:p=(0,1.2,-.5)
            if track=='全ての親' and frame==210:p=(0,1,0)
            data+=name(track,15)+struct.pack('<I3f4f',frame,*p,*q)+interpolation
    data+=struct.pack('<I',len(frames))
    for frame in frames:data+=name('まばたき',15)+struct.pack('<If',frame,1.0 if frame==30 else 0.0)
    data+=struct.pack('<4I',0,0,0,0)
    return bytes(data)


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('output',type=Path);a=p.parse_args()
    a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_bytes(create());print(a.output)
