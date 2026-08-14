using System.Numerics;
using Pso2ShapeStudio.Character;
using Pso2ShapeStudio.Formats;
using Pso2ShapeStudio.GameData;

namespace Pso2ShapeStudio.Core.Tests.Formats;

public sealed class AqpLoaderTests
{
    private static string AqpPath => TestPaths.ReferenceAqp;

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA")]
    public void LoadReferenceModelMatchesIndependentCountsAndNormalizesWeights()
    {
        Assert.True(File.Exists(AqpPath), $"Reference model is missing: {AqpPath}");

        var model = AqpLoader.Load(AqpPath);

        Assert.Equal(19, model.Meshes.Count);
        Assert.Equal(78_973, model.VertexCount);
        Assert.Equal(105_206, model.TriangleCount);
        Assert.Equal(3_615, model.Meshes[0].VertexCount);
        Assert.Equal(6_870, model.Meshes[0].TriangleCount);

        var first = model.Meshes[0].Weights[0];
        var sum = first.X + first.Y + first.Z + first.W;
        Assert.InRange(sum, 0.99999f, 1.00001f);
        Assert.InRange(first.X, 0.897f, 0.899f);
        Assert.Equal(86, model.Meshes[0].PaletteIndices[0].X);
        Assert.Equal(Pso2BodyType.Type2, model.BodyType);
        Assert.Equal(3, model.Materials.Count(material => material.UsesSkinTexture));
        Assert.Contains(model.Meshes, mesh => mesh.Part == Pso2MeshPart.BasewearOrnament1);
        Assert.Contains(model.Meshes, mesh => mesh.Part == Pso2MeshPart.BasewearOrnament2);
        Assert.DoesNotContain(model.Meshes, mesh => mesh.Part == Pso2MeshPart.OuterwearOrnament);
        Assert.Equal(
            [
                MaterialBlendMode.Opaque,
                MaterialBlendMode.Cutout,
                MaterialBlendMode.Opaque,
                MaterialBlendMode.Opaque,
                MaterialBlendMode.Cutout,
                MaterialBlendMode.Opaque,
            ],
            model.Materials.Select(material => material.BlendMode));
        Assert.All(model.Meshes, mesh =>
        {
            Assert.Equal(mesh.VertexCount, mesh.Uv.Length);
            Assert.Equal(mesh.VertexCount, mesh.GetUvChannel(1).Length);
            Assert.Equal(mesh.VertexCount, mesh.GetUvChannel(2).Length);
        });
        var allUv = model.Meshes.SelectMany(mesh => mesh.Uv).ToArray();
        Assert.Contains(allUv, value => value != Vector2.Zero);
        Assert.All(allUv, value =>
        {
            Assert.True(float.IsFinite(value.X));
            Assert.True(float.IsFinite(value.Y));
        });
        Assert.True(allUv.Max(value => value.X) - allUv.Min(value => value.X) > 0.9f);
        Assert.True(allUv.Max(value => value.Y) - allUv.Min(value => value.Y) > 0.9f);

        var baseMaterials = model.Materials.Where(material => !material.UsesSkinTexture).ToArray();
        Assert.Equal(3, baseMaterials.Length);
        Assert.All(baseMaterials, material =>
        {
            Assert.EndsWith("_bw_d.dds", material.DiffuseTexture!.Name);
            Assert.EndsWith("_bw_m.dds", material.MaskTexture!.Name);
            Assert.EndsWith("_bw_n.dds", material.NormalTexture!.Name);
            Assert.EndsWith("_bw_s.dds", material.MultiTexture!.Name);
        });
        Assert.Equal(4, model.TextureCount);
    }

    [Fact]
    public void TextureRowsAreReorderedForOpenGlUpload()
    {
        byte[] topToBottom =
        [
            1, 2, 3, 4, 5, 6, 7, 8,
            9, 10, 11, 12, 13, 14, 15, 16,
        ];

        var bottomToTop = TexturePixelRows.ToOpenGl(topToBottom, width: 2, height: 2);

        Assert.Equal(
            new byte[]
            {
                9, 10, 11, 12, 13, 14, 15, 16,
                1, 2, 3, 4, 5, 6, 7, 8,
            },
            bottomToTop);
    }

