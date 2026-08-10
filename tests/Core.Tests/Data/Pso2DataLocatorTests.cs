using Pso2ShapeStudio.GameData;

namespace Pso2ShapeStudio.Core.Tests.Data;

public sealed class Pso2DataLocatorTests
{
    [Fact]
    public void ValidateSelectedPath_AcceptsInstallBinAndDataFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pso2-data-{Guid.NewGuid():N}");
        var data = Path.Combine(root, "pso2_bin", "data");
        Directory.CreateDirectory(Path.Combine(data, "win32"));
        File.WriteAllBytes(Path.Combine(data, "win32", new string('a', 32)), [1]);
        try
        {
            Assert.Equal(data, Pso2DataLocator.NormalizeDataPath(root));
            Assert.Equal(data, Pso2DataLocator.NormalizeDataPath(Path.Combine(root, "pso2_bin")));
            Assert.Equal(data, Pso2DataLocator.NormalizeDataPath(data));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidateSelectedPath_RejectsOrdinaryAndEmptyStorageFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pso2-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var ordinary = Pso2DataLocator.ValidateSelectedPath(root);
            Assert.False(ordinary.IsValid);
            Assert.Equal(Pso2DataPathError.DataStructureNotFound, ordinary.Error);

            Directory.CreateDirectory(Path.Combine(root, "pso2_bin", "data", "win32"));
            var emptyStorage = Pso2DataLocator.ValidateSelectedPath(root);
            Assert.False(emptyStorage.IsValid);
            Assert.Equal(Pso2DataPathError.GameFilesNotFound, emptyStorage.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidateSelectedPath_AcceptsSplitRebootStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pso2-data-{Guid.NewGuid():N}");
        var data = Path.Combine(root, "pso2_bin", "data");
        var prefix = Path.Combine(data, "win32reboot", "cf");
        Directory.CreateDirectory(prefix);
        File.WriteAllBytes(Path.Combine(prefix, new string('5', 30)), [1]);
        try
        {
            var validation = Pso2DataLocator.ValidateSelectedPath(root);
            Assert.True(validation.IsValid);
            Assert.Equal(data, validation.DataPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HashAndHighQualityName_MatchReferenceAddon()
    {
        const string normal = "character/making_reboot/pl_bw_201630.ice";
        const string highQuality = "character/making_reboot_ex/pl_bw_201630_ex.ice";

        Assert.Equal(highQuality, Pso2DataLocator.GetHighQualityFileName(normal));
        Assert.Equal(
            "cf540ec3ff917cd65e9fd3e67f4fecfa",
            Pso2DataLocator.ComputeHash(highQuality));
    }

    [Fact]
    public void Resolve_PrefersHighQualityAcrossDocumentedRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pso2-data-{Guid.NewGuid():N}");
        var data = Path.Combine(root, "pso2_bin", "data");
        Directory.CreateDirectory(Path.Combine(data, "win32"));
        try
        {
            const string normal = "character/making_reboot/pl_bw_201630.ice";
            var highQuality = Pso2DataLocator.GetHighQualityFileName(normal)!;
            var hash = Pso2DataLocator.ComputeHash(highQuality);
            var path = Path.Combine(data, "win32", hash);
            File.WriteAllBytes(path, [1, 2, 3]);

            var locator = new Pso2DataLocator(root);
            var resolved = locator.Resolve(normal);

            Assert.NotNull(resolved);
            Assert.True(resolved.IsHighQuality);
            Assert.Equal(path, resolved.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
