using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Pso2ShapeStudio.GameData;

namespace Pso2ShapeStudio.App.Localization;

public readonly record struct AppLanguage(string Code)
{
    public static AppLanguage English => new("en");

    public static AppLanguage Japanese => new("ja");

    public static AppLanguage Korean => new("ko");

    public static AppLanguage TraditionalChinese => new("zh-Hant");

    public override string ToString() => Code ?? "en";
}

public sealed class AppLanguageOption(AppLanguage language, string displayName)
{
    public AppLanguage Language { get; } = language;

    public string DisplayName { get; } = displayName;

    public override string ToString() => DisplayName;
}

public static class AppLocalizer
{
    private const string EnglishCode = "en";
    private const string ResourcePrefix = "Pso2ShapeStudio.Locales.";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
    private static readonly Lazy<LocalizationCatalog> Catalog = new(LoadCatalog);

    public static IReadOnlyList<AppLanguageOption> AvailableLanguages => Catalog.Value.Options;

    public static string LanguageCode(AppLanguage language) => Entry(language).Code;

    public static AppLanguage ParseLanguage(string? code) => Catalog.Value.Resolve(code).Language;

    public static bool IsLanguage(AppLanguage language, string code) =>
        string.Equals(
            LanguageCode(language),
            LanguageCode(ParseLanguage(code)),
            StringComparison.OrdinalIgnoreCase);

    public static AppLanguage DetectSystemLanguage(CultureInfo? uiCulture = null) =>
        Catalog.Value.ResolveCulture(uiCulture ?? CultureInfo.CurrentUICulture).Language;

    public static string Text(AppLanguage language, AppText key, params object?[] arguments)
    {
        var entry = Entry(language);
        var english = Catalog.Value.English;
        var keyName = key.ToString();
        var fallback = Translation(english.Strings, keyName) ?? keyName;
        var localized = Translation(entry.Strings, keyName) ?? fallback;

        if (arguments.Length == 0)
        {
            return localized;
        }

        try
        {
            return string.Format(entry.Culture, localized, arguments);
        }
        catch (FormatException) when (!string.Equals(entry.Code, EnglishCode, StringComparison.OrdinalIgnoreCase))
        {
            return string.Format(english.Culture, fallback, arguments);
        }
    }

    public static string ShapeName(AppLanguage language, string key, string fallback)
    {
        var entry = Entry(language);
        var english = Catalog.Value.English;
        return Translation(entry.ShapeNames, key) ??
               Translation(english.ShapeNames, key) ??
               fallback;
    }

    public static string ObjectTypeName(AppLanguage language, string objectType)
    {
        var entry = Entry(language);
        var english = Catalog.Value.English;
        return Translation(entry.ObjectTypes, objectType) ??
               Translation(english.ObjectTypes, objectType) ??
               objectType;
    }

    public static string DataPathError(AppLanguage language, Pso2DataPathValidation validation) =>
        validation.Error switch
        {
            Pso2DataPathError.NotSelected => Text(language, AppText.DataPathNotSelected),
            Pso2DataPathError.InvalidPath => Text(language, AppText.DataPathInvalid),
            Pso2DataPathError.FolderNotFound => Text(
                language, AppText.DataPathFolderMissing, validation.SelectedPath),
            Pso2DataPathError.DataStructureNotFound => Text(language, AppText.DataPathStructureMissing),
            Pso2DataPathError.GameFilesNotFound => Text(language, AppText.DataPathFilesMissing),
            Pso2DataPathError.AccessDenied => Text(
                language, AppText.DataPathAccessDenied, validation.DataPath ?? validation.SelectedPath),
            _ => validation.Message,
        };

    private static LocaleEntry Entry(AppLanguage language) => Catalog.Value.Resolve(language.Code);

    private static string? Translation(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static LocalizationCatalog LoadCatalog()
    {
        var entries = new Dictionary<string, LocaleEntry>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(AppLocalizer).Assembly;

        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
                                    name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is not null && TryReadEntry(stream, out var entry))
            {
                entries[entry.Code] = entry;
            }
        }

