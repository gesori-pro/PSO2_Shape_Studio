using System.Numerics;
using static Pso2ShapeStudio.Rigging.Cloth.ClothMath;

namespace Pso2ShapeStudio.Rigging.Cloth;

/// <summary>What a collision pass needs besides the collider itself.</summary>
internal sealed class CollisionInputs
{
    public required float[] InverseMass { get; init; }
    public required float[] Radii { get; init; }
    public required float[] Weights0 { get; init; }
    public required float[] Weights1 { get; init; }
    public required DistanceConstraint[] Constraints { get; init; }
    public required Vector3[] History { get; init; }
    public required ulong SleepMask { get; init; }
}

/// <summary>A collider placed on this frame's bones.</summary>
internal abstract class PlacedCollider
{
    protected PlacedCollider(int flags, byte[] filter)
    {
        Flags = flags;
        Filter = filter;
    }

    public int Flags { get; }

    public byte[] Filter { get; }

    public abstract void Apply(Vector3[] p, CollisionInputs inputs);

    protected IEnumerable<int> Order(int count) =>
        Filter.Length > 0 ? Filter.Select(i => (int)i) : Enumerable.Range(0, count);

    /// <summary>
    /// The springs an edge collider visits (28CF110): all of them, or with
    /// shape flag 8 the filter list read as pairs, each taking the first
    /// spring that joins them.
    /// </summary>
    protected IEnumerable<(int A, int B, DistanceConstraint Spring)> EdgeItems(DistanceConstraint[] constraints)
    {
        if ((Flags & 8) == 0)
        {
            foreach (var r in constraints) yield return (r.Parent, r.Child, r);
            yield break;
        }

        for (var k = 0; k + 1 < Filter.Length; k += 2)
        {
            int a = Filter[k], b = Filter[k + 1];
            var r = constraints.FirstOrDefault(c => (c.Parent == a && c.Child == b) || (c.Parent == b && c.Child == a));
            if (r is not null) yield return (a, b, r);
        }
    }
}

internal static class ColliderFactory
{
    private const float ScaleBlendMax = 0.9900152683258057f;

    /// <summary>
    /// 28CCCF0 per bound slot, then the runtime shape (28BBE60). Null when the
    /// shape is switched off or of a kind this preview does not model.
    /// </summary>
    public static PlacedCollider? Place(FileCollider source, IReadOnlyList<Matrix4x4> world)
    {
        if (source.Disabled || source.Kind is 6 or 8)
        {
            return null;
        }

        var n = source.Bindings.Length;
        var transforms = new Matrix4x4[n];
        var inverses = new Matrix4x4[n];
        var moved = new float[n];
        var flags = source.Flags;
        for (var slot = 0; slot < n; slot++)
        {
            var node = source.Bindings[slot]!.Value;
            var incoming = node == FileCollider.CharacterTop ? Matrix4x4.Identity : world[node];
            moved[slot] = Translation(incoming).Length();
            flags |= 0x1000;
            transforms[slot] = PrepareTransform(incoming, flags, source.Blend);
            inverses[slot] = (flags & 2) != 0 ? InverseBasis(transforms[slot]) : Matrix4x4.Identity;
        }

        var v = source.Values;
        var filter = source.ParticleFilter;
        switch (source.Kind)
        {
            case 5:
                return new PlaneCollider(transforms[0], new Vector3(v[0], v[1], v[2]), flags, v[3], v[4], filter);
            case 2:
            case 3:
            {
                var origin = new Vector3(v[0], v[1], v[2]);
                var axis = new Vector3(v[3], v[4], v[5]);
                return new SegmentCollider(transforms[0], inverses[0], origin, axis, v[6], flags, filter,
                    inner: source.Kind == 3);
            }
            case 1:
                return new SphereCollider(transforms[0], inverses[0], new Vector3(v[0], v[1], v[2]), MathF.Max(v[3], 0f),
                    flags, filter, inner: true);
            case 0:
                return new SphereCollider(transforms[0], inverses[0], new Vector3(v[0], v[1], v[2]), MathF.Max(v[3], 0f),
                    flags, filter, inner: false);
            case 7:
            {
                float r0 = v[1], r1 = v[2];
                if ((flags & 4) != 0 && source.Growth is { } growth)
                {
                    r0 = Grow(r0, moved[0], growth);
                    r1 = Grow(r1, moved[1], growth);
                }

                return new CapsuleCollider(transforms, inverses[0],
                    new Vector3(v[5], v[6], v[7]), new Vector3(v[8], v[9], v[10]), new Vector3(v[11], v[12], v[13]),
                    r0, r1, v[3], v[4], v[0], flags, filter);
            }
            default:
                return null;
        }
    }

