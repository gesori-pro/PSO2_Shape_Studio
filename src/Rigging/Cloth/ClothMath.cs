using System.Numerics;

namespace Pso2ShapeStudio.Rigging.Cloth;

/// <summary>
/// Small vector helpers shared by the cloth stages. Matrices are the game's
/// row matrices, the same convention as System.Numerics: rows 1..3 are the
/// bone axes and row 4 the position, so a point transforms as p * M.
/// </summary>
/// <remarks>
/// The add-on this is ported from reproduces the game's SSE reciprocal
/// estimates bit for bit. The preview only needs the settled shape, so exact
/// division and square roots stand in for them here.
/// </remarks>
internal static class ClothMath
{
    public const float Small = 1f / 65536f;
    public const float Epsilon = 1f / 8388608f;

    public static float Dot(Vector3 a, Vector3 b) => Vector3.Dot(a, b);

    public static Vector3 Cross(Vector3 a, Vector3 b) => Vector3.Cross(a, b);

    public static float Rcp(float value) => 1f / value;

    public static float Rsqrt(float value) => 1f / MathF.Sqrt(value);

    /// <summary>sqrt kept only where 0 &lt; x, as the game's masked rsqrt * x.</summary>
    public static float MaskedSqrt(float value) => value > 0f ? MathF.Sqrt(value) : 0f;

    public static bool IsSmall(Vector3 v) =>
        MathF.Abs(v.X) < Epsilon && MathF.Abs(v.Y) < Epsilon && MathF.Abs(v.Z) < Epsilon;

    public static float Clamp01(float value) => value > 0f ? (value < 1f ? value : 1f) : 0f;

    public static Vector3 Row(in Matrix4x4 m, int row) => row switch
    {
        0 => new Vector3(m.M11, m.M12, m.M13),
        1 => new Vector3(m.M21, m.M22, m.M23),
        2 => new Vector3(m.M31, m.M32, m.M33),
        3 => new Vector3(m.M41, m.M42, m.M43),
        _ => throw new ArgumentOutOfRangeException(nameof(row)),
    };

    public static void SetRow(ref Matrix4x4 m, int row, Vector3 value)
    {
        switch (row)
        {
            case 0: m.M11 = value.X; m.M12 = value.Y; m.M13 = value.Z; break;
            case 1: m.M21 = value.X; m.M22 = value.Y; m.M23 = value.Z; break;
            case 2: m.M31 = value.X; m.M32 = value.Y; m.M33 = value.Z; break;
            case 3: m.M41 = value.X; m.M42 = value.Y; m.M43 = value.Z; break;
            default: throw new ArgumentOutOfRangeException(nameof(row));
        }
    }

    public static Vector3 Translation(in Matrix4x4 m) => new(m.M41, m.M42, m.M43);

    /// <summary>A point through a row matrix: x*R0 + y*R1 + z*R2 + R3.</summary>
    public static Vector3 Point(Vector3 p, in Matrix4x4 m) => Vector3.Transform(p, m);

    /// <summary>A direction through a row matrix's basis rows.</summary>
    public static Vector3 Direction(Vector3 v, in Matrix4x4 m) => Vector3.TransformNormal(v, m);

    /// <summary>Affine inverse, or identity for a degenerate basis (as the game's helper).</summary>
    public static Matrix4x4 InverseBasis(in Matrix4x4 m)
    {
        var x = Row(m, 0);
        var y = Row(m, 1);
        var z = Row(m, 2);
        if (MathF.Abs(Dot(x, Cross(y, z))) < Small)
        {
            return Matrix4x4.Identity;
        }

        var affine = m;
        affine.M14 = 0f;
        affine.M24 = 0f;
        affine.M34 = 0f;
        affine.M44 = 1f;
        return Matrix4x4.Invert(affine, out var inverse) ? inverse : Matrix4x4.Identity;
    }

    /// <summary>Rotation turning unit vector <paramref name="from"/> onto <paramref name="to"/>.</summary>
    /// <returns>Null when the game leaves the frame unchanged (opposed vectors).</returns>
    public static Quaternion? Arc(Vector3 from, Vector3 to)
    {
        var cross = Cross(from, to);
        var cross2 = Dot(cross, cross);
        var dot = Dot(from, to);
        if (cross2 > 0f)
        {
            var half = Math.Clamp(dot * 0.5f, -0.5f, 0.5f);
            var w = MaskedSqrt(half + 0.5f);
            var s = MaskedSqrt((0.5f - half) / cross2);
            return new Quaternion(cross * s, w);
        }

        return dot >= Small ? Quaternion.Identity : null;
    }

    public static Vector3 Rotate(Quaternion q, Vector3 v) => Vector3.Transform(v, q);
}
