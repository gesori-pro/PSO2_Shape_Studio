using System.ComponentModel;
using System.Runtime.CompilerServices;
using Pso2ShapeStudio.App.Localization;
using Pso2ShapeStudio.GameData;

namespace Pso2ShapeStudio.App;

public sealed class SkinOptionViewModel : INotifyPropertyChanged
{
    private readonly ResolvedGameFile? _resolved;
    private AppLanguage _language;

    public SkinOptionViewModel(
        ModelCatalogRecord record,
        ResolvedGameFile? resolved,
        AppLanguage language)
    {
        Record = record;
        _resolved = resolved;
        _language = language;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ModelCatalogRecord Record { get; }
    public int Id => Record.Id;
    public string? ResolvedPath => _resolved?.Path;

    public string DisplayName
    {
        get
        {
            var preferred = _language == AppLanguage.Japanese
                ? Record.NameJapanese
                : Record.NameEnglish;
            var fallback = _language == AppLanguage.Japanese
                ? Record.NameEnglish
                : Record.NameJapanese;
            var name = !string.IsNullOrWhiteSpace(preferred)
                ? preferred
                : !string.IsNullOrWhiteSpace(fallback)
                    ? fallback
                    : AppLocalizer.Text(_language, AppText.UnnamedItem, Id);
            return $"{name} · {Id}";
        }
    }

    public void SetLanguage(AppLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
    }
}
