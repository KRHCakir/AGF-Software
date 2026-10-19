using AgfAdmin.Data;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Infrastructure;

// Gedeelde, expliciete testdata voor verkooporders en reserveringen (rechtstreeks opgeslagen; alle versies starten op 0).
//   - Warehouse-pallet P-WH (Received/Warehouse/COLD-01): Approved 40 box / 160 kg (LOT-A), Approved 10 colli / 55 kg (LOT-B),
//     Quarantine 5 box / 20 kg (LOT-Q), Blocked 6 box / 24 kg (LOT-X), Rejected 2 box / 8 kg (LOT-R).
//   - Tweede Warehouse-pallet P-WH2 (Received/Warehouse): Approved 20 box / 80 kg (LOT-C).
//   - Pallet P-IN (InTransit): Approved 7 box / 28 kg (LOT-T).
//   - Pallet P-XD (Received/Crossdock): Approved 3 box / 12 kg (LOT-D, Warehouse-route).
//   - DirectTrade-pallet P-DT (Received/Crossdock): Approved 9 box / 36 kg (LOT-DT) voor klant 1.
//   - Variant Avocado Hass (box en colli) en Mango Kent (box). Klanten: 1 "Customer T", 2 "Customer U".
public sealed record SalesSeed(
    int CustomerId,
    int OtherCustomerId,
    int AvocadoId,
    int MangoId,
    int WarehousePalletId,
    int SecondWarehousePalletId,
    int ApprovedBox,
    int ApprovedColli,
    int QuarantineBox,
    int BlockedBox,
    int RejectedBox,
    int SecondPalletApprovedBox,
    int InTransitBox,
    int CrossdockBox,
    int DirectTradeBox,
    int LegacyOrderLineId);

public static class SalesTestData
{
    public static async Task<SalesSeed> SeedAsync(SqliteAppDbContextFactory factory)
    {
        await using var db = factory.CreateContext();
        var product = new Product { Name = "Avocado" };
        var avocado = new ProductVariant { Product = product, Name = "Hass", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.5m };
        var mango = new ProductVariant { Product = new Product { Name = "Mango" }, Name = "Kent", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 9m };
        var vendor = new Vendor { Name = "Supplier B" };
        var customer = new Customer { Name = "Customer T" };
        var otherCustomer = new Customer { Name = "Customer U" };

        SupplyCommitment Commitment(string route, Customer bound) => new()
        {
            RequestId = Guid.NewGuid(),
            Vendor = vendor,
            Customer = bound,
            Route = route,
            Status = "Expected",
            Origin = "Peru",
            Destination = bound == null ? null : "Turkije",
            ExpectedAt = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc)
        };
        SupplyCommitmentLine Line(SupplyCommitment commitment, ProductVariant variant, string unit) => new()
        {
            SupplyCommitment = commitment,
            ProductVariant = variant,
            ExpectedQuantity = 100m,
            ExpectedUnit = unit
        };

        var warehouse = Commitment("Warehouse", null);
        var avocadoBox = Line(warehouse, avocado, "box");
        var avocadoColli = Line(warehouse, avocado, "colli");
        var direct = Commitment("DirectTrade", customer);
        var directBox = Line(direct, avocado, "box");

        Pallet NewPallet(string reference, string status, string location, string bin = null) =>
            new() { Reference = reference, LogisticsStatus = status, LocationContext = location, WarehouseLocation = bin };
        PalletContentLine Content(Pallet pallet, SupplyCommitmentLine source, decimal quantity, decimal kg, string quality, string lot) => new()
        {
            RequestId = Guid.NewGuid(),
            Pallet = pallet,
            SupplyCommitmentLine = source,
            LotNumber = lot,
            Quantity = quantity,
            Unit = source.ExpectedUnit,
            NetWeightKg = kg,
            QualityStatus = quality
        };

        var wh = NewPallet("P-WH", "Received", "Warehouse", "COLD-01");
        var wh2 = NewPallet("P-WH2", "Received", "Warehouse");
        var inTransit = NewPallet("P-IN", "InTransit", "InTransit");
        var crossdock = NewPallet("P-XD", "Received", "Crossdock");
        var directPallet = NewPallet("P-DT", "Received", "Crossdock");

        var approvedBox = Content(wh, avocadoBox, 40m, 160m, "Approved", "LOT-A");
        var approvedColli = Content(wh, avocadoColli, 10m, 55m, "Approved", "LOT-B");
        var quarantineBox = Content(wh, avocadoBox, 5m, 20m, "Quarantine", "LOT-Q");
        var blockedBox = Content(wh, avocadoBox, 6m, 24m, "Blocked", "LOT-X");
        var rejectedBox = Content(wh, avocadoBox, 2m, 8m, "Rejected", "LOT-R");
        var secondBox = Content(wh2, avocadoBox, 20m, 80m, "Approved", "LOT-C");
        var transitBox = Content(inTransit, avocadoBox, 7m, 28m, "Approved", "LOT-T");
        var crossdockBox = Content(crossdock, avocadoBox, 3m, 12m, "Approved", "LOT-D");
        var directContent = Content(directPallet, directBox, 9m, 36m, "Approved", "LOT-DT");

        var legacyOrder = new Order { CustomCustomerName = "Legacy" };
        var legacyLine = new OrderLine { Order = legacyOrder, ProductVariant = avocado, Quantity = 1, UnitPrice = 12.5m };

