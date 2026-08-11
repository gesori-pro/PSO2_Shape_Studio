namespace Pso2ShapeStudio.Formats;

using Pso2ShapeStudio.Character;

/// <summary>
/// Everything a model ICE yields in one pass. The shape-adjust entry stays
/// raw bytes: parsing it is the rigging module's business, and keeping it
/// out of here is what keeps Formats free of a dependency cycle.
/// </summary>
public sealed record GameModelArchive(
    string SourcePath,
    IReadOnlyList<RenderModel> Models,
    AqnSkeleton? Skeleton,
    byte[]? ShapeAdjustData,
    int EntryCount,
    int DdsCount);

public static class ModelArchiveLoader
{
    public static GameModelArchive Load(
        string path,
        Pso2ColorMapping? bodyColorMapping = null)
    {
        var archive = IceArchive.Load(path);
        var entries = archive.Entries;
        var textures = entries
            .Where(entry => Extension(entry.Name) == ".dds")
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Data,
                StringComparer.OrdinalIgnoreCase);

        var aqpEntries = entries.Where(entry => Extension(entry.Name) == ".aqp").ToArray();
        if (aqpEntries.Length == 0)
        {
            throw new InvalidDataException($"ICE archive contains no AQP model: {path}");
        }

        var aqnEntries = entries.Where(entry => Extension(entry.Name) == ".aqn").ToArray();
        var models = aqpEntries.Select(entry => AqpLoader.Load(
            entry.Data,
            $"{archive.SourcePath}::{entry.Name}",
            textures,
            bodyColorMapping)).ToArray();

        var primaryStem = Path.GetFileNameWithoutExtension(aqpEntries[0].Name);
        var skeletonEntry = aqnEntries.FirstOrDefault(entry =>
                                string.Equals(
                                    Path.GetFileNameWithoutExtension(entry.Name),
                                    primaryStem,
                                    StringComparison.OrdinalIgnoreCase))
                            ?? aqnEntries.FirstOrDefault();
        var skeleton = skeletonEntry is null
            ? null
            : AqnSkeleton.Load(
                skeletonEntry.Data,
                $"{archive.SourcePath}::{skeletonEntry.Name}");

        var shapeEntry = entries.FirstOrDefault(entry =>
            entry.Name.EndsWith("_sa.aqm", StringComparison.OrdinalIgnoreCase));

        return new GameModelArchive(
            archive.SourcePath,
            models,
            skeleton,
            shapeEntry?.Data,
            entries.Count,
            textures.Count);
    }

    private static string Extension(string name) => Path.GetExtension(name).ToLowerInvariant();
}
