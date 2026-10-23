using System.Numerics;
using Pso2ShapeStudio.Formats;

namespace Pso2ShapeStudio.App.Rendering;

// Byte4 addresses a mesh-local palette, never the global skeleton. 256 mat4s
// fit the ES 3.0 minimum uniform-block size (16 KiB), regardless of rig size.
internal sealed class SkinningPalette
{
    internal const int Capacity = 256;
    private readonly int[] _bones;
    internal Matrix4x4[] Matrices { get; } = new Matrix4x4[Capacity];

    internal SkinningPalette(IReadOnlyList<int> palette)
    {
        // Entries beyond 255 cannot be addressed by the source Byte4 format.
        _bones = palette.Take(Capacity).ToArray();
        if (_bones.Any(bone => bone < 0))
            throw new InvalidDataException("Bone palettes cannot contain negative skeleton indices.");
        Update([]);
    }

    internal Byte4 ValidateIndices(Byte4 indices)
    {
        // Preserve the existing empty-palette convention: all lanes use bone 0.
        if (_bones.Length == 0) return default;
        for (var lane = 0; lane < 4; lane++)
            if (indices[lane] >= _bones.Length)
                throw new InvalidDataException($"Palette index {indices[lane]} exceeds palette length {_bones.Length}.");
        return indices;
    }

    internal void Update(IReadOnlyList<Matrix4x4> skinMatrices)
    {
        Array.Fill(Matrices, Matrix4x4.Identity);
        if (_bones.Length == 0)
        {
            if (skinMatrices.Count > 0) Matrices[0] = skinMatrices[0];
            return;
        }
        for (var local = 0; local < _bones.Length; local++)
        {
            var global = _bones[local];
            // Models may be displayed before an AQN/pose has been loaded.
            if (global < skinMatrices.Count) Matrices[local] = skinMatrices[global];
        }
    }
}
