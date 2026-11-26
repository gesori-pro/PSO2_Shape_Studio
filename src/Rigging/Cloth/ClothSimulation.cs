using System.Numerics;
using Pso2ShapeStudio.Formats;
using static Pso2ShapeStudio.Rigging.Cloth.ClothMath;

namespace Pso2ShapeStudio.Rigging.Cloth;

/// <summary>
/// The outfit's FLTD cloth settled on a still pose: what the character
/// creator shows once the physics has come to rest. Ported from
/// blender_pso2_tools' FileClothPreview33 with its 30 settling steps,
/// using the game's own gravity and a fixed 30 FPS timestep.
/// </summary>
public sealed class ClothSimulation
{
    public const int DefaultSettleSteps = 30;
    public const float Fps = 30f;
    private static readonly Vector3 Gravity = new(0f, -9.81f, 0f);

    private readonly AqnSkeleton _skeleton;
    private readonly IReadOnlyList<ClothPart> _parts;
    private readonly Matrix4x4[][] _references;

    private ClothSimulation(AqnSkeleton skeleton, IReadOnlyList<ClothPart> parts)
    {
        _skeleton = skeleton;
        _parts = parts;
        // 2908F20: each part's type-2 modifiers turn bones of the shared
        // reference model before its rest lengths are measured, cumulatively.
        var current = skeleton.Bones.Select(bone => bone.WorldBind).ToArray();
        _references = new Matrix4x4[parts.Count][];
        for (var i = 0; i < parts.Count; i++)
        {
            current = OverrideReference(current, parts[i]);
            _references[i] = current;
        }

        DisabledColliders = parts.Sum(part => part.Colliders.Count(c => c.Disabled));
        UnsupportedColliders = parts.Sum(part => part.Colliders.Count(c => !c.Disabled && c.Kind is 6 or 8));
    }

    public int PartCount => _parts.Count;

    public int ParticleCount => _parts.Sum(part => part.Mapping.Length);

    /// <summary>Colliders naming bones this skeleton lacks: the game switches those off too.</summary>
    public int DisabledColliders { get; }

    /// <summary>Sector and box colliders, which this preview does not model.</summary>
    public int UnsupportedColliders { get; }

    /// <summary>
    /// The cloth for this skeleton, or null with a reason when the file has
    /// no cloth or uses something the preview does not support.
    /// </summary>
    public static ClothSimulation? TryCreate(byte[] fltd, AqnSkeleton skeleton, out string? problem)
    {
        problem = null;
        try
        {
            if (!FltdReader.DeclaresCloth(fltd))
            {
                return null;
            }

            var names = skeleton.Bones.Select(bone => bone.Name).ToArray();
            var parents = skeleton.Bones.Select(bone => bone.ParentIndex).ToArray();
            var parts = FltdReader.ReadParts(fltd, names, parents);
            return parts.Count == 0 ? null : new ClothSimulation(skeleton, parts);
        }
        catch (InvalidDataException error)
        {
            problem = error.Message;
            return null;
        }
    }

    /// <summary>
    /// Settles the cloth on <paramref name="pose"/> (game world rows, one per
    /// bone) and returns the pose with the cloth bones replaced.
    /// </summary>
    public Matrix4x4[] Settle(IReadOnlyList<Matrix4x4> pose, int steps = DefaultSettleSteps)
    {
        var world = pose.ToArray();
        var drivers = _parts.Select((part, i) => new PartDriver(part, _references[i], world)).ToArray();
        var output = Evaluate(drivers, world, step: false);
        for (var i = 0; i < steps; i++)
        {
            output = Evaluate(drivers, world, step: true);
        }

        return output;
    }

