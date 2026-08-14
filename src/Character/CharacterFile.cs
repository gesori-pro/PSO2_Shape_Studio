using System.Buffers.Binary;
using System.Text.Json;

namespace Pso2ShapeStudio.Character;

public sealed record CharacterField(string Name, int Offset, string Type, int Count)
{
    public int Width => Type switch
    {
        "i32" or "u32" or "f32" => 4,
        "i16" or "u16" => 2,
        "i8" or "u8" => 1,
        _ => throw new InvalidDataException($"Unsupported character field type: {Type}"),
    };
}

public sealed class CharacterFile
{
    public const uint CharacterBlowfishKey = 0x9A46D7C8;

    public static IReadOnlyList<string> SupportedExtensions { get; } = Array.AsReadOnly(
    [
        ".fdp",
        ".fnp",
        ".fhp",
        ".fcp",
        ".fdpu",
        ".fnpu",
        ".fhpu",
        ".fcpu",
    ]);

    private readonly byte[] _body;
    private readonly IReadOnlyDictionary<string, CharacterField> _layout;

    private CharacterFile(byte[] body, int version, IReadOnlyDictionary<string, CharacterField> layout)
    {
        _body = body;
        Version = version;
        _layout = layout;
    }

    public int Version { get; }

    public int BodySize => _body.Length;

    public IEnumerable<string> Fields => _layout.Keys;

    public object this[string field]
    {
        get => Read(_layout[field]);
        set => Write(_layout[field], value);
    }

    public bool Contains(string field) => _layout.ContainsKey(field);

    public static bool IsSupportedPath(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static CharacterFile Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        if (raw.Length < 16)
        {
            throw new InvalidDataException("Character file is shorter than its 16-byte header.");
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0, 4));
        var bodySize = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(4, 4));
        var storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(8, 4));
        if (bodySize < 0 || raw.Length < 16 + bodySize)
        {
            throw new InvalidDataException($"Character body size {bodySize} exceeds file length {raw.Length}.");
        }

        var payload = raw.AsSpan(16, bodySize).ToArray();
        byte[] body;
        if (Path.GetExtension(path).EndsWith("u", StringComparison.OrdinalIgnoreCase))
        {
            body = payload;
        }
        else
        {
            var actualCrc = Crc32(payload);
            if (actualCrc != storedCrc)
            {
                throw new InvalidDataException(
                    $"Character CRC mismatch: stored=0x{storedCrc:X8}, actual=0x{actualCrc:X8}.");
            }

            body = CharacterCipher.Decrypt(payload, DeriveKey(bodySize));
        }

        var layout = LoadLayout(version, out var declaredSize);
        if (body.Length != declaredSize)
        {
            throw new InvalidDataException(
                $"Version {version} layout expects {declaredSize} bytes, file has {body.Length}.");
        }

        return new CharacterFile(body, version, layout);
    }

    public void Save(string path)
    {
        var encrypted = CharacterCipher.Encrypt(_body, DeriveKey(_body.Length));
        var output = new byte[16 + encrypted.Length];
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0, 4), Version);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4, 4), encrypted.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8, 4), Crc32(encrypted));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(12, 4), 0);
        encrypted.CopyTo(output, 16);
        File.WriteAllBytes(path, output);
    }

    public Dictionary<string, object> ToDictionary() =>
        _layout.Keys.ToDictionary(name => name, name => this[name]);

    public static uint DeriveKey(int bodySize) =>
        BinaryPrimitives.ReverseEndianness((uint)bodySize) ^ CharacterBlowfishKey;

    private static IReadOnlyDictionary<string, CharacterField> LoadLayout(int version, out int size)
    {
        if (version is < 10 or > 16)
        {
            throw new InvalidDataException($"Unsupported character version {version}; expected 10 through 16.");
        }

        using var stream = EmbeddedData.Open($"Data.Character.xxpv{version}_layout.json");
        using var document = JsonDocument.Parse(stream);
        size = document.RootElement.GetProperty("size").GetInt32();
        return document.RootElement.GetProperty("fields")
            .EnumerateArray()
            .Select(element => new CharacterField(
                element.GetProperty("name").GetString()!,
                element.GetProperty("offset").GetInt32(),
                element.GetProperty("type").GetString()!,
                element.GetProperty("count").GetInt32()))
            .ToDictionary(field => field.Name, StringComparer.Ordinal);
    }

    private object Read(CharacterField field)
    {
        if (field.Count == 1)
        {
            return ReadScalar(field, field.Offset);
        }

        var values = new object[field.Count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = ReadScalar(field, field.Offset + index * field.Width);
        }

        return values;
    }

    private object ReadScalar(CharacterField field, int offset)
    {
        var span = _body.AsSpan(offset, field.Width);
        return field.Type switch
        {
            "i32" => BinaryPrimitives.ReadInt32LittleEndian(span),
            "u32" => BinaryPrimitives.ReadUInt32LittleEndian(span),
            "i16" => BinaryPrimitives.ReadInt16LittleEndian(span),
            "u16" => BinaryPrimitives.ReadUInt16LittleEndian(span),
            "i8" => unchecked((sbyte)span[0]),
            "u8" => span[0],
            "f32" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span)),
            _ => throw new InvalidDataException($"Unsupported character field type: {field.Type}"),
        };
    }

    private void Write(CharacterField field, object value)
    {
        if (field.Count == 1)
        {
            WriteScalar(field, field.Offset, value);
            return;
        }

        if (value is not System.Collections.IEnumerable enumerable || value is string)
        {
            throw new ArgumentException($"{field.Name} expects {field.Count} values.", nameof(value));
        }

        var values = enumerable.Cast<object>().ToArray();
        if (values.Length != field.Count)
        {
            throw new ArgumentException(
                $"{field.Name} expects {field.Count} values, got {values.Length}.", nameof(value));
        }

        for (var index = 0; index < values.Length; index++)
        {
            WriteScalar(field, field.Offset + index * field.Width, values[index]);
        }
    }

    private void WriteScalar(CharacterField field, int offset, object value)
    {
        var span = _body.AsSpan(offset, field.Width);
        switch (field.Type)
        {
            case "i32": BinaryPrimitives.WriteInt32LittleEndian(span, Convert.ToInt32(value)); break;
            case "u32": BinaryPrimitives.WriteUInt32LittleEndian(span, Convert.ToUInt32(value)); break;
            case "i16": BinaryPrimitives.WriteInt16LittleEndian(span, Convert.ToInt16(value)); break;
            case "u16": BinaryPrimitives.WriteUInt16LittleEndian(span, Convert.ToUInt16(value)); break;
            case "i8": span[0] = unchecked((byte)Convert.ToSByte(value)); break;
            case "u8": span[0] = Convert.ToByte(value); break;
            case "f32":
                BinaryPrimitives.WriteInt32LittleEndian(
                    span, BitConverter.SingleToInt32Bits(Convert.ToSingle(value)));
                break;
            default: throw new InvalidDataException($"Unsupported character field type: {field.Type}");
        }
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }
        }

        return ~crc;
    }
}
