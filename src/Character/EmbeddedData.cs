using System.Reflection;

namespace Pso2ShapeStudio.Character;

internal static class EmbeddedData
{
    public static Stream Open(string relativeName)
    {
        var assembly = typeof(EmbeddedData).Assembly;
        var suffix = relativeName.Replace('/', '.').Replace('\\', '.');
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new FileNotFoundException($"Embedded data resource was not found: {relativeName}");
        }

        return assembly.GetManifestResourceStream(resourceName) ??
               throw new FileNotFoundException($"Embedded data resource could not be opened: {resourceName}");
    }
}
