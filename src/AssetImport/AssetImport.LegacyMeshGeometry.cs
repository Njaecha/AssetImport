using System;
using System.Collections.Generic;

namespace AssetImport
{
    /// <summary>
    /// Pure managed geometry operations for Unity 5.6. Keeping the original vertex
    /// index for every output vertex lets all vertex channels share one remapping.
    /// </summary>
    internal static class LegacyMeshGeometry
    {
        internal const int MaximumVertices = 65535;

        internal sealed class MeshPart
        {
            internal readonly int[] SourceVertices;
            internal readonly int[] Triangles;

            internal MeshPart(int[] sourceVertices, int[] triangles)
            {
                SourceVertices = sourceVertices;
                Triangles = triangles;
            }
        }

        internal struct BoneInfluence
        {
            internal readonly int BoneIndex;
            internal readonly float Weight;

            internal BoneInfluence(int boneIndex, float weight)
            {
                BoneIndex = boneIndex;
                Weight = weight;
            }
        }

        internal struct BasisVector
        {
            internal readonly double X, Y, Z;
            internal BasisVector(double x, double y, double z) { X = x; Y = y; Z = z; }
            internal double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
            internal BasisVector Scaled(double scale) { return new BasisVector(X * scale, Y * scale, Z * scale); }
            internal BasisVector Normalized() { return Length == 0 ? this : Scaled(1 / Length); }
            internal static double Dot(BasisVector a, BasisVector b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
            internal static BasisVector Cross(BasisVector a, BasisVector b)
            {
                return new BasisVector(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            }
        }

        internal sealed class TransformBasis
        {
            internal BasisVector Scale, Right, Up, Forward;
            internal bool HasShear;
        }

        /// <summary>
        /// Decomposes a column-vector transform's linear part. Ordinary TRS,
        /// reflected axes and zero scale axes are representable by this basis.
        /// Explicit local shear is detected because one Unity Transform cannot
        /// represent it; the returned basis is an orthogonal approximation.
        /// </summary>
        internal static TransformBasis DecomposeBasis(float m00, float m01, float m02,
            float m10, float m11, float m12, float m20, float m21, float m22)
        {
            var x = new BasisVector(m00, m10, m20);
            var y = new BasisVector(m01, m11, m21);
            var z = new BasisVector(m02, m12, m22);
            double sx = x.Length, sy = y.Length, sz = z.Length;
            if (double.IsNaN(sx + sy + sz) || double.IsInfinity(sx + sy + sz) ||
                sx > float.MaxValue || sy > float.MaxValue || sz > float.MaxValue)
                // Use a CLR 2 core exception: KK's Unity/Mono profile cannot load
                // InvalidDataException, even in an untaken validation branch.
                throw new ArgumentException("Node transform contains an invalid or unrepresentable scale.");
            if (BasisVector.Dot(x, BasisVector.Cross(y, z)) < 0) sz = -sz;
            x = x.Normalized();
            y = y.Normalized();
            z = z.Normalized().Scaled(sz < 0 ? -1 : 1);
            bool shear = Math.Abs(BasisVector.Dot(x, y)) > 1e-5 ||
                         Math.Abs(BasisVector.Dot(x, z)) > 1e-5 ||
                         Math.Abs(BasisVector.Dot(y, z)) > 1e-5;

            BasisVector up, forward;
            if (sz != 0)
            {
                forward = z;
                if (sy != 0 && BasisVector.Cross(y, z).Length > 1e-10) up = y;
                else if (sx != 0 && BasisVector.Cross(z, x).Length > 1e-10) up = BasisVector.Cross(z, x).Normalized();
                else up = ReferenceAxis(forward);
            }
            else if (sx != 0 && sy != 0 && BasisVector.Cross(x, y).Length > 1e-10)
            {
                forward = BasisVector.Cross(x, y).Normalized();
                up = y;
            }
            else if (sy != 0)
            {
                up = y;
                forward = BasisVector.Cross(ReferenceAxis(up), up).Normalized();
            }
            else if (sx != 0)
            {
                forward = BasisVector.Cross(x, ReferenceAxis(x)).Normalized();
                up = BasisVector.Cross(forward, x).Normalized();
            }
            else
            {
                up = new BasisVector(0, 1, 0);
                forward = new BasisVector(0, 0, 1);
            }
            var right = BasisVector.Cross(up, forward).Normalized();
            up = BasisVector.Cross(forward, right).Normalized();
            return new TransformBasis
            {
                Scale = new BasisVector(sx, sy, sz), Right = right, Up = up, Forward = forward, HasShear = shear
            };
        }

        private static BasisVector ReferenceAxis(BasisVector direction)
        {
            return Math.Abs(direction.Y) < 0.9 ? new BasisVector(0, 1, 0) : new BasisVector(1, 0, 0);
        }

        /// <summary>
        /// Splits on triangle boundaries, duplicating shared vertices as necessary.
        /// Triangle order and winding are preserved, as are unreferenced vertices.
        /// </summary>
        internal static List<MeshPart> Partition(int vertexCount, IList<int> triangles, int maximumVertices = MaximumVertices)
        {
            if (vertexCount < 0) throw new ArgumentOutOfRangeException("vertexCount");
            if (triangles == null) throw new ArgumentNullException("triangles");
            if (maximumVertices < 3 || maximumVertices > MaximumVertices)
                throw new ArgumentOutOfRangeException("maximumVertices");
            if (triangles.Count % 3 != 0)
                throw new ArgumentException("Triangle indices must be supplied in triples.", "triangles");

            for (int i = 0; i < triangles.Count; i++)
                if (triangles[i] < 0 || triangles[i] >= vertexCount)
                    throw new ArgumentException("A triangle references a vertex outside the mesh.", "triangles");

            var parts = new List<MeshPart>();
            if (vertexCount <= maximumVertices)
            {
                var identity = new int[vertexCount];
                for (int i = 0; i < vertexCount; i++) identity[i] = i;
                var copiedTriangles = new int[triangles.Count];
                triangles.CopyTo(copiedTriangles, 0);
                parts.Add(new MeshPart(identity, copiedTriangles));
                return parts;
            }

            var sourceVertices = new List<int>();
            var localTriangles = new List<int>();
            var sourceToLocal = new Dictionary<int, int>();
            var referenced = new bool[vertexCount];

            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                int additional = sourceToLocal.ContainsKey(a) ? 0 : 1;
                if (b != a && !sourceToLocal.ContainsKey(b)) additional++;
                if (c != a && c != b && !sourceToLocal.ContainsKey(c)) additional++;

                if (sourceVertices.Count + additional > maximumVertices)
                {
                    parts.Add(new MeshPart(sourceVertices.ToArray(), localTriangles.ToArray()));
                    sourceVertices.Clear();
                    localTriangles.Clear();
                    sourceToLocal.Clear();
                }

                for (int j = 0; j < 3; j++)
                {
                    int source = triangles[i + j];
                    int local;
                    if (!sourceToLocal.TryGetValue(source, out local))
                    {
                        local = sourceVertices.Count;
                        sourceToLocal.Add(source, local);
                        sourceVertices.Add(source);
                    }
                    localTriangles.Add(local);
                    referenced[source] = true;
                }
            }

            // Retain vertices that are not used by a face, including vertex-only meshes.
            for (int source = 0; source < vertexCount; source++)
            {
                if (referenced[source]) continue;
                if (sourceVertices.Count == maximumVertices)
                {
                    parts.Add(new MeshPart(sourceVertices.ToArray(), localTriangles.ToArray()));
                    sourceVertices.Clear();
                    localTriangles.Clear();
                }
                sourceVertices.Add(source);
            }

            if (sourceVertices.Count > 0)
                parts.Add(new MeshPart(sourceVertices.ToArray(), localTriangles.ToArray()));
            return parts;
        }

