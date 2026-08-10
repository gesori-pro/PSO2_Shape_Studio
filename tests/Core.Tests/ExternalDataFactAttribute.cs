namespace Pso2ShapeStudio.Core.Tests;

/// <summary>
/// Skips integration tests at discovery time when their local, optional PSO2
/// fixtures are not configured. This is compatible with the xUnit v2 core
/// used by the project; runtime dynamic skips require xUnit v3.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class ExternalDataFactAttribute : FactAttribute
{
    public ExternalDataFactAttribute(params string[] variables)
    {
        foreach (var variable in variables)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value))
            {
                Skip = $"Set {variable} to run this local PSO2 reference-data test.";
                return;
            }

            var fullPath = Path.GetFullPath(value);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                Skip = $"{variable} does not resolve to an existing file or directory: {fullPath}";
                return;
            }
        }
    }
}
