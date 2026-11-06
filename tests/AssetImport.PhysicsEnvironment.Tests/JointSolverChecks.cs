using AssetImport;
using UnityEngine;

internal static class JointSolverChecks
{
    private static bool SameRotation(Quaternion a, Quaternion b, float tolerance = .002f)
        => (a * new Vector3(1, 0, 0) - b * new Vector3(1, 0, 0)).magnitude < tolerance &&
           (a * new Vector3(0, 1, 0) - b * new Vector3(0, 1, 0)).magnitude < tolerance;

    internal static void Run(Action<bool, string> check)
    {
        var random = new Random(48);
        for (int i = 0; i < 2000; i++)
        {
            float Sample(float range) => ((float)random.NextDouble() * 2 - 1) * range;
            var rotation = PhysicsJointSolver.Rotation(new(Sample(3.14f), Sample(1.57f), Sample(3.14f)));
            check(SameRotation(rotation, PhysicsJointSolver.Rotation(PhysicsJointSolver.Angles(rotation))), "joint XYZ roundtrip");
        }
        foreach (float y in new[] { -MathF.PI / 2, MathF.PI / 2 })
            foreach (float x in new[] { -2f, 0, 1f })
            {
                var q = PhysicsJointSolver.Rotation(new(x, y, .4f));
                check(SameRotation(q, PhysicsJointSolver.Rotation(PhysicsJointSolver.Angles(q))), "singular joint angle roundtrip");
            }
        var min = new Vector3(-.2f, 1, -.1f); var max = new Vector3(.3f, -1, .2f);
        var limited = PhysicsJointSolver.Limit(PhysicsJointSolver.Rotation(new(.8f, .6f, -.8f)), min, max, new(), 0);
        check((PhysicsJointSolver.Angles(limited) - new Vector3(.3f, .6f, -.1f)).magnitude < .0001f,
            "asymmetric angular limits preserve free axes where lower exceeds upper");
        var freeMin = new Vector3(1, 1, 1); var freeMax = new Vector3(-1, -1, -1);
        var input = PhysicsJointSolver.Rotation(new(.8f, 0, 0));
        var spring = new Vector3(3600, 0, 0);
        check(Math.Abs(PhysicsJointSolver.Angles(PhysicsJointSolver.Limit(input, freeMin, freeMax, spring, 1f / 60)).x - .4f) < .0001f,
            "spring response uses one integration time step");
        check(SameRotation(input, PhysicsJointSolver.Limit(input, freeMin, freeMax, spring, 0)),
            "render-only passes do not repeatedly apply spring response");

        var model = DrapeChecks.Model(false, (0, 0, .4f));
        var chain = model.Chain;
        var joints = chain.m_Particles.Take(4).Select(p => new PhysicsAngularJoint {
            Parent = p.m_Transform.parent, Bone = p.m_Transform, Frame = Quaternion.identity,
            Min = new(-.15f, -.15f, -.15f), Max = new(.15f, .15f, .15f), SpringRate = new(2, 2, 2)
        }).ToArray();
        var bindings = new[] { new PhysicsJointBinding { Chain = chain, Joints = joints } };
        AssetPhysicsEnvironment.Attach(chain.gameObject, model.Environment.ImportedRoot, [chain],
            new() { DownwardForce = .001f }, joints: bindings);
        check(model.Environment.JointCount(chain) == 4, "joint bindings register on their owning chain");
        DrapeChecks.Run(chain, 1500, true);
        var tip = chain.m_Particles.Last().m_Position;
        float drop = chain.m_Root.position.y - tip.y;
        Console.WriteLine($"Angular support fixture: static drop={drop:F5}, tip X={tip.x:F5}, chain length=1.");
        check(drop > .05f && drop < .5f && tip.x > .7f, "angular support permits moderate sag without a vertical collapse");
        CheckLinks(chain, check);
        // Rotate the whole anchor: constraints must rotate with the model and
        // never pull a moved model toward the original world-space pose.
        model.Environment.ImportedRoot.rotation = Quaternion.Turn(.35f);
        chain.m_Root.position += new Vector3(3, 1, 0);
        DrapeChecks.Run(chain, 900, true);
        CheckLinks(chain, check);
        check(chain.m_Particles.Last().m_Position.x > 3.5f, "angular frames follow a moved and rotated model");
        var settings = model.Environment.Settings.Copy(); settings.NaturalDrape = true;
        model.Environment.Apply(settings); DrapeChecks.Run(chain, 1800, true);
        check(chain.m_Particles.Last().m_Position.y < chain.m_Root.position.y - .98f,
            "explicit free hanging releases native and PMX angular shape support");

        // A reversed body pair uses the same joint-local coordinates, with
        // inverse relative rotation; asymmetric bounds must therefore flip.
        var reverse = DrapeChecks.Model(false, (0, 0, .4f));
        var rc = reverse.Chain;
        var rj = new PhysicsAngularJoint { Parent = rc.m_Root.parent, Bone = rc.m_Root,
            Frame = Quaternion.identity, Reverse = true, Min = new(0, 0, -.3f), Max = new(0, 0, -.1f) };
        var solver = new PhysicsJointSolver(new() { Chain = rc, Joints = [rj] });
        solver.Apply(rc, reverse.Environment.ImportedRoot, false);
        check(rc.m_Particles[1].m_Position.y < rc.m_Root.position.y,
            "reversing an asymmetric joint constrains the opposite relative direction");
        CheckLinks(rc, check);
        var collider = rc.gameObject.AddComponent<DynamicBoneCollider>();
        int contacts = 0;
        collider.Project = (position, radius) => { contacts++; return new(position.x, Math.Max(position.y, rc.m_Root.position.y), position.z); };
        rc.m_Colliders.Add(collider);
        rc.InitTransforms(); rc.ResetParticlesPosition();
        solver.Apply(rc, reverse.Environment.ImportedRoot, false);
        check(contacts > 0 && rc.m_Particles[1].m_Position.y >= rc.m_Root.position.y,
            "native body colliders are rechecked after angular projection");
        var previous = rc.m_Particles[1].m_Position;
        rc.m_FreezeAxis = DynamicBone.FreezeAxis.X;
        solver.Apply(rc, reverse.Environment.ImportedRoot, true);
        check((previous - rc.m_Particles[1].m_Position).magnitude == 0, "an editor's frozen axis takes priority over PMX angle projection");
    }

    private static void CheckLinks(DynamicBone chain, Action<bool, string> check)
    {
        foreach (var p in chain.m_Particles.Skip(1))
            check(Math.Abs((p.m_Position - chain.m_Particles[p.m_ParentIndex].m_Position).magnitude - .25f) < .0001f,
                "constrained links remain finite and preserve length");
        foreach (var p in chain.m_Particles.Take(4))
        {
            var a = PhysicsJointSolver.Angles(Quaternion.Inverse(p.m_Transform.parent.rotation) * p.m_Transform.rotation);
            check(Math.Abs(a.x) <= .1501f && Math.Abs(a.y) <= .1501f && Math.Abs(a.z) <= .1501f,
                "final bone angles respect parent-relative bounds");
        }
    }
}
