using System.ComponentModel;
using System.Runtime.CompilerServices;
using Pso2ShapeStudio.App.Localization;
using Pso2ShapeStudio.GameData;

namespace Pso2ShapeStudio.App;

public sealed class ModelSearchResultViewModel : INotifyPropertyChanged
{
    private readonly ResolvedGameFile? _resolved;
    private AppLanguage _language;

    public ModelSearchResultViewModel(
        ModelCatalogRecord record,
        ResolvedGameFile? resolved,
        AppLanguage language)
    {
        Record = record;
        _resolved = resolved;
        _language = language;
        ResolvedPath = resolved?.Path;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ModelCatalogRecord Record { get; }

    public string DisplayName
    {
        get
        {
            var globalEnglish = Record.NameEnglish;
            var japanese = Record.NameJapanese;
            return AppLocalizer.IsLanguage(_language, "ja")
                ? FirstName(
                    japanese, globalEnglish, AppLocalizer.Text(_language, AppText.UnnamedItem, Record.Id))
                : FirstName(
                    globalEnglish, japanese, AppLocalizer.Text(_language, AppText.UnnamedItem, Record.Id));
        }
    }

    public string Metadata
    {
        get
        {
            var quality = _resolved is null
                ? AppText.QualityMissing
                : _resolved.IsHighQuality ? AppText.QualityHigh : AppText.QualityNormal;
            return $"{AppLocalizer.ObjectTypeName(_language, Record.ObjectType)} · " +
                   $"ID {Record.Id} · {AppLocalizer.Text(_language, quality)}";
        }
    }

    public string? ResolvedPath { get; }

    public void SetLanguage(AppLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Metadata)));
    }

    private static string FirstName(string? preferred, string? fallback, string missing) =>
        !string.IsNullOrWhiteSpace(preferred)
            ? preferred
            : !string.IsNullOrWhiteSpace(fallback) ? fallback : missing;
}
