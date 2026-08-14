using AquaModelLibrary.Data.PSO2.Aqua;
using AquaModelLibrary.Data.PSO2.Aqua.CharacterMakingIndexData;
using AquaModelLibrary.Data.Utility;
using Microsoft.Data.Sqlite;

namespace Pso2ShapeStudio.GameData;

public readonly record struct ModelColorMapping(int Red, int Green, int Blue, int Alpha)
{
    public bool UsesAny => Red != 0 || Green != 0 || Blue != 0 || Alpha != 0;
}

public sealed record ModelCatalogRecord(
    string ObjectType,
    int Id,
    int AdjustedId,
    string NameJapanese,
    string NameEnglish,
    string FileName,
    string Hash,
    string? HighQualityFileName,
    string? HighQualityHash,
    int? LinkedInnerId = null,
    int? LinkedOuterId = null,
    ModelColorMapping ColorMapping = default)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(NameEnglish)
        ? NameEnglish
        : !string.IsNullOrWhiteSpace(NameJapanese)
            ? NameJapanese
            : $"Unnamed {Id}";
}

public sealed record ModelCatalogBuildResult(
    string DatabasePath,
    int RecordCount,
    DateTimeOffset BuiltAt,
    TimeSpan Elapsed);

public sealed class ModelCatalog
{
    private const int SchemaVersion = 4;

    /// <summary>Column order shared by every reader; must match ReadRecord.</summary>
    private const string RecordColumns = """
        object_type, id, adjusted_id, name_jp, name_en,
               file_name, hash, ex_file_name, ex_hash,
               linked_inner_id, linked_outer_id,
               color_red, color_green, color_blue, color_alpha
        """;

    /// <summary>
    /// The only object types the search UI offers. Everything else stays in
    /// the catalog (the skin dictionary feeds the texture path, for one) but
    /// never comes back from a search.
    /// </summary>
    public static readonly IReadOnlyList<string> WearObjectTypes =
        ["basewear", "outerwear", "setwear", "costume"];