        internal static T[] Remap<T>(IList<T> source, int[] sourceVertices)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (sourceVertices == null) throw new ArgumentNullException("sourceVertices");
            var result = new T[sourceVertices.Length];
            for (int i = 0; i < result.Length; i++) result[i] = source[sourceVertices[i]];
            return result;
        }

        internal static TResult[] RemapDeltas<TSource, TResult>(IList<TSource> basis, IList<TSource> target,
            int vertexCount, int[] sourceVertices, Func<TSource, TSource, TResult> subtract)
        {
            if (basis == null) throw new ArgumentNullException("basis");
            if (target == null) throw new ArgumentNullException("target");
            if (sourceVertices == null) throw new ArgumentNullException("sourceVertices");
            if (subtract == null) throw new ArgumentNullException("subtract");
            if (target.Count != 0 && (target.Count != vertexCount || basis.Count != vertexCount))
                throw new ArgumentException("BlendShape channel length does not match the base mesh.", "target");
            var result = new TResult[sourceVertices.Length];
            if (target.Count == 0) return result;
            for (int i = 0; i < result.Length; i++)
            {
                int source = sourceVertices[i];
                result[i] = subtract(target[source], basis[source]);
            }
            return result;
        }

        /// <summary>
        /// Unity 5.6 accepts at most four bone influences per vertex. Merge duplicate
        /// bone entries, retain the strongest influences, then normalize the result.
        /// </summary>
        internal static BoneInfluence[] LimitBoneInfluences(IList<BoneInfluence> influences)
        {
            if (influences == null) throw new ArgumentNullException("influences");
            var combined = new Dictionary<int, double>();
            for (int i = 0; i < influences.Count; i++)
            {
                BoneInfluence influence = influences[i];
                if (influence.BoneIndex < 0 || float.IsNaN(influence.Weight) || float.IsInfinity(influence.Weight) || influence.Weight < 0)
                    throw new ArgumentException("Bone influences must have a valid index and a finite, non-negative weight.", "influences");
                if (influence.Weight == 0) continue;
                double previous;
                combined.TryGetValue(influence.BoneIndex, out previous);
                combined[influence.BoneIndex] = previous + influence.Weight;
            }

            var sorted = new List<KeyValuePair<int, double>>(combined);
            sorted.Sort(delegate(KeyValuePair<int, double> a, KeyValuePair<int, double> b)
            {
                int weightOrder = b.Value.CompareTo(a.Value);
                return weightOrder != 0 ? weightOrder : a.Key.CompareTo(b.Key);
            });
            int count = Math.Min(4, sorted.Count);
            var result = new BoneInfluence[count];
            double total = 0;
            for (int i = 0; i < count; i++) total += sorted[i].Value;
            for (int i = 0; i < count; i++) result[i] = new BoneInfluence(sorted[i].Key, (float)(sorted[i].Value / total));
            return result;
        }
    }
}
