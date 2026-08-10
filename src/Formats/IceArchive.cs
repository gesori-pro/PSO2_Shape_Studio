using AquaModelLibrary.Helpers.Ice;

namespace Pso2ShapeStudio.Formats;

public sealed record IceArchiveEntry(string Name, byte[] Data);

public sealed record IceArchive(
    string SourcePath,
    IReadOnlyList<IceArchiveEntry> GroupOne,
    IReadOnlyList<IceArchiveEntry> GroupTwo)
{
    public IReadOnlyList<IceArchiveEntry> Entries => [.. GroupOne, .. GroupTwo];

    public static IceArchive Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var source = Zamboni.IceFile.LoadIceFile(stream);
        return new IceArchive(
            fullPath,
            source.groupOneFiles.Select(ReadEntry).ToArray(),
            source.groupTwoFiles.Select(ReadEntry).ToArray());
    }

    private static IceArchiveEntry ReadEntry(byte[] source)
    {
        if (source.Length < 0x10)
        {
            throw new InvalidDataException("ICE entry is shorter than its fixed header.");
        }

        var name = Zamboni.IceFile.getFileName(source);
        var headerSize = BitConverter.ToInt32(source, 0xC);
        if (headerSize < 0x10 || headerSize > source.Length)
        {
            throw new InvalidDataException(
                $"ICE entry '{name}' has invalid header size {headerSize} for {source.Length} bytes.");
        }

        byte[] data;
        try
        {
            data = IceMethods.RemoveIceEnvelope(source);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new InvalidDataException($"ICE entry '{name}' has an invalid envelope.", exception);
        }

        return new IceArchiveEntry(name, data);
    }
}
