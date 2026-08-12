using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Pso2ShapeStudio.App.Localization;
using Pso2ShapeStudio.Character;
using Pso2ShapeStudio.GameData;
using Pso2ShapeStudio.Rigging;

namespace Pso2ShapeStudio.App;

public partial class OptionsWindow : Window
{
    private readonly AppLanguage _language;
    private readonly HashSet<string> _boneNames;
    private readonly IReadOnlyList<BoneSelectionOption> _pairBoneOptions;
    private readonly IReadOnlyList<BoneSelectionOption> _singleBoneOptions;

    public OptionsWindow()
        : this(
            AppLanguage.English,
            "",
            AppSettingDefaults.MainSkinColor,
            AppSettingDefaults.SubSkinColor,
            [],
            [],
            [])
    {
    }

    internal OptionsWindow(
        AppLanguage language,
        string dataPath,
        string mainSkin,
        string subSkin,
        IReadOnlyCollection<string> hiddenBuiltInGroups,
        IReadOnlyList<CustomBoneGroupSetting> customGroups,
        IReadOnlyList<string> boneNames)
    {
        _language = language;
        var distinctBoneNames = boneNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _boneNames = distinctBoneNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        (_pairBoneOptions, _singleBoneOptions) = CreateBoneOptions(distinctBoneNames);
        var hidden = hiddenBuiltInGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);

        BuiltInGroups = new ObservableCollection<BuiltInBoneGroupOption>(
            ShapeSliders.Groups.Select(group => new BuiltInBoneGroupOption(
                group.Key,
                AppLocalizer.ShapeName(language, group.Key, group.Label),
                group.RightBone is null
                    ? group.LeftBone
                    : $"{group.LeftBone} / {group.RightBone}",
                !hidden.Contains(group.Key))));
        CustomGroups = new ObservableCollection<CustomBoneGroupOption>(
            customGroups.Select(group => new CustomBoneGroupOption(
                group.Id,
                group.Label,
                group.LeftBone,
                group.RightBone,
                group.RightBone is not null,
                L(group.RightBone is null ? AppText.Single : AppText.Pair),
                group.RightBone is null ? _singleBoneOptions : _pairBoneOptions)));

        DataContext = this;
        InitializeComponent();

        DataFolderBox.Text = dataPath;
        MainSkinColorBox.Text = NormalizeHex(mainSkin);
        SubSkinColorBox.Text = NormalizeHex(subSkin);
        ApplyLanguage();
        UpdateColorPreview(MainSkinColorBox.Text, MainSkinPreview);
        UpdateColorPreview(SubSkinColorBox.Text, SubSkinPreview);

