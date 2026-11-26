using System.Numerics;
using static Pso2ShapeStudio.Rigging.Cloth.ClothMath;

namespace Pso2ShapeStudio.Rigging.Cloth;

/// <summary>
/// Particle-to-bone reconstruction (290AD30/290A750): every chain edge turns
/// its parent bone onto the solved edge and moves the child bone to its
/// particle. Ported from blender_pso2_tools' native_cloth_writeback.
/// </summary>
internal static class ClothWriteback
{
    private readonly record struct Options(
        bool AllowY, bool AllowZ, bool CompensateDistance, bool UnitBasis,
        bool UniformReferenceScale, bool Orthogonalize, bool RebuildFromParentY);

    public static void Apply(Matrix4x4[] world, ClothPart part, Vector3[] positions, Vector3[] targets, Vector3[][] axes)
    {
        var d = part.Definition;
        var flags = d.WrapperFlags;
        var mapping = part.Mapping;
        if ((flags & 1) == 0) return;
        if ((flags & 2) == 0)
        {
            for (var i = 0; i < mapping.Length; i++)
            {
                if ((d.SkipMask >> (i & 63) & 1) == 0)
                {
                    SetRow(ref world[mapping[i]], 3, positions[i]);
                }
            }

            return;
        }

        foreach (var record in part.Records)
        {
            int a = record[0], b = record[1], previous = record[2];
            if ((d.SkipMask >> (b & 63) & 1) != 0) continue;
            int parent = mapping[a], child = mapping[b];
            Matrix4x4? helper = previous != -1 ? world[mapping[part.Records[previous][0]]] : null;
            var options = new Options(
                AllowY: (d.Flags & 0x20) != 0,
                AllowZ: (d.Flags & 0x40) != 0,
                CompensateDistance: (flags & 0x800) == 0 && d.SolverVersion > 8,
                UnitBasis: (d.UnitMask >> (a & 63) & 1) != 0,
                UniformReferenceScale: (d.UniformMask >> (a & 63) & 1) != 0,
                Orthogonalize: d.SolverVersion >= 14 && (d.OrthogonalMask >> (a & 63) & 1) != 0,
                RebuildFromParentY: d.SolverVersion >= 18 && (flags & 0x1000) != 0);
            (world[parent], world[child]) = ReconstructEdge(
                positions[a], positions[b], [axes[0][a], axes[1][a], axes[2][a]],
                world[parent], world[child], targets[a], targets[b], helper, options);
        }
    }

    private static (Matrix4x4, Matrix4x4) ReconstructEdge(
        Vector3 p, Vector3 c, Vector3[] axes, Matrix4x4 outP, Matrix4x4 outC,
        Vector3 tp, Vector3 tc, Matrix4x4? helperMatrix, Options flags)
    {
        var helper = helperMatrix ?? outP;
        var oldX = Row(helper, 0);
        var y = Row(helper, 1);
        var z = Row(helper, 2);
        var delta = c - p;
        if (IsSmall(oldX))
        {
            SetRow(ref outC, 3, c);
            outC.M44 = 1;
            return (outP, outC);
        }

        var x = oldX;
        var direction = delta;
        foreach (var (allowed, axis) in new[] { (flags.AllowY, axes[1]), (flags.AllowZ, axes[2]) })
        {
            var norm = Dot(axis, axis);
            if (!allowed && norm >= Small)
            {
                var inverse = 1f / norm;
                x -= axis * Dot(axis, x * inverse);
                direction -= axis * Dot(axis, direction * inverse);
            }
        }

        x = IsSmall(x) ? oldX / MathF.Sqrt(Dot(oldX, oldX)) : x / MathF.Sqrt(Dot(x, x));
        Vector3 newX;
        if (IsSmall(direction))
        {
            y = axes[1];
            z = axes[2];
            newX = delta;
        }
        else
        {
            direction /= MathF.Sqrt(Dot(direction, direction));
            var useArc = true;
            if (flags.RebuildFromParentY)
            {
                var nextZ = Cross(direction, Row(outP, 1));
                var nextY = Cross(nextZ, direction);
                float yy = Dot(y, y), zz = Dot(z, z), ny = Dot(nextY, nextY), nz = Dot(nextZ, nextZ);
                if (MathF.Min(MathF.Min(yy, zz), MathF.Min(ny, nz)) >= Small)
                {
                    y = nextY * Rsqrt(ny * (1f / yy));
                    z = nextZ * Rsqrt(nz * (1f / zz));
                    useArc = false;
                }
            }

            if (useArc)
            {
                var cross = Cross(x, direction);
                if (Dot(cross, cross) <= 0 && Dot(x, direction) < Small)
                {
                    y = axes[1];
                    z = axes[2];
                }
                else
                {
                    var q = Arc(x, direction) ?? Quaternion.Identity;
                    y = Rotate(q, y);
                    z = Rotate(q, z);
                }
            }

            float lengthY = 1, lengthZ = 1;
            if (flags.Orthogonalize)
            {
                float yy = Dot(y, y), zz = Dot(z, z);
                lengthY = MaskedSqrt(yy);
                lengthZ = MaskedSqrt(zz);
                var unitY = MathF.Abs(lengthY) >= Small ? y * Rsqrt(yy) : y;
                z = Cross(direction, unitY);
                y = Cross(z, direction);
            }

            if (!flags.UnitBasis && !flags.UniformReferenceScale)
            {
                float scale;
                if (flags.CompensateDistance)
                {
                    var f = Dot(delta, delta) * Dot(axes[0], axes[0]);
                    scale = f > 0 ? Rsqrt(1f / f * Dot(tp - tc, tp - tc)) : 0f;
                }
                else
                {
                    scale = MaskedSqrt(Dot(axes[0], axes[0]));
                }

                newX = direction * scale;
                if (flags.Orthogonalize)
                {
                    y *= lengthY;
                    z *= lengthZ;
                }
            }
            else
            {
                if (!IsSmall(y)) y *= Rsqrt(Dot(y, y));
                if (!IsSmall(z)) z *= Rsqrt(Dot(z, z));
                newX = direction;
                if (flags.UniformReferenceScale)
                {
                    var scale = MaskedSqrt(Dot(oldX, oldX));
                    newX *= scale;
                    y *= scale;
                    z *= scale;
                }
            }
        }

        SetBasis(ref outP, newX, y, z);
        SetBasis(ref outC, newX, y, z);
        SetRow(ref outC, 3, c);
        outC.M44 = 1;
        return (outP, outC);
    }

    private static void SetBasis(ref Matrix4x4 m, Vector3 x, Vector3 y, Vector3 z)
    {
        SetRow(ref m, 0, x);
        SetRow(ref m, 1, y);
        SetRow(ref m, 2, z);
        m.M14 = 0;
        m.M24 = 0;
        m.M34 = 0;
    }
}
