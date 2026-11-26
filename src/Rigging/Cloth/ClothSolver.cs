using System.Numerics;
using static Pso2ShapeStudio.Rigging.Cloth.ClothMath;

namespace Pso2ShapeStudio.Rigging.Cloth;

internal sealed class SolverState(Vector3[] positions, Vector3[] previous, float previousDt, ulong sleepMask)
{
    public Vector3[] Positions { get; } = positions;
    public Vector3[] Previous { get; } = previous;
    public float PreviousDt { get; } = previousDt;
    public ulong SleepMask { get; } = sleepMask;
}

/// <summary>
/// One version 31..33 dispatcher step (29048F0): forces, integration,
/// attachment, two constraint passes around the target pull, collisions in
/// the second. Ported from blender_pso2_tools' native_cloth_solver33,
/// native_cloth_stages and native_cloth_angles.
/// </summary>
internal static class ClothSolver
{
    public sealed class StepInputs
    {
        public required Vector3[] Targets { get; init; }
        public required Vector3[] Basis0 { get; init; }
        public required Vector3[] Basis1 { get; init; }
        public required float[] CollisionRadii { get; init; }
        public required PlacedCollider?[] Colliders { get; init; }
        public required int CollisionPrefix { get; init; }
        public required float Dt { get; init; }
        public required float StopReciprocal { get; init; }
        public required Vector3 Gravity { get; init; }
    }

    public static SolverState Step(ClothPartDefinition d, DistanceConstraint[] constraints, SolverState state, StepInputs input)
    {
        var flags = d.Flags;
        var p = (Vector3[])state.Positions.Clone();
        var n = p.Length;
        var previous = state.Previous;
        var mass = d.InverseMass;
        var newDt = input.Dt;
        var inverseDt = 1f / newDt;
        var force = new Vector3[n];
        if ((flags & 1) != 0)
        {
            SpringForces(force, p, previous, constraints, inverseDt);
        }

        for (var i = 0; i < n; i++)
        {
            force[i] += input.Gravity * d.ForceWeights1[i] * d.ForceWeights0[i];
        }

        if ((flags & 2) != 0)
        {
            VelocityDrag(force, p, previous, constraints, d, inverseDt);
        }

        // 2907570: Verlet-like step with the previous timestep's ratio.
        var oldDt = state.PreviousDt;
        var factor = (oldDt + newDt) * 0.5f * newDt;
        var integrated = new Vector3[n];
        for (var i = 0; i < n; i++)
        {
            var displacement = force[i] * (factor * mass[i]);
            if (oldDt >= Small)
            {
                displacement += (p[i] - previous[i]) * (1f / oldDt * newDt);
            }

            integrated[i] = p[i] + displacement;
        }

        var history = p;
        var output = integrated;
        Attach(output, input.Targets, mass, input.Basis0, input.Basis1, flags);
        var iterations = (flags & 0x1000) != 0 ? 1 : (flags & 0x800) != 0 ? d.AlternateIterations : d.NormalIterations;
        var collision = new CollisionInputs
        {
            InverseMass = mass,
            Radii = input.CollisionRadii,
            Weights0 = d.CollisionWeights0,
            Weights1 = d.CollisionWeights1,
            Constraints = constraints,
            History = history,
            SleepMask = state.SleepMask,
        };
        output = Iterate(output, d, constraints, iterations, input, false, collision);
        PullTargets(output, input.Targets, (flags & 0x10) != 0 ? d.TargetWeights0 : d.TargetWeights1, newDt);
        output = Iterate(output, d, constraints, iterations, input, true, collision);

        var sleep = 0UL;
        for (var i = 0; i < n; i++)
        {
            var delta = history[i] - output[i];
            if (Dot(delta, delta) < Small) sleep |= 1UL << (i & 63);
        }

        return new SolverState(output, history, newDt, sleep);
    }

