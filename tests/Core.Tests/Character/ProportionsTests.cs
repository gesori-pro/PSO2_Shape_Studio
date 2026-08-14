using System.Text.Json;
using Pso2ShapeStudio.Character;

namespace Pso2ShapeStudio.Core.Tests.Character;

public sealed class ProportionsTests
{
    [ExternalDataFact("PSO2_SHAPE_TEST_DATA", "PSO2_SHAPE_REFERENCE_FNP")]
    public void ReferenceFnpBoneDeltasMatchGolden() =>
        AssertMatchesGolden(TestPaths.ReferenceFnp, TestPaths.GoldenFnp, expectedBones: 133);

    /// <summary>
    /// The male tables were wired up long before any male file could be
    /// opened, so this branch had never run. The golden comes from the
    /// Blender add-on's Python implementation, which makes this a port
    /// check rather than a comparison of the C# against itself.
    /// </summary>
    [ExternalDataFact("PSO2_SHAPE_TEST_DATA", "PSO2_SHAPE_MALE_CHARACTER")]
    public void MaleCharacterBoneDeltasMatchGolden()
    {
        var result = AssertMatchesGolden(
            TestPaths.MaleCharacter, TestPaths.GoldenMale, expectedBones: 135);

        Assert.Equal("pl_cmakemot_b_mh_rb.json", result.Source);
        Assert.Equal(0, Convert.ToInt32(CharacterFile.Load(TestPaths.MaleCharacter)["baseDOC.gender"]));
    }

    private static ProportionResult AssertMatchesGolden(
        string characterPath,
        string goldenPath,
        int expectedBones)
    {
        var result = Proportions.Compute(CharacterFile.Load(characterPath));
        using var golden = JsonDocument.Parse(File.ReadAllBytes(goldenPath));
        var expected = golden.RootElement.GetProperty("bones");

        Assert.Equal(expected.GetProperty("source").GetString(), result.Source);
        Assert.Equal(expected.GetProperty("outfit_adjust_bones").GetInt32(), result.OutfitAdjustBones);
        Assert.Equal(expected.GetProperty("sliders").GetArrayLength(), result.Sliders.Count);
        Assert.Equal(expectedBones, result.Bones.Count);

        var expectedBoneTable = expected.GetProperty("bones");
        Assert.Equal(expectedBoneTable.EnumerateObject().Count(), result.Bones.Count);
        foreach (var property in expectedBoneTable.EnumerateObject())
        {
            Assert.True(result.Bones.TryGetValue(property.Name, out var actual), $"Missing bone {property.Name}");
            var expectedBone = property.Value;
            if (expectedBone.GetProperty("index").ValueKind == JsonValueKind.Null)
            {
                Assert.Null(actual!.Index);
            }
            else
            {
                Assert.Equal(expectedBone.GetProperty("index").GetInt32(), actual!.Index);
            }

            AssertVector(property.Name + ".scale", expectedBone.GetProperty("scale"), actual!.Scale);
            AssertVector(property.Name + ".pos", expectedBone.GetProperty("pos"), actual.Pos);
            AssertVector(property.Name + ".rotQuat", expectedBone.GetProperty("rotQuat"), actual.RotQuat);
        }

        return result;
    }

    private static void AssertVector(string name, JsonElement expected, IReadOnlyList<double> actual)
    {
        var values = expected.EnumerateArray().ToArray();
        Assert.Equal(values.Length, actual.Count);
        for (var index = 0; index < values.Length; index++)
        {
            var difference = Math.Abs(values[index].GetDouble() - actual[index]);
            Assert.True(difference <= 1e-6, $"{name}[{index}]: difference={difference:G9}");
        }
    }
}
