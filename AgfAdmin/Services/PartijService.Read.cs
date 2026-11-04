using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Partijlijst en partijdetail: alles afgeleid uit de bestaande regels (inkoopregels, palletregels en verkoopreserveringen met deze
    // partij); niets opgeslagen. Kosten en resultaat komen in P2/P3.
    public sealed partial class PartijService
    {
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;

        private sealed record LineFact(int PartijId, int LineId, int OrderId, string OrderNumber, string OrderStatus, DateOnly OrderedOn, DateTime? ExpectedAt,
            string SupplierLotReference, string VendorName, string Product, decimal Expected, string Unit);
        private sealed record ContentFact(int PartijId, int Id, int PalletId, string PalletReference, string PalletStatus, int LineId, int OrderId, string OrderNumber,
            string ProductName, string VariantName, string LotNumber, decimal Quantity, decimal Dispatched, decimal SplitOff, string Unit, decimal NetWeightKg,
            string QualityStatus, bool FromCorrection, bool FromSplit, DateTime CreatedAt);
        private sealed record SaleFact(int PartijId, int SalesOrderId, string OrderNumber, string CustomerName, string OrderStatus, string Status, decimal Quantity, string Unit);
        private sealed record Facts(List<LineFact> Lines, List<ContentFact> Contents, List<SaleFact> Sales);

        // Zoeken op partijnummer (zonder spaties en streepjes), partij van de leverancier, product en leverancier; status Open of SoldOut.
        public async Task<ServiceResult<PartijListDto>> ListAsync(string query, string status, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var statusFilter = string.IsNullOrWhiteSpace(status) || status.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ? null
                : new[] { PartijRules.StatusOpen, PartijRules.StatusSoldOut }.FirstOrDefault(s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(status) && statusFilter == null && !status.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<PartijListDto>.Validation(new Dictionary<string, string[]> { ["status"] = new[] { "Status must be all, Open or SoldOut." } });
            }
            page = Math.Max(1, page);
            pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

            var partijen = await _db.Partijen.AsNoTracking().ToListAsync(cancellationToken);
            var facts = await LoadFactsAsync(partijen.Select(p => p.Id).ToList(), cancellationToken);
            var term = (query ?? "").Trim().ToLowerInvariant();
            var key = PartijRules.NumberKey(query);
            var items = partijen
                .Select(p => Totals<PartijListItemDto>(p, facts))
                .Where(i => term == "" || (key != null && PartijRules.NumberKey(i.Number).Contains(key, StringComparison.Ordinal))
                    || i.SupplierLotReferences.Concat(i.Products).Concat(i.Vendors).Any(text => text.ToLowerInvariant().Contains(term)))
                .Where(i => statusFilter == null || i.Status == statusFilter)
                .OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
                .ToList();
            return ServiceResult<PartijListDto>.Ok(new PartijListDto
            {
                Total = items.Count, Page = page, PageSize = pageSize, Items = items.Skip((page - 1) * pageSize).Take(pageSize).ToList()
            });
        }

        public async Task<ServiceResult<PartijDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var partij = await _db.Partijen.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (partij == null)
            {
                return ServiceResult<PartijDetailDto>.NotFound($"Lot {id} does not exist.");
            }
            var facts = await LoadFactsAsync(new List<int> { id }, cancellationToken);
            var detail = Totals<PartijDetailDto>(partij, facts);
            detail.Note = partij.Note;
            detail.CreatedBy = partij.CreatedBy;
            detail.Linked = await IsLinkedAsync(id, cancellationToken);
            detail.ReservedQuantity = facts.Sales.Where(s => s.Status == ReservationRules.StatusActive).Sum(s => s.Quantity);
            detail.ShippedQuantity = facts.Sales.Where(s => s.Status == ReservationRules.StatusConsumed).Sum(s => s.Quantity);
            var orderStatuses = await OrderStatusesAsync(facts.Lines.Select(l => l.OrderId).Distinct().ToList(), cancellationToken);
            detail.Orders = facts.Lines.GroupBy(l => l.OrderId).Select(g =>
            {
                var first = g.First();
                var expected = g.Sum(l => l.Expected);
                var received = facts.Contents.Where(c => c.OrderId == g.Key && IsPhysicalReceipt(c)).Sum(c => c.Quantity);
                return new PartijOrderDto
                {
                    Id = g.Key, OrderNumber = first.OrderNumber, VendorName = first.VendorName, OrderedOn = first.OrderedOn, SupplierLotReference = first.SupplierLotReference,
                    ExpectedAt = first.ExpectedAt == null ? null : DateTime.SpecifyKind(first.ExpectedAt.Value, DateTimeKind.Utc),
                    Unit = SingleUnit(g.Select(l => l.Unit)), ExpectedQuantity = expected, ReceivedQuantity = received,
                    Status = orderStatuses.TryGetValue(g.Key, out var orderStatus) ? orderStatus : OrderStatus(first.OrderStatus, expected, received)
                };
            }).OrderBy(o => o.OrderedOn).ThenBy(o => o.Id).ToList();
            detail.Receipts = facts.Contents.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).Select(c => new PartijReceiptDto
            {
                PalletContentLineId = c.Id, PalletId = c.PalletId, PalletReference = c.PalletReference, PalletStatus = c.PalletStatus, SupplyCommitmentId = c.OrderId,
                OrderNumber = c.OrderNumber, ProductName = c.ProductName, ProductVariantName = c.VariantName, LotNumber = c.LotNumber,
                Kind = c.FromCorrection ? "Correction" : c.FromSplit ? "SplitOff" : "Receipt", Quantity = c.Quantity, Unit = c.Unit, NetWeightKg = c.NetWeightKg,
                OnHandQuantity = OnHand(c), QualityStatus = c.QualityStatus, ReceivedAt = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc)
            }).ToList();
            detail.SalesOrders = facts.Sales.GroupBy(s => new { s.SalesOrderId, s.Unit }).Select(g => new PartijSalesOrderDto
            {
                SalesOrderId = g.Key.SalesOrderId, OrderNumber = g.First().OrderNumber, CustomerName = g.First().CustomerName, Status = g.First().OrderStatus, Unit = g.Key.Unit,
                ReservedQuantity = g.Where(s => s.Status == ReservationRules.StatusActive).Sum(s => s.Quantity),
                ShippedQuantity = g.Where(s => s.Status == ReservationRules.StatusConsumed).Sum(s => s.Quantity)
            }).OrderBy(s => s.SalesOrderId).ThenBy(s => s.Unit).ToList();
            var events = await _db.PartijAuditEvents.AsNoTracking().Where(e => e.PartijId == id).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync(cancellationToken);
            detail.History = events.Select(e => new PartijHistoryDto
                {
                    OccurredAt = DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc), Type = e.EventType, OldValue = e.OldValue, NewValue = e.NewValue,
                    Reason = e.Reason, SupplyCommitmentId = e.SupplyCommitmentId, ActorReference = e.ActorReference
                })
                .Prepend(new PartijHistoryDto { OccurredAt = detail.CreatedAt, Type = "Created", NewValue = events.FirstOrDefault(e => e.EventType == PartijRules.EventNumberCorrected)?.OldValue ?? partij.Number, ActorReference = partij.CreatedBy })
                .ToList();
            return ServiceResult<PartijDetailDto>.Ok(detail);
        }

        private async Task<Facts> LoadFactsAsync(List<int> ids, CancellationToken cancellationToken)
        {
            var lines = await _db.SupplyCommitmentLines.AsNoTracking()
                .Where(l => l.PartijId != null && ids.Contains(l.PartijId.Value))
                .Select(l => new LineFact(l.PartijId.Value, l.Id, l.SupplyCommitmentId, l.SupplyCommitment.OrderNumber, l.SupplyCommitment.Status, l.SupplyCommitment.OrderedOn,
                    l.SupplyCommitment.ExpectedAt, l.SupplyCommitment.SupplierLotReference, l.SupplyCommitment.Vendor.Name,
                    l.ProductVariant.Product.Name + " " + l.ProductVariant.Name, l.ExpectedQuantity, l.ExpectedUnit))
                .ToListAsync(cancellationToken);
            var contents = await _db.PalletContentLines.AsNoTracking()
                .Where(c => c.PartijId != null && ids.Contains(c.PartijId.Value))
                .Select(c => new ContentFact(c.PartijId.Value, c.Id, c.PalletId, c.Pallet.Reference, c.Pallet.LogisticsStatus, c.SupplyCommitmentLineId,
                    c.SupplyCommitmentLine.SupplyCommitmentId, c.SupplyCommitmentLine.SupplyCommitment.OrderNumber, c.SupplyCommitmentLine.ProductVariant.Product.Name,
                    c.SupplyCommitmentLine.ProductVariant.Name, c.LotNumber, c.Quantity, c.DispatchedQuantity, c.SplitOffQuantity, c.Unit, c.NetWeightKg, c.QualityStatus,
                    c.OriginCorrectionLineId != null, c.OriginRejectionLineId != null, c.CreatedAt))
                .ToListAsync(cancellationToken);
            // Verkocht: Physical via de palletregel, klantgebonden (DirectTrade) via de inkoopregel; alleen actief of verzonden.
            var sales = await _db.StockReservations.AsNoTracking()
                .Where(r => r.SalesOrderLineId != null && (r.Status == ReservationRules.StatusActive || r.Status == ReservationRules.StatusConsumed)
                    && ((r.PalletContentLine != null && r.PalletContentLine.PartijId != null && ids.Contains(r.PalletContentLine.PartijId.Value))
                        || (r.PalletContentLineId == null && r.SupplyCommitmentLine.PartijId != null && ids.Contains(r.SupplyCommitmentLine.PartijId.Value))))
                .Select(r => new SaleFact(r.PalletContentLine != null ? r.PalletContentLine.PartijId.Value : r.SupplyCommitmentLine.PartijId.Value,
                    r.SalesOrderLine.SalesOrderId, r.SalesOrderLine.SalesOrder.OrderNumber, r.SalesOrderLine.SalesOrder.Customer.Name, r.SalesOrderLine.SalesOrder.Status,
                    r.Status, r.Quantity, r.Unit))
                .ToListAsync(cancellationToken);
            return new Facts(lines, contents, sales);
        }

        // Weergavestatus van de order als geheel (alle regels en ontvangsten, ook van andere partijen), met dezelfde regels als de inkooporderlijst.
        private async Task<Dictionary<int, string>> OrderStatusesAsync(List<int> orderIds, CancellationToken cancellationToken)
        {
            var result = new Dictionary<int, string>();
            if (orderIds.Count == 0) return result;
            var orders = await _db.SupplyCommitments.AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.Status }).ToListAsync(cancellationToken);
            var lines = await _db.SupplyCommitmentLines.AsNoTracking().Where(l => orderIds.Contains(l.SupplyCommitmentId))
                .Select(l => new { l.Id, l.SupplyCommitmentId, l.ExpectedQuantity, l.ExpectedUnit }).ToListAsync(cancellationToken);
            var receipts = await _db.PalletContentLines.AsNoTracking()
                .Where(c => orderIds.Contains(c.SupplyCommitmentLine.SupplyCommitmentId) && c.OriginCorrectionLineId == null && c.OriginRejectionLineId == null
                    && c.Pallet.LogisticsStatus != PalletRules.StatusCancelled)
                .Select(c => new { c.SupplyCommitmentLineId, c.Quantity, c.Unit }).ToListAsync(cancellationToken);
            var removed = await _db.SupplyRejectionLines.AsNoTracking().Where(l => orderIds.Contains(l.SupplyRejection.SupplyCommitmentId))
                .Select(l => new { l.SupplyCommitmentLineId, l.Quantity, l.SupplyRejection.Decision }).ToListAsync(cancellationToken);
            foreach (var order in orders)
            {
                var orderLines = lines.Where(l => l.SupplyCommitmentId == order.Id).ToList();
                var lineIds = orderLines.Select(l => l.Id).ToHashSet();
                var perLine = orderLines.Select(l => (l.ExpectedQuantity, Received: receipts
                    .Where(r => r.SupplyCommitmentLineId == l.Id && (l.ExpectedUnit == null || string.Equals(l.ExpectedUnit, r.Unit, StringComparison.Ordinal))).Sum(r => r.Quantity))).ToList();
                var receivedAll = receipts.Where(r => lineIds.Contains(r.SupplyCommitmentLineId)).Sum(r => r.Quantity);
                var removedQuantity = removed.Where(r => lineIds.Contains(r.SupplyCommitmentLineId) && !PurchaseOrderRules.KeepsGoods(r.Decision)).Sum(r => r.Quantity);
                result[order.Id] = order.Status != SupplyRules.StatusCancelled && PurchaseOrderRules.IsFullyRejected(receivedAll, removedQuantity)
                    ? PurchaseOrderRules.OrderRejected
                    : PurchaseOrderRules.OrderStatus(order.Status, perLine);
            }
            return result;
        }

        private static T Totals<T>(Partij partij, Facts facts) where T : PartijTotalsDto, new()
        {
            var lines = facts.Lines.Where(l => l.PartijId == partij.Id).ToList();
            var contents = facts.Contents.Where(c => c.PartijId == partij.Id).ToList();
            var sales = facts.Sales.Where(s => s.PartijId == partij.Id).ToList();
            var liveLines = lines.Where(l => l.OrderStatus != SupplyRules.StatusCancelled).ToList();
            var onHand = contents.Where(c => c.QualityStatus == PalletRules.QualityApproved && !PalletRules.IsTerminalLogisticsStatus(c.PalletStatus)).Sum(OnHand);
            var inInspection = contents.Where(c => c.QualityStatus == PalletRules.QualityQuarantine && !PalletRules.IsTerminalLogisticsStatus(c.PalletStatus)).Sum(OnHand);
            // Nog verwacht: per regel van een open order wat nog niet fysiek ontvangen is.
            var openExpected = lines.Where(l => l.OrderStatus is SupplyRules.StatusExpected or SupplyRules.StatusPartiallyReceived)
                .Sum(l => Math.Max(0m, l.Expected - contents.Where(c => c.LineId == l.LineId && IsPhysicalReceipt(c)).Sum(c => c.Quantity)));
            var received = contents.Where(IsPhysicalReceipt).ToList();
            return new T
            {
                Id = partij.Id,
                Number = partij.Number,
                CreatedAt = DateTime.SpecifyKind(partij.CreatedAt, DateTimeKind.Utc),
                Status = PartijRules.DisplayStatus(partij.Status, received.Count > 0, onHand + inInspection, openExpected),
                Vendors = lines.Select(l => l.VendorName).Where(v => v != null).Distinct().OrderBy(v => v).ToList(),
                Products = lines.Select(l => l.Product).Concat(contents.Select(c => $"{c.ProductName} {c.VariantName}")).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().OrderBy(p => p).ToList(),
                SupplierLotReferences = lines.Select(l => l.SupplierLotReference).Where(r => r != null).Distinct().OrderBy(r => r).ToList(),
                Unit = SingleUnit(liveLines.Select(l => l.Unit).Concat(contents.Select(c => c.Unit))),
                PurchasedQuantity = liveLines.Sum(l => l.Expected),
                ReceivedQuantity = received.Sum(c => c.Quantity),
                OnHandQuantity = onHand,
                InInspectionQuantity = inInspection,
                OpenExpectedQuantity = openExpected,
                SoldQuantity = sales.Sum(s => s.Quantity)
            };
        }

        // Fysieke ontvangst: geen teruggeboekte of afgesplitste inhoud (die is al bij de oorspronkelijke regel geteld), geen geannuleerde pallet.
        private static bool IsPhysicalReceipt(ContentFact c) => !c.FromCorrection && !c.FromSplit && c.PalletStatus != PalletRules.StatusCancelled;

        private static decimal OnHand(ContentFact c) => ReservationRules.OnHandQuantity(c.Quantity, c.Dispatched, c.SplitOff);

        private static string SingleUnit(IEnumerable<string> units)
        {
            var distinct = units.Where(u => u != null).Distinct().ToList();
            return distinct.Count == 1 ? distinct[0] : null;
        }

        private static string OrderStatus(string stored, decimal expected, decimal received) => stored switch
        {
            SupplyRules.StatusCancelled => PurchaseOrderRules.OrderCancelled,
            SupplyRules.StatusRejected => PurchaseOrderRules.OrderRejected,
            _ => received <= 0m ? PurchaseOrderRules.OrderOrdered : received >= expected ? PurchaseOrderRules.OrderReceived : PurchaseOrderRules.OrderPartiallyReceived
        };
    }
}
