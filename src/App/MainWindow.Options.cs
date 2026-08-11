using Avalonia.Controls;
using Avalonia.Interactivity;
using Pso2ShapeStudio.App.Localization;
using Pso2ShapeStudio.Character;
using Pso2ShapeStudio.GameData;
using Pso2ShapeStudio.Rigging;

namespace Pso2ShapeStudio.App;

// Persistent application options and the configurable shape-slider list.
public partial class MainWindow : Window
{
    private IReadOnlyList<ShapeGroupDefinition> ActiveShapeGroups() =>
        SliderGroups.Select(editor => editor.Definition).ToArray();

    private void RebuildSliderGroups()
    {
        var previousDefinitions = SliderGroups.ToDictionary(
            editor => editor.Key,
            editor => editor.Definition,
            StringComparer.OrdinalIgnoreCase);
        var nodeIds = _skeleton?.Bones
            .Where(bone => bone.Index < ShapeAdjustFile.NodeCount)
            .ToDictionary(bone => bone.Name, bone => bone.Index, StringComparer.OrdinalIgnoreCase);
        var customGroups = _customShapeGroups.Select(group => new CustomShapeGroupDefinition(
            group.Key,
            group.Label,
            group.LeftBone,
            group.RightBone));
        var definitions = ShapeSliders.ConfigureGroups(
            _hiddenShapeGroups,
            customGroups,
            nodeIds);
        var loadedProfile = _shapeAdjust?.ToProfile(definitions);

        if (loadedProfile is not null)
        {
            foreach (var definition in definitions)
            {
                var mappingChanged = !previousDefinitions.TryGetValue(
                                         definition.Key,
                                         out var previous) ||
                                     !string.Equals(
                                         previous.LeftBone,
                                         definition.LeftBone,
                                         StringComparison.OrdinalIgnoreCase) ||
                                     !string.Equals(
                                         previous.RightBone,
                                         definition.RightBone,
                                         StringComparison.OrdinalIgnoreCase);
                var loadedValue = loadedProfile[definition.Key];
                if (mappingChanged && !loadedValue.IsIdentity)
                {
                    _profile[definition.Key] = loadedValue;
                }
            }
        }

        foreach (var editor in SliderGroups)
        {
            editor.ValueChanged -= SliderValueChanged;
        }

        SliderGroups.Clear();
        foreach (var definition in definitions)
        {
            var editor = new ShapeGroupViewModel(definition, _language);
            editor.SetValue(_profile[definition.Key]);
            editor.ValueChanged += SliderValueChanged;
            SliderGroups.Add(editor);
        }
    }

    private void ApplyDefaultSkinColors()
    {
        _characterColors = CharacterColorPalette.CreateWithSkinColors(
            _defaultMainSkin,
            _defaultSubSkin);
        if (!_characterColorsFromFile)
        {
            Viewport.SetCharacterColors(_characterColors);
        }
    }

    private async void OpenOptions(object? sender, RoutedEventArgs e)
    {
        CommitPendingShapeEdit();
        var editableBones = _skeleton?.Bones
            .Where(bone => bone.Index < ShapeAdjustFile.NodeCount)
            .Select(bone => bone.Name)
            .ToArray() ?? [];
        var dialog = new OptionsWindow(
            _language,
            _dataLocator?.DataPath ?? "",
            _defaultMainSkin,
            _defaultSubSkin,
            _hiddenShapeGroups,
            _customShapeGroups,
            editableBones);
        var result = await dialog.ShowDialog<OptionsDialogResult?>(this);
        if (result is null)
        {
            return;
        }

        var oldDataPath = _dataLocator?.DataPath ?? "";
        _defaultMainSkin = result.DefaultMainSkin;
        _defaultSubSkin = result.DefaultSubSkin;
        _hiddenShapeGroups = result.HiddenShapeGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _customShapeGroups = result.CustomShapeGroups.ToList();

        ApplyDefaultSkinColors();
        RebuildSliderGroups();
        RebuildPose();

        if (result.DataPath.Length == 0)
        {
            if (oldDataPath.Length > 0)
            {
                ClearDataFolderConfiguration();
            }
        }
        else if (!string.Equals(oldDataPath, result.DataPath, StringComparison.OrdinalIgnoreCase))
        {
            await ConfigureDataFolderAsync(result.DataPath, rebuild: false);
        }

        try
        {
            await SaveSettingsAsync(result.DataPath);
            StatusText.Text = L(AppText.OptionsSaved);
        }
        catch (Exception exception)
        {
            StatusText.Text = L(AppText.DataPrepareFailedStatus, exception.Message);
        }
    }

    private void ClearDataFolderConfiguration()
    {
        _dataLocator = null;
        _catalog = null;
        SearchResults.Clear();
        ClearSkinOptions();
        DataFolderText.Text = L(AppText.DataFolderUnspecified);
        DataFolderText.Foreground = DataInfoBrush;
        DataFolderValidationText.Text = L(AppText.DataFolderHelp);
        DataFolderValidationText.Foreground = DataInfoBrush;
        BuildCatalogButton.IsEnabled = false;
        SearchModelsButton.IsEnabled = false;
        LoadSearchResultButton.IsEnabled = false;
    }

    private static string ValidSkinHexOrDefault(string? value, string fallback) =>
        CharacterColorPalette.TryParseSrgbHex(value, out _)
            ? value!.Trim().TrimStart('#').ToUpperInvariant()
            : fallback;

    private void RestoreSidebarWidth(double? width)
    {
        var value = double.IsFinite(width ?? double.NaN) ? width!.Value : 420;
        RootLayout.ColumnDefinitions[0].Width = new GridLength(
            Math.Clamp(value, 320, 760),
            GridUnitType.Pixel);
    }

    private double CurrentSidebarWidth()
    {
        var column = RootLayout.ColumnDefinitions[0];
        return column.ActualWidth >= 320
            ? column.ActualWidth
            : Math.Clamp(column.Width.Value, 320, 760);
    }
}
