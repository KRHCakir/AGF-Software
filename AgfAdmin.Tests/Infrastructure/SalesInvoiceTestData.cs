using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Infrastructure;

// Verkoopfactuur 1: een verkooporder met prijzen die in twee leveringen is verzonden, om uit te factureren.
public static class SalesInvoiceTestData
{
    public sealed record Arranged(SalesSeed Seed, int OrderId, int BoxLine, int ServiceLine, int PackagingLine, int FirstDelivery, int SecondDelivery,
        int FirstReservation, int SecondReservation);

    // Order voor Customer T: 50 doos Avocado à 12,50 met 10% korting en 9% btw, handling 1 × 25,00 (21%) en 2 pallets wegwerp à 7,50 (21%).
    // Eerste levering: 30 doos van pallet P-WH (120 kg, partij LOT-A), verzonden. Tweede levering (deellevering): 15 doos van P-WH2
    // (60 kg, partij LOT-C), verzonden als shipSecond.
    public static async Task<Arranged> ArrangeAsync(SqliteAppDbContextFactory factory, bool shipSecond = true, SalesSeed seed = null)
    {
        seed ??= await SalesTestData.SeedAsync(factory);
        int orderId, boxLine, serviceLine, packagingLine;
        await using (var db = factory.CreateContext())
        {
            var handling = new CostArticle { Name = "Handling", NameKey = "handling", Kind = CostArticleRules.KindCost, VatPercentage = 21m, CreatedBy = "test" };
            var pallet = new CostArticle { Name = "Pallet wegwerp", NameKey = "pallet wegwerp", Kind = CostArticleRules.KindPackaging, VatPercentage = 21m, CreatedBy = "test" };
            var order = new SalesOrder
            {
                RequestId = Guid.NewGuid(), CustomerId = seed.CustomerId, Status = "Open", CustomerReference = "PO-778",
                RequestedDeliveryDate = new DateTime(2026, 11, 20, 6, 0, 0, DateTimeKind.Utc), ActorReference = "office-1"
            };
            var box = new SalesOrderLine { SalesOrder = order, ProductVariantId = seed.AvocadoId, Quantity = 50m, Unit = "box", UnitPrice = 12.50m, DiscountPercentage = 10m, VatPercentage = 9m };
            var service = new SalesOrderServiceLine { SalesOrder = order, CostArticle = handling, Quantity = 1m, UnitPrice = 25m, VatPercentage = 21m };
            var packaging = new SalesOrderPackagingLine { SalesOrder = order, CostArticle = pallet, Quantity = 2, UnitPrice = 7.50m, VatPercentage = 21m };
            db.AddRange(handling, pallet, order, box, service, packaging);
            await db.SaveChangesAsync();
            (orderId, boxLine, serviceLine, packagingLine) = (order.Id, box.Id, service.Id, packaging.Id);
        }
        var first = await ReserveAsync(factory, orderId, boxLine, seed.ApprovedBox, 30m, 120m);
        var second = await ReserveAsync(factory, orderId, boxLine, seed.SecondPalletApprovedBox, 15m, 60m);
        await using (var db = factory.CreateContext())
        {
            var split = await new SalesDeliveryService(db).CreateAsync(orderId, new CreateSalesDeliveryDto { RequestId = Guid.NewGuid(), ReservationIds = new List<int> { second } });
            if (split.Status != ServiceResultStatus.Created) throw new InvalidOperationException($"Partial delivery failed: {split.Status}");
        }
        await SalesTestData.ConsumeAsync(factory, first);
        if (shipSecond) await SalesTestData.ConsumeAsync(factory, second);
        await using var read = factory.CreateContext();
        var deliveries = await read.SalesDeliveries.Where(d => d.SalesOrderId == orderId).OrderBy(d => d.Sequence).Select(d => d.Id).ToListAsync();
        return new Arranged(seed, orderId, boxLine, serviceLine, packagingLine, deliveries[0], deliveries[1], first, second);
    }

