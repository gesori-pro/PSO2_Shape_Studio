namespace Pso2ShapeStudio.GameData;

/// <summary>
/// The character-making index lives only in the classic data folder, so an
/// NGS-only installation passes the data-folder check and then has nothing to
/// build a catalog from. Reported on its own so the app can say that plainly
/// instead of surfacing a null-reference message.
/// </summary>
public sealed class Pso2ClassicDataMissingException(string dataPath)
    : InvalidOperationException(
        "The character-making index (CMX) is missing. It ships with the classic PSO2 data, " +
        $"which was not found under {dataPath}. Model search needs that data downloaded.")
{
    public string DataPath { get; } = dataPath;
}
