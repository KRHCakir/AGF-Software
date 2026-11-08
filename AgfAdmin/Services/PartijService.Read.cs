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
        private sealed record SaleFact(int PartijId, int SalesOrderId, string OrderNumber, string CustomerName, string OrderStatus, string Status, decimal Quantity, string Unit,
            int? SalesPartijId, string SalesPartijNumber);
        // Verkoop-partij: de order, haar regels en per reservering de inkoop-partij waar de colli vandaan komen.
        private sealed record SalesSide(int PartijId, int SalesOrderId, string OrderNumber, string OrderStatus, string CustomerName, List<(int LineId, string Product, decimal Quantity, string Unit, decimal? NetKg)> Lines,
            List<SourceFact> Sources);
        private sealed record SourceFact(int SalesOrderId, int SalesOrderLineId, int? PartijId, string Number, string VendorName, string Product, string Status, decimal Quantity, string Unit, decimal NetKg);
        private sealed record Facts(List<LineFact> Lines, List<ContentFact> Contents, List<SaleFact> Sales)
        {
            public ILookup<int, LineFact> LinesByPartij { get; } = Lines.ToLookup(l => l.PartijId);
            public ILookup<int, ContentFact> ContentsByPartij { get; } = Contents.ToLookup(c => c.PartijId);
            public ILookup<int, SaleFact> SalesByPartij { get; } = Sales.ToLookup(s => s.PartijId);
            public Dictionary<int, SalesSide> SalesSides { get; init; } = new();
        }

        // Zoeken op partijnummer (zonder spaties en streepjes), partij van de leverancier, product en leverancier (bij een verkoop-partij:
        // klant, VO-nummer en product); status Open of SoldOut. Soort: Purchase (standaard, zoals P1), Sales of all.
        public async Task<ServiceResult<PartijListDto>> ListAsync(string query, string status, int page, int pageSize, CancellationToken cancellationToken = default, string kind = null)
        {
            var kindFilter = string.IsNullOrWhiteSpace(kind) ? PartijRules.KindPurchase
                : kind.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ? null
                : PartijRules.Kinds.FirstOrDefault(k => k.Equals(kind.Trim(), StringComparison.OrdinalIgnoreCase));
            if (kindFilter == null && !string.IsNullOrWhiteSpace(kind) && !kind.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<PartijListDto>.Validation(new Dictionary<string, string[]> { ["kind"] = new[] { "Kind must be all, Purchase or Sales." } });
            }
            var statusFilter = string.IsNullOrWhiteSpace(status) || status.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ? null
                : new[] { PartijRules.StatusOpen, PartijRules.StatusSoldOut }.FirstOrDefault(s => s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(status) && statusFilter == null && !status.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<PartijListDto>.Validation(new Dictionary<string, string[]> { ["status"] = new[] { "Status must be all, Open or SoldOut." } });
            }
            page = Math.Max(1, page);
            pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

            var term = (query ?? "").Trim().ToLowerInvariant();
            var key = PartijRules.NumberKey(query);
            // Zoeken in de database: alleen de passende partijen worden verder opgehaald.
            var candidates = _db.Partijen.AsNoTracking().AsQueryable();
            if (kindFilter != null)
            {
                candidates = candidates.Where(p => p.Kind == kindFilter);
            }
            if (term != "")
            {
                candidates = candidates.Where(p => (key != null && p.NumberKey.Contains(key))
                    || (p.SalesOrder != null && (p.SalesOrder.OrderNumber.ToLower().Contains(term) || p.SalesOrder.Customer.Name.ToLower().Contains(term)
                        || p.SalesOrder.Lines.Any(l => (l.ProductVariant.Product.Name + " " + l.ProductVariant.Name).ToLower().Contains(term))))
                    || _db.SupplyCommitmentLines.Any(l => l.PartijId == p.Id
                        && ((l.SupplyCommitment.SupplierLotReference != null && l.SupplyCommitment.SupplierLotReference.ToLower().Contains(term))
                            || (l.SupplyCommitment.Vendor.Name != null && l.SupplyCommitment.Vendor.Name.ToLower().Contains(term))
                            || (l.ProductVariant.Product.Name + " " + l.ProductVariant.Name).ToLower().Contains(term)))
                    || _db.PalletContentLines.Any(c => c.PartijId == p.Id
                        && (c.SupplyCommitmentLine.ProductVariant.Product.Name + " " + c.SupplyCommitmentLine.ProductVariant.Name).ToLower().Contains(term)));
            }
            var ordered = candidates.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id);
            List<PartijListItemDto> items;
            int total;
            if (statusFilter == null)
            {
                // Zonder statusfilter pagineren in de database en alleen de feiten van de gevraagde pagina laden.
                total = await candidates.CountAsync(cancellationToken);
                var pagePartijen = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
                var pageFacts = await LoadFactsAsync(pagePartijen, cancellationToken);
                items = pagePartijen.Select(p => Totals<PartijListItemDto>(p, pageFacts)).ToList();
            }
            else
            {
                // De status is afgeleid uit de feiten; daarom eerst de (al door de zoekterm beperkte) kandidaten berekenen.
                var partijen = await ordered.ToListAsync(cancellationToken);
                var facts = await LoadFactsAsync(partijen, cancellationToken);
                var matching = partijen.Select(p => Totals<PartijListItemDto>(p, facts)).Where(i => i.Status == statusFilter).ToList();
                total = matching.Count;
                items = matching.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            }
            return ServiceResult<PartijListDto>.Ok(new PartijListDto { Total = total, Page = page, PageSize = pageSize, Items = items });
        }

        public async Task<ServiceResult<PartijDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var partij = await _db.Partijen.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (partij == null)
            {
                return ServiceResult<PartijDetailDto>.NotFound($"Lot {id} does not exist.");
            }
            var facts = await LoadFactsAsync(new List<Partij> { partij }, cancellationToken);
            var detail = Totals<PartijDetailDto>(partij, facts);
            if (facts.SalesSides.TryGetValue(partij.Id, out var side))
            {
                detail.ReservedQuantity = side.Sources.Where(s => s.Status == ReservationRules.StatusActive).Sum(s => s.Quantity);
                detail.ShippedQuantity = side.Sources.Where(s => s.Status == ReservationRules.StatusConsumed).Sum(s => s.Quantity);
                detail.Sources = side.Sources.GroupBy(s => new { s.PartijId, s.Number, s.VendorName, s.Product, s.Unit }).Select(g => new PartijSourceDto
                {
                    PartijId = g.Key.PartijId ?? 0, Number = g.Key.Number, VendorName = g.Key.VendorName, Product = g.Key.Product, Unit = g.Key.Unit,
                    ReservedQuantity = g.Where(s => s.Status == ReservationRules.StatusActive).Sum(s => s.Quantity),
                    ShippedQuantity = g.Where(s => s.Status == ReservationRules.StatusConsumed).Sum(s => s.Quantity)
                }).OrderBy(s => s.Number).ThenBy(s => s.Product).ToList();
            }
            detail.Note = partij.Note;
            detail.CreatedBy = partij.CreatedBy;
            detail.Linked = await IsLinkedAsync(id, cancellationToken);
            detail.NumberFixed = partij.Kind == PartijRules.KindSales ? await SalesNumberFixedAsync(_db, partij.SalesOrderId!.Value, async: true, cancellationToken) : detail.Linked;
            if (partij.Kind == PartijRules.KindPurchase)
            {
                detail.ReservedQuantity = facts.Sales.Where(s => s.Status == ReservationRules.StatusActive).Sum(s => s.Quantity);
                detail.ShippedQuantity = facts.Sales.Where(s => s.Status == ReservationRules.StatusConsumed).Sum(s => s.Quantity);
            }
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
                SalesOrderId = g.Key.SalesOrderId, OrderNumber = g.First().OrderNumber, SalesPartijId = g.First().SalesPartijId, SalesPartijNumber = g.First().SalesPartijNumber,
                CustomerName = g.First().CustomerName, Status = g.First().OrderStatus, Unit = g.Key.Unit,
                ReservedQuantity = g.Where(s => s.Status == ReservationRules.StatusActive).Sum(s => s.Quantity),
                ShippedQuantity = g.Where(s => s.Status == ReservationRules.StatusConsumed).Sum(s => s.Quantity)
            }).OrderBy(s => s.SalesOrderId).ThenBy(s => s.Unit).ToList();
            await AddCostsAsync(detail, partij, facts, cancellationToken);
            var events = await _db.PartijAuditEvents.AsNoTracking().Where(e => e.PartijId == id).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync(cancellationToken);
            detail.History = events.Select(e => new PartijHistoryDto
                {
                    OccurredAt = DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc), Type = e.EventType, OldValue = e.OldValue, NewValue = e.NewValue,
                    Reason = e.Reason, SupplyCommitmentId = e.SupplyCommitmentId, ActorReference = e.ActorReference
                })
                .Prepend(new PartijHistoryDto { OccurredAt = detail.CreatedAt, Type = "Created", ActorReference = partij.CreatedBy,
                    NewValue = events.FirstOrDefault(e => e.EventType is PartijRules.EventNumberCorrected or PartijRules.EventNumberChanged)?.OldValue ?? partij.Number })
                .ToList();
            return ServiceResult<PartijDetailDto>.Ok(detail);
        }

        // Kosten (P2): de kostenregels van deze partij met factuur en leverancier, en apart (niet opgeteld) de kosten op de gekoppelde
        // partijen van de andere soort. Zo telt een kostenpost nooit twee keer: hij hangt aan precies één partij.
        private async Task AddCostsAsync(PartijDetailDto detail, Partij partij, Facts facts, CancellationToken cancellationToken)
        {
            detail.Costs = await _db.PartijCosts.AsNoTracking().Where(c => c.PartijId == partij.Id)
                .OrderBy(c => c.Kind).ThenBy(c => c.IncomingInvoiceLine.IncomingInvoice.InvoiceDate).ThenBy(c => c.Id)
                .Select(c => new PartijCostLineDto
                {
                    Kind = c.Kind, InvoiceId = c.IncomingInvoiceLine.IncomingInvoiceId, InvoiceNumber = c.IncomingInvoiceLine.IncomingInvoice.InvoiceNumber,
                    VendorName = c.IncomingInvoiceLine.IncomingInvoice.Vendor.Name, InvoiceDate = c.IncomingInvoiceLine.IncomingInvoice.InvoiceDate,
                    Description = c.IncomingInvoiceLine.Description, Amount = c.AmountExclVat,
                    Currency = c.IncomingInvoiceLine.IncomingInvoice.Currency, ExchangeRate = c.IncomingInvoiceLine.IncomingInvoice.ExchangeRate,
                    AmountInCurrency = c.IncomingInvoiceLine.LineTotal
                })
                .ToListAsync(cancellationToken);
            foreach (var cost in detail.Costs)
            {
                // Het oorspronkelijke bedrag naast het eurobedrag, alleen bij een andere valuta (de koers ligt vast zodra er kosten zijn):
                // naar verhouding van de regel (AmountInCurrency bevat hier nog het regelbedrag).
                var euro = ExchangeRateRules.IsEuro(cost.Currency) || cost.ExchangeRate == null;
                var lineTotal = cost.AmountInCurrency ?? 0m;
                cost.Currency = euro ? null : cost.Currency.Trim().ToUpperInvariant();
                cost.AmountInCurrency = euro ? null : ExchangeRateRules.InCurrency(cost.Amount, lineTotal, ExchangeRateRules.ToEuro(lineTotal, cost.ExchangeRate.Value));
                cost.ExchangeRate = euro ? null : cost.ExchangeRate;
            }
            detail.CostTotal = detail.Costs.Sum(c => c.Amount);
            var linked = partij.Kind == PartijRules.KindSales
                ? (facts.SalesSides.TryGetValue(partij.Id, out var side) ? side.Sources.Where(s => s.PartijId != null).Select(s => s.PartijId.Value) : Enumerable.Empty<int>())
                : facts.Sales.Where(s => s.SalesPartijId != null).Select(s => s.SalesPartijId.Value);
            var linkedIds = linked.Distinct().ToList();
            if (linkedIds.Count == 0)
            {
                return;
            }
            var amounts = await _db.PartijCosts.AsNoTracking().Where(c => linkedIds.Contains(c.PartijId)).Select(c => new { c.PartijId, c.AmountExclVat }).ToListAsync(cancellationToken);
            var lots = await _db.Partijen.AsNoTracking().Where(p => linkedIds.Contains(p.Id)).Select(p => new { p.Id, p.Number, p.Kind }).ToListAsync(cancellationToken);
            detail.LinkedCosts = lots.Select(p => new PartijLinkedCostDto
                {
                    PartijId = p.Id, Number = p.Number, Kind = p.Kind, CostTotal = amounts.Where(a => a.PartijId == p.Id).Sum(a => a.AmountExclVat)
                })
                .Where(l => l.CostTotal > 0m).OrderBy(l => l.Number).ToList();
        }

        // Totalen van een aantal partijen (beide soorten), voor de basis van "Verdeel naar kg / colli" bij de kostenverdeling.
        internal async Task<Dictionary<int, PartijTotalsDto>> TotalsAsync(List<int> ids, CancellationToken cancellationToken)
        {
            var partijen = await _db.Partijen.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
            var facts = await LoadFactsAsync(partijen, cancellationToken);
            return partijen.ToDictionary(p => p.Id, p => (PartijTotalsDto)Totals<PartijListItemDto>(p, facts));
        }

        private async Task<Facts> LoadFactsAsync(List<Partij> partijen, CancellationToken cancellationToken)
        {
            var ids = partijen.Where(p => p.Kind == PartijRules.KindPurchase).Select(p => p.Id).ToList();
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
                    r.Status, r.Quantity, r.Unit, r.SalesOrderLine.SalesOrder.SalesPartij.Id, r.SalesOrderLine.SalesOrder.SalesPartij.Number))
                .ToListAsync(cancellationToken);
            return new Facts(lines, contents, sales) { SalesSides = await LoadSalesSidesAsync(partijen, cancellationToken) };
        }

        private async Task<Dictionary<int, SalesSide>> LoadSalesSidesAsync(List<Partij> partijen, CancellationToken cancellationToken)
        {
            var byOrder = partijen.Where(p => p.Kind == PartijRules.KindSales && p.SalesOrderId != null).ToDictionary(p => p.SalesOrderId.Value, p => p.Id);
            if (byOrder.Count == 0)
            {
                return new Dictionary<int, SalesSide>();
            }
            var orderIds = byOrder.Keys.ToList();
            var orders = await _db.SalesOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.OrderNumber, o.Status, Customer = o.Customer.Name }).ToListAsync(cancellationToken);
            var lines = await _db.SalesOrderLines.AsNoTracking().Where(l => orderIds.Contains(l.SalesOrderId))
                .Select(l => new { l.Id, l.SalesOrderId, Product = l.ProductVariant.Product.Name + " " + l.ProductVariant.Name, l.Quantity, l.Unit, l.NetWeightKg }).ToListAsync(cancellationToken);
            var sources = await _db.StockReservations.AsNoTracking()
                .Where(r => r.SalesOrderLineId != null && orderIds.Contains(r.SalesOrderLine.SalesOrderId)
                    && (r.Status == ReservationRules.StatusActive || r.Status == ReservationRules.StatusConsumed))
                .Select(r => new SourceFact(r.SalesOrderLine.SalesOrderId, r.SalesOrderLineId.Value,
                    r.PalletContentLine != null ? r.PalletContentLine.PartijId : r.SupplyCommitmentLine.PartijId,
                    r.PalletContentLine != null ? r.PalletContentLine.Partij.Number : r.SupplyCommitmentLine.Partij.Number,
                    r.SupplyCommitmentLine.SupplyCommitment.Vendor.Name, r.SupplyCommitmentLine.ProductVariant.Product.Name + " " + r.SupplyCommitmentLine.ProductVariant.Name,
                    r.Status, r.Quantity, r.Unit, r.NetWeightKg ?? 0m))
                .ToListAsync(cancellationToken);
            return orders.ToDictionary(o => byOrder[o.Id], o => new SalesSide(byOrder[o.Id], o.Id, o.OrderNumber, o.Status, o.Customer,
                lines.Where(l => l.SalesOrderId == o.Id).Select(l => (l.Id, l.Product, l.Quantity, l.Unit, l.NetWeightKg)).ToList(),
                sources.Where(s => s.SalesOrderId == o.Id).ToList()));
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
            if (facts.SalesSides.TryGetValue(partij.Id, out var side))
            {
                return new T
                {
                    Id = partij.Id, Number = partij.Number, Kind = partij.Kind, CreatedAt = DateTime.SpecifyKind(partij.CreatedAt, DateTimeKind.Utc), Status = partij.Status,
                    SalesOrderId = side.SalesOrderId, SalesOrderNumber = side.OrderNumber, SalesOrderStatus = side.OrderStatus, CustomerName = side.CustomerName,
                    Products = side.Lines.Select(l => l.Product).Distinct().OrderBy(p => p).ToList(),
                    Unit = SingleUnit(side.Lines.Select(l => l.Unit)),
                    OrderedQuantity = side.Lines.Sum(l => l.Quantity),
                    SoldQuantity = side.Sources.Sum(s => s.Quantity),
                    SourcePartijen = side.Sources.Select(s => s.Number).Where(n => n != null).Distinct().OrderBy(n => n).ToList(),
                    Vendors = side.Sources.Select(s => s.VendorName).Where(v => v != null).Distinct().OrderBy(v => v).ToList(),
                    // Per orderregel: de kg van de regel, en voor een regel zonder kg de gereserveerde kg van die regel (nooit dubbel).
                    NetKg = side.Lines.Sum(l => l.NetKg ?? side.Sources.Where(s => s.SalesOrderLineId == l.LineId).Sum(s => s.NetKg))
                };
            }
            var lines = facts.LinesByPartij[partij.Id].ToList();
            var contents = facts.ContentsByPartij[partij.Id].ToList();
            var sales = facts.SalesByPartij[partij.Id].ToList();
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
                Kind = partij.Kind,
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
                SoldQuantity = sales.Sum(s => s.Quantity),
                NetKg = received.Sum(c => c.NetWeightKg)
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
