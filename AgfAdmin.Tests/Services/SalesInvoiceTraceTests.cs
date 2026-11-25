using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;

namespace AgfAdmin.Tests.Services;

// Verkoopfactuur 1, herleidbaarheid: een concept van een directe factuur houdt voorraad van de partij vast (gereserveerd), een definitieve
// factuur heeft afgeboekt en staat in de recall van de inkoop-partij als bestemming (klant, factuurnummer, aantal), met verschil 0. De
// verkoop-partij (V-nummer) van de factuur toont de inkoop-partijen van haar regels.
public sealed class SalesInvoiceTraceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<PartijDetailDto> DetailAsync(int id)
    {
        await using var db = _factory.CreateContext();
        return (await new PartijService(db).GetAsync(id)).Value;
    }

    [Fact]
    public async Task A_Concept_Is_Reserved_And_A_Final_Direct_Invoice_Is_A_Destination_In_The_Recall()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var concept = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotA, 6m))).Value;

        var reserved = await DetailAsync(a.LotA);
        Assert.Equal((6m, 0m), (reserved.ReservedQuantity, reserved.ShippedQuantity));
        Assert.Empty(reserved.Recall.Destinations);
        Assert.Empty(reserved.SalesOrders);
        var salesLot = (await DetailAsync(await SalesLotIdAsync(concept.Id))).Delivery;
        Assert.False(salesLot.Shipped);
        Assert.Equal("Reserved", salesLot.Lines.Single().Sources.Single().Status);

        SalesInvoiceDto final;
        await using (var db = _factory.CreateContext())
        {
            final = (await new SalesInvoiceService(db).FinalizeAsync(concept.Id, new FinalizeSalesInvoiceDto { ExpectedVersion = concept.Version })).Value;
        }
        var shipped = await DetailAsync(a.LotA);
        Assert.Equal((0m, 6m), (shipped.ReservedQuantity, shipped.ShippedQuantity));
        var destination = Assert.Single(shipped.Recall.Destinations);
        Assert.Equal((final.InvoiceNumber, "Customer T", 6m, "box", final.SalesLotNumber), (destination.Reference, destination.CustomerName, destination.Quantity, destination.Unit, destination.SalesPartijNumber));
        Assert.Null(destination.SalesOrderId);
        Assert.Empty(shipped.Recall.DirectInvoices);
        Assert.Equal(0m, shipped.Recall.Difference);

        var trace = (await DetailAsync(await SalesLotIdAsync(final.Id))).Delivery;
        Assert.True(trace.Shipped);
        Assert.Equal(("I-2611001", 6m, "Invoiced"), (trace.Lines.Single().Sources.Single().Number, trace.Lines.Single().Quantity, trace.Lines.Single().Sources.Single().Status));
    }

    [Fact]
    public async Task A_Discarded_Concept_Leaves_No_Trace_On_The_Lot()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var concept = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotA, 6m))).Value;
        await using (var db = _factory.CreateContext())
        {
            await new SalesInvoiceService(db).DiscardAsync(concept.Id, new DiscardSalesInvoiceDto { ExpectedVersion = concept.Version });
        }
        var lot = await DetailAsync(a.LotA);
        Assert.Equal((0m, 0m), (lot.ReservedQuantity, lot.ShippedQuantity));
        Assert.Empty((await DetailAsync(await SalesLotIdAsync(concept.Id))).Delivery.Lines);
    }

    private async Task<int> SalesLotIdAsync(int invoiceId)
    {
        await using var db = _factory.CreateContext();
        return db.Partijen.Single(p => p.InvoiceId == invoiceId).Id;
    }
}
