namespace Pso2ShapeStudio.Formats;

public sealed record SkinTextureArchive(
    string SourcePath,
    RenderSkinTextureSet TextureSets,
    int DdsCount)
{
    public RenderTextureSet Textures => TextureSets.Base;

    public RenderTextureSet MuscleTextures => TextureSets.Muscle;

    public RenderTexture DiffuseTexture => Textures.Diffuse
        ?? throw new InvalidDataException($"Skin archive has no diffuse texture: {SourcePath}");
}

public static class SkinTextureLoader
{
    public static SkinTextureArchive Load(string path, int adjustedId)
    {
        var archive = IceArchive.Load(path);
        var ddsEntries = archive.Entries
            .Where(entry => Path.GetExtension(entry.Name)
                .Equals(".dds", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var names = ddsEntries.Select(entry => entry.Name).ToArray();
        RenderTexture? Decode(string? name)
        {
            if (name is null)
            {
                return null;
            }

            var entry = ddsEntries.First(value =>
                string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase));
            return DdsTextureDecoder.Decode(entry.Name, entry.Data);
        }

        var baseNames = new[] { 'd', 'm', 'n', 's' }
            .ToDictionary(
                suffix => suffix,
                suffix => SelectTextureName(names, adjustedId, suffix));
        var baseTextures = new RenderTextureSet(
            Decode(baseNames['d']),
            Decode(baseNames['m']),
            Decode(baseNames['n']),
            Decode(baseNames['s']));
        if (baseTextures.Diffuse is null)
        {
            throw new InvalidDataException(
                $"Skin ICE contains no diffuse texture for ID {adjustedId}: {path}");
        }

        RenderTexture? DecodeMuscle(char suffix, RenderTexture? fallback)
        {
            var name = SelectExactTextureName(names, adjustedId + 1, suffix);
            if (name is null)
            {
                return fallback;
            }

            var baseName = baseNames[suffix];
            if (baseName is not null)
            {
                var muscleEntry = ddsEntries.First(value =>
                    string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase));
                var baseEntry = ddsEntries.First(value =>
                    string.Equals(value.Name, baseName, StringComparison.OrdinalIgnoreCase));
                if (muscleEntry.Data.AsSpan().SequenceEqual(baseEntry.Data))
                {
                    return fallback;
                }
            }

            return Decode(name);
        }

        var muscleTextures = new RenderTextureSet(
            DecodeMuscle('d', baseTextures.Diffuse),
            DecodeMuscle('m', baseTextures.Mask),
            DecodeMuscle('n', baseTextures.Normal),
            DecodeMuscle('s', baseTextures.Multi));

        return new SkinTextureArchive(
            archive.SourcePath,
            new RenderSkinTextureSet(baseTextures, muscleTextures),
            ddsEntries.Length);
    }

    public static string? SelectDiffuseTextureName(
        IEnumerable<string> names,
        int adjustedId)
    {
        return SelectTextureName(names, adjustedId, 'd');
    }

    public static string? SelectTextureName(
        IEnumerable<string> names,
        int adjustedId,
        char suffix)
    {
        var available = names.ToArray();
        var expected = $"pl_rbd_{adjustedId:000000}_sk_{suffix}.dds";
        return available.FirstOrDefault(name =>
                   string.Equals(Path.GetFileName(name), expected, StringComparison.OrdinalIgnoreCase))
               ?? available.FirstOrDefault(name =>
                   Path.GetFileNameWithoutExtension(name)
                       .EndsWith($"_sk_{suffix}", StringComparison.OrdinalIgnoreCase));
    }

    private static string? SelectExactTextureName(
        IEnumerable<string> names,
        int adjustedId,
        char suffix)
    {
        var expected = $"pl_rbd_{adjustedId:000000}_sk_{suffix}.dds";
        return names.FirstOrDefault(name =>
            string.Equals(Path.GetFileName(name), expected, StringComparison.OrdinalIgnoreCase));
    }
}
