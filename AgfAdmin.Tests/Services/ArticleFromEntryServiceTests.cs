using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// "Artikel aanmaken en doorgaan" vanuit aanvoer of ontvangst: precies wat de gebruiker typte onder een bestaand artikel (productsoort +
// ras), nooit dubbel (hoofdletters en dubbele spaties tellen niet, leeg = "geen" en is een echte waarde), idempotent via RequestId.
public sealed class ArticleFromEntryServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private int _hassPeru;

    public void Dispose() => _factory.Dispose();

    private async Task ArrangeAsync()
    {
        await using var db = _factory.CreateContext();
        var avocado = new Product { Name = "Avocado" };
        var hass = new ProductVariant
        {
            Product = avocado, Name = "Hass", Origin = "Peru", OriginCountryCode = "PE", SizeCategory = "18", Packaging = "4 kg doos", PackagingUnit = "colli",
            TradeClass = "I", WeightMethod = "Calculated", DefaultNetKgPerPackage = 4m, DefaultPackagesPerPallet = 240m, GoodsCode = "08044000", GoodsCodeSystem = "CN", DefaultPrice = 0m,
        };
        var branded = new ProductVariant { Product = avocado, Name = "Hass", Origin = "Zuid-Afrika", OriginCountryCode = "ZA", SizeCategory = "12", Packaging = "Test  Doos", TradeClass = "I", Brand = "Test", DefaultPrice = 0m };
        db.AddRange(hass, branded);
        await db.SaveChangesAsync();
        _hassPeru = hass.Id;
    }

    private CreateArticleFromEntryDto Entry(Guid? requestId = null, string country = "ZA", string size = "12", string packaging = "test", string tradeClass = "I",
        string brand = "test", decimal? kg = null, string source = "Receipt") => new()
    {
        RequestId = requestId ?? Guid.NewGuid(), Source = source, TemplateVariantId = _hassPeru, OriginCountryCode = country, SizeCategory = size, Packaging = packaging,
        TradeClass = tradeClass, Brand = brand, NetKgPerPackage = kg, ActorReference = "Kees",
    };

    private async Task<ServiceResult<ArticleFromEntryResultDto>> CreateAsync(CreateArticleFromEntryDto request)
    {
        await using var db = _factory.CreateContext();
        return await new ArticleFromEntryService(db).CreateAsync(request);
    }

    private async Task<int> VariantCountAsync()
    {
        await using var db = _factory.CreateContext();
        return await db.ProductVariants.CountAsync();
    }

    [Fact]
    public async Task A_New_Combination_Is_Created_With_What_Was_Typed_And_The_Defaults_Of_The_Same_Article()
    {
        await ArrangeAsync();
        var requestId = Guid.NewGuid();
        var result = await CreateAsync(Entry(requestId, size: " 14 ", packaging: "test  krat", brand: "Verita", kg: 4.25m));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var article = result.Value.Article;
        Assert.Equal(("Avocado", "Hass", "ZA", "Zuid-Afrika", "14", "test krat", "I", "Verita"),
            (article.ProductName, article.VariantName, article.OriginCountryCode, article.Origin, article.SizeCategory, article.Packaging, article.TradeClass, article.Brand));
        Assert.Equal(("Calculated", "colli", 4.25m, 240m, "08044000"), (article.WeightMethod, article.PackagingUnit, article.DefaultNetKgPerPackage.Value, article.DefaultPackagesPerPallet.Value, article.GoodsCode));

        await using var db = _factory.CreateContext();
        var stored = await db.ProductVariants.SingleAsync(v => v.Id == article.Id);
        Assert.Equal(requestId, stored.RequestId);
        var created = await db.ArticleAuditEvents.SingleAsync();
        Assert.Equal(("CreatedFromReceipt", article.Id, "Kees", requestId), (created.EventType, created.ProductVariantId.Value, created.ActorReference, created.RequestId.Value));
        Assert.Equal("Avocado Hass · Zuid-Afrika · maat 14 · verpakking test krat · klasse I · merk Verita", created.Description);
    }

    [Fact]
    public async Task Without_Brand_And_Packaging_The_Article_Has_None_And_The_Default_Kg_Of_The_Article()
    {
        await ArrangeAsync();
        var result = await CreateAsync(Entry(packaging: null, brand: "  ", source: "Supply"));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal((null, null, 4m), (result.Value.Article.Packaging, result.Value.Article.Brand, result.Value.Article.DefaultNetKgPerPackage));
        await using var db = _factory.CreateContext();
        Assert.Equal("CreatedFromSupply", (await db.ArticleAuditEvents.SingleAsync()).EventType);
    }

    [Fact]
    public async Task A_Repeated_Request_Returns_The_Same_Article_And_Never_Creates_A_Second()
    {
        await ArrangeAsync();
        var requestId = Guid.NewGuid();
        var first = await CreateAsync(Entry(requestId));
        var again = await CreateAsync(Entry(requestId));

        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Ok), (first.Status, again.Status));
        Assert.True(again.Value.Replayed);
        Assert.Equal(first.Value.Article.Id, again.Value.Article.Id);
        Assert.Equal(3, await VariantCountAsync());

        var reused = await CreateAsync(Entry(requestId, size: "20"));
        Assert.Equal(ServiceResultStatus.Conflict, reused.Status);
        Assert.Equal(3, await VariantCountAsync());
    }

    // "Test" en "test", dubbele spaties en null tegen leeg zijn hetzelfde: het bestaande artikel komt terug, er komt geen tweede.
    [Theory]
    [InlineData("12", "test doos", "I", "test")]
    [InlineData("12", "TEST   DOOS", "i", "TEST")]
    [InlineData(" 12", " Test Doos ", "I", "Test ")]
    public async Task An_Almost_Identical_Combination_Is_Never_Created_Twice(string size, string packaging, string tradeClass, string brand)
    {
        await ArrangeAsync();
        var result = await CreateAsync(Entry(size: size, packaging: packaging, tradeClass: tradeClass, brand: brand));
        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains("bestaat al", result.Message);
        Assert.Equal(2, await VariantCountAsync());
    }

    // Leeg is een echte waarde: bestaand met merk "Test", ingevuld zonder merk (of andersom) is een ander artikel.
    [Theory]
    [InlineData(null, "test doos")]
    [InlineData("Test", null)]
    [InlineData("Test", "test doos 2")]
    public async Task Empty_Against_Filled_Is_Another_Article(string brand, string packaging)
    {
        await ArrangeAsync();
        var result = await CreateAsync(Entry(packaging: packaging, brand: brand));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(3, await VariantCountAsync());
    }

    [Fact]
    public async Task An_Archived_Twin_Is_Not_Recreated_But_Pointed_Out()
    {
        await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            var branded = await db.ProductVariants.SingleAsync(v => v.Brand == "Test");
            branded.IsArchived = true;
            branded.ArchivedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var result = await CreateAsync(Entry(packaging: "test doos"));
        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains("gearchiveerd", result.Message);
    }

    [Theory]
    [InlineData(null, "Receipt", true)]
    [InlineData("XX", "Receipt", true)]
    [InlineData("ZA", "Elsewhere", true)]
    [InlineData("ZA", "Receipt", false)]
    public async Task Never_Half_An_Article_A_Country_From_The_List_An_Existing_Article_And_A_Known_Source(string country, string source, bool validTemplate)
    {
        await ArrangeAsync();
        var request = Entry(country: country, source: source);
        if (!validTemplate)
        {
            request.TemplateVariantId = 9999;
        }
        Assert.Equal(ServiceResultStatus.Validation, (await CreateAsync(request)).Status);
        Assert.Equal(2, await VariantCountAsync());
    }

    [Fact]
    public async Task The_Created_Article_Can_Be_Used_On_A_New_Supply_Line()
    {
        await ArrangeAsync();
        var article = (await CreateAsync(Entry(source: "Supply"))).Value.Article;
        await using var db = _factory.CreateContext();
        var vendor = new Vendor { Name = "Nature's Pride" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        var supply = await new SupplyCommitmentService(db).CreateAsync(new CreateSupplyCommitmentDto
        {
            RequestId = Guid.NewGuid(), VendorId = vendor.Id, Route = "Warehouse", Origin = "", ExpectedAt = PurchaseOrderTestData.ExpectedAt,
            Lines = new List<CreateSupplyCommitmentLineDto> { new() { ProductVariantId = article.Id, ExpectedUnit = "colli", ExpectedQuantity = 10m } }
        });
        Assert.Equal(ServiceResultStatus.Created, supply.Status);
    }
}
