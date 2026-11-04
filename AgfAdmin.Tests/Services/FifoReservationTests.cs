using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// "Reserveren (oudste partij eerst)": een voorstel in FIFO-volgorde (ontvangstmoment, dan partijnummer) over zoveel pallets als nodig,
// aan te passen en te bevestigen als één batch van gewone exacte reserveringen (alles of niets, idempotent via RequestId, gelijktijdige
// schrijvers botsen op de versie). En de werklijst "Verkocht, wacht op voorraad", die nooit zelf reserveert.
public sealed class FifoReservationTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();

    public void Dispose() => _s.Dispose();

    private static CreateStockReservationBatchDto Batch(Guid requestId, params (int Content, decimal Quantity, decimal Kg)[] items) => new()
    {
        RequestId = requestId, Unit = "colli", 
        Items = items.Select(i => new FifoReservationItemDto { PalletContentLineId = i.Content, Quantity = i.Quantity, NetWeightKg = i.Kg }).ToList()
    };

    private async Task<FifoReservationProposalDto> ProposalAsync(int orderId, int lineId)
    {
        await using var db = _s.Factory.CreateContext();
        var result = await new SalesOrderService(db).GetFifoProposalAsync(orderId, lineId);
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        return result.Value;
    }

    private async Task<ServiceResult<IReadOnlyList<StockReservationDto>>> ReserveBatchAsync(int orderId, int lineId, CreateStockReservationBatchDto batch)
    {
        await using var db = _s.Factory.CreateContext();
        return await new StockReservationService(db).ReserveBatchAsync(orderId, lineId, batch);
    }

    [Fact]
    public async Task Fifo_Spreads_One_Line_Over_Two_Pallets_And_Two_Lots_Oldest_Receipt_First()
    {
        await _s.ArrangeAsync();
        var newer = await _s.ReceiveAsync(_s.OpenOrderLineId, 20m, "Approved", "LOT-B", SalesBeforeArrivalScenario.Received.AddDays(3));
        var order = (await _s.CreateAsync(_s.Order((50m, "Expected")))).Value;

        var proposal = await ProposalAsync(order.Id, order.Lines[0].Id);
        Assert.Equal((50m, 50m), (proposal.NeededQuantity, proposal.ProposedQuantity));
        Assert.Equal(new[] { _s.StockContentId, newer }, proposal.Candidates.Select(c => c.PalletContentLineId));
        Assert.Equal(new[] { (_s.StockContentId, 40m, 240m), (newer, 10m, 60m) }, proposal.Items.Select(i => (i.PalletContentLineId, i.Quantity, i.NetWeightKg)));
        Assert.All(proposal.Candidates, c => Assert.StartsWith("IO-", c.SupplyOrderNumber));

        var reserved = await ReserveBatchAsync(order.Id, order.Lines[0].Id, new CreateStockReservationBatchDto
        {
            RequestId = Guid.NewGuid(), Unit = "colli", Items = proposal.Items
        });
        Assert.Equal(ServiceResultStatus.Created, reserved.Status);
        Assert.Equal(new[] { ("LOT-A", 40m), ("LOT-B", 10m) }, reserved.Value.Select(r => (r.LotNumber, r.Quantity)));
        Assert.All(reserved.Value, r => Assert.Equal("Physical", r.Source));
        await using var db = _s.Factory.CreateContext();
        var after = (await new SalesOrderService(db).GetAsync(order.Id)).Value.Lines[0];
        Assert.Equal((50m, false), (after.ReservedQuantity, after.AwaitingStock));
        // Herkomst per stuk: inkooporder, partij en pallet van elke reservering.
        Assert.All(after.Reservations, r => Assert.False(string.IsNullOrEmpty(r.SupplyOrderNumber) || string.IsNullOrEmpty(r.PalletReference)));
        Assert.Equal(2, await db.PalletAuditEvents.CountAsync(e => e.EventType == "ReservationCreated"));
    }

    [Fact]
    public async Task On_The_Same_Receipt_Moment_The_Lot_Number_Decides()
    {
        await _s.ArrangeAsync();
        var lotZ = await _s.ReceiveAsync(_s.OpenOrderLineId, 10m, "Approved", "LOT-Z", SalesBeforeArrivalScenario.Received);
        var lot0 = await _s.ReceiveAsync(_s.OpenOrderLineId, 10m, "Approved", "LOT-0", SalesBeforeArrivalScenario.Received);
        var order = (await _s.CreateAsync(_s.Order((15m, "Stock")))).Value;
        var proposal = await ProposalAsync(order.Id, order.Lines[0].Id);
        Assert.Equal(new[] { lot0, _s.StockContentId, lotZ }, proposal.Candidates.Select(c => c.PalletContentLineId));
        Assert.Equal(new[] { (lot0, 10m), (_s.StockContentId, 5m) }, proposal.Items.Select(i => (i.PalletContentLineId, i.Quantity)));
        // Een deel van gewogen inhoud krijgt kg naar verhouding (5 van 40 colli à 240 kg = 30 kg).
        Assert.Equal(30m, proposal.Items[1].NetWeightKg);
    }

    [Fact]
    public async Task Quarantine_And_Blocked_Content_Are_Never_Proposed()
    {
        await _s.ArrangeAsync();
        await _s.ReceiveAsync(_s.OpenOrderLineId, 10m, "Quarantine", "LOT-Q", SalesBeforeArrivalScenario.Received.AddDays(-5));
        await _s.ReceiveAsync(_s.OpenOrderLineId, 10m, "Blocked", "LOT-X", SalesBeforeArrivalScenario.Received.AddDays(-5));
        var order = (await _s.CreateAsync(_s.Order((10m, "Stock")))).Value;
        var proposal = await ProposalAsync(order.Id, order.Lines[0].Id);
        Assert.Equal(new[] { _s.StockContentId }, proposal.Candidates.Select(c => c.PalletContentLineId));
    }

    [Fact]
    public async Task A_Batch_Is_All_Or_Nothing_And_Never_Exceeds_The_Line()
    {
        await _s.ArrangeAsync();
        var other = await _s.ReceiveAsync(_s.OpenOrderLineId, 20m, "Approved", "LOT-B");
        var order = (await _s.CreateAsync(_s.Order((30m, "Expected")))).Value;
        var tooMuch = await ReserveBatchAsync(order.Id, order.Lines[0].Id, Batch(Guid.NewGuid(), (_s.StockContentId, 20m, 120m), (other, 15m, 90m)));
        Assert.Equal(ServiceResultStatus.Conflict, tooMuch.Status);
        Assert.StartsWith("items[1]:", tooMuch.Message);
        await using var check = _s.Factory.CreateContext();
        Assert.Equal(0, await check.StockReservations.CountAsync());
        Assert.Equal(0, await check.PalletAuditEvents.CountAsync(e => e.EventType == "ReservationCreated"));
        Assert.Equal(ServiceResultStatus.Validation, (await ReserveBatchAsync(order.Id, order.Lines[0].Id, Batch(Guid.NewGuid(), (other, 5m, 30m), (other, 5m, 30m)))).Status);
    }

    [Fact]
    public async Task Replaying_A_Batch_With_Its_RequestId_Returns_The_Same_Reservations_And_A_Different_Payload_Is_409()
    {
        await _s.ArrangeAsync();
        var order = (await _s.CreateAsync(_s.Order((20m, "Stock")))).Value;
        var requestId = Guid.NewGuid();
        var first = await ReserveBatchAsync(order.Id, order.Lines[0].Id, Batch(requestId, (_s.StockContentId, 20m, 120m)));
        Assert.Equal(ServiceResultStatus.Created, first.Status);

        var replay = await ReserveBatchAsync(order.Id, order.Lines[0].Id, Batch(requestId, (_s.StockContentId, 20m, 120m)));
        Assert.Equal(ServiceResultStatus.Ok, replay.Status);
        Assert.Equal(first.Value.Select(r => r.Id), replay.Value.Select(r => r.Id));
        Assert.Equal(ServiceResultStatus.Conflict, (await ReserveBatchAsync(order.Id, order.Lines[0].Id, Batch(requestId, (_s.StockContentId, 19m, 114m)))).Status);
        await using var check = _s.Factory.CreateContext();
        Assert.Equal(1, await check.StockReservations.CountAsync());
    }

    [Fact]
    public async Task Two_Concurrent_Fifo_Reservations_On_The_Same_Pallet_Have_Exactly_One_Winner()
    {
        await _s.ArrangeAsync();
        var a = (await _s.CreateAsync(_s.Order((30m, "Stock")))).Value;
        var b = (await _s.CreateAsync(_s.Order((10m, "Stock")))).Value;
        ServiceResultStatus inner = default;
        var concurrent = new ConcurrentSaveInterceptor(async () =>
            inner = (await ReserveBatchAsync(b.Id, b.Lines[0].Id, Batch(Guid.NewGuid(), (_s.StockContentId, 10m, 60m)))).Status);
        await using var db = _s.Factory.CreateContext(concurrent);
        var outer = await new StockReservationService(db).ReserveBatchAsync(a.Id, a.Lines[0].Id, Batch(Guid.NewGuid(), (_s.StockContentId, 30m, 180m)));

        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Conflict), (inner, outer.Status));
        await using var check = _s.Factory.CreateContext();
        Assert.Equal(10m, (await check.StockReservations.Where(r => r.Status == "Active").ToListAsync()).Sum(r => r.Quantity));
    }

    [Fact]
    public async Task A_Pallet_Of_A_Variant_With_Almost_The_Same_Name_Is_Never_Reserved()
    {
        await _s.ArrangeAsync();
        var order = (await _s.CreateAsync(_s.Order(_s.LookalikeId, (15m, "Stock")))).Value;
        var result = await ReserveBatchAsync(order.Id, order.Lines[0].Id, Batch(Guid.NewGuid(), (_s.StockContentId, 5m, 30m)));
        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains("different product variant", result.Message);
        Assert.DoesNotContain((await ProposalAsync(order.Id, order.Lines[0].Id)).Candidates, c => c.PalletContentLineId == _s.StockContentId);
    }

    [Fact]
    public async Task The_Worklist_Shows_A_Line_Sold_Before_Arrival_Once_Approved_Stock_Is_Free_And_Never_Reserves_By_Itself()
    {
        await _s.ArrangeAsync();
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(_s.Order((40m, "Stock")))).Status);
        var early = (await _s.CreateAsync(_s.Order((15m, "Expected")))).Value;
        await using (var db = _s.Factory.CreateContext())
        {
            Assert.Empty((await new SalesOrderService(db).GetAwaitingStockAsync()).Value);
        }

        // De 20 komen binnen in quarantaine: nog niets te reserveren; na goedkeuring wel (15 van de 20).
        var arrived = await _s.ReceiveAsync(_s.OpenOrderLineId, 20m, "Quarantine", "LOT-B");
        await using (var db = _s.Factory.CreateContext())
        {
            Assert.Empty((await new SalesOrderService(db).GetAwaitingStockAsync()).Value);
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == arrived);
            content.QualityStatus = "Approved";
            await db.SaveChangesAsync();
        }
        await using (var db = _s.Factory.CreateContext())
        {
            var item = Assert.Single((await new SalesOrderService(db).GetAwaitingStockAsync()).Value);
            Assert.Equal((early.Id, early.OrderNumber, "PO-778", 15m, 15m), (item.SalesOrderId, item.OrderNumber, item.CustomerReference, item.UnreservedQuantity, item.ReservableNow));
            Assert.Equal(0, await db.StockReservations.CountAsync());
        }

        // Pas na bevestigen is de regel gereserveerd en verdwijnt hij uit de werklijst.
        var proposal = await ProposalAsync(early.Id, early.Lines[0].Id);
        Assert.Equal(ServiceResultStatus.Created, (await ReserveBatchAsync(early.Id, early.Lines[0].Id, new CreateStockReservationBatchDto
        {
            RequestId = Guid.NewGuid(), Unit = "colli", Items = proposal.Items
        })).Status);
        await using (var db = _s.Factory.CreateContext())
        {
            Assert.Empty((await new SalesOrderService(db).GetAwaitingStockAsync()).Value);
            Assert.False((await new SalesOrderService(db).GetAsync(early.Id)).Value.AwaitingStock);
        }
    }
}