    /// <summary>
    /// Settles the cloth in <paramref name="physicsPose"/> (the creator's
    /// idle) and carries each cloth bone's settled local transform over to
    /// <paramref name="shown"/>, so the shown pose gets the cloth as it hangs
    /// in that pose, relative to the bones it is attached to.
    /// </summary>
    public Matrix4x4[] Settle(IReadOnlyList<Matrix4x4> shown, IReadOnlyList<Matrix4x4> physicsPose, int steps = DefaultSettleSteps)
    {
        var settled = Settle(physicsPose, steps);
        var cloth = _parts.SelectMany(part => part.Mapping).ToHashSet();
        var output = shown.ToArray();
        foreach (var bone in _skeleton.Bones)
        {
            if (!cloth.Contains(bone.Index) || bone.ParentIndex < 0) continue;
            Matrix4x4.Invert(settled[bone.ParentIndex], out var inverseParent);
            output[bone.Index] = settled[bone.Index] * inverseParent * output[bone.ParentIndex];
        }

        return output;
    }

    private Matrix4x4[] Evaluate(PartDriver[] drivers, Matrix4x4[] world, bool step)
    {
        var output = (Matrix4x4[])world.Clone();
        var written = new HashSet<int>();
        var reposed = false;
        for (var i = 0; i < drivers.Length; i++)
        {
            var part = _parts[i];
            var source = world;
            if (part.FollowsEarlier)
            {
                // Wrapper 0x8000: the model is re-posed after the earlier
                // parts' writeback and this part reads that pose.
                if (!reposed)
                {
                    output = Repose(world, output, written);
                    reposed = true;
                }

                source = output;
            }

            var changed = drivers[i].Evaluate(source, step);
            foreach (var node in part.Mapping)
            {
                output[node] = changed[node];
                written.Add(node);
            }
        }

        return output;
    }

    /// <summary>54C690 stand-in: bones below a written one follow it with their pre-physics local transform.</summary>
    private Matrix4x4[] Repose(Matrix4x4[] world, Matrix4x4[] pose, HashSet<int> written)
    {
        var output = (Matrix4x4[])pose.Clone();
        var moved = new HashSet<int>(written);
        foreach (var bone in _skeleton.Bones)
        {
            if (written.Contains(bone.Index) || bone.ParentIndex < 0 || !moved.Contains(bone.ParentIndex)) continue;
            Matrix4x4.Invert(world[bone.ParentIndex], out var inverseParent);
            output[bone.Index] = world[bone.Index] * inverseParent * output[bone.ParentIndex];
            moved.Add(bone.Index);
        }

        return output;
    }

    /// <summary>
    /// 2979150: a type-2 modifier turns its particle's bone in its own frame
    /// (M @ local); bones below it keep their reference local transforms.
    /// </summary>
    private Matrix4x4[] OverrideReference(Matrix4x4[] reference, ClothPart part)
    {
        var overrides = part.Definition.ReferenceOverrides;
        if (overrides.Length == 0)
        {
            return reference;
        }

        var turned = new Dictionary<int, Matrix4x4>();
        foreach (var entry in overrides)
        {
            var node = part.Mapping[entry.Particle];
            var radians = entry.AnglesDegrees * (MathF.PI / 180f);
            var rotation = Matrix4x4.CreateFromQuaternion(EulerRows(radians));
            rotation.M41 = entry.Position.X;
            rotation.M42 = entry.Position.Y;
            rotation.M43 = entry.Position.Z;
            turned[node] = rotation * turned.GetValueOrDefault(node, Matrix4x4.Identity);
        }

        var output = (Matrix4x4[])reference.Clone();
        var moved = new HashSet<int>();
        foreach (var bone in _skeleton.Bones)
        {
            var parent = bone.ParentIndex;
            Matrix4x4 local = Matrix4x4.Identity;
            if (parent >= 0)
            {
                Matrix4x4.Invert(reference[parent], out var inverseParent);
                local = reference[bone.Index] * inverseParent;
            }

            if (turned.TryGetValue(bone.Index, out var turn))
            {
                output[bone.Index] = turn * (parent >= 0 ? local * output[parent] : output[bone.Index]);
                moved.Add(bone.Index);
            }
            else if (parent >= 0 && moved.Contains(parent))
            {
                output[bone.Index] = local * output[parent];
                moved.Add(bone.Index);
            }
        }

        return output;
    }

