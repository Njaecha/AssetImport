using System;
using System.IO;
using System.Text;
using AssetImport;

internal static class RigidBodyReaderChecks
{
    internal static void Run(byte[] baseFixture, Action<bool,string> check)
    {
        var first=new Body { Name="胸の剛体", Bone=0, Group=15, Mask=0x8001, Shape=2, Mode=0 };
        byte[] data=Fixture(baseFixture,first,new Body { Bone=-1, Mode=1 },new Body { Bone=0, Shape=1, Mode=2 });
        var model=PmxReader.Read(data);
        check(model.RigidBodies==3 && model.RigidBodyData.Length==3 && model.Joints==0,"rigid body records retained without changing existing counts");
        var body=model.RigidBodyData[0];
        check(body.Name==first.Name && body.Bone==0,"rigid body primary name and attached bone retained");
        check(body.Group==15 && body.CollisionMask==0x8001,"high collision group and unsigned 16-bit collision mask retained");
        check(body.Shape==2 && body.Mode==0,"shape and static body mode retained");
        check(body.Size.X==.25f && body.Size.Y==2f && body.Size.Z==0,"capsule dimensions keep unused zero axis");
        check(body.Position.X==-3f && body.Position.Y==4f && body.Position.Z==5f,"rigid body rest position stays in original PMX coordinates");
        check(body.Rotation.X==-.5f && body.Rotation.Y==.75f && body.Rotation.Z==1f,"rigid body rotation stays in radians and original axes");
        check(body.Mass==3f && body.LinearDamping==.125f && body.AngularDamping==.25f && body.Restitution==.5f && body.Friction==.75f,"all rigid body physical parameters retained in wire order");
        check(model.RigidBodyData[1].Bone==-1 && model.RigidBodyData[1].Mode==1 && model.RigidBodyData[2].Mode==2,"optional unbound rigid body and both dynamic modes retained");
        check(model.PhysicsBones.Count==1 && model.PhysicsBones.Contains(0),"PhysicsBones still includes only attached bodies with mode greater than zero");
        check(PmxReader.Read(Fixture(baseFixture,first)).PhysicsBones.Count==0,"static body does not mark its bone as simulated");
        check(PmxReader.Read(baseFixture).RigidBodyData.Length==0,"zero rigid body table remains supported");
        check(PmxReader.Read(Fixture(baseFixture,new Body { SizeX=0 })).RigidBodyData[0].Size.X==0,"degenerate radius left for collider planning to skip");
        check(PmxReader.Read(Fixture(baseFixture,new Body { Mask=0xffff })).RigidBodyData[0].CollisionMask==65535,"collision mask is not sign extended");

        Reject(Fixture(baseFixture,new Body { Group=16 }),check,"out-of-range rigid body collision group");
        Reject(Fixture(baseFixture,new Body { Shape=3 }),check,"out-of-range rigid body shape");
        Reject(Fixture(baseFixture,new Body { Mode=3 }),check,"out-of-range rigid body mode");
        Reject(Fixture(baseFixture,new Body { Bone=-2 }),check,"negative invalid rigid body bone reference");
        Reject(Fixture(baseFixture,new Body { Bone=1 }),check,"rigid body bone reference beyond bone table");

        byte[] one=Fixture(baseFixture,new Body());
        // Empty primary/English names are two 32-bit byte lengths; the base
        // fixture uses an 8-bit bone index. The next fourteen floats cover
        // size, position, rotation and all five physical parameters.
        int floats=baseFixture.Length-8+4+8+1+1+2+1;
        for (int i=0;i<14;i++)
        {
            byte[] invalid=(byte[])one.Clone();
            Buffer.BlockCopy(BitConverter.GetBytes(i%2==0 ? float.NaN : float.PositiveInfinity),0,invalid,floats+i*4,4);
            if (i >= 3 && i <= 5)
            {
                var damaged = PmxReader.Read(invalid);
                check(damaged.InvalidRigidBodyPositions == 1, "invalid optional body position recorded");
                var plan = PmxCollisionPlan.Build(damaged, Array.Empty<int[]>());
                check(plan.Colliders.Count == 0 && plan.Warnings.Count == 1, "invalid obstacle skipped with warning, never moved to origin");
                check(damaged.Vertices.Length == model.Vertices.Length && damaged.PhysicsBones.Count == 0, "body position does not affect geometry or physics ownership");
            }
            else Reject(invalid,check,"non-finite rigid body float "+i);
        }
        for (int length=baseFixture.Length-8;length<data.Length;length++)
        {
            byte[] truncated=new byte[length]; Buffer.BlockCopy(data,0,truncated,0,length);
            Reject(truncated,check,"truncated rigid body table at "+length);
        }
    }

    internal sealed class Body
    {
        internal string Name="";
        internal int Bone=0, Group=0, Mask=0, Shape=0, Mode=0;
        internal float SizeX=.25f;
    }

    internal static byte[] Fixture(byte[] original, params Body[] bodies)
    {
        using (var stream=new MemoryStream())
        using (var writer=new BinaryWriter(stream))
        {
            writer.Write(original,0,original.Length-8);
            writer.Write(bodies.Length);
            foreach (var body in bodies)
            {
                byte[] name=Encoding.UTF8.GetBytes(body.Name);
                writer.Write(name.Length); writer.Write(name); writer.Write(0); // primary and English names
                writer.Write((sbyte)body.Bone); writer.Write((byte)body.Group); writer.Write((ushort)body.Mask); writer.Write((byte)body.Shape);
                foreach (float value in new[] {body.SizeX,2f,0f,-3f,4f,5f,-.5f,.75f,1f,3f,.125f,.25f,.5f,.75f}) writer.Write(value);
                writer.Write((byte)body.Mode);
            }
            writer.Write(0); // joint count
            return stream.ToArray();
        }
    }

    private static void Reject(byte[] data, Action<bool,string> check, string label)
    {
        try { PmxReader.Read(data); }
        catch (FormatException) { check(true,label); return; }
        check(false,"accepted "+label);
    }
}
