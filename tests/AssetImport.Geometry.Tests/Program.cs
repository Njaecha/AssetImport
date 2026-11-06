using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AssetImport;
using BoneInfluence = AssetImport.LegacyMeshGeometry.BoneInfluence;

internal static class Program
{
    private static int _checks;

    private static void Main()
    {
        SmallMeshesPreserveVertexOrder();
        SharedVerticesAndDegenerateTrianglesSurviveSplitting();
        LargeMeshesKeepEveryTriangleAndVertex();
        RandomizedTopologyRoundTrips();
        SplitVertexChannelsAndMorphFramesStayAligned();
        BoneWeightsAreLimitedAndNormalized();
        LocalTransformsPreserveScaleAndHandedness();
        ParentChildTransformsKeepHierarchicalShear();
        ExplicitLocalShearIsDetected();
        InvalidInputIsRejected();
        Console.WriteLine("PASS: geometry partitioning, remapping, morph deltas, legacy skin weights and pure managed transform math (" + _checks + " checks; no Unity runtime).");
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { _checks++; return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static List<LegacyMeshGeometry.MeshPart> RoundTrip(int vertexCount, int[] triangles, int limit)
    {
        var parts = LegacyMeshGeometry.Partition(vertexCount, triangles, limit);
        var reconstructed = new List<int>();
        var present = new HashSet<int>();
        foreach (var part in parts)
        {
            Check(part.SourceVertices.Length <= limit, "A part exceeds the vertex limit.");
            Check(part.SourceVertices.Distinct().Count() == part.SourceVertices.Length, "A part contains duplicate local vertices.");
            Check(part.Triangles.Length % 3 == 0, "A triangle was cut across parts.");
            foreach (int source in part.SourceVertices)
            {
                Check(source >= 0 && source < vertexCount, "Invalid source vertex mapping.");
                present.Add(source);
            }
            foreach (int local in part.Triangles)
            {
                Check(local >= 0 && local < part.SourceVertices.Length, "Invalid local triangle index.");
                reconstructed.Add(part.SourceVertices[local]);
            }
        }
        Check(triangles.SequenceEqual(reconstructed), "Splitting changed triangle order, winding or vertices.");
        Check(present.Count == vertexCount, "Splitting lost an unreferenced vertex.");
        return parts;
    }

    private static void SmallMeshesPreserveVertexOrder()
    {
        var parts = RoundTrip(6, new[] { 4, 1, 3 }, 6);
        Check(parts.Count == 1, "A mesh below the limit should stay intact.");
        Check(parts[0].SourceVertices.SequenceEqual(Enumerable.Range(0, 6)), "A small mesh changed vertex ordering.");
        Check(LegacyMeshGeometry.Partition(0, new int[0]).Count == 1, "An empty source mesh lost its slot.");
    }

    private static void SharedVerticesAndDegenerateTrianglesSurviveSplitting()
    {
        var parts = RoundTrip(8, new[] { 0, 1, 2, 2, 3, 0, 3, 4, 0, 4, 4, 4, 0, 5, 4 }, 4);
        Check(parts.Count > 1, "Expected a split.");
        Check(parts.Sum(p => p.SourceVertices.Length) > 8, "Boundary vertices should be duplicated.");
        RoundTrip(20, new int[0], 3);
    }

    private static void LargeMeshesKeepEveryTriangleAndVertex()
    {
        int limit = LegacyMeshGeometry.MaximumVertices;
        var triangles = new List<int>();
        for (int i = 0; i < limit + 7; i += 3)
        {
            triangles.Add(i);
            triangles.Add(i + 1);
            triangles.Add(i + 2);
        }
        var parts = RoundTrip(limit + 12, triangles.ToArray(), limit);
        Check(parts.Count == 2, "A mesh just above the 16-bit limit should be split into two parts.");
        Check(parts[0].SourceVertices.Length == limit, "The first part should fill the available vertex budget.");
        RoundTrip(limit + 1, new[] { 0, limit, 1 }, limit);
    }

    private static void RandomizedTopologyRoundTrips()
    {
        var random = new Random(23781);
        for (int sample = 0; sample < 80; sample++)
        {
            int vertices = random.Next(3, 300);
            int triangleCount = random.Next(0, 300);
            var triangles = new int[triangleCount * 3];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = random.Next(vertices);
            RoundTrip(vertices, triangles, random.Next(3, 25));
        }
    }

    private static void SplitVertexChannelsAndMorphFramesStayAligned()
    {
        const int count = 9;
        var parts = RoundTrip(count, new[] { 7, 0, 5, 5, 4, 7, 3, 0, 5, 0, 1, 3 }, 4);
        var positions = Enumerable.Range(0, count).Select(i => i * 13f).ToArray();
        var normals = Enumerable.Range(0, count).Select(i => "normal:" + i).ToArray();
        var uv = Enumerable.Range(0, count).Select(i => i / 10.0).ToArray();
        var boneIds = Enumerable.Range(0, count).Select(i => i % 3).ToArray();
        var morph1 = positions.Select((value, i) => value + i * 2f).ToArray();
        var morph2 = positions.Select((value, i) => value - i * 3f).ToArray();
        foreach (var part in parts)
        {
            int[] map = part.SourceVertices;
            var remappedPositions = LegacyMeshGeometry.Remap(positions, map);
            var remappedNormals = LegacyMeshGeometry.Remap(normals, map);
            var remappedUv = LegacyMeshGeometry.Remap(uv, map);
            var remappedBones = LegacyMeshGeometry.Remap(boneIds, map);
            var delta1 = LegacyMeshGeometry.RemapDeltas(positions, morph1, count, map, (a, b) => a - b);
            var delta2 = LegacyMeshGeometry.RemapDeltas(positions, morph2, count, map, (a, b) => a - b);
            var absent = LegacyMeshGeometry.RemapDeltas(new float[0], new float[0], count, map, (a, b) => a - b);
            for (int i = 0; i < map.Length; i++)
            {
                int source = map[i];
                Check(remappedPositions[i] == positions[source], "Position channel moved to the wrong vertex.");
                Check(remappedNormals[i] == normals[source], "Normal channel moved to the wrong vertex.");
                Check(remappedUv[i] == uv[source], "UV channel moved to the wrong vertex.");
                Check(remappedBones[i] == boneIds[source], "Skin channel moved to the wrong vertex.");
                Check(remappedPositions[i] + delta1[i] == morph1[source], "First morph target no longer reconstructs.");
                Check(remappedPositions[i] + delta2[i] == morph2[source], "Second morph target no longer reconstructs.");
                Check(absent[i] == 0, "An absent morph channel must have zero deltas.");
            }
        }
    }

    private static void BoneWeightsAreLimitedAndNormalized()
    {
        var limited = LegacyMeshGeometry.LimitBoneInfluences(new[]
        {
            new BoneInfluence(0, 0.1f), new BoneInfluence(1, 0.05f),
            new BoneInfluence(2, 0.4f), new BoneInfluence(3, 0.2f),
            new BoneInfluence(4, 0.15f), new BoneInfluence(5, 0.1f)
        });
        Check(limited.Length == 4, "Unity 5.6 skin weights must have at most four influences.");
        Check(limited.Select(x => x.BoneIndex).SequenceEqual(new[] { 2, 3, 4, 0 }), "The strongest influences or deterministic tie order were lost.");
        Check(Math.Abs(limited.Sum(x => x.Weight) - 1) < 1e-6, "Truncated skin weights were not normalized.");
        var merged = LegacyMeshGeometry.LimitBoneInfluences(new[] { new BoneInfluence(2, 0.2f), new BoneInfluence(2, 0.3f), new BoneInfluence(1, 0.1f) });
        Check(merged.Length == 2 && merged[0].BoneIndex == 2, "Duplicate bone entries should be merged before limiting.");
        Check(Math.Abs(merged.Sum(x => x.Weight) - 1) < 1e-6, "Weights below a total of one must also be normalized.");
        Check(LegacyMeshGeometry.LimitBoneInfluences(new BoneInfluence[0]).Length == 0, "An unweighted vertex must not acquire a fabricated bone.");
    }

    // System.Numerics uses row vectors; the importer/Assimp use column vectors.
    // Transpose the linear part explicitly so these tests exercise both conventions.
    private static LegacyMeshGeometry.TransformBasis Basis(Matrix4x4 rowMatrix)
    {
        return LegacyMeshGeometry.DecomposeBasis(rowMatrix.M11, rowMatrix.M21, rowMatrix.M31,
            rowMatrix.M12, rowMatrix.M22, rowMatrix.M32, rowMatrix.M13, rowMatrix.M23, rowMatrix.M33);
    }

    private static Matrix4x4 Recompose(LegacyMeshGeometry.TransformBasis basis, Vector3 translation)
    {
        var x = basis.Right.Scaled(basis.Scale.X);
        var y = basis.Up.Scaled(basis.Scale.Y);
        var z = basis.Forward.Scaled(basis.Scale.Z);
        return new Matrix4x4((float)x.X, (float)x.Y, (float)x.Z, 0,
            (float)y.X, (float)y.Y, (float)y.Z, 0,
            (float)z.X, (float)z.Y, (float)z.Z, 0,
            translation.X, translation.Y, translation.Z, 1);
    }

    private static void CheckTransform(Matrix4x4 actual, Matrix4x4 expected)
    {
        // Test transformed basis points as well as a point away from every axis.
        foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(2, -3, 5) })
        {
            Vector3 a = Vector3.Transform(point, actual);
            Vector3 e = Vector3.Transform(point, expected);
            Check(Vector3.Distance(a, e) <= 2e-5f * Math.Max(1, e.Length()), "The decomposed transform changed a point.");
        }
    }

