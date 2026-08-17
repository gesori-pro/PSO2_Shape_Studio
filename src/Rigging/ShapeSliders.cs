using System.Numerics;
using Pso2ShapeStudio.Formats;

namespace Pso2ShapeStudio.Rigging;

/// <summary>
/// A bone that follows a group's scale but not its rotation or position.
///
/// PSO2 flags most bones inherit-scale-none, so scaling a parent moves its
/// children without resizing them - measured on the reference skeleton, 106 of
/// 223 bones are flagged this way, including every finger. Rotation and
/// translation still pass down the hierarchy, which is why turning a hand
/// carries the fingers with it while enlarging one only inflates the palm.
/// The game solves this by listing each bone in its own slider table; a
/// follower is the same idea.
///
/// The multiplier is linear in the group's deviation from 1, with separate
/// slopes below and above it, because the game does not treat the two
/// directions alike: the wrist thins in step with the hand but thickens only
/// half as much.
/// </summary>
public sealed record ShapeScaleFollower(
    string LeftBone,
    string? RightBone,
    Vector3 WhenShrinking,
    Vector3 WhenGrowing)
{
    public ShapeScaleFollower(string leftBone, string? rightBone)
        : this(leftBone, rightBone, Vector3.One, Vector3.One)
    {
    }

    public Vector3 ScaleFor(Vector3 groupScale) => new(
        Axis(groupScale.X, WhenShrinking.X, WhenGrowing.X),
        Axis(groupScale.Y, WhenShrinking.Y, WhenGrowing.Y),
        Axis(groupScale.Z, WhenShrinking.Z, WhenGrowing.Z));

    private static float Axis(float value, float shrinking, float growing) =>
        1f + ((value - 1f) * (value < 1f ? shrinking : growing));
}

public sealed record ShapeGroupDefinition(
    string Key,
    string Label,
    string LeftBone,
    string? RightBone,
    IReadOnlyDictionary<string, int> NodeIds,
    bool SupportsRotation = true,
    bool ShowsRotation = true,
    bool SupportsPosition = true,
    bool SupportsScaleX = true,
    bool SupportsScaleY = true,
    bool SupportsScaleZ = true,
    double ScaleMinimum = 0.1,
    double ScaleMaximum = 4.0,
    double ScaleStep = 0.01,
    IReadOnlyList<ShapeScaleFollower>? ScaleFollowers = null)
{
    public IReadOnlyList<ShapeScaleFollower> Followers => ScaleFollowers ?? [];
}

