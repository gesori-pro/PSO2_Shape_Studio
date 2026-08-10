using System.Text.Json;
using Pso2ShapeStudio.Character;

namespace Pso2ShapeStudio.Core.Tests.Character;

public sealed class ProportionsTests
{
    private static string FnpPath => TestPaths.ReferenceFnp;
    private static string GoldenPath => TestPaths.GoldenFnp;

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA", "PSO2_SHAPE_REFERENCE_FNP")]
    public void ReferenceFnpBoneDeltasMatchGolden()
    {
        var result = Proportions.Compute(CharacterFile.Load(FnpPath));
        using var golden = JsonDocument.Parse(File.ReadAllBytes(GoldenPath));
        var expected = golden.RootElement.GetProperty("bones");

        Assert.Equal(expected.GetProperty("source").GetString(), result.Source);
        Assert.Equal(expected.GetProperty("outfit_adjust_bones").GetInt32(), result.OutfitAdjustBones);
        Assert.Equal(expected.GetProperty("sliders").GetArrayLength(), result.Sliders.Count);
        Assert.Equal(133, result.Bones.Count);

        var expectedBones = expected.GetProperty("bones");
        Assert.Equal(expectedBones.EnumerateObject().Count(), result.Bones.Count);
        foreach (var property in expectedBones.EnumerateObject())
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
