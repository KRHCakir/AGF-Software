using AgfAdmin.DTOs;
using AgfAdmin.Services;

namespace AgfAdmin.Tests.Infrastructure;

// Gedeelde stappen voor DirectTrade-tests via de echte services: toewijzen, verzenden en terugnemen.
public static class DirectTradeTestFlow
{
    public static readonly DateOnly Today = new(2026, 10, 20);

    public static async Task<StockReservationDto> AllocateAsync(SqliteAppDbContextFactory factory, int orderId, int lineId, int sourceLineId, decimal quantity, string unit = "box")
    {
        await using var db = factory.CreateContext();
        var result = await new DirectTradeAllocationService(db).AllocateAsync(orderId, lineId, new CreateDirectTradeAllocationDto
        {
            RequestId = Guid.NewGuid(), SupplyCommitmentLineId = sourceLineId, Quantity = quantity, Unit = unit, AcknowledgeExpectedSupply = true, ActorReference = "office-1"
        });
        if (result.Status != ServiceResultStatus.Created) throw new InvalidOperationException($"allocate failed: {result.Status} {result.Message}");
        return result.Value;
    }

    public static async Task<int> OrderVersionAsync(SqliteAppDbContextFactory factory, int orderId)
    {
        await using var db = factory.CreateContext();
        return (await new SalesOrderService(db).GetAsync(orderId)).Value.Version;
    }

    public static async Task<DirectTradeShipmentDto> ShipAsync(SqliteAppDbContextFactory factory, int orderId, params (StockReservationDto Allocation, decimal Kg, string Lot)[] lines)
    {
        await using var db = factory.CreateContext();
        var result = await new DirectTradeShipmentService(db, new FixedBusinessCalendar(Today)).ShipAsync(orderId, new CreateDirectTradeShipmentDto
        {
            RequestId = Guid.NewGuid(), ExpectedOrderVersion = await OrderVersionAsync(factory, orderId), ActorReference = "office-1", ShippedOn = new DateOnly(2026, 10, 19), Reference = "CMR-T",
            Lines = lines.Select(l => new CreateDirectTradeShipmentLineDto
            {
                StockReservationId = l.Allocation.Id, ExpectedReservationVersion = l.Allocation.Version, Quantity = l.Allocation.Quantity, Unit = l.Allocation.Unit,
                NetWeightKg = l.Kg, LotNumber = l.Lot
            }).ToList()
        });
        if (result.Status != ServiceResultStatus.Created) throw new InvalidOperationException($"ship failed: {result.Status} {result.Message}");
        return result.Value;
    }

    // Verwachte reserveringsversie na verzenden: één statuswijziging verder dan bij het toewijzen.
    public static CorrectDirectTradeShipmentDto Correction(int orderVersion, DirectTradeShipmentDto shipment, Guid? requestId = null, string reason = "Vervoerder heeft niet geladen", bool? confirmed = true, int versionAfterShipping = 1)
        => new()
        {
            RequestId = requestId ?? Guid.NewGuid(), ExpectedOrderVersion = orderVersion, ActorReference = "office-1", Reason = reason, ShipmentDidNotTakePlace = confirmed,
            Lines = shipment.Lines.Select(l => new CreateSalesDispatchLineDto { StockReservationId = l.StockReservationId, ExpectedReservationVersion = versionAfterShipping }).ToList()
        };
}