    /// <summary>28E5032: each end radius times min(max(rate * moved + 1, 1), cap).</summary>
    private static float Grow(float radius, float moved, (float Rate, float Cap) growth) =>
        Math.Min(Math.Max(growth.Rate * moved + 1f, 1f), growth.Cap) * radius;

    /// <summary>28CCCF0: local-mode axes evened out and blended toward unit length.</summary>
    private static Matrix4x4 PrepareTransform(Matrix4x4 incoming, int flags, float blend)
    {
        var output = incoming;
        var alpha = Math.Clamp(blend, 0f, 1f);
        if ((flags & 2) == 0)
        {
            return output;
        }

        if ((flags & 0x380) != 0)
        {
            var inverseLengths = new float[3];
            for (var i = 0; i < 3; i++)
            {
                var row = Row(output, i);
                inverseLengths[i] = Rsqrt(Dot(row, row));
            }

            var selected = new[] { 0x80, 0x100, 0x200 }.Select((bit, i) => (bit, i)).Where(x => (flags & x.bit) != 0).ToArray();
            var summed = selected.Sum(x => inverseLengths[x.i]);
            var common = MathF.Abs(summed) >= Small ? 1f / summed * selected.Length : summed;
            for (var i = 0; i < 3; i++)
            {
                SetRow(ref output, i, Row(output, i) * (common * inverseLengths[i]));
            }
        }

        if (alpha >= Small && alpha <= ScaleBlendMax &&
            MathF.Abs(Dot(Row(output, 0), Cross(Row(output, 2), Row(output, 1)))) >= Small)
        {
            var residual = 1f - alpha;
            for (var i = 0; i < 3; i++)
            {
                var row = Row(output, i);
                SetRow(ref output, i, row * (residual * Rsqrt(Dot(row, row)) + alpha));
            }
        }

        return output;
    }
}

/// <summary>Runtime tag 5, 28D9760: a floor at the transformed origin with friction.</summary>
internal sealed class PlaneCollider(Matrix4x4 transform, Vector3 origin, int flags, float frictionSleeping, float frictionMoving, byte[] filter)
    : PlacedCollider(flags, filter)
{
    public override void Apply(Vector3[] p, CollisionInputs inputs)
    {
        var local = (Flags & 2) != 0;
        var plane = Point(origin, local ? Matrix4x4.Identity : transform);
        var planeHere = local ? Point(plane, transform) : plane;
        foreach (var i in Order(p.Length))
        {
            if (inputs.InverseMass[i] <= 0) continue;
            var beforeY = p[i].Y;
            var depth = beforeY - planeHere.Y - inputs.Radii[i];
            if (depth >= 0) continue;
            var moved = p[i] - new Vector3(0, depth, 0);
            p[i] = moved;
            var coefficient = (inputs.SleepMask >> (i & 63) & 1) != 0
                ? inputs.Weights1[i] * frictionSleeping
                : inputs.Weights0[i] * frictionMoving;
            var limit = coefficient * depth;
            var limit2 = limit * limit;
            if (limit2 < Small) continue;
            var reference = inputs.History[i];
            var gap = reference.Y - beforeY;
            if (gap >= Small)
            {
                reference = (moved - reference) * ((gap + depth) / gap) + reference;
            }

            var tangent = reference - moved;
            tangent.Y = 0;
            if (IsSmall(tangent)) continue;
            var length2 = Dot(tangent, tangent);
            var minimum = MathF.Min(length2, limit2);
            p[i] = tangent * (Rsqrt(minimum * length2) * minimum) + moved;
        }
    }
}