        db.AddRange(product, avocado, mango, vendor, customer, otherCustomer, warehouse, avocadoBox, avocadoColli, direct, directBox,
            wh, wh2, inTransit, crossdock, directPallet,
            approvedBox, approvedColli, quarantineBox, blockedBox, rejectedBox, secondBox, transitBox, crossdockBox, directContent,
            legacyOrder, legacyLine);
        await db.SaveChangesAsync();

        return new SalesSeed(customer.Id, otherCustomer.Id, avocado.Id, mango.Id, wh.Id, wh2.Id,
            approvedBox.Id, approvedColli.Id, quarantineBox.Id, blockedBox.Id, rejectedBox.Id, secondBox.Id,
            transitBox.Id, crossdockBox.Id, directContent.Id, legacyLine.Id);
    }

    // Een open verkooporder rechtstreeks in de database (zonder service), voor model- en guardtests.
    public static async Task<(int OrderId, int BoxLineId, int ColliLineId)> SeedOrderAsync(SqliteAppDbContextFactory factory, SalesSeed seed, decimal boxQuantity = 50m, decimal colliQuantity = 10m)
    {
        await using var db = factory.CreateContext();
        var order = new SalesOrder
        {
            RequestId = Guid.NewGuid(),
            CustomerId = seed.CustomerId,
            Status = "Open",
            RequestedDeliveryDate = new DateTime(2026, 10, 23, 6, 0, 0, DateTimeKind.Utc),
            ActorReference = "office-1"
        };
        var box = new SalesOrderLine { SalesOrder = order, ProductVariantId = seed.AvocadoId, Quantity = boxQuantity, Unit = "box" };
        var colli = new SalesOrderLine { SalesOrder = order, ProductVariantId = seed.AvocadoId, Quantity = colliQuantity, Unit = "colli" };
        db.AddRange(order, box, colli);
        await db.SaveChangesAsync();
        return (order.Id, box.Id, colli.Id);
    }

    public static async Task<int> SupplyLineOfAsync(AppDbContext db, int contentLineId) =>
        (await db.PalletContentLines.FindAsync(contentLineId))!.SupplyCommitmentLineId;

    // Volledige afboeking van één Active reservering rechtstreeks in de database (zoals de dispatch-service die schrijft):
    // Consumed + ConsumedAt, Dispatch-mutatie, verzending met regel en de verhoging van afgeboekt op de inhoudsregel.
    // Alleen zo accepteert guard-regel 8 een Consumed; een losse statuswijziging wordt geweigerd.
    public static async Task<int> ConsumeAsync(SqliteAppDbContextFactory factory, int reservationId, string reference = null)
    {
        await using var db = factory.CreateContext();
        var dispatch = await ConsumeInContextAsync(db, reservationId, reference);
        await db.SaveChangesAsync();
        return dispatch.Id;
    }

    // Zelfde volledige afboeking, maar in een meegegeven context (voor race-tests met een interceptor); slaat niet zelf op.
    // `into`: bestaande (nieuwe, gevolgde) verzending om de regel aan toe te voegen; `intoDispatchId`: alleen-FK-verwijzing naar een opgeslagen verzending.
    // `fkOnly`: de verzendregel verwijst naar reservering en inhoudsregel alleen via FK (geen navigaties), zoals een externe writer dat zou doen.
    public static async Task<SalesDispatch> ConsumeInContextAsync(AppDbContext db, int reservationId, string reference = null, SalesDispatch into = null, int? intoDispatchId = null, bool fkOnly = false)
    {
        var reservation = await db.StockReservations.Include(r => r.PalletContentLine).Include(r => r.SalesOrderLine).SingleAsync(r => r.Id == reservationId);
        var now = DateTime.UtcNow;
        var content = reservation.PalletContentLine;
        reservation.Status = "Consumed";
        reservation.ConsumedAt = now;
        content.DispatchedQuantity += reservation.Quantity;
        content.DispatchedNetWeightKg += reservation.NetWeightKg ?? 0m;
        var movement = new StockMovement
        {
            Type = "Dispatch", Quantity = reservation.Quantity, Unit = reservation.Unit, NetWeightKg = reservation.NetWeightKg,
            SupplyCommitmentLineId = content.SupplyCommitmentLineId, PalletContentLine = content, StockReservation = reservation, CreatedAt = now
        };
        var line = fkOnly
            ? new SalesDispatchLine
            {
                StockReservationId = reservation.Id, SalesOrderLineId = reservation.SalesOrderLineId!.Value, PalletContentLineId = content.Id,
                Quantity = reservation.Quantity, Unit = reservation.Unit, NetWeightKg = reservation.NetWeightKg ?? 0m, ExpectedReservationVersion = reservation.Version, StockMovement = movement
            }
            : new SalesDispatchLine
            {
                StockReservation = reservation, SalesOrderLineId = reservation.SalesOrderLineId!.Value, PalletContentLine = content,
                Quantity = reservation.Quantity, Unit = reservation.Unit, NetWeightKg = reservation.NetWeightKg ?? 0m, ExpectedReservationVersion = reservation.Version, StockMovement = movement
            };
        db.StockMovements.Add(movement);
        if (intoDispatchId != null)
        {
            line.SalesDispatchId = intoDispatchId.Value;
            db.SalesDispatchLines.Add(line);
            return null;
        }
        var dispatch = into ?? new SalesDispatch { RequestId = Guid.NewGuid(), SalesOrderId = reservation.SalesOrderLine.SalesOrderId, Reference = reference, ActorReference = "test", DispatchedAt = now };
        dispatch.Lines.Add(line);
        if (into == null)
        {
            db.SalesDispatches.Add(dispatch);
        }
        return dispatch;
    }
}
