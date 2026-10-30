using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Vaste landenlijst en de regels voor herkomst: eenduidig koppelen (spiegel van de SQL in de migratie AddCountries) en eigen aliassen.
public sealed class CountryRulesTests
{
    [Fact]
    public void The_list_has_all_249_iso_codes_with_unique_codes_and_names()
    {
        Assert.Equal(249, CountryList.All.Count);
        Assert.All(CountryList.All, c => Assert.Matches("^[A-Z]{2}$", c.Code));
        Assert.Equal(249, CountryList.All.Select(c => c.Code).Distinct().Count());
        // Geen twee landen met dezelfde sleutel voor naam of code: anders zou koppelen of zoeken niet eenduidig zijn.
        var keys = CountryList.All.SelectMany(c => new[] { CountryRules.Key(c.Code), CountryRules.Key(c.NameNl), CountryRules.Key(c.NameEn) }.Distinct().Select(k => (Key: k, c.Code)));
        Assert.Empty(keys.GroupBy(k => k.Key).Where(g => g.Select(k => k.Code).Distinct().Count() > 1).Select(g => g.Key));
        Assert.Equal(("Peru", "Peru"), (CountryRules.Find("PE").NameNl, CountryRules.Find("PE").NameEn));
        Assert.Equal("Zuid-Afrika", CountryRules.Find("ZA").NameNl);
    }

    [Theory]
    [InlineData("PE", "PE")]
    [InlineData("pe", "PE")]
    [InlineData("Peru", "PE")]
    [InlineData("  PERU  ", "PE")]
    [InlineData("Brazilië", "BR")]
    [InlineData("brazilie", "BR")]
    [InlineData("Brazil", "BR")]
    [InlineData("south   africa", "ZA")]
    [InlineData("Zuid-Afrika", "ZA")]
    [InlineData("Côte d'Ivoire", "CI")]
    [InlineData("Spain", "ES")]
    [InlineData("Peru (DEMO)", null)]
    [InlineData("EU", null)]
    [InlineData("Divers", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_an_unambiguous_text_becomes_a_country(string text, string expected) =>
        Assert.Equal(expected, CountryRules.Unambiguous(text));

    [Fact]
    public void Aliases_point_to_exactly_one_country_and_never_to_another_countrys_code_or_name()
    {
        var existing = new[] { (Key: "kaap", CountryCode: "ZA") };
        Assert.Null(CountryRules.AliasConflict("PER", "PE", existing));
        Assert.Null(CountryRules.AliasConflict("Peru", "PE", existing)); // eigen naam: overbodig maar niet verwarrend
        Assert.Contains("Chili", CountryRules.AliasConflict("CL", "PE", existing));
        Assert.Contains("Spanje", CountryRules.AliasConflict("spain", "PE", existing));
        Assert.Contains("already points to Zuid-Afrika", CountryRules.AliasConflict(" Kaap ", "PE", existing));
        Assert.Contains("already exists", CountryRules.AliasConflict("KAAP", "ZA", existing));
        Assert.Contains("not in the country list", CountryRules.AliasConflict("X", "XX", existing));
        Assert.Contains("required", CountryRules.AliasConflict("  ", "PE", existing));
        Assert.Contains("at most", CountryRules.AliasConflict(new string('a', CountryRules.MaxAliasLength + 1), "PE", existing));
    }

    [Fact]
    public async Task The_country_list_is_seeded_in_the_database()
    {
        using var factory = new SqliteAppDbContextFactory();
        await using var db = factory.CreateContext();
        Assert.Equal(249, await db.Countries.CountAsync());
        Assert.Equal("Zuid-Afrika", (await db.Countries.SingleAsync(c => c.Code == "ZA")).NameNl);
    }
}
