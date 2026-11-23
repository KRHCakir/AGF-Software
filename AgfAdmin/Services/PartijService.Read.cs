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
            string SupplierLotReference, string VendorName, string Product, decimal Expected, string Unit, string SupplierReference,
            decimal? UnitPrice = null, decimal? Discount = null, decimal Rate = 1m);
        private sealed record ContentFact(int PartijId, int Id, int PalletId, string PalletReference, string PalletStatus, int LineId, int OrderId, string OrderNumber,
            string ProductName, string VariantName, string LotNumber, decimal Quantity, decimal Dispatched, decimal SplitOff, string Unit, decimal NetWeightKg,
            string QualityStatus, bool FromCorrection, bool FromSplit, DateTime CreatedAt);
        private sealed record SaleFact(int PartijId, int SalesOrderId, string OrderNumber, string CustomerName, string OrderStatus, string Status, decimal Quantity, string Unit,
            int? SalesPartijId, string SalesPartijNumber);
        // Verkoop-partij: de order, haar regels en per reservering de inkoop-partij waar de colli vandaan komen.
        private sealed record SalesSide(int PartijId, int SalesOrderId, string OrderNumber, string OrderStatus, string CustomerName, List<(int LineId, string Product, decimal Quantity, string Unit, decimal? NetKg)> Lines,
            List<SourceFact> Sources);
        private sealed record SourceFact(int SalesOrderId, int SalesOrderLineId, int? PartijId, string Number, string VendorName, string Product, string Status, decimal Quantity, string Unit, decimal NetKg,
            int? DeliveryId = null, string Origin = null, decimal? UnitPrice = null, decimal? Discount = null, decimal Rate = 1m, DateTime? ShippedAt = null);
        private sealed record Facts(List<LineFact> Lines, List<ContentFact> Contents, List<SaleFact> Sales)
        {
            public ILookup<int, LineFact> LinesByPartij { get; } = Lines.ToLookup(l => l.PartijId);
            public ILookup<int, ContentFact> ContentsByPartij { get; } = Contents.ToLookup(c => c.PartijId);
            public ILookup<int, SaleFact> SalesByPartij { get; } = Sales.ToLookup(s => s.PartijId);
            public Dictionary<int, SalesSide> SalesSides { get; init; } = new();
        }

        // Zoeken op partijnummer (zonder spaties en streepjes), partij van de leverancier, product en leverancier (bij een verkoop-partij:
        // klant, VO-nummer en product); status Open of SoldOut. Soort: Purchase (standaard, zoals P1), Sales of all.
        public async Task<ServiceResult<PartijListDto>> ListAsync(string query, string status, int page, int pageSize, CancellationToken cancellationToken = default, string kind = null,
            SortRequest sort = null)
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
            if (statusFilter == null && sort == null)
            {
                // Zonder statusfilter en zonder gekozen sortering pagineren in de database en alleen de feiten van de gevraagde pagina laden.
                total = await candidates.CountAsync(cancellationToken);
                var pagePartijen = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
                var pageFacts = await LoadFactsAsync(pagePartijen, cancellationToken);
                items = pagePartijen.Select(p => Totals<PartijListItemDto>(p, pageFacts)).ToList();
            }
            else
            {
                // De status en de aantallen zijn afgeleid uit de feiten; daarom eerst de (al door de zoekterm beperkte) kandidaten berekenen,
                // dan filteren op status en sorteren op de gekozen kolom (feedback 22 november) over alle partijen, en pas dan pagineren.
                var partijen = await ordered.ToListAsync(cancellationToken);
                var facts = await LoadFactsAsync(partijen, cancellationToken);
                var matching = ListSort.Apply(partijen.Select(p => Totals<PartijListItemDto>(p, facts)).Where(i => statusFilter == null || i.Status == statusFilter), sort, ListSortFields.Partijen);
                total = matching.Count;
                items = matching.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            }
            return ServiceResult<PartijListDto>.Ok(new PartijListDto { Total = total, Page = page, PageSize = pageSize, Items = items });
        }

        // Voorraad zonder partij: palletregels die nog iets op voorraad hebben (niet geannuleerd) en geen partij. Sinds regel 41 kan dat niet
        // meer ontstaan; in een oude kopie kan het nog bestaan. De gebruiker koppelt de order via Partij › Inkooporder koppelen (met reden).
        public async Task<IReadOnlyList<MissingPartijLineDto>> MissingAsync(CancellationToken cancellationToken = default)
        {
            var lines = await _db.PalletContentLines.AsNoTracking()
                .Where(c => c.PartijId == null && c.Pallet.LogisticsStatus != PalletRules.StatusCancelled && c.Quantity - c.DispatchedQuantity - c.SplitOffQuantity > 0m)
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .Select(c => new MissingPartijLineDto
                {
                    PalletContentLineId = c.Id, PalletId = c.PalletId, PalletReference = c.Pallet.Reference, SupplyCommitmentId = c.SupplyCommitmentLine.SupplyCommitmentId,
                    OrderNumber = c.SupplyCommitmentLine.SupplyCommitment.OrderNumber, VendorName = c.SupplyCommitmentLine.SupplyCommitment.Vendor.Name,
                    Product = c.SupplyCommitmentLine.ProductVariant.Product.Name + " " + c.SupplyCommitmentLine.ProductVariant.Name, LotNumber = c.LotNumber,
                    OnHandQuantity = c.Quantity - c.DispatchedQuantity - c.SplitOffQuantity, Unit = c.Unit, ReceivedAt = c.CreatedAt
                })
                .ToListAsync(cancellationToken);
            foreach (var line in lines)
            {
                line.ReceivedAt = DateTime.SpecifyKind(line.ReceivedAt, DateTimeKind.Utc);
            }
            return lines;
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
            detail.AllowedColors = partij.Kind == PartijRules.KindPurchase ? await LotColorService.AllowedAsync(_db, partij.Id, cancellationToken) : new List<string>();
            detail.CreatedBy = partij.CreatedBy;
            detail.Linked = await IsLinkedAsync(id, cancellationToken);
            detail.NumberFixed = partij.Kind != PartijRules.KindSales ? detail.Linked
                : partij.SalesOrderId == null || await SalesNumberFixedAsync(_db, partij.SalesOrderId.Value, async: true, cancellationToken);
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
                    Id = g.Key, OrderNumber = first.OrderNumber, SupplierReference = first.SupplierReference, VendorName = first.VendorName, OrderedOn = first.OrderedOn, SupplierLotReference = first.SupplierLotReference,
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
            detail.SalesOrders = facts.Sales.GroupBy(s => new { s.SalesOrderId, s.SalesPartijId, s.Unit }).Select(g => new PartijSalesOrderDto
            {
                SalesOrderId = g.Key.SalesOrderId, OrderNumber = g.First().OrderNumber, SalesPartijId = g.First().SalesPartijId, SalesPartijNumber = g.First().SalesPartijNumber,
                CustomerName = g.First().CustomerName, Status = g.First().OrderStatus, Unit = g.Key.Unit,
                ReservedQuantity = g.Where(s => s.Status == ReservationRules.StatusActive).Sum(s => s.Quantity),
                ShippedQuantity = g.Where(s => s.Status == ReservationRules.StatusConsumed).Sum(s => s.Quantity)
            }).OrderBy(s => s.SalesOrderId).ThenBy(s => s.SalesPartijNumber).ThenBy(s => s.Unit).ToList();
            await AddCostsAsync(detail, partij, facts, cancellationToken);
            if (partij.Kind == PartijRules.KindPurchase)
            {
                detail.Recall = await RecallAsync(partij, facts, detail.Unit, cancellationToken);
            }
            else
            {
                detail.Delivery = await DeliveryTraceAsync(partij, facts, cancellationToken);
            }
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

        // Feedback 22 november, herleidbaarheid van een inkoop-partij (recall): wat er binnenkwam, waar het naartoe ging (per verkoop-partij met
        // klant, colli en datum), wat afgekeurd is en wat er nog ligt. Ontvangen = geleverd - teruggenomen + afgekeurd + op voorraad; Verschil
        // toont wat daarvan afwijkt. Geannuleerde pallets tellen nergens mee. Directe facturen staan apart (zij boeken geen voorraad af).
        private async Task<PartijRecallDto> RecallAsync(Partij partij, Facts facts, string unit, CancellationToken cancellationToken)
        {
            var live = facts.Contents.Where(c => c.PalletStatus != PalletRules.StatusCancelled).ToList();
            // Nooit verschillende eenheden bij elkaar optellen: per eenheid een eigen balans; de totalen bovenaan gelden alleen bij één eenheid.
            var byUnit = live.GroupBy(c => c.Unit).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
            {
                var rec = g.Where(IsPhysicalReceipt).Sum(c => c.Quantity);
                var ret = g.Where(c => c.FromCorrection).Sum(c => c.Quantity);
                var shp = g.Sum(c => c.Dispatched);
                var rej = g.Where(c => c.QualityStatus == PalletRules.QualityRejected).Sum(OnHand);
                var hand = g.Where(c => c.QualityStatus != PalletRules.QualityRejected).Sum(OnHand);
                return new PartijRecallUnitDto { Unit = g.Key, Received = rec, Shipped = shp, Returned = ret, Rejected = rej, OnHand = hand, Difference = rec - (shp - ret) - rej - hand };
            }).ToList();
            var single = byUnit.Count == 1 ? byUnit[0] : null;
            if (single != null) unit = single.Unit;
            else if (byUnit.Count > 1) unit = null;
            var shipments = await _db.SalesDispatchLines.AsNoTracking()
                .Where(l => l.PalletContentLine.PartijId == partij.Id)
                .Select(l => new
                {
                    l.Quantity, l.Unit, l.SalesDispatch.DispatchedAt, l.SalesDispatch.SalesOrderId, OrderNumber = l.SalesDispatch.SalesOrder.OrderNumber,
                    Customer = l.SalesDispatch.SalesOrder.Customer.Name,
                    LotId = l.StockReservation.SalesDeliveryId != null ? l.StockReservation.SalesDelivery.PartijId
                        : l.SalesDispatch.SalesOrder.Deliveries.Where(d => d.Sequence == 1).Select(d => (int?)d.PartijId).FirstOrDefault(),
                    LotNumber = l.StockReservation.SalesDeliveryId != null ? l.StockReservation.SalesDelivery.Partij.Number
                        : l.SalesDispatch.SalesOrder.Deliveries.Where(d => d.Sequence == 1).Select(d => d.Partij.Number).FirstOrDefault(),
                })
                .ToListAsync(cancellationToken);
            var invoices = await _db.Set<InvoiceLine>().AsNoTracking().Where(l => l.PartijId == partij.Id)
                .Select(l => new { l.Quantity, l.Unit, l.Invoice.InvoiceNumber, l.Invoice.InvoiceDate, l.Invoice.CustomCustomerName, LotId = (int?)l.Invoice.SalesPartij.Id, LotNumber = l.Invoice.SalesPartij.Number })
                .ToListAsync(cancellationToken);
            return new PartijRecallDto
            {
                Unit = unit, Received = single?.Received ?? 0m, Shipped = single?.Shipped ?? 0m, Returned = single?.Returned ?? 0m,
                Rejected = single?.Rejected ?? 0m, OnHand = single?.OnHand ?? 0m, Difference = single?.Difference ?? 0m, ByUnit = byUnit,
                Destinations = shipments.GroupBy(s => new { s.LotId, s.LotNumber, s.SalesOrderId, s.OrderNumber, s.Customer, s.Unit })
                    .Select(g => new PartijDestinationDto
                    {
                        SalesPartijId = g.Key.LotId, SalesPartijNumber = g.Key.LotNumber, SalesOrderId = g.Key.SalesOrderId, Reference = g.Key.OrderNumber, CustomerName = g.Key.Customer,
                        Quantity = g.Sum(s => s.Quantity), Unit = g.Key.Unit, Date = DateTime.SpecifyKind(g.Max(s => s.DispatchedAt), DateTimeKind.Utc)
                    })
                    .OrderBy(d => d.Date).ThenBy(d => d.SalesPartijNumber).ToList(),
                DirectInvoices = invoices.GroupBy(i => new { i.LotId, i.LotNumber, i.InvoiceNumber, i.CustomCustomerName, i.Unit, i.InvoiceDate })
                    .Select(g => new PartijDestinationDto
                    {
                        SalesPartijId = g.Key.LotId, SalesPartijNumber = g.Key.LotNumber, Reference = g.Key.InvoiceNumber, CustomerName = g.Key.CustomCustomerName,
                        Quantity = g.Sum(i => i.Quantity), Unit = g.Key.Unit, Date = DateTime.SpecifyKind(g.Key.InvoiceDate, DateTimeKind.Utc)
                    })
                    .OrderBy(d => d.Date).ToList(),
            };
        }

        // Feedback 22 november, herleidbaarheid van een verkoop-partij: de klant, de leverdatum (verzonden; anders laden op; anders de gevraagde
        // leverdatum) en per regel de inkoop-partijen waar de colli vandaan komen, met leverancier en herkomst. Bij een directe factuur de
        // regels van de factuur met hun inkoop-partij.
        private async Task<SalesLotTraceDto> DeliveryTraceAsync(Partij partij, Facts facts, CancellationToken cancellationToken)
        {
            if (partij.InvoiceId != null)
            {
                var invoice = await _db.Invoices.AsNoTracking().Where(i => i.Id == partij.InvoiceId)
                    .Select(i => new { i.InvoiceNumber, i.CustomCustomerName, i.DeliveryDate, i.InvoiceDate }).SingleAsync(cancellationToken);
                var lines = await _db.Set<InvoiceLine>().AsNoTracking().Where(l => l.InvoiceId == partij.InvoiceId).OrderBy(l => l.Id)
                    .Select(l => new { l.Description, l.Quantity, l.Unit, l.PartijId, Number = l.Partij.Number, l.OriginCountry,
                        Vendor = _db.SupplyCommitments.Where(c => c.PartijId == l.PartijId).Select(c => c.Vendor.Name).FirstOrDefault(),
                        PurchaseOrigin = _db.SupplyCommitments.Where(c => c.PartijId == l.PartijId).Select(c => c.Origin).FirstOrDefault() })
                    .ToListAsync(cancellationToken);
                return new SalesLotTraceDto
                {
                    CustomerName = invoice.CustomCustomerName, Reference = invoice.InvoiceNumber, Shipped = true,
                    DeliveryDate = DateTime.SpecifyKind(invoice.DeliveryDate ?? invoice.InvoiceDate, DateTimeKind.Utc),
                    Lines = lines.Select(l => new SalesLotTraceLineDto
                    {
                        Product = l.Description, Quantity = l.Quantity, Unit = l.Unit,
                        Sources = l.PartijId == null ? new() : new() { new SalesLotTraceSourceDto { PartijId = l.PartijId, Number = l.Number, VendorName = l.Vendor, Origin = string.IsNullOrWhiteSpace(l.OriginCountry) ? l.PurchaseOrigin : l.OriginCountry, Quantity = l.Quantity, Status = "Invoiced" } }
                    }).ToList(),
                };
            }
            if (!facts.SalesSides.TryGetValue(partij.Id, out var side))
            {
                return null;
            }
            var delivery = await _db.SalesDeliveries.AsNoTracking().Where(d => d.PartijId == partij.Id)
                .Select(d => new { d.Sequence, d.LoadAt, OrderLoadAt = d.SalesOrder.LoadAt, d.SalesOrder.RequestedDeliveryDate }).SingleOrDefaultAsync(cancellationToken);
            var shippedAt = side.Sources.Where(s => s.ShippedAt != null).Select(s => s.ShippedAt).Max();
            DateTime? planned = delivery == null ? null : (delivery.Sequence <= 1 ? delivery.OrderLoadAt : delivery.LoadAt) ?? delivery.RequestedDeliveryDate;
            var date = shippedAt ?? planned;
            return new SalesLotTraceDto
            {
                CustomerName = side.CustomerName, SalesOrderId = side.SalesOrderId, Reference = side.OrderNumber, Sequence = delivery?.Sequence,
                Shipped = shippedAt != null, DeliveryDate = date == null ? null : DateTime.SpecifyKind(date.Value, DateTimeKind.Utc),
                Lines = side.Lines.Select(l => new SalesLotTraceLineDto
                {
                    Product = l.Product, Quantity = l.Quantity, Unit = l.Unit,
                    Sources = side.Sources.Where(s => s.SalesOrderLineId == l.LineId)
                        .GroupBy(s => new { s.PartijId, s.Number, s.VendorName, s.Origin, s.Status })
                        .Select(g => new SalesLotTraceSourceDto { PartijId = g.Key.PartijId, Number = g.Key.Number, VendorName = g.Key.VendorName, Origin = g.Key.Origin, Quantity = g.Sum(s => s.Quantity), Status = g.Key.Status })
                        .OrderBy(s => s.Number).ToList(),
                }).ToList(),
            };
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
                    l.ProductVariant.Product.Name + " " + l.ProductVariant.Name, l.ExpectedQuantity, l.ExpectedUnit, l.SupplyCommitment.SupplierReference,
                    l.UnitPrice, l.DiscountPercentage, l.SupplyCommitment.ExchangeRate))
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
                    r.Status, r.Quantity, r.Unit,
                    // Feedback 22 november: de verkoop-partij van de levering van deze reservering (leeg = de eerste levering van de order).
                    r.SalesDeliveryId != null ? r.SalesDelivery.PartijId : r.SalesOrderLine.SalesOrder.Deliveries.Where(d => d.Sequence == 1).Select(d => (int?)d.PartijId).FirstOrDefault(),
                    r.SalesDeliveryId != null ? r.SalesDelivery.Partij.Number : r.SalesOrderLine.SalesOrder.Deliveries.Where(d => d.Sequence == 1).Select(d => d.Partij.Number).FirstOrDefault()))
                .ToListAsync(cancellationToken);
            var sides = await LoadSalesSidesAsync(partijen, cancellationToken);
            foreach (var invoiceSide in await LoadInvoiceSidesAsync(partijen, cancellationToken))
            {
                sides[invoiceSide.Key] = invoiceSide.Value;
            }
            return new Facts(lines, contents, sales) { SalesSides = sides };
        }

        // Feedback 22 november: een verkoop-partij is één levering. Per partij de reserveringen van haar levering (leeg = de eerste) en per
        // orderregel het aantal van die levering; wat nog niet gereserveerd is, telt bij de laatste levering (zoals op de laadbon).
        private async Task<Dictionary<int, SalesSide>> LoadSalesSidesAsync(List<Partij> partijen, CancellationToken cancellationToken)
        {
            var salesIds = partijen.Where(p => p.Kind == PartijRules.KindSales && p.SalesOrderId != null).Select(p => p.Id).ToList();
            if (salesIds.Count == 0)
            {
                return new Dictionary<int, SalesSide>();
            }
            var deliveries = await _db.SalesDeliveries.AsNoTracking().Where(d => salesIds.Contains(d.PartijId))
                .Select(d => new { d.Id, d.PartijId, d.SalesOrderId, d.Sequence }).ToListAsync(cancellationToken);
            var orderIds = deliveries.Select(d => d.SalesOrderId).Distinct().ToList();
            var lastSequence = (await _db.SalesDeliveries.AsNoTracking().Where(d => orderIds.Contains(d.SalesOrderId)).Select(d => new { d.SalesOrderId, d.Sequence }).ToListAsync(cancellationToken))
                .GroupBy(d => d.SalesOrderId).ToDictionary(g => g.Key, g => g.Max(d => d.Sequence));
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
                    r.Status, r.Quantity, r.Unit, r.NetWeightKg ?? 0m, r.SalesDeliveryId,
                    r.SupplyCommitmentLine.SupplyCommitment.Origin ?? r.SupplyCommitmentLine.ProductVariant.OriginCountryCode,
                    r.SupplyCommitmentLine.UnitPrice, r.SupplyCommitmentLine.DiscountPercentage, r.SupplyCommitmentLine.SupplyCommitment.ExchangeRate,
                    r.DispatchLine != null ? r.DispatchLine.SalesDispatch.DispatchedAt : (DateTime?)null))
                .ToListAsync(cancellationToken);
            var result = new Dictionary<int, SalesSide>();
            foreach (var delivery in deliveries)
            {
                var order = orders.Single(o => o.Id == delivery.SalesOrderId);
                var last = lastSequence[delivery.SalesOrderId] == delivery.Sequence;
                var orderSources = sources.Where(s => s.SalesOrderId == order.Id).ToList();
                var mine = orderSources.Where(s => s.DeliveryId == delivery.Id || (s.DeliveryId == null && delivery.Sequence == 1)).ToList();
                var deliveryLines = new List<(int LineId, string Product, decimal Quantity, string Unit, decimal? NetKg)>();
                foreach (var line in lines.Where(l => l.SalesOrderId == order.Id).OrderBy(l => l.Id))
                {
                    var open = Math.Max(0m, line.Quantity - orderSources.Where(s => s.SalesOrderLineId == line.Id).Sum(s => s.Quantity));
                    var quantity = mine.Where(s => s.SalesOrderLineId == line.Id).Sum(s => s.Quantity) + (last ? open : 0m);
                    if (quantity <= 0m) continue;
                    decimal? kg = line.NetWeightKg is decimal lineKg && line.Quantity > 0m
                        ? (quantity == line.Quantity ? lineKg : Math.Round(lineKg * quantity / line.Quantity, 3, MidpointRounding.AwayFromZero)) : null;
                    deliveryLines.Add((line.Id, line.Product, quantity, line.Unit, kg));
                }
                result[delivery.PartijId] = new SalesSide(delivery.PartijId, order.Id, order.OrderNumber, order.Status, order.Customer, deliveryLines, mine);
            }
            return result;
        }

        // Feedback 22 november: de verkoop-partij van een directe factuur, als "verkoopkant" zonder order: de factuurregels met hun inkoop-partij.
        // SalesOrderId 0 = geen order; het factuurnummer staat op de plek van het ordernummer.
        private async Task<Dictionary<int, SalesSide>> LoadInvoiceSidesAsync(List<Partij> partijen, CancellationToken cancellationToken)
        {
            var invoiceIds = partijen.Where(p => p.Kind == PartijRules.KindSales && p.InvoiceId != null).ToDictionary(p => p.InvoiceId.Value, p => p.Id);
            if (invoiceIds.Count == 0)
            {
                return new Dictionary<int, SalesSide>();
            }
            var ids = invoiceIds.Keys.ToList();
            var invoices = await _db.Invoices.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.InvoiceNumber, i.CustomCustomerName }).ToListAsync(cancellationToken);
            var lines = await _db.Set<InvoiceLine>().AsNoTracking().Where(l => ids.Contains(l.InvoiceId))
                .Select(l => new { l.Id, l.InvoiceId, l.Description, l.Quantity, l.Unit, l.NetWeight, l.PartijId, l.ProductVariantId, Number = l.Partij.Number,
                    Vendor = _db.SupplyCommitments.Where(c => c.PartijId == l.PartijId).Select(c => c.Vendor.Name).FirstOrDefault() })
                .ToListAsync(cancellationToken);
            // Inkoopprijs van de inkoop-partij: alleen de inkoopregel van hetzelfde product; zonder passende regel blijft de prijs onbekend.
            var purchasePartijIds = lines.Where(l => l.PartijId != null).Select(l => l.PartijId.Value).Distinct().ToList();
            var costs = await _db.SupplyCommitmentLines.AsNoTracking().Where(c => c.PartijId != null && purchasePartijIds.Contains(c.PartijId.Value) && c.UnitPrice != null)
                .OrderBy(c => c.Id)
                .Select(c => new { PartijId = c.PartijId.Value, c.ProductVariantId, c.UnitPrice, c.DiscountPercentage, c.SupplyCommitment.ExchangeRate }).ToListAsync(cancellationToken);
            return invoices.ToDictionary(i => invoiceIds[i.Id], i => new SalesSide(invoiceIds[i.Id], 0, i.InvoiceNumber, "Invoiced", i.CustomCustomerName,
                lines.Where(l => l.InvoiceId == i.Id).OrderBy(l => l.Id).Select(l => (l.Id, l.Description, l.Quantity, l.Unit, (decimal?)(l.NetWeight > 0m ? l.NetWeight : null))).ToList(),
                lines.Where(l => l.InvoiceId == i.Id && l.PartijId != null)
                    .Select(l =>
                    {
                        var mine = costs.Where(c => c.PartijId == l.PartijId).ToList();
                        var cost = mine.FirstOrDefault(c => c.ProductVariantId == l.ProductVariantId);
                        return new SourceFact(0, l.Id, l.PartijId, l.Number, l.Vendor, l.Description, "Invoiced", l.Quantity, l.Unit, l.NetWeight,
                            null, null, cost?.UnitPrice, cost?.DiscountPercentage, cost?.ExchangeRate ?? 1m);
                    }).ToList()));
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
                    SalesOrderId = side.SalesOrderId > 0 ? side.SalesOrderId : null, SalesOrderNumber = side.OrderNumber, SalesOrderStatus = side.OrderStatus, CustomerName = side.CustomerName,
                    Products = side.Lines.Select(l => l.Product).Distinct().OrderBy(p => p).ToList(),
                    Unit = SingleUnit(side.Lines.Select(l => l.Unit)),
                    OrderedQuantity = side.Lines.Sum(l => l.Quantity),
                    SoldQuantity = side.Sources.Sum(s => s.Quantity),
                    SourcePartijen = side.Sources.Select(s => s.Number).Where(n => n != null).Distinct().OrderBy(n => n).ToList(),
                    Vendors = side.Sources.Select(s => s.VendorName).Where(v => v != null).Distinct().OrderBy(v => v).ToList(),
                    // Per orderregel: de kg van de regel, en voor een regel zonder kg de gereserveerde kg van die regel (nooit dubbel).
                    NetKg = side.Lines.Sum(l => l.NetKg ?? side.Sources.Where(s => s.SalesOrderLineId == l.LineId).Sum(s => s.NetKg)),
                    // Alleen een volledig gedekte levering heeft een inkoopwaarde: elke regel moet in zijn geheel uit (geprijsde) bronnen komen.
                    PurchaseValue = side.Lines.All(l => side.Sources.Where(s => s.SalesOrderLineId == l.LineId).Sum(s => s.Quantity) >= l.Quantity)
                        ? Value(side.Sources.Select(s => (s.Quantity, s.UnitPrice, s.Discount, s.Rate))) : null
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
                Color = partij.Color,
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
                NetKg = received.Sum(c => c.NetWeightKg),
                PurchaseValue = Value(liveLines.Select(l => (l.Expected, l.UnitPrice, l.Discount, l.Rate)))
            };
        }

        // Inkoopwaarde in euro (feedback 22 november, Verdeel naar waarde): aantal × prijs min korting, gedeeld door de koers van de order, op
        // centen. Heeft één regel geen prijs, dan is de waarde onvolledig en dus null
        // (dan doet de partij niet mee met verdelen naar waarde); een te lage waarde zou de verdeling vertekenen.
        private static decimal? Value(IEnumerable<(decimal Quantity, decimal? UnitPrice, decimal? Discount, decimal Rate)> parts)
        {
            var priced = parts.ToList();
            if (priced.Count == 0 || priced.Any(p => p.UnitPrice == null)) return null;
            return priced.Sum(p => ExchangeRateRules.ToEuro(OrderLineMath.LineAmount(p.Quantity, p.UnitPrice, p.Discount) ?? 0m, p.Rate > 0m ? p.Rate : 1m));
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
