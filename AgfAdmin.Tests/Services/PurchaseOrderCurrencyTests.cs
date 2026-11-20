using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Valuta-ronde (november 2026): valuta en koers op de inkooporder. De boekvaluta is euro; de order bewaart haar valuta, de prijzen in die
// valuta, de koers (eenheden valuta per 1 euro, zoals de ECB noteert), de datum en de bron (ECB, Manual of NotApplicable bij euro). De
// ECB-koers wordt één keer per dag opgehaald en bij opslaan vastgelegd; een latere koers verandert een bestaande order nooit. Nooit het
// echte netwerk: een nagebootste bron.
public class PurchaseOrderCurrencyTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private PurchaseOrderSeed _seed;

    // Vrijdag 20 november 2026.
    private static readonly DateOnly Today = new(2026, 11, 20);

    public PurchaseOrderCurrencyTests() => _factory.Calendar = new FixedBusinessCalendar(Today);

    public void Dispose() => _factory.Dispose();

    private sealed class FakeRates : IExchangeRateSource
    {
        public Dictionary<string, ExchangeRateQuote> Quotes { get; } = new();
        public bool Down { get; set; }
        public List<(string Currency, DateOnly Day)> Asked { get; } = new();

        public Task<ExchangeRateQuote> LatestAsync(string currency, DateOnly day, CancellationToken cancellationToken)
        {
            Asked.Add((currency, day));
            if (Down) throw new ExchangeRateUnavailableException("The ECB exchange rates could not be reached.");
            return Task.FromResult(Quotes.GetValueOrDefault(currency));
        }
    }

    private readonly FakeRates _rates = new();

    private static ExchangeRateQuote Quote(string currency, decimal rate, DateOnly date) => new(currency, rate, date, ExchangeRateRules.SourceEcb);

    private async Task<CreateSupplyCommitmentDto> RequestAsync(string currency = null, decimal? rate = null, string source = null, DateOnly? orderedOn = null, decimal price = 10m)
    {
        _seed ??= await PurchaseOrderTestData.SeedAsync(_factory);
        var request = PurchaseOrderTestData.OrderRequest(_seed);
        (request.Currency, request.ExchangeRate, request.ExchangeRateSource, request.OrderedOn) = (currency, rate, source, orderedOn);
        request.Lines[0].UnitPrice = price;
        return request;
    }

    private async Task<ServiceResult<SupplyCommitmentDto>> CreateAsync(CreateSupplyCommitmentDto request)
    {
        await using var db = _factory.CreateContext();
        return await new SupplyCommitmentService(db, null, _rates).CreateAsync(request);
    }

    private async Task<PurchaseOrderDetailDto> DetailAsync(int id)
    {
        await using var db = _factory.CreateContext();
        return (await new PurchaseOrderService(db).GetAsync(id)).Value;
    }

    private async Task<ServiceResult<PurchaseOrderDetailDto>> UpdateAsync(int id, UpdatePurchaseOrderDto request)
    {
        await using var db = _factory.CreateContext();
        return await new PurchaseOrderService(db, null, _rates).UpdateAsync(id, request);
    }

    [Fact]
    public async Task An_Order_In_Euro_Has_Rate_1_And_No_Source_And_Its_Euro_Total_Is_The_Total()
    {
        var created = await CreateAsync(await RequestAsync());
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal(("EUR", 1m, (DateOnly?)null, "NotApplicable"), (created.Value.Currency, created.Value.ExchangeRate, created.Value.ExchangeRateDate, created.Value.ExchangeRateSource));
        var detail = await DetailAsync(created.Value.Id);
        Assert.Equal((2210m, 2210m), (detail.Totals.Amount, detail.Totals.AmountEur));
        Assert.Empty(_rates.Asked);
    }

    [Fact]
    public async Task A_Dollar_Order_Gets_The_Ecb_Rate_Of_The_Order_Date_Fixed_By_The_Server_And_Rounds_The_Euro_Total_To_Cents()
    {
        _rates.Quotes["USD"] = Quote("USD", 1.0842m, Today);
        // Een meegestuurd getal telt niet bij bron ECB: de server legt zelf vast.
        var created = await CreateAsync(await RequestAsync("usd", 9.99m, "ECB", price: 3.6577m - 0.0077m));
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal(("USD", 1.0842m, (DateOnly?)Today, "ECB"), (created.Value.Currency, created.Value.ExchangeRate, created.Value.ExchangeRateDate, created.Value.ExchangeRateSource));
        Assert.Equal(("USD", Today), Assert.Single(_rates.Asked));
        var detail = await DetailAsync(created.Value.Id);
        // 221 × 3,65 = 806,65 USD; / 1,0842 = 744,0048 → 744,00 EUR.
        Assert.Equal((806.65m, 744.00m), (detail.Totals.Amount, detail.Totals.AmountEur));
        Assert.Equal(("USD", 1.0842m), (detail.Currency, detail.ExchangeRate));
    }

    [Fact]
    public async Task On_A_Sunday_The_Rate_Of_The_Last_Working_Day_Counts_And_A_Stale_Rate_Asks_For_A_Manual_One()
    {
        var sunday = new DateOnly(2026, 11, 15);
        _rates.Quotes["USD"] = Quote("USD", 1.07m, new DateOnly(2026, 11, 13));
        var weekend = await CreateAsync(await RequestAsync("USD", orderedOn: sunday));
        Assert.Equal((new DateOnly(2026, 11, 13), 1.07m), (weekend.Value.ExchangeRateDate!.Value, weekend.Value.ExchangeRate));
        Assert.Contains(("USD", sunday), _rates.Asked);

        // Een order voor morgen vraagt de koers van vandaag (er is nog geen koers voor morgen).
        _rates.Quotes["USD"] = Quote("USD", 1.08m, Today);
        var tomorrow = await CreateAsync(await RequestAsync("USD", orderedOn: Today.AddDays(1)));
        Assert.Equal(Today, tomorrow.Value.ExchangeRateDate);

        // Ouder dan tien dagen (zoals een oude indicatieve koers) is geen actuele koers: de melding vraagt om zelf in te vullen.
        _rates.Quotes["MAD"] = Quote("MAD", 10.8m, new DateOnly(2020, 3, 31));
        var stale = await CreateAsync(await RequestAsync("MAD"));
        Assert.Equal(ServiceResultStatus.Validation, stale.Status);
        Assert.Contains("Vul de koers zelf in", stale.Errors["exchangeRate"][0]);
    }

    [Fact]
    public async Task A_Currency_Without_An_Ecb_Rate_Or_An_Unreachable_Ecb_Works_With_A_Manual_Rate()
    {
        var none = await CreateAsync(await RequestAsync("AED"));
        Assert.Equal(ServiceResultStatus.Validation, none.Status);
        Assert.Contains("geen actuele koers voor AED", none.Errors["exchangeRate"][0]);

        var manual = await CreateAsync(await RequestAsync("AED", 3.98m, "Manual"));
        Assert.Equal(ServiceResultStatus.Created, manual.Status);
        Assert.Equal((3.98m, (DateOnly?)null, "Manual"), (manual.Value.ExchangeRate, manual.Value.ExchangeRateDate, manual.Value.ExchangeRateSource));

        _rates.Down = true;
        var down = await CreateAsync(await RequestAsync("USD"));
        Assert.Contains("niet bereikbaar", down.Errors["exchangeRate"][0]);
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(await RequestAsync("USD", 1.1m, "Manual"))).Status);

        // Een handmatige koers moet een getal boven nul zijn, hoogstens zes decimalen; een onbekende bron of valuta wordt geweigerd.
        Assert.Contains("exchangeRate", (await CreateAsync(await RequestAsync("USD", 0m, "Manual"))).Errors.Keys);
        Assert.Contains("exchangeRate", (await CreateAsync(await RequestAsync("USD", 1.1234567m, "Manual"))).Errors.Keys);
        Assert.Contains("exchangeRateSource", (await CreateAsync(await RequestAsync("USD", 1.1m, "Bank"))).Errors.Keys);
        Assert.Contains("currency", (await CreateAsync(await RequestAsync("US$"))).Errors.Keys);
    }

    [Fact]
    public async Task The_Fixed_Rate_Never_Follows_A_Later_Ecb_Rate_And_A_Retry_Keeps_It()
    {
        _rates.Quotes["USD"] = Quote("USD", 1.0842m, Today);
        var request = await RequestAsync("USD");
        var created = await CreateAsync(request);

        // De ECB publiceert een nieuwe koers: herhalen geeft dezelfde order met de koers van toen, en aanpassen zonder andere valuta ook.
        _rates.Quotes["USD"] = Quote("USD", 1.20m, Today);
        var retry = await CreateAsync(request);
        Assert.Equal((created.Value.Id, 1.0842m), (retry.Value.Id, retry.Value.ExchangeRate));
        var detail = await DetailAsync(created.Value.Id);
        var edit = PurchaseOrderEditHarness.EditOf(detail);
        (edit.Currency, edit.ExchangeRateSource) = ("USD", "ECB");
        edit.Note = "Andere notitie";
        var saved = await UpdateAsync(detail.Id, edit);
        Assert.Equal((1.0842m, Today), (saved.Value.ExchangeRate, saved.Value.ExchangeRateDate!.Value));
        Assert.DoesNotContain(saved.Value.Changes.Single().Fields, f => f.Field is "currency" or "exchangeRate");

        // Dezelfde RequestId met een andere valuta is een ander verzoek.
        var other = await RequestAsync("GBP");
        other.RequestId = request.RequestId;
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(other)).Status);
    }

    [Fact]
    public async Task An_Open_Order_Changes_Currency_And_Rate_With_Audit_A_Definitive_Order_Never()
    {
        var created = await CreateAsync(await RequestAsync());
        var detail = await DetailAsync(created.Value.Id);
        _rates.Quotes["USD"] = Quote("USD", 1.0842m, Today);
        var toDollar = PurchaseOrderEditHarness.EditOf(detail);
        (toDollar.Currency, toDollar.ExchangeRateSource) = ("USD", "ECB");
        var dollar = await UpdateAsync(detail.Id, toDollar);
        Assert.Equal(ServiceResultStatus.Ok, dollar.Status);
        var fields = dollar.Value.Changes.Single().Fields;
        Assert.Contains(fields, f => f.Field == "currency" && f.Old == "EUR" && f.New == "USD");
        Assert.Contains(fields, f => f.Field == "exchangeRate" && f.Old == null && f.New == "1 EUR = 1.0842 USD · ECB 2026-11-20");

        // De gebruiker past de koers aan: bron Manual, met een auditregel.
        var manual = PurchaseOrderEditHarness.EditOf(dollar.Value);
        (manual.Currency, manual.ExchangeRate, manual.ExchangeRateSource) = ("USD", 1.1m, "Manual");
        var adjusted = await UpdateAsync(detail.Id, manual);
        Assert.Equal((1.1m, "Manual", (DateOnly?)null), (adjusted.Value.ExchangeRate, adjusted.Value.ExchangeRateSource, adjusted.Value.ExchangeRateDate));
        Assert.Contains(adjusted.Value.Changes[^1].Fields, f => f.Field == "exchangeRate" && f.New == "1 EUR = 1.1 USD · Manual");

        // Weggelaten valuta (een oudere client): alles blijft zoals het was.
        var omitted = PurchaseOrderEditHarness.EditOf(adjusted.Value);
        omitted.Note = "Zonder valuta";
        var kept = (await UpdateAsync(detail.Id, omitted)).Value;
        Assert.Equal((1.1m, "Manual"), (kept.ExchangeRate, kept.ExchangeRateSource));

        // Definitief: aanpassen kan niet meer, en ook de guard (regel 29) laat valuta en koers niet veranderen.
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await new PurchaseOrderService(db).FinalizeAsync(detail.Id, new FinalizePurchaseOrderDto { RequestId = Guid.NewGuid(), ExpectedVersion = 3 })).Status);
        }
        await using (var db = _factory.CreateContext())
        {
            var order = await db.SupplyCommitments.SingleAsync(c => c.Id == detail.Id);
            order.ExchangeRate = 1.2m;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task The_List_Shows_Each_Order_In_Its_Currency_And_In_Euro()
    {
        _rates.Quotes["USD"] = Quote("USD", 1.25m, Today);
        await CreateAsync(await RequestAsync());
        await CreateAsync(await RequestAsync("USD", price: 5m));
        await using var db = _factory.CreateContext();
        var list = (await new PurchaseOrderService(db).ListAsync(null, "all", null, 1, 50)).Value.Items;
        Assert.Contains(list, o => o.Currency == "EUR" && o.Amount == 2210m && o.AmountEur == 2210m);
        // 221 × 5 = 1.105 USD / 1,25 = 884,00 EUR.
        Assert.Contains(list, o => o.Currency == "USD" && o.Amount == 1105m && o.AmountEur == 884m);
        Assert.Equal(3094m, list.Sum(o => o.AmountEur));
    }

}
