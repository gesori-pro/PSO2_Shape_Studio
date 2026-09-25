using System.Numerics;
using System.Runtime.CompilerServices;
using Pso2ShapeStudio.Formats;

namespace Pso2ShapeStudio.Rigging;

/// <summary>
/// Stands a posed character on the floor by its soles, the way the Blender
/// add-on's ground contact does: whatever the pose leaves under or over
/// Y = 0 at the lowest foot vertex is the height to lift the models by.
/// </summary>
/// <remarks>
/// The CMX legLength the pose already multiplies into body_root is right
/// for most outfits, but not all of them: most V2 colour variants carry the
/// unset 1.0 (Soffiera's WM Clothes T2-V2 sank 45 mm; its original says
/// 1.067), full-body suits carry something else entirely (0.195 on the
/// Lillipan Suit Mini, 186 mm under), and others are off by a centimetre
/// or so either way. Only vertices whose strongest weight is a foot or toe
/// bone count, so a skirt or a cape that reaches lower than the shoes does
/// not lift the character off the floor. A model with no feet at all (the
/// Ghost Suit Mini) stands on its lowest vertex instead.
/// </remarks>
public static class GroundContact
{
    private static readonly ConditionalWeakTable<RenderMesh, int[]> FootVertexCache = new();

    /// <summary>
    /// How far to raise the models so their lowest foot vertex - or with no
    /// feet, their lowest vertex - sits on Y = 0; null when there is nothing
    /// to stand.
    /// </summary>
    public static float? Lift(
        IEnumerable<RenderModel> models,
        AqnSkeleton skeleton,
        IReadOnlyList<Matrix4x4> skinMatrices)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(skinMatrices);

        // A pose built for another skeleton than the one these meshes were
        // loaded with (between a load and its pose rebuild) is skipped.
        var meshes = models
            .SelectMany(model => model.Meshes)
            .Where(mesh => mesh.Palette.All(bone => (uint)bone < (uint)skinMatrices.Count))
            .ToArray();
        var lowest = Lowest(mesh => FootVertexCache.GetValue(mesh, value => FootVertices(value, skeleton)));
        if (!float.IsFinite(lowest))
        {
            lowest = Lowest(mesh => Enumerable.Range(0, mesh.VertexCount));
        }

        return float.IsFinite(lowest) ? -lowest : null;

        float Lowest(Func<RenderMesh, IEnumerable<int>> vertices)
        {
            var result = float.PositiveInfinity;
            foreach (var mesh in meshes)
            {
                foreach (var index in vertices(mesh))
                {
                    var position = CpuSkinning.TransformPosition(
                        mesh.Positions[index],
                        mesh.Weights[index],
                        mesh.PaletteIndices[index],
                        mesh.Palette,
                        skinMatrices);
                    result = Math.Min(result, position.Y);
                }
            }

            return result;
        }
    }

    /// <summary>The vertices of a mesh whose strongest weight is a foot or toe bone.</summary>
    public static int[] FootVertices(RenderMesh mesh, AqnSkeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(skeleton);

        var feet = new List<int>();
        for (var index = 0; index < mesh.VertexCount; index++)
        {
            var weights = mesh.Weights[index];
            var strongest = 0;
            var weight = weights.X;
            if (weights.Y > weight) (strongest, weight) = (1, weights.Y);
            if (weights.Z > weight) (strongest, weight) = (2, weights.Z);
            if (weights.W > weight) (strongest, weight) = (3, weights.W);
            if (weight <= 0f)
            {
                continue;
            }

            var paletteIndex = mesh.PaletteIndices[index][strongest];
            if (paletteIndex >= mesh.Palette.Length)
            {
                continue;
            }

            var bone = mesh.Palette[paletteIndex];
            if ((uint)bone < (uint)skeleton.Bones.Count && IsFootBone(skeleton.Bones[bone].Name))
            {
                feet.Add(index);
            }
        }

        return [.. feet];
    }

    // l_foot_alt, r_toe, l_foot_effl, ...
    private static bool IsFootBone(string name) =>
        name.Contains("foot", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("toe", StringComparison.OrdinalIgnoreCase);
}
