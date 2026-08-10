using Pso2ShapeStudio.Character;

namespace Pso2ShapeStudio.Core.Tests.Character;

public sealed class CharacterColorsTests
{
    private static string CombatJacketFnp => TestPaths.FocusliteFnp;

    [ExternalDataFact("PSO2_SHAPE_FOCUSLITE_FNP")]
    public void CombatJacketColorsMatchBlenderBgrAndSrgbConversion()
    {
        Assert.True(File.Exists(CombatJacketFnp), $"Reference character is missing: {CombatJacketFnp}");
        var palette = CharacterColorPalette.FromCharacter(CharacterFile.Load(CombatJacketFnp));

        AssertColor(palette[Pso2ColorChannel.Base1], 0f, 0f, 0f);
        AssertColor(palette[Pso2ColorChannel.Base2], 0.004391f, 0.003035f, 0.003035f);
        AssertColor(palette[Pso2ColorChannel.MainSkin], 1f, 0.745404f, 0.644480f);
        AssertColor(palette[Pso2ColorChannel.SubSkin], 0.309469f, 0.001518f, 0f);
    }

    private static void AssertColor(
        System.Numerics.Vector4 actual,
        float red,
        float green,
        float blue)
    {
        Assert.InRange(MathF.Abs(actual.X - red), 0f, 0.000001f);
        Assert.InRange(MathF.Abs(actual.Y - green), 0f, 0.000001f);
        Assert.InRange(MathF.Abs(actual.Z - blue), 0f, 0.000001f);
        Assert.Equal(1f, actual.W);
    }
}