    private static Vector3[] Iterate(
        Vector3[] positions, ClothPartDefinition d, DistanceConstraint[] constraints, int iterations,
        StepInputs input, bool postCollision, CollisionInputs collision)
    {
        var flags = d.Flags;
        var output = positions;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var scratch = (Vector3[])output.Clone();
            ConstrainDistances(output, d.InverseMass, constraints, (flags & 8) == 0, flags, postCollision);
            var (basis0, _) = ConstrainAngles(output, input.Targets, input.Basis0, input.Basis1, d.InverseMass,
                d.TargetAngular, d.Angular, postCollision && (flags & 0x400) != 0, (flags & 0x2000) != 0,
                d.SolverVersion == 32);
            if (postCollision && input.Colliders.Length > 0)
            {
                var before = (Vector3[])output.Clone();
                Collide(output, input.Colliders.Take(input.CollisionPrefix), collision);
                if ((flags & 0x80) != 0)
                {
                    Reflect(output, before, basis0, input.Basis1, d.PackedNormalModes);
                }

                Collide(output, input.Colliders.Skip(input.CollisionPrefix), collision);
            }

            var moving = false;
            for (var i = 0; i < output.Length && !moving; i++)
            {
                var v = output[i] - scratch[i];
                moving = Dot(v, v) * input.StopReciprocal * input.StopReciprocal >= 1f;
            }

            if (!moving) break;
        }

