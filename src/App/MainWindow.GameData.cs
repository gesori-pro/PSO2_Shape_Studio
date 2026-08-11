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

// Game folder, model catalog, wear search, and skin texture selection.
public partial class MainWindow : Window
{
    private async void SkinType1Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_applyingSkinSelections)
        {
            await ApplySkinSelectionAsync(Pso2BodyType.Type1, saveSettings: true);
        }
    }

    private async void SkinType2Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_applyingSkinSelections)
        {
            await ApplySkinSelectionAsync(Pso2BodyType.Type2, saveSettings: true);
        }
    }

    private async void SelectDataFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L(AppText.GameFolderPickerTitle),
            AllowMultiple = false,
        });
        if (folders.Count == 0)
        {
            return;
        }

        await ConfigureDataFolderAsync(folders[0].Path.LocalPath, rebuild: true);
    }

    private async void RebuildCatalog(object? sender, RoutedEventArgs e)
    {
        if (_dataLocator is not null)
        {
            await BuildCatalogAsync(_dataLocator);
        }
    }

    private async void SearchModels(object? sender, RoutedEventArgs e) => await SearchModelsAsync();

    private async Task SearchModelsAsync()
    {
        if (_catalog is null || _dataLocator is null)
        {
            return;
        }

        SearchModelsButton.IsEnabled = false;
        try
        {
            var query = ModelSearchBox.Text ?? "";
            var categories = SelectedSearchCategories();
            var records = await Task.Run(() => _catalog.Search(query, 100, categories));
            SearchResults.Clear();
            foreach (var record in records)
            {
                SearchResults.Add(new ModelSearchResultViewModel(
                    record, _dataLocator.Resolve(record.FileName), _language));
            }

            StatusText.Text = L(AppText.SearchResultCount, records.Count);
        }
        catch (Exception exception)
        {
            StatusText.Text = L(AppText.ModelSearchFailed, exception.Message);
        }
        finally
        {
            SearchModelsButton.IsEnabled = _catalog.Exists;
        }
    }

    /// <summary>
    /// The checked subset of the wear whitelist. Search never leaves the
    /// whitelist - hair, eyes, stickers and the rest stay out even with
    /// every box unchecked, which is treated the same as all checked so an
    /// accidental clear does not silently search nothing.
    /// </summary>
    private IReadOnlyCollection<string> SelectedSearchCategories()
    {
        var selected = new List<string>(4);
        if (WearBasewearCheck.IsChecked == true) selected.Add("basewear");
        if (WearOuterwearCheck.IsChecked == true) selected.Add("outerwear");
        if (WearSetwearCheck.IsChecked == true) selected.Add("setwear");
        if (WearCostumeCheck.IsChecked == true) selected.Add("costume");
        return selected.Count > 0 ? selected : ModelCatalog.WearObjectTypes;
    }

    private async Task PopulateSkinOptionsAsync()
    {
        if (_catalog is null || _dataLocator is null)
        {
            ClearSkinOptions();
            return;
        }

        var records = await Task.Run(() => _catalog.GetByObjectType("skin"));
        var type1 = CreateSkinOptions(records, Pso2BodyType.Type1, DefaultSkinType1Id);
        var type2 = CreateSkinOptions(records, Pso2BodyType.Type2, DefaultSkinType2Id);

        _applyingSkinSelections = true;
        try
        {
            ReplaceOptions(SkinType1Options, type1);
            ReplaceOptions(SkinType2Options, type2);
            SkinType1ComboBox.SelectedItem =
                type1.FirstOrDefault(option => option.Id == _selectedSkinType1Id) ?? type1.FirstOrDefault();
            SkinType2ComboBox.SelectedItem =
                type2.FirstOrDefault(option => option.Id == _selectedSkinType2Id) ?? type2.FirstOrDefault();
            SkinType1ComboBox.IsEnabled = type1.Count > 0;
            SkinType2ComboBox.IsEnabled = type2.Count > 0;
            await ApplySkinSelectionAsync(Pso2BodyType.Type1, saveSettings: false);
            await ApplySkinSelectionAsync(Pso2BodyType.Type2, saveSettings: false);
            await SaveSettingsAsync();
        }
        finally
        {
            _applyingSkinSelections = false;
        }
    }

    private List<SkinOptionViewModel> CreateSkinOptions(
        IEnumerable<ModelCatalogRecord> records,
        Pso2BodyType bodyType,
        int baseId)
    {
        var rangeStart = bodyType == Pso2BodyType.Type1 ? 100000 : 200000;
        var rangeEnd = rangeStart + 100000;
        return records
            .Where(record => record.Id >= rangeStart && record.Id < rangeEnd)
            .Where(record =>
                !string.IsNullOrWhiteSpace(record.NameEnglish) ||
                !string.IsNullOrWhiteSpace(record.NameJapanese))
            .Select(record => new SkinOptionViewModel(
                record,
                _dataLocator!.Resolve(record.FileName),
                _language))
            .OrderBy(option => option.Id == baseId ? 0 : 1)
            .ThenBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private async Task ApplySkinSelectionAsync(Pso2BodyType bodyType, bool saveSettings)
    {
        var selected = bodyType == Pso2BodyType.Type1
            ? SkinType1ComboBox.SelectedItem as SkinOptionViewModel
            : SkinType2ComboBox.SelectedItem as SkinOptionViewModel;
        if (selected is null)
        {
            return;
        }

        if (bodyType == Pso2BodyType.Type1)
        {
            _selectedSkinType1Id = selected.Id;
        }
        else
        {
            _selectedSkinType2Id = selected.Id;
        }

        try
        {
            var path = selected.ResolvedPath
                       ?? throw new FileNotFoundException(selected.Record.FileName);
            if (!_skinTextureCache.TryGetValue(path, out var texture))
            {
                var archive = await Task.Run(() =>
                    SkinTextureLoader.Load(path, selected.Record.AdjustedId));
                texture = archive.Textures;
                _skinTextureCache[path] = texture;
            }

            if (bodyType == Pso2BodyType.Type1)
            {
                _skinTextureType1 = texture;
            }
            else
            {
                _skinTextureType2 = texture;
            }

            Viewport.SetSkinTextures(_skinTextureType1, _skinTextureType2);
            var diffuse = texture.Diffuse
                          ?? throw new InvalidDataException("Selected skin has no diffuse texture.");
            StatusText.Text = L(
                AppText.SkinLoaded, selected.DisplayName, diffuse.Width, diffuse.Height);
        }
        catch (Exception exception)
        {
            StatusText.Text = L(AppText.SkinLoadFailed, exception.Message);
        }

        if (saveSettings)
        {
            try
            {
                await SaveSettingsAsync();
            }
            catch (Exception exception)
            {
                StatusText.Text = L(AppText.DataPrepareFailedStatus, exception.Message);
            }
        }
    }

    private static void ReplaceOptions(
        ObservableCollection<SkinOptionViewModel> target,
        IEnumerable<SkinOptionViewModel> source)
    {
        target.Clear();
        foreach (var option in source)
        {
            target.Add(option);
        }
    }

    private void ClearSkinOptions()
    {
        _applyingSkinSelections = true;
        try
        {
            SkinType1Options.Clear();
            SkinType2Options.Clear();
            SkinType1ComboBox.SelectedItem = null;
            SkinType2ComboBox.SelectedItem = null;
            SkinType1ComboBox.IsEnabled = false;
            SkinType2ComboBox.IsEnabled = false;
            _skinTextureType1 = null;
            _skinTextureType2 = null;
            _skinTextureCache.Clear();
            Viewport.SetSkinTextures(null, null);
        }
        finally
        {
            _applyingSkinSelections = false;
        }
    }

    /// <summary>
    /// A stale-schema catalog opens fine but classifies wear types under the
    /// old rules, so it must be rebuilt rather than reused.
    /// </summary>
    private bool IsCatalogCurrent()
    {
        try
        {
            return new ModelCatalog(CatalogPath).IsCurrentSchema;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async void LoadSearchResult(object? sender, RoutedEventArgs e)
    {
        if (ModelSearchResultsList.SelectedItem is not ModelSearchResultViewModel
            { ResolvedPath: not null } selected)
        {
            return;
        }

        // Setwear/basewear often carries hidden partner pieces (a linked
        // outerwear holding the physics-driven skirt or cape, a linked
        // innerwear underneath). Loading the outfit without them shows a
        // visibly broken garment, so resolve and load them together.
        var paths = new List<string> { selected.ResolvedPath };
        foreach (var linked in ResolveLinkedWear(selected.Record))
        {
            paths.Add(linked);
        }

        await LoadPathsAsync(paths);

        // A searched outfit should come up wearing skin, not gray flesh. If
        // neither skin set made it to the viewport (first run, or an earlier
        // failure the user never saw), retry the whole selection now.
        if (_skinTextureType1 is null && _skinTextureType2 is null && _catalog is not null)
        {
            await PopulateSkinOptionsAsync();
        }
    }

    /// <summary>
    /// Resolved file paths of an outfit's linked inner/outerwear, in the
    /// order they should stack under the outfit. Missing links, records the
    /// CMX no longer has, and unresolvable files are all skipped silently -
    /// the main outfit still loads on its own.
    /// </summary>
    private IReadOnlyList<string> ResolveLinkedWear(ModelCatalogRecord record)
    {
        if (_catalog is null || _dataLocator is null)
        {
            return [];
        }

        var paths = new List<string>(2);
        void Add(string objectType, int? id)
        {
            if (id is not int value)
            {
                return;
            }

            var linked = _catalog.FindByTypeAndId(objectType, value);
            if (linked is null)
            {
                return;
            }

            var resolved = _dataLocator.Resolve(linked.FileName);
            if (resolved is not null)
            {
                paths.Add(resolved.Path);
            }
        }

        Add("innerwear", record.LinkedInnerId);
        Add("outerwear", record.LinkedOuterId);
        return paths;
    }

    private async Task ConfigureDataFolderAsync(string selectedPath, bool rebuild)
    {
        var validation = Pso2DataLocator.ValidateSelectedPath(selectedPath);
        if (!validation.IsValid)
        {
            ShowDataFolderError(
                selectedPath,
                AppLocalizer.DataPathError(_language, validation),
                preserveExisting: true);
            return;
        }

        var locator = new Pso2DataLocator(validation.DataPath!);

        _dataLocator = locator;
        DataFolderText.Text = locator.DataPath;
        DataFolderText.Foreground = DataInfoBrush;
        DataFolderValidationText.Text = L(AppText.DataPathValid);
        DataFolderValidationText.Foreground = DataSuccessBrush;
        BuildCatalogButton.IsEnabled = true;

        try
        {
            await SaveSettingsAsync(locator.DataPath);

            if (rebuild || !File.Exists(CatalogPath) || !IsCatalogCurrent())
            {
                await BuildCatalogAsync(locator);
            }
            else
            {
                _catalog = new ModelCatalog(CatalogPath);
                SearchModelsButton.IsEnabled = true;
                DataFolderValidationText.Text = L(AppText.CacheReadyInline, _catalog.RecordCount);
                DataFolderValidationText.Foreground = DataSuccessBrush;
                StatusText.Text = L(AppText.CacheReadyStatus, _catalog.RecordCount);
                await PopulateSkinOptionsAsync();
            }
        }
        catch (Exception exception)
        {
            _catalog = null;
            ClearSkinOptions();
            SearchModelsButton.IsEnabled = false;
            DataFolderValidationText.Text = L(AppText.DataPrepareFailedInline, exception.Message);
            DataFolderValidationText.Foreground = DataErrorBrush;
            StatusText.Text = L(AppText.DataPrepareFailedStatus, exception.Message);
        }
    }

    private async Task BuildCatalogAsync(Pso2DataLocator locator)
    {
        var validation = Pso2DataLocator.ValidateSelectedPath(locator.DataPath);
        if (!validation.IsValid)
        {
            _dataLocator = null;
            _catalog = null;
            SearchResults.Clear();
            ClearSkinOptions();
            DataFolderText.Text = locator.DataPath;
            DataFolderText.Foreground = DataErrorBrush;
            DataFolderValidationText.Text = L(
                AppText.CacheUnavailable,
                AppLocalizer.DataPathError(_language, validation));
            DataFolderValidationText.Foreground = DataErrorBrush;
            BuildCatalogButton.IsEnabled = false;
            SearchModelsButton.IsEnabled = false;
            LoadSearchResultButton.IsEnabled = false;
            StatusText.Text = DataFolderValidationText.Text;
            return;
        }

        BuildCatalogButton.IsEnabled = false;
        BuildCatalogButton.Content = L(AppText.Refreshing);
        SelectDataFolderButton.IsEnabled = false;
        SearchModelsButton.IsEnabled = false;
        LoadSearchResultButton.IsEnabled = false;
        StatusText.Text = L(AppText.CacheBuildingStatus);
        DataFolderValidationText.Text = L(AppText.CacheBuildingInline);
        DataFolderValidationText.Foreground = DataSuccessBrush;
        try
        {
            var result = await Task.Run(() => ModelCatalog.BuildFromGame(locator, CatalogPath));
            _catalog = new ModelCatalog(result.DatabasePath);
            SearchModelsButton.IsEnabled = true;
            DataFolderValidationText.Text = L(
                AppText.CacheCompletedInline, result.RecordCount, result.Elapsed.TotalSeconds);
            DataFolderValidationText.Foreground = DataSuccessBrush;
            StatusText.Text = L(
                AppText.CacheCreatedStatus, result.RecordCount, result.Elapsed.TotalSeconds);
            await PopulateSkinOptionsAsync();
        }
        catch (Exception exception)
        {
            _catalog = null;
            ClearSkinOptions();
            DataFolderValidationText.Text = L(AppText.CacheFailedInline, exception.Message);
            DataFolderValidationText.Foreground = DataErrorBrush;
            StatusText.Text = L(AppText.CacheFailedStatus, exception.Message);
        }
        finally
        {
            SelectDataFolderButton.IsEnabled = true;
            BuildCatalogButton.Content = L(AppText.RefreshCache);
            BuildCatalogButton.IsEnabled = _dataLocator is not null;
        }
    }

    private void ShowDataFolderError(string selectedPath, string message, bool preserveExisting)
    {
        var keptExisting = preserveExisting && _dataLocator is not null;
        if (!keptExisting)
        {
            _dataLocator = null;
            _catalog = null;
            SearchResults.Clear();
            ClearSkinOptions();
            DataFolderText.Text = string.IsNullOrWhiteSpace(selectedPath)
                ? L(AppText.DataFolderUnspecified)
                : selectedPath;
            DataFolderText.Foreground = DataErrorBrush;
            BuildCatalogButton.IsEnabled = false;
            SearchModelsButton.IsEnabled = false;
            LoadSearchResultButton.IsEnabled = false;
        }
        else
        {
            DataFolderText.Text = _dataLocator!.DataPath;
            DataFolderText.Foreground = DataInfoBrush;
            BuildCatalogButton.IsEnabled = true;
            SearchModelsButton.IsEnabled = _catalog?.Exists == true;
        }

        DataFolderValidationText.Text = keptExisting
            ? L(AppText.InvalidKeep, message)
            : L(AppText.Invalid, message);
        DataFolderValidationText.Foreground = DataErrorBrush;
        StatusText.Text = L(AppText.GameFolderFailed, message);
    }

    private async Task RestoreDataFolderAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            return;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(
                await File.ReadAllTextAsync(SettingsPath));
            SetLanguage(AppLocalizer.ParseLanguage(settings?.Language));
            _selectedSkinType1Id = settings?.SkinType1 ?? DefaultSkinType1Id;
            _selectedSkinType2Id = settings?.SkinType2 ?? DefaultSkinType2Id;
            RestoreBackground(settings?.Background);
            if (!string.IsNullOrWhiteSpace(settings?.DataPath))
            {
                await ConfigureDataFolderAsync(settings.DataPath, rebuild: false);
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = L(AppText.RestoreFailed, exception.Message);
        }
    }
}
