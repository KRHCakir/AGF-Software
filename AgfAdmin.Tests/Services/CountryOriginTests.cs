using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Herkomst als land: het artikelscherm kiest een land (nooit vrije tekst), opgeslagen wordt de landcode met de Nederlandse naam als
// weergave; oude varianten zonder land blijven werken en staan in "Herkomst opschonen"; eigen aliassen zijn alleen invoerhulp.
public sealed class CountryOriginTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<ServiceResult<ArticleDto>> CreateAsync(SaveArticleDto request)
    {
        await using var db = _factory.CreateContext();
        return await new ArticleService(db).CreateAsync(request);
    }

    private async Task<int> OldVariantAsync(string origin)
    {
        await using var db = _factory.CreateContext();
        var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass", Origin = origin, Packaging = "4kg Box", DefaultPrice = 1m };
        db.Add(variant);
        await db.SaveChangesAsync();
        return variant.Id;
    }

    [Theory]
    [InlineData("pe")]
    [InlineData(" PE ")]
    public async Task The_country_is_stored_never_the_abbreviation_or_a_free_text(string code)
    {
        var created = await CreateAsync(new SaveArticleDto { ProductName = "Avocado", VariantName = "Hass", OriginCountryCode = code, Origin = "PE (vrij)" });

        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal(("PE", "Peru"), (created.Value.OriginCountryCode, created.Value.Origin));
        await using var db = _factory.CreateContext();
        var stored = await db.ProductVariants.AsNoTracking().SingleAsync();
        Assert.Equal(("PE", "Peru"), (stored.OriginCountryCode, stored.Origin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XX")]
    [InlineData("Peru")]
    public async Task A_new_article_needs_a_country_from_the_list(string code)
    {
        var created = await CreateAsync(new SaveArticleDto { ProductName = "Avocado", OriginCountryCode = code });

        Assert.Equal(ServiceResultStatus.Validation, created.Status);
        Assert.Contains("originCountryCode", created.Errors.Keys);
    }

    [Fact]
    public async Task Old_variants_without_a_country_keep_working_and_are_listed_for_cleanup()
    {
        var demo = await OldVariantAsync("Peru (DEMO)");
        var plain = await OldVariantAsync("Peru");
        await CreateAsync(new SaveArticleDto { ProductName = "Mango", OriginCountryCode = "BR" });

        await using (var db = _factory.CreateContext())
        {
            var service = new ArticleService(db);
            Assert.Equal("Peru (DEMO)", (await service.GetAsync(demo)).Value.Origin);
            Assert.Equal(3, (await service.ListAsync()).Count);
            var cleanup = await service.OriginCleanupAsync();
            Assert.Equal(new[] { demo, plain }.OrderBy(id => id), cleanup.Select(i => i.Id).OrderBy(id => id));
            Assert.Empty(cleanup.Single(i => i.Id == demo).Suggestions);
            Assert.Equal(new[] { "PE" }, cleanup.Single(i => i.Id == plain).Suggestions);

            var set = await service.SetOriginCountryAsync(demo, new SetOriginCountryDto { OriginCountryCode = "pe" });
            Assert.Equal(("PE", "Peru"), (set.Value.OriginCountryCode, set.Value.Origin));
            Assert.Equal(ServiceResultStatus.Validation, (await service.SetOriginCountryAsync(plain, new SetOriginCountryDto { OriginCountryCode = "EU" })).Status);
            Assert.Equal(ServiceResultStatus.NotFound, (await service.SetOriginCountryAsync(9999, new SetOriginCountryDto { OriginCountryCode = "PE" })).Status);
        }
        await using var verify = _factory.CreateContext();
        Assert.Equal(new[] { plain }, (await new ArticleService(verify).OriginCleanupAsync()).Select(i => i.Id));
    }

    [Fact]
    public async Task Aliases_point_to_one_country_and_are_only_an_input_aid()
    {
        await using var db = _factory.CreateContext();
        var service = new CountryService(db);

        var created = await service.AddAliasAsync(new AddCountryAliasDto { Alias = " PER ", CountryCode = "pe", ActorReference = "Kees" });
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal(("PER", "PE"), (created.Value.Alias, created.Value.CountryCode));
        Assert.Equal(ServiceResultStatus.Ok, (await service.AddAliasAsync(new AddCountryAliasDto { Alias = "per", CountryCode = "PE" })).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await service.AddAliasAsync(new AddCountryAliasDto { Alias = "Per", CountryCode = "CL" })).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await service.AddAliasAsync(new AddCountryAliasDto { Alias = "CL", CountryCode = "PE" })).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await service.AddAliasAsync(new AddCountryAliasDto { Alias = "Chili", CountryCode = "PE" })).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await service.AddAliasAsync(new AddCountryAliasDto { Alias = " ", CountryCode = "PE" })).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await service.AddAliasAsync(new AddCountryAliasDto { Alias = "Kaap", CountryCode = "XX" })).Status);

        var reference = await service.ReferenceAsync();
        Assert.Equal(249, reference.Countries.Count);
        Assert.Equal("Afghanistan", reference.Countries[0].NameNl);
        Assert.Equal(new[] { ("PER", "PE") }, reference.Aliases.Select(a => (a.Alias, a.CountryCode)));

        var removed = await service.RemoveAliasAsync(created.Value.Id);
        Assert.Empty(removed.Value.Aliases);
        Assert.Equal(ServiceResultStatus.NotFound, (await service.RemoveAliasAsync(created.Value.Id)).Status);
    }
}
