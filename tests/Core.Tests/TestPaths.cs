namespace Pso2ShapeStudio.Core.Tests;

/// <summary>
/// Locates optional, non-redistributable integration-test fixtures without
/// embedding workstation paths in the public source tree.
/// </summary>
internal static class TestPaths
{
    public static string ReferenceAqp => UnderTestData("tae", "pl_rbd_201630_bw.aqp");

    public static string ReferenceAqn => UnderTestData("tae", "pl_rbd_201630_bw.aqn");

    public static string ReferenceShape => UnderTestData("tae", "pl_rbd_201630_bw_sa.aqm");

    public static string GoldenFnp => UnderTestData("golden_test", "golden_fnp.json");

    public static string GoldenMale => UnderTestData("golden_test", "golden_male.json");

    public static string GoldenShape => UnderTestData("golden_test", "golden_sa.aqm");

    public static string GoldenFocuslitePose =>
        UnderTestData("golden_test", "focuslite_pose_blender.json");

    public static string ReferenceFnp => RequireFile("PSO2_SHAPE_REFERENCE_FNP");

    public static string FocusliteFnp => RequireFile("PSO2_SHAPE_FOCUSLITE_FNP");

    /// <summary>
    /// A male character file (m*p). The proportion tables differ by gender,
    /// and male files were unopenable at first, so the male half needs its
    /// own fixture rather than sharing the female reference.
    /// </summary>
    public static string MaleCharacter => RequireFile("PSO2_SHAPE_MALE_CHARACTER");

    public static string GameDirectory => RequireDirectory("PSO2_GAME_DIR");

    private static string UnderTestData(params string[] components)
    {
        var root = RequireDirectory("PSO2_SHAPE_TEST_DATA");
        return RequireFile(Path.Combine([root, .. components]), "PSO2_SHAPE_TEST_DATA");
    }

    private static string RequireFile(string variable) =>
        RequireFile(ReadEnvironment(variable), variable);

    private static string RequireFile(string path, string variable)
    {
        var fullPath = Path.GetFullPath(path);
        return File.Exists(fullPath)
            ? fullPath
            : throw new FileNotFoundException(
                $"{variable} does not resolve to the required test file.", fullPath);
    }

    private static string RequireDirectory(string variable)
    {
        var fullPath = Path.GetFullPath(ReadEnvironment(variable));
        return Directory.Exists(fullPath)
            ? fullPath
            : throw new DirectoryNotFoundException(
                $"{variable} does not resolve to an existing directory: {fullPath}");
    }

    private static string ReadEnvironment(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Set {variable} to run integration tests that use local PSO2 reference data.");
    }
}
