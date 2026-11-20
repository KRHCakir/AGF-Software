using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Valuta-ronde: de inkoopfactuur legt haar koers vast bij opslaan: de ECB-koers van de factuurdatum (aanpasbaar: Manual), niet automatisch
// die van de order. Een factuur bij een order is in de valuta van die order; het vergelijken van factuur en bon gebeurt in die valuta.
// Verschillen de koersen, dan blijven beide staan (een koersverschil is voor de boekhouder, geen fout). Nooit het echte netwerk.
public class PurchaseInvoiceCurrencyTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly DateTime InvoiceDate = new(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc);
    private PurchaseOrderSeed _seed;

    public PurchaseInvoiceCurrencyTests() => _factory.Calendar = new FixedBusinessCalendar(Today);

    public void Dispose() => _factory.Dispose();

    private sealed class FakeRates : IExchangeRateSource
    {
        public Dictionary<string, ExchangeRateQuote> Quotes { get; } = new();
        public bool Down { get; set; }
        public int Calls { get; private set; }

        public Task<ExchangeRateQuote> LatestAsync(string currency, DateOnly day, CancellationToken cancellationToken)
        {
            Calls++;
            if (Down) throw new ExchangeRateUnavailableException("The ECB exchange rates could not be reached.");
            return Task.FromResult(Quotes.GetValueOrDefault(currency));
        }
    }

    private readonly FakeRates _rates = new();

    private static ExchangeRateQuote Quote(string currency, decimal rate, DateOnly date) => new(currency, rate, date, ExchangeRateRules.SourceEcb);

    private async Task<SupplyCommitmentDto> OrderAsync(string currency = null, decimal? rate = null, string source = null)
    {
        _seed ??= await PurchaseOrderTestData.SeedAsync(_factory);
        var request = PurchaseOrderTestData.OrderRequest(_seed, quantity: 100m);
        request.Lines[0].ExpectedNetWeightKg = null;
        request.Lines[0].UnitPrice = 10m;
        (request.Currency, request.ExchangeRate, request.ExchangeRateSource) = (currency, rate, source);
        await using var db = _factory.CreateContext();
        var created = await new SupplyCommitmentService(db, null, _rates).CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        return created.Value;
    }

    private async Task<CreatePurchaseInvoiceDto> InvoiceAsync(string currency, decimal? rate = null, string source = null, int? orderLineId = null, decimal price = 10m)
    {
        _seed ??= await PurchaseOrderTestData.SeedAsync(_factory);
        return new CreatePurchaseInvoiceDto
        {
            RequestId = Guid.NewGuid(), VendorId = _seed.VendorId, InvoiceNumber = $"F-{Guid.NewGuid():N}"[..10], InvoiceDate = InvoiceDate, Currency = currency,
            ExchangeRate = rate, ExchangeRateSource = source,
            Lines = new() { new CreatePurchaseInvoiceLineDto { ProductVariantId = _seed.HassId, Description = "Avocado · Hass", Quantity = 100m, Unit = "box", UnitPrice = price, VatPercentage = 0m, SupplyCommitmentLineId = orderLineId } },
        };
    }

    private async Task<ServiceResult<PurchaseInvoiceDto>> SaveAsync(CreatePurchaseInvoiceDto request)
    {
        await using var db = _factory.CreateContext();
        return await new PurchaseInvoiceService(db, null, _rates).CreateAsync(request);
    }

    [Fact]
    public async Task A_Euro_Invoice_Has_Rate_1_And_Its_Euro_Total_Is_Its_Total()
    {
        var saved = await SaveAsync(await InvoiceAsync("EUR"));
        Assert.Equal(ServiceResultStatus.Created, saved.Status);
        Assert.Equal((1m, (DateTime?)null, "NotApplicable", 1000m, (decimal?)1000m), (saved.Value.ExchangeRate!.Value, saved.Value.ExchangeRateDate, saved.Value.ExchangeRateSource, saved.Value.TotalAmount, saved.Value.TotalAmountEur));
        Assert.Equal(0, _rates.Calls);
    }

    [Fact]
    public async Task A_Dollar_Invoice_Fixes_The_Ecb_Rate_Of_Its_Date_When_Saved_And_Keeps_It()
    {
        _rates.Quotes["USD"] = Quote("USD", 1.25m, new DateOnly(2026, 11, 19));
        var saved = await SaveAsync(await InvoiceAsync("USD"));
        Assert.Equal(ServiceResultStatus.Created, saved.Status);
        Assert.Equal((1.25m, "ECB", (decimal?)800m), (saved.Value.ExchangeRate!.Value, saved.Value.ExchangeRateSource, saved.Value.TotalAmountEur));
        Assert.Equal(new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc), saved.Value.ExchangeRateDate);

        // Een nieuwere ECB-koers verandert de opgeslagen factuur niet, ook niet bij "Kosten verdelen" openen.
        _rates.Quotes["USD"] = Quote("USD", 1.10m, Today);
        await using var db = _factory.CreateContext();
        var opened = await new InvoiceExchangeRateService(db, _rates, _factory.Calendar).RefreshAsync(saved.Value.Id);
        Assert.Equal(1.25m, opened.Value.ExchangeRate);
        Assert.Equal(1, _rates.Calls);
    }

    [Fact]
    public async Task A_Manual_Rate_Is_Kept_And_An_Unreachable_Ecb_Asks_For_One()
    {
        var manual = await SaveAsync(await InvoiceAsync("USD", 1.2m, "Manual"));
        Assert.Equal((1.2m, "Manual", (DateTime?)null, (decimal?)833.33m), (manual.Value.ExchangeRate!.Value, manual.Value.ExchangeRateSource, manual.Value.ExchangeRateDate, manual.Value.TotalAmountEur));

        _rates.Down = true;
        var down = await SaveAsync(await InvoiceAsync("USD"));
        Assert.Equal(ServiceResultStatus.Validation, down.Status);
        Assert.Contains("Vul de koers zelf in", down.Errors["exchangeRate"][0]);
    }

    [Fact]
    public async Task The_Euro_Totals_Are_The_Sum_Of_The_Lines_Converted_And_Rounded_One_By_One()
    {
        var request = await InvoiceAsync("USD", 1.2m, "Manual");
        var line = request.Lines[0];
        line.Quantity = 1m;
        line.UnitPrice = 1m;
        request.Lines = new() { line, Clone(line), Clone(line) };

        var saved = await SaveAsync(request);
        Assert.Equal(ServiceResultStatus.Created, saved.Status);
        // 3 x (1,00 / 1,2 = 0,83): per regel afgerond is 2,49, niet 3,00 / 1,2 = 2,50 (gelijk aan de kostenverdeling).
        Assert.Equal((3m, (decimal?)2.49m, (decimal?)2.49m), (saved.Value.TotalAmount, saved.Value.SubTotalEur, saved.Value.TotalAmountEur));
    }

    private static CreatePurchaseInvoiceLineDto Clone(CreatePurchaseInvoiceLineDto l) => new()
    {
        ProductVariantId = l.ProductVariantId, Description = l.Description, Quantity = l.Quantity, Unit = l.Unit, UnitPrice = l.UnitPrice,
        VatPercentage = l.VatPercentage, SupplyCommitmentLineId = l.SupplyCommitmentLineId
    };

    [Fact]
    public async Task An_Invoice_Links_Only_To_An_Order_In_The_Same_Currency_And_May_Have_Another_Rate()
    {
        _rates.Quotes["USD"] = Quote("USD", 1.25m, new DateOnly(2026, 11, 19));
        var order = await OrderAsync("USD", 1.20m, "Manual");
        var lineId = order.Lines[0].Id;

        // Een factuur in euro bij een order in dollars: geweigerd, nooit stil omrekenen.
        var euro = await SaveAsync(await InvoiceAsync("EUR", orderLineId: lineId));
        Assert.Equal(ServiceResultStatus.Validation, euro.Status);
        Assert.Contains("dezelfde valuta", euro.Errors["lines[0].supplyCommitmentLineId"][0]);

        // In dollars: gekoppeld; de factuur heeft de ECB-koers van haar datum (1,25), de order houdt haar koers (1,20).
        var request = await InvoiceAsync("USD", orderLineId: lineId, price: 10.5m, source: "ECB");
        request.OrderPriceDecision = "RecordDifference";
        var invoice = await SaveAsync(request);
        Assert.True(invoice.Status == ServiceResultStatus.Created, string.Join(" | ", invoice.Errors.SelectMany(e => e.Value)));
        Assert.Equal(1.25m, invoice.Value.ExchangeRate);
        await using var db = _factory.CreateContext();
        var detail = (await new PurchaseOrderService(db).GetAsync(order.Id)).Value;
        Assert.Equal((1.20m, "Manual"), (detail.ExchangeRate, detail.ExchangeRateSource));
        // Het verschil tussen factuur en bon staat in de valuta van de documenten: 100 × (10,50 − 10,00) = 50 USD.
        var linked = Assert.Single(detail.Invoices);
        Assert.Equal(("USD", 50m), (linked.Currency, linked.Difference));
        var line = await db.IncomingInvoiceLines.SingleAsync(l => l.IncomingInvoiceId == invoice.Value.Id);
        Assert.Equal(10m, line.OrderUnitPrice);
    }

    [Fact]
    public async Task The_Order_Candidates_For_An_Invoice_Carry_The_Currency_And_Rate_Of_The_Order()
    {
        var order = await OrderAsync("USD", 1.2m, "Manual");
        await using var db = _factory.CreateContext();
        var candidate = Assert.Single(await new PurchaseOrderService(db).InvoiceCandidatesAsync(_seed.VendorId, null, null));
        Assert.Equal((order.Id, "USD", 1.2m, "Manual"), (candidate.Id, candidate.Currency, candidate.ExchangeRate, candidate.ExchangeRateSource));
    }

    [Fact]
    public async Task The_Database_Refuses_A_Rate_Without_A_Source_And_A_Manual_Rate_With_A_Date()
    {
        var saved = await SaveAsync(await InvoiceAsync("USD", 1.2m, "Manual"));
        foreach (var spoil in new Action<AgfAdmin.Models.IncomingInvoice>[]
        {
            invoice => invoice.ExchangeRateSource = null,
            invoice => invoice.ExchangeRateDate = InvoiceDate,
        })
        {
            await using var db = _factory.CreateContext();
            var invoice = await db.IncomingInvoices.SingleAsync(i => i.Id == saved.Value.Id);
            spoil(invoice);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task A_Retry_With_The_Same_Rate_Is_The_Same_Invoice_And_Another_Rate_Is_Another_Request()
    {
        var request = await InvoiceAsync("USD", 1.2m, "Manual");
        var first = await SaveAsync(request);
        var retry = await SaveAsync(request);
        Assert.Equal((ServiceResultStatus.Ok, first.Value.Id), (retry.Status, retry.Value.Id));
        request.ExchangeRate = 1.3m;
        Assert.Equal(ServiceResultStatus.Conflict, (await SaveAsync(request)).Status);
    }
}
