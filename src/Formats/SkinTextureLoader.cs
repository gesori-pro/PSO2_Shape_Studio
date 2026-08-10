namespace Pso2ShapeStudio.Formats;

public sealed record SkinTextureArchive(
    string SourcePath,
    RenderTextureSet Textures,
    int DdsCount)
{
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
        RenderTexture? Decode(char suffix)
        {
            var name = SelectTextureName(names, adjustedId, suffix);
            if (name is null)
            {
                return null;
            }

            var entry = ddsEntries.First(value =>
                string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase));
            return DdsTextureDecoder.Decode(entry.Name, entry.Data);
        }

        var textures = new RenderTextureSet(
            Decode('d'),
            Decode('m'),
            Decode('n'),
            Decode('s'));
        if (textures.Diffuse is null)
        {
            throw new InvalidDataException(
                $"Skin ICE contains no diffuse texture for ID {adjustedId}: {path}");
        }

        return new SkinTextureArchive(
            archive.SourcePath,
            textures,
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
}