    private static void LocalTransformsPreserveScaleAndHandedness()
    {
        var random = new Random(14532);
        var scales = new[] { 0f, -2f, 3.7f, 0.000001f, 1000000f };
        foreach (float x in scales)
        foreach (float y in scales)
        foreach (float z in scales)
        for (int sample = 0; sample < 6; sample++)
        {
            Quaternion rotation = Quaternion.CreateFromYawPitchRoll((float)random.NextDouble() * 6,
                (float)random.NextDouble() * 6, (float)random.NextDouble() * 6);
            var translation = new Vector3(5, -12, 0.8f);
            Matrix4x4 source = Matrix4x4.CreateScale(x, y, z) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
            var basis = Basis(source);
            Check(!basis.HasShear, "Ordinary local TRS was incorrectly classified as shear.");
            CheckTransform(Recompose(basis, translation), source);
            Check(Math.Abs(LegacyMeshGeometry.BasisVector.Dot(basis.Right,
                LegacyMeshGeometry.BasisVector.Cross(basis.Up, basis.Forward)) - 1) < 1e-8,
                "Rotation basis must stay right handed; reflection belongs in scale.");
        }
    }

    private static void ParentChildTransformsKeepHierarchicalShear()
    {
        var parentPosition = new Vector3(1, 2, 3);
        var childPosition = new Vector3(-4, 0, 2);
        Matrix4x4 parent = Matrix4x4.CreateScale(3, 2, 1) * Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(parentPosition);
        Matrix4x4 child = Matrix4x4.CreateScale(1, 4, 2) * Matrix4x4.CreateRotationZ(0.8f) * Matrix4x4.CreateTranslation(childPosition);
        Check(!Basis(parent).HasShear && !Basis(child).HasShear, "Each local transform should be ordinary TRS.");
        Check(Basis(child * parent).HasShear, "The composed transform should exercise hierarchical shear.");
        CheckTransform(Recompose(Basis(child), childPosition) * Recompose(Basis(parent), parentPosition), child * parent);
    }

