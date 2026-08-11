using System.Numerics;
using System.Runtime.InteropServices;
using Pso2ShapeStudio.Character;

namespace Pso2ShapeStudio.Formats;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct Byte4(byte X, byte Y, byte Z, byte W)
{
    public byte this[int index] => index switch
    {
        0 => X,
        1 => Y,
        2 => Z,
        3 => W,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}

public sealed record RenderMesh(
    Vector3[] Positions,
    Vector3[] Normals,
    Vector2[] Uv,
    Vector4[] Weights,
    Byte4[] PaletteIndices,
    int[] Triangles,
    int[] Palette,
    string Name,
    int MaterialIndex,
    Vector2[]? Uv2 = null,
    Vector2[]? Uv3 = null)
{
    public int VertexCount => Positions.Length;
    public int TriangleCount => Triangles.Length / 3;

    public Vector2[] GetUvChannel(int channel) => channel switch
    {
        1 when Uv2?.Length == VertexCount => Uv2,
        2 when Uv3?.Length == VertexCount => Uv3,
        _ => Uv,
    };
}

public sealed record RenderTexture(
    string Name,
    int Width,
    int Height,
    byte[] RgbaPixels);

public sealed record RenderMaterial(
    string Name,
    Vector4 BaseColor,
    RenderTexture? DiffuseTexture,
    bool TwoSided,
    byte AlphaCutoff,
    bool UsesSkinTexture = false,
    RenderTexture? MaskTexture = null,
    RenderTexture? NormalTexture = null,
    RenderTexture? MultiTexture = null,
    Pso2ColorMapping ColorMapping = default,
    RenderTextureUvSets TextureUvSets = default,
    MaterialBlendMode BlendMode = MaterialBlendMode.Opaque)
{
    public bool IsTransparent => BlendMode is
        MaterialBlendMode.AlphaBlend or MaterialBlendMode.Additive;
}

public enum MaterialBlendMode
{
    Opaque,
    Cutout,
    AlphaBlend,
    Additive,
}

public readonly record struct RenderTextureUvSets(
    int Diffuse = 0,
    int Mask = 0,
    int Normal = 0,
    int Multi = 0);

public sealed record RenderTextureSet(
    RenderTexture? Diffuse,
    RenderTexture? Mask,
    RenderTexture? Normal,
    RenderTexture? Multi)
{
    public int Count => new[] { Diffuse, Mask, Normal, Multi }
        .Where(texture => texture is not null)
        .Select(texture => texture!.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
}

public static class TexturePixelRows
{
    /// <summary>
    /// Converts the top-to-bottom row order returned by DDS decoders to the
    /// bottom-to-top order expected by glTexImage2D. PSO2 UVs are converted to
    /// OpenGL coordinates separately, so skipping this conversion mirrors a
    /// texture vertically a second time.
    /// </summary>
    public static byte[] ToOpenGl(ReadOnlySpan<byte> rgba, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var rowBytes = checked(width * 4);
        var expected = checked(rowBytes * height);
        if (rgba.Length != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} RGBA bytes for {width}x{height}, received {rgba.Length}.",
                nameof(rgba));
        }

        var result = new byte[expected];
        for (var sourceRow = 0; sourceRow < height; sourceRow++)
        {
            var destinationRow = height - 1 - sourceRow;
            rgba.Slice(sourceRow * rowBytes, rowBytes)
                .CopyTo(result.AsSpan(destinationRow * rowBytes, rowBytes));
        }

        return result;
    }
}

public enum Pso2BodyType
{
    Unknown,
    Type1,
    Type2,
}

public sealed record RenderModel(
    string SourcePath,
    IReadOnlyList<RenderMesh> Meshes,
    IReadOnlyList<RenderMaterial> Materials,
    Pso2BodyType BodyType = Pso2BodyType.Unknown)
{
    public int VertexCount => Meshes.Sum(mesh => mesh.VertexCount);
    public int TriangleCount => Meshes.Sum(mesh => mesh.TriangleCount);
    public int TextureCount => Materials
        .SelectMany(material => new[]
        {
            material.DiffuseTexture?.Name,
            material.MaskTexture?.Name,
            material.NormalTexture?.Name,
            material.MultiTexture?.Name,
        })
        .Where(name => name is not null)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
}
