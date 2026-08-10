using System.Numerics;
using AquaModelLibrary.Data.PSO2.Aqua;

namespace Pso2ShapeStudio.Formats;

public sealed record SkeletonBone(
    int Index,
    string Name,
    int ParentIndex,
    ushort BoneFlags,
    Vector3 Position,
    Vector3 EulerDegrees,
    Vector3 Scale,
    Matrix4x4 InverseBind,
    Matrix4x4 WorldBind,
    Matrix4x4 LocalFromBind);

public sealed record TrsValidationFailure(int BoneIndex, string BoneName, float MaximumError);

public sealed record TrsValidationResult(
    int BoneCount,
    float MaximumError,
    IReadOnlyList<TrsValidationFailure> Failures);

public sealed class AqnSkeleton
{
    private AqnSkeleton(string sourcePath, IReadOnlyList<SkeletonBone> bones, int AuxiliaryNodeCount)
    {
        SourcePath = sourcePath;
        Bones = bones;
        this.AuxiliaryNodeCount = AuxiliaryNodeCount;
    }

    public string SourcePath { get; }

    public IReadOnlyList<SkeletonBone> Bones { get; }

    public int AuxiliaryNodeCount { get; }

    public static AqnSkeleton Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        return Load(File.ReadAllBytes(fullPath), fullPath);
    }

    /// <summary>
    /// Reads the skeleton, taking the bind matrices as authoritative rather
    /// than the per-bone pos/eulRot/scale the file also carries.
    /// </summary>
    /// <remarks>
    /// Those raw values are rounded: recomposing them (XZY euler order) misses
    /// the file's own bind matrix by more than 1e-4 on 7 of 223 bones in the
    /// reference model, worst case 1.55e-4. Deriving the local matrix from the
    /// bind matrices instead lands at 8.3e-7. The raw values are still exposed
    /// on <see cref="SkeletonBone"/> and checked by
    /// <see cref="ValidateSourceTrs"/>, which pins the known failures rather
    /// than quietly widening the tolerance.
    /// </remarks>
    public static AqnSkeleton Load(ReadOnlyMemory<byte> data, string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        var node = new AquaNode(data.ToArray());
        var inverseBind = node.nodeList
            .Select(value => value.GetInverseBindPoseMatrix())
            .ToArray();
        var world = node.nodeList
            .Select(value => value.GetInverseBindPoseMatrixInverted())
            .ToArray();
        if (world.Any(matrix => !IsFinite(matrix)))
        {
            throw new InvalidDataException("AQN contains a non-invertible bind matrix.");
        }

        var bones = new SkeletonBone[node.nodeList.Count];

        for (var index = 0; index < bones.Length; index++)
        {
            var value = node.nodeList[index];
            var local = world[index];
            if (value.parentId >= 0)
            {
                if (value.parentId >= world.Length)
                {
                    throw new InvalidDataException($"Invalid AQN parent for bone {index}: {value.parentId}");
                }

                // System.Numerics uses row vectors: local * parentWorld = world.
                // The parent's inverse bind is already stored in the file;
                // re-inverting its world matrix adds enough float error to
                // push a few bones over the 1e-4 validation gate.
                local = MultiplyUsingDouble(world[index], inverseBind[value.parentId]);
            }

            bones[index] = new SkeletonBone(
                index,
                value.boneName.GetString(),
                value.parentId,
                value.boneShort1,
                value.pos,
                value.eulRot,
                value.scale,
                inverseBind[index],
                world[index],
                local);
        }

        return new AqnSkeleton(sourceName, bones, node.nodoList.Count);
    }

    public TrsValidationResult ValidateSourceTrs(float tolerance = 1e-4f)
    {
        var failures = new List<TrsValidationFailure>();
        var maximum = 0f;

        foreach (var bone in Bones)
        {
            var built = ComposeSourceLocal(bone.Position, bone.EulerDegrees, bone.Scale);
            var error = MatrixMaximumDifference(bone.LocalFromBind, built);
            maximum = Math.Max(maximum, error);
            if (error >= tolerance)
            {
                failures.Add(new TrsValidationFailure(bone.Index, bone.Name, error));
            }
        }

        return new TrsValidationResult(Bones.Count, maximum, failures);
    }

    public static Matrix4x4 ComposeSourceLocal(Vector3 position, Vector3 eulerDegrees, Vector3 scale)
    {
        var radians = eulerDegrees * (MathF.PI / 180f);
        var rotation = Matrix4x4.CreateRotationX(radians.X) *
                       Matrix4x4.CreateRotationZ(radians.Z) *
                       Matrix4x4.CreateRotationY(radians.Y);
        return Matrix4x4.CreateScale(scale) * rotation * Matrix4x4.CreateTranslation(position);
    }

    public static float MatrixMaximumDifference(in Matrix4x4 left, in Matrix4x4 right)
    {
        return new[]
        {
            MathF.Abs(left.M11 - right.M11), MathF.Abs(left.M12 - right.M12),
            MathF.Abs(left.M13 - right.M13), MathF.Abs(left.M14 - right.M14),
            MathF.Abs(left.M21 - right.M21), MathF.Abs(left.M22 - right.M22),
            MathF.Abs(left.M23 - right.M23), MathF.Abs(left.M24 - right.M24),
            MathF.Abs(left.M31 - right.M31), MathF.Abs(left.M32 - right.M32),
            MathF.Abs(left.M33 - right.M33), MathF.Abs(left.M34 - right.M34),
            MathF.Abs(left.M41 - right.M41), MathF.Abs(left.M42 - right.M42),
            MathF.Abs(left.M43 - right.M43), MathF.Abs(left.M44 - right.M44),
        }.Max();
    }

    private static bool IsFinite(in Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) &&
        float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14) &&
        float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) &&
        float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24) &&
        float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) &&
        float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34) &&
        float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) &&
        float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);

    private static Matrix4x4 MultiplyUsingDouble(in Matrix4x4 left, in Matrix4x4 right)
    {
        var a = ToDoubleArray(left);
        var b = ToDoubleArray(right);
        var product = new double[4, 4];
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                for (var inner = 0; inner < 4; inner++)
                {
                    product[row, column] += a[row, inner] * b[inner, column];
                }
            }
        }

        return FromDoubleArray(product);
    }

    private static double[,] ToDoubleArray(in Matrix4x4 matrix) => new[,]
    {
        { (double)matrix.M11, matrix.M12, matrix.M13, matrix.M14 },
        { (double)matrix.M21, matrix.M22, matrix.M23, matrix.M24 },
        { (double)matrix.M31, matrix.M32, matrix.M33, matrix.M34 },
        { (double)matrix.M41, matrix.M42, matrix.M43, matrix.M44 },
    };

    private static Matrix4x4 FromDoubleArray(double[,] matrix) => new(
        (float)matrix[0, 0], (float)matrix[0, 1], (float)matrix[0, 2], (float)matrix[0, 3],
        (float)matrix[1, 0], (float)matrix[1, 1], (float)matrix[1, 2], (float)matrix[1, 3],
        (float)matrix[2, 0], (float)matrix[2, 1], (float)matrix[2, 2], (float)matrix[2, 3],
        (float)matrix[3, 0], (float)matrix[3, 1], (float)matrix[3, 2], (float)matrix[3, 3]);
}
