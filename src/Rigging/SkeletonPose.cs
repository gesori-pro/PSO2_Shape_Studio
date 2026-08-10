using System.Numerics;
using Pso2ShapeStudio.Formats;

namespace Pso2ShapeStudio.Rigging;

public readonly record struct BoneDelta(Vector3 Scale, Vector3 Position, Quaternion Rotation)
{
    public static BoneDelta Identity { get; } = new(Vector3.One, Vector3.Zero, Quaternion.Identity);

    public bool IsIdentity =>
        Vector3.DistanceSquared(Scale, Vector3.One) < 1e-14f &&
        Position.LengthSquared() < 1e-14f &&
        MathF.Abs(Quaternion.Dot(Quaternion.Normalize(Rotation), Quaternion.Identity)) > 1f - 1e-7f;
}

public sealed class SkeletonPose
{
    private readonly AqnSkeleton _skeleton;
    private readonly BoneDelta[] _deltas;
    private readonly Matrix4x4[] _world;
    private readonly Matrix4x4[] _skin;

    public SkeletonPose(AqnSkeleton skeleton)
    {
        _skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
        _deltas = Enumerable.Repeat(BoneDelta.Identity, skeleton.Bones.Count).ToArray();
        _world = new Matrix4x4[skeleton.Bones.Count];
        _skin = new Matrix4x4[skeleton.Bones.Count];
        Rebuild();
    }

    public IReadOnlyList<Matrix4x4> WorldMatrices => _world;

    public IReadOnlyList<Matrix4x4> SkinMatrices => _skin;

    public void Reset()
    {
        Array.Fill(_deltas, BoneDelta.Identity);
        Rebuild();
    }

    public void SetDelta(int boneIndex, BoneDelta delta)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(boneIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(boneIndex, _deltas.Length);
        _deltas[boneIndex] = delta with { Rotation = Quaternion.Normalize(delta.Rotation) };
    }

    public void SetDelta(string boneName, BoneDelta delta)
    {
        var index = _skeleton.Bones
            .FirstOrDefault(bone => string.Equals(bone.Name, boneName, StringComparison.OrdinalIgnoreCase))?.Index ?? -1;
        if (index < 0)
        {
            throw new KeyNotFoundException($"Skeleton has no bone named '{boneName}'.");
        }

        SetDelta(index, delta);
    }

    public void Rebuild()
    {
        if (_deltas.All(delta => delta.IsIdentity))
        {
            for (var index = 0; index < _skeleton.Bones.Count; index++)
            {
                var bone = _skeleton.Bones[index];
                _world[index] = bone.WorldBind;
                _skin[index] = Matrix4x4.Identity;
            }

            return;
        }

        var affected = new bool[_skeleton.Bones.Count];
        for (var index = 0; index < _skeleton.Bones.Count; index++)
        {
            var bone = _skeleton.Bones[index];
            var delta = _deltas[index];
            affected[index] = !delta.IsIdentity ||
                              (bone.ParentIndex >= 0 && affected[bone.ParentIndex]);
            if (!affected[index])
            {
                _world[index] = bone.WorldBind;
                _skin[index] = Matrix4x4.Identity;
                continue;
            }

            var channel = ComposeDelta(delta);

            if (bone.ParentIndex < 0)
            {
                _world[index] = channel * bone.LocalFromBind;
            }
            else
            {
                var parentWorld = _world[bone.ParentIndex];
                if ((bone.BoneFlags & 0x1C0) != 0)
                {
                    // Blender's INHERIT_SCALE_NONE (the mode used by the
                    // verified Python importer) has two parent transforms:
                    // rotation/scale ignores parent scale and shear, while
                    // location still uses the complete parent pose.  Using
                    // one scale-stripped matrix for both keeps child bones at
                    // bind length and badly distorts body proportions.
                    var rotationScaleBasis = bone.LocalFromBind * WithoutScale(parentWorld);
                    var locationBasis = bone.LocalFromBind * parentWorld;
                    var world = channel * rotationScaleBasis;
                    var location = Vector3.Transform(
                        new Vector3(channel.M41, channel.M42, channel.M43),
                        locationBasis);
                    world.M41 = location.X;
                    world.M42 = location.Y;
                    world.M43 = location.Z;
                    _world[index] = world;
                }
                else
                {
                    _world[index] = channel * bone.LocalFromBind * parentWorld;
                }
            }

            _skin[index] = bone.InverseBind * _world[index];
        }
    }

    private static Matrix4x4 ComposeDelta(in BoneDelta delta)
    {
        return Matrix4x4.CreateScale(delta.Scale) *
               Matrix4x4.CreateFromQuaternion(delta.Rotation) *
               Matrix4x4.CreateTranslation(delta.Position);
    }

    private static Matrix4x4 WithoutScale(in Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out _, out var rotation, out var translation))
        {
            throw new InvalidDataException("Could not remove inherited scale from a posed bone matrix.");
        }

        return Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation)) *
               Matrix4x4.CreateTranslation(translation);
    }
}

public static class CpuSkinning
{
    public static Vector3 TransformPosition(
        Vector3 position,
        Vector4 weights,
        Byte4 paletteIndices,
        IReadOnlyList<int> palette,
        IReadOnlyList<Matrix4x4> skinMatrices)
    {
        var transformed = Vector3.Zero;
        Accumulate(paletteIndices.X, weights.X);
        Accumulate(paletteIndices.Y, weights.Y);
        Accumulate(paletteIndices.Z, weights.Z);
        Accumulate(paletteIndices.W, weights.W);
        return transformed;

        void Accumulate(byte paletteIndex, float weight)
        {
            if (weight == 0f)
            {
                return;
            }

            if (paletteIndex >= palette.Count)
            {
                throw new InvalidDataException($"Palette index {paletteIndex} exceeds palette size {palette.Count}.");
            }

            var boneIndex = palette[paletteIndex];
            if ((uint)boneIndex >= skinMatrices.Count)
            {
                throw new InvalidDataException($"Bone index {boneIndex} exceeds skeleton size {skinMatrices.Count}.");
            }

            transformed += Vector3.Transform(position, skinMatrices[boneIndex]) * weight;
        }
    }
}
