using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Inkooporders bovenop de bestaande toezeggingen (SupplyCommitment = order, PalletContentLine = ontvangst): lijst en detail.
    // Factuurnummers koppelen: PurchaseOrderService.InvoiceRefs.cs; afkeuren: PurchaseOrderService.Rejections.cs.
    // Schrijft nooit voorraadmutaties of reserveringen.
    public sealed partial class PurchaseOrderService
    {
        private readonly AppDbContext _db;

        public PurchaseOrderService(AppDbContext db)
        {
            _db = db;
        }

        // ---------------- Lijst ----------------

        // Status: leeg of "open" = Besteld + Deels ontvangen (standaard), "all" = alles, of één weergavestatus. De weergavestatus volgt
        // uit ontvangen en afgekeurde hoeveelheden en wordt daarom na de SQL-filters (zoeken, leverancier) in het geheugen bepaald;
        // daarna sorteren (nieuwste eerst) en pagineren op de server.
        public async Task<ServiceResult<PurchaseOrderListDto>> ListAsync(string query, string status, int? vendorId, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            // Hoofdletterongevoelig ("Open", "received"), maar altijd terug naar een waarde uit de toelatingslijst.
            var statusFilter = string.IsNullOrWhiteSpace(status) ? "open"
                : new[] { "open", "all" }.Concat(PurchaseOrderRules.OrderStatuses).FirstOrDefault(s => string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (statusFilter == null)
            {
                return ServiceResult<PurchaseOrderListDto>.Validation(new Dictionary<string, string[]>
                {
                    ["status"] = new[] { $"Status must be open, all or one of: {string.Join(", ", PurchaseOrderRules.OrderStatuses)}." }
                });
            }
            page = Math.Max(1, page);
            pageSize = pageSize <= 0 ? PurchaseOrderRules.DefaultPageSize : Math.Min(pageSize, PurchaseOrderRules.MaxPageSize);

            var commitments = _db.SupplyCommitments.AsNoTracking();
            if (vendorId != null) commitments = commitments.Where(c => c.VendorId == vendorId);
            var term = (query ?? "").Trim().ToLowerInvariant();
            if (term != "")
            {
                // Ordernummer, leveranciersreferentie, leverancier en factuur- of creditnotanummer.
                commitments = commitments.Where(c => c.OrderNumber.ToLower().Contains(term)
                    || (c.SupplierReference != null && c.SupplierReference.ToLower().Contains(term))
                    || (c.Vendor.Name != null && c.Vendor.Name.ToLower().Contains(term))
                    || _db.SupplyCommitmentInvoiceRefs.Any(r => r.SupplyCommitmentId == c.Id && r.InvoiceNumberKey.Contains(term)));
            }
            // Voorfilter op de opgeslagen status: Geannuleerd en Afgekeurd zijn opgeslagen eindtoestanden.
            if (statusFilter == PurchaseOrderRules.OrderCancelled) commitments = commitments.Where(c => c.Status == SupplyRules.StatusCancelled);
            else if (statusFilter != "all" && statusFilter != PurchaseOrderRules.OrderRejected) commitments = commitments.Where(c => c.Status != SupplyRules.StatusCancelled && c.Status != SupplyRules.StatusRejected);

            var headers = await commitments
                .Select(c => new
                {
                    c.Id, c.OrderNumber, c.OrderYear, c.OrderSequence, c.VendorId, VendorName = c.Vendor.Name, c.SupplierReference, c.OrderedOn,
                    c.ExpectedAt, c.Route, CustomerName = c.Customer.Name, c.Status
                })
                .ToListAsync(cancellationToken);
            var facts = await LoadFactsAsync(headers.Select(h => h.Id).ToList(), cancellationToken);
            var withDeviation = await OrdersWithDeviationAsync(headers.Select(h => h.Id).ToList(), cancellationToken);

            var items = headers
                .Select(h =>
                {
                    var f = facts.For(h.Id);
                    return (Year: h.OrderYear, Sequence: h.OrderSequence, Dto: new PurchaseOrderListItemDto
                    {
                        Id = h.Id,
                        OrderNumber = h.OrderNumber,
                        VendorId = h.VendorId,
                        VendorName = h.VendorName,
                        SupplierReference = h.SupplierReference,
                        OrderedOn = h.OrderedOn,
                        ExpectedAt = h.ExpectedAt == null ? null : DateTime.SpecifyKind(h.ExpectedAt.Value, DateTimeKind.Utc),
                        LinesOrigin = f.LinesOrigin,
                        Route = h.Route,
                        CustomerName = h.CustomerName,
                        StoredStatus = h.Status,
                        Status = f.Status(h.Status),
                        ExpectedQuantity = f.Lines.Sum(l => l.ExpectedQuantity),
                        ExpectedNetWeightKg = f.Lines.Any(l => l.ExpectedNetWeightKg != null) ? f.Lines.Sum(l => l.ExpectedNetWeightKg ?? 0m) : null,
                        ReceivedQuantity = f.Lines.Sum(l => l.ReceivedQuantity),
                        RejectedQuantity = f.RejectedQuantity,
                        InvoiceNumbers = f.Refs.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
                            .Select(r => new PurchaseOrderInvoiceNumberDto { Kind = r.Kind, InvoiceNumber = r.InvoiceNumber }).ToList(),
                        InvoiceIndicator = f.InvoiceIndicator(),
                        HasDeviation = withDeviation.Contains(h.Id)
                    });
                })
                .Where(i => statusFilter == "all"
                    || (statusFilter == "open" ? PurchaseOrderRules.OpenOrderStatuses.Contains(i.Dto.Status, StringComparer.Ordinal) : i.Dto.Status == statusFilter))
                .OrderByDescending(i => i.Year).ThenByDescending(i => i.Sequence).ThenByDescending(i => i.Dto.Id)
                .ToList();

            return ServiceResult<PurchaseOrderListDto>.Ok(new PurchaseOrderListDto
            {
                Total = items.Count,
                Page = page,
                PageSize = pageSize,
                Items = items.Skip((page - 1) * pageSize).Take(pageSize).Select(i => i.Dto).ToList()
            });
        }

        // Orders waaraan dit factuurnummer hangt (via de sleutel: zonder hoofdletters en spaties aan de randen); met leverancier alleen diens orders.
        public async Task<IReadOnlyList<PurchaseOrderInvoiceMatchDto>> FindByInvoiceNumberAsync(string invoiceNumber, int? vendorId, CancellationToken cancellationToken = default)
        {
            var key = PurchaseOrderRules.InvoiceNumberKey(invoiceNumber);
            if (key == null)
            {
                return Array.Empty<PurchaseOrderInvoiceMatchDto>();
            }
            var refs = _db.SupplyCommitmentInvoiceRefs.AsNoTracking().Where(r => r.InvoiceNumberKey == key);
            if (vendorId != null) refs = refs.Where(r => r.SupplyCommitment.VendorId == vendorId);
            return await refs
                .OrderBy(r => r.SupplyCommitment.OrderYear).ThenBy(r => r.SupplyCommitment.OrderSequence).ThenBy(r => r.Id)
                .Select(r => new PurchaseOrderInvoiceMatchDto { Id = r.SupplyCommitmentId, OrderNumber = r.SupplyCommitment.OrderNumber, Kind = r.Kind, VendorName = r.SupplyCommitment.Vendor.Name })
                .Take(20)
                .ToListAsync(cancellationToken);
        }

        // ---------------- Detail ----------------

        public async Task<ServiceResult<PurchaseOrderDetailDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var commitment = await _db.SupplyCommitments.AsNoTracking()
                .Include(c => c.Vendor)
                .Include(c => c.Customer)
                .Include(c => c.Lines).ThenInclude(l => l.ProductVariant).ThenInclude(v => v.Product)
                .AsSplitQuery()
                .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (commitment == null)
            {
                return ServiceResult<PurchaseOrderDetailDto>.NotFound($"Purchase order {id} does not exist.");
            }

            var facts = (await LoadFactsAsync(new List<int> { id }, cancellationToken)).For(id);
            var contents = await _db.PalletContentLines.AsNoTracking()
                .Where(c => c.SupplyCommitmentLine.SupplyCommitmentId == id)
                .Select(c => new
                {
                    c.Id, c.PalletId, PalletReference = c.Pallet.Reference, PalletStatus = c.Pallet.LogisticsStatus, c.SupplyCommitmentLineId,
                    ProductName = c.SupplyCommitmentLine.ProductVariant.Product.Name, VariantName = c.SupplyCommitmentLine.ProductVariant.Name,
                    c.LotNumber, c.Quantity, c.Unit, c.NetWeightKg, c.WeightMethod, c.NetKgPerPackage, c.DispatchedQuantity, c.DispatchedNetWeightKg,
                    c.SplitOffQuantity, c.SplitOffNetWeightKg, c.QualityStatus, c.QualityReason, c.OriginCorrectionLineId, c.OriginRejectionLineId, c.CreatedAt, c.Version,
                    c.ExpectedShownBeforeEntry, c.ExpectedShownAt, c.ExpectedShownBy
                })
                .OrderBy(c => c.Id)
                .ToListAsync(cancellationToken);
            var contentIds = contents.Select(c => c.Id).ToList();
            var reserved = await ReservedByContentAsync(contentIds, cancellationToken);
            var qualityEvents = await _db.PalletAuditEvents.AsNoTracking()
                .Where(e => e.PalletContentLineId != null && contentIds.Contains(e.PalletContentLineId.Value) && e.EventType == PalletService.QualityStatusChangedEvent)
                .Select(e => new { e.PalletContentLineId, e.OldValue, e.NewValue, e.ActorReference, e.OccurredAt })
                .ToListAsync(cancellationToken);
            var rejections = await _db.SupplyRejections.AsNoTracking()
                .Where(r => r.SupplyCommitmentId == id)
                .Include(r => r.Lines).ThenInclude(l => l.PalletContentLine).ThenInclude(c => c.Pallet)
                .OrderBy(r => r.RejectedAt).ThenBy(r => r.Id)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);

            var deviations = await _db.SupplyReceiptDeviations.AsNoTracking()
                .Where(d => d.SupplyCommitmentId == id)
                .Select(d => new { Deviation = d, PalletReference = d.PalletContentLine.Pallet.Reference })
                .OrderBy(d => d.Deviation.CreatedAt).ThenBy(d => d.Deviation.Id)
                .ToListAsync(cancellationToken);

            var wholeRejected = rejections.SelectMany(r => r.Lines).Where(l => !l.SplitOff).Select(l => l.PalletContentLineId).ToHashSet();
            var receipts = contents.Select(c =>
            {
                var (rq, rkg) = reserved.GetValueOrDefault(c.Id);
                var onHand = ReservationRules.OnHandQuantity(c.Quantity, c.DispatchedQuantity, c.SplitOffQuantity);
                var onHandKg = ReservationRules.OnHandQuantity(c.NetWeightKg, c.DispatchedNetWeightKg, c.SplitOffNetWeightKg);
                var rejectable = c.PalletStatus == PalletRules.StatusReceived && c.QualityStatus != PalletRules.QualityRejected && onHand - rq > 0m
                    && !PurchaseOrderRules.IsCoveredByRejection(c.OriginRejectionLineId, wholeRejected.Contains(c.Id));
                return new PurchaseOrderReceiptLineDto
                {
                    PalletContentLineId = c.Id,
                    PalletId = c.PalletId,
                    PalletReference = c.PalletReference,
                    PalletStatus = c.PalletStatus,
                    SupplyCommitmentLineId = c.SupplyCommitmentLineId,
                    ProductName = c.ProductName,
                    ProductVariantName = c.VariantName,
                    LotNumber = c.LotNumber,
                    Quantity = c.Quantity,
                    Unit = c.Unit,
                    NetWeightKg = c.NetWeightKg,
                    WeightMethod = c.WeightMethod,
                    NetKgPerPackage = c.NetKgPerPackage,
                    OnHandQuantity = onHand,
                    OnHandNetWeightKg = onHandKg,
                    ReservedQuantity = rq,
                    ReservedNetWeightKg = rkg,
                    QualityStatus = c.QualityStatus,
                    QualityReason = c.QualityReason,
                    Origin = c.OriginCorrectionLineId != null ? "DispatchCorrection" : c.OriginRejectionLineId != null ? "RejectionSplit" : "Receipt",
                    ReceivedAt = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
                    ExpectedShownBeforeEntry = c.ExpectedShownBeforeEntry,
                    ExpectedShownAt = c.ExpectedShownAt == null ? null : DateTime.SpecifyKind(c.ExpectedShownAt.Value, DateTimeKind.Utc),
                    ExpectedShownBy = c.ExpectedShownBy,
                    Rejectable = rejectable,
                    RejectableQuantity = rejectable ? onHand - rq : 0m,
                    RejectableNetWeightKg = rejectable ? Math.Max(0m, onHandKg - rkg) : 0m,
                    Version = c.Version
                };
            }).ToList();

            var history = new List<PurchaseOrderHistoryEventDto>
            {
                new() { OccurredAt = Utc(commitment.CreatedAt), Type = "Ordered", Reference = commitment.SupplierReference, Channel = commitment.OrderChannel }
            };
            history.AddRange(contents.Where(c => c.OriginCorrectionLineId == null && c.OriginRejectionLineId == null && c.PalletStatus != PalletRules.StatusCancelled).Select(c => new PurchaseOrderHistoryEventDto
            {
                OccurredAt = Utc(c.CreatedAt), Type = "Received", Reference = c.PalletReference, Quantity = c.Quantity, Unit = c.Unit
            }));
            history.AddRange(qualityEvents.Select(e => new PurchaseOrderHistoryEventDto
            {
                OccurredAt = Utc(e.OccurredAt), Type = "QualityChanged", ActorReference = e.ActorReference,
                Reference = contents.FirstOrDefault(c => c.Id == e.PalletContentLineId)?.PalletReference,
                FromStatus = SnapshotStatus(e.OldValue), ToStatus = SnapshotStatus(e.NewValue)
            }));
            history.AddRange(facts.Refs.Select(r => new PurchaseOrderHistoryEventDto
            {
                OccurredAt = Utc(r.CreatedAt), Type = r.Kind == PurchaseOrderRules.KindCreditNote ? "CreditNoteRecorded" : "InvoiceRecorded",
                ActorReference = r.ActorReference, Reference = r.InvoiceNumber
            }));
            history.AddRange(rejections.Select(r => new PurchaseOrderHistoryEventDto
            {
                OccurredAt = Utc(r.RejectedAt), Type = "Rejected", ActorReference = r.ActorReference, Reason = r.Reason, Decision = r.Decision,
                Quantity = r.Lines.Sum(l => l.Quantity), Unit = r.Lines.Select(l => l.Unit).Distinct().Count() == 1 ? r.Lines.First().Unit : null
            }));
            history.AddRange(deviations.Select(d => new PurchaseOrderHistoryEventDto
            {
                OccurredAt = Utc(d.Deviation.CreatedAt), Type = "DeviationRecorded", ActorReference = d.Deviation.ActorReference, Reference = d.PalletReference,
                Quantity = d.Deviation.Quantity, Unit = d.Deviation.Unit, Reason = d.Deviation.Reason, Kinds = d.Deviation.Kinds
            }));

            var lines = commitment.Lines.OrderBy(l => l.Id).Select(l =>
            {
                var line = facts.Lines.Single(f => f.Id == l.Id);
                return new PurchaseOrderLineDto
                {
                    Id = l.Id,
                    ProductVariantId = l.ProductVariantId,
                    ProductName = l.ProductVariant?.Product?.Name,
                    ProductVariantName = l.ProductVariant?.Name,
                    Origin = l.ProductVariant?.Origin,
                    Size = l.ProductVariant?.SizeCategory,
                    Packaging = l.ProductVariant?.Packaging,
                    LotNumber = l.LotNumber,
                    ExpectedQuantity = l.ExpectedQuantity,
                    Unit = l.ExpectedUnit,
                    ExpectedNetWeightKg = l.ExpectedNetWeightKg,
                    ReplacesLineId = l.ReplacesLineId,
                    ReplacedByLineIds = commitment.Lines.Where(r => r.ReplacesLineId == l.Id).OrderBy(r => r.Id).Select(r => r.Id).ToList(),
                    ReceivedQuantity = line.ReceivedQuantity,
                    ReceivedNetWeightKg = line.ReceivedNetWeightKg,
                    RejectedQuantity = line.RejectedQuantity,
                    RejectedNetWeightKg = line.RejectedNetWeightKg
                };
            }).ToList();

            return ServiceResult<PurchaseOrderDetailDto>.Ok(new PurchaseOrderDetailDto
            {
                Id = commitment.Id,
                OrderNumber = commitment.OrderNumber,
                VendorId = commitment.VendorId,
                VendorName = commitment.Vendor?.Name,
                SupplierReference = commitment.SupplierReference,
                OrderedOn = commitment.OrderedOn,
                OrderChannel = commitment.OrderChannel,
                Note = commitment.Note,
                Route = commitment.Route,
                CustomerId = commitment.CustomerId,
                CustomerName = commitment.Customer?.Name,
                Origin = commitment.Origin,
                LinesOrigin = facts.LinesOrigin,
                Destination = commitment.Destination,
                ExpectedAt = commitment.ExpectedAt == null ? null : Utc(commitment.ExpectedAt.Value),
                CreatedAt = Utc(commitment.CreatedAt),
                StoredStatus = commitment.Status,
                Status = facts.Status(commitment.Status),
                InvoiceIndicator = facts.InvoiceIndicator(),
                ExpectedQuantity = lines.Sum(l => l.ExpectedQuantity),
                ExpectedNetWeightKg = lines.Any(l => l.ExpectedNetWeightKg != null) ? lines.Sum(l => l.ExpectedNetWeightKg ?? 0m) : null,
                ReceivedQuantity = lines.Sum(l => l.ReceivedQuantity),
                ReceivedNetWeightKg = lines.Sum(l => l.ReceivedNetWeightKg),
                RejectedQuantity = facts.RejectedQuantity,
                RejectedNetWeightKg = facts.RejectedNetWeightKg,
                Lines = lines,
                Receipts = receipts,
                InvoiceRefs = facts.Refs.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Select(r => new PurchaseOrderInvoiceRefDto
                {
                    Id = r.Id, Kind = r.Kind, InvoiceNumber = r.InvoiceNumber, InvoiceDate = r.InvoiceDate, AmountExclVat = r.AmountExclVat,
                    IncomingInvoiceId = r.IncomingInvoiceId, ActorReference = r.ActorReference, CreatedAt = Utc(r.CreatedAt)
                }).ToList(),
                Rejections = rejections.Select(r => new PurchaseOrderRejectionDto
                {
                    Id = r.Id, Reason = r.Reason, Explanation = r.Explanation, Decision = r.Decision, ActorReference = r.ActorReference, RejectedAt = Utc(r.RejectedAt),
                    Lines = r.Lines.OrderBy(l => l.Id).Select(l => new PurchaseOrderRejectionLineDto
                    {
                        PalletContentLineId = l.PalletContentLineId, PalletReference = l.PalletContentLine?.Pallet?.Reference, LotNumber = l.PalletContentLine?.LotNumber,
                        Quantity = l.Quantity, Unit = l.Unit, NetWeightKg = l.NetWeightKg, SplitOff = l.SplitOff
                    }).ToList()
                }).ToList(),
                Deviations = deviations.Select(d => new PurchaseOrderDeviationDto
                {
                    Id = d.Deviation.Id, OriginalLineId = d.Deviation.OriginalLineId, NewLineId = d.Deviation.NewLineId,
                    PalletContentLineId = d.Deviation.PalletContentLineId, PalletReference = d.PalletReference,
                    Kinds = d.Deviation.Kinds.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    Expected = ReceiptDeviationService.ReadVariant(d.Deviation.ExpectedVariantSnapshot),
                    Received = ReceiptDeviationService.ReadVariant(d.Deviation.ReceivedVariantSnapshot),
                    Quantity = d.Deviation.Quantity, Unit = d.Deviation.Unit, ReducedQuantity = d.Deviation.ReducedQuantity,
                    ExpectedKgPerPackage = d.Deviation.ExpectedKgPerPackage, ReceivedKgPerPackage = d.Deviation.ReceivedKgPerPackage,
                    Reason = d.Deviation.Reason, ReasonNote = d.Deviation.ReasonNote, ActorReference = d.Deviation.ActorReference, CreatedAt = Utc(d.Deviation.CreatedAt)
                }).ToList(),
                History = history.OrderBy(h => h.OccurredAt).ToList()
            });
        }

        // ---------------- Gedeelde feiten ----------------

        private sealed class LineFact
        {
            public int Id { get; init; }
            public decimal ExpectedQuantity { get; init; }
            public string Unit { get; init; }
            public decimal? ExpectedNetWeightKg { get; init; }
            public string Origin { get; init; }
            // In de eenheid van de regel (voor de status); ReceivedAllUnits telt elke eenheid (voor "alles afgekeurd").
            public decimal ReceivedQuantity { get; set; }
            public decimal ReceivedNetWeightKg { get; set; }
            public decimal ReceivedAllUnitsQuantity { get; set; }
            public decimal RejectedQuantity { get; set; }
            public decimal RejectedNetWeightKg { get; set; }
            // Afgekeurd met terugsturen of vernietigen (zonder korting claimen): bepaalt "alles afgekeurd".
            public decimal RemovedQuantity { get; set; }
        }

        private sealed record RefFact(int Id, int SupplyCommitmentId, string Kind, string InvoiceNumber, DateOnly? InvoiceDate, decimal? AmountExclVat, int? IncomingInvoiceId, string ActorReference, DateTime CreatedAt);

        private sealed class OrderFacts
        {
            public List<LineFact> Lines { get; } = new();
            public List<RefFact> Refs { get; } = new();
            public List<(string Decision, DateTime RejectedAt)> Rejections { get; } = new();
            public decimal RejectedQuantity => Lines.Sum(l => l.RejectedQuantity);
            public decimal RejectedNetWeightKg => Lines.Sum(l => l.RejectedNetWeightKg);
            public decimal RemovedQuantity => Lines.Sum(l => l.RemovedQuantity);
            // Regels die nog iets verwachten of iets ontvingen; een geheel vervangen regel zonder ontvangst telt niet mee.
            public string LinesOrigin => SupplyRules.LinesOrigin(Lines.Where(l => l.ExpectedQuantity > 0m || l.ReceivedAllUnitsQuantity > 0m).Select(l => l.Origin));

            // Volledig afgekeurd telt ook als de opgeslagen status (nog) niet is bijgewerkt.
            public string Status(string storedStatus) =>
                storedStatus != SupplyRules.StatusCancelled && PurchaseOrderRules.IsFullyRejected(Lines.Sum(l => l.ReceivedAllUnitsQuantity), RemovedQuantity)
                    ? PurchaseOrderRules.OrderRejected
                    : PurchaseOrderRules.OrderStatus(storedStatus, Lines.Select(l => (l.ExpectedQuantity, l.ReceivedQuantity)));

            public string InvoiceIndicator() =>
                PurchaseOrderRules.InvoiceIndicator(Refs.Select(r => (r.Kind, r.CreatedAt)), Rejections);
        }

        private sealed class FactSet
        {
            public Dictionary<int, OrderFacts> ByOrder { get; } = new();
            public OrderFacts For(int id) => ByOrder.TryGetValue(id, out var facts) ? facts : new OrderFacts();
        }

        // In batches, zodat de IN-clauses onder de SQLite-parameterlimiet blijven.
        private async Task<FactSet> LoadFactsAsync(List<int> ids, CancellationToken cancellationToken)
        {
            var set = new FactSet();
            foreach (var batch in ids.Chunk(500))
            {
                await LoadFactsBatchAsync(batch.ToList(), set, cancellationToken);
            }
            return set;
        }

        private async Task LoadFactsBatchAsync(List<int> ids, FactSet set, CancellationToken cancellationToken)
        {
            var lines = await _db.SupplyCommitmentLines.AsNoTracking().Where(l => ids.Contains(l.SupplyCommitmentId))
                .Select(l => new { l.Id, l.SupplyCommitmentId, l.ExpectedQuantity, l.ExpectedUnit, l.ExpectedNetWeightKg, l.ProductVariant.Origin })
                .ToListAsync(cancellationToken);
            // Fysieke ontvangst: geen teruggeboekte of afgesplitste inhoud (al bij de oorspronkelijke regel geteld), geen geannuleerde pallet.
            var receipts = await _db.PalletContentLines.AsNoTracking()
                .Where(c => ids.Contains(c.SupplyCommitmentLine.SupplyCommitmentId) && c.OriginCorrectionLineId == null && c.OriginRejectionLineId == null
                    && c.Pallet.LogisticsStatus != PalletRules.StatusCancelled)
                .Select(c => new { c.SupplyCommitmentLineId, c.Quantity, c.Unit, c.NetWeightKg })
                .ToListAsync(cancellationToken);
            var rejected = await _db.SupplyRejectionLines.AsNoTracking().Where(l => ids.Contains(l.SupplyRejection.SupplyCommitmentId))
                .Select(l => new { l.SupplyCommitmentLineId, l.Quantity, l.NetWeightKg, l.SupplyRejection.Decision })
                .ToListAsync(cancellationToken);
            var rejections = await _db.SupplyRejections.AsNoTracking().Where(r => ids.Contains(r.SupplyCommitmentId))
                .Select(r => new { r.SupplyCommitmentId, r.Decision, r.RejectedAt })
                .ToListAsync(cancellationToken);
            var refs = await _db.SupplyCommitmentInvoiceRefs.AsNoTracking().Where(r => ids.Contains(r.SupplyCommitmentId))
                .Select(r => new RefFact(r.Id, r.SupplyCommitmentId, r.Kind, r.InvoiceNumber, r.InvoiceDate, r.AmountExclVat, r.IncomingInvoiceId, r.ActorReference, r.CreatedAt))
                .ToListAsync(cancellationToken);

            OrderFacts Order(int id)
            {
                if (!set.ByOrder.TryGetValue(id, out var facts))
                {
                    facts = new OrderFacts();
                    set.ByOrder[id] = facts;
                }
                return facts;
            }

            var byLine = new Dictionary<int, LineFact>();
            foreach (var line in lines.OrderBy(l => l.Id))
            {
                var fact = new LineFact { Id = line.Id, ExpectedQuantity = line.ExpectedQuantity, Unit = line.ExpectedUnit, ExpectedNetWeightKg = line.ExpectedNetWeightKg, Origin = line.Origin };
                byLine[line.Id] = fact;
                Order(line.SupplyCommitmentId).Lines.Add(fact);
            }
            foreach (var receipt in receipts)
            {
                if (!byLine.TryGetValue(receipt.SupplyCommitmentLineId, out var line)) continue;
                line.ReceivedAllUnitsQuantity += receipt.Quantity;
                // Nooit eenheden omrekenen: alleen inhoud in de eenheid van de regel telt als ontvangen tegen de verwachting.
                if (line.Unit == null || string.Equals(line.Unit, receipt.Unit, StringComparison.Ordinal))
                {
                    line.ReceivedQuantity += receipt.Quantity;
                    line.ReceivedNetWeightKg += receipt.NetWeightKg;
                }
            }
            foreach (var line in rejected)
            {
                if (!byLine.TryGetValue(line.SupplyCommitmentLineId, out var fact)) continue;
                fact.RejectedQuantity += line.Quantity;
                fact.RejectedNetWeightKg += line.NetWeightKg;
                if (!PurchaseOrderRules.KeepsGoods(line.Decision)) fact.RemovedQuantity += line.Quantity;
            }
            foreach (var rejection in rejections) Order(rejection.SupplyCommitmentId).Rejections.Add((rejection.Decision, rejection.RejectedAt));
            foreach (var reference in refs) Order(reference.SupplyCommitmentId).Refs.Add(reference);
        }

        // Orders met minstens één vastgelegde afwijkende ontvangst (lijstaanduiding), in batches zoals LoadFactsAsync.
        private async Task<HashSet<int>> OrdersWithDeviationAsync(List<int> ids, CancellationToken cancellationToken)
        {
            var result = new HashSet<int>();
            foreach (var batch in ids.Chunk(500))
            {
                var found = await _db.SupplyReceiptDeviations.AsNoTracking().Where(d => batch.Contains(d.SupplyCommitmentId))
                    .Select(d => d.SupplyCommitmentId).Distinct().ToListAsync(cancellationToken);
                result.UnionWith(found);
            }
            return result;
        }

        // Actieve fysieke verkoopreserveringen per inhoudsregel (hoeveelheid en kg); som in het geheugen (SQLite kent geen decimal-SUM).
        private async Task<Dictionary<int, (decimal Quantity, decimal Kg)>> ReservedByContentAsync(List<int> contentIds, CancellationToken cancellationToken)
        {
            var reservations = await _db.StockReservations.AsNoTracking()
                .Where(r => r.PalletContentLineId != null && contentIds.Contains(r.PalletContentLineId.Value)
                    && r.SalesOrderLineId != null && r.Status == ReservationRules.StatusActive && r.Source == ReservationRules.SourcePhysical)
                .Select(r => new { ContentId = r.PalletContentLineId!.Value, r.Quantity, r.NetWeightKg })
                .ToListAsync(cancellationToken);
            return reservations.GroupBy(r => r.ContentId).ToDictionary(g => g.Key, g => (g.Sum(r => r.Quantity), g.Sum(r => r.NetWeightKg ?? 0m)));
        }

        private static string SnapshotStatus(string snapshot)
        {
            if (snapshot == null) return null;
            try
            {
                using var document = JsonDocument.Parse(snapshot);
                return document.RootElement.TryGetProperty("qualityStatus", out var status) ? status.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

        private static ServiceResult<T> Validation<T>(Dictionary<string, List<string>> errors) =>
            ServiceResult<T>.Validation(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));

        private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
        {
            if (!errors.TryGetValue(key, out var messages))
            {
                messages = new List<string>();
                errors[key] = messages;
            }
            messages.Add(message);
        }
    }
}
