namespace Pso2ShapeStudio.Character;

/// <summary>
/// Character-file material controls normalized for the renderer.
/// muscleMass is stored on a 0..60000 scale. skinGloss is a signed byte;
/// keeping its sign preserves the dry and wet halves around the neutral zero.
/// </summary>
public readonly record struct CharacterSurfaceSettings(
    float Muscularity,
    float SkinGloss)
{
    public const float MaximumMuscleMass = 60000f;
    public const float MaximumSkinGlossMagnitude = 127f;

    public static CharacterSurfaceSettings Default => new(0f, 0f);

    public static CharacterSurfaceSettings FromCharacter(CharacterFile character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var muscleMass = character.Contains("baseDOC.muscleMass")
            ? Convert.ToSingle(character["baseDOC.muscleMass"])
            : 0f;
        var skinGloss = character.Contains("ngsSLID.skinGloss")
            ? Convert.ToInt32(character["ngsSLID.skinGloss"])
            : 0;
        return FromRaw(muscleMass, skinGloss);
    }

    public static CharacterSurfaceSettings FromRaw(float muscleMass, int skinGloss) => new(
        Math.Clamp(muscleMass / MaximumMuscleMass, 0f, 1f),
        Math.Clamp(skinGloss / MaximumSkinGlossMagnitude, -1f, 1f));
}