    [Theory]
    [InlineData("pl_rbd_100000_bw.aqp", Pso2BodyType.Type1)]
    [InlineData("archive::pl_rbd_299999_bd.aqp", Pso2BodyType.Type2)]
    [InlineData("model.aqp", Pso2BodyType.Unknown)]
    public void DetectBodyTypeUsesNgsRebootModelId(string source, Pso2BodyType expected) =>
        Assert.Equal(expected, AqpLoader.DetectBodyType(source));

    [ExternalDataFact("PSO2_GAME_DIR")]
    public void CombatJacketArchivesPreserveToggleableOrnamentParts()
    {
        var locator = new Pso2DataLocator(TestPaths.GameDirectory);
        var basewearPath = locator.Resolve("character/making_reboot/pl_bw_201630.ice")?.Path;
        var outerwearPath = locator.Resolve("character/making_reboot/pl_ow_201630.ice")?.Path;
        Assert.NotNull(basewearPath);
        Assert.NotNull(outerwearPath);

        var basewear = Assert.Single(ModelArchiveLoader.Load(basewearPath).Models);
        var outerwear = Assert.Single(ModelArchiveLoader.Load(outerwearPath).Models);

        Assert.Equal(1, basewear.Meshes.Count(mesh => mesh.Part == Pso2MeshPart.BasewearOrnament1));
        Assert.Equal(4, basewear.Meshes.Count(mesh => mesh.Part == Pso2MeshPart.BasewearOrnament2));
        Assert.Equal(2, outerwear.Meshes.Count(mesh => mesh.Part == Pso2MeshPart.OuterwearOrnament));
    }

    [ExternalDataFact("PSO2_GAME_DIR")]
    public void YukariSetwearResolvesItsOwnAtlasWithoutReplacingClothesWithSkin()
    {
        var locator = new Pso2DataLocator(TestPaths.GameDirectory);
        var basewearPath = locator.Resolve("character/making_reboot/pl_bw_219010.ice")?.Path;
        var outerwearPath = locator.Resolve("character/making_reboot/pl_ow_219010.ice")?.Path;
        Assert.NotNull(basewearPath);
        Assert.NotNull(outerwearPath);

        var baseColors = new Pso2ColorMapping(Pso2ColorChannel.Base1, Pso2ColorChannel.Base2);
        var basewear = Assert.Single(ModelArchiveLoader.Load(basewearPath, baseColors).Models);
        var outerLayer = Assert.Single(basewear.Materials, material => material.Name == "outer_opa");
        var baseLayer = Assert.Single(basewear.Materials, material => material.Name == "base_opa");

        Assert.False(outerLayer.UsesSkinTexture);
        Assert.False(baseLayer.UsesSkinTexture);
        Assert.Equal(baseColors, outerLayer.ColorMapping);
        Assert.Equal(baseColors, baseLayer.ColorMapping);
        AssertWearTextures(outerLayer, "_bw_");
        AssertWearTextures(baseLayer, "_bw_");
        Assert.All(
            basewear.Materials.Where(material => material.Name.StartsWith("bodyskin", StringComparison.Ordinal)),
            material => Assert.True(material.UsesSkinTexture));

        var linkedOuter = Assert.Single(ModelArchiveLoader.Load(outerwearPath, baseColors).Models);
        var linkedLayer = Assert.Single(linkedOuter.Materials);
        Assert.Equal(baseColors, linkedLayer.ColorMapping);
        AssertWearTextures(linkedLayer, "_ow_");

        static void AssertWearTextures(RenderMaterial material, string category)
        {
            Assert.Contains(category, material.DiffuseTexture!.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(category, material.MaskTexture!.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(category, material.NormalTexture!.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(category, material.MultiTexture!.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
