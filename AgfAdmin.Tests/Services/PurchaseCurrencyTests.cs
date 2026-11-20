using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Valuta-ronde (november 2026), de basis: ECB-koersen één keer per dag, de lijst valuta's voor inkoop onder Instellingen, en de kolommen
// voor valuta en koers op de inkooporder (bestaande orders: euro, koers 1, bron NotApplicable). Nooit het echte netwerk.
public class PurchaseCurrencyTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private static readonly DateOnly Today = new(2026, 11, 20);

    public PurchaseCurrencyTests() => _factory.Calendar = new FixedBusinessCalendar(Today);

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

    [Fact]
    public async Task The_Ecb_Is_Asked_Once_Per_Currency_And_Day_A_New_Day_Asks_Again_And_An_Outage_Is_Not_Remembered()
    {
        var calendar = new FixedBusinessCalendar(Today);
        var cache = new ExchangeRateDayCache();
        _rates.Quotes["USD"] = Quote("USD", 1.0842m, Today);
        var cached = new CachedExchangeRateSource(_rates, cache, calendar);
        Assert.Equal(1.0842m, (await cached.LatestAsync("usd", Today, default)).UnitsPerEuro);
        Assert.Equal(1.0842m, (await cached.LatestAsync("USD", Today, default)).UnitsPerEuro);
        Assert.Null(await cached.LatestAsync("AED", Today, default));
        Assert.Null(await cached.LatestAsync("AED", Today, default));
        Assert.Equal(2, _rates.Asked.Count);

        var nextDay = new CachedExchangeRateSource(_rates, cache, new FixedBusinessCalendar(Today.AddDays(1)));
        await nextDay.LatestAsync("USD", Today, default);
        Assert.Equal(3, _rates.Asked.Count);

        _rates.Down = true;
        var down = new CachedExchangeRateSource(_rates, cache, new FixedBusinessCalendar(Today.AddDays(2)));
        await Assert.ThrowsAsync<ExchangeRateUnavailableException>(() => down.LatestAsync("USD", Today, default));
        _rates.Down = false;
        Assert.Equal(1.0842m, (await down.LatestAsync("USD", Today, default)).UnitsPerEuro);
        Assert.Equal(5, _rates.Asked.Count);
    }

    [Fact]
    public async Task The_Currency_List_Starts_With_Euro_Can_Be_Extended_And_Says_Which_Have_An_Ecb_Rate()
    {
        await using var db = _factory.CreateContext();
        var service = new PurchaseCurrencyService(db);
        var list = await service.GetAsync();
        Assert.Equal(new[] { "EUR", "USD", "GBP", "CHF", "TRY", "ZAR", "CAD", "NOK" }, list.Currencies.Select(c => c.Code));
        Assert.True(list.Currencies.Single(c => c.Code == "TRY").HasEcbRate);
        var saved = await service.UpdateAsync(new UpdatePurchaseCurrenciesDto { Codes = new() { "usd", "aed", "USD", " gbp " } });
        Assert.Equal(new[] { "EUR", "USD", "AED", "GBP" }, saved.Value.Currencies.Select(c => c.Code));
        Assert.False(saved.Value.Currencies.Single(c => c.Code == "AED").HasEcbRate);
        Assert.Equal(ServiceResultStatus.Validation, (await service.UpdateAsync(new UpdatePurchaseCurrenciesDto { Codes = new() { "US$" } })).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await service.UpdateAsync(null)).Status);
        var onlyEuro = await service.UpdateAsync(new UpdatePurchaseCurrenciesDto { Codes = new() });
        Assert.Equal(new[] { "EUR" }, onlyEuro.Value.Currencies.Select(c => c.Code));
        Assert.Equal(new[] { "EUR" }, (await service.GetAsync()).Currencies.Select(c => c.Code));
    }

    [Fact]
    public async Task The_Database_Refuses_A_Rate_That_Does_Not_Fit_The_Currency()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var created = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        await using var db = _factory.CreateContext();
        var order = await db.SupplyCommitments.SingleAsync(c => c.Id == created.Id);
        // Bestaande en nieuwe orders zonder valuta: euro, koers 1, bron NotApplicable (de standaard van de kolommen, ook voor de migratie).
        Assert.Equal(("EUR", 1m, (DateOnly?)null, "NotApplicable"), (order.Currency, order.ExchangeRate, order.ExchangeRateDate, order.ExchangeRateSource));
        order.Currency = "USD";
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