public sealed record CustomShapeGroupDefinition(
    string Key,
    string Label,
    string LeftBone,
    string? RightBone);

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
        new ShapeGroupDefinition(
            "bodyroot",
            "Sole Height (body_root Y)",
            "body_root",
            null,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["body_root"] = 1 },
            SupportsRotation: false,
            ShowsRotation: false,
            SupportsPosition: false,
            SupportsScaleX: false,
            SupportsScaleY: true,
            SupportsScaleZ: false,
            ScaleMinimum: 0.5,
            ScaleMaximum: 1.2,
            ScaleStep: 0.001),
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
        HandGroup(),
    ];

    /// <summary>
    /// Hand size, matching the 43 bones the game's own hands slider drives:
    /// both palms, all 38 finger joints, the weapon offset, and the forearm
    /// twists. Scaling the palms alone only inflates the palms, because the
    /// fingers do not inherit scale - see <see cref="ShapeScaleFollower"/>.
    ///
    /// Measured from the slider table, every bone but the forearm takes the
    /// palm's multiplier unchanged. The forearm twist keeps its length (X) and
    /// follows on thickness only, fully when shrinking and at half when
    /// growing.
    /// </summary>
    private static ShapeGroupDefinition HandGroup()
    {
        // Kept local: static field initialisers run in declaration order, and
        // Groups above would read this one before it was assigned.
        (string Suffix, int Left, int Right)[] fingerBones =
        [
            ("00", 74, 99), ("01", 75, 100), ("02", 76, 101),
            ("10", 77, 102), ("11", 78, 103), ("12", 79, 104), ("13", 80, 105),
            ("20", 81, 106), ("21", 82, 107), ("22", 83, 108), ("23", 84, 109),
            ("30", 85, 110), ("31", 86, 111), ("32", 87, 112), ("33", 88, 113),
            ("40", 89, 114), ("41", 90, 115), ("42", 91, 116), ("43", 92, 117),
        ];
        var nodeIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["l_hand"] = 27,
            ["r_hand"] = 35,
            ["l_forearm_tw"] = 26,
            ["r_forearm_tw"] = 34,
            ["c_weapon_offset2"] = 38,
        };
        var followers = new List<ShapeScaleFollower>
        {
            new("c_weapon_offset2", null),
            new(
                "l_forearm_tw",
                "r_forearm_tw",
                WhenShrinking: new Vector3(0f, 1f, 1f),
                WhenGrowing: new Vector3(0f, 0.5f, 0.5f)),
        };
        foreach (var (suffix, left, right) in fingerBones)
        {
            var leftName = $"l_finger_{suffix}";
            var rightName = $"r_finger_{suffix}";
            nodeIds[leftName] = left;
            nodeIds[rightName] = right;
            followers.Add(new ShapeScaleFollower(leftName, rightName));
        }

        return new ShapeGroupDefinition(
            "hand",
            "Hand",
            "l_hand",
            "r_hand",
            nodeIds,
            ScaleFollowers: followers);
    }

    public static IReadOnlyList<ShapeGroupDefinition> ConfigureGroups(
        IEnumerable<string>? hiddenBuiltInKeys,
        IEnumerable<CustomShapeGroupDefinition>? customGroups,
        IReadOnlyDictionary<string, int>? skeletonNodeIds = null)
    {
        var hidden = hiddenBuiltInKeys?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var configured = Groups.Where(group => !hidden.Contains(group.Key)).ToList();
        var usedKeys = configured.Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var custom in customGroups ?? [])
        {
            if (string.IsNullOrWhiteSpace(custom.Key) ||
                string.IsNullOrWhiteSpace(custom.Label) ||
                string.IsNullOrWhiteSpace(custom.LeftBone) ||
                !usedKeys.Add(custom.Key))
            {
                continue;
            }

            var left = custom.LeftBone.Trim();
            var right = string.IsNullOrWhiteSpace(custom.RightBone) ? null : custom.RightBone.Trim();
            var nodeIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            AddNode(left);
            if (right is not null)
            {
                AddNode(right);
            }

            configured.Add(new ShapeGroupDefinition(
                custom.Key,
                custom.Label.Trim(),
                left,
                right,
                nodeIds));

            void AddNode(string name)
            {
                if (skeletonNodeIds?.TryGetValue(name, out var nodeId) == true)
                {
                    nodeIds[name] = nodeId;
                }
            }
        }

        return configured;
    }

    public static SkeletonPose Apply(
        AqnSkeleton skeleton,
        ShapeProfile profile,
        IReadOnlyList<ShapeGroupDefinition>? groups = null)
    {
        var composer = new BodyPoseComposer(skeleton);
        foreach (var group in groups ?? Groups)
        {
            ApplyGroup(composer, group, profile[group.Key]);
        }

        return composer.Build();
    }

    /// <summary>
    /// Writes one group's edit onto a composer: the paired bones take the full
    /// transform, and any followers take scale only. Both the viewport pose and
    /// the saved AQM go through here so a group cannot pick up followers in one
    /// path and lose them in the other.
    /// </summary>
    public static void ApplyGroup(
        BodyPoseComposer composer,
        ShapeGroupDefinition group,
        ShapeValue value)
    {
        if (value.IsIdentity)
        {
            return;
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

        foreach (var follower in group.Followers)
        {
            // Scale only: rotation and position already reach these bones
            // through the hierarchy, and applying them again would double up.
            var scale = follower.ScaleFor(value.Scale);
            ApplySide(composer, follower.LeftBone, scale, Vector3.Zero, Quaternion.Identity);
            if (follower.RightBone is not null)
            {
                ApplySide(composer, follower.RightBone, scale, Vector3.Zero, Quaternion.Identity);
            }
        }
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
