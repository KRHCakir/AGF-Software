using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Verkoopfactuur 1, directe factuur: een eigen V-nummer, een productregel alleen met partij en nooit meer dan vrij in die partij, de
// voorraad gereserveerd zolang het een concept is en weer vrij na weggooien; vrije regels met een omzetsoort zonder voorraad.
public sealed class SalesInvoiceDirectTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public SalesInvoiceDirectTests() => _factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 25));

    public void Dispose() => _factory.Dispose();

    private async Task<List<StockReservation>> ReservationsAsync(int invoiceId)
    {
        await using var db = _factory.CreateContext();
        return await db.StockReservations.Where(r => r.InvoiceLine.InvoiceId == invoiceId).OrderBy(r => r.Id).ToListAsync();
    }

    [Fact]
    public async Task A_Direct_Invoice_Gets_A_V_Number_Holds_Stock_From_The_Lot_And_Has_Vat_Per_Rate()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var result = await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId,
            DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotA, 10m), DirectInvoiceTestData.Service("Loodskosten", 35m));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var invoice = result.Value;
        Assert.Equal((SalesInvoiceRules.KindDirect, SalesInvoiceRules.StatusConcept), (invoice.Kind, invoice.Status));
        Assert.Null(invoice.InvoiceNumber);
        Assert.Matches("^V-2611\\d{3}$", invoice.SalesLotNumber);
        var goods = invoice.Lines[0];
        Assert.Equal(("I-2611001", 10m, 40m, 120m), (goods.LotNumber, goods.Quantity, goods.NetWeightKg.Value, goods.Amount.Value));
        var service = invoice.Lines[1];
        Assert.Equal(("Loodskosten", "Omzet diensten", 35m), (service.Description, service.RevenueTypeName, service.Amount.Value));
        Assert.Equal(new[] { (9m, 120m, 10.80m), (21m, 35m, 7.35m) }, invoice.Totals.VatRates.Select(r => (r.Percentage, r.Net, r.Vat)));
        Assert.Equal(173.15m, invoice.Totals.Gross);

        var reservation = Assert.Single(await ReservationsAsync(invoice.Id));
        Assert.Equal((ReservationRules.StatusActive, 10m, a.Seed.ApprovedBox), (reservation.Status, reservation.Quantity, reservation.PalletContentLineId.Value));
        Assert.Equal(30m, await DirectInvoiceTestData.FreeAsync(_factory, a, a.LotA));
    }

    [Fact]
    public async Task A_Product_Line_Without_A_Lot_Or_With_More_Than_Free_Is_Refused()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var noLot = await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Product(a.Seed.AvocadoId, null, 5m));
        Assert.Equal(ServiceResultStatus.Validation, noLot.Status);
        Assert.Equal("Choose the lot this comes from.", noLot.Errors["lines[0].partijId"][0]);

        var tooMuch = await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotA, 41m));
        Assert.Equal("Only 40 box is free in lot I-2611001.", tooMuch.Errors["lines[0].quantity"][0]);

        var twice = await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId,
            DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotA, 30m), DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotA, 15m));
        Assert.Equal("Only 10 box is free in lot I-2611001.", twice.Errors["lines[1].quantity"][0]);

        await using var db = _factory.CreateContext();
        Assert.Equal(0, await db.Invoices.CountAsync());
        Assert.Equal(0, await db.StockReservations.CountAsync(r => r.InvoiceLineId != null));
    }

    [Fact]
    public async Task Discarding_The_Concept_Releases_The_Stock()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var invoice = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotA, 25m))).Value;
        Assert.Equal(15m, await DirectInvoiceTestData.FreeAsync(_factory, a, a.LotA));
        await using (var db = _factory.CreateContext())
        {
            var discarded = await new SalesInvoiceService(db).DiscardAsync(invoice.Id, new DiscardSalesInvoiceDto { ExpectedVersion = invoice.Version });
            Assert.Equal(ServiceResultStatus.Ok, discarded.Status);
        }
        Assert.All(await ReservationsAsync(invoice.Id), r => Assert.Equal(ReservationRules.StatusReleased, r.Status));
        Assert.Equal(40m, await DirectInvoiceTestData.FreeAsync(_factory, a, a.LotA));
    }

    [Fact]
    public async Task Changing_A_Concept_Reserves_Again_Releases_Removed_Lines_And_Adds_New_Ones()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var invoice = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId,
            DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotA, 10m), DirectInvoiceTestData.Service("Handling", 20m))).Value;
        await using (var db = _factory.CreateContext())
        {
            var changed = await new SalesInvoiceService(db).UpdateAsync(invoice.Id, new UpdateSalesInvoiceDto
            {
                ExpectedVersion = invoice.Version,
                Lines = new List<UpdateSalesInvoiceLineDto>
                {
                    new() { Id = invoice.Lines[0].Id, Quantity = 35m, UnitPrice = 11m, VatPercentage = 9m },
                    new() { Id = invoice.Lines[1].Id, Quantity = 1m, UnitPrice = 20m, VatPercentage = 21m, Description = "Handling en opslag" }
                },
                NewLines = new List<SalesInvoiceNewLineDto> { DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotB, 5m) }
            });
            Assert.Equal(ServiceResultStatus.Ok, changed.Status);
            Assert.Equal(new[] { 35m, 1m, 5m }, changed.Value.Lines.Select(l => l.Quantity));
            Assert.Equal("Handling en opslag", changed.Value.Lines[1].Description);
            Assert.Equal(140m, changed.Value.Lines[0].NetWeightKg);
            invoice = changed.Value;
        }
        var reservations = await ReservationsAsync(invoice.Id);
        Assert.Equal(new[] { ReservationRules.StatusReleased, ReservationRules.StatusActive, ReservationRules.StatusActive }, reservations.Select(r => r.Status));
        Assert.Equal((5m, 15m), (await DirectInvoiceTestData.FreeAsync(_factory, a, a.LotA), await DirectInvoiceTestData.FreeAsync(_factory, a, a.LotB)));

        await using (var db = _factory.CreateContext())
        {
            var removed = await new SalesInvoiceService(db).UpdateAsync(invoice.Id, new UpdateSalesInvoiceDto
            {
                ExpectedVersion = invoice.Version,
                Lines = new List<UpdateSalesInvoiceLineDto> { new() { Id = invoice.Lines[0].Id, Remove = true } }
            });
            Assert.Equal(ServiceResultStatus.Ok, removed.Status);
            Assert.Equal(2, removed.Value.Lines.Count);
        }
        Assert.Equal(40m, await DirectInvoiceTestData.FreeAsync(_factory, a, a.LotA));
    }

    [Fact]
    public async Task A_Free_Line_Has_No_Stock_And_A_Revenue_Type()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var invoice = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Service("Stroom koelcel november", 120m))).Value;
        var line = Assert.Single(invoice.Lines);
        Assert.Equal((SalesInvoiceRules.LineService, "Omzet diensten", (int?)null), (line.LineKind, line.RevenueTypeName, line.PartijId));
        Assert.Empty(await ReservationsAsync(invoice.Id));

        var unknown = DirectInvoiceTestData.Service("Transport", 50m);
        unknown.RevenueTypeId = 999;
        Assert.Equal(ServiceResultStatus.Validation, (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, unknown)).Status);
    }

    [Fact]
    public async Task An_Own_Number_That_Exists_Is_Refused_And_A_Repeated_Request_Returns_The_Same_Concept()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var taken = await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, Guid.NewGuid(), "I-2611001", DirectInvoiceTestData.Service("Handling", 10m));
        Assert.Equal(ServiceResultStatus.Conflict, taken.Status);

        var key = Guid.NewGuid();
        var own = await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, key, "BALIE-25", DirectInvoiceTestData.Service("Handling", 10m));
        Assert.Equal(("BALIE-25", ServiceResultStatus.Created), (own.Value.SalesLotNumber, own.Status));
        var again = await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, key, "BALIE-25", DirectInvoiceTestData.Service("Handling", 10m));
        Assert.Equal((ServiceResultStatus.Ok, own.Value.Id), (again.Status, again.Value.Id));
    }
}
