using System.Numerics;

namespace Pso2ShapeStudio.Rigging.Cloth;

internal sealed record DistanceConstraint(
    int Parent,
    int Child,
    float RestLength,
    float MinimumRatio,
    float MaximumRatio,
    int Flags,
    float Stiffness,
    float Damping,
    float ContactStart,
    float ContactEnd);

internal sealed record AngularConstraint(
    float Cosine0,
    float Sine0,
    float Offset0,
    float Offset1,
    float Cosine1,
    float Sine1,
    float Scale,
    int Flags,
    int Parent,
    int Child,
    int Reference,
    bool TargetRelative);

/// <summary>A type-2 modifier: turns a particle's bone in the reference pose.</summary>
internal sealed record ReferenceOverride(int Particle, Vector3 Position, Vector3 AnglesDegrees);

/// <summary>One FLTD part as its group initializes it (28F4440/29090D0).</summary>
internal sealed class ClothPartDefinition
{
    public required int PartIndex { get; init; }
    public required int ParticleCount { get; init; }
    public required int MirrorSign { get; init; }
    public required float[] InverseMass { get; init; }
    public required DistanceConstraint[] Constraints { get; init; }
    public required float[] ForceWeights0 { get; init; }
    public required float[] ForceWeights1 { get; init; }
    public required float[] TargetWeights0 { get; init; }
    public required float[] TargetWeights1 { get; init; }
    public required int NormalIterations { get; init; }
    public required int AlternateIterations { get; init; }
    public required float Damping { get; init; }
    public required float AirResistance { get; init; }
    public required int Flags { get; init; }
    public required int SolverVersion { get; init; }
    public required float MassCoefficient { get; init; }
    public required AngularConstraint[] Angular { get; init; }
    public required AngularConstraint[] TargetAngular { get; init; }
    public required float[] CollisionRadii { get; init; }
    public required float[] CollisionWeights0 { get; init; }
    public required float[] CollisionWeights1 { get; init; }
    public required byte[] PackedNormalModes { get; init; }
    public required uint[] ParticleFlags { get; init; }
    public required ulong UnitMask { get; init; }
    public required ulong UniformMask { get; init; }
    public required ulong OrthogonalMask { get; init; }
    public required ulong ExclusionMask { get; init; }
    public required ulong SkipMask { get; init; }
    public required int WrapperFlags { get; init; }
    public required float ConvergenceMultiplier { get; init; }
    public required ReferenceOverride[] ReferenceOverrides { get; init; }
}

/// <summary>
/// A collider bone: a node of this skeleton, or null when the name is not in
/// it. The game then registers the shape with node -1 and switches it off.
/// </summary>
internal sealed record FileCollider(
    int Kind,
    int?[] Bindings,
    int Flags,
    float Blend,
    float[] Values,
    byte[] ParticleFilter,
    bool[] Mirror,
    (float Rate, float Cap)? Growth)
{
    /// <summary>Binding of the character's top node: the model origin.</summary>
    public const int CharacterTop = -2;

    public bool Disabled => Bindings.Any(binding => binding is null);
}

internal sealed record ClothPart(
    ClothPartDefinition Definition,
    int[] Mapping,
    int[][] Records,
    FileCollider[] Colliders,
    int CollisionPrefixCount,
    bool FollowsEarlier);
