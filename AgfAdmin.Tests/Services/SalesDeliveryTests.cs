using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: een verkoop-partij is één levering aan één klant. Elke order heeft een eerste levering met haar V-nummer;
// "Deellevering" maakt een tweede levering met een nieuw V-nummer, een eigen laadbon en transportopdracht. Regel 42 bewaakt de leveringen.
public sealed class SalesDeliveryTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public SalesDeliveryTests() => _factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 23));

    public void Dispose() => _factory.Dispose();

    private sealed record Arranged(SalesSeed Seed, int OrderId, int BoxLine, int First, int Second);

    // Order: 50 doos Avocado, 30 gereserveerd op de eerste pallet en 15 op de tweede; 5 nog open. Bevestigd: laadbon versie 1.
    private async Task<Arranged> ArrangeAsync(bool confirm = true)
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, box, colli) = await SalesTestData.SeedOrderAsync(_factory, seed, boxQuantity: 50m);
        await using (var raw = _factory.CreateContext())
        {
            // Alleen de doosregel: de colliregel valt weg (buiten de guard om, alleen voor de opzet van deze test).
            await raw.Database.ExecuteSqlAsync($"DELETE FROM \"SalesOrderLines\" WHERE \"Id\" = {colli}");
        }
        var first = await ReserveAsync(orderId, box, seed.ApprovedBox, 30m, 120m);
        var second = await ReserveAsync(orderId, box, seed.SecondPalletApprovedBox, 15m, 60m);
        if (confirm)
        {
            await using var db = _factory.CreateContext();
            Assert.Equal(ServiceResultStatus.Ok, (await new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto())).Status);
        }
        return new Arranged(seed, orderId, box, first, second);
    }

    private async Task<int> ReserveAsync(int orderId, int lineId, int contentId, decimal quantity, decimal kg)
    {
        await using var db = _factory.CreateContext();
        var result = await new StockReservationService(db).ReserveAsync(orderId, lineId, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = contentId, Quantity = quantity, Unit = "box", NetWeightKg = kg
        });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value.Id;
    }

    private async Task<ServiceResult<SalesOrderDto>> SplitAsync(int orderId, Guid requestId, params int[] reservations)
    {
        await using var db = _factory.CreateContext();
        var result = await new SalesDeliveryService(db).CreateAsync(orderId, new CreateSalesDeliveryDto { RequestId = requestId, ReservationIds = reservations.ToList() });
        if (result.Status is ServiceResultStatus.Created or ServiceResultStatus.Ok)
        {
            // Zoals de controller (RefreshLoadingSlip): de documenten van de leveringen verversen.
            await new LoadingSlipService(db).RefreshAsync(orderId);
        }
        return result;
    }

    private async Task<List<SalesOrderDocument>> DocumentsAsync(int orderId)
    {
        await using var db = _factory.CreateContext();
        return await db.SalesOrderDocuments.AsNoTracking().Include(d => d.SalesDelivery).Where(d => d.SalesOrderId == orderId)
            .OrderBy(d => d.SalesDelivery.Sequence).ThenBy(d => d.Version).ToListAsync();
    }

    [Fact]
    public async Task A_Partial_Delivery_Gets_A_New_V_Number_And_Its_Own_Loading_Slip_And_The_First_Slip_Gets_A_New_Version()
    {
        var a = await ArrangeAsync();
        var created = await SplitAsync(a.OrderId, Guid.NewGuid(), a.Second);
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        var deliveries = created.Value.Deliveries;
        Assert.Equal(new[] { 1, 2 }, deliveries.Select(d => d.Sequence));
        Assert.NotEqual(deliveries[0].PartijNumber, deliveries[1].PartijNumber);
        Assert.Equal(("V-2611001", "V-2611002"), (deliveries[0].PartijNumber, deliveries[1].PartijNumber));
        Assert.Equal(new[] { a.First }, deliveries[0].ReservationIds);
        Assert.Equal(new[] { a.Second }, deliveries[1].ReservationIds);
        Assert.Equal((false, true), (deliveries[0].Last, deliveries[1].Last));

        var documents = await DocumentsAsync(a.OrderId);
        var first = documents.Where(d => d.SalesDelivery.Sequence == 1).ToList();
        var second = Assert.Single(documents.Where(d => d.SalesDelivery.Sequence == 2));
        Assert.Equal(new[] { 1, 2 }, first.Select(d => d.Version));
        Assert.Equal((1, "Confirmed"), (second.Version, second.Reason));
        Assert.Equal(first[0].Number + "-2", second.Number);

        // Eerste levering: alleen de 30 van de eerste pallet. Tweede levering: de 15 van de tweede pallet plus de 5 die nog open zijn.
        var slip1 = LoadingSlipBuilder.Deserialize(first[1].SnapshotJson);
        var slip2 = LoadingSlipBuilder.Deserialize(second.SnapshotJson);
        Assert.Equal(("V-2611001", 30m), (slip1.Position, slip1.TotalQuantity));
        Assert.Equal(("V-2611002", 20m), (slip2.Position, slip2.TotalQuantity));
        Assert.Contains(slip2.Rows, r => r.Quantity == 5m && r.PalletReference == null);
        Assert.DoesNotContain(slip1.Rows, r => r.PalletReference == null);
        // Emballage gaat mee met de eerste levering.
        Assert.Empty(slip2.Packaging);

        // De auditregel zegt wie en wanneer.
        await using var db = _factory.CreateContext();
        var audit = await db.SalesOrderAuditEvents.SingleAsync(e => e.EventType == SalesDeliveryRules.DeliveryCreatedEvent);
        Assert.Contains("V-2611002", audit.NewValue);
    }

    [Fact]
    public async Task A_Replay_Gives_The_Same_Delivery_And_Another_Request_With_The_Same_Key_Is_A_Conflict()
    {
        var a = await ArrangeAsync();
        var key = Guid.NewGuid();
        Assert.Equal(ServiceResultStatus.Created, (await SplitAsync(a.OrderId, key, a.Second)).Status);
        var replay = await SplitAsync(a.OrderId, key, a.Second);
        Assert.Equal(ServiceResultStatus.Ok, replay.Status);
        Assert.Equal(2, replay.Value.Deliveries.Count);
        Assert.Equal(ServiceResultStatus.Conflict, (await SplitAsync(a.OrderId, key, a.First)).Status);
        await using var db = _factory.CreateContext();
        Assert.Equal(2, await db.SalesDeliveries.CountAsync(d => d.SalesOrderId == a.OrderId));
    }

    [Fact]
    public async Task A_Replay_With_Another_Own_V_Number_Is_A_Conflict_And_The_Same_Number_Is_A_Replay()
    {
        var a = await ArrangeAsync();
        var key = Guid.NewGuid();
        async Task<ServiceResult<SalesOrderDto>> Send(string number)
        {
            await using var db = _factory.CreateContext();
            return await new SalesDeliveryService(db).CreateAsync(a.OrderId, new CreateSalesDeliveryDto { RequestId = key, ReservationIds = new List<int> { a.Second }, PartijNumber = number });
        }
        Assert.Equal(ServiceResultStatus.Created, (await Send("V-EIGEN-1")).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await Send(" v-eigen-1 ")).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await Send("V-EIGEN-2")).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await Send(null)).Status);
        await using var check = _factory.CreateContext();
        Assert.Equal(2, await check.SalesDeliveries.CountAsync(d => d.SalesOrderId == a.OrderId));
    }

    [Fact]
    public async Task Without_Reservations_The_Open_Rest_Moves_And_A_Later_Reservation_Goes_With_The_Last_Delivery()
    {
        var a = await ArrangeAsync(confirm: false);
        var created = await SplitAsync(a.OrderId, Guid.NewGuid());
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal(new[] { a.First, a.Second }, created.Value.Deliveries[0].ReservationIds);
        Assert.Empty(created.Value.Deliveries[1].ReservationIds);

        var later = await ReserveAsync(a.OrderId, a.BoxLine, a.Seed.SecondPalletApprovedBox, 5m, 20m);
        await using var db = _factory.CreateContext();
        var order = (await new SalesOrderService(db).GetAsync(a.OrderId)).Value;
        Assert.Equal(new[] { later }, order.Deliveries[1].ReservationIds);
        Assert.Equal(order.Deliveries[1].Id, order.Lines.SelectMany(l => l.Reservations).Single(r => r.Id == later).SalesDeliveryId);
    }

    [Fact]
    public async Task Shipped_Or_Unknown_Reservations_Get_No_New_Delivery()
    {
        var a = await ArrangeAsync();
        await SalesTestData.ConsumeAsync(_factory, a.First);
        Assert.Equal(ServiceResultStatus.Conflict, (await SplitAsync(a.OrderId, Guid.NewGuid(), a.First)).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await SplitAsync(a.OrderId, Guid.NewGuid(), 9999)).Status);
        await using var db = _factory.CreateContext();
        Assert.Equal(1, await db.SalesDeliveries.CountAsync(d => d.SalesOrderId == a.OrderId));
    }

    [Fact]
    public async Task A_Cancelled_Order_Gets_No_New_Delivery()
    {
        var a = await ArrangeAsync(confirm: false);
        await using (var db = _factory.CreateContext())
        {
            var version = (await new SalesOrderService(db).GetAsync(a.OrderId)).Value.Version;
            Assert.Equal(ServiceResultStatus.Ok, (await new SalesOrderService(db).CancelAsync(a.OrderId, new CancelSalesOrderDto { RequestId = Guid.NewGuid(), ExpectedOrderVersion = version, Reason = "Klant belde af" })).Status);
        }
        Assert.Equal(ServiceResultStatus.Conflict, (await SplitAsync(a.OrderId, Guid.NewGuid())).Status);
    }

    [Fact]
    public async Task A_Partial_Delivery_Has_Its_Own_Loading_Time_And_Plate()
    {
        var a = await ArrangeAsync();
        var deliveries = (await SplitAsync(a.OrderId, Guid.NewGuid(), a.Second)).Value.Deliveries;
        var load = new DateTime(2026, 11, 25, 7, 0, 0, DateTimeKind.Utc);
        await using var db = _factory.CreateContext();
        var result = await new LoadingSlipService(db).UpdateLoadingAsync(a.OrderId, new UpdateSalesOrderLoadingDto { SalesDeliveryId = deliveries[1].Id, LoadAt = load, LicensePlate = "BX-12-YZ" });
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var second = result.Value.Deliveries.Single(d => d.Sequence == 2);
        Assert.Equal((load, "BX-12-YZ"), (second.LoadAt, second.LicensePlate));
        Assert.Null(result.Value.Deliveries.Single(d => d.Sequence == 1).LicensePlate);
        var slip = LoadingSlipBuilder.Deserialize((await DocumentsAsync(a.OrderId)).Where(d => d.SalesDelivery.Sequence == 2).OrderBy(d => d.Version).Last().SnapshotJson);
        Assert.Equal((load, "BX-12-YZ"), (slip.LoadAt, slip.LicensePlate));
    }

    [Fact]
    public async Task Each_Sales_Lot_Shows_Its_Own_Delivery_And_The_Purchase_Lot_Shows_Both_V_Numbers()
    {
        var a = await ArrangeAsync();
        var deliveries = (await SplitAsync(a.OrderId, Guid.NewGuid(), a.Second)).Value.Deliveries;
        await using var db = _factory.CreateContext();
        var service = new PartijService(db);
        var first = (await service.GetAsync(deliveries[0].PartijId)).Value;
        var second = (await service.GetAsync(deliveries[1].PartijId)).Value;
        Assert.Equal((30m, 30m), (first.OrderedQuantity, first.SoldQuantity));
        Assert.Equal((20m, 15m), (second.OrderedQuantity, second.SoldQuantity));
    }

    // Mailconcepten (documenten deel 3) per levering: de nieuwe versie van de eerste laadbon vervangt alleen het concept van die levering.
    [Fact]
    public async Task Each_Delivery_Has_A_Mail_Draft_For_Its_Newest_Loading_Slip()
    {
        var a = await ArrangeAsync();
        await SplitAsync(a.OrderId, Guid.NewGuid(), a.Second);
        await using var db = _factory.CreateContext();
        await new MailDraftService(db).EnsureDraftsAsync(a.OrderId);
        var open = await db.MailDrafts.AsNoTracking().Include(m => m.Document).ThenInclude(d => d.SalesDelivery)
            .Where(m => m.SalesOrderId == a.OrderId && m.Status == DocumentMailRules.StatusDraft && m.DocumentKind == SalesOrderDocumentRules.KindLoadingSlip).ToListAsync();
        Assert.Equal(new[] { (1, 2), (2, 1) }, open.Select(m => (m.Document.SalesDelivery.Sequence, m.Document.Version)).OrderBy(x => x.Item1));
        Assert.Contains(open, m => m.Document.SalesDelivery.Sequence == 2 && m.Subject.Contains("LB-") && m.Subject.Contains("-2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Guard_Keeps_Deliveries_And_Their_Reservations(bool async)
    {
        var a = await ArrangeAsync();
        var deliveries = (await SplitAsync(a.OrderId, Guid.NewGuid(), a.Second)).Value.Deliveries;
        async Task Refused(Action<AppDbContext> change)
        {
            await using var db = _factory.CreateContext();
            await db.SalesDeliveries.LoadAsync();
            await db.StockReservations.LoadAsync();
            change(db);
            if (async) await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
            else Assert.Throws<TraceabilityViolationException>(() => db.SaveChanges());
        }

        await Refused(db => db.SalesDeliveries.Remove(db.SalesDeliveries.Local.Single(d => d.Sequence == 2)));
        await Refused(db => db.SalesDeliveries.Local.Single(d => d.Sequence == 2).Sequence = 3);
        await Refused(db => db.SalesDeliveries.Local.Single(d => d.Sequence == 2).PartijId = deliveries[0].PartijId);
        // Een reservering verhuist niet zonder de auditregel van een nieuwe levering.
        await Refused(db => db.StockReservations.Local.Single(r => r.Id == a.First).SalesDeliveryId = deliveries[1].Id);
        // Een levering met een partij van een andere order, of met een gat in het volgnummer, kan niet ontstaan.
        await Refused(db =>
        {
            var lot = new Partij { Kind = PartijRules.KindSales, Number = "V-LOS", NumberKey = "VLOS", CreatedBy = "test", SalesOrderId = a.OrderId };
            db.Partijen.Add(lot);
            db.SalesDeliveries.Add(new SalesDelivery { SalesOrderId = a.OrderId, Sequence = 4, Partij = lot, CreatedBy = "test" });
        });
        await Refused(db => db.Partijen.Add(new Partij { Kind = PartijRules.KindSales, Number = "V-ZONDER", NumberKey = "VZONDER", CreatedBy = "test", SalesOrderId = a.OrderId }));

        // Wat verzonden is, blijft bij zijn levering.
        await SalesTestData.ConsumeAsync(_factory, a.First);
        await Refused(db =>
        {
            db.StockReservations.Local.Single(r => r.Id == a.First).SalesDeliveryId = deliveries[1].Id;
            db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent { SalesOrderId = a.OrderId, EventType = SalesDeliveryRules.DeliveryCreatedEvent, OccurredAt = DateTime.UtcNow, RequestId = Guid.NewGuid() });
        });
    }
}
