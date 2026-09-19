using Pso2ShapeStudio.Character;

namespace Pso2ShapeStudio.Core.Tests.Character;

public sealed class CharacterSurfaceSettingsTests
{
    [ExternalDataFact("PSO2_SHAPE_REFERENCE_FNP")]
    public void ReferenceCharacterReadsMuscleAndGlossFields()
    {
        var settings = CharacterSurfaceSettings.FromCharacter(
            CharacterFile.Load(TestPaths.ReferenceFnp));

        Assert.Equal(23691f / 60000f, settings.Muscularity, 5);
        Assert.Equal(17f / 127f, settings.SkinGloss, 5);
    }

    [Theory]
    [InlineData(0f, -127, 0f, -1f)]
    [InlineData(30000f, 0, 0.5f, 0f)]
    [InlineData(60000f, 127, 1f, 1f)]
    [InlineData(-1f, -200, 0f, -1f)]
    [InlineData(70000f, 200, 1f, 1f)]
    public void FromRawNormalizesAndClampsCharacterMaterialValues(
        float muscleMass,
        int skinGloss,
        float expectedMuscularity,
        float expectedSkinGloss)
    {
        var settings = CharacterSurfaceSettings.FromRaw(muscleMass, skinGloss);

        Assert.Equal(expectedMuscularity, settings.Muscularity, 5);
        Assert.Equal(expectedSkinGloss, settings.SkinGloss, 5);
    }
}
