using AgfAdmin.Controllers;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: herleidbaarheid in beide richtingen. Vanuit een verkoop-partij zie je uit welke inkoop-partijen het kwam; vanuit
// een inkoop-partij waar alles naartoe ging, wat afgekeurd is en wat er nog ligt, met een som die klopt (recall). En de inkoopwaarde als
// basis voor "Verdeel naar waarde".
public sealed class PartijTraceTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();
    private int _lotA, _lotB, _palletA1, _palletA2, _palletB;

    public PartijTraceTests() => _s.Factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 23));

    public void Dispose() => _s.Dispose();

    // Inkoop-partij A (Kwekerij Zonnegloed, Spanje): 30 colli goedgekeurd + 8 afgekeurd, € 10 per colli. Inkoop-partij B (Marokko): 20 colli,
    // USD 11 per colli tegen 1,10 per euro (= € 10).
    private async Task ArrangeAsync()
    {
        await _s.ArrangeAsync();
        await using var db = _s.Factory.CreateContext();
        (int Lot, int[] Contents) Lot(string number, string origin, string currency, decimal rate, decimal price, params (decimal Quantity, string Quality)[] pallets)
        {
            var lot = new Partij { Kind = PartijRules.KindPurchase, Number = number, NumberKey = PartijRules.NumberKey(number), CreatedBy = "test" };
            var order = new SupplyCommitment { RequestId = Guid.NewGuid(), VendorId = _s.VendorId, Route = "Warehouse", Status = "Received", Origin = origin, Partij = lot, Currency = currency, ExchangeRate = rate,
                ExchangeRateSource = currency == "EUR" ? "NotApplicable" : "Manual" };
            var line = new SupplyCommitmentLine { SupplyCommitment = order, ProductVariantId = _s.TomatoId, ExpectedQuantity = pallets.Sum(p => p.Quantity), ExpectedUnit = "colli", Partij = lot, UnitPrice = price };
            var contents = pallets.Select((p, i) => new PalletContentLine
            {
                RequestId = Guid.NewGuid(), SupplyCommitmentLine = line, Partij = lot, LotNumber = number, Quantity = p.Quantity, Unit = "colli", NetWeightKg = p.Quantity * 6m, QualityStatus = p.Quality,
                Pallet = new Pallet { Reference = $"{number}-P{i + 1}", LogisticsStatus = "Received", LocationContext = "Warehouse" },
            }).ToList();
            db.AddRange(lot, order, line);
            db.AddRange(contents);
            db.SaveChanges();
            return (lot.Id, contents.Select(c => c.Id).ToArray());
        }
        var a = Lot("I-2611010", "Spanje", "EUR", 1m, 10m, (30m, "Approved"), (8m, "Rejected"));
        var b = Lot("I-2611011", "Marokko", "USD", 1.1m, 11m, (20m, "Approved"));
        (_lotA, _palletA1, _palletA2, _lotB, _palletB) = (a.Lot, a.Contents[0], a.Contents[1], b.Lot, b.Contents[0]);
    }

    private async Task<int> ReserveAsync(int orderId, int lineId, int contentId, decimal quantity)
    {
        await using var db = _s.Factory.CreateContext();
        var result = await new StockReservationService(db).ReserveAsync(orderId, lineId, new CreateStockReservationDto { RequestId = Guid.NewGuid(), PalletContentLineId = contentId, Quantity = quantity, Unit = "colli", NetWeightKg = quantity * 6m });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value.Id;
    }

    private async Task<PartijDetailDto> DetailAsync(int id)
    {
        await using var db = _s.Factory.CreateContext();
        return (await new PartijService(db).GetAsync(id)).Value;
    }

    [Fact]
    public async Task A_Purchase_Lot_Shows_Where_Everything_Went_And_What_Is_Left_And_The_Sum_Adds_Up()
    {
        await ArrangeAsync();
        var order = (await _s.CreateAsync(_s.Order((25m, "Stock")))).Value;
        var first = await ReserveAsync(order.Id, order.Lines[0].Id, _palletA1, 12m);
        var second = await ReserveAsync(order.Id, order.Lines[0].Id, _palletB, 13m);
        await SalesTestData.ConsumeAsync(_s.Factory, first);
        // Een tweede klant krijgt 5 uit partij A (gereserveerd, nog niet verzonden: die liggen nog op voorraad).
        var other = (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value;
        await ReserveAsync(other.Id, other.Lines[0].Id, _palletA1, 5m);

        var recall = (await DetailAsync(_lotA)).Recall;
        Assert.Equal((38m, 12m, 0m, 8m, 18m, 0m), (recall.Received, recall.Shipped, recall.Returned, recall.Rejected, recall.OnHand, recall.Difference));
        var destination = Assert.Single(recall.Destinations);
        Assert.Equal((order.SalesPartijNumber, "Groothandel G", 12m, order.OrderNumber), (destination.SalesPartijNumber, destination.CustomerName, destination.Quantity, destination.Reference));
        Assert.NotNull(destination.Date);
        Assert.True(second > 0);
    }

    // Review: de inkoopwaarde van een directe factuur gebruikt de inkoopprijs alleen in dezelfde eenheid (de prijs geldt per verpakking).
    [Theory]
    [InlineData("colli", 40.0)]
    [InlineData("kg", null)]
    public async Task A_Direct_Invoice_Lot_Has_A_Purchase_Value_Only_In_The_Unit_Of_The_Purchase_Price(string unit, double? expected)
    {
        await ArrangeAsync();
        await using (var db = _s.Factory.CreateContext())
        {
            var created = await new SalesInvoiceController(db, new PartijService(db)).CreateInvoice(new SalesInvoiceController.CreateInvoiceDto
            {
                CustomerName = "Balieklant", Lines = new() { new() { Description = "Tomaat ronde", Quantity = 4m, Unit = unit, UnitPrice = 12m, VATPercentage = 9m, PartijId = _lotA } }
            });
            Assert.IsType<OkObjectResult>(created);
        }
        await using var verify = _s.Factory.CreateContext();
        var lot = await verify.Partijen.SingleAsync(p => p.InvoiceId != null);
        Assert.Equal(expected == null ? null : (decimal?)expected.Value, (await DetailAsync(lot.Id)).PurchaseValue);
    }

    // Review: twee verzendingen van dezelfde order uit dezelfde partij blijven twee regels, elk met de datum van die verzending.
    [Fact]
    public async Task Each_Dispatch_Is_Its_Own_Destination_With_Its_Own_Date()
    {
        await ArrangeAsync();
        var order = (await _s.CreateAsync(_s.Order((20m, "Stock")))).Value;
        var first = await ReserveAsync(order.Id, order.Lines[0].Id, _palletA1, 12m);
        await SalesTestData.ConsumeAsync(_s.Factory, first);
        var second = await ReserveAsync(order.Id, order.Lines[0].Id, _palletA1, 8m);
        await SalesTestData.ConsumeAsync(_s.Factory, second);

        var recall = (await DetailAsync(_lotA)).Recall;
        Assert.Equal(new[] { 12m, 8m }, recall.Destinations.Select(d => d.Quantity));
        await using var db = _s.Factory.CreateContext();
        var dates = await db.SalesDispatches.AsNoTracking().Where(d => d.SalesOrderId == order.Id).OrderBy(d => d.DispatchedAt).Select(d => d.DispatchedAt).ToListAsync();
        Assert.Equal(dates.Select(d => DateTime.SpecifyKind(d, DateTimeKind.Utc)), recall.Destinations.Select(d => d.Date!.Value));
    }

    [Fact]
    public async Task A_Sales_Lot_Shows_The_Purchase_Lots_Per_Line_With_Supplier_And_Origin_And_The_Delivery_Date()
    {
        await ArrangeAsync();
        var order = (await _s.CreateAsync(_s.Order((25m, "Stock")))).Value;
        var first = await ReserveAsync(order.Id, order.Lines[0].Id, _palletA1, 12m);
        await ReserveAsync(order.Id, order.Lines[0].Id, _palletB, 13m);
        await SalesTestData.ConsumeAsync(_s.Factory, first);

        var trace = (await DetailAsync(order.SalesPartijId!.Value)).Delivery;
        Assert.Equal(("Groothandel G", order.OrderNumber, 1, true), (trace.CustomerName, trace.Reference, trace.Sequence, trace.Shipped));
        var line = Assert.Single(trace.Lines);
        Assert.Equal(25m, line.Quantity);
        Assert.Equal(new[] { ("I-2611010", "Kweker K", "Spanje", 12m, "Consumed"), ("I-2611011", "Kweker K", "Marokko", 13m, "Active") },
            line.Sources.Select(s => (s.Number, s.VendorName, s.Origin, s.Quantity, s.Status)));
    }

    [Fact]
    public async Task A_Direct_Invoice_Lot_Shows_Its_Purchase_Lots_And_The_Purchase_Lot_Lists_The_Invoice_Apart()
    {
        await ArrangeAsync();
        await using (var db = _s.Factory.CreateContext())
        {
            var created = await new SalesInvoiceController(db, new PartijService(db)).CreateInvoice(new SalesInvoiceController.CreateInvoiceDto
            {
                CustomerName = "Balieklant", Lines = new() { new() { Description = "Tomaat ronde", Quantity = 4m, Unit = "colli", UnitPrice = 12m, VATPercentage = 9m, PartijId = _lotA } }
            });
            Assert.IsType<OkObjectResult>(created);
        }
        await using var verify = _s.Factory.CreateContext();
        var lot = await verify.Partijen.SingleAsync(p => p.InvoiceId != null);
        var trace = (await DetailAsync(lot.Id)).Delivery;
        Assert.Equal(("Balieklant", "I-2611010", 4m), (trace.CustomerName, trace.Lines.Single().Sources.Single().Number, trace.Lines.Single().Quantity));
        var recall = (await DetailAsync(_lotA)).Recall;
        Assert.Equal((lot.Number, "Balieklant", 4m), (recall.DirectInvoices.Single().SalesPartijNumber, recall.DirectInvoices.Single().CustomerName, recall.DirectInvoices.Single().Quantity));
        Assert.Equal(0m, recall.Difference);

        // Review: een factuur zonder eigen verkoop-partij (bijvoorbeeld een orderfactuur) is geen directe factuur, ook niet met een partij op de regel.
        await using (var db = _s.Factory.CreateContext())
        {
            db.Add(new Invoice
            {
                InvoiceNumber = "FAC-ORDER-1", InvoiceDate = DateTime.UtcNow, CustomCustomerName = "Orderklant",
                InvoiceLines = new List<InvoiceLine> { new() { Description = "Tomaat ronde", Quantity = 2m, Unit = "colli", UnitPrice = 12m, PartijId = _lotA } }
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal("Balieklant", (await DetailAsync(_lotA)).Recall.DirectInvoices.Single().CustomerName);
        var listed = (await new PartijService(verify).ListAsync(lot.Number, "all", 1, 50, kind: "Sales")).Value.Items.Single();
        Assert.Equal("Balieklant", listed.CustomerName);
        Assert.Equal(new[] { "I-2611010" }, listed.SourcePartijen);
    }

    [Fact]
    public async Task The_Purchase_Value_Is_The_Lines_In_Euro_And_A_Sales_Lot_Values_Its_Colli_At_Purchase_Price()
    {
        await ArrangeAsync();
        Assert.Equal(380m, (await DetailAsync(_lotA)).PurchaseValue);
        Assert.Equal(200m, (await DetailAsync(_lotB)).PurchaseValue);
        var order = (await _s.CreateAsync(_s.Order((25m, "Stock")))).Value;
        await ReserveAsync(order.Id, order.Lines[0].Id, _palletA1, 12m);
        await ReserveAsync(order.Id, order.Lines[0].Id, _palletB, 13m);
        Assert.Equal(250m, (await DetailAsync(order.SalesPartijId!.Value)).PurchaseValue);

        // Review: een prijs per andere verpakking dan de reservering wordt nooit zomaar vermenigvuldigd; dan is de waarde onbekend.
        await using (var db = _s.Factory.CreateContext())
        {
            await db.SupplyCommitmentLines.Where(l => l.PartijId == _lotB).ExecuteUpdateAsync(u => u.SetProperty(l => l.ExpectedUnit, "box"));
        }
        Assert.Null((await DetailAsync(order.SalesPartijId!.Value)).PurchaseValue);
    }
}
