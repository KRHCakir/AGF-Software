using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Infrastructure;

// Verkopen vóór aankomst, de praktijk van de eigenaar: Tomaat Ronde 67-82 met 40 colli goedgekeurd op voorraad (LOT-A, inkooporder 1) en
// 20 colli verwacht op inkooporder 2 (nog niet ontvangen), en een variant met bijna dezelfde naam (Ronde 67-82 Extra, 15 colli op
// voorraad) die nooit mag meetellen. Rechtstreeks opgeslagen, zoals ontvangst en keuring ze schrijven.
public sealed class SalesBeforeArrivalScenario : IDisposable
{
    public SqliteAppDbContextFactory Factory { get; } = new();
    public int CustomerId { get; private set; }
    public int TomatoId { get; private set; }
    public int LookalikeId { get; private set; }
    public int VendorId { get; private set; }
    public int OpenOrderLineId { get; private set; }
    public int StockContentId { get; private set; }

    public void Dispose() => Factory.Dispose();

    public static readonly DateTime Received = new(2026, 10, 19, 7, 0, 0, DateTimeKind.Utc);

    public async Task ArrangeAsync()
    {
        await using var db = Factory.CreateContext();
        var product = new Product { Name = "Tomaat" };
        var tomato = new ProductVariant { Product = product, Name = "Ronde 67-82", Origin = "NL", Packaging = "6kg Doos", DefaultPrice = 9m };
        var lookalike = new ProductVariant { Product = product, Name = "Ronde 67-82 Extra", Origin = "NL", Packaging = "6kg Doos", DefaultPrice = 9m };
        var vendor = new Vendor { Name = "Kweker K" };
        var customer = new Customer { Name = "Groothandel G" };
        var received = Commitment(vendor, "Received", Received);
        var receivedLine = new SupplyCommitmentLine { SupplyCommitment = received, ProductVariant = tomato, ExpectedQuantity = 40m, ExpectedUnit = "colli", LotNumber = "LOT-A" };
        var lookalikeLine = new SupplyCommitmentLine { SupplyCommitment = received, ProductVariant = lookalike, ExpectedQuantity = 15m, ExpectedUnit = "colli", LotNumber = "LOT-L" };
        var open = Commitment(vendor, "Expected", new DateTime(2026, 11, 5, 6, 0, 0, DateTimeKind.Utc));
        var openLine = new SupplyCommitmentLine { SupplyCommitment = open, ProductVariant = tomato, ExpectedQuantity = 20m, ExpectedUnit = "colli" };
        var pallet = new Pallet { Reference = "P-T1", LogisticsStatus = "Received", LocationContext = "Warehouse" };
        var stock = Content(pallet, receivedLine, 40m, 240m, "Approved", "LOT-A", Received);
        var lookalikeStock = Content(pallet, lookalikeLine, 15m, 90m, "Approved", "LOT-L", Received);
        db.AddRange(product, tomato, lookalike, vendor, customer, received, receivedLine, lookalikeLine, open, openLine, pallet, stock, lookalikeStock);
        await db.SaveChangesAsync();
        (CustomerId, TomatoId, LookalikeId, VendorId, OpenOrderLineId, StockContentId) = (customer.Id, tomato.Id, lookalike.Id, vendor.Id, openLine.Id, stock.Id);
    }

    private static SupplyCommitment Commitment(Vendor vendor, string status, DateTime expectedAt) => new()
    {
        RequestId = Guid.NewGuid(), Vendor = vendor, Route = "Warehouse", Status = status, Origin = "NL", ExpectedAt = expectedAt
    };

    private static PalletContentLine Content(Pallet pallet, SupplyCommitmentLine source, decimal quantity, decimal kg, string quality, string lot, DateTime createdAt) => new()
    {
        RequestId = Guid.NewGuid(), Pallet = pallet, SupplyCommitmentLine = source, LotNumber = lot, Quantity = quantity, Unit = "colli",
        NetWeightKg = kg, QualityStatus = quality, CreatedAt = createdAt
    };

    public CreateSalesOrderDto Order(params (decimal Quantity, string Source)[] lines) => Order(TomatoId, lines);

    public CreateSalesOrderDto Order(int variantId, params (decimal Quantity, string Source)[] lines) => new()
    {
        RequestId = Guid.NewGuid(),
        CustomerId = CustomerId,
        CustomerReference = "PO-778",
        RequestedDeliveryDate = new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero),
        ActorReference = "verkoop-1",
        Lines = lines.Select(l => new CreateSalesOrderLineDto { ProductVariantId = variantId, Quantity = l.Quantity, Unit = "colli", SupplySource = l.Source }).ToList()
    };

    public async Task<ServiceResult<SalesOrderDto>> CreateAsync(CreateSalesOrderDto request)
    {
        await using var db = Factory.CreateContext();
        return await new SalesOrderService(db).CreateAsync(request);
    }

    public async Task<ServiceResult<StockReservationDto>> ReserveAsync(int orderId, int lineId, int contentId, decimal quantity, decimal kg)
    {
        await using var db = Factory.CreateContext();
        return await new StockReservationService(db).ReserveAsync(orderId, lineId, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = contentId, Quantity = quantity, Unit = "colli", NetWeightKg = kg, ActorReference = "verkoop-1"
        });
    }
}
