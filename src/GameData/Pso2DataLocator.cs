using AquaModelLibrary.Helpers;

namespace Pso2ShapeStudio.GameData;

public sealed record ResolvedGameFile(
    string RequestedFileName,
    string ResolvedFileName,
    string Hash,
    string Path,
    bool IsHighQuality);

public enum Pso2DataPathError
{
    None,
    NotSelected,
    InvalidPath,
    FolderNotFound,
    DataStructureNotFound,
    GameFilesNotFound,
    AccessDenied,
}

public sealed record Pso2DataPathValidation(
    bool IsValid,
    string SelectedPath,
    string? DataPath,
    Pso2DataPathError Error,
    string Message);

public sealed class Pso2DataLocator
{
    private const string ClassicPrefix = "character/making/";
    private const string RebootPrefix = "character/making_reboot/";
    private const string RebootExPrefix = "character/making_reboot_ex/";
    private static readonly string[] StorageFolderNames =
        ["win32", "win32reboot", "win32_na", "win32reboot_na"];

    public Pso2DataLocator(string selectedPath)
    {
        DataPath = NormalizeDataPath(selectedPath);
        BinPath = Directory.GetParent(DataPath)?.FullName
                  ?? throw new DirectoryNotFoundException($"PSO2 data parent was not found: {DataPath}");
    }

    public string DataPath { get; }

    public string BinPath { get; }

    public static string ComputeHash(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return HashHelpers.GetFileHash(fileName.Replace('\\', '/'));
    }

    public static string? GetHighQualityFileName(string fileName)
    {
        var normalized = fileName.Replace('\\', '/');
        if (!normalized.StartsWith(RebootPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var extension = Path.GetExtension(normalized);
        var stem = extension.Length == 0 ? normalized : normalized[..^extension.Length];
        return RebootExPrefix + stem[RebootPrefix.Length..] + "_ex" + extension;
    }

    public ResolvedGameFile? Resolve(string fileName, bool preferHighQuality = true)
    {
        var normalized = fileName.Replace('\\', '/');
        var highQuality = GetHighQualityFileName(normalized);
        IEnumerable<(string Name, bool HighQuality)> candidates = preferHighQuality && highQuality is not null
            ? [(highQuality, true), (normalized, false)]
            : highQuality is not null
                ? [(normalized, false), (highQuality, true)]
                : [(normalized, false)];

        foreach (var candidate in candidates)
        {
            var hash = ComputeHash(candidate.Name);
            var path = ResolveHash(hash);
            if (path is not null)
            {
                return new ResolvedGameFile(
                    normalized,
                    candidate.Name,
                    hash,
                    path,
                    candidate.HighQuality);
            }
        }

        return null;
    }

    public string? ResolveHash(string hash)
    {
        if (hash.Length != 32 || hash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("PSO2 data hashes must be 32 hexadecimal characters.", nameof(hash));
        }

        var normalized = hash.ToLowerInvariant();
        var rebootHash = HashHelpers.GetRebootHash(normalized)
            .Replace('\\', Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            Path.Combine(DataPath, "win32", normalized),
            Path.Combine(DataPath, "win32reboot", rebootHash),
            Path.Combine(DataPath, "win32_na", normalized),
            Path.Combine(DataPath, "win32reboot_na", rebootHash),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string NormalizeDataPath(string selectedPath)
    {
        var validation = ValidateSelectedPath(selectedPath);
        return validation.IsValid
            ? validation.DataPath!
            : throw new DirectoryNotFoundException(validation.Message);
    }

    public static Pso2DataPathValidation ValidateSelectedPath(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return new Pso2DataPathValidation(
                false,
                selectedPath ?? "",
                null,
                Pso2DataPathError.NotSelected,
                "No game folder was selected.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(selectedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new Pso2DataPathValidation(
                false,
                selectedPath,
                null,
                Pso2DataPathError.InvalidPath,
                $"The folder path is invalid: {exception.Message}");
        }

        if (!Directory.Exists(fullPath))
        {
            return new Pso2DataPathValidation(
                false,
                fullPath,
                null,
                Pso2DataPathError.FolderNotFound,
                $"The selected folder does not exist: {fullPath}");
        }

        var candidates = new[]
        {
            fullPath,
            Path.Combine(fullPath, "data"),
            Path.Combine(fullPath, "pso2_bin", "data"),
        }.Distinct(StringComparer.OrdinalIgnoreCase);
        var dataPath = candidates.FirstOrDefault(HasStorageFolder);
        if (dataPath is null)
        {
            return new Pso2DataPathValidation(
                false,
                fullPath,
                null,
                Pso2DataPathError.DataStructureNotFound,
                "PSO2 data folders were not found. Select the game installation folder, " +
                "pso2_bin, or pso2_bin\\data.");
        }

        try
        {
            if (!ContainsHashedGameFile(dataPath))
            {
                return new Pso2DataPathValidation(
                    false,
                    fullPath,
                    dataPath,
                    Pso2DataPathError.GameFilesNotFound,
                    "win32/win32reboot exists but contains no game data files. " +
                    "Check that game installation or patching is complete.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new Pso2DataPathValidation(
                false,
                fullPath,
                dataPath,
                Pso2DataPathError.AccessDenied,
                $"The game data folder could not be accessed: {exception.Message}");
        }

        return new Pso2DataPathValidation(
            true,
            fullPath,
            Path.GetFullPath(dataPath),
            Pso2DataPathError.None,
            "The PSO2 game data folder was verified.");
    }

    private static bool HasStorageFolder(string dataPath) =>
        StorageFolderNames.Any(name => Directory.Exists(Path.Combine(dataPath, name)));

    private static bool ContainsHashedGameFile(string dataPath)
    {
        foreach (var folderName in StorageFolderNames)
        {
            var storagePath = Path.Combine(dataPath, folderName);
            if (!Directory.Exists(storagePath))
            {
                continue;
            }

            if (!folderName.Contains("reboot", StringComparison.Ordinal))
            {
                if (Directory.EnumerateFiles(storagePath)
                    .Any(path => IsHexName(Path.GetFileName(path), 32) && new FileInfo(path).Length > 0))
                {
                    return true;
                }

                continue;
            }

            foreach (var prefixPath in Directory.EnumerateDirectories(storagePath))
            {
                if (!IsHexName(Path.GetFileName(prefixPath), 2))
                {
                    continue;
                }

                if (Directory.EnumerateFiles(prefixPath)
                    .Any(path => IsHexName(Path.GetFileName(path), 30) && new FileInfo(path).Length > 0))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsHexName(string name, int length) =>
        name.Length == length && name.All(Uri.IsHexDigit);
}
