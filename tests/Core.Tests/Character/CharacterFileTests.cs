using System.Text.Json;
using Pso2ShapeStudio.Character;

namespace Pso2ShapeStudio.Core.Tests.Character;

public sealed class CharacterFileTests
{
    private static string FnpPath => TestPaths.ReferenceFnp;
    private static string GoldenPath => TestPaths.GoldenFnp;

    [Fact]
    public void BlowfishRoundTripAndDerivedKeyMatchReference()
    {
        Assert.Equal(0x3645D7C8u, CharacterFile.DeriveKey(940));
        var source = Enumerable.Range(0, 947).Select(index => (byte)(index * 37)).ToArray();
        var cipher = new Pso2Blowfish(CharacterFile.DeriveKey(source.Length));
        var encrypted = cipher.encryptBlock(source);
        var decrypted = cipher.decryptBlock(encrypted);

        Assert.NotEqual(source, encrypted);
        Assert.Equal(source, decrypted);
    }

    [ExternalDataFact("PSO2_SHAPE_TEST_DATA", "PSO2_SHAPE_REFERENCE_FNP")]
    public void ReferenceFnpFieldsMatchGoldenAndSaveReloads()
    {
        var character = CharacterFile.Load(FnpPath);
        using var golden = JsonDocument.Parse(File.ReadAllBytes(GoldenPath));
        var expected = golden.RootElement.GetProperty("fields");
        var actual = character.ToDictionary();

        Assert.Equal(expected.EnumerateObject().Count(), actual.Count);
        foreach (var property in expected.EnumerateObject())
        {
            Assert.True(actual.TryGetValue(property.Name, out var value), $"Missing field {property.Name}");
            AssertJsonValue(property.Name, property.Value, value!);
        }

        var output = Path.Combine(Path.GetTempPath(), $"pso2-shape-{Guid.NewGuid():N}.fnp");
        try
        {
            character.Save(output);
            var reloaded = CharacterFile.Load(output);
            Assert.Equal(character.ToDictionary(), reloaded.ToDictionary());
        }
        finally
        {
            File.Delete(output);
        }
    }

    private static void AssertJsonValue(string name, JsonElement expected, object actual)
    {
        if (expected.ValueKind == JsonValueKind.Array)
        {
            var actualValues = Assert.IsAssignableFrom<IEnumerable<object>>(actual).ToArray();
            var expectedValues = expected.EnumerateArray().ToArray();
            Assert.Equal(expectedValues.Length, actualValues.Length);
            for (var index = 0; index < expectedValues.Length; index++)
            {
                AssertJsonValue($"{name}[{index}]", expectedValues[index], actualValues[index]);
            }
            return;
        }

        if (expected.TryGetInt64(out var integer))
        {
            Assert.Equal(integer, Convert.ToInt64(actual));
        }
        else
        {
            var difference = Math.Abs(expected.GetDouble() - Convert.ToDouble(actual));
            Assert.True(difference <= 1e-6, $"{name}: difference={difference:G9}");
        }
    }
}