/// <summary>
/// Tags 0 and 1: a sphere on one bone. Tag 0 pushes particles out (point
/// 28D0530 or edge 28CF110 with flag 4); tag 1 keeps them inside (28D0FB0).
/// </summary>
internal sealed class SphereCollider(
    Matrix4x4 transform, Matrix4x4 inverse, Vector3 origin, float radius, int flags, byte[] filter, bool inner)
    : PlacedCollider(flags, filter)
{
    public override void Apply(Vector3[] p, CollisionInputs inputs)
    {
        var local = (Flags & 2) != 0;
        var center = Point(origin, local ? Matrix4x4.Identity : transform);
        var into = local ? inverse : Matrix4x4.Identity;
        var outOf = local ? transform : Matrix4x4.Identity;
        if (!inner && (Flags & 4) != 0)
        {
            ApplyEdges(p, inputs, center, into, outOf);
            return;
        }

        foreach (var i in Order(p.Length))
        {
            var point = Point(p[i], into);
            var delta = point - center;
            var distance2 = Dot(delta, delta);
            if (inner)
            {
                if (!(inputs.InverseMass[i] > 0) || radius * radius > distance2 || !(distance2 >= Small)) continue;
                p[i] = Point(delta * (Rsqrt(distance2) * radius) + center, outOf);
                continue;
            }

            var r = MathF.Max(radius + inputs.Radii[i], 0f);
            if (!(inputs.InverseMass[i] >= Small) || !(r * r > distance2) || !(distance2 >= Small)) continue;
            p[i] = Point(delta * (Rsqrt(distance2) * r) + center, outOf);
        }
    }

    private void ApplyEdges(Vector3[] p, CollisionInputs inputs, Vector3 center, Matrix4x4 into, Matrix4x4 outOf)
    {
        var mass = inputs.InverseMass;
        var radii = inputs.Radii;
        foreach (var (a, b, r) in EdgeItems(inputs.Constraints))
        {
            bool freeA = mass[a] > 0, freeB = mass[b] > 0;
            var pa = Point(p[a], into);
            var pb = Point(p[b], into);
            if ((r.Flags & 8) == 0)
            {
                if (!freeA && Inside(pa)) continue;
                if (!freeB && Inside(pb)) continue;
            }

            if (!freeA && !freeB) continue;
            float start = r.ContactStart, end = r.ContactEnd;
            var edge = pb - pa;
            var ca = edge * start + pa;
            var cb = edge * end + pa;
            var direction = cb - ca;
            var norm = Dot(direction, direction);
            var along = norm >= Small ? Clamp01(Dot(center - ca, direction) / norm) : 0f;
            var delta = direction * along + ca - center;
            var ra = (1 - start) * radii[a] + start * radii[b];
            var rb = (1 - end) * radii[a] + end * radii[b];
            var effective = ra * (1 - along) + rb * along + radius;
            var distance2 = Dot(delta, delta);
            if (distance2 >= effective * effective || distance2 < Small) continue;
            var world = Direction(delta * (Rsqrt(distance2) * effective - 1), outOf);
            if (freeA) p[a] += world;
            if (freeB) p[b] += world;
        }

        bool Inside(Vector3 point)
        {
            var d = point - center;
            return Dot(d, d) < radius * radius;
        }
    }
}

