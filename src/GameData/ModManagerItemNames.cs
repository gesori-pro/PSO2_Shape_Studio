using System.Text.Json;

namespace Pso2ShapeStudio.GameData;

public sealed record OfficialItemName(string Japanese, string GlobalEnglish);

public static class ModManagerItemNames
{
    private static readonly Lazy<IReadOnlyDictionary<(string ObjectType, int Id), OfficialItemName>>
        Names = new(LoadNames);

    public static int Count => Names.Value.Count;

    public static OfficialItemName? Find(string objectType, int id) =>
        Names.Value.GetValueOrDefault((objectType, id));

    private static IReadOnlyDictionary<(string ObjectType, int Id), OfficialItemName> LoadNames()
    {
        var assembly = typeof(ModManagerItemNames).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith("Data.Items.mod_manager_item_names.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded item names were not found: {resourceName}");
        using var document = JsonDocument.Parse(stream);
        var names = new Dictionary<(string ObjectType, int Id), OfficialItemName>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var values = item.EnumerateArray().ToArray();
            names[(values[0].GetString()!, values[1].GetInt32())] = new OfficialItemName(
                values[2].GetString() ?? "",
                values[3].GetString() ?? "");
        }

        return names;
    }
}
