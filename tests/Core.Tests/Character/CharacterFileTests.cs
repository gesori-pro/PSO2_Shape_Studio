using System.Buffers.Binary;
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
        var key = CharacterFile.DeriveKey(source.Length);
        var encrypted = CharacterCipher.Encrypt(source, key);
        var decrypted = CharacterCipher.Decrypt(encrypted, key);

        Assert.NotEqual(source, encrypted);
        Assert.Equal(source, decrypted);
        // The trailing 3 bytes (947 % 8) stay unencrypted in this format.
        Assert.Equal(source[^3..], encrypted[^3..]);
    }

    [Theory]
    [InlineData("character.fdp")]
    [InlineData("character.fnp")]
    [InlineData("character.fhp")]
    [InlineData("character.fcp")]
    [InlineData("character.fdpu")]
    [InlineData("character.fnpu")]
    [InlineData("character.fhpu")]
    [InlineData("CHARACTER.FCPU")]
    public void RaceSpecificCharacterExtensionsAreSupported(string path)
    {
        Assert.True(CharacterFile.IsSupportedPath(path));
    }

    [Theory]
    [InlineData("character.aqm")]
    [InlineData("character.fdp.bak")]
    [InlineData("character")]
    public void UnrelatedExtensionsAreNotCharacterFiles(string path)
    {
        Assert.False(CharacterFile.IsSupportedPath(path));
    }

    [ExternalDataFact("PSO2_SHAPE_REFERENCE_FNP")]
    public void EncryptedRaceSpecificExtensionsLoadTheSamePayload()
    {
        var expected = CharacterFile.Load(FnpPath).ToDictionary();
        foreach (var extension in new[] { ".fdp", ".fnp", ".fhp", ".fcp" })
        {
            var output = Path.Combine(
                Path.GetTempPath(), $"pso2-shape-{Guid.NewGuid():N}{extension}");
            try
            {
                File.Copy(FnpPath, output);
                Assert.Equal(expected, CharacterFile.Load(output).ToDictionary());
            }
            finally
            {
                File.Delete(output);
            }
        }
    }

    [ExternalDataFact("PSO2_SHAPE_REFERENCE_FNP")]
    public void UnencryptedRaceSpecificExtensionsLoadTheSamePayload()
    {
        var expected = CharacterFile.Load(FnpPath).ToDictionary();
        var raw = File.ReadAllBytes(FnpPath);
        var bodySize = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(4, 4));
        var encrypted = raw.AsSpan(16, bodySize).ToArray();
        var body = CharacterCipher.Decrypt(encrypted, CharacterFile.DeriveKey(bodySize));
        body.CopyTo(raw, 16);

        foreach (var extension in new[] { ".fdpu", ".fnpu", ".fhpu", ".fcpu" })
        {
            var output = Path.Combine(
                Path.GetTempPath(), $"pso2-shape-{Guid.NewGuid():N}{extension}");
            try
            {
                File.WriteAllBytes(output, raw);
                Assert.Equal(expected, CharacterFile.Load(output).ToDictionary());
            }
            finally
            {
                File.Delete(output);
            }
        }
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
