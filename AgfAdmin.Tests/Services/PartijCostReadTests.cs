using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Partij P2, kosten op het partijdetail: per partij de kostenregels met factuur en leverancier en het totaal. Een inkoop-partij en de
// verkoop-partij die er colli uit pakt houden hun kosten gescheiden (niets telt dubbel); de ander staat er apart bij. De verdeling noemt
// per partij de basis voor "verdeel naar colli/kg": ontvangen bij inkoop, besteld bij verkoop.
public sealed class PartijCostReadTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();

    public PartijCostReadTests() => _s.Factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 5));

    public void Dispose() => _s.Dispose();

    private async Task<(int InvoiceId, int LineId)> InvoiceAsync(int costArticleId, string number, decimal amount)
    {
        await using var db = _s.Factory.CreateContext();
        var invoice = (await new PurchaseInvoiceService(db).CreateAsync(new CreatePurchaseInvoiceDto
        {
            RequestId = Guid.NewGuid(), VendorId = _s.VendorId, InvoiceNumber = number, InvoiceDate = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc),
            Lines = new() { new() { CostArticleId = costArticleId, Quantity = 1m, UnitPrice = amount, VatPercentage = 21m } }
        })).Value;
        return (invoice.Id, invoice.Lines[0].Id);
    }

    private async Task<InvoiceAllocationsDto> AllocateAsync((int InvoiceId, int LineId) line, params (int PartijId, decimal Amount)[] allocations)
    {
        await using var db = _s.Factory.CreateContext();
        var result = await new PartijCostService(db).SaveLineAsync(line.InvoiceId, line.LineId, new SaveLineAllocationDto
        {
            RequestId = Guid.NewGuid(), ExpectedVersion = 0, Allocations = allocations.Select(a => new SaveLineAllocationItemDto { PartijId = a.PartijId, Amount = a.Amount }).ToList()
        });
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        return result.Value;
    }

    [Fact]
    public async Task Each_Lot_Shows_Its_Own_Costs_And_The_Other_Kind_Separately_So_Nothing_Counts_Twice()
    {
        await _s.ArrangeAsync();
        var costs = await CostArticleTestData.SeedStandardAsync(_s.Factory);
        int purchase;
        await using (var db = _s.Factory.CreateContext())
        {
            purchase = (await new PartijService(db).CreateAsync(new CreatePartijDto { RequestId = Guid.NewGuid(), Number = "52611002" })).Value.Id;
            var received = await db.PalletContentLines.Where(c => c.Id == _s.StockContentId).Select(c => c.SupplyCommitmentLine.SupplyCommitmentId).SingleAsync();
            Assert.Equal(ServiceResultStatus.Ok, (await new PartijService(db).LinkOrderAsync(purchase, new LinkPartijDto { SupplyCommitmentId = received, Reason = "Test" })).Status);
        }
        var order = (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value;
        Assert.Equal(ServiceResultStatus.Created, (await _s.ReserveAsync(order.Id, order.Lines[0].Id, _s.StockContentId, 5m, 30m)).Status);
        var sales = order.SalesPartijId.Value;

        var handling = await AllocateAsync(await InvoiceAsync(costs["Handling"], "BC-118", 5000m), (sales, 500m));
        var freight = await AllocateAsync(await InvoiceAsync(costs["Zeevracht"], "MAERSK-77", 2000m), (purchase, 2000m));

        // De basis voor verdelen: verkoop = besteld (5 colli, kg van de reservering), inkoop = ontvangen (40 + 15 colli, 240 + 90 kg).
        var salesBasis = handling.Lines[0].Allocations.Single();
        Assert.Equal((5m, 30m, "colli"), (salesBasis.Quantity, salesBasis.NetKg, salesBasis.Unit));
        Assert.Contains(order.OrderNumber, salesBasis.Party);
        var purchaseBasis = freight.Lines[0].Allocations.Single();
        Assert.Equal((55m, 330m, "Kweker K"), (purchaseBasis.Quantity, purchaseBasis.NetKg, purchaseBasis.Party));

        await using var read = _s.Factory.CreateContext();
        var service = new PartijService(read);
        var salesDetail = (await service.GetAsync(sales)).Value;
        var cost = Assert.Single(salesDetail.Costs);
        Assert.Equal(("Handling", "BC-118", "Kweker K", 500m), (cost.Kind, cost.InvoiceNumber, cost.VendorName, cost.Amount));
        Assert.Equal(500m, salesDetail.CostTotal);
        Assert.Equal(new[] { ("52611002", 2000m) }, salesDetail.LinkedCosts.Select(l => (l.Number, l.CostTotal)));

        var purchaseDetail = (await service.GetAsync(purchase)).Value;
        Assert.Equal(("Zeevracht", 2000m), (Assert.Single(purchaseDetail.Costs).Kind, purchaseDetail.CostTotal));
        Assert.Equal(new[] { (salesDetail.Number, 500m) }, purchaseDetail.LinkedCosts.Select(l => (l.Number, l.CostTotal)));
        // Samen precies wat verdeeld is: 2.000 + 500; nooit 2.500 op één partij.
        Assert.Equal(2500m, purchaseDetail.CostTotal + salesDetail.CostTotal);
    }

    [Fact]
    public async Task The_Kg_Basis_Of_A_Sales_Lot_Takes_The_Line_Kg_And_Only_For_Lines_Without_Kg_The_Reserved_Kg()
    {
        await _s.ArrangeAsync();
        var request = _s.Order((5m, "Stock"));
        request.Lines[0].NetWeightKg = 50m;
        request.Lines.Add(new CreateSalesOrderLineDto { ProductVariantId = _s.LookalikeId, Quantity = 5m, Unit = "colli", SupplySource = "Stock" });
        var order = (await _s.CreateAsync(request)).Value;
        int lookalikeContent;
        await using (var db = _s.Factory.CreateContext())
        {
            lookalikeContent = await db.PalletContentLines.Where(c => c.LotNumber == "LOT-L").Select(c => c.Id).SingleAsync();
        }
        // Regel 1 heeft 50 kg en een reservering van 30 kg (telt niet dubbel); regel 2 heeft geen kg en een reservering van 30 kg.
        Assert.Equal(ServiceResultStatus.Created, (await _s.ReserveAsync(order.Id, order.Lines[0].Id, _s.StockContentId, 5m, 30m)).Status);
        Assert.Equal(ServiceResultStatus.Created, (await _s.ReserveAsync(order.Id, order.Lines[1].Id, lookalikeContent, 5m, 30m)).Status);
        await using var read = _s.Factory.CreateContext();
        var lot = (await new PartijService(read).ListAsync(null, "all", 1, 50, kind: "Sales")).Value.Items.Single(i => i.SalesOrderId == order.Id);
        Assert.Equal((10m, 80m), (lot.OrderedQuantity, lot.NetKg));
    }

    [Fact]
    public async Task A_Lot_Without_Costs_Has_An_Empty_Cost_Block()
    {
        await _s.ArrangeAsync();
        var order = (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value;
        await using var db = _s.Factory.CreateContext();
        var detail = (await new PartijService(db).GetAsync(order.SalesPartijId.Value)).Value;
        Assert.Empty(detail.Costs);
        Assert.Equal(0m, detail.CostTotal);
        Assert.Empty(detail.LinkedCosts);
    }
}
