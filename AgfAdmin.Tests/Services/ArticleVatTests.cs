using AgfAdmin.DTOs;
using AgfAdmin.Migrations;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: het btw-tarief hoort bij het artikel (9% onbewerkt, de standaard; 21% bewerkt; 0%). Een Dienst is standaard 21%.
// Op een nieuwe factuurregel is het tarief van het artikel het voorstel; per regel aanpassen blijft. Btw per land komt later.
public class ArticleVatTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static SaveArticleDto Ginger(decimal? vat = null) => new() { ProductName = "Gember", OriginCountryCode = "CN", TradeClass = "I", VatPercentage = vat };

    [Theory]
    [InlineData(null, 9)]
    [InlineData(9, 9)]
    [InlineData(21, 21)]
    [InlineData(0, 0)]
    public async Task A_New_Article_Gets_Its_Rate_With_9_As_The_Standard(int? vat, int expected)
    {
        await using var db = _factory.CreateContext();
        var created = await new ArticleService(db).CreateAsync(Ginger(vat));
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal(expected, created.Value.VatPercentage);
        Assert.Equal(expected, (await db.ProductVariants.AsNoTracking().SingleAsync()).VatPercentage);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(19)]
    [InlineData(-9)]
    public async Task Another_Rate_Is_Refused(int vat)
    {
        await using var db = _factory.CreateContext();
        var refused = await new ArticleService(db).CreateAsync(Ginger(vat));
        Assert.Equal(ServiceResultStatus.Validation, refused.Status);
        Assert.Contains("vatPercentage", refused.Errors.Keys);
    }

    [Fact]
    public async Task Editing_Without_A_Rate_Keeps_It_And_A_New_Rate_Is_Saved()
    {
        await using var db = _factory.CreateContext();
        var service = new ArticleService(db);
        var created = await service.CreateAsync(Ginger(21));
        var kept = await service.UpdateAsync(created.Value.Id, Ginger());
        Assert.Equal(21, kept.Value.VatPercentage);
        var changed = await service.UpdateAsync(created.Value.Id, Ginger(9));
        Assert.Equal(9, changed.Value.VatPercentage);
    }

    [Fact]
    public async Task An_Article_Made_From_An_Entry_Takes_The_Rate_Of_Its_Template()
    {
        int template;
        await using (var db = _factory.CreateContext())
        {
            var variant = new ProductVariant { Product = new Product { Name = "Gember" }, Name = "", Origin = "China", OriginCountryCode = "CN", TradeClass = "I", DefaultPrice = 0m, VatPercentage = 21m };
            db.Add(variant);
            await db.SaveChangesAsync();
            template = variant.Id;
        }
        await using (var db = _factory.CreateContext())
        {
            var created = await new ArticleFromEntryService(db).CreateAsync(new CreateArticleFromEntryDto
            {
                RequestId = Guid.NewGuid(), Source = "Supply", TemplateVariantId = template, OriginCountryCode = "PE", TradeClass = "I",
            });
            Assert.Equal(21, created.Value.Article.VatPercentage);
        }
    }

    [Fact]
    public async Task A_Service_Is_21_By_Default_And_Its_Rate_Can_Change()
    {
        await using var db = _factory.CreateContext();
        var service = new CostArticleService(db);
        var handling = await service.CreateAsync(new CreateCostArticleDto { RequestId = Guid.NewGuid(), Name = "Koelcel" });
        Assert.Equal(21, handling.Value.VatPercentage);
        var low = await service.CreateAsync(new CreateCostArticleDto { RequestId = Guid.NewGuid(), Name = "Sorteren", VatPercentage = 9m });
        Assert.Equal(9, low.Value.VatPercentage);
        Assert.Equal(ServiceResultStatus.Validation, (await service.CreateAsync(new CreateCostArticleDto { RequestId = Guid.NewGuid(), Name = "Iets", VatPercentage = 6m })).Status);
        var zero = await service.SetServiceVatAsync(handling.Value.Id, new SetCostArticleVatDto { VatPercentage = 0m });
        Assert.Equal(0, zero.Value.VatPercentage);
        Assert.Equal(ServiceResultStatus.Validation, (await service.SetServiceVatAsync(handling.Value.Id, new SetCostArticleVatDto())).Status);
        // Emballage valt hier niet onder: die houdt haar eigen btw-percentage (emballage-ronde).
        var packaging = await service.CreatePackagingAsync(new SavePackagingArticleDto { RequestId = Guid.NewGuid(), Name = "Krat groot", VatPercentage = 9m });
        Assert.Equal(ServiceResultStatus.NotFound, (await service.SetServiceVatAsync(packaging.Value.Id, new SetCostArticleVatDto { VatPercentage = 21m })).Status);
    }

    // Het voorstel op een nieuwe factuurregel: het tarief van het artikel of de Dienst; een ingevuld tarief per regel wint altijd.
    [Fact]
    public async Task An_Invoice_Line_Without_A_Rate_Takes_The_Rate_Of_Its_Article_Or_Service()
    {
        int vendorId, ginger, juice, handling;
        await using (var db = _factory.CreateContext())
        {
            var vendor = new Vendor { Name = "Gemberhandel", VATNumber = "NL001", DefaultCurrency = "EUR", PaymentTermsDays = 30 };
            var raw = new ProductVariant { Product = new Product { Name = "Gember" }, Name = "", Origin = "China", OriginCountryCode = "CN", TradeClass = "I", DefaultPrice = 0m, VatPercentage = 9m };
            var processed = new ProductVariant { Product = new Product { Name = "Gembersap" }, Name = "", Origin = "Nederland", OriginCountryCode = "NL", TradeClass = "I", DefaultPrice = 0m, VatPercentage = 21m };
            var service = new CostArticle { Name = "Koelcel", NameKey = "koelcel", Kind = CostArticleRules.KindCost, VatPercentage = 21m, CreatedBy = "test" };
            db.AddRange(vendor, raw, processed, service);
            await db.SaveChangesAsync();
            (vendorId, ginger, juice, handling) = (vendor.Id, raw.Id, processed.Id, service.Id);
        }
        await using (var db = _factory.CreateContext())
        {
            var created = await new PurchaseInvoiceService(db).CreateAsync(new CreatePurchaseInvoiceDto
            {
                RequestId = Guid.NewGuid(), VendorId = vendorId, InvoiceNumber = "GH-1", InvoiceDate = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc),
                Lines = new List<CreatePurchaseInvoiceLineDto>
                {
                    new() { ProductVariantId = ginger, Quantity = 10m, UnitPrice = 10m },
                    new() { ProductVariantId = juice, Quantity = 10m, UnitPrice = 10m },
                    new() { CostArticleId = handling, Quantity = 1m, UnitPrice = 10m },
                    new() { ProductVariantId = juice, Quantity = 1m, UnitPrice = 10m, VatPercentage = 9m },
                    new() { Description = "Zonder artikel", Quantity = 1m, UnitPrice = 10m },
                },
            });
            Assert.Equal(ServiceResultStatus.Created, created.Status);
            Assert.Equal(new decimal[] { 9, 21, 21, 9, 21 }, created.Value.Lines.Select(l => l.VatPercentage)); // laatste: de standaard van de instellingen
        }
    }

    [Fact]
    public async Task The_Check_Lists_Only_What_Has_No_Rate()
    {
        await using (var db = _factory.CreateContext())
        {
            db.AddRange(
                new ProductVariant { Product = new Product { Name = "Gember" }, Name = "", Origin = "China", OriginCountryCode = "CN", TradeClass = "I", DefaultPrice = 0m },
                new ProductVariant { Product = new Product { Name = "Oud" }, Name = "", Origin = "", TradeClass = "I", DefaultPrice = 0m, VatPercentage = null },
                new CostArticle { Name = "Oude dienst", NameKey = "oude dienst", Kind = CostArticleRules.KindCost, CreatedBy = "test" });
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            var check = await new ArticleService(db).VatCheckAsync();
            Assert.Equal(new[] { ("Article", "Oud"), ("Cost", "Oude dienst") }, check.Select(i => (i.Kind, i.Description)));
        }
    }

    [Fact]
    public void The_Migration_Gives_Products_9_And_Services_21_And_Leaves_Packaging_Alone()
    {
        Assert.Contains("UPDATE \"ProductVariants\" SET \"VatPercentage\" = 9 WHERE \"VatPercentage\" IS NULL", AddArticleVat.FillSql);
        Assert.Contains("\"Kind\" = 'Cost' AND \"VatPercentage\" IS NULL", AddArticleVat.FillSql);
        Assert.DoesNotContain("Packaging", AddArticleVat.FillSql);
    }
}