/// <summary>
/// Tags 2 and 3: a capsule on one bone (origin, axis, radius). Tag 2 pushes
/// out (point 28D6DA0, edge 28D51F0); tag 3 keeps particles inside (28D78B0).
/// </summary>
internal sealed class SegmentCollider(
    Matrix4x4 transform, Matrix4x4 inverse, Vector3 origin, Vector3 axis, float radius, int flags, byte[] filter, bool inner)
    : PlacedCollider(flags, filter)
{
    public override void Apply(Vector3[] p, CollisionInputs inputs)
    {
        var local = (Flags & 2) != 0;
        var frame = local ? Matrix4x4.Identity : transform;
        var o = Point(origin, frame);
        var a = Direction(axis, frame);
        var into = local ? inverse : Matrix4x4.Identity;
        var outOf = local ? transform : Matrix4x4.Identity;
        var norm = Dot(a, a);
        if (!inner && (Flags & 4) != 0)
        {
            ApplyEdges(p, inputs, o, a, into, outOf);
            return;
        }

        foreach (var i in Order(p.Length))
        {
            var r = inner ? radius : radius + inputs.Radii[i];
            var movable = inner ? inputs.InverseMass[i] > 0 : inputs.InverseMass[i] >= Small;
            if (!movable || !(norm >= Small)) continue;
            var offset = Point(p[i], into) - o;
            var along = a * Clamp01(Dot(offset, a) / norm);
            var perpendicular = offset - along;
            var q = Dot(perpendicular, perpendicular);
            var violates = inner ? r * r < q : r * r > q;
            if (!violates || !(q >= Small)) continue;
            p[i] = Point(perpendicular * (Rsqrt(q) * r) + along + o, outOf);
        }
    }

    private void ApplyEdges(Vector3[] p, CollisionInputs inputs, Vector3 o, Vector3 a, Matrix4x4 into, Matrix4x4 outOf)
    {
        var mass = inputs.InverseMass;
        var norm = Dot(a, a);
        foreach (var (i, j, r) in EdgeItems(inputs.Constraints))
        {
            bool freeA = mass[i] > 0, freeB = mass[j] > 0;
            var pa = Point(p[i], into);
            var pb = Point(p[j], into);
            if ((r.Flags & 8) == 0)
            {
                if (!freeA && Inside(pa)) continue;
                if (!freeB && Inside(pb)) continue;
            }

            if (!freeA && !freeB) continue;
            float start = r.ContactStart, end = r.ContactEnd;
            float ra = inputs.Radii[i], rb = inputs.Radii[j];
            var edge = pb - pa;
            var ca = edge * start + pa;
            var cb = edge * end + pa;
            var direction = cb - ca;
            var size0 = (1 - start) * ra + start * rb;
            var size1 = (1 - end) * ra + rb * end;
            if (MathF.Abs(ra) < Small && MathF.Abs(rb) < Small)
            {
                var (s, t) = Contact.ClosestSegment(ca, direction, o, a);
                var delta = direction * s + ca - (a * t + o);
                var effective = (1 - s) * size0 + size1 * s + radius;
                var q = Dot(delta, delta);
                if (!(effective * effective > q) || !(q >= Small)) continue;
                Push(i, j, delta * (Rsqrt(q) * effective - 1), freeA, freeB);
                continue;
            }

            var length2 = Dot(edge, edge);
            var (s1, t1) = Contact.ClosestSegmentEps(ca, direction, o, a);
            var first = direction * s1 + ca - (a * t1 + o);
            var depth1 = (1 - s1) * size0 + s1 * size1 + radius - MaskedSqrt(Dot(first, first));
            var depth2 = 0f;
            var second = Vector3.Zero;
            if (length2 >= Small)
            {
                var w = o - pa;
                var perpendicular = w - edge * Dot(edge, w);
                var p2 = Dot(perpendicular, perpendicular);
                if (p2 >= Small)
                {
                    var normal = perpendicular * Rsqrt(p2);
                    var q1 = normal * size0 + ca;
                    var q2 = normal * size1 + cb;
                    var band = q2 - q1;
                    var (s2, t2) = Contact.ClosestSegmentEps(q1, band, o, a);
                    second = band * s2 + q1 - (a * t2 + o);
                    depth2 = radius - MaskedSqrt(Dot(second, second));
                }
            }

            if (!(depth1 >= Small || depth2 >= Small)) continue;
            var chosen = depth2 > depth1 ? second : first;
            var qq = Dot(chosen, chosen);
            if (!(qq >= Small)) continue;
            var depth = depth1 < depth2 ? depth2 : depth1;
            Push(i, j, chosen * (Rsqrt(qq) * depth), freeA, freeB);
        }

        bool Inside(Vector3 point)
        {
            if (!(norm >= Small)) return false;
            var offset = point - o;
            var d = offset - a * Clamp01(Dot(offset, a) / norm);
            return radius * radius > Dot(d, d);
        }

        void Push(int x, int y, Vector3 correction, bool freeX, bool freeY)
        {
            var world = Direction(correction, outOf);
            if (freeX) p[x] += world;
            if (freeY) p[y] += world;
        }
    }
}

