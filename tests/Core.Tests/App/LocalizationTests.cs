using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pso2ShapeStudio.App.Localization;

namespace Pso2ShapeStudio.Core.Tests.App;

public sealed partial class LocalizationTests
{
    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("ja-JP", "ja")]
    [InlineData("ko-KR", "ko")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-Hant", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("zh-MO", "zh-Hant")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-SG", "zh-Hans")]
    public void DetectSystemLanguageUsesWindowsUiCulture(string cultureName, string expectedCode)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);

        Assert.Equal(
            expectedCode,
            AppLocalizer.LanguageCode(AppLocalizer.DetectSystemLanguage(culture)));
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("ja", "ja")]
    [InlineData("ja-JP", "ja")]
    [InlineData("ko", "ko")]
    [InlineData("ko-KR", "ko")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-Hant", "zh-Hant")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-Hans", "zh-Hans")]
    public void SavedLanguageCodesAndCultureVariantsResolve(string input, string expectedCode)
    {
        var language = AppLocalizer.ParseLanguage(input);

        Assert.Equal(expectedCode, AppLocalizer.LanguageCode(language));
        Assert.Equal(
            language,
            AppLocalizer.ParseLanguage(AppLocalizer.LanguageCode(language)));
    }

    [Fact]
    public void LanguageSelectorIsPopulatedFromLocaleMetadata()
    {
        var options = AppLocalizer.AvailableLanguages;

        Assert.Equal(new[] { "en", "ja", "ko", "zh-Hans", "zh-Hant" },
            options.Select(option => AppLocalizer.LanguageCode(option.Language)));
        Assert.Equal(new[] { "English (Global)", "日本語", "한국어", "简体中文", "繁體中文" },
            options.Select(option => option.DisplayName));
    }

    [Fact]
    public void EveryRepositoryLocaleIsCompleteAndKeepsEnglishPlaceholders()
    {
        var localesDirectory = Path.Combine(AppContext.BaseDirectory, "locales");
        Assert.True(Directory.Exists(localesDirectory),
            $"Locale output directory was not copied: {localesDirectory}");

        var files = Directory.EnumerateFiles(localesDirectory, "*.json")
            .Where(file => !Path.GetFileName(file).Equals(
                "schema.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(5, files.Length);

        using var englishDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(localesDirectory, "en.json")));
        var englishStrings = ReadTranslations(englishDocument.RootElement, "strings");
        var englishShapeNames = ReadTranslations(englishDocument.RootElement, "shapeNames");
        var englishObjectTypes = ReadTranslations(englishDocument.RootElement, "objectTypes");
        var expectedTextKeys = Enum.GetNames<AppText>().Order().ToArray();
        Assert.Equal(expectedTextKeys, englishStrings.Keys.Order().ToArray());

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            var code = root.GetProperty("code").GetString();
            var name = root.GetProperty("name").GetString();
            Assert.False(string.IsNullOrWhiteSpace(code));
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.True(codes.Add(code!));
            _ = CultureInfo.CreateSpecificCulture(code!);

            var strings = ReadTranslations(root, "strings");
            var shapeNames = ReadTranslations(root, "shapeNames");
            var objectTypes = ReadTranslations(root, "objectTypes");
            Assert.Equal(englishStrings.Keys.Order().ToArray(), strings.Keys.Order().ToArray());
            Assert.Equal(englishShapeNames.Keys.Order().ToArray(), shapeNames.Keys.Order().ToArray());
            Assert.Equal(englishObjectTypes.Keys.Order().ToArray(), objectTypes.Keys.Order().ToArray());

            foreach (var (key, english) in englishStrings)
            {
                Assert.Equal(Placeholders(english), Placeholders(strings[key]));
            }
        }
    }

    private static Dictionary<string, string> ReadTranslations(JsonElement root, string property) =>
        root.GetProperty(property)
            .EnumerateObject()
            .ToDictionary(item => item.Name, item => item.Value.GetString() ?? string.Empty);

    private static string[] Placeholders(string value) =>
        CompositeFormatPlaceholder()
            .Matches(value)
            .Select(match => match.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

    [GeneratedRegex(@"\{\d+(?:[^{}]*)\}")]
    private static partial Regex CompositeFormatPlaceholder();
}
