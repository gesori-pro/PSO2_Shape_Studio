using Pso2ShapeStudio.Formats;
using Pso2ShapeStudio.GameData;

namespace Pso2ShapeStudio.Core.Tests.Formats;

public sealed class SkinTextureLoaderTests
{
    [Fact]
    public void SelectDiffuseTextureNamePrefersSelectedSkinId()
    {
        string[] names =
        [
            "pl_rbd_100001_sk_d.dds",
            "pl_rbd_100000_sk_m.dds",
            "pl_rbd_100000_sk_d.dds",
        ];

        Assert.Equal(
            "pl_rbd_100000_sk_d.dds",
            SkinTextureLoader.SelectDiffuseTextureName(names, 100000));
    }

    [ExternalDataFact("PSO2_GAME_DIR")]
    public void LoadType2BaseSkinReturnsBlenderTextureSlots()
    {
        var locator = new Pso2DataLocator(TestPaths.GameDirectory);
        var resolved = locator.Resolve("character/making_reboot/pl_sk_200000.ice");
        Assert.NotNull(resolved);

        var archive = SkinTextureLoader.Load(resolved.Path, 200000);

        Assert.Equal(10, archive.DdsCount);
        Assert.Equal("pl_rbd_200000_sk_d.dds", archive.Textures.Diffuse!.Name);
        Assert.Equal("pl_rbd_200000_sk_m.dds", archive.Textures.Mask!.Name);
        Assert.Equal("pl_rbd_200000_sk_n.dds", archive.Textures.Normal!.Name);
        Assert.Equal("pl_rbd_200000_sk_s.dds", archive.Textures.Multi!.Name);
        Assert.Equal(4, archive.Textures.Count);
    }
}