/// <summary>
/// Tag 7: a tapered capsule between two bones. Point response 28E6600,
/// edge response 28E4CD0 (flag 4).
/// </summary>
internal sealed class CapsuleCollider(
    Matrix4x4[] transforms, Matrix4x4 attachmentInverse, Vector3 endpoint0, Vector3 endpoint1, Vector3 scale,
    float radius0, float radius1, float contactStart, float contactEnd, float selector, int flags, byte[] filter)
    : PlacedCollider(flags, filter)
{
    public override void Apply(Vector3[] p, CollisionInputs inputs)
    {
        if ((Flags & 4) != 0)
        {
            ApplyEdges(p, inputs);
        }
        else
        {
            ApplyPoints(p, inputs);
        }
    }

    private static float Lerp(float a, float b, float t) => (1 - t) * a + t * b;

    private static Matrix4x4 BasisMatrix(Vector3 x, Vector3 y, Vector3 z, Vector3 t)
    {
        var m = Matrix4x4.Identity;
        SetRow(ref m, 0, x);
        SetRow(ref m, 1, y);
        SetRow(ref m, 2, z);
        SetRow(ref m, 3, t);
        return m;
    }

    private void ApplyEdges(Vector3[] p, CollisionInputs inputs)
    {
        Vector3 translation;
        Vector3[] axes;
        if ((Flags & 2) != 0)
        {
            translation = Translation(transforms[0]);
            var source = transforms[selector >= 0.5f ? 1 : 0];
            axes = [Row(source, 0), Row(source, 1), Row(source, 2)];
        }
        else
        {
            translation = Vector3.Zero;
            axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        }

        var basis = BasisMatrix(axes[0] * scale.X, axes[1] * scale.Y, axes[2] * scale.Z, translation);
        var inverse = InverseBasis(basis);
        var a = Point(Point(endpoint0, transforms[0]), inverse);
        var b = Point(Point(endpoint1, transforms[1]), inverse);
        var full = b - a;
        var origin = full * contactStart + a;
        var direction = full * (contactEnd - contactStart);
        var mass = inputs.InverseMass;
        var radii = inputs.Radii;
        foreach (var (i, j, record) in EdgeItems(inputs.Constraints))
        {
            var pa = Point(p[i], inverse);
            var pb = Point(p[j], inverse);
            bool freeA = mass[i] > 0, freeB = mass[j] > 0;
            if ((record.Flags & 8) == 0)
            {
                if (!freeA && Contact.InTaperedSegment(pa, origin, direction, radius0 + radii[i], radius1 + radii[i])) continue;
                if (!freeB && Contact.InTaperedSegment(pb, origin, direction, radius0 + radii[j], radius1 + radii[j])) continue;
            }

            if (!freeA && !freeB) continue;
            float low = record.ContactStart, high = record.ContactEnd;
            var edge = pb - pa;
            var clippedOrigin = edge * low + pa;
            var clippedDirection = edge * high + pa - clippedOrigin;
            var (s, t) = Contact.ClosestSegment(clippedOrigin, clippedDirection, origin, direction);
            var closestEdge = clippedDirection * s + clippedOrigin;
            var capsuleSize = Lerp(radius0, radius1, contactStart) * (1 - t) + Lerp(radius0, radius1, contactEnd) * t;
            var particleSize = Lerp(radii[i], radii[j], low) * (1 - s) + Lerp(radii[i], radii[j], high) * s;
            var size = capsuleSize + particleSize;
            var delta = closestEdge - (direction * t + origin);
            var norm = Dot(delta, delta);
            if (size * size <= norm || norm < Small) continue;
            var correction = Direction(delta * (Rsqrt(norm) * size - 1), basis);
            if (freeA) p[i] += correction;
            if (freeB) p[j] += correction;
        }
    }

    private void ApplyPoints(Vector3[] p, CollisionInputs inputs)
    {
        var size0 = Lerp(radius0, radius1, contactStart);
        var size1 = Lerp(radius0, radius1, contactEnd);
        Matrix4x4 rows, placement;
        if ((Flags & 2) != 0)
        {
            rows = transforms[0];
            if (selector >= 0.5f)
            {
                // Bone 0's rows renormalized to bone 1's row lengths.
                for (var k = 0; k < 3; k++)
                {
                    var row = Row(rows, k);
                    var norm = Dot(row, row);
                    var length = MaskedSqrt(Dot(Row(transforms[1], k), Row(transforms[1], k)));
                    SetRow(ref rows, k, norm >= Small ? row * (Rsqrt(norm) * length) : row);
                }
            }

            placement = Matrix4x4.Identity;
        }
        else
        {
            rows = Matrix4x4.Identity;
            placement = transforms[0];
        }

        var basis = BasisMatrix(Row(rows, 0) * scale.X, Row(rows, 1) * scale.Y, Row(rows, 2) * scale.Z, Row(rows, 3));
        var inverse = InverseBasis(basis);
        var end1 = Point(Point(endpoint1, transforms[1]), attachmentInverse);
        var span = end1 - endpoint0;
        var origin = Point(span * contactStart + endpoint0, placement);
        var direction = Direction(span * (contactEnd - contactStart), placement);
        foreach (var i in Order(p.Length))
        {
            float ra = size0 + inputs.Radii[i], rb = size1 + inputs.Radii[i];
            if (!(inputs.InverseMass[i] >= Small)) continue;
            var point = Point(p[i], inverse);
            if (!Contact.InTaperedSegment(point, origin, direction, ra, rb)) continue;
            var norm = Dot(direction, direction);
            if (!(norm >= Small)) continue;
            var offset = point - origin;
            var t = Clamp01(Dot(offset, direction) / norm);
            var along = direction * t;
            var perpendicular = offset - along;
            var q = Dot(perpendicular, perpendicular);
            if (!(q >= Small)) continue;
            var local = perpendicular * (Rsqrt(q) * ((1 - t) * ra + t * rb)) + along + origin;
            p[i] = Point(local, basis);
        }
    }
}

