using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Inkoopfactuur invoeren: leveranciersdefaults op het document, geldberekening, optionele velden volgens
// bedrijfsinstellingen, idempotentie, en nooit een voorraadwijziging.
public class PurchaseInvoiceServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private int _vendorId;
    private int _variantId;

    public void Dispose() => _factory.Dispose();

    private async Task SeedAsync()
    {
        await using var db = _factory.CreateContext();
        var vendor = new Vendor { Name = "The Greenery", VATNumber = "NL001", DefaultCurrency = "EUR", PaymentTermsDays = 30 };
        var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.5m };
        db.AddRange(vendor, variant, new Inventory { ProductVariant = variant, Location = "A1", Quantity = 10 });
        await db.SaveChangesAsync();
        _vendorId = vendor.Id;
        _variantId = variant.Id;
    }

    private CreatePurchaseInvoiceDto Request(Guid? requestId = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        VendorId = _vendorId,
        InvoiceNumber = "GR-2026-001",
        InvoiceDate = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc),
        Lines = new List<CreatePurchaseInvoiceLineDto>
        {
            new() { ProductVariantId = _variantId, Quantity = 80m, Unit = "box", UnitPrice = 12.5m },
            new() { Description = "Transport", Quantity = 1m, UnitPrice = 150m, VatPercentage = 21m },
            new() { Description = "Pallets statiegeld", Quantity = 3m, UnitPrice = 10m, VatPercentage = 0m, DiscountPercentage = 10m },
        }
    };

    private async Task<ServiceResult<PurchaseInvoiceDto>> CreateAsync(CreatePurchaseInvoiceDto request)
    {
        await using var db = _factory.CreateContext();
        return await new PurchaseInvoiceService(db).CreateAsync(request);
    }

    private async Task<ServiceResult<PurchaseInvoiceSettingsDto>> UpdateSettingsAsync(UpdatePurchaseInvoiceSettingsDto request)
    {
        await using var db = _factory.CreateContext();
        return await new PurchaseInvoiceService(db).UpdateSettingsAsync(request);
    }

    private async Task<int> InvoiceCountAsync()
    {
        await using var db = _factory.CreateContext();
        return await db.IncomingInvoices.CountAsync();
    }

    [Fact]
    public async Task Create_Stores_Vendor_Defaults_On_The_Document_And_Computes_Money_Per_Line()
    {
        await SeedAsync();
        var result = await CreateAsync(Request());

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var dto = result.Value;
        Assert.Equal("Draft", dto.Status);
        Assert.Equal(30, dto.PaymentTermsDays);
        Assert.Equal("EUR", dto.Currency);
        Assert.Equal(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc), dto.DueDate);
        // 80 × 12,50 = 1000,00 (btw 21 % standaard = 210,00); 150,00 (31,50); 3 × 10 − 10 % = 27,00 (0 %).
        Assert.Equal(new[] { 1000m, 150m, 27m }, dto.Lines.Select(l => l.LineTotal).ToArray());
        Assert.Equal(new[] { 210m, 31.5m, 0m }, dto.Lines.Select(l => l.VatAmount).ToArray());
        Assert.Equal(1177m, dto.SubTotal);
        Assert.Equal(241.5m, dto.VatAmount);
        Assert.Equal(1418.5m, dto.TotalAmount);
        Assert.Equal("Avocado · Hass", dto.Lines[0].Description);
        Assert.Equal(_variantId, dto.Lines[0].ProductVariantId);
        Assert.Equal("box", dto.Lines[0].Unit);
    }

    [Fact]
    public async Task Document_Values_Override_Vendor_Defaults_And_Later_Vendor_Changes_Do_Not_Touch_The_Invoice()
    {
        await SeedAsync();
        var request = Request();
        request.PaymentTermsDays = 60;
        request.Currency = "usd";
        request.DueDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var created = (await CreateAsync(request)).Value;
        Assert.Equal((60, "USD", new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)), (created.PaymentTermsDays!.Value, created.Currency, created.DueDate!.Value));

        await using (var db = _factory.CreateContext())
        {
            var vendor = await db.Vendors.SingleAsync();
            vendor.PaymentTermsDays = 14;
            vendor.DefaultCurrency = "GBP";
            await db.SaveChangesAsync();
        }
        await using var verify = _factory.CreateContext();
        var stored = await new PurchaseInvoiceService(verify).GetAsync(created.Id);
        Assert.Equal((60, "USD"), (stored.Value.PaymentTermsDays!.Value, stored.Value.Currency));
    }

    // Regressie uit de browsersmoke: de client stuurt "2026-10-21" (Kind=Unspecified); opslag moet als UTC-middernacht slagen.
    [Fact]
    public async Task Dates_Without_Time_Zone_Are_Stored_As_Utc_Midnight()
    {
        await SeedAsync();
        var request = Request();
        request.InvoiceDate = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Unspecified);
        request.ReceivedDate = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Unspecified);
        var created = (await CreateAsync(request)).Value;
        // SQLite geeft Kind niet terug; de kalenderdagen moeten kloppen en de echte PostgreSQL-opslag is in de browsersmoke
        // bewezen (vóór deze normalisatie gaf Npgsql een 500 op Kind=Unspecified).
        Assert.Equal(new DateTime(2026, 10, 21), created.InvoiceDate!.Value.Date);
        Assert.Equal(new DateTime(2026, 11, 20), created.DueDate!.Value.Date);
        Assert.Equal(new DateTime(2026, 10, 22), created.ReceivedDate!.Value.Date);
    }

    [Fact]
    public async Task Identical_Retry_Returns_The_Same_Invoice_And_A_Different_Payload_Conflicts()
    {
        await SeedAsync();
        var requestId = Guid.NewGuid();
        var first = await CreateAsync(Request(requestId));
        var retry = await CreateAsync(Request(requestId));
        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(first.Value.Id, retry.Value.Id);

        var changed = Request(requestId);
        changed.Lines[0].Quantity = 81m;
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(changed)).Status);
        Assert.Equal(1, await InvoiceCountAsync());
    }

    [Fact]
    public async Task Saving_An_Invoice_Never_Changes_Inventory_Pallets_Or_Content()
    {
        await SeedAsync();
        await CreateAsync(Request());
        await using var db = _factory.CreateContext();
        Assert.Equal(10, (await db.Set<Inventory>().SingleAsync()).Quantity);
        Assert.Equal(0, await db.Pallets.CountAsync());
        Assert.Equal(0, await db.PalletContentLines.CountAsync());
        Assert.Equal(0, await db.StockMovements.CountAsync());
    }

    [Fact]
    public async Task Disabled_Optional_Fields_Are_Ignored_On_Entry_While_Enabled_Ones_Are_Stored()
    {
        await SeedAsync();
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateSettingsAsync(new UpdatePurchaseInvoiceSettingsDto { OptionalFields = new List<string> { "remarks", "receivedDate" } })).Status);
        var request = Request();
        request.Remarks = "Levering vrijdag";
        request.ReceivedDate = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
        request.ContainerNumber = "MSCU1234567"; // uitgeschakeld: genegeerd, geen fout
        request.GrossWeightKg = -5m;              // uitgeschakeld: ook geen validatie
        var created = (await CreateAsync(request)).Value;
        Assert.Equal("Levering vrijdag", created.Remarks);
        Assert.Equal(request.ReceivedDate, created.ReceivedDate);
        Assert.Null(created.ContainerNumber);
        Assert.Null(created.GrossWeightKg);
    }

    [Fact]
    public async Task Historical_Values_Survive_When_A_Field_Is_Disabled_Later()
    {
        await SeedAsync();
        await UpdateSettingsAsync(new UpdatePurchaseInvoiceSettingsDto { OptionalFields = new List<string> { "containerNumber" } });
        var request = Request();
        request.ContainerNumber = "MSCU1234567";
        var created = (await CreateAsync(request)).Value;
        await UpdateSettingsAsync(new UpdatePurchaseInvoiceSettingsDto { OptionalFields = new List<string>() });

        await using var db = _factory.CreateContext();
        Assert.Equal("MSCU1234567", (await new PurchaseInvoiceService(db).GetAsync(created.Id)).Value.ContainerNumber);
        Assert.Empty((await new PurchaseInvoiceService(db).GetSettingsAsync()).Value.OptionalFields);
    }

    [Theory]
    [InlineData("vendor-missing", "vendorId")]
    [InlineData("vendor-unknown", "vendorId")]
    [InlineData("number-blank", "invoiceNumber")]
    [InlineData("date-missing", "invoiceDate")]
    [InlineData("terms-negative", "paymentTermsDays")]
    [InlineData("currency-invalid", "currency")]
    [InlineData("due-before-invoice", "dueDate")]
    [InlineData("no-lines", "lines")]
    [InlineData("line-no-description", "lines[1].description")]
    [InlineData("line-quantity-zero", "lines[0].quantity")]
    [InlineData("line-price-three-decimals", "lines[0].unitPrice")]
    [InlineData("line-discount-over-100", "lines[2].discountPercentage")]
    [InlineData("line-vat-negative", "lines[1].vatPercentage")]
    [InlineData("line-unit-unknown", "lines[0].unit")]
    [InlineData("line-variant-unknown", "lines[0].productVariantId")]
    [InlineData("request-id-missing", "requestId")]
    public async Task Invalid_Input_Is_Rejected_And_Nothing_Is_Stored(string invalidCase, string expectedErrorKey)
    {
        await SeedAsync();
        var request = Request();
        switch (invalidCase)
        {
            case "vendor-missing": request.VendorId = null; break;
            case "vendor-unknown": request.VendorId = 999; break;
            case "number-blank": request.InvoiceNumber = "  "; break;
            case "date-missing": request.InvoiceDate = null; break;
            case "terms-negative": request.PaymentTermsDays = -1; break;
            case "currency-invalid": request.Currency = "EURO"; break;
            case "due-before-invoice": request.DueDate = request.InvoiceDate!.Value.AddDays(-1); break;
            case "no-lines": request.Lines.Clear(); break;
            case "line-no-description": request.Lines[1].Description = " "; break;
            case "line-quantity-zero": request.Lines[0].Quantity = 0m; break;
            case "line-price-three-decimals": request.Lines[0].UnitPrice = 12.505m; break;
            case "line-discount-over-100": request.Lines[2].DiscountPercentage = 101m; break;
            case "line-vat-negative": request.Lines[1].VatPercentage = -1m; break;
            case "line-unit-unknown": request.Lines[0].Unit = "pallet"; break;
            case "line-variant-unknown": request.Lines[0].ProductVariantId = 999; break;
            case "request-id-missing": request.RequestId = null; break;
        }
        var result = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(expectedErrorKey, result.Errors.Keys);
        Assert.Equal(0, await InvoiceCountAsync());
    }

    [Fact]
    public async Task Settings_Validate_And_Persist_Defaults()
    {
        await SeedAsync();
        var invalid = await UpdateSettingsAsync(new UpdatePurchaseInvoiceSettingsDto { OptionalFields = new List<string> { "palletContents" }, DefaultVatPercentage = 101m, DefaultCurrency = "euro" });
        Assert.Equal(ServiceResultStatus.Validation, invalid.Status);
        Assert.Equal(new[] { "defaultCurrency", "defaultVatPercentage", "optionalFields" }, invalid.Errors.Keys.OrderBy(k => k).ToArray());

        var ok = await UpdateSettingsAsync(new UpdatePurchaseInvoiceSettingsDto { DefaultVatPercentage = 9m, DefaultPaymentTermsDays = 14, DefaultCurrency = "eur" });
        Assert.Equal(ServiceResultStatus.Ok, ok.Status);
        Assert.Equal((9m, 14, "EUR"), (ok.Value.DefaultVatPercentage, ok.Value.DefaultPaymentTermsDays, ok.Value.DefaultCurrency));
        Assert.Equal(PurchaseInvoiceRules.OptionalFields, ok.Value.AllOptionalFields);

        // Nieuwe regels zonder btw krijgen het bedrijfsvoorstel (9 %), geen blinde afleiding uit de leverancier.
        var request = Request();
        request.Lines = new List<CreatePurchaseInvoiceLineDto> { new() { Description = "Kisten", Quantity = 2m, UnitPrice = 5m } };
        Assert.Equal(9m, (await CreateAsync(request)).Value.Lines[0].VatPercentage);
    }
}

public class InvoiceMathTests
{
    [Theory]
    [InlineData(80, 12.5, 0, 1000.00, 21, 210.00)]
    [InlineData(3, 10, 10, 27.00, 0, 0)]
    [InlineData(1, 0.335, 0, 0.34, 21, 0.07)]
    [InlineData(7, 1.115, 12.5, 6.83, 9, 0.61)]
    [InlineData(2.5, 3.333, 0, 8.33, 21, 1.75)]
    public void Line_Amounts_Round_Half_Up_To_Two_Decimals(double quantity, double price, double discount, double expectedNet, double vat, double expectedVat)
    {
        var net = InvoiceMath.LineNet((decimal)quantity, (decimal)price, (decimal)discount);
        Assert.Equal((decimal)expectedNet, net);
        Assert.Equal((decimal)expectedVat, InvoiceMath.LineVat(net, (decimal)vat));
    }

    [Fact]
    public void Totals_Sum_The_Rounded_Lines()
    {
        var totals = InvoiceMath.Totals(new[] { (0.34m, 0.07m), (0.34m, 0.07m), (0.34m, 0.07m) });
        Assert.Equal((1.02m, 0.21m, 1.23m), totals);
    }
}
