using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Partij P2, kostenartikelen en directe inkoopfactuur: de standaardsoorten (Handling, Transport, QC, Zeevracht, Afval, Overig), zelf een
// kostenartikel maken (dubbelcontrole op de naam), een factuur van derden zonder inkooporder en zonder ontvangst met kostenregels, en
// hetzelfde factuurnummer van dezelfde leverancier nooit twee keer. Een Dienst staat nooit in Beschikbaarheid of "Maat en klasse opschonen".
public sealed class DirectPurchaseInvoiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private int _vendorId;
    private int _otherVendorId;
    private Dictionary<string, int> _costs;

    public void Dispose() => _factory.Dispose();

    private async Task SeedAsync()
    {
        _costs = await CostArticleTestData.SeedStandardAsync(_factory);
        await using var db = _factory.CreateContext();
        var vendor = new Vendor { Name = "Berencourt", PaymentTermsDays = 30 };
        var other = new Vendor { Name = "Maersk", PaymentTermsDays = 30 };
        db.AddRange(vendor, other);
        await db.SaveChangesAsync();
        (_vendorId, _otherVendorId) = (vendor.Id, other.Id);
    }

    private CreatePurchaseInvoiceDto Handling(string number = "BC-2026-118", int? vendorId = null, Guid? requestId = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        VendorId = vendorId ?? _vendorId,
        InvoiceNumber = number,
        InvoiceDate = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc),
        Lines = new List<CreatePurchaseInvoiceLineDto>
        {
            new() { CostArticleId = _costs["Handling"], Quantity = 1m, UnitPrice = 5000m, VatPercentage = 21m },
            new() { Description = "Elektra loods oktober", Quantity = 1m, UnitPrice = 312.40m, VatPercentage = 21m },
        }
    };

    private async Task<ServiceResult<PurchaseInvoiceDto>> CreateAsync(CreatePurchaseInvoiceDto request)
    {
        await using var db = _factory.CreateContext();
        return await new PurchaseInvoiceService(db).CreateAsync(request);
    }

    [Fact]
    public async Task The_Standard_Cost_Articles_Exist_In_Order_And_Own_Ones_Can_Be_Added_Once()
    {
        await SeedAsync();
        await using var db = _factory.CreateContext();
        var service = new CostArticleService(db);
        Assert.Equal(CostArticleRules.StandardNames, (await service.ListAsync(includeInactive: false)).Select(c => c.Name));

        var requestId = Guid.NewGuid();
        var created = await service.CreateAsync(new CreateCostArticleDto { RequestId = requestId, Name = "  Auto huur " });
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal(("Auto huur", false, true), (created.Value.Name, created.Value.IsStandard, created.Value.IsActive));
        Assert.Equal(ServiceResultStatus.Ok, (await service.CreateAsync(new CreateCostArticleDto { RequestId = requestId, Name = "auto  huur" })).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await service.CreateAsync(new CreateCostArticleDto { RequestId = Guid.NewGuid(), Name = "AUTO HUUR" })).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await service.CreateAsync(new CreateCostArticleDto { RequestId = Guid.NewGuid(), Name = "zeevracht" })).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await service.CreateAsync(new CreateCostArticleDto { RequestId = Guid.NewGuid(), Name = "  " })).Status);
        Assert.Equal("Standaard", (await db.CostArticles.SingleAsync(c => c.Name == "QC")).CreatedBy);
    }

    [Fact]
    public async Task A_Direct_Invoice_Without_Order_Or_Receipt_Has_Cost_Lines_And_Never_Touches_Stock()
    {
        await SeedAsync();
        var result = await CreateAsync(Handling());
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var lines = result.Value.Lines;
        Assert.Equal(("Handling", _costs["Handling"], 5000m, 1050m), (lines[0].Description, lines[0].CostArticleId, lines[0].LineTotal, lines[0].VatAmount));
        Assert.Equal(("Handling", (int?)null, (string)null), (lines[0].CostArticleName, lines[1].CostArticleId, lines[1].CostArticleName));
        // 5.000 + 312,40 excl. btw; btw 1.050 + 65,60 (afgerond per regel).
        Assert.Equal((5312.40m, 6428.00m), (result.Value.SubTotal, result.Value.TotalAmount));

        await using var db = _factory.CreateContext();
        Assert.Equal(0, await db.SupplyCommitments.CountAsync());
        Assert.Equal(0, await db.PalletContentLines.CountAsync());
        Assert.Equal(0, await db.StockMovements.CountAsync());
        Assert.Equal(0, await db.SupplyCommitmentInvoiceRefs.CountAsync());
    }

    [Fact]
    public async Task The_Same_Invoice_Number_Of_The_Same_Supplier_Is_Refused_And_Names_The_Existing_Invoice()
    {
        await SeedAsync();
        var first = (await CreateAsync(Handling("BC-2026-118"))).Value;
        var duplicate = await CreateAsync(Handling(" bc-2026-118 "));
        Assert.Equal(ServiceResultStatus.Conflict, duplicate.Status);
        Assert.Equal((first.Id, "INVOICE_EXISTS"), (((DuplicatePurchaseInvoiceDto)duplicate.ConflictBody).InvoiceId, ((DuplicatePurchaseInvoiceDto)duplicate.ConflictBody).Code));
        // Een andere leverancier mag hetzelfde nummer gebruiken; een exacte herhaling is geen dubbel.
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(Handling("BC-2026-118", _otherVendorId))).Status);
        var replay = Handling("BC-2026-119");
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(replay)).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(replay)).Status);
        await using var db = _factory.CreateContext();
        Assert.Equal(3, await db.IncomingInvoices.CountAsync());
    }

    [Fact]
    public async Task A_Line_Has_An_Article_Or_A_Cost_Article_And_An_Inactive_Cost_Article_Is_Refused()
    {
        await SeedAsync();
        int variantId;
        await using (var db = _factory.CreateContext())
        {
            var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass", Origin = "Peru", Packaging = "4kg", DefaultPrice = 0m };
            db.Add(variant);
            await db.SaveChangesAsync();
            variantId = variant.Id;
            Assert.Equal(ServiceResultStatus.Ok, (await new CostArticleService(db).SetActiveAsync(_costs["Afval"], new SetCostArticleActiveDto { IsActive = false })).Status);
        }
        var both = Handling();
        both.Lines[0].ProductVariantId = variantId;
        Assert.True((await CreateAsync(both)).Errors.ContainsKey("lines[0].costArticleId"));
        var inactive = Handling();
        inactive.Lines[0].CostArticleId = _costs["Afval"];
        Assert.True((await CreateAsync(inactive)).Errors.ContainsKey("lines[0].costArticleId"));
        var unknown = Handling();
        unknown.Lines[0].CostArticleId = 999;
        Assert.True((await CreateAsync(unknown)).Errors.ContainsKey("lines[0].costArticleId"));

        // Inactief blijft in de lijst met inactieve, niet in de keuzelijst.
        await using var read = _factory.CreateContext();
        Assert.DoesNotContain(await new CostArticleService(read).ListAsync(includeInactive: false), c => c.Name == "Afval");
        Assert.Contains(await new CostArticleService(read).ListAsync(includeInactive: true), c => c.Name == "Afval" && !c.IsActive);
    }

    [Fact]
    public async Task A_Service_Never_Shows_In_Availability_Or_In_The_Size_And_Class_Cleanup()
    {
        await SeedAsync();
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(Handling())).Status);
        await using var db = _factory.CreateContext();
        var names = CostArticleRules.StandardNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Empty(await db.ProductVariants.ToListAsync());
        Assert.DoesNotContain(await new ArticleService(db).SizeClassCleanupAsync(), i => names.Contains(i.Description));
        Assert.DoesNotContain(await new ArticleService(db).ListAsync(), a => names.Contains(a.ProductName) || names.Contains(a.VariantName));
    }
}
