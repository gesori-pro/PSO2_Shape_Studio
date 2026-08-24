namespace Pso2ShapeStudio.GameData;

/// <summary>
/// The character-making index was not where the game keeps it. Every model in
/// the catalog is listed in that one file - classic and NGS parts alike - so
/// there is nothing to build from without it.
///
/// Reported on its own, naming the path that was checked, because the library
/// returns null instead of throwing when the file is absent and the resulting
/// null reference told the player nothing.
/// </summary>
public sealed class Pso2CharacterIndexMissingException(string expectedPath)
    : InvalidOperationException(
        $"The character-making index was not found at {expectedPath}. " +
        "Model search needs this file from the game's data download.")
{
    public string ExpectedPath { get; } = expectedPath;
}
