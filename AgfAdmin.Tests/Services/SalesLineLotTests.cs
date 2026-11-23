using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: verkopen per partij. Op de orderregel een gevraagde partij (optioneel); leeg = de oudste partij eerst (FIFO), zoals
// altijd. De partijkeuze toont per partij het vrije aantal, maat, herkomst, locatie en ontvangstdatum. Een regel mag uit meer partijen komen.
// Bij het picken blijft de partij aan te passen: vrijgeven en opnieuw reserveren uit de andere partij, in één opslag met auditregel.
public sealed class SalesLineLotTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();
    private int _oldLot, _newLot, _oldContent, _newContent, _secondNewContent;

    public SalesLineLotTests() => _s.Factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 23));

    public void Dispose() => _s.Dispose();

    // Twee inkoop-partijen tomaten in de loods: I-2611001 (oud, 30 colli op één pallet) en I-2611002 (nieuw, 25 + 10 colli op twee pallets).
    private async Task ArrangeAsync()
    {
        await _s.ArrangeAsync();
        await using var db = _s.Factory.CreateContext();
        var location = new StockLocation { Name = "Koelhuis Noord", Kind = "ExternalStorage", CreatedBy = "test" };
        (int Lot, int[] Contents) Lot(string number, DateTime receivedAt, params decimal[] pallets)
        {
            var lot = new Partij { Kind = PartijRules.KindPurchase, Number = number, NumberKey = PartijRules.NumberKey(number), CreatedBy = "test" };
            var order = new SupplyCommitment { RequestId = Guid.NewGuid(), VendorId = _s.VendorId, Route = "Warehouse", Status = "Received", Origin = "Spanje", Partij = lot, ExpectedAt = receivedAt };
            var line = new SupplyCommitmentLine { SupplyCommitment = order, ProductVariantId = _s.TomatoId, ExpectedQuantity = pallets.Sum(), ExpectedUnit = "colli", Partij = lot };
            var contents = pallets.Select((quantity, i) => new PalletContentLine
            {
                RequestId = Guid.NewGuid(), SupplyCommitmentLine = line, Partij = lot, LotNumber = number, Quantity = quantity, Unit = "colli", NetWeightKg = quantity * 6m,
                QualityStatus = "Approved", CreatedAt = receivedAt.AddMinutes(i),
                Pallet = new Pallet { Reference = $"{number}-P{i + 1}", LogisticsStatus = "Received", LocationContext = "Warehouse", WarehouseLocation = $"B-0{i + 1}", StockLocation = i == 1 ? location : null },
            }).ToList();
            db.AddRange(lot, order, line);
            db.AddRange(contents);
            db.SaveChanges();
            return (lot.Id, contents.Select(c => c.Id).ToArray());
        }
        var older = Lot("I-2611001", new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc), 30m);
        var newer = Lot("I-2611002", new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc), 25m, 10m);
        (_oldLot, _oldContent, _newLot, _newContent, _secondNewContent) = (older.Lot, older.Contents[0], newer.Lot, newer.Contents[0], newer.Contents[1]);
    }

    private CreateSalesOrderDto Order(decimal quantity, int? partijId)
    {
        var request = _s.Order((quantity, "Stock"));
        request.Lines[0].PartijId = partijId;
        return request;
    }

    private async Task<FifoReservationProposalDto> FifoAsync(int orderId, int lineId, int? partijId = null)
    {
        await using var db = _s.Factory.CreateContext();
        return (await new SalesOrderService(db).GetFifoProposalAsync(orderId, lineId, partijId)).Value;
    }

    private async Task<IReadOnlyList<StockReservationDto>> ReserveAsync(int orderId, int lineId, FifoReservationProposalDto proposal)
    {
        await using var db = _s.Factory.CreateContext();
        var result = await new StockReservationService(db).ReserveBatchAsync(orderId, lineId, new CreateStockReservationBatchDto
        {
            RequestId = Guid.NewGuid(), Unit = "colli",
            Items = proposal.Items.Select(i => new FifoReservationItemDto { PalletContentLineId = i.PalletContentLineId, Quantity = i.Quantity, NetWeightKg = i.NetWeightKg }).ToList()
        });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    [Fact]
    public async Task A_Line_Can_Ask_For_A_Lot_And_The_Replay_Compares_It()
    {
        await ArrangeAsync();
        var request = Order(20m, _newLot);
        var created = await _s.CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal((_newLot, "I-2611002"), (created.Value.Lines[0].PartijId, created.Value.Lines[0].PartijNumber));
        Assert.Equal(ServiceResultStatus.Ok, (await _s.CreateAsync(request)).Status);
        request.Lines[0].PartijId = _oldLot;
        Assert.Equal(ServiceResultStatus.Conflict, (await _s.CreateAsync(request)).Status);
    }

    [Fact]
    public async Task A_Sales_Lot_Or_A_Lot_Without_This_Article_Is_Refused()
    {
        await ArrangeAsync();
        var sale = await _s.CreateAsync(Order(5m, null));
        var salesLot = await _s.CreateAsync(Order(5m, sale.Value.SalesPartijId));
        Assert.Equal(ServiceResultStatus.Validation, salesLot.Status);
        Assert.Contains("lines[0].partijId", salesLot.Errors.Keys);
        int otherLot;
        await using (var db = _s.Factory.CreateContext())
        {
            var lot = new Partij { Kind = PartijRules.KindPurchase, Number = "I-2611099", NumberKey = "I2611099", CreatedBy = "test" };
            db.Partijen.Add(lot);
            await db.SaveChangesAsync();
            otherLot = lot.Id;
        }
        var empty = await _s.CreateAsync(Order(5m, otherLot));
        Assert.Equal(ServiceResultStatus.Validation, empty.Status);
        Assert.Contains("no stock or expected supply", empty.Errors["lines[0].partijId"].Single());
    }

    [Fact]
    public async Task The_Lot_Choice_Shows_Free_Quantity_Size_Origin_Location_And_Receipt_Date_Oldest_First()
    {
        await ArrangeAsync();
        await using var db = _s.Factory.CreateContext();
        var options = (await new SalesOrderService(db).GetLotOptionsAsync(_s.TomatoId, "colli")).Value;
        // LOT-A uit de opzet (zonder partij, alleen ter informatie) en de twee partijen, oudste ontvangst eerst.
        var lots = options.Where(o => o.PartijId != null).ToList();
        Assert.Equal(new[] { "I-2611001", "I-2611002" }, lots.Select(o => o.Number));
        Assert.Equal((30m, 1), (lots[0].AvailableQuantity, lots[0].PalletCount));
        Assert.Equal((35m, 2), (lots[1].AvailableQuantity, lots[1].PalletCount));
        Assert.Equal(new[] { null, "Koelhuis Noord" }, lots[1].Locations);
        Assert.Equal(new[] { "B-01", "B-02" }, lots[1].Bins);
        Assert.Equal(("NL", "Kweker K"), (lots[1].Origin, lots[1].VendorName));
        Assert.Contains(options, o => o.PartijId == null && o.Number == "LOT-A");
        Assert.Equal(ServiceResultStatus.Validation, (await new SalesOrderService(db).GetLotOptionsAsync(_s.TomatoId, "kg")).Status);
    }

    [Fact]
    public async Task Without_A_Lot_FIFO_Takes_The_Oldest_With_A_Lot_Only_That_Lot_And_A_Line_Can_Come_From_Two_Lots()
    {
        await ArrangeAsync();
        var free = (await _s.CreateAsync(Order(30m, null))).Value;
        var oldest = await FifoAsync(free.Id, free.Lines[0].Id);
        Assert.Equal(oldest.Candidates.OrderBy(c => c.ReceivedAt).First().PalletContentLineId, oldest.Items[0].PalletContentLineId);

        var asked = (await _s.CreateAsync(Order(30m, _newLot))).Value;
        var fromLot = await FifoAsync(asked.Id, asked.Lines[0].Id);
        Assert.All(fromLot.Candidates, c => Assert.Equal(_newLot, c.PartijId));
        Assert.Equal(new[] { (_newContent, 25m), (_secondNewContent, 5m) }, fromLot.Items.Select(i => (i.PalletContentLineId, i.Quantity)));

        // Verdeeld over twee partijen: 10 uit de oude partij (gekozen in de partijkeuze), de rest uit de gevraagde partij.
        var split = (await _s.CreateAsync(Order(30m, null))).Value;
        await ReserveAsync(split.Id, split.Lines[0].Id, await FifoAsync(split.Id, split.Lines[0].Id, _oldLot) is var p && p.Items.Count > 0
            ? new FifoReservationProposalDto { Items = new List<FifoReservationItemDto> { new() { PalletContentLineId = p.Items[0].PalletContentLineId, Quantity = 10m, NetWeightKg = 60m } } }
            : p);
        await ReserveAsync(split.Id, split.Lines[0].Id, await FifoAsync(split.Id, split.Lines[0].Id, _newLot));
        await using var db = _s.Factory.CreateContext();
        var lots = await db.StockReservations.Where(r => r.SalesOrderLine.SalesOrderId == split.Id && r.Status == "Active")
            .Select(r => r.PalletContentLine.Partij.Number).Distinct().OrderBy(n => n).ToListAsync();
        Assert.Equal(new[] { "I-2611001", "I-2611002" }, lots);
    }

    [Fact]
    public async Task Changing_The_Lot_At_Picking_Releases_And_Reserves_In_One_Save_With_An_Audit_Line()
    {
        await ArrangeAsync();
        var order = (await _s.CreateAsync(Order(20m, null))).Value;
        var reserved = (await ReserveAsync(order.Id, order.Lines[0].Id, await FifoAsync(order.Id, order.Lines[0].Id, _oldLot))).Single();
        var key = Guid.NewGuid();
        await using (var db = _s.Factory.CreateContext())
        {
            var changed = await new StockReservationService(db).ChangeLotAsync(reserved.Id, new ChangeReservationLotDto { RequestId = key, ExpectedReservationVersion = reserved.Version, PartijId = _newLot });
            Assert.Equal(ServiceResultStatus.Created, changed.Status);
            Assert.Equal((20m, _newContent), (changed.Value.Sum(r => r.Quantity), changed.Value.Single().PalletContentLineId));
        }
        await using (var db = _s.Factory.CreateContext())
        {
            var replay = await new StockReservationService(db).ChangeLotAsync(reserved.Id, new ChangeReservationLotDto { RequestId = key, ExpectedReservationVersion = reserved.Version, PartijId = _newLot });
            Assert.Equal(ServiceResultStatus.Ok, replay.Status);
            Assert.Single(replay.Value);
        }
        await using var verify = _s.Factory.CreateContext();
        var old = await verify.StockReservations.SingleAsync(r => r.Id == reserved.Id);
        Assert.Equal(("Released", "Partij gewijzigd: I-2611001 → I-2611002"), (old.Status, old.ReleaseReason));
        var audit = await verify.SalesOrderAuditEvents.SingleAsync(e => e.EventType == StockReservationService.ReservationLotChangedEvent);
        Assert.Contains("I-2611002", audit.NewValue);
        Assert.Equal(20m, await verify.StockReservations.Where(r => r.SalesOrderLineId == order.Lines[0].Id && r.Status == "Active").SumAsync(r => r.Quantity));
    }

    [Fact]
    public async Task A_Lot_Without_Enough_Free_Stock_An_Unknown_Lot_Or_The_Same_Lot_Changes_Nothing()
    {
        await ArrangeAsync();
        // De oude partij heeft nog 10 vrij: een andere order reserveert er 20.
        var other = (await _s.CreateAsync(Order(20m, null))).Value;
        await ReserveAsync(other.Id, other.Lines[0].Id, await FifoAsync(other.Id, other.Lines[0].Id, _oldLot));
        var order = (await _s.CreateAsync(Order(25m, _newLot))).Value;
        var reserved = (await ReserveAsync(order.Id, order.Lines[0].Id, await FifoAsync(order.Id, order.Lines[0].Id))).Single();
        Assert.Equal(25m, reserved.Quantity);

        await using var db = _s.Factory.CreateContext();
        var service = new StockReservationService(db);
        var tooSmall = await service.ChangeLotAsync(reserved.Id, new ChangeReservationLotDto { RequestId = Guid.NewGuid(), ExpectedReservationVersion = reserved.Version, PartijId = _oldLot });
        Assert.Equal(ServiceResultStatus.Conflict, tooSmall.Status);
        Assert.Contains("has 10 colli free", tooSmall.Message);
        Assert.Equal(ServiceResultStatus.NotFound, (await service.ChangeLotAsync(reserved.Id, new ChangeReservationLotDto { RequestId = Guid.NewGuid(), ExpectedReservationVersion = reserved.Version, PartijId = 99999 })).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await service.ChangeLotAsync(reserved.Id, new ChangeReservationLotDto { RequestId = Guid.NewGuid(), ExpectedReservationVersion = reserved.Version, PartijId = _newLot })).Status);
        await using var verify = _s.Factory.CreateContext();
        Assert.Equal("Active", (await verify.StockReservations.SingleAsync(r => r.Id == reserved.Id)).Status);
        Assert.False(await verify.SalesOrderAuditEvents.AnyAsync(e => e.EventType == StockReservationService.ReservationLotChangedEvent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Requested_Lot_Of_A_Line_Never_Changes_And_Is_Always_A_Purchase_Lot(bool async)
    {
        await ArrangeAsync();
        var order = (await _s.CreateAsync(Order(5m, _oldLot))).Value;
        await using var db = _s.Factory.CreateContext();
        var line = await db.SalesOrderLines.SingleAsync(l => l.Id == order.Lines[0].Id);
        line.PartijId = _newLot;
        if (async) await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        else Assert.Throws<TraceabilityViolationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        var other = await db.SalesOrderLines.SingleAsync(l => l.Id == order.Lines[0].Id);
        other.PartijId = order.SalesPartijId;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }
}
