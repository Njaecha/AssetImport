using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AssetImport
{
    // Managed PMX 2.0 reader. Disk imports and saved-scene blobs use this same
    // bounded parser; Assimp 5's PMX importer bypasses its memory IO callbacks.
    internal sealed class PmxModel
    {
        internal struct V3
        {
            internal float X, Y, Z;
            internal V3(float x, float y, float z) { X = x; Y = y; Z = z; }
            internal static V3 Add(V3 a, V3 b) { return new V3(a.X+b.X, a.Y+b.Y, a.Z+b.Z); }
            internal static V3 Mul(V3 a, float s) { return new V3(a.X*s, a.Y*s, a.Z*s); }
        }
        internal sealed class Vertex
        {
            internal V3 Position, Normal, UV;
            internal int[] Bones;
            internal float[] Weights;
        }
        internal sealed class Material
        {
            internal string Name;
            internal float[] Diffuse;
            internal int Texture, IndexCount;
            internal bool TwoSided;
        }
        internal sealed class Bone
        {
            internal string Name;
            internal V3 Position;
            internal int Parent, Flags, Layer, GrantParent = -1, IkTarget = -1, IkIterations;
            internal float GrantWeight, IkAngle;
            internal V3 FixedAxis;
            internal List<IkLink> IkLinks = new List<IkLink>();
        }
        internal sealed class IkLink
        {
            internal int Bone;
            internal bool Limited;
            internal V3 Min, Max;
        }
        internal sealed class Morph
        {
            internal string Name;
            internal int Type;
            internal Dictionary<int, V3> Deltas = new Dictionary<int, V3>();
            internal List<KeyValuePair<int, float>> Groups = new List<KeyValuePair<int, float>>();
        }
        internal sealed class RigidBody
        {
            internal string Name;
            internal int Bone, Group, CollisionMask, Shape, Mode;
            internal V3 Size, Position, Rotation;
            internal float Mass, LinearDamping, AngularDamping, Restitution, Friction;
        }
        internal sealed class Joint
        {
            internal string Name;
            internal int BodyA, BodyB;
            internal V3 Position, Rotation, MoveMin, MoveMax, RotateMin, RotateMax, MoveSpring, RotateSpring;
        }
        internal string Name;
        internal Vertex[] Vertices;
        internal int[] Indices;
        internal string[] Textures;
        internal Material[] Materials;
        internal Bone[] Bones;
        internal Morph[] Morphs;
        internal RigidBody[] RigidBodyData;
        internal Joint[] JointData = new Joint[0];
        internal readonly HashSet<int> PhysicsBones = new HashSet<int>();
        internal readonly List<string> IgnoredMetadataWarnings = new List<string>();
        internal readonly List<string> MorphWarnings = new List<string>();
        internal int InvalidIgnoredMetadataFields;
        internal int SdefVertices, RigidBodies, Joints, IkBones, GrantBones, UnusedNormalsRepaired, InvalidRigidBodyPositions;
        internal static string BoneName(int index, string name) { return "PMX_" + index.ToString("D4") + "_" + name; }
    }

    internal sealed class PmxReader : IDisposable
    {
        private readonly BinaryReader reader;
        private Encoding encoding;
        private int vertexSize, textureSize, materialSize, boneSize, morphSize, rigidSize;
        private long Remaining { get { return reader.BaseStream.Length - reader.BaseStream.Position; } }
        private PmxReader(byte[] bytes) { reader = new BinaryReader(new MemoryStream(bytes, false)); }
        public void Dispose() { reader.Close(); }
        private FormatException Error(string message) { return new FormatException("PMX: " + message + " (byte " + reader.BaseStream.Position + ")"); }
        private byte[] Bytes(int size)
        {
            if (size < 0 || size > Remaining) throw Error("truncated data");
            return reader.ReadBytes(size);
        }
        private void Skip(long size)
        {
            if (size < 0 || size > Remaining) throw Error("truncated data");
            reader.BaseStream.Seek(size, SeekOrigin.Current);
        }
        private int Count(int minBytes)
        {
            int value = reader.ReadInt32();
            if (value < 0 || value > 20000000 || (long)value * minBytes > Remaining) throw Error("invalid count");
            return value;
        }
        private string Text() { return encoding.GetString(Bytes(Count(1))); }
        private float Float()
        {
            float value = reader.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value)) throw Error("non-finite number");
            return value;
        }
        private float[] Floats(int n) { var values = new float[n]; for (int i=0;i<n;i++) values[i]=Float(); return values; }
        private PmxModel.V3 Vector() { return new PmxModel.V3(Float(),Float(),Float()); }
        // These fields are deliberately not consumed by this importer. Invalid
        // optional metadata must not reject usable geometry. Still consume every
        // byte through BinaryReader, so truncated records remain fatal.
        private void UnusedFloats(PmxModel model, int count, string field, int index)
        {
            long offset = reader.BaseStream.Position;
            bool invalid = false;
            for (int i = 0; i < count; i++)
            {
                float value = reader.ReadSingle();
                invalid |= float.IsNaN(value) || float.IsInfinity(value);
            }
            if (!invalid) return;
            model.InvalidIgnoredMetadataFields++;
            if (model.IgnoredMetadataWarnings.Count < 16)
                model.IgnoredMetadataWarnings.Add(field + "[" + index + "] (byte " + offset + ")");
        }
        // Some editors leave NaN normals on vertices left over after deleting
        // faces. Defer normal validation until the index table is known;
        // vertex positions, UVs and skin weights remain strict. Optional body
        // positions also use RawVector and are screened by collider planning.
        private PmxModel.V3 RawVector() { return new PmxModel.V3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()); }
        private static bool NonFinite(PmxModel.V3 n)
        {
            return float.IsNaN(n.X) || float.IsInfinity(n.X) ||
                float.IsNaN(n.Y) || float.IsInfinity(n.Y) ||
                float.IsNaN(n.Z) || float.IsInfinity(n.Z);
        }
        private void RepairUnusedNormals(PmxModel model)
        {
            bool[] referenced=null;
            for (int i=0;i<model.Vertices.Length;i++)
            {
                var vertex=model.Vertices[i];
                if (!NonFinite(vertex.Normal)) continue;
                if (referenced==null)
                {
                    referenced=new bool[model.Vertices.Length];
                    foreach (int index in model.Indices) referenced[index]=true;
                }
                if (referenced[i]) throw Error("non-finite normal on referenced vertex " + i);
                // Unreferenced vertices are not copied into any render mesh.
                // Do not weld, remove, renumber or recalculate valid normals:
                // original vertex indices may still be used by morph records.
                vertex.Normal=new PmxModel.V3(0,1,0);
                model.UnusedNormalsRepaired++;
            }
        }
        private int Index(int size, bool unsigned = false)
        {
            if (size == 1) return unsigned ? (int)reader.ReadByte() : reader.ReadSByte();
            if (size == 2) return unsigned ? (int)reader.ReadUInt16() : reader.ReadInt16();
            return reader.ReadInt32();
        }
        private void ValidateIndex(int value, int count, bool optional)
        {
            if (value < (optional ? -1 : 0) || value >= count) throw Error("index outside referenced table");
        }
        internal static PmxModel Read(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            using (var input = new PmxReader(bytes))
            {
                try { return input.ReadModel(); }
                catch (EndOfStreamException) { throw input.Error("truncated data"); }
                catch (DecoderFallbackException) { throw input.Error("invalid text encoding"); }
            }
        }
        private PmxModel ReadModel()
        {
            if (Encoding.ASCII.GetString(Bytes(4)) != "PMX ") throw Error("invalid signature");
            if (Math.Abs(Float() - 2f) > .0001f) throw Error("only PMX 2.0 is supported in this preview; 2.1 soft bodies are not supported");
            if (reader.ReadByte() != 8) throw Error("unsupported header size");
            byte[] h = Bytes(8);
            if (h[0] > 1 || h[1] > 4) throw Error("unsupported encoding or additional UV count");
            for (int i=2;i<8;i++) if (h[i]!=1 && h[i]!=2 && h[i]!=4) throw Error("invalid index size");
            encoding = h[0] == 0 ? (Encoding)new UnicodeEncoding(false,false,true) : new UTF8Encoding(false,true);
            vertexSize=h[2]; textureSize=h[3]; materialSize=h[4]; boneSize=h[5]; morphSize=h[6]; rigidSize=h[7];
            var m = new PmxModel { Name=Text() };
            Text(); Text(); Text();
            m.Vertices = new PmxModel.Vertex[Count(38)];
            for (int i=0;i<m.Vertices.Length;i++)
            {
                var v = new PmxModel.Vertex { Position=Vector(), Normal=RawVector(), UV=new PmxModel.V3(Float(),Float(),0) };
                UnusedFloats(m,4 * h[1],"vertex additional UV",i);
                byte mode = reader.ReadByte();
                int n = mode == 0 ? 1 : mode == 1 || mode == 3 ? 2 : mode == 2 ? 4 : 0;
                if (n == 0) throw Error("unsupported vertex skinning mode");
                v.Bones = new int[n]; for (int j=0;j<n;j++) v.Bones[j]=Index(boneSize);
                v.Weights = n == 1 ? new[] {1f} : n == 2 ? new[] {Float(),0f} : Floats(4);
                if (n == 2) v.Weights[1]=1-v.Weights[0];
                if (mode == 3) { UnusedFloats(m,9,"vertex SDEF parameters",i); m.SdefVertices++; } // approximate SDEF with BDEF2
                UnusedFloats(m,1,"vertex edge scale",i);
                float sum=0; foreach (float w in v.Weights) { if (w < 0 || w > 1.00001f) throw Error("invalid skin weight"); sum+=w; }
                if (sum < .00001f) throw Error("vertex has no effective skin weight");
                for (int j=0;j<n;j++) v.Weights[j]/=sum;
                m.Vertices[i]=v;
            }
            m.Indices = new int[Count(vertexSize)];
            if (m.Indices.Length % 3 != 0) throw Error("non-triangular index count");
            for (int i=0;i<m.Indices.Length;i++) { m.Indices[i]=Index(vertexSize,true); ValidateIndex(m.Indices[i],m.Vertices.Length,false); }
            m.Textures = new string[Count(4)]; for (int i=0;i<m.Textures.Length;i++) m.Textures[i]=Text();
            m.Materials = new PmxModel.Material[Count(80)];
            long assignedIndices=0;
            for (int i=0;i<m.Materials.Length;i++)
            {
                var mat = new PmxModel.Material { Name=Text() }; Text(); mat.Diffuse=Floats(4);
                UnusedFloats(m,7,"material specular/ambient",i); mat.TwoSided=(reader.ReadByte() & 1)!=0;
                UnusedFloats(m,5,"material edge",i);
                mat.Texture=Index(textureSize); ValidateIndex(mat.Texture,m.Textures.Length,true);
                ValidateIndex(Index(textureSize),m.Textures.Length,true); reader.ReadByte();
                byte toon=reader.ReadByte();
                if (toon==0) ValidateIndex(Index(textureSize),m.Textures.Length,true);
                else if (toon==1) reader.ReadByte(); else throw Error("invalid toon flag");
                Text(); mat.IndexCount=reader.ReadInt32();
                if (mat.IndexCount<0 || mat.IndexCount%3!=0) throw Error("invalid material index count");
                assignedIndices+=mat.IndexCount; m.Materials[i]=mat;
            }
            if (assignedIndices!=m.Indices.Length) throw Error("material index counts do not cover mesh");
            m.Bones = new PmxModel.Bone[Count(24)];
            var referencedBones=new List<int>();
            for (int i=0;i<m.Bones.Length;i++)
            {
                var b = new PmxModel.Bone { Name=Text() }; Text(); b.Position=Vector(); b.Parent=Index(boneSize); b.Layer=reader.ReadInt32(); b.Flags=reader.ReadUInt16();
                if ((b.Flags&1)!=0) referencedBones.Add(Index(boneSize)); else UnusedFloats(m,3,"bone display tail offset",i);
                if ((b.Flags&0x300)!=0) { b.GrantParent=Index(boneSize); referencedBones.Add(b.GrantParent); b.GrantWeight=Float(); m.GrantBones++; }
                if ((b.Flags&0x400)!=0) b.FixedAxis=Vector();
                if ((b.Flags&0x800)!=0) UnusedFloats(m,6,"bone local axes",i);
                if ((b.Flags&0x2000)!=0) reader.ReadInt32();
                if ((b.Flags&0x20)!=0)
                {
                    b.IkTarget=Index(boneSize); referencedBones.Add(b.IkTarget); b.IkIterations=reader.ReadInt32(); b.IkAngle=Float(); m.IkBones++;
                    int links=Count(boneSize+1);
                    for (int j=0;j<links;j++)
                    {
                        var link=new PmxModel.IkLink { Bone=Index(boneSize), Limited=reader.ReadByte()!=0 };
                        referencedBones.Add(link.Bone);
                        if (link.Limited) { link.Min=Vector(); link.Max=Vector(); }
                        b.IkLinks.Add(link);
                    }
                }
                m.Bones[i]=b;
            }
            foreach (int idx in referencedBones) ValidateIndex(idx,m.Bones.Length,true);
            for (int i=0;i<m.Bones.Length;i++)
            {
                ValidateIndex(m.Bones[i].Parent,m.Bones.Length,true);
                int index=i;
                for (int hops=0;index>=0;hops++)
                {
                    if (hops>=m.Bones.Length) throw Error("cyclic bone hierarchy");
                    ValidateIndex(index,m.Bones.Length,false); index=m.Bones[index].Parent;
                }
            }
            foreach (var v in m.Vertices) for (int i=0;i<v.Bones.Length;i++) ValidateIndex(v.Bones[i],m.Bones.Length,v.Weights[i]==0);
            m.Morphs=new PmxModel.Morph[Count(14)];
            for (int i=0;i<m.Morphs.Length;i++)
            {
                var morph=new PmxModel.Morph { Name=Text() }; Text(); reader.ReadByte(); morph.Type=reader.ReadByte(); int count=Count(1);
                for (int j=0;j<count;j++)
                {
                    if (morph.Type==0)
                    {
                        int idx=Index(morphSize); ValidateIndex(idx,m.Morphs.Length,false);
                        morph.Groups.Add(new KeyValuePair<int,float>(idx,Float()));
                    }
                    else if (morph.Type==1)
                    {
                        int idx=Index(vertexSize,true); ValidateIndex(idx,m.Vertices.Length,false);
                        PmxModel.V3 old; morph.Deltas.TryGetValue(idx,out old); morph.Deltas[idx]=PmxModel.V3.Add(old,Vector());
                    }
                    else if (morph.Type==2) { ValidateIndex(Index(boneSize),m.Bones.Length,false); Skip(28); }
                    else if (morph.Type>=3 && morph.Type<=7) { ValidateIndex(Index(vertexSize,true),m.Vertices.Length,false); Skip(16); }
                    else if (morph.Type==8) { ValidateIndex(Index(materialSize),m.Materials.Length,true); Skip(113); }
                    else throw Error("unsupported morph type");
                }
                m.Morphs[i]=morph;
            }
            int frames=Count(13);
            for (int i=0;i<frames;i++)
            {
                Text(); Text(); reader.ReadByte(); int count=Count(2);
                for (int j=0;j<count;j++)
                {
                    byte kind=reader.ReadByte();
                    if (kind==0) ValidateIndex(Index(boneSize),m.Bones.Length,false);
                    else if (kind==1) ValidateIndex(Index(morphSize),m.Morphs.Length,false);
                    else throw Error("invalid display frame element");
                }
            }
            m.RigidBodies=Count(70);
            m.RigidBodyData=new PmxModel.RigidBody[m.RigidBodies];
            for (int i=0;i<m.RigidBodies;i++)
            {
                var body=new PmxModel.RigidBody { Name=Text() }; Text();
                body.Bone=Index(boneSize); ValidateIndex(body.Bone,m.Bones.Length,true);
                body.Group=reader.ReadByte(); body.CollisionMask=reader.ReadUInt16(); body.Shape=reader.ReadByte();
                if (body.Group>15) throw Error("invalid rigid body collision group");
                if (body.Shape>2) throw Error("invalid rigid body shape");
                // PMX stores world-space rest transforms; convert only when building
                // a collider so the original rigid body dimensions stay available.
                body.Size=Vector(); body.Position=RawVector(); body.Rotation=Vector();
                // Optional collision obstacles can contain editor garbage even
                // when the render mesh is valid. Keep the original position so
                // TryCollider's finite check skips it, never invent an obstacle
                // at the origin. Geometry, sizes and other floats remain strict.
                if (NonFinite(body.Position)) m.InvalidRigidBodyPositions++;
                body.Mass=Float(); body.LinearDamping=Float(); body.AngularDamping=Float();
                body.Restitution=Float(); body.Friction=Float(); body.Mode=reader.ReadByte();
                if (body.Mode>2) throw Error("invalid rigid body mode");
                m.RigidBodyData[i]=body;
                if (body.Mode>0 && body.Bone>=0) m.PhysicsBones.Add(body.Bone);
            }
            m.Joints=Count(107);
            m.JointData=new PmxModel.Joint[m.Joints];
            for (int i=0;i<m.Joints;i++)
            {
                var joint=new PmxModel.Joint { Name=Text() }; Text();
                if (reader.ReadByte()!=0) throw Error("unsupported joint type");
                joint.BodyA=Index(rigidSize); joint.BodyB=Index(rigidSize);
                ValidateIndex(joint.BodyA,m.RigidBodies,true); ValidateIndex(joint.BodyB,m.RigidBodies,true);
                // Retain optional malformed data for planner diagnostics. This
                // metadata used to be skipped; it must not break mesh import.
                joint.Position=RawVector(); joint.Rotation=RawVector();
                joint.MoveMin=RawVector(); joint.MoveMax=RawVector();
                joint.RotateMin=RawVector(); joint.RotateMax=RawVector();
                joint.MoveSpring=RawVector(); joint.RotateSpring=RawVector();
                m.JointData[i]=joint;
            }
            if (Remaining!=0) throw Error("unexpected trailing data");
            if (m.Vertices.Length==0 || m.Indices.Length==0) throw Error("model has no triangles");
            RepairUnusedNormals(m);
            return m;
        }
    }
}
