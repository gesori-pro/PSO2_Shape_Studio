using System.Numerics;
using Pso2ShapeStudio.Formats;

namespace Pso2ShapeStudio.Rigging;

/// <summary>
/// Composes PSO2 character proportions and an outfit shape-adjust motion in
/// the same two pose-channel layers used by blender_pso2_tools.
/// </summary>
public sealed class BodyPoseComposer
{
    private readonly AqnSkeleton _skeleton;
    private readonly Dictionary<string, BoneDelta> _proportions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BoneDelta> _shape = new(StringComparer.OrdinalIgnoreCase);

    public BodyPoseComposer(AqnSkeleton skeleton)
    {
        _skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
    }

    public void SetProportion(string boneName, BoneDelta value) =>
        _proportions[boneName] = Normalize(value);

    public void SetShape(string boneName, BoneDelta value) =>
        _shape[boneName] = Normalize(value);

    public SkeletonPose Build()
    {
        var pose = new SkeletonPose(_skeleton);
        foreach (var bone in _skeleton.Bones)
        {
            var proportion = _proportions.GetValueOrDefault(bone.Name, BoneDelta.Identity);
            var shape = _shape.GetValueOrDefault(bone.Name, BoneDelta.Identity);
            if (proportion.IsIdentity && shape.IsIdentity)
            {
                continue;
            }

            var restRotation = GetRestRotation(bone);
            var proportionRotation = Quaternion.Normalize(
                Quaternion.Inverse(restRotation) * proportion.Rotation * restRotation);
            var position = ToBoneLocal(proportion.Position, restRotation) +
                           ToBoneLocal(shape.Position, restRotation);
            pose.SetDelta(
                bone.Index,
                new BoneDelta(
                    proportion.Scale * shape.Scale,
                    position,
                    Quaternion.Normalize(proportionRotation * shape.Rotation)));
        }

        pose.Rebuild();
        return pose;
    }

    private static BoneDelta Normalize(BoneDelta value) =>
        value with { Rotation = Quaternion.Normalize(value.Rotation) };

    private static Vector3 ToBoneLocal(Vector3 position, Quaternion restRotation) =>
        Vector3.Transform(position, Quaternion.Inverse(restRotation));

    private static Quaternion GetRestRotation(SkeletonBone bone)
    {
        if (!Matrix4x4.Decompose(bone.LocalFromBind, out _, out var rotation, out _))
        {
            throw new InvalidDataException($"Could not decompose rest transform for bone '{bone.Name}'.");
        }

        return Quaternion.Normalize(rotation);
    }
}
