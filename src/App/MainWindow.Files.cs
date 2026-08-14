using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Pso2ShapeStudio.App.Localization;
using Pso2ShapeStudio.App.Rendering;
using Pso2ShapeStudio.Character;
using Pso2ShapeStudio.GameData;
using Pso2ShapeStudio.Formats;
using Pso2ShapeStudio.Rigging;

namespace Pso2ShapeStudio.App;

// Opening files: dialogs, drag-drop, command line, and the model list.
public partial class MainWindow : Window
{
    private async void OpenFiles(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L(AppText.ModelPickerTitle),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(L(AppText.ModelPickerType))
                {
                    Patterns = ["*.aqp", "*.aqn", "*.ice"],
                },
            ],
        });
        await LoadPathsAsync(files.Select(file => file.Path.LocalPath));
    }

    private async void FilesDropped(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            return;
        }

        e.Handled = true;
        await LoadPathsAsync(files.Select(file => file.Path.LocalPath));
    }

    private async void LoadCommandLineFiles(object? sender, EventArgs e)
    {
        Opened -= LoadCommandLineFiles;
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var dataArgument = arguments.FirstOrDefault(argument =>
            argument.StartsWith("--data=", StringComparison.OrdinalIgnoreCase));
        if (dataArgument is not null)
        {
            await ConfigureDataFolderAsync(
                dataArgument[7..],
                rebuild: arguments.Contains("--rebuild-catalog", StringComparer.OrdinalIgnoreCase));
        }
        else
        {
            await RestoreDataFolderAsync();
        }

        var languageArgument = arguments.FirstOrDefault(argument =>
            argument.StartsWith("--language=", StringComparison.OrdinalIgnoreCase));
        if (languageArgument is not null)
        {
            SetLanguage(AppLocalizer.ParseLanguage(languageArgument[11..]));
            await SaveSettingsAsync();
        }

        var paths = arguments
            .Where(path => !path.StartsWith("--", StringComparison.Ordinal) && IsSupportedPath(path))
            .ToArray();
        if (paths.Length > 0)
        {
            await LoadPathsAsync(paths);
        }

        var searchArgument = arguments.FirstOrDefault(argument =>
            argument.StartsWith("--search=", StringComparison.OrdinalIgnoreCase));
        if (searchArgument is not null && _catalog is not null)
        {
            ModelSearchBox.Text = searchArgument[9..];
            await SearchModelsAsync();
        }

        foreach (var argument in arguments)
        {
            if (argument.StartsWith("--breast=", StringComparison.OrdinalIgnoreCase) &&
                float.TryParse(argument[9..], out var breast))
            {
                SetUniformDiagnosticScale("breast", breast);
            }
            else if (argument.StartsWith("--waist=", StringComparison.OrdinalIgnoreCase) &&
                     float.TryParse(argument[8..], out var waist))
            {
                SetUniformDiagnosticScale("waist", waist);
            }
        }

        if (arguments.Contains("--stress", StringComparer.OrdinalIgnoreCase))
        {
            StartStressTest();
        }
    }

    private async Task LoadPathsAsync(
        IEnumerable<string> inputPaths,
        IReadOnlyDictionary<string, Pso2ColorMapping>? bodyColorMappings = null)
    {
        var paths = inputPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && IsSupportedPath(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        IsEnabled = false;
        StatusText.Text = L(AppText.ReadingFiles);
        try
        {
            var result = await Task.Run(() => LoadFiles(paths, bodyColorMappings));
            if (result.Skeleton is not null)
            {
                _skeleton = result.Skeleton;
                RebuildSliderGroups();
            }

            if (result.Proportions is not null)
            {
                _proportions = result.Proportions;
            }

            if (result.Colors is not null)
            {
                _characterColors = result.Colors;
                _characterColorsFromFile = true;
                Viewport.SetCharacterColors(_characterColors);
            }

            if (result.ShapeAdjust is not null)
            {
                CommitPendingShapeEdit();
                var before = CaptureShapeState();
                _shapeAdjust = result.ShapeAdjust;
                _profile = result.ShapeAdjust.ToProfile(ActiveShapeGroups());
                RecordShapeEdit(before);
                SetEditorsFromProfile();
            }

            foreach (var model in result.Models)
            {
                var entry = new ModelEntry(model, _language);
                entry.VisibilityChanged += ModelVisibilityChanged;
                Models.Add(entry);
            }

            RefreshOrnamentControls();
            RefreshViewportModels();
            RebuildPose();
            UpdateCommandAvailability();

            var parts = new List<string>();
            if (result.Models.Count > 0) parts.Add(L(AppText.ModelsCount, result.Models.Count));
            var textureCount = result.Models.Sum(model => model.TextureCount);
            if (textureCount > 0) parts.Add(L(AppText.TexturesCount, textureCount));
            if (result.ArchiveDdsCount > 0) parts.Add(L(AppText.IceDdsCount, result.ArchiveDdsCount));
            if (result.ArchiveEntryCount > 0) parts.Add(L(AppText.IceEntriesCount, result.ArchiveEntryCount));
            if (_skeleton is not null) parts.Add(L(AppText.BonesCount, _skeleton.Bones.Count));
            if (result.Proportions is not null)
            {
                parts.Add(L(AppText.CharacterBonesCount, result.Proportions.Bones.Count));
            }
            if (result.ShapeAdjust is not null)
            {
                parts.Add(L(AppText.AqmAdjustmentsCount, result.ShapeAdjust.Adjustments.Count));
            }
            StatusText.Text = parts.Count > 0 ? string.Join(" · ", parts) : L(AppText.NoLoadableData);
        }
        catch (Exception exception)
        {
            StatusText.Text = L(AppText.LoadFailed, exception.Message);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private LoadResult LoadFiles(
        IReadOnlyList<string> paths,
        IReadOnlyDictionary<string, Pso2ColorMapping>? bodyColorMappings)
    {
        AqnSkeleton? skeleton = null;
        ProportionResult? proportions = null;
        CharacterColorPalette? colors = null;
        ShapeAdjustFile? shapeAdjust = null;
        var archiveEntryCount = 0;
        var archiveDdsCount = 0;
        var explicitAqm = paths.FirstOrDefault(path => Extension(path) == ".aqm");

        var aqnPath = paths.FirstOrDefault(path => Extension(path) == ".aqn");
        if (aqnPath is not null)
        {
            skeleton = AqnSkeleton.Load(aqnPath);
        }

        var models = new List<RenderModel>();
        foreach (var path in paths.Where(IsArchivePath))
        {
            var mapping = bodyColorMappings is not null &&
                          bodyColorMappings.TryGetValue(path, out var configuredMapping)
                ? configuredMapping
                : (Pso2ColorMapping?)null;
            var archive = ModelArchiveLoader.Load(path, mapping);
            models.AddRange(archive.Models);
            skeleton ??= archive.Skeleton;
            shapeAdjust ??= archive.ShapeAdjustData is { } shapeBytes
                ? ShapeAdjustFile.Load(shapeBytes)
                : null;
            archiveEntryCount += archive.EntryCount;
            archiveDdsCount += archive.DdsCount;
        }

        foreach (var path in paths.Where(path => Extension(path) == ".aqp"))
        {
            if (skeleton is null)
            {
                var adjacentAqn = Path.ChangeExtension(path, ".aqn");
                if (File.Exists(adjacentAqn))
                {
                    skeleton = AqnSkeleton.Load(adjacentAqn);
                }
                else if (_skeleton is null)
                {
                    throw new FileNotFoundException(L(AppText.AqnMissing), adjacentAqn);
                }
            }

            models.Add(AqpLoader.Load(path));
            if (explicitAqm is null && shapeAdjust is null)
            {
                var adjacentShape = Path.Combine(
                    Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_sa.aqm");
                if (File.Exists(adjacentShape))
                {
                    shapeAdjust = ShapeAdjustFile.Load(adjacentShape);
                }
            }
        }

        var characterPath = paths.FirstOrDefault(CharacterFile.IsSupportedPath);
        if (characterPath is not null)
        {
            var character = CharacterFile.Load(characterPath);
            proportions = Proportions.Compute(character);
            colors = CharacterColorPalette.FromCharacter(character);
        }

        if (explicitAqm is not null)
        {
            shapeAdjust = ShapeAdjustFile.Load(explicitAqm);
        }

        return new LoadResult(
            skeleton,
            models,
            proportions,
            colors,
            shapeAdjust,
            archiveEntryCount,
            archiveDdsCount);
    }

    private void ModelVisibilityChanged(object? sender, EventArgs e) => RefreshViewportModels();

    private void RemoveModel(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ModelEntry entry })
        {
            return;
        }

        entry.VisibilityChanged -= ModelVisibilityChanged;
        if (!Models.Remove(entry))
        {
            return;
        }

        RefreshOrnamentControls();
        RefreshViewportModels();
        StatusText.Text = L(AppText.ModelRemoved, entry.DisplayName);
        e.Handled = true;
    }

    private void RefreshViewportModels() =>
        Viewport.SetModels(Models.Where(entry => entry.Visible).Select(entry => entry.Model));

    private static bool IsSupportedPath(string path)
    {
        var extension = Extension(path);
        return extension is ".aqp" or ".aqn" or ".aqm" or ".ice" ||
               IsArchivePath(path) ||
               CharacterFile.IsSupportedPath(path);
    }

    private static bool IsArchivePath(string path)
    {
        if (Extension(path) == ".ice")
        {
            return true;
        }

        var name = Path.GetFileName(path);
        return Path.GetExtension(name).Length == 0 &&
               name.Length == 32 &&
               name.All(Uri.IsHexDigit);
    }

    private static string Extension(string path) => Path.GetExtension(path).ToLowerInvariant();

    private static Vector3 ToVector3(IReadOnlyList<double> value) =>
        new((float)value[0], (float)value[1], (float)value[2]);
}
