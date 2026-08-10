using System.Numerics;

namespace Pso2ShapeStudio.Character;

public enum Pso2ColorChannel
{
    Unused = 0,
    Outer1 = 1,
    Outer2 = 2,
    Base1 = 3,
    Base2 = 4,
    Inner1 = 5,
    Inner2 = 6,
    Cast1 = 7,
    Cast2 = 8,
    Cast3 = 9,
    Cast4 = 10,
    MainSkin = 11,
    SubSkin = 12,
    RightEye = 13,
    LeftEye = 14,
    Eyebrow = 15,
    Eyelash = 16,
    Hair1 = 17,
    Hair2 = 18,
}

public readonly record struct Pso2ColorMapping(
    Pso2ColorChannel Red,
    Pso2ColorChannel Green,
    Pso2ColorChannel Blue = Pso2ColorChannel.Unused,
    Pso2ColorChannel Alpha = Pso2ColorChannel.Unused)
{
    public bool UsesAny => Red != Pso2ColorChannel.Unused ||
                           Green != Pso2ColorChannel.Unused ||
                           Blue != Pso2ColorChannel.Unused ||
                           Alpha != Pso2ColorChannel.Unused;
}

public sealed class CharacterColorPalette
{
    private static readonly IReadOnlyDictionary<Pso2ColorChannel, string> Fields =
        new Dictionary<Pso2ColorChannel, string>
        {
            [Pso2ColorChannel.Outer1] = "ngsCOL2.outerColor1",
            [Pso2ColorChannel.Outer2] = "ngsCOL2.outerColor2",
            [Pso2ColorChannel.Base1] = "ngsCOL2.baseColor1",
            [Pso2ColorChannel.Base2] = "ngsCOL2.baseColor2",
            [Pso2ColorChannel.Inner1] = "ngsCOL2.innerColor1",
            [Pso2ColorChannel.Inner2] = "ngsCOL2.innerColor2",
            [Pso2ColorChannel.Cast1] = "ngsCOL2.mainColor",
            [Pso2ColorChannel.Cast2] = "ngsCOL2.subColor1",
            [Pso2ColorChannel.Cast3] = "ngsCOL2.subColor2",
            [Pso2ColorChannel.Cast4] = "ngsCOL2.subColor3",
            [Pso2ColorChannel.MainSkin] = "ngsCOL2.skinColor1",
            [Pso2ColorChannel.SubSkin] = "ngsCOL2.skinColor2",
            [Pso2ColorChannel.RightEye] = "ngsCOL2.rightEyeColor",
            [Pso2ColorChannel.LeftEye] = "ngsCOL2.leftEyeColor",
            [Pso2ColorChannel.Eyebrow] = "ngsCOL2.eyebrowColor",
            [Pso2ColorChannel.Eyelash] = "ngsCOL2.eyelashColor",
            [Pso2ColorChannel.Hair1] = "ngsCOL2.hairColor1",
            [Pso2ColorChannel.Hair2] = "ngsCOL2.hairColor2",
        };

    private readonly IReadOnlyDictionary<Pso2ColorChannel, Vector4> _colors;

    private CharacterColorPalette(IReadOnlyDictionary<Pso2ColorChannel, Vector4> colors) =>
        _colors = colors;

    public static CharacterColorPalette Default { get; } = new(
        new Dictionary<Pso2ColorChannel, Vector4>
        {
            [Pso2ColorChannel.Outer1] = Gray(),
            [Pso2ColorChannel.Outer2] = Gray(),
            [Pso2ColorChannel.Base1] = Gray(),
            [Pso2ColorChannel.Base2] = Gray(),
            [Pso2ColorChannel.Inner1] = Gray(),
            [Pso2ColorChannel.Inner2] = Gray(),
            [Pso2ColorChannel.Cast1] = Gray(),
            [Pso2ColorChannel.Cast2] = Gray(),
            [Pso2ColorChannel.Cast3] = Gray(),
            [Pso2ColorChannel.Cast4] = Gray(),
            [Pso2ColorChannel.MainSkin] = new Vector4(0.8f, 0.42f, 0.30f, 1f),
            [Pso2ColorChannel.SubSkin] = new Vector4(1f, 0.02f, 0.02f, 1f),
            [Pso2ColorChannel.RightEye] = new Vector4(0f, 0.85f, 0.85f, 1f),
            [Pso2ColorChannel.LeftEye] = new Vector4(0f, 0.85f, 0.85f, 1f),
            [Pso2ColorChannel.Eyebrow] = new Vector4(1f, 0.49f, 0.14f, 1f),
            [Pso2ColorChannel.Eyelash] = new Vector4(1f, 0.49f, 0.14f, 1f),
            [Pso2ColorChannel.Hair1] = new Vector4(1f, 0.49f, 0.14f, 1f),
            [Pso2ColorChannel.Hair2] = new Vector4(1f, 0.82f, 0.67f, 1f),
        });

    public Vector4 this[Pso2ColorChannel channel] =>
        channel == Pso2ColorChannel.Unused
            ? Vector4.One
            : _colors.TryGetValue(channel, out var color)
                ? color
                : Default._colors[channel];

    public static CharacterColorPalette FromCharacter(CharacterFile character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var colors = Default._colors.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (channel, field) in Fields)
        {
            if (!character.Contains(field) || character[field] is not object[] values || values.Length < 3)
            {
                continue;
            }

            // PSO2 stores the visible colour bytes in B, G, R order. The fourth
            // byte is not opacity. Blender colour properties and the shader use
            // linear RGB, so decode the stored sRGB bytes here.
            colors[channel] = new Vector4(
                SrgbToLinear(Convert.ToByte(values[2]) / 255f),
                SrgbToLinear(Convert.ToByte(values[1]) / 255f),
                SrgbToLinear(Convert.ToByte(values[0]) / 255f),
                1f);
        }

        return new CharacterColorPalette(colors);
    }

    public static float SrgbToLinear(float value) =>
        value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static Vector4 Gray() => new(0.5f, 0.5f, 0.5f, 1f);
}