    /// <summary>B70B10's quaternion: half-angle sines and cosines combined X, Y, Z.</summary>
    private static Quaternion EulerRows(Vector3 radians)
    {
        float sx = MathF.Sin(radians.X / 2), cx = MathF.Cos(radians.X / 2);
        float sy = MathF.Sin(radians.Y / 2), cy = MathF.Cos(radians.Y / 2);
        float sz = MathF.Sin(radians.Z / 2), cz = MathF.Cos(radians.Z / 2);
        var first = new Vector4(cy * sx * cz, cy * sx * -sz, sy * sx * -cz, cy * cx * cz);
        var second = new Vector4(sy * cx * sz, sy * cx * cz, cy * cx * sz, sy * sx * sz);
        var q = first + second;
        return Quaternion.Normalize(new Quaternion(q.X, q.Y, q.Z, q.W));
    }

    /// <summary>One part's solver state across the settling steps (FileClothDriver33).</summary>
    private sealed class PartDriver
    {
        private readonly ClothPart _part;
        private readonly DistanceConstraint[] _constraints;
        private SolverState _state;

        public PartDriver(ClothPart part, Matrix4x4[] reference, Matrix4x4[] current)
        {
            _part = part;
            var map = part.Mapping;
            // 28F3750: every edge length from the reference pose.
            _constraints = part.Definition.Constraints
                .Select(c => c with { RestLength = Vector3.Distance(Translation(reference[map[c.Parent]]), Translation(reference[map[c.Child]])) })
                .ToArray();
            var start = map.Select(node => Translation(current[node])).ToArray();
            _state = new SolverState(start, (Vector3[])start.Clone(), 0f, 0);
        }

        public Matrix4x4[] Evaluate(Matrix4x4[] world, bool step)
        {
            var d = _part.Definition;
            var map = _part.Mapping;
            var n = map.Length;
            var targets = new Vector3[n];
            var axes = new[] { new Vector3[n], new Vector3[n], new Vector3[n] };
            for (var i = 0; i < n; i++)
            {
                var m = world[map[i]];
                targets[i] = Translation(m);
                for (var k = 0; k < 3; k++) axes[k][i] = Row(m, k);
            }

            if (step)
            {
                // 2908A60: pins move before forces; radii follow the bone scale.
                var positions = (Vector3[])_state.Positions.Clone();
                var radii = (float[])d.CollisionRadii.Clone();
                for (var i = 0; i < n; i++)
                {
                    if ((d.SkipMask >> (i & 63) & 1) != 0) positions[i] = targets[i];
                    var flags = d.ParticleFlags[i];
                    if ((flags & 7) == 0) continue;
                    float sum = 0;
                    var count = 0;
                    for (var k = 0; k < 3; k++)
                    {
                        if ((flags & (1u << k)) == 0) continue;
                        sum += axes[k][i].Length();
                        count++;
                    }

                    radii[i] = sum / count * d.CollisionRadii[i];
                }

                var prepared = new SolverState(positions, _state.Previous, _state.PreviousDt, _state.SleepMask);
                if ((d.WrapperFlags & 0x20) != 0)
                {
                    // Wrapper bypass: particles follow their targets.
                    _state = new SolverState((Vector3[])targets.Clone(), (Vector3[])targets.Clone(), prepared.PreviousDt, prepared.SleepMask);
                }
                else
                {
                    var dt = 1f / Fps;
                    _state = ClothSolver.Step(d, _constraints, prepared, new ClothSolver.StepInputs
                    {
                        Targets = targets,
                        Basis0 = axes[1],
                        Basis1 = axes[2],
                        CollisionRadii = radii,
                        Colliders = _part.Colliders.Select(c => ColliderFactory.Place(c, world)).ToArray(),
                        CollisionPrefix = _part.CollisionPrefixCount,
                        Dt = dt,
                        StopReciprocal = 1f / dt * d.ConvergenceMultiplier,
                        Gravity = Gravity,
                    });
                }
            }

            var output = (Matrix4x4[])world.Clone();
            ClothWriteback.Apply(output, _part, _state.Positions, targets, axes);
            return output;
        }
    }
}