internal static class Contact
{
    /// <summary>28E2050: strictly inside a tapered segment; a degenerate one never is.</summary>
    public static bool InTaperedSegment(Vector3 point, Vector3 origin, Vector3 direction, float r0, float r1)
    {
        var norm = Dot(direction, direction);
        if (norm < Small) return false;
        var delta = point - origin;
        var t = Clamp01(Dot(delta, direction) / norm);
        var radius = (1 - t) * r0 + t * r1;
        var perpendicular = delta - direction * t;
        return Dot(perpendicular, perpendicular) < radius * radius;
    }

    /// <summary>28D17D0: closest fractions on two segments (origin + t * direction).</summary>
    public static (float S, float T) ClosestSegment(Vector3 o0, Vector3 d0, Vector3 o1, Vector3 d1)
    {
        var delta = o0 - o1;
        float aa = Dot(d0, d0), bb = Dot(d1, d1), br = Dot(d1, delta);
        if (aa < Small) return (0f, bb >= Small ? Clamp01(br / bb) : 0f);
        var ar = Dot(d0, delta);
        if (bb < Small) return (Clamp01(-ar / aa), 0f);
        var ab = Dot(d0, d1);
        var denominator = bb * aa - ab * ab;
        var s = denominator >= Small ? Clamp01((ab * br - ar * bb) / denominator) : 0f;
        var t = (s * ab + br) / bb;
        if (t < 0) return (Clamp01(-ar / aa), 0f);
        if (t > 1) return (Clamp01((ab - ar) / aa), 1f);
        return (s, t);
    }

    /// <summary>28D1570: as <see cref="ClosestSegment"/> with the near-parallel rules of the band test.</summary>
    public static (float S, float T) ClosestSegmentEps(Vector3 o0, Vector3 d0, Vector3 o1, Vector3 d1)
    {
        const float eps = 1f / 4294967296f;
        var r = o0 - o1;
        float a = Dot(d0, d0), e = Dot(d1, d1), f = Dot(d1, r);
        if (!(a >= Small)) return !(e >= Small) ? (0f, 0f) : (0f, Clamp01(f / e));
        var c = Dot(d0, r);
        if (!(e >= Small)) return (Clamp01(-c / a), 0f);
        var b = Dot(d0, d1);
        var bb = b * b;
        var s = 0f;
        if (bb > eps)
        {
            var denominator = e * a - bb;
            var number = b * f - c * e;
            if (denominator > eps) s = Clamp01(number / denominator);
            else if (!(number < denominator * 0.5f)) s = 1f;
        }

        var t = (s * b + f) / e;
        if (0f > t) return (Clamp01(-c / a), 0f);
        if (t > 1f) return (Clamp01((b - c) / a), 1f);
        return (s, t);
    }
}
