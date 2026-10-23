using System.Numerics;
using System.Runtime.InteropServices;
using System.Reflection;
using Pso2ShapeStudio.App.Rendering;
using Pso2ShapeStudio.Formats;
using Pso2ShapeStudio.Rigging;

namespace Pso2ShapeStudio.Core.Tests.App;

public sealed class SkinningPaletteTests
{
    [Theory]
    [InlineData(232)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(282)]
    public void ViewportAcceptsAndSnapshotsTheEntireRig(int count)
    {
        var viewport = new ModelViewport();
        var matrices = Enumerable.Repeat(Matrix4x4.Identity, count).ToArray();
        matrices[^1] = Matrix4x4.CreateScale(2, 3, 4);
        viewport.SetSkinMatrices(matrices);
        matrices[^1] = Matrix4x4.Identity;
        var pending = (Matrix4x4[])typeof(ModelViewport)
            .GetField("_pendingSkinMatrices", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(viewport)!;
        Assert.Equal(count, pending.Length);
        Assert.Equal(Matrix4x4.CreateScale(2, 3, 4), pending[^1]);
    }

    [Theory]
    [InlineData(232)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(282)]
    [InlineData(1024)]
    public void LastGlobalBoneSurvivesLocalByteIndicesAndUploadBytes(int count)
    {
        var matrices = Enumerable.Repeat(Matrix4x4.Identity, count).ToArray();
        matrices[^1] = Matrix4x4.CreateScale(2, 3, 4) *
                       Matrix4x4.CreateRotationZ(0.37f) * Matrix4x4.CreateTranslation(11, -7, 19);
        int[] mapping = [count - 1, 0, Math.Min(255, count - 2)];
        var palette = new SkinningPalette(mapping);
        var indices = palette.ValidateIndices(new Byte4(0, 1, 2, 0));
        palette.Update(matrices);

        // Read the exact std140 bytes uploaded by the viewport. .NET rows
        // become GLSL columns, matching the existing column-vector shader.
        var bytes = MemoryMarshal.AsBytes(palette.Matrices.AsSpan()).ToArray();
        Assert.Equal(16384, bytes.Length);
        var uploaded = MemoryMarshal.Cast<byte, Matrix4x4>(bytes).ToArray();
        Assert.Equal(matrices[^1], uploaded[0]);
        var position = new Vector3(1, 2, -3);
        var weights = new Vector4(0.5f, 0.25f, 0.125f, 0.125f);
        var expected = CpuSkinning.TransformPosition(position, weights, indices, mapping, matrices);
        var actual = Vector3.Zero;
        for (var lane = 0; lane < 4; lane++)
            actual += Vector3.Transform(position, uploaded[indices[lane]]) * weights[lane];
        Assert.True(Vector3.Distance(expected, actual) < 1e-5f, $"CPU/upload error: {Vector3.Distance(expected, actual):G9}");
        Assert.True(Vector3.Distance(position, actual) > 1f);
    }

    [Fact]
    public void LocalSlot255CanReferToGlobalBone281AndPalettesStayIndependent()
    {
        var matrices = Enumerable.Repeat(Matrix4x4.Identity, 282).ToArray();
        matrices[281] = Matrix4x4.CreateTranslation(28, 1, 7);
        var mapping = Enumerable.Range(0, 256).ToArray();
        mapping[255] = 281;
        var first = new SkinningPalette(mapping);
        var second = new SkinningPalette([1, 281]);
        first.Update(matrices);
        second.Update(matrices);
        Assert.Equal(new Byte4(255, 0, 1, 2), first.ValidateIndices(new Byte4(255, 0, 1, 2)));
        Assert.Equal(matrices[281], first.Matrices[255]);
        Assert.Equal(matrices[281], second.Matrices[1]);
        Assert.Equal(Matrix4x4.Identity, second.Matrices[0]);
        // Reloading a shorter/no-pose rig must not retain a previous transform.
        first.Update([Matrix4x4.Identity]);
        Assert.All(first.Matrices, value => Assert.Equal(Matrix4x4.Identity, value));
    }

    [Fact]
    public void EmptyPaletteUsesBoneZeroAndMissingPoseUsesIdentity()
    {
        var palette = new SkinningPalette([]);
        Assert.Equal(default, palette.ValidateIndices(new Byte4(255, 3, 2, 1)));
        palette.Update([Matrix4x4.CreateScale(2)]);
        Assert.Equal(Matrix4x4.CreateScale(2), palette.Matrices[0]);
        palette.Update([]);
        Assert.All(palette.Matrices, value => Assert.Equal(Matrix4x4.Identity, value));
    }

    [Fact]
    public void InvalidLocalIndicesAndNegativeGlobalIndicesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new SkinningPalette([-1]));
        var palette = new SkinningPalette([281]);
        Assert.Throws<InvalidDataException>(() => palette.ValidateIndices(new Byte4(0, 0, 1, 0)));
    }
}