    public static async Task<int> ReserveAsync(SqliteAppDbContextFactory factory, int orderId, int lineId, int contentId, decimal quantity, decimal kg, string unit = "box")
    {
        await using var db = factory.CreateContext();
        var result = await new StockReservationService(db).ReserveAsync(orderId, lineId, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = contentId, Quantity = quantity, Unit = unit, NetWeightKg = kg
        });
        if (result.Status != ServiceResultStatus.Created) throw new InvalidOperationException($"Reservation failed: {result.Status}");
        return result.Value.Id;
    }

    public static async Task<ServiceResult<SalesInvoiceDto>> FromDeliveriesAsync(SqliteAppDbContextFactory factory, Guid requestId, params int[] deliveryIds)
    {
        await using var db = factory.CreateContext();
        return await new SalesInvoiceService(db).CreateFromDeliveriesAsync(new CreateInvoiceFromDeliveriesDto { RequestId = requestId, DeliveryIds = deliveryIds.ToList() });
    }
}

// Verkoopfactuur 1, directe factuur: partij I-2611001 op pallet P-WH (40 doos, 160 kg) en I-2611002 op P-WH2 (20 doos, 80 kg).
public static class DirectInvoiceTestData
{
    public sealed record Arranged(SalesSeed Seed, int LotA, int LotB);

    public static async Task<Arranged> ArrangeAsync(SqliteAppDbContextFactory factory)
    {
        var seed = await SalesTestData.SeedAsync(factory);
        await using var db = factory.CreateContext();
        var a = new Partij { Number = "I-2611001", NumberKey = PartijRules.NumberKey("I-2611001"), CreatedBy = "test" };
        var b = new Partij { Number = "I-2611002", NumberKey = PartijRules.NumberKey("I-2611002"), CreatedBy = "test" };
        db.Partijen.AddRange(a, b);
        await db.SaveChangesAsync();
        // Opzet buiten de guard om: de inhoud van de twee pallets hoort bij deze partijen.
        await db.Database.ExecuteSqlAsync($"UPDATE \"PalletContentLines\" SET \"PartijId\" = {a.Id} WHERE \"Id\" = {seed.ApprovedBox}");
        await db.Database.ExecuteSqlAsync($"UPDATE \"PalletContentLines\" SET \"PartijId\" = {b.Id} WHERE \"Id\" = {seed.SecondPalletApprovedBox}");
        return new Arranged(seed, a.Id, b.Id);
    }

    public static SalesInvoiceNewLineDto Product(int variantId, int? lotId, decimal quantity, decimal? price = 12m, decimal? vat = 9m) => new()
    {
        LineKind = SalesInvoiceRules.LineProduct, ProductVariantId = variantId, PartijId = lotId, Unit = "box", Quantity = quantity, UnitPrice = price, VatPercentage = vat
    };

    public static SalesInvoiceNewLineDto Service(string description, decimal price, decimal vat = 21m) => new()
    {
        LineKind = SalesInvoiceRules.LineService, Description = description, Quantity = 1m, UnitPrice = price, VatPercentage = vat
    };

    public static async Task<ServiceResult<SalesInvoiceDto>> CreateAsync(SqliteAppDbContextFactory factory, int customerId, params SalesInvoiceNewLineDto[] lines) =>
        await CreateAsync(factory, customerId, Guid.NewGuid(), null, lines);

    public static async Task<ServiceResult<SalesInvoiceDto>> CreateAsync(SqliteAppDbContextFactory factory, int customerId, Guid requestId, string lotNumber, params SalesInvoiceNewLineDto[] lines)
    {
        await using var db = factory.CreateContext();
        return await new SalesInvoiceService(db).CreateDirectAsync(new CreateDirectInvoiceDto
        {
            RequestId = requestId, CustomerId = customerId, LotNumber = lotNumber, DeliveryDate = new DateOnly(2026, 11, 25), Lines = lines.ToList()
        });
    }

    public static async Task<decimal> FreeAsync(SqliteAppDbContextFactory factory, Arranged a, int lotId)
    {
        await using var db = factory.CreateContext();
        var options = (await new SalesOrderService(db).GetLotOptionsAsync(a.Seed.AvocadoId, "box")).Value;
        return options.SingleOrDefault(o => o.PartijId == lotId)?.AvailableQuantity ?? 0m;
    }
}