    public ModelCatalog(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath { get; }

    public bool Exists => File.Exists(DatabasePath);

    /// <summary>
    /// Whether the file on disk was written by this schema. An old catalog
    /// still opens, but its rows may be classified under the previous rules
    /// (NGS setwear used to land in cast_body), so callers should rebuild.
    /// </summary>
    public bool IsCurrentSchema
    {
        get
        {
            using var connection = OpenReadOnly();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            return Convert.ToInt32(command.ExecuteScalar()) == SchemaVersion;
        }
    }

    public int RecordCount
    {
        get
        {
            using var connection = OpenReadOnly();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM objects";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public IReadOnlyList<ModelCatalogRecord> Search(
        string query,
        int limit = 100,
        IReadOnlyCollection<string>? objectTypes = null)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var tokens = (query ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        var predicates = new List<string>();
        if (objectTypes is { Count: > 0 })
        {
            var names = new List<string>();
            var typeIndex = 0;
            foreach (var objectType in objectTypes)
            {
                var parameter = $"$objectType{typeIndex++}";
                names.Add(parameter);
                command.Parameters.AddWithValue(parameter, objectType);
            }

            predicates.Add($"object_type IN ({string.Join(", ", names)})");
        }
        for (var index = 0; index < tokens.Length; index++)
        {
            var parameter = $"$token{index}";
            predicates.Add($"""
                (instr(lower(object_type), lower({parameter})) > 0 OR
                 instr(CAST(id AS TEXT), {parameter}) > 0 OR
                 instr(CAST(adjusted_id AS TEXT), {parameter}) > 0 OR
                 instr(lower(name_jp), lower({parameter})) > 0 OR
                 instr(lower(name_en), lower({parameter})) > 0 OR
                 instr(lower(file_name), lower({parameter})) > 0 OR
                 instr(lower(hash), lower({parameter})) > 0 OR
                 instr(lower(COALESCE(ex_hash, '')), lower({parameter})) > 0)
                """);
            command.Parameters.AddWithValue(parameter, tokens[index]);
        }

        command.CommandText = $"""
            SELECT {RecordColumns}
            FROM objects
            {(predicates.Count == 0 ? "" : "WHERE " + string.Join(" AND ", predicates))}
            ORDER BY CASE WHEN CAST(id AS TEXT) = $query THEN 0 ELSE 1 END,
                     object_type, id
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$query", query ?? "");
        command.Parameters.AddWithValue("$limit", limit);

        return ReadRecords(command);
    }

    public IReadOnlyList<ModelCatalogRecord> GetByObjectType(string objectType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectType);
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {RecordColumns}
            FROM objects
            WHERE object_type = $type
            ORDER BY id
            """;
        command.Parameters.AddWithValue("$type", objectType);

        return ReadRecords(command);
    }

    /// <summary>One record by exact type and id - how a linked
    /// inner/outerwear id becomes a loadable file.</summary>
    public ModelCatalogRecord? FindByTypeAndId(string objectType, int id)
    {
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {RecordColumns}
            FROM objects
            WHERE object_type = $type AND id = $id
            """;
        command.Parameters.AddWithValue("$type", objectType);
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader) : null;
    }

    private static List<ModelCatalogRecord> ReadRecords(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var result = new List<ModelCatalogRecord>();
        while (reader.Read())
        {
            result.Add(ReadRecord(reader));
        }

        return result;
    }

    private static ModelCatalogRecord ReadRecord(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            new ModelColorMapping(
                reader.GetInt32(11),
                reader.GetInt32(12),
                reader.GetInt32(13),
                reader.GetInt32(14)));

    public static ModelCatalogBuildResult BuildFromGame(
        Pso2DataLocator locator,
        string databasePath)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var started = System.Diagnostics.Stopwatch.StartNew();
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var buildingPath = fullPath + ".building";
        if (File.Exists(buildingPath))
        {
            File.Delete(buildingPath);
        }

        try
        {
            var records = ReadGameRecords(locator).ToArray();
            WriteDatabase(buildingPath, locator, records);
            File.Move(buildingPath, fullPath, true);
            started.Stop();
            return new ModelCatalogBuildResult(
                fullPath,
                records.Length,
                DateTimeOffset.UtcNow,
                started.Elapsed);
        }
        catch
        {
            try
            {
                if (File.Exists(buildingPath))
                {
                    File.Delete(buildingPath);
                }
            }
            catch (IOException)
            {
                // Preserve the original database/build exception. A failed
                // temporary cache is replaced on the next build.
            }
            throw;
        }
    }

    private SqliteConnection OpenReadOnly()
    {
        if (!Exists)
        {
            throw new FileNotFoundException("Model catalog has not been built.", DatabasePath);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static IEnumerable<ModelCatalogRecord> ReadGameRecords(Pso2DataLocator locator)
    {
        var cmx = ReferenceGenerator.ExtractCMX(locator.BinPath);
        ReferenceGenerator.ReadCMXText(
            locator.BinPath,
            out var partsText,
            out var accessoryText,
            out _,
            out _);

        var costumeNames = ReadNames(partsText, "costume");
        MergeNames(costumeNames, ReadNames(partsText, "body"));

        foreach (var record in CreateRecords(
                     "accessory", "ac", cmx.accessoryDict.Keys,
                     ReadNames(accessoryText, "decoy"), cmx.accessoryIdLink)) yield return record;
        var basewearNames = ReadNames(partsText, "basewear");
        foreach (var pair in cmx.baseWearDict)
        {
            yield return CreateRecord(
                "basewear", "bw", pair.Key, basewearNames,
                AdjustedId(pair.Key, cmx.baseWearIdLink),
                OptionalId(pair.Value.body2.linkedInnerId),
                OptionalId(pair.Value.body2.linkedOuterId),
                ToModelColorMapping(pair.Value.bodyMaskColorMapping));
        }
        foreach (var pair in cmx.costumeDict)
        {
            yield return CreateRecord(
                ClassifyCostumeId(pair.Key), "bd", pair.Key, costumeNames,
                AdjustedId(pair.Key, cmx.costumeIdLink),
                OptionalId(pair.Value.body2.linkedInnerId),
                OptionalId(pair.Value.body2.linkedOuterId),
                ToModelColorMapping(pair.Value.bodyMaskColorMapping));
        }
        foreach (var record in CreateRecords(
                     "bodypaint", "b1", cmx.bodyPaintDict.Keys,
                     ReadNames(partsText, "bodypaint1"), null)) yield return record;
        foreach (var record in CreateRecords(
                     "cast_arms", "am", cmx.carmDict.Keys,
                     ReadNames(partsText, "arm"), cmx.castArmIdLink)) yield return record;
        foreach (var record in CreateRecords(
                     "cast_legs", "lg", cmx.clegDict.Keys,
                     ReadNames(partsText, "Leg"), cmx.clegIdLink)) yield return record;
        foreach (var record in CreateRecords(
                     "ear", "ea", cmx.ngsEarDict.Keys,
                     ReadNames(partsText, "ears"), null)) yield return record;
        foreach (var record in CreateRecords(
                     "eye", "ey", cmx.eyeDict.Keys,
                     ReadNames(partsText, "eye"), null)) yield return record;
        foreach (var record in CreateRecords(
                     "eyebrow", "eb", cmx.eyebrowDict.Keys,
                     ReadNames(partsText, "eyebrows"), null)) yield return record;
        foreach (var record in CreateRecords(
                     "eyelash", "el", cmx.eyelashDict.Keys,
                     ReadNames(partsText, "eyelashes"), null)) yield return record;
        foreach (var record in CreateRecords(
                     "face", "fc", cmx.faceDict.Keys,
                     ReadNames(partsText, "face"), null)) yield return record;
        foreach (var record in CreateRecords(
                     "face_texture", "f1", cmx.faceTextureDict.Keys,
                     ReadNames(partsText, "facepaint1"), null)) yield return record;
        foreach (var record in CreateRecords(
                     "facepaint", "f2", cmx.fcpDict.Keys,
                     ReadNames(partsText, "facepaint2"), null)) yield return record;
        foreach (var record in CreateRecords(
                     "hair", "hr", cmx.hairDict.Keys,
                     ReadNames(partsText, "hair"), null)) yield return record;
        foreach (var record in CreateRecords(
                     "horn", "hn", cmx.ngsHornDict.Keys,
                     ReadNames(partsText, "horn"), null)) yield return record;
        foreach (var record in CreateRecords(
                     "innerwear", "iw", cmx.innerWearDict.Keys,
                     ReadNames(partsText, "innerwear"), cmx.innerWearIdLink)) yield return record;
        var outerwearNames = ReadNames(partsText, "costume");
        foreach (var pair in cmx.outerDict)
        {
            yield return CreateRecord(
                "outerwear", "ow", pair.Key, outerwearNames,
                AdjustedId(pair.Key, cmx.outerWearIdLink),
                OptionalId(pair.Value.body2.linkedInnerId),
                OptionalId(pair.Value.body2.linkedOuterId),
                ToModelColorMapping(pair.Value.bodyMaskColorMapping));
        }
        foreach (var record in CreateRecords(
                     "skin", "sk", cmx.ngsSkinDict.Keys,
                     ReadNames(partsText, "skin"), null)) yield return record;
        foreach (var record in CreateRecords(
                     "sticker", "b2", cmx.stickerDict.Keys,
                     ReadNames(partsText, "bodypaint2"), null)) yield return record;
        foreach (var record in CreateRecords(
                     "teeth", "de", cmx.ngsTeethDict.Keys,
                     ReadNames(partsText, "dental"), null)) yield return record;
    }

    private static IEnumerable<ModelCatalogRecord> CreateRecords(
        string objectType,
        string tag,
        IEnumerable<int> ids,
        IReadOnlyDictionary<int, (string Japanese, string English)> names,
        Dictionary<int, BCLNObject>? links)
    {
        foreach (var id in ids)
        {
            yield return CreateRecord(objectType, tag, id, names, AdjustedId(id, links));
        }
    }

    private static ModelCatalogRecord CreateRecord(
        string objectType,
        string tag,
        int id,
        IReadOnlyDictionary<int, (string Japanese, string English)> names,
        int adjustedId,
        int? linkedInnerId = null,
        int? linkedOuterId = null,
        ModelColorMapping colorMapping = default)
    {
        var prefix = id >= 100000 ? "character/making_reboot/pl_" : "character/making/pl_";
        var fileName = $"{prefix}{tag}_{adjustedId:00000}.ice";
        names.TryGetValue(id, out var name);
        var officialName = ModManagerItemNames.Find(objectType, id);
        var japaneseName = !string.IsNullOrWhiteSpace(officialName?.Japanese)
            ? officialName.Japanese
            : name.Japanese ?? "";
        var globalEnglishName = !string.IsNullOrWhiteSpace(officialName?.GlobalEnglish)
            ? officialName.GlobalEnglish
            : name.English ?? "";
        var highQuality = Pso2DataLocator.GetHighQualityFileName(fileName);
        return new ModelCatalogRecord(
            RefineWearType(objectType, japaneseName, globalEnglishName),
            id,
            adjustedId,
            japaneseName,
            globalEnglishName,
            fileName,
            Pso2DataLocator.ComputeHash(fileName),
            highQuality,
            highQuality is null ? null : Pso2DataLocator.ComputeHash(highQuality),
            linkedInnerId,
            linkedOuterId,
            colorMapping);
    }

    /// <summary>CMX stores "no link" as -1 (occasionally 0).</summary>
    private static int? OptionalId(int value) => value > 0 ? value : null;

    private static ModelColorMapping ToModelColorMapping(BODYMaskColorMapping mapping) =>
        new(
            (int)mapping.redIndex,
            (int)mapping.greenIndex,
            (int)mapping.blueIndex,
            (int)mapping.alphaIndex);

    /// <summary>
    /// The CMX costume dictionary mixes classic full outfits with cast
    /// bodies, split by id band (blender_pso2_tools objects.py:100-114).
    /// The NGS setwear bands are reserved here too, though measured against
    /// a live install they are empty - see RefineWearType for where NGS
    /// setwear actually lives.
    /// </summary>
    public static string ClassifyCostumeId(int id) => id switch
    {
        < 40000 => "costume",                     // classic full outfits
        >= 100000 and < 300000 => "setwear",      // NGS T1 + T2 bands
        >= 500000 and < 600000 => "setwear",      // NGS genderless band
        _ => "cast_body",                         // classic + NGS cast bands
    };

    /// <summary>
    /// NGS setwear has no dictionary of its own: measured against a live
    /// install, costumeDict holds zero ids in the NGS bands and every [Se]
    /// full-set outfit is registered in baseWearDict alongside [Ba] items.
    /// The only marker separating them is the official name suffix, so the
    /// wear type is refined by name at build time. An unnamed or unsuffixed
    /// item simply stays basewear - the safe direction.
    /// </summary>
    public static string RefineWearType(
        string objectType,
        string japaneseName,
        string englishName)
    {
        if (objectType != "basewear")
        {
            return objectType;
        }

        return japaneseName.Contains("[Se]", StringComparison.OrdinalIgnoreCase) ||
               englishName.Contains("[Se]", StringComparison.OrdinalIgnoreCase)
            ? "setwear"
            : "basewear";
    }

    private static int AdjustedId(int id, Dictionary<int, BCLNObject>? links) =>
        links is not null && links.TryGetValue(id, out var link) ? link.bcln.fileId : id;

    private static Dictionary<int, (string Japanese, string English)> ReadNames(
        PSO2Text text,
        string category)
    {
        var result = new Dictionary<int, (string Japanese, string English)>();
        var index = text.categoryNames.IndexOf(category);
        if (index < 0)
        {
            return result;
        }

        var languages = text.text[index];
        for (var language = 0; language < Math.Min(2, languages.Count); language++)
        {
            foreach (var pair in languages[language])
            {
                var key = pair.name.Trim();
                if (key.StartsWith("No", StringComparison.OrdinalIgnoreCase))
                {
                    key = key[2..];
                }

                if (!int.TryParse(key, out var id))
                {
                    continue;
                }

                result.TryGetValue(id, out var current);
                var value = pair.str.Trim();
                result[id] = language == 0
                    ? (value, current.English ?? "")
                    : (current.Japanese ?? "", value);
            }
        }

        return result;
    }

    private static void MergeNames(
        IDictionary<int, (string Japanese, string English)> target,
        IEnumerable<KeyValuePair<int, (string Japanese, string English)>> source)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static void WriteDatabase(
        string databasePath,
        Pso2DataLocator locator,
        IReadOnlyCollection<ModelCatalogRecord> records)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = $"""
                PRAGMA user_version={SchemaVersion};
                PRAGMA journal_mode=DELETE;
                CREATE TABLE objects(
                    object_type TEXT NOT NULL,
                    id INTEGER NOT NULL,
                    adjusted_id INTEGER NOT NULL,
                    name_jp TEXT NOT NULL,
                    name_en TEXT NOT NULL,
                    file_name TEXT NOT NULL,
                    hash TEXT NOT NULL,
                    ex_file_name TEXT,
                    ex_hash TEXT,
                    linked_inner_id INTEGER,
                    linked_outer_id INTEGER,
                    color_red INTEGER NOT NULL,
                    color_green INTEGER NOT NULL,
                    color_blue INTEGER NOT NULL,
                    color_alpha INTEGER NOT NULL,
                    PRIMARY KEY(object_type, id)
                );
                CREATE INDEX objects_name_jp ON objects(name_jp);
                CREATE INDEX objects_name_en ON objects(name_en);
                CREATE INDEX objects_hash ON objects(hash);
                CREATE INDEX objects_ex_hash ON objects(ex_hash);
                CREATE TABLE metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                """;
            schema.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO objects VALUES(
                $type, $id, $adjusted, $jp, $en, $file, $hash, $exFile, $exHash,
                $linkedInner, $linkedOuter, $colorRed, $colorGreen, $colorBlue, $colorAlpha)
            """;
        var type = insert.Parameters.Add("$type", SqliteType.Text);
        var id = insert.Parameters.Add("$id", SqliteType.Integer);
        var adjusted = insert.Parameters.Add("$adjusted", SqliteType.Integer);
        var jp = insert.Parameters.Add("$jp", SqliteType.Text);
        var en = insert.Parameters.Add("$en", SqliteType.Text);
        var file = insert.Parameters.Add("$file", SqliteType.Text);
        var hash = insert.Parameters.Add("$hash", SqliteType.Text);
        var exFile = insert.Parameters.Add("$exFile", SqliteType.Text);
        var exHash = insert.Parameters.Add("$exHash", SqliteType.Text);
        var linkedInner = insert.Parameters.Add("$linkedInner", SqliteType.Integer);
        var linkedOuter = insert.Parameters.Add("$linkedOuter", SqliteType.Integer);
        var colorRed = insert.Parameters.Add("$colorRed", SqliteType.Integer);
        var colorGreen = insert.Parameters.Add("$colorGreen", SqliteType.Integer);
        var colorBlue = insert.Parameters.Add("$colorBlue", SqliteType.Integer);
        var colorAlpha = insert.Parameters.Add("$colorAlpha", SqliteType.Integer);
        foreach (var record in records)
        {
            type.Value = record.ObjectType;
            id.Value = record.Id;
            adjusted.Value = record.AdjustedId;
            jp.Value = record.NameJapanese;
            en.Value = record.NameEnglish;
            file.Value = record.FileName;
            hash.Value = record.Hash;
            exFile.Value = (object?)record.HighQualityFileName ?? DBNull.Value;
            exHash.Value = (object?)record.HighQualityHash ?? DBNull.Value;
            linkedInner.Value = (object?)record.LinkedInnerId ?? DBNull.Value;
            linkedOuter.Value = (object?)record.LinkedOuterId ?? DBNull.Value;
            colorRed.Value = record.ColorMapping.Red;
            colorGreen.Value = record.ColorMapping.Green;
            colorBlue.Value = record.ColorMapping.Blue;
            colorAlpha.Value = record.ColorMapping.Alpha;
            insert.ExecuteNonQuery();
        }

        using var metadata = connection.CreateCommand();
        metadata.Transaction = transaction;
        metadata.CommandText = """
            INSERT INTO metadata VALUES('data_path', $dataPath);
            INSERT INTO metadata VALUES('built_utc', $builtUtc);
            """;
        metadata.Parameters.AddWithValue("$dataPath", locator.DataPath);
        metadata.Parameters.AddWithValue("$builtUtc", DateTimeOffset.UtcNow.ToString("O"));
        metadata.ExecuteNonQuery();
        transaction.Commit();
    }
}
