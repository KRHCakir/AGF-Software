using AgfAdmin.Data;
using AgfAdmin.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Laadt de feiten voor SalesBalanceCalculator: dezelfde aanvoer- en inhoudsfeiten als het beschikbaarheidsoverzicht plus de open
    // verkooporderregels (orders met status Open). SQL laadt alleen platte projecties; optellen gebeurt exact in het geheugen.
    internal static class SalesBalanceLoader
    {
        public static async Task<Dictionary<(int ProductVariantId, string Unit), SalesBalance>> LoadAsync(AppDbContext db, IReadOnlyCollection<int> variantIds, CancellationToken cancellationToken)
        {
            if (variantIds != null && variantIds.Count == 0)
            {
                return new Dictionary<(int, string), SalesBalance>();
            }
            var (sources, contents) = await AvailabilityService.LoadFactsAsync(db, variantIds, cancellationToken);
            var lines = await LoadSalesLinesAsync(db, variantIds, cancellationToken);
            return SalesBalanceCalculator.Build(sources, contents, lines);
        }

        public static async Task<List<SalesLineFact>> LoadSalesLinesAsync(AppDbContext db, IReadOnlyCollection<int> variantIds, CancellationToken cancellationToken)
        {
            var lineQuery = db.SalesOrderLines.AsNoTracking().Where(l => l.SalesOrder.Status == ReservationRules.OrderStatusOpen);
            var reservationQuery = db.StockReservations.AsNoTracking()
                .Where(r => r.SalesOrderLineId != null && r.SalesOrderLine.SalesOrder.Status == ReservationRules.OrderStatusOpen
                    && (r.Status == ReservationRules.StatusActive || r.Status == ReservationRules.StatusConsumed));
            if (variantIds != null)
            {
                lineQuery = lineQuery.Where(l => variantIds.Contains(l.ProductVariantId));
                reservationQuery = reservationQuery.Where(r => variantIds.Contains(r.SalesOrderLine.ProductVariantId));
            }

            var rows = await lineQuery
                .Select(l => new
                {
                    l.Id, l.SalesOrderId, l.SalesOrder.OrderNumber, l.SalesOrder.CustomerId, CustomerName = l.SalesOrder.Customer.Name, l.SalesOrder.CustomerReference,
                    l.SalesOrder.CreatedAt, l.SalesOrder.RequestedDeliveryDate, l.ProductVariantId, ProductName = l.ProductVariant.Product.Name, VariantName = l.ProductVariant.Name,
                    l.Unit, l.SupplySource, l.Quantity
                })
                .ToListAsync(cancellationToken);
            var reservations = await reservationQuery
                .Select(r => new { LineId = r.SalesOrderLineId!.Value, r.Quantity, r.Status, r.Source, Quality = r.PalletContentLine == null ? null : r.PalletContentLine.QualityStatus })
                .ToListAsync(cancellationToken);
            var byLine = reservations.ToLookup(r => r.LineId);

            return rows.Select(l =>
            {
                var own = byLine[l.Id].ToList();
                var active = own.Where(r => r.Status == ReservationRules.StatusActive).ToList();
                return new SalesLineFact(
                    l.Id, l.SalesOrderId, l.OrderNumber, l.CustomerId, l.CustomerName, l.CustomerReference,
                    DateTime.SpecifyKind(l.CreatedAt, DateTimeKind.Utc), DateTime.SpecifyKind(l.RequestedDeliveryDate, DateTimeKind.Utc),
                    l.ProductVariantId, l.ProductName, l.VariantName, l.Unit, l.SupplySource, l.Quantity,
                    ReservedApproved: active.Where(r => r.Source == ReservationRules.SourcePhysical && r.Quality == PalletRules.QualityApproved).Sum(r => r.Quantity),
                    ReservedOnHold: active.Where(r => r.Source == ReservationRules.SourcePhysical && r.Quality != PalletRules.QualityApproved).Sum(r => r.Quantity),
                    Allocated: active.Where(r => r.Source == ReservationRules.SourceDirectTrade).Sum(r => r.Quantity),
                    Consumed: own.Where(r => r.Status == ReservationRules.StatusConsumed).Sum(r => r.Quantity));
            }).ToList();
        }

        internal static SalesBalanceDto ToBalanceDto(SalesBalance balance) => new()
        {
            ProductVariantId = balance.ProductVariantId,
            Unit = balance.Unit,
            Stock = balance.Stock,
            Expected = balance.Expected,
            ExpectedNotReceived = balance.ExpectedNotReceived,
            ExpectedInInspection = balance.ExpectedInInspection,
            Sold = balance.Sold,
            SoldReserved = balance.SoldReserved,
            Free = balance.Free,
            FreeStock = balance.FreeStock,
            ShortageQuantity = balance.ShortageQuantity
        };

        internal static SalesLineCoverageDto ToCoverageDto(LineCoverage coverage) => new()
        {
            FromStock = coverage.FromStock,
            Shortage = coverage.Shortage,
            Expected = coverage.Expected.Select(e => new ExpectedCoverDto
            {
                SupplyCommitmentId = e.SupplyCommitmentId,
                SupplyOrderNumber = e.OrderNumber,
                ExpectedAt = e.ExpectedAt == null ? null : DateTime.SpecifyKind(e.ExpectedAt.Value, DateTimeKind.Utc),
                Quantity = e.Quantity,
                InInspection = e.InInspection
            }).ToList()
        };
    }
}
