using System.Numerics;
using Pso2ShapeStudio.Formats;

namespace Pso2ShapeStudio.Rigging;

public sealed record ShapeGroupDefinition(
    string Key,
    string Label,
    string LeftBone,
    string? RightBone,
    IReadOnlyDictionary<string, int> NodeIds,
    bool SupportsRotation = true);

public readonly record struct ShapeValue(Vector3 Scale, Vector3 Position, Vector3 EulerDegrees)
{
    public static ShapeValue Identity { get; } = new(Vector3.One, Vector3.Zero, Vector3.Zero);

    public bool IsIdentity =>
        Vector3.DistanceSquared(Scale, Vector3.One) < 1e-14f &&
        Position.LengthSquared() < 1e-14f &&
        EulerDegrees.LengthSquared() < 1e-14f;
}

public sealed class ShapeProfile
{
    private readonly Dictionary<string, ShapeValue> _values = new(StringComparer.OrdinalIgnoreCase);

    public ShapeValue this[string key]
    {
        get => _values.GetValueOrDefault(key, ShapeValue.Identity);
        set => _values[key] = value;
    }

    public void Reset() => _values.Clear();

    public ShapeProfile Clone()
    {
        var clone = new ShapeProfile();
        foreach (var (key, value) in _values)
        {
            clone._values.Add(key, value);
        }

        return clone;
    }

    public bool ValueEquals(ShapeProfile? other)
    {
        if (other is null)
        {
            return false;
        }

        var keys = _values.Keys.Concat(other._values.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        return keys.All(key => this[key] == other[key]);
    }
}

public static class ShapeSliders
{
    public static IReadOnlyList<ShapeGroupDefinition> Groups { get; } =
    [
        Group("breast", "Breast", "l_breast", "r_breast", ("l_breast", 41), ("r_breast", 43)),
        Group("breast2", "Breast Scale", "l_breast_scale", "r_breast_scale", ("l_breast_scale", 124), ("r_breast_scale", 125)),
        Group("cbreast2", "Center Breast Scale", "c_breast_scale", null, ("c_breast_scale", 130)),
        Group("clav", "Clavicle", "l_clavicle", "r_clavicle", false, ("l_clavicle", 22), ("r_clavicle", 30)),
        Group("waist", "Waist", "spine1_2", null, ("spine1_2", 132)),
        Group("hip", "Hip", "hip", null, ("hip", 2)),
        Group("pelvis", "Pelvis", "pelvis", null, ("pelvis", 3)),
        Group("hiptw", "Hip Twist", "l_hip_tw", "r_hip_tw", ("l_hip_tw", 50), ("r_hip_tw", 51)),
        Group("thigh", "Thigh", "l_thigh_alt", "r_thigh_alt", ("l_thigh_alt", 52), ("r_thigh_alt", 63)),
        Group("thightw", "Thigh Twist", "l_thigh_tw_alt", "r_thigh_tw_alt", ("l_thigh_tw_alt", 53), ("r_thigh_tw_alt", 64)),
        Group("thightw2", "Thigh Twist 2", "l_thigh_tw2_alt", "r_thigh_tw2_alt", ("l_thigh_tw2_alt", 54), ("r_thigh_tw2_alt", 65)),
        Group("calf0", "Calf Upper", "l_calf0_alt", "r_calf0_alt", ("l_calf0_alt", 55), ("r_calf0_alt", 66)),
        Group("calf", "Calf", "l_calf_alt", "r_calf_alt", ("l_calf_alt", 56), ("r_calf_alt", 67)),
        Group("foot", "Foot", "l_foot_alt", "r_foot_alt", ("l_foot_alt", 57), ("r_foot_alt", 68)),
    ];

    public static SkeletonPose Apply(AqnSkeleton skeleton, ShapeProfile profile)
    {
        var composer = new BodyPoseComposer(skeleton);
        foreach (var group in Groups)
        {
            var value = profile[group.Key];
            if (value.IsIdentity)
            {
                continue;
            }

            var quaternion = EulerDegreesToQuaternion(value.EulerDegrees);
            ApplySide(composer, group.LeftBone, value.Scale, value.Position, quaternion);
            if (group.RightBone is not null)
            {
                ApplySide(
                    composer,
                    group.RightBone,
                    value.Scale,
                    MirrorPosition(value.Position),
                    MirrorQuaternion(quaternion));
            }
        }

        return composer.Build();
    }

    public static Quaternion EulerDegreesToQuaternion(Vector3 degrees)
    {
        var radians = degrees * (MathF.PI / 180f);
        var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, radians.X);
        var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians.Y);
        var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radians.Z);
        return Quaternion.Normalize(qz * qy * qx);
    }

    public static Vector3 MirrorPosition(Vector3 value) => new(value.X, -value.Y, value.Z);

    public static Quaternion MirrorQuaternion(Quaternion value) => new(-value.X, value.Y, -value.Z, value.W);

    private static void ApplySide(
        BodyPoseComposer composer,
        string boneName,
        Vector3 scale,
        Vector3 position,
        Quaternion rotation)
    {
        composer.SetShape(boneName, new BoneDelta(scale, position, rotation));
    }

    private static ShapeGroupDefinition Group(
        string key,
        string label,
        string left,
        string? right,
        params (string Name, int Id)[] nodes) =>
        Group(key, label, left, right, true, nodes);

    private static ShapeGroupDefinition Group(
        string key,
        string label,
        string left,
        string? right,
        bool supportsRotation,
        params (string Name, int Id)[] nodes) =>
        new(key, label, left, right, nodes.ToDictionary(node => node.Name, node => node.Id), supportsRotation);
}
