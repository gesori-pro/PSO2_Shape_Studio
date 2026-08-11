namespace Pso2ShapeStudio.App;

using System.ComponentModel;
using System.Runtime.CompilerServices;

internal static class AppSettingDefaults
{
    public const string MainSkinColor = "E7AE95";
    public const string SubSkinColor = "FF2727";
}

internal sealed record CustomBoneGroupSetting(
    string Id,
    string Label,
    string LeftBone,
    string? RightBone)
{
    public string Key => $"custom_{Id}";
}

internal sealed record AppSettings(
    string DataPath = "",
    string Language = "en",
    int SkinType1 = 100000,
    int SkinType2 = 200000,
    string Background = "0E1114",
    bool? FloorGuide = null,
    string DefaultMainSkin = AppSettingDefaults.MainSkinColor,
    string DefaultSubSkin = AppSettingDefaults.SubSkinColor,
    IReadOnlyList<string>? HiddenShapeGroups = null,
    IReadOnlyList<CustomBoneGroupSetting>? CustomShapeGroups = null,
    double SidebarWidth = 420);

internal sealed record OptionsDialogResult(
    string DataPath,
    string DefaultMainSkin,
    string DefaultSubSkin,
    IReadOnlyList<string> HiddenShapeGroups,
    IReadOnlyList<CustomBoneGroupSetting> CustomShapeGroups);

public sealed class BuiltInBoneGroupOption : INotifyPropertyChanged
{
    private bool _enabled;

    public BuiltInBoneGroupOption(string key, string label, string bones, bool enabled)
    {
        Key = key;
        Label = label;
        Bones = bones;
        _enabled = enabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }
    public string Label { get; }
    public string Bones { get; }
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }
}

public sealed class CustomBoneGroupOption : INotifyPropertyChanged
{
    private string _label;
    private BoneSelectionOption? _selectedBone;

    public CustomBoneGroupOption(
        string id,
        string label,
        string leftBone,
        string? rightBone,
        bool paired,
        string kindLabel,
        IReadOnlyList<BoneSelectionOption> boneOptions)
    {
        Id = id;
        _label = label;
        IsPaired = paired;
        KindLabel = kindLabel;
        var options = boneOptions.ToList();
        _selectedBone = options.FirstOrDefault(option =>
            string.Equals(option.LeftBone, leftBone, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(option.RightBone, rightBone, StringComparison.OrdinalIgnoreCase));
        if (_selectedBone is null && !string.IsNullOrWhiteSpace(leftBone))
        {
            _selectedBone = new BoneSelectionOption(
                rightBone is null ? leftBone : $"{leftBone} / {rightBone}",
                leftBone,
                rightBone);
            options.Insert(0, _selectedBone);
        }

        BoneOptions = options;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public bool IsPaired { get; }
    public string KindLabel { get; }
    public string Label { get => _label; set => Set(ref _label, value); }
    public IReadOnlyList<BoneSelectionOption> BoneOptions { get; }
    public BoneSelectionOption? SelectedBone
    {
        get => _selectedBone;
        set
        {
            if (Equals(_selectedBone, value)) return;
            _selectedBone = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedBone)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeftBone)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightBone)));
        }
    }
    public string LeftBone => SelectedBone?.LeftBone ?? "";
    public string RightBone => SelectedBone?.RightBone ?? "";

    private void Set(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record BoneSelectionOption(
    string DisplayName,
    string LeftBone,
    string? RightBone);