        BrowseButton.Click += BrowseForDataFolder;
        AddPairButton.Click += (_, _) => AddCustomGroup(paired: true);
        AddSingleButton.Click += (_, _) => AddCustomGroup(paired: false);
        MainSkinColorBox.TextChanged += (_, _) =>
            UpdateColorPreview(MainSkinColorBox.Text, MainSkinPreview);
        SubSkinColorBox.TextChanged += (_, _) =>
            UpdateColorPreview(SubSkinColorBox.Text, SubSkinPreview);
        CancelButton.Click += (_, _) => Close((OptionsDialogResult?)null);
        SaveButton.Click += SaveOptions;
    }

    public ObservableCollection<BuiltInBoneGroupOption> BuiltInGroups { get; }

    public ObservableCollection<CustomBoneGroupOption> CustomGroups { get; }

    private string L(AppText key, params object?[] arguments) =>
        AppLocalizer.Text(_language, key, arguments);

    private void ApplyLanguage()
    {
        Title = L(AppText.OptionsTitle);
        GeneralTab.Header = L(AppText.General);
        BonesTab.Header = L(AppText.EditableBones);
        DataFolderLabel.Text = L(AppText.GameDataFolder);
        BrowseButton.Content = L(AppText.Browse);
        DefaultSkinColorsLabel.Text = L(AppText.DefaultSkinColors);
        MainSkinColorLabel.Text = L(AppText.MainSkinColor);
        SubSkinColorLabel.Text = L(AppText.SubSkinColor);
        HexHelpText.Text = L(AppText.HexColorHelp);
        BoneHelpText.Text = L(AppText.BoneEditorHelp);
        SkeletonStatusText.Text = _boneNames.Count == 0
            ? L(AppText.ModelRequiredForBone)
            : L(AppText.BonesCount, _boneNames.Count);
        AddPairButton.Content = L(AppText.AddPair);
        AddSingleButton.Content = L(AppText.AddSingle);
        AddPairButton.IsEnabled = _pairBoneOptions.Count > 0;
        AddSingleButton.IsEnabled = _singleBoneOptions.Count > 0;
        CancelButton.Content = L(AppText.Cancel);
        SaveButton.Content = L(AppText.Save);
    }

    private async void BrowseForDataFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L(AppText.GameDataFolder),
            AllowMultiple = false,
        });
        if (folders.Count > 0)
        {
            DataFolderBox.Text = folders[0].Path.LocalPath;
        }
    }

    private void AddCustomGroup(bool paired)
    {
        var options = paired ? _pairBoneOptions : _singleBoneOptions;
        var selected = options.FirstOrDefault();
        if (selected is null)
        {
            return;
        }

        CustomGroups.Add(new CustomBoneGroupOption(
            Guid.NewGuid().ToString("N"),
            SuggestedLabel(selected),
            selected.LeftBone,
            selected.RightBone,
            paired,
            L(paired ? AppText.Pair : AppText.Single),
            options));
    }

    private void RemoveCustomBone(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CustomBoneGroupOption option })
        {
            CustomGroups.Remove(option);
        }
    }

    private void SaveOptions(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        var mainSkin = NormalizeHex(MainSkinColorBox.Text);
        var subSkin = NormalizeHex(SubSkinColorBox.Text);
        if (!CharacterColorPalette.TryParseSrgbHex(mainSkin, out _) ||
            !CharacterColorPalette.TryParseSrgbHex(subSkin, out _))
        {
            ErrorText.Text = L(AppText.InvalidSkinColor);
            return;
        }

        var dataPath = DataFolderBox.Text?.Trim() ?? "";
        if (dataPath.Length > 0)
        {
            var validation = Pso2DataLocator.ValidateSelectedPath(dataPath);
            if (!validation.IsValid)
            {
                ErrorText.Text = AppLocalizer.DataPathError(_language, validation);
                return;
            }

            dataPath = validation.DataPath!;
        }

        var assignedBones = ShapeSliders.Groups
            .Where(group => BuiltInGroups.Any(option => option.Key == group.Key && option.Enabled))
            .SelectMany(group => group.RightBone is null
                ? new[] { group.LeftBone }
                : new[] { group.LeftBone, group.RightBone })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in CustomGroups)
        {
            if (string.IsNullOrWhiteSpace(group.Label) ||
                string.IsNullOrWhiteSpace(group.LeftBone) ||
                group.IsPaired && string.IsNullOrWhiteSpace(group.RightBone))
            {
                ErrorText.Text = L(AppText.MissingBoneFields);
                return;
            }

            var names = group.IsPaired
                ? new[] { group.LeftBone.Trim(), group.RightBone.Trim() }
                : new[] { group.LeftBone.Trim() };
            var missing = _boneNames.Count > 0
                ? names.FirstOrDefault(name => !_boneNames.Contains(name))
                : null;
            if (missing is not null)
            {
                ErrorText.Text = L(AppText.BoneNotFound, missing);
                return;
            }

            var duplicate = names.FirstOrDefault(name => !assignedBones.Add(name));
            if (duplicate is not null)
            {
                ErrorText.Text = L(AppText.DuplicateBone, duplicate);
                return;
            }
        }

        var result = new OptionsDialogResult(
            dataPath,
            mainSkin,
            subSkin,
            BuiltInGroups.Where(group => !group.Enabled).Select(group => group.Key).ToArray(),
            CustomGroups.Select(group => new CustomBoneGroupSetting(
                group.Id,
                group.Label.Trim(),
                group.LeftBone.Trim(),
                group.IsPaired ? group.RightBone.Trim() : null)).ToArray());
        Close(result);
    }

    private static (IReadOnlyList<BoneSelectionOption> Pairs,
        IReadOnlyList<BoneSelectionOption> Singles) CreateBoneOptions(
        IReadOnlyList<string> boneNames)
    {
        var byName = boneNames.ToDictionary(name => name, StringComparer.OrdinalIgnoreCase);
        var pairedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pairs = new List<BoneSelectionOption>();
        foreach (var left in boneNames.Where(name =>
                     name.StartsWith("l_", StringComparison.OrdinalIgnoreCase)))
        {
            if (!byName.TryGetValue("r_" + left[2..], out var right))
            {
                continue;
            }

            pairs.Add(new BoneSelectionOption($"{left} / {right}", left, right));
            pairedNames.Add(left);
            pairedNames.Add(right);
        }

        var singles = boneNames
            .Where(name => !pairedNames.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new BoneSelectionOption(name, name, null))
            .ToArray();
        return (
            pairs.OrderBy(option => option.LeftBone, StringComparer.OrdinalIgnoreCase).ToArray(),
            singles);
    }

    private static string SuggestedLabel(BoneSelectionOption option)
    {
        var name = option.LeftBone;
        return option.RightBone is not null &&
               name.StartsWith("l_", StringComparison.OrdinalIgnoreCase)
            ? name[2..]
            : name;
    }

    private static string NormalizeHex(string? value) =>
        (value ?? "").Trim().TrimStart('#').ToUpperInvariant();

    private static void UpdateColorPreview(string? value, Border preview)
    {
        var normalized = NormalizeHex(value);
        preview.Background = CharacterColorPalette.TryParseSrgbHex(normalized, out _)
            ? Brush.Parse("#" + normalized)
            : Brushes.Transparent;
    }
}
