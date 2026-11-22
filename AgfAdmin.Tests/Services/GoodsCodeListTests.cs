using AgfAdmin.DTOs;
using AgfAdmin.Services;
using Xunit;

namespace AgfAdmin.Tests.Services;

// De meegeleverde goederencodelijst (Data/Reference/goods-codes.json, gemaakt met scripts/goods-codes/build_cn_reference.py uit de
// SKOS-dataset GN 2026 van het Publicatiebureau): hoofdstukken 07, 08, 09 en 20 volledig, en de melding van 22 november (gember).
public class GoodsCodeListTests
{
    [Fact]
    public void The_Shipped_List_Loads_With_Paths_And_Its_Source()
    {
        var catalog = Shipped();
        Assert.False(string.IsNullOrWhiteSpace(catalog.Source.Name));
        Assert.False(string.IsNullOrWhiteSpace(catalog.Source.LookupUrl));
        Assert.Contains("GN 2026", catalog.Source.Version);
        Assert.All(catalog.Entries, e => Assert.True(ArticleRules.IsGoodsCodeFor(e.System, e.Code), $"{e.Code} is not a valid {e.System} code"));
        // Geen onderbouwde TARIC-referenties in de meegeleverde lijst: het antwoord onder TARIC is eerlijk leeg.
        Assert.Empty(catalog.EntriesFor("TARIC"));
        // Elke GN-code valt onder een HS-post met een omschrijving op postniveau; avocado: HS 080440, GN 08044000.
        var headings = catalog.EntriesFor("HS");
        Assert.All(catalog.Entries, e => Assert.Contains(headings, h => h.Code == e.HsCode && !string.IsNullOrWhiteSpace(h.DescriptionNl) && !string.IsNullOrWhiteSpace(h.DescriptionEn)));
        Assert.All(headings, h => Assert.Equal(6, h.Code.Length));
        var avocado = catalog.Search("avocado", "HS").First();
        Assert.Equal("080440", avocado.Code);
        Assert.Equal(new[] { "08044000" }, avocado.MergedCodes);
        Assert.EndsWith("avocado's", avocado.DescriptionNl);
        Assert.Equal(3, catalog.Search("tomaten", "HS").Single(h => h.Code == "070200").MergedCodes.Count);
    }

    // Hoofdstukken 07, 08, 09 en 20 volledig (GN 2026, scripts/goods-codes/build_cn_reference.py); andere hoofdstukken niet.
    [Fact]
    public void The_Shipped_List_Has_Chapters_07_08_09_And_20_In_Full()
    {
        var cn = Shipped().EntriesFor("CN");
        var perChapter = cn.GroupBy(e => e.Code[..2]).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new[] { "07", "08", "09", "20" }, perChapter.Keys.OrderBy(k => k));
        Assert.Equal(602, cn.Count);
        Assert.Equal((117, 128, 50, 307), (perChapter["07"], perChapter["08"], perChapter["09"], perChapter["20"]));
        Assert.Equal(234, Shipped().EntriesFor("HS").Count);
    }

    // De melding van de eigenaar (22 november): "gember" en "ginger" vonden niets, omdat post 0910 niet in de korte lijst stond
    // (34 GN-codes, 25 HS-posten). Nu vinden naam, code met en zonder spatie en de GN-code de gember.
    [Theory]
    [InlineData("gember")]
    [InlineData("Ginger")]
    [InlineData("GEMBER vers")]
    public void Ginger_Is_Found_By_Its_Dutch_And_English_Name(string query)
    {
        var catalog = Shipped();
        Assert.Equal("091011", catalog.Search(query.Replace(" vers", ""), "HS").First().Code);
        Assert.Equal(new[] { "091011", "091012" }, catalog.Search(query.Replace(" vers", ""), "HS").Take(2).Select(m => m.Code));
        Assert.Equal("09101100", catalog.Search(query.Replace(" vers", ""), "CN").First().Code);
    }

    [Fact]
    public void Ginger_Is_Found_By_Code_With_And_Without_Separators()
    {
        var catalog = Shipped();
        Assert.Equal(new[] { "091011", "091012", "091020", "091030", "091091", "091099" }, catalog.Search("0910", "HS").Select(m => m.Code));
        Assert.Equal(new[] { "091011" }, catalog.Search("0910 11", "HS").Select(m => m.Code));
        Assert.Equal(new[] { "091011" }, catalog.Search("0910.11", "HS").Select(m => m.Code));
        Assert.Equal(new[] { "09101100" }, catalog.Search("0910 11", "CN").Select(m => m.Code));
        Assert.Equal(new[] { "09101100" }, catalog.Search("0910.11.00", "CN").Select(m => m.Code));
        Assert.True(catalog.IsKnown("CN", "0910 11 00"));
        Assert.True(catalog.IsKnown("HS", "0910.11"));
    }

    // Een code buiten de lijst (ander hoofdstuk of een tikfout) is onbekend: het scherm waarschuwt, opslaan blijft mogelijk.
    [Fact]
    public void A_Code_Outside_The_List_Is_Unknown()
    {
        var catalog = Shipped();
        Assert.False(catalog.IsKnown("CN", "06031100"));   // snijbloemen, hoofdstuk 06
        Assert.False(catalog.IsKnown("CN", "09101199"));   // bestaat niet
        Assert.False(catalog.IsKnown("HS", "09101100"));   // GN-code is geen HS-code
        Assert.False(catalog.IsKnown("TARIC", "0910110000"));
        Assert.False(catalog.IsKnown("CN", null));
        // Het zoekantwoord zegt het erbij, alleen bij een volledige code van het stelsel.
        Assert.False(catalog.Result("0603 11 00", "CN").Known);
        Assert.True(catalog.Result("0910 11 00", "CN").Known);
        Assert.Null(catalog.Result("0910", "CN").Known);
        Assert.Null(catalog.Result("gember", "CN").Known);
        Assert.True(catalog.Result("091011", "HS").Known);
    }


    // Synoniemen: een gangbare naam zoekt ook op het woord in de GN-tekst; elk zoekwoord staat echt in de lijst.
    [Theory]
    [InlineData("paprika", "070960")]
    [InlineData("blauwe bes", "081040")]
    [InlineData("limoen", "080550")]
    [InlineData("kaki", "081070")]
    [InlineData("cherrytomaat", "070200")]
    [InlineData("ijsbergsla", "070511")]
    public void A_Common_Name_Finds_The_Code_Through_A_Synonym(string query, string hs)
    {
        Assert.Contains(Shipped().Search(query, "HS"), m => m.Code == hs);
    }

    [Fact]
    public void Every_Synonym_Search_Word_Occurs_In_The_Shipped_List()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Reference", GoodsCodeCatalog.SynonymsFileName);
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var texts = Shipped().EntriesFor("CN").Select(e => GoodsCodeCatalog.SearchKey(e.DescriptionNl + " " + e.DescriptionEn)).ToList();
        foreach (var synonym in json.RootElement.GetProperty("synonyms").EnumerateArray())
        {
            foreach (var word in synonym.GetProperty("search").EnumerateArray().Select(w => GoodsCodeCatalog.SearchKey(w.GetString())))
            {
                Assert.True(texts.Any(t => t.Contains(word)), $"zoekwoord '{word}' staat niet in de lijst");
            }
        }
    }

    private static GoodsCodeCatalog Shipped() => GoodsCodeCatalog.Load(Path.Combine(AppContext.BaseDirectory, GoodsCodeCatalog.DefaultRelativePath));
}
