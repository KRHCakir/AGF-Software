using System.Net;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Koers bij een factuur in een andere valuta (feedback 8 november): de server haalt de ECB-koers van de factuurdatum op (nooit getypt),
// rekent het regelbedrag om naar euro als grens voor kosten op partijen, haalt hem opnieuw op zolang er niets verdeeld is en zet hem
// vast zodra er kosten op een partij staan (regel 26). Een factuur van 5.000 USD met koers 1,1165 is 4.478,28 EUR.
public sealed class ExchangeRateTests : IDisposable
{
    private static readonly DateOnly InvoiceDay = new(2026, 11, 5);
    private readonly SalesBeforeArrivalScenario _s = new();
    private readonly FakeRates _rates = new();
    private int _invoiceId;
    private int _lineId;
    private int _salesA;
    private int _salesB;

    public ExchangeRateTests() => _s.Factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 8));

    public void Dispose() => _s.Dispose();

    private sealed class FakeRates : IExchangeRateSource
    {
        public ExchangeRateQuote Next { get; set; }
        public bool Down { get; set; }
        public List<(string Currency, DateOnly Day)> Asked { get; } = new();

        public Task<ExchangeRateQuote> LatestAsync(string currency, DateOnly day, CancellationToken cancellationToken)
        {
            Asked.Add((currency, day));
            if (Down)
            {
                throw new ExchangeRateUnavailableException("The ECB exchange rates could not be reached.");
            }
            return Task.FromResult(Next);
        }
    }

    private static ExchangeRateQuote Usd(decimal rate, int day = 5) => new("USD", rate, new DateOnly(2026, 11, day), ExchangeRateRules.SourceEcb);

    private async Task ArrangeAsync(string currency = "USD")
    {
        await _s.ArrangeAsync();
        var costs = await CostArticleTestData.SeedStandardAsync(_s.Factory);
        var a = (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value;
        var b = (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value;
        (_salesA, _salesB) = (a.SalesPartijId.Value, b.SalesPartijId.Value);
        await using var db = _s.Factory.CreateContext();
        var invoice = await new PurchaseInvoiceService(db).CreateAsync(new CreatePurchaseInvoiceDto
        {
            RequestId = Guid.NewGuid(), VendorId = _s.VendorId, InvoiceNumber = "MAERSK-1", InvoiceDate = InvoiceDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Currency = currency,
            Lines = new() { new() { CostArticleId = costs["Zeevracht"], Quantity = 1m, UnitPrice = 5000m, VatPercentage = 0m } }
        });
        Assert.Equal(ServiceResultStatus.Created, invoice.Status);
        (_invoiceId, _lineId) = (invoice.Value.Id, invoice.Value.Lines[0].Id);
    }

    private async Task<ServiceResult<InvoiceAllocationsDto>> RefreshAsync()
    {
        await using var db = _s.Factory.CreateContext();
        return await new InvoiceExchangeRateService(db, _rates, _s.Factory.Calendar).RefreshAsync(_invoiceId);
    }

    private async Task<ServiceResult<InvoiceAllocationsDto>> SaveAsync(int version, params (int PartijId, decimal Amount)[] allocations)
    {
        await using var db = _s.Factory.CreateContext();
        return await new PartijCostService(db).SaveLineAsync(_invoiceId, _lineId, new SaveLineAllocationDto
        {
            RequestId = Guid.NewGuid(), ExpectedVersion = version,
            Allocations = allocations.Select(a => new SaveLineAllocationItemDto { PartijId = a.PartijId, Amount = a.Amount }).ToList()
        });
    }

    [Fact]
    public void Rules_Convert_Per_Line_Round_Half_Away_From_Zero_And_Only_Accept_A_Recent_Rate()
    {
        Assert.Equal(4478.28m, ExchangeRateRules.ToEuro(5000m, 1.1165m));
        Assert.Equal(0.01m, ExchangeRateRules.ToEuro(0.01m, 1.5m));
        // Terug naar de factuurvaluta naar verhouding van de regel: de hele regel is precies het regelbedrag (2.149,57 EUR = 2.400,00 USD,
        // niet 2.399,99 zoals 2.149,57 x 1,1165).
        Assert.Equal(558.25m, ExchangeRateRules.InCurrency(500m, 5000m, 4478.28m));
        Assert.Equal(2400m, ExchangeRateRules.InCurrency(2149.57m, 2400m, ExchangeRateRules.ToEuro(2400m, 1.1165m)));
        Assert.Equal(0m, ExchangeRateRules.InCurrency(1m, 2400m, 0m));
        Assert.Equal(5000m, ExchangeRateRules.LineAmountInEuro(5000m, "EUR", null));
        Assert.Equal(5000m, ExchangeRateRules.LineAmountInEuro(5000m, null, 1.1165m));
        Assert.Equal(4478.28m, ExchangeRateRules.LineAmountInEuro(5000m, "usd", 1.1165m));
        Assert.Null(ExchangeRateRules.LineAmountInEuro(5000m, "USD", null));
        // Een factuur van morgen: de koers van vandaag; zonder factuurdatum ook vandaag.
        var today = new DateOnly(2026, 11, 8);
        Assert.Equal(today, ExchangeRateRules.RateDay(new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc), today));
        Assert.Equal(today, ExchangeRateRules.RateDay(null, today));
        Assert.True(ExchangeRateRules.IsCurrent(new DateOnly(2026, 11, 6), today));
        Assert.True(ExchangeRateRules.IsCurrent(new DateOnly(2026, 10, 29), today));
        Assert.False(ExchangeRateRules.IsCurrent(new DateOnly(2026, 10, 28), today));
        Assert.False(ExchangeRateRules.IsCurrent(new DateOnly(2026, 11, 9), today));
    }

    [Fact]
    public void The_Ecb_Csv_Gives_The_Last_Observation_And_Nothing_Without_One()
    {
        const string csv = "KEY,FREQ,CURRENCY,CURRENCY_DENOM,EXR_TYPE,EXR_SUFFIX,TIME_PERIOD,OBS_VALUE,OBS_STATUS,TITLE_COMPL\r\n"
            + "EXR.D.USD.EUR.SP00.A,D,USD,EUR,SP00,A,2026-11-05,1.1224,A,\"ECB reference exchange rate, US dollar/Euro, 2.15 pm (C.E.T.)\"\r\n"
            + "EXR.D.USD.EUR.SP00.A,D,USD,EUR,SP00,A,2026-11-06,1.1165,A,\"ECB reference exchange rate, US dollar/Euro, 2.15 pm (C.E.T.)\"\r\n";
        Assert.Equal(Usd(1.1165m, 6), EcbExchangeRateSource.Parse(csv, "USD"));
        Assert.Null(EcbExchangeRateSource.Parse("", "USD"));
        Assert.Null(EcbExchangeRateSource.Parse("KEY,TIME_PERIOD\nx,2026-11-06", "USD"));
        Assert.Null(EcbExchangeRateSource.Parse("TIME_PERIOD,OBS_VALUE\n2026-11-06,NaN\n2026-11-07,-1", "USD"));
    }

    [Fact]
    public async Task The_Ecb_Source_Asks_For_The_Last_Rate_Up_To_The_Day_And_Reports_404_As_None_And_Errors_As_Unavailable()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("TIME_PERIOD,OBS_VALUE\n2026-11-05,1.1224\n") });
        var source = new EcbExchangeRateSource(new HttpClient(handler));
        Assert.Equal(Usd(1.1224m), await source.LatestAsync("USD", InvoiceDay, default));
        Assert.Equal("https://data-api.ecb.europa.eu/service/data/EXR/D.USD.EUR.SP00.A?endPeriod=2026-11-05&lastNObservations=1&format=csvdata", handler.Last.ToString());
        Assert.Null(await new EcbExchangeRateSource(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)))).LatestAsync("XYZ", InvoiceDay, default));
        await Assert.ThrowsAsync<ExchangeRateUnavailableException>(() =>
            new EcbExchangeRateSource(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))).LatestAsync("USD", InvoiceDay, default));
        await Assert.ThrowsAsync<ExchangeRateUnavailableException>(() =>
            new EcbExchangeRateSource(new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")))).LatestAsync("USD", InvoiceDay, default));
        // Nooit een vraag voor euro of een vreemde code.
        var count = handler.Calls;
        Assert.Null(await source.LatestAsync("EUR", InvoiceDay, default));
        Assert.Null(await source.LatestAsync("U$D", InvoiceDay, default));
        Assert.Equal(count, handler.Calls);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) => _answer = answer;
        public Uri Last { get; private set; }
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request.RequestUri;
            Calls++;
            return Task.FromResult(_answer(request));
        }
    }

    [Fact]
    public async Task Opening_A_Usd_Invoice_Fetches_The_Rate_Of_The_Invoice_Date_And_Converts_The_Line_To_Euro()
    {
        await ArrangeAsync();
        await using (var db = _s.Factory.CreateContext())
        {
            var before = (await new PartijCostService(db).GetInvoiceAsync(_invoiceId)).Value;
            Assert.Equal((false, (decimal?)null, (decimal?)null), (before.CanAllocate, before.Lines[0].LineTotalEur, before.Lines[0].General));
        }
        _rates.Next = Usd(1.1165m);
        var result = await RefreshAsync();
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal(("USD", InvoiceDay), Assert.Single(_rates.Asked));
        var invoice = result.Value;
        Assert.Equal(("USD", 1.1165m, InvoiceDay, "ECB", false, true), (invoice.Currency, invoice.ExchangeRate, invoice.ExchangeRateDate, invoice.ExchangeRateSource, invoice.RateLocked, invoice.CanAllocate));
        var line = Assert.Single(invoice.Lines);
        Assert.Equal((5000m, (decimal?)4478.28m, 0m, (decimal?)4478.28m, 1), (line.LineTotal, line.LineTotalEur, line.Allocated, line.General, line.Version));

        // Dezelfde koers nog eens: niets verandert (ook de versie niet); een nieuwere koers (nog niets verdeeld) wordt overgenomen.
        Assert.Equal(1, (await RefreshAsync()).Value.Lines[0].Version);
        _rates.Next = Usd(1.12m);
        var updated = (await RefreshAsync()).Value;
        Assert.Equal((1.12m, (decimal?)4464.29m, 2), (updated.ExchangeRate.Value, updated.Lines[0].LineTotalEur, updated.Lines[0].Version));
        await using var check = _s.Factory.CreateContext();
        var stored = await check.IncomingInvoices.SingleAsync(i => i.Id == _invoiceId);
        Assert.NotNull(stored.ExchangeRateFetchedAt);
    }

    [Fact]
    public async Task No_Rate_Unreachable_Ecb_Or_An_Old_Rate_Leaves_The_Invoice_Without_A_Rate()
    {
        await ArrangeAsync();
        _rates.Down = true;
        Assert.Equal(ServiceResultStatus.Unavailable, (await RefreshAsync()).Status);
        _rates.Down = false;
        _rates.Next = null;
        Assert.Equal(ServiceResultStatus.Validation, (await RefreshAsync()).Status);
        // De ECB geeft voor sommige valuta's alleen een oude indicatieve koers (MAD: 2020): die telt niet.
        _rates.Next = new ExchangeRateQuote("USD", 10.7485m, new DateOnly(2020, 10, 30), "ECB");
        var old = await RefreshAsync();
        Assert.Equal(ServiceResultStatus.Validation, old.Status);
        Assert.True(old.Errors.ContainsKey("currency"));
        await using var db = _s.Factory.CreateContext();
        Assert.Null((await db.IncomingInvoices.SingleAsync(i => i.Id == _invoiceId)).ExchangeRate);
    }

    [Fact]
    public async Task A_Euro_Invoice_Never_Asks_For_A_Rate()
    {
        await ArrangeAsync("EUR");
        var result = (await RefreshAsync()).Value;
        Assert.Empty(_rates.Asked);
        Assert.Equal((true, (decimal?)null, false, (decimal?)5000m), (result.CanAllocate, result.ExchangeRate, result.RateLocked, result.Lines[0].LineTotalEur));
    }

    [Fact]
    public async Task The_Quote_For_The_Entry_Screen_Saves_Nothing()
    {
        await ArrangeAsync();
        _rates.Next = Usd(1.1165m);
        await using var db = _s.Factory.CreateContext();
        var service = new InvoiceExchangeRateService(db, _rates, _s.Factory.Calendar);
        var quote = await service.QuoteAsync("usd", InvoiceDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        Assert.Equal(("USD", 1.1165m, InvoiceDay, InvoiceDay, "ECB"), (quote.Value.Currency, quote.Value.UnitsPerEuro, quote.Value.RateDate, quote.Value.ForDate, quote.Value.Source));
        Assert.Equal(ServiceResultStatus.Validation, (await service.QuoteAsync("EUR", null)).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await service.QuoteAsync("US", null)).Status);
        Assert.Null((await db.IncomingInvoices.AsNoTracking().SingleAsync(i => i.Id == _invoiceId)).ExchangeRate);
    }

}