        var localesDirectory = Path.Combine(AppContext.BaseDirectory, "locales");
        if (Directory.Exists(localesDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(localesDirectory, "*.json")
                         .Where(file => !Path.GetFileName(file).Equals(
                             "schema.json", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    if (TryReadEntry(stream, out var entry))
                    {
                        entries[entry.Code] = entry;
                    }
                }
                catch (IOException)
                {
                    // A broken optional locale must not prevent the application from starting.
                }
                catch (UnauthorizedAccessException)
                {
                    // The embedded locales remain available when an external file cannot be read.
                }
                catch (JsonException)
                {
                    // Invalid community locale files are ignored until corrected.
                }
            }
        }

        if (!entries.TryGetValue(EnglishCode, out var english))
        {
            throw new InvalidOperationException("The embedded English locale is missing or invalid.");
        }

        return new LocalizationCatalog(entries.Values, english);
    }

    private static bool TryReadEntry(Stream stream, out LocaleEntry entry)
    {
        var document = JsonSerializer.Deserialize<LocaleDocument>(stream, JsonOptions);
        if (document is null ||
            string.IsNullOrWhiteSpace(document.Code) ||
            string.IsNullOrWhiteSpace(document.Name) ||
            document.Strings.Count == 0)
        {
            entry = null!;
            return false;
        }

        CultureInfo culture;
        try
        {
            culture = CultureInfo.CreateSpecificCulture(document.Code.Trim());
        }
        catch (CultureNotFoundException)
        {
            entry = null!;
            return false;
        }

        var code = document.Code.Trim();
        entry = new LocaleEntry(
            code,
            document.Name.Trim(),
            document.Order,
            culture,
            document.Strings,
            document.ShapeNames,
            document.ObjectTypes);
        return true;
    }

    private sealed class LocalizationCatalog
    {
        private readonly Dictionary<string, LocaleEntry> _codes;

        public LocalizationCatalog(IEnumerable<LocaleEntry> entries, LocaleEntry english)
        {
            Entries = entries
                .OrderBy(entry => entry.Order)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            English = english;
            _codes = Entries.ToDictionary(entry => entry.Code, StringComparer.OrdinalIgnoreCase);

            Options = Entries
                .Select(entry => new AppLanguageOption(entry.Language, entry.DisplayName))
                .ToArray();
        }

        public LocaleEntry[] Entries { get; }

        public LocaleEntry English { get; }

        public IReadOnlyList<AppLanguageOption> Options { get; }

        public LocaleEntry Resolve(string? code)
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                var candidate = code.Trim();
                if (_codes.TryGetValue(candidate, out var byCode))
                {
                    return byCode;
                }

                try
                {
                    return ResolveCulture(CultureInfo.GetCultureInfo(candidate));
                }
                catch (CultureNotFoundException)
                {
                    // Unknown codes use the English fallback.
                }
            }

            return English;
        }

        public LocaleEntry ResolveCulture(CultureInfo culture)
        {
            for (var candidate = culture; !string.IsNullOrEmpty(candidate.Name); candidate = candidate.Parent)
            {
                if (_codes.TryGetValue(candidate.Name, out var entry))
                {
                    return entry;
                }
            }

            return English;
        }
    }

    private sealed class LocaleEntry(
        string code,
        string displayName,
        int order,
        CultureInfo culture,
        IReadOnlyDictionary<string, string> strings,
        IReadOnlyDictionary<string, string> shapeNames,
        IReadOnlyDictionary<string, string> objectTypes)
    {
        public string Code { get; } = code;

        public string DisplayName { get; } = displayName;

        public int Order { get; } = order;

        public CultureInfo Culture { get; } = culture;

        public AppLanguage Language { get; } = new(code);

        public IReadOnlyDictionary<string, string> Strings { get; } =
            Normalize(strings);

        public IReadOnlyDictionary<string, string> ShapeNames { get; } =
            Normalize(shapeNames);

        public IReadOnlyDictionary<string, string> ObjectTypes { get; } =
            Normalize(objectTypes);

        private static IReadOnlyDictionary<string, string> Normalize(
            IReadOnlyDictionary<string, string> values) =>
            new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class LocaleDocument
    {
        public string Code { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public int Order { get; init; } = 100;

        public Dictionary<string, string> Strings { get; init; } = [];

        public Dictionary<string, string> ShapeNames { get; init; } = [];

        public Dictionary<string, string> ObjectTypes { get; init; } = [];
    }
}