        return output;
    }

    private static void Collide(Vector3[] p, IEnumerable<PlacedCollider?> colliders, CollisionInputs inputs)
    {
        foreach (var collider in colliders)
        {
            if (collider is not null && (collider.Flags & 0x11) == 1)
            {
                collider.Apply(p, inputs);
            }
        }
    }

    /// <summary>Flag 0x80: per-particle half-space reflection against a basis normal.</summary>
    private static void Reflect(Vector3[] p, Vector3[] before, Vector3[] scratch0, Vector3[] basis1, byte[] modes)
    {
        for (var i = 0; i < p.Length; i++)
        {
            var nibble = modes[i / 2] >> (4 * (i & 1)) & 0xF;
            Vector3 normal;
            switch (nibble)
            {
                case 1: normal = scratch0[i]; break;
                case 2: normal = -scratch0[i]; break;
                case 3: normal = basis1[i]; break;
                case 4: normal = -basis1[i]; break;
                default: continue;
            }

            var projection = Dot(p[i] - before[i], normal);
            if (projection < 0) p[i] -= normal * (projection + projection);
        }
    }

    /// <summary>28B5A00: spring and damping force along stiff edges.</summary>
    private static void SpringForces(Vector3[] force, Vector3[] p, Vector3[] h, DistanceConstraint[] constraints, float inverseDt)
    {
        foreach (var r in constraints)
        {
            if ((r.Flags & 0x10) != 0 || (r.Flags & 4) == 0) continue;
            int a = r.Parent, b = r.Child;
            var delta = p[b] - p[a];
            var length2 = Dot(delta, delta);
            var value = Vector3.Zero;
            if (length2 >= Small)
            {
                var seed = Rsqrt(length2);
                var direction = delta * seed;
                var relative = (p[b] - h[b]) * inverseDt - (p[a] - h[a]) * inverseDt;
                value = direction * ((seed * length2 - r.RestLength) * r.Stiffness + Dot(direction, relative) * r.Damping);
            }

            force[a] += value;
            force[b] -= value;
        }
    }

    /// <summary>28F2CA0: drag perpendicular to each edge, capped per solver version.</summary>
    private static void VelocityDrag(Vector3[] force, Vector3[] p, Vector3[] h, DistanceConstraint[] constraints,
        ClothPartDefinition d, float inverseDt)
    {
        if (d.Damping <= 0 && d.AirResistance <= 0) return;
        float? uniformCap = d.SolverVersion < 33 ? 30f * d.MassCoefficient : null;
        foreach (var r in constraints)
        {
            if ((r.Flags & 0x10) != 0) continue;
            int a = r.Parent, b = r.Child;
            var cap = uniformCap ?? (d.ForceWeights0[a] + d.ForceWeights0[b]) * 0.5f * 30f;
            var damping = MathF.Min(d.Damping, cap);
            var resistance = MathF.Min(d.AirResistance, cap);
            var delta = p[b] - p[a];
            var averageVelocity = (p[b] - h[b] + (p[a] - h[a])) * (inverseDt * 0.5f);
            // Wind and the environment's relative velocity are zero in the preview.
            var velocity = averageVelocity * resistance + Vector3.Zero * damping;
            var length2 = Dot(delta, delta);
            if (length2 < Small) continue;
            var projection = delta * (Dot(velocity, delta) / length2);
            var value = (velocity - projection) * -0.5f;
            force[a] += value;
            force[b] += value;
        }
    }

    /// <summary>28F3640: pin immovable particles, remove forbidden axis motion.</summary>
    private static void Attach(Vector3[] p, Vector3[] t, float[] mass, Vector3[] b0, Vector3[] b1, int flags)
    {
        for (var i = 0; i < p.Length; i++)
        {
            if (mass[i] <= 0 || (flags & 0x60) == 0)
            {
                p[i] = t[i];
            }
            else if ((flags & 0x60) != 0x60)
            {
                var axis = (flags & 0x20) == 0 ? b0[i] : b1[i];
                var norm = Dot(axis, axis);
                if (norm > 0)
                {
                    p[i] -= axis * (Dot(p[i] - t[i], axis) / norm);
                }
            }
        }
    }

    /// <summary>28F3DC0: pull towards the animated targets after the first pass.</summary>
    private static void PullTargets(Vector3[] p, Vector3[] t, float[] weights, float dt)
    {
        for (var i = 0; i < p.Length; i++)
        {
            var weight = weights[i];
            if (weight <= 0) continue;
            var factor = weight * weight * dt * 29.999998f * dt * 29.999998f;
            if (factor > 0)
            {
                var scale = MathF.Min(1f, 1f / (1f / factor + 1f));
                p[i] += (t[i] - p[i]) * scale;
            }
        }
    }

    /// <summary>The distance section of 29034A0/28FE440: equal split between free endpoints.</summary>
    private static void ConstrainDistances(Vector3[] p, float[] mass, DistanceConstraint[] constraints,
        bool constrainAll, int flags, bool postCollision)
    {
        foreach (var r in constraints)
        {
            if ((r.Flags & 0x10) != 0) continue;
            int a = r.Parent, b = r.Child;
            var parentFree = mass[a] > 0;
            if (postCollision && (flags & 0x400) != 0 && (r.Flags & 0x20) == 0) parentFree = false;
            if (!parentFree && mass[b] <= 0) continue;
            float rest = r.RestLength, low = r.MinimumRatio, high = r.MaximumRatio;
            float lowLength = low * rest, highLength = high * rest;
            var delta = p[b] - p[a];
            var length2 = Dot(delta, delta);
            var over = highLength * highLength < length2;
            var exact = constrainAll || (r.Flags & 1) != 0;
            if (!exact && !over && lowLength * lowLength <= length2) continue;
            if (length2 < Small) continue;
            var wanted = exact ? rest : (over ? high : low) * rest;
            var correction = delta - delta * (Rsqrt(length2) * wanted);
            var half = correction * 0.5f;
            var childDelta = -half;
            var parentDelta = half;
            if (!parentFree)
            {
                childDelta *= 2;
                parentDelta = Vector3.Zero;
            }

            if (mass[b] <= 0)
            {
                parentDelta *= 2;
                childDelta = Vector3.Zero;
            }

            p[a] += parentDelta;
            p[b] += childDelta;
        }
    }

    /// <summary>
    /// Target-angle then three-particle-angle records (28F70F0 per record).
    /// With basis propagation (0x2000) each record reads its reference's
    /// basis and turns it onto the corrected edge into its parent's slot.
    /// </summary>
    private static (Vector3[] Basis0, Vector3[] Basis1) ConstrainAngles(
        Vector3[] p, Vector3[] target, Vector3[] basis0, Vector3[] basis1, float[] mass,
        AngularConstraint[] targetRecords, AngularConstraint[] angularRecords,
        bool pinParent, bool propagate, bool halfUnit)
    {
        var y = propagate ? (Vector3[])basis0.Clone() : basis0;
        var z = propagate ? (Vector3[])basis1.Clone() : basis1;

        (Vector3, Vector3) Bases(int source, AngularConstraint record) =>
            halfUnit && (record.Flags & 2) == 0 ? (HalfUnit(y[source]), HalfUnit(z[source])) : (y[source], z[source]);

        void Turn(Vector3 by, Vector3 bz, int slot, Vector3 parent, Vector3 child)
        {
            if (!propagate) return;
            var x = Cross(by, bz);
            var edge = child - parent;
            if (IsSmall(x) || IsSmall(edge)) return;
            var q = Arc(Vector3.Normalize(x), Vector3.Normalize(edge));
            if (q is null) return;
            y[slot] = Rotate(q.Value, by);
            z[slot] = Rotate(q.Value, bz);
        }

        foreach (var r in targetRecords)
        {
            int a = r.Parent, b = r.Child;
            var reference = r.TargetRelative ? target[a] - target[b] + p[a] : p[a] + p[a] - target[b];
            var (by, bz) = Bases(a, r);
            // The parent goes in as a copy and is not written back.
            var (parent, child) = ConstrainAngle(p[a], p[b], reference, by, bz, mass[a], mass[b], r, pinParent);
            p[b] = child;
            Turn(by, bz, a, parent, p[b]);
        }

        foreach (var r in angularRecords)
        {
            int a = r.Parent, b = r.Child;
            var (by, bz) = Bases(propagate ? r.Reference : a, r);
            (p[a], p[b]) = ConstrainAngle(p[a], p[b], p[r.Reference], by, bz, mass[a], mass[b], r, pinParent);
            Turn(by, bz, a, p[a], p[b]);
        }

        return (y, z);
    }

    private static Vector3 HalfUnit(Vector3 b)
    {
        var d = Dot(b, b);
        return d > 0.25f ? b / MathF.Sqrt(d) * 0.5f : b;
    }

    /// <summary>28F70F0: an elliptical limit (2D capsule/box bound) or the circular cone.</summary>
    private static (Vector3 Parent, Vector3 Child) ConstrainAngle(
        Vector3 a, Vector3 b, Vector3 reference, Vector3 y, Vector3 z, float pm, float cm, AngularConstraint r, bool pinParent)
    {
        float c0 = r.Cosine0, s0 = r.Sine0, c1 = r.Cosine1, s1 = r.Sine1;
        if (c0 <= 0) return (a, b);
        if (MathF.Abs(c1) < Small || MathF.Abs(c0 - c1) < Small)
        {
            return Cone(a, b, reference, y, z, pm, cm, c0, s0, r, pinParent);
        }

        if (c1 <= 0 || s0 <= 0 || s1 <= 0) return (a, b);
        var edge = b - a;
        var axis = r.Scale * r.Offset0 * y + (a - reference) + r.Scale * r.Offset1 * z;
        var side0 = Cross(axis, z);
        var side1 = Cross(side0, axis);
        if (!IsSmall(side0)) side0 *= Rsqrt(Dot(side0, side0));
        if (!IsSmall(side1)) side1 *= Rsqrt(Dot(side1, side1));
        float dot = Dot(axis, edge), edge2 = Dot(edge, edge), axis2 = Dot(axis, axis);
        if (dot < 0)
        {
            // Behind the axis: the smaller cone pair goes to the circular leaf.
            if (c1 < c0) (c0, s0) = (c1, s1);
            return Cone(a, b, reference, y, z, pm, cm, c0, s0, r, pinParent);
        }

        if (axis2 <= 0 || edge2 <= 0) return (a, b);
        var perpendicular = edge - axis * (dot / axis2);
        if (Dot(perpendicular, perpendicular) <= 0) return (a, b);
        var axisDistance = MathF.Abs(Dot(edge, axis * Rsqrt(axis2)));
        var radius0 = MathF.Abs(s0 / c0 * axisDistance);
        var radius1 = MathF.Abs(s1 / c1 * axisDistance);
        var diff = MathF.Abs(radius0 - radius1);
        float xMajor = radius0 >= radius1 ? 1 : 0, yMajor = radius0 < radius1 ? 1 : 0;
        float dx = xMajor * diff, dy = yMajor * diff;
        var segmentX = xMajor * -diff - dx;
        var segmentY = yMajor * -diff - dy;
        var point = new Vector2(Dot(perpendicular, side0), Dot(perpendicular, side1));
        var selected = Vector2.Zero;
        if (segmentX * segmentX + segmentY * segmentY < Small || (r.Flags & 1) != 0)
        {
            if (point.X < -radius0 || radius0 <= point.X || point.Y < -radius1 || radius1 <= point.Y)
            {
                selected = new Vector2(Math.Clamp(point.X, -radius0, radius0), Math.Clamp(point.Y, -radius1, radius1));
            }
        }
        else
        {
            var capsuleRadius = MathF.Max(0f, MathF.Min(radius0, radius1));
            if (!CapsuleInside(dx, dy, segmentX, segmentY, capsuleRadius, point))
            {
                selected = CapsuleBoundary(dx, dy, segmentX, segmentY, capsuleRadius, point);
            }
        }

        var norm = selected.X * selected.X + selected.Y * selected.Y;
        if (norm <= 0) return (a, b);
        var axial = Rsqrt(axis2) * axisDistance;
        var desired = axis * axial + side0 * selected.X + side1 * selected.Y;
        var inverse = Rsqrt(1f / edge2 * Dot(desired, desired));
        var newChild = desired * inverse + a;
        var half = (newChild - b) * 0.5f;
        Vector3 changeA = -half, changeB = half;
        if (pinParent || pm <= 0 || norm < Small)
        {
            changeB *= 2;
            changeA = Vector3.Zero;
        }

        if (cm <= 0)
        {
            changeA *= 2;
            changeB = Vector3.Zero;
        }

        return (a + changeA, b + changeB);
    }

    private static bool CapsuleInside(float x, float y, float dx, float dy, float radius, Vector2 point)
    {
        var norm = dx * dx + dy * dy;
        var along = 0f;
        if (norm >= Small) along = Clamp01(((point.Y - y) * dy + (point.X - x) * dx) / norm);
        var a = point.X - (dx * along + x);
        var b = point.Y - (dy * along + y);
        return b * b + a * a <= radius * radius;
    }

    private static Vector2 CapsuleBoundary(float x, float y, float dx, float dy, float radius, Vector2 point)
    {
        var norm = dx * dx + dy * dy;
        var along = 0f;
        if (norm >= Small) along = Clamp01(((point.X - x) * dx + (point.Y - y) * dy) / norm);
        float nearX = dx * along + x, nearY = dy * along + y;
        float a = point.X - nearX, b = point.Y - nearY;
        var length2 = b * b + a * a;
        if (length2 >= Small)
        {
            var inverse = Rsqrt(length2);
            a *= inverse;
            b *= inverse;
        }

        return new Vector2(a * radius + nearX, b * radius + nearY);
    }

    /// <summary>28F5A50: the circular cone around the (offset) reference axis.</summary>
    private static (Vector3, Vector3) Cone(
        Vector3 a, Vector3 b, Vector3 reference, Vector3 y, Vector3 z, float pm, float cm,
        float cos, float sin, AngularConstraint r, bool pinParent)
    {
        if (cos <= 0) return (a, b);
        var edge = b - a;
        var edge2 = Dot(edge, edge);
        var axis = y * (r.Scale * r.Offset0) + (a - reference) + z * (r.Scale * r.Offset1);
        var dot = Dot(axis, edge);
        var axis2 = Dot(axis, axis);
        var cross = Cross(edge, axis);
        var outside = dot < 0 || sin * sin * axis2 * edge2 < Dot(cross, cross);
        if (!outside || edge2 <= 0 || axis2 <= 0) return (a, b);
        var perpendicular = edge - axis * (dot / axis2);
        var perp2 = Dot(perpendicular, perpendicular);
        if (perp2 <= 0) return (a, b);
        var inverseEdge2 = 1f / edge2;
        var desired = axis * (Rsqrt(inverseEdge2 * axis2) * cos) + perpendicular * (Rsqrt(perp2 * inverseEdge2) * sin);
        var half = (desired - edge) * 0.5f;
        Vector3 changeA = -half, changeB = half;
        if (pinParent || pm <= 0)
        {
            changeB *= 2;
            changeA = Vector3.Zero;
        }

        if (cm <= 0)
        {
            changeA *= 2;
            changeB = Vector3.Zero;
        }

        return (a + changeA, b + changeB);
    }
}
