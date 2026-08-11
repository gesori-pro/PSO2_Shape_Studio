using Microsoft.Data.Sqlite;
using Pso2ShapeStudio.GameData;

namespace Pso2ShapeStudio.Core.Tests.Data;

public sealed class ModelCatalogTests
{
    [Fact]
    public void ModManagerNames_ContainGlobalBasewearReference()
    {
        var item = ModManagerItemNames.Find("basewear", 201630);

        Assert.NotNull(item);
        Assert.Equal("N-コンバットジャケットT2[Ba]", item.Japanese);
        Assert.Equal("N-Combat Jacket T2 [Ba]", item.GlobalEnglish);
        Assert.Equal(36_954, ModManagerItemNames.Count);
    }

    [Theory]
    [InlineData(1, "costume")]        // classic full outfit
    [InlineData(39999, "costume")]
    [InlineData(40000, "cast_body")]  // classic cast band
    [InlineData(59999, "cast_body")]
    [InlineData(105000, "setwear")]   // NGS T1 sets
    [InlineData(201630, "setwear")]   // NGS T2 sets
    [InlineData(299999, "setwear")]
    [InlineData(300000, "cast_body")] // NGS cast band
    [InlineData(400001, "cast_body")]
    [InlineData(500001, "setwear")]   // NGS genderless sets
    [InlineData(600001, "cast_body")]
    public void ClassifyCostumeId_SplitsByIdBand(int id, string expected)
    {
        Assert.Equal(expected, ModelCatalog.ClassifyCostumeId(id));
    }

    [Theory]
    [InlineData("basewear", "N-コンバットジャケットT2[Ba]", "N-Combat Jacket T2 [Ba]", "basewear")]
    [InlineData("basewear", "N-バニースーツ[Se]", "N-Bunny Suit [Se]", "setwear")]
    [InlineData("basewear", "N-バニースーツ[Se]", "", "setwear")]      // JP name only
    [InlineData("basewear", "", "N-Bunny Suit [Se]", "setwear")]      // EN name only
    [InlineData("basewear", "名前なし", "", "basewear")]               // no suffix -> stays
    [InlineData("outerwear", "N-何か[Se]", "", "outerwear")]           // only basewear refines
    public void RefineWearType_SplitsSetwearByNameSuffix(
        string objectType, string japanese, string english, string expected)
    {
        Assert.Equal(expected, ModelCatalog.RefineWearType(objectType, japanese, english));
    }

    [Fact]
    public void Search_WithWearWhitelist_ExcludesOtherCategories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pso2-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "objects.db");
        try
        {
            CreateDatabase(path);
            var catalog = new ModelCatalog(path);

            // A bare token that matches the hair record and both wear records.
            var unrestricted = catalog.Search("N-");
            Assert.Contains(unrestricted, record => record.ObjectType == "hair");

            var restricted = catalog.Search("N-", 100, ModelCatalog.WearObjectTypes);
            Assert.NotEmpty(restricted);
            Assert.All(restricted, record =>
                Assert.Contains(record.ObjectType, ModelCatalog.WearObjectTypes));
            Assert.DoesNotContain(restricted, record => record.ObjectType == "hair");
            Assert.Contains(restricted, record => record.ObjectType == "setwear");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindByTypeAndId_ReturnsLinkedWearIds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pso2-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "objects.db");
        try
        {
            CreateDatabase(path);
            var catalog = new ModelCatalog(path);

            var setwear = catalog.FindByTypeAndId("setwear", 205990);
            Assert.NotNull(setwear);
            Assert.Equal(100400, setwear.LinkedOuterId);
            Assert.Null(setwear.LinkedInnerId);
            Assert.Null(catalog.FindByTypeAndId("setwear", 999999));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Search_MatchesTypeIdNameAndHashTokens()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pso2-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "objects.db");
        try
        {
            CreateDatabase(path);
            var catalog = new ModelCatalog(path);

            var byId = Assert.Single(catalog.Search("201630"));
            Assert.Equal("basewear", byId.ObjectType);
            Assert.Single(catalog.Search("basewear コンバット"));
            Assert.Single(catalog.Search("cf540ec3"));
            var skins = catalog.GetByObjectType("skin");
            Assert.Equal([100000, 200000], skins.Select(skin => skin.Id));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CreateDatabase(string path)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
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
                PRIMARY KEY(object_type, id)
            );
            INSERT INTO objects VALUES(
                'basewear', 201630, 201630, 'N-コンバットジャケットT2[Ba]', '',
                'character/making_reboot/pl_bw_201630.ice',
                '1e75629697436ed480353c3ebc1c59b3',
                'character/making_reboot_ex/pl_bw_201630_ex.ice',
                'cf540ec3ff917cd65e9fd3e67f4fecfa', NULL, NULL
            );
            INSERT INTO objects VALUES(
                'skin', 100000, 100000, 'ベースボディT1', 'Base Body T1',
                'character/making_reboot/pl_sk_100000.ice', 'a', NULL, NULL, NULL, NULL
            );
            INSERT INTO objects VALUES(
                'skin', 200000, 200000, 'ベースボディT2', 'Base Body T2',
                'character/making_reboot/pl_sk_200000.ice', 'b', NULL, NULL, NULL, NULL
            );
            INSERT INTO objects VALUES(
                'hair', 110000, 110000, 'N-ポニーテール', 'N-Ponytail',
                'character/making_reboot/pl_hr_110000.ice', 'c', NULL, NULL, NULL, NULL
            );
            INSERT INTO objects VALUES(
                'setwear', 205990, 205990, 'N-ドレスセット', 'N-Dress Set',
                'character/making_reboot/pl_bd_205990.ice', 'd', NULL, NULL, NULL, 100400
            );
            """;
        command.ExecuteNonQuery();
    }
}