    private static void ExplicitLocalShearIsDetected()
    {
        Matrix4x4 shear = Matrix4x4.Identity;
        shear.M21 = 0.35f;
        Check(Basis(shear).HasShear, "An explicit local shear must not silently be presented as exact TRS.");
    }

    private static void InvalidInputIsRejected()
    {
        Throws<ArgumentException>(() => LegacyMeshGeometry.Partition(3, new[] { 0, 1 }));
        Throws<ArgumentException>(() => LegacyMeshGeometry.Partition(3, new[] { 0, 1, 3 }));
        Throws<ArgumentException>(() => LegacyMeshGeometry.Partition(3, new[] { 0, -1, 2 }));
        Throws<ArgumentOutOfRangeException>(() => LegacyMeshGeometry.Partition(3, new int[0], 2));
        Throws<ArgumentException>(() => LegacyMeshGeometry.RemapDeltas(new float[4], new float[3], 4, new[] { 1 }, (a, b) => a - b));
        Throws<ArgumentException>(() => LegacyMeshGeometry.LimitBoneInfluences(new[] { new BoneInfluence(0, float.NaN) }));
        Throws<ArgumentException>(() => LegacyMeshGeometry.LimitBoneInfluences(new[] { new BoneInfluence(0, -1) }));
        Throws<ArgumentException>(() => LegacyMeshGeometry.DecomposeBasis(float.NaN, 0, 0, 0, 1, 0, 0, 0, 1));
    }
}
