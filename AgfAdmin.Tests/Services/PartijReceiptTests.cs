using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Ontvangst erft de partij van de orderregel (niets te typen); LotNumber krijgt het partijnummer. Afgesplitste inhoud houdt de partij.
public class PartijReceiptTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(int OrderId, int LineId, int PartijId, int PalletId)> ArrangeAsync(bool withPartij = true)
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var partijId = 0;
        if (withPartij)
        {
            partijId = await PartijTestData.CreateAsync(_factory, "2610251");
            await PartijTestData.LinkOrderAsync(_factory, order.Id, partijId);
        }
        await using var db = _factory.CreateContext();
        return (order.Id, order.Lines.Single().Id, partijId, await new PalletService(db).CreatePalletForTestAsync());
    }

    private async Task<ServiceResult<PalletContentLineDto>> ReceiveAsync(int palletId, int lineId, string lot, Guid? requestId = null)
    {
        await using var db = _factory.CreateContext();
        return await new PalletService(db).AddContentLineAsync(palletId, new AddPalletContentLineDto
        {
            RequestId = requestId ?? Guid.NewGuid(), SupplyCommitmentLineId = lineId, LotNumber = lot, Quantity = 20m, Unit = "box", NetWeightKg = 80m
        });
    }

    [Fact]
    public async Task Receipt_Inherits_The_Lot_Of_The_Order_Without_Typing()
    {
        var a = await ArrangeAsync();
        var result = await ReceiveAsync(a.PalletId, a.LineId, null);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(a.PartijId, result.Value.PartijId);
        Assert.Equal("2610251", result.Value.LotNumber);
    }

    [Fact]
    public async Task Same_Lot_In_Another_Spelling_Is_Stored_As_The_Lot_Number_And_A_Retry_Is_The_Same_Request()
    {
        var a = await ArrangeAsync();
        var requestId = Guid.NewGuid();
        var first = await ReceiveAsync(a.PalletId, a.LineId, "2610 251", requestId);
        Assert.Equal(ServiceResultStatus.Created, first.Status);
        Assert.Equal("2610251", first.Value.LotNumber);

        var retry = await ReceiveAsync(a.PalletId, a.LineId, "2610 251", requestId);
        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(first.Value.Id, retry.Value.Id);
    }

    [Fact]
    public async Task Another_Lot_On_An_Order_With_A_Lot_Is_Refused_And_Nothing_Is_Mixed()
    {
        var a = await ArrangeAsync();
        var result = await ReceiveAsync(a.PalletId, a.LineId, "2610252");

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains("2610251", result.Message);
        await using var db = _factory.CreateContext();
        Assert.Equal(0, await db.PalletContentLines.CountAsync());
    }

    [Fact]
    public async Task Without_A_Lot_On_The_Order_The_Lot_Number_Stays_Required()
    {
        var a = await ArrangeAsync(withPartij: false);
        var missing = await ReceiveAsync(a.PalletId, a.LineId, "  ");
        Assert.Equal(ServiceResultStatus.Validation, missing.Status);
        Assert.Contains("lotNumber", missing.Errors.Keys);

        var typed = await ReceiveAsync(a.PalletId, a.LineId, "OUD-7");
        Assert.Equal(ServiceResultStatus.Created, typed.Status);
        Assert.Null(typed.Value.PartijId);
        Assert.Equal("OUD-7", typed.Value.LotNumber);
    }

    [Fact]
    public async Task Partial_Rejection_Keeps_The_Lot_On_The_Split_Off_Line()
    {
        var a = await ArrangeAsync();
        var received = await ReceiveAsync(a.PalletId, a.LineId, null);
        int version;
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync();
            content.QualityStatus = "Approved";
            await db.SaveChangesAsync();
            version = content.Version;
        }

        await using (var db = _factory.CreateContext())
        {
            var rejected = await new PurchaseOrderService(db).RejectAsync(a.OrderId, new RejectPurchaseOrderDto
            {
                RequestId = Guid.NewGuid(), Reason = "Quality", Decision = "Dispose",
                Lines = new List<RejectPurchaseOrderLineDto> { new() { PalletContentLineId = received.Value.Id, ExpectedContentLineVersion = version, Quantity = 5m } }
            });
            Assert.Equal(ServiceResultStatus.Created, rejected.Status);
        }

        await using var verify = _factory.CreateContext();
        var lines = await verify.PalletContentLines.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => { Assert.Equal(a.PartijId, l.PartijId); Assert.Equal("2610251", l.LotNumber); });
    }

    [Fact]
    public async Task After_Migration_A_Receipt_On_A_Linked_Order_Inherits_The_Lot()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        await PurchaseOrderTestData.ReceiveAsync(_factory, order.Id, 10m, 40m, lot: "2610 251");
        await using (var migration = _factory.CreateContext())
        {
            await migration.Database.ExecuteSqlRawAsync(AgfAdmin.Migrations.AddPartijen.LinkExistingLotsSql);
        }

        // Een nieuwe ontvangst op een door de migratie gekoppelde order neemt de partij over, zonder iets te typen.
        await using var db = _factory.CreateContext();
        var pallet = await new PalletService(db).CreatePalletForTestAsync();
        var line = await db.SupplyCommitmentLines.AsNoTracking().SingleAsync(l => l.SupplyCommitmentId == order.Id);
        var received = await new PalletService(db).AddContentLineAsync(pallet, new()
        {
            RequestId = Guid.NewGuid(), SupplyCommitmentLineId = line.Id, Quantity = 4m, Unit = "box", NetWeightKg = 16m
        });
        Assert.Equal(ServiceResultStatus.Created, received.Status);
        Assert.Equal("2610 251", received.Value.LotNumber); // de schrijfwijze van de oudste regel werd het partijnummer
    }
}
