using System.Globalization;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Inkooporder aanpassen (stap 4a; Definitief maken en corrigeren: PurchaseOrderService.Finalize.cs). Aanpassen gebruikt dezelfde regels als aanmaken
    // (SupplyCommitmentService.BuildCandidate) en dezelfde grenzen als guardregel 29: een regel waaraan iets hangt blijft staan en houdt
    // artikel en eenheid, nooit onder ontvangen; leverancier en partij liggen vast zodra er iets ontvangen is. Elke wijziging schrijft één
    // auditregel met oud en nieuw per veld; de actor komt uit CurrentUser. Alleen orders voor het eigen magazijn (klantgebonden aanvoer
    // niet in deze stap).
    public sealed partial class PurchaseOrderService
    {
        public const string StaleCode = "stale";
        public const string DefinitiveCode = "definitive";
        public const string OpenCode = "open";
        public const string NotEditableCode = "notEditable";
        public const string LockedCode = "locked";

        private sealed record EditFacts(HashSet<int> InUse, Dictionary<int, decimal> Received, bool AnyReceived);

        public async Task<ServiceResult<PurchaseOrderDetailDto>> UpdateAsync(int id, UpdatePurchaseOrderDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (!HasCommand(request?.RequestId, request?.ExpectedVersion, errors))
            {
                return Validation<PurchaseOrderDetailDto>(errors);
            }
            // Controle en opslag in één transactie met een rijlock op de order en haar regels (FOR UPDATE): een gelijktijdige ontvangst
            // (voegt een inhoudsregel toe, FK-lock op de orderregel) wacht tot deze wijziging klaar is, en een gelijktijdige herhaling met
            // dezelfde requestId ziet de eerdere wijziging bij de replaycontrole. Alleen PostgreSQL (productie) kent deze lock; SQLite
            // (tests) serialiseert schrijvers zelf.
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            if (_db.Database.IsNpgsql())
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SupplyCommitments\" WHERE \"Id\" = {id} FOR UPDATE", cancellationToken);
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SupplyCommitmentLines\" WHERE \"SupplyCommitmentId\" = {id} FOR UPDATE", cancellationToken);
            }
            var result = await UpdateLockedAsync(id, request, errors, cancellationToken);
            if (result.Status == ServiceResultStatus.Ok && _db.Database.CurrentTransaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return result;
        }

        private async Task<ServiceResult<PurchaseOrderDetailDto>> UpdateLockedAsync(int id, UpdatePurchaseOrderDto request, Dictionary<string, List<string>> errors, CancellationToken cancellationToken)
        {
            var replay = await ReplayAsync(request.RequestId!.Value, id, PurchaseOrderDocumentRules.KindEdit, cancellationToken);
            if (replay != null) return replay;

            var order = await _db.SupplyCommitments.Include(c => c.Lines).ThenInclude(l => l.ProductVariant).ThenInclude(v => v.Product).Include(c => c.Partij)
                .AsSplitQuery().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (order == null) return ServiceResult<PurchaseOrderDetailDto>.NotFound($"Purchase order {id} does not exist.");
            var refused = Refusal(order, request.ExpectedVersion!.Value, PurchaseOrderDocumentRules.KindEdit);
            if (refused != null) return refused;

            var candidate = SupplyCommitmentService.BuildCandidate(new CreateSupplyCommitmentDto
            {
                RequestId = request.RequestId, VendorId = request.VendorId, Route = SupplyRules.RouteWarehouse, Origin = request.Origin, ExpectedAt = request.ExpectedAt,
                SupplierReference = request.SupplierReference, OrderedOn = request.OrderedOn ?? order.OrderedOn, OrderChannel = request.OrderChannel, Note = request.Note,
                PartijId = request.PartijId, NewPartij = request.NewPartij, SupplierLotReference = request.SupplierLotReference,
                Lines = request.Lines?.Select(l => l == null ? null : new CreateSupplyCommitmentLineDto
                {
                    ProductVariantId = l.ProductVariantId, ExpectedQuantity = l.ExpectedQuantity, ExpectedUnit = l.ExpectedUnit, ExpectedNetWeightKg = l.ExpectedNetWeightKg,
                    ExpectedGrossWeightKg = l.ExpectedGrossWeightKg, WeightSource = l.WeightSource, LotNumber = l.LotNumber, PalletCount = l.PalletCount,
                    PackagesPerPallet = l.PackagesPerPallet, LoosePackages = l.LoosePackages, UnitPrice = l.UnitPrice,
                }).ToList(),
            }, _db.BusinessCalendar.Today(), errors);
            if (errors.Count > 0) return Validation<PurchaseOrderDetailDto>(errors);

            var facts = await EditFactsAsync(order.Lines.Select(l => l.Id).ToList(), cancellationToken);
            var stored = order.Lines.ToDictionary(l => l.Id);
            var next = candidate.Lines.ToList();
            var kept = new HashSet<int>();
            for (var i = 0; i < next.Count; i++)
            {
                var key = $"lines[{i}]";
                var lineId = request.Lines[i].Id;
                if (lineId == null)
                {
                    PackagingRules.WholePalletErrors(next[i].PalletCount, next[i].PackagesPerPallet, key, errors, AddError);
                    continue;
                }
                if (!stored.TryGetValue(lineId.Value, out var before) || !kept.Add(lineId.Value))
                {
                    AddError(errors, key, $"Line {lineId} does not belong to purchase order {id}, or occurs twice.");
                    continue;
                }
                if (next[i].PalletCount != before.PalletCount || next[i].PackagesPerPallet != before.PackagesPerPallet)
                {
                    PackagingRules.WholePalletErrors(next[i].PalletCount, next[i].PackagesPerPallet, key, errors, AddError);
                }
                if (!facts.InUse.Contains(before.Id)) continue;
                if (next[i].ProductVariantId != before.ProductVariantId || !string.Equals(next[i].ExpectedUnit, before.ExpectedUnit, StringComparison.Ordinal))
                {
                    AddError(errors, $"{key}.productVariantId", "Goods have been received on this line (or it has history), so its article and unit are fixed.");
                }
                var received = facts.Received.GetValueOrDefault(before.Id);
                if (next[i].ExpectedQuantity < received)
                {
                    AddError(errors, $"{key}.expectedQuantity", $"{received.ToString(CultureInfo.InvariantCulture)} has already been received on this line; the quantity cannot be lower.");
                }
            }
            foreach (var removed in order.Lines.Where(l => !kept.Contains(l.Id) && facts.InUse.Contains(l.Id)))
            {
                AddError(errors, "lines", $"Line {removed.Id} ({Describe(removed)}) has receipts or history and cannot be removed.");
            }
            if (candidate.VendorId != order.VendorId && facts.AnyReceived)
            {
                AddError(errors, "vendorId", "Goods have been received on this order, so the supplier is fixed.");
            }
            var partijChange = PartijChange(order, request, facts.AnyReceived, errors);
            if (errors.Count > 0) return Validation<PurchaseOrderDetailDto>(errors);

            var notFound = await MissingForEditAsync(order, candidate, request, cancellationToken);
            if (notFound != null) return ServiceResult<PurchaseOrderDetailDto>.NotFound(notFound);
            await CalculatedKgErrorsAsync(next, request, stored, errors, cancellationToken);
            if (errors.Count > 0) return Validation<PurchaseOrderDetailDto>(errors);

            Partij newPartij = null;
            if (partijChange == PartijChangeKind.New)
            {
                var built = await new PartijService(_db, _user).BuildNewAsync<PurchaseOrderDetailDto>(request.NewPartij.Number, "newPartij.number", PartijRules.KindPurchase, cancellationToken);
                if (built.Refused != null) return built.Refused;
                newPartij = built.Partij;
            }

            var fields = new List<PurchaseOrderChangeField>();
            await ApplyHeaderAsync(order, candidate, partijChange, request.PartijId, newPartij, fields, cancellationToken);
            await ApplyLinesAsync(order, request, next, stored, kept, fields, cancellationToken);
            if (fields.Count == 0)
            {
                return await GetAsync(id, cancellationToken);
            }
            return await SaveChangeAsync(order, request.RequestId!.Value, PurchaseOrderDocumentRules.KindEdit, null, fields, cancellationToken);
        }

        // Detail: documentstatus, wat vastligt (leverancier en partij na ontvangst; regels waaraan iets hangt) en de auditregels, ook in
        // de geschiedenis. Een order die bij de migratie definitief werd (factuurnummer) heeft geen Finalize-regel: dan uit de kop.
        private async Task AddDocumentAsync(PurchaseOrderDetailDto detail, SupplyCommitment order, CancellationToken cancellationToken)
        {
            var facts = await EditFactsAsync(order.Lines.Select(l => l.Id).ToList(), cancellationToken);
            var changes = await _db.PurchaseOrderChanges.AsNoTracking().Where(c => c.SupplyCommitmentId == order.Id).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToListAsync(cancellationToken);
            detail.DocumentStatus = order.DocumentStatus;
            detail.FinalizedAt = order.FinalizedAt == null ? null : DateTime.SpecifyKind(order.FinalizedAt.Value, DateTimeKind.Utc);
            detail.FinalizedBy = order.FinalizedBy;
            detail.FinalizedVia = order.FinalizedVia;
            detail.EditVersion = order.EditVersion;
            detail.Editable = !PurchaseOrderDocumentRules.IsDefinitive(order) && order.Route == SupplyRules.RouteWarehouse && order.Status != SupplyRules.StatusCancelled;
            detail.VendorFixed = facts.AnyReceived;
            detail.PartijFixed = facts.AnyReceived;
            detail.Changes = changes.Select(c => new PurchaseOrderChangeDto
            {
                Id = c.Id, Kind = c.Kind, Reason = c.Reason, ActorReference = c.ActorReference, CreatedAt = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
                Fields = PurchaseOrderDocumentRules.Deserialize(c.ChangesJson),
            }).ToList();
            foreach (var line in detail.Lines)
            {
                var stored = order.Lines.Single(l => l.Id == line.Id);
                (line.PalletInputCount, line.PalletInputPackagesPerPallet, line.PalletInputLoose) = (stored.PalletCount, stored.PackagesPerPallet, stored.LoosePackages);
                line.InUse = facts.InUse.Contains(line.Id);
            }
            detail.History.AddRange(detail.Changes.Select(c => new PurchaseOrderHistoryEventDto
            {
                OccurredAt = c.CreatedAt, ActorReference = c.ActorReference, Reason = c.Reason,
                Type = c.Kind == PurchaseOrderDocumentRules.KindEdit ? "Edited" : c.Kind == PurchaseOrderDocumentRules.KindFinalize ? "Finalized" : "Corrected",
                Reference = c.Kind == PurchaseOrderDocumentRules.KindFinalize ? order.FinalizedVia : null, Quantity = c.Fields.Count,
            }));
            if (detail.FinalizedAt != null && changes.All(c => c.Kind != PurchaseOrderDocumentRules.KindFinalize))
            {
                detail.History.Add(new PurchaseOrderHistoryEventDto { OccurredAt = detail.FinalizedAt.Value, Type = "Finalized", ActorReference = order.FinalizedBy, Reference = order.FinalizedVia });
            }
            detail.History = detail.History.OrderBy(h => h.OccurredAt).ToList();
        }

        // ---------------- Hulpstukken ----------------

        private static bool HasCommand(Guid? requestId, int? expectedVersion, Dictionary<string, List<string>> errors)
        {
            if (requestId == null || requestId == Guid.Empty) AddError(errors, "requestId", "A client-generated, non-empty requestId is required.");
            if (expectedVersion == null) AddError(errors, "expectedVersion", "The version of the order as shown (editVersion) is required.");
            return errors.Count == 0;
        }

        private string Actor() => _user.Actor(PurchaseOrderDocumentRules.MaxActorLength);

        private static ServiceResult<PurchaseOrderDetailDto> Conflict(string code, string message) =>
            ServiceResult<PurchaseOrderDetailDto>.Conflict(message, new { message, code });

        // Dezelfde requestId: de eerdere wijziging is al opgeslagen, dus de huidige order terug; voor een andere order of soort een fout.
        private async Task<ServiceResult<PurchaseOrderDetailDto>> ReplayAsync(Guid requestId, int id, string kind, CancellationToken cancellationToken)
        {
            var earlier = await _db.PurchaseOrderChanges.AsNoTracking().Where(c => c.RequestId == requestId).Select(c => new { c.SupplyCommitmentId, c.Kind }).SingleOrDefaultAsync(cancellationToken);
            if (earlier == null) return null;
            return earlier.SupplyCommitmentId == id && earlier.Kind == kind
                ? await GetAsync(id, cancellationToken)
                : ServiceResult<PurchaseOrderDetailDto>.Conflict($"RequestId {requestId} was already used for another change.");
        }

        private static ServiceResult<PurchaseOrderDetailDto> Refusal(SupplyCommitment order, int expectedVersion, string kind)
        {
            if (kind == PurchaseOrderDocumentRules.KindEdit)
            {
                if (PurchaseOrderDocumentRules.IsDefinitive(order))
                {
                    return Conflict(DefinitiveCode, $"Purchase order {order.OrderNumber} is definitive since {order.FinalizedAt:yyyy-MM-dd} ({order.FinalizedBy}); it can only be corrected, with a reason.");
                }
                if (order.Route != SupplyRules.RouteWarehouse || order.Status == SupplyRules.StatusCancelled)
                {
                    return Conflict(NotEditableCode, $"Purchase order {order.OrderNumber} cannot be edited here (customer-bound supply or cancelled).");
                }
            }
            else if (!PurchaseOrderDocumentRules.IsDefinitive(order))
            {
                return Conflict(OpenCode, $"Purchase order {order.OrderNumber} is still open: edit it instead of correcting it.");
            }
            return order.EditVersion == expectedVersion ? null
                : Conflict(StaleCode, $"Purchase order {order.OrderNumber} has been changed in the meantime; reload it first.");
        }

        // Wat aan de regels hangt: ontvangst (elke inhoud), afwijking, vervanging, reservering of afkeuring (dezelfde lijst als regel 29).
        private async Task<EditFacts> EditFactsAsync(List<int> lineIds, CancellationToken cancellationToken)
        {
            var inUse = (await _db.SupplyCommitmentLines.AsNoTracking().Where(l => lineIds.Contains(l.Id) && (l.ReplacesLineId != null
                    || _db.PalletContentLines.Any(c => c.SupplyCommitmentLineId == l.Id) || _db.SupplyReceiptDeviations.Any(d => d.OriginalLineId == l.Id || d.NewLineId == l.Id)
                    || _db.SupplyCommitmentLines.Any(r => r.ReplacesLineId == l.Id) || _db.StockReservations.Any(r => r.SupplyCommitmentLineId == l.Id)
                    || _db.SupplyRejectionLines.Any(r => r.SupplyCommitmentLineId == l.Id)))
                .Select(l => l.Id).ToListAsync(cancellationToken)).ToHashSet();
            var units = await _db.SupplyCommitmentLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.ExpectedUnit, cancellationToken);
            var receipts = await _db.PalletContentLines.AsNoTracking().Where(c => lineIds.Contains(c.SupplyCommitmentLineId) && c.OriginCorrectionLineId == null
                    && c.OriginRejectionLineId == null && c.Pallet.LogisticsStatus != PalletRules.StatusCancelled)
                .Select(c => new { c.SupplyCommitmentLineId, c.Quantity, c.Unit }).ToListAsync(cancellationToken);
            var received = receipts.Where(r => units[r.SupplyCommitmentLineId] == null || string.Equals(units[r.SupplyCommitmentLineId], r.Unit, StringComparison.Ordinal))
                .GroupBy(r => r.SupplyCommitmentLineId).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
            var anyReceived = await _db.PalletContentLines.AsNoTracking().AnyAsync(c => lineIds.Contains(c.SupplyCommitmentLineId), cancellationToken);
            return new EditFacts(inUse, received, anyReceived);
        }

        private enum PartijChangeKind { None, Existing, New }

        // Partij: blijft, wordt gezet of (zolang er niets ontvangen is) vervangen; een partij gaat nooit van de order af (regel 23).
        private static PartijChangeKind PartijChange(SupplyCommitment order, UpdatePurchaseOrderDto request, bool anyReceived, Dictionary<string, List<string>> errors)
        {
            if (request.PartijId == null && request.NewPartij == null)
            {
                if (order.PartijId != null) AddError(errors, "partijId", "A lot is never removed from an order; choose another lot instead.");
                return PartijChangeKind.None;
            }
            if (request.PartijId != null && request.PartijId == order.PartijId) return PartijChangeKind.None;
            if (anyReceived)
            {
                AddError(errors, "partijId", "Goods have been received on this order, so its lot is fixed here; link a lot with 'Choose lot' on the order.");
                return PartijChangeKind.None;
            }
            return request.PartijId != null ? PartijChangeKind.Existing : PartijChangeKind.New;
        }

        // Leverancier en artikelen moeten bestaan; een gearchiveerd artikel alleen op een regel die het al had (regel 19).
        private async Task<string> MissingForEditAsync(SupplyCommitment order, SupplyCommitment candidate, UpdatePurchaseOrderDto request, CancellationToken cancellationToken)
        {
            if (candidate.VendorId != order.VendorId)
            {
                var active = await _db.Vendors.AsNoTracking().Where(v => v.Id == candidate.VendorId).Select(v => (bool?)v.Relation.IsActive).SingleOrDefaultAsync(cancellationToken);
                if (active == null) return $"Vendor {candidate.VendorId} does not exist.";
                if (active == false) return $"Vendor {candidate.VendorId} belongs to an inactive relation; reactivate the relation first or choose another vendor.";
            }
            if (request.PartijId != null && request.PartijId != order.PartijId)
            {
                var kind = await _db.Partijen.AsNoTracking().Where(p => p.Id == request.PartijId).Select(p => p.Kind).SingleOrDefaultAsync(cancellationToken);
                if (kind == null) return $"Lot {request.PartijId} does not exist.";
                if (kind != PartijRules.KindPurchase) return $"Lot {request.PartijId} is a sales lot; a purchase order needs a purchase lot.";
            }
            var stored = order.Lines.ToDictionary(l => l.Id, l => l.ProductVariantId);
            var lines = candidate.Lines.ToList();
            var newVariantIds = lines.Where((l, i) => request.Lines[i].Id is not { } lineId || stored.GetValueOrDefault(lineId) != l.ProductVariantId).Select(l => l.ProductVariantId).Distinct().ToList();
            var variants = await _db.ProductVariants.AsNoTracking().Where(v => newVariantIds.Contains(v.Id)).Select(v => new { v.Id, v.IsArchived }).ToListAsync(cancellationToken);
            var missing = newVariantIds.Except(variants.Select(v => v.Id)).Cast<int?>().FirstOrDefault();
            if (missing != null) return $"Product variant {missing} does not exist.";
            var archived = variants.FirstOrDefault(v => v.IsArchived);
            return archived == null ? null : ArticleRules.ArchivedMessage(archived.Id);
        }

        // Berekende kg (aantal × de huidige standaard) alleen voor regels die nieuw zijn of waarvan artikel, aantal of kg wijzigt: een
        // ongewijzigde regel van vóór een gewijzigde artikelstandaard blijft geldig zoals ze is.
        private async Task CalculatedKgErrorsAsync(List<SupplyCommitmentLine> next, UpdatePurchaseOrderDto request, Dictionary<int, SupplyCommitmentLine> stored,
            Dictionary<string, List<string>> errors, CancellationToken cancellationToken)
        {
            var check = next.Select((line, i) => (line, i)).Where(x => request.Lines[x.i].Id is not { } id || !stored.TryGetValue(id, out var before)
                || before.ProductVariantId != x.line.ProductVariantId || before.ExpectedQuantity != x.line.ExpectedQuantity || before.ExpectedNetWeightKg != x.line.ExpectedNetWeightKg
                || before.ExpectedGrossWeightKg != x.line.ExpectedGrossWeightKg || !string.Equals(before.WeightSource, x.line.WeightSource, StringComparison.Ordinal)).ToList();
            if (check.Count == 0) return;
            var ids = check.Select(x => x.line.ProductVariantId).Distinct().ToList();
            var defaults = await _db.ProductVariants.AsNoTracking().Where(v => ids.Contains(v.Id))
                .Select(v => new { v.Id, v.DefaultNetKgPerPackage, v.DefaultGrossKgPerPackage }).ToDictionaryAsync(v => v.Id, cancellationToken);
            foreach (var (line, i) in check)
            {
                var variant = defaults[line.ProductVariantId];
                LineWeightRules.CalculatedErrors(line.ExpectedQuantity, line.ExpectedNetWeightKg, line.ExpectedGrossWeightKg, line.WeightSource,
                    variant.DefaultNetKgPerPackage, variant.DefaultGrossKgPerPackage, $"lines[{i}]", "expectedNetWeightKg", "expectedGrossWeightKg", errors, AddError);
            }
        }

        private async Task ApplyHeaderAsync(SupplyCommitment order, SupplyCommitment candidate, PartijChangeKind partijChange, int? partijId, Partij newPartij,
            List<PurchaseOrderChangeField> fields, CancellationToken cancellationToken)
        {
            void Text(string field, string before, string after, Action apply)
            {
                if (string.Equals(before, after, StringComparison.Ordinal)) return;
                fields.Add(new(field, null, PurchaseOrderDocumentRules.ActionChanged, before, after));
                apply();
            }
            if (candidate.VendorId != order.VendorId)
            {
                var names = await _db.Vendors.AsNoTracking().Where(v => v.Id == order.VendorId || v.Id == candidate.VendorId).ToDictionaryAsync(v => v.Id, v => v.Name, cancellationToken);
                fields.Add(new("vendor", null, PurchaseOrderDocumentRules.ActionChanged, names.GetValueOrDefault(order.VendorId), names.GetValueOrDefault(candidate.VendorId)));
                order.VendorId = candidate.VendorId;
            }
            Text(PurchaseOrderDocumentRules.FieldSupplierReference, order.SupplierReference, candidate.SupplierReference, () => order.SupplierReference = candidate.SupplierReference);
            Text("orderedOn", Date(order.OrderedOn), Date(candidate.OrderedOn), () => order.OrderedOn = candidate.OrderedOn);
            Text("expectedAt", ExpectedDate(order.ExpectedAt), ExpectedDate(candidate.ExpectedAt), () => order.ExpectedAt = candidate.ExpectedAt);
            Text("origin", Blank(order.Origin), Blank(candidate.Origin), () => order.Origin = candidate.Origin);
            Text(PurchaseOrderDocumentRules.FieldNote, order.Note, candidate.Note, () => order.Note = candidate.Note);
            Text("supplierLotReference", order.SupplierLotReference, candidate.SupplierLotReference, () => order.SupplierLotReference = candidate.SupplierLotReference);
            Text("orderChannel", order.OrderChannel, candidate.OrderChannel, () => order.OrderChannel = candidate.OrderChannel);
            if (partijChange == PartijChangeKind.None) return;
            var partij = newPartij ?? await _db.Partijen.SingleAsync(p => p.Id == partijId, cancellationToken);
            fields.Add(new("partij", null, PurchaseOrderDocumentRules.ActionChanged, order.Partij?.Number, partij.Number));
            // Kop en alle regels samen (regel 23); nieuwe regels erven haar in ApplyLinesAsync.
            order.Partij = partij;
            foreach (var line in order.Lines)
            {
                line.Partij = partij;
            }
        }

        private async Task ApplyLinesAsync(SupplyCommitment order, UpdatePurchaseOrderDto request, List<SupplyCommitmentLine> next, Dictionary<int, SupplyCommitmentLine> stored,
            HashSet<int> kept, List<PurchaseOrderChangeField> fields, CancellationToken cancellationToken)
        {
            var variantIds = next.Select(l => l.ProductVariantId).Concat(stored.Values.Select(l => l.ProductVariantId)).Distinct().ToList();
            var names = (await _db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).Select(v => new { v.Id, Product = v.Product.Name, v.Name }).ToListAsync(cancellationToken))
                .ToDictionary(v => v.Id, v => $"{v.Product} {v.Name}".Trim());
            foreach (var removed in order.Lines.Where(l => !kept.Contains(l.Id)).ToList())
            {
                fields.Add(new("line", removed.Id, PurchaseOrderDocumentRules.ActionRemoved, Describe(removed, names), null));
                _db.SupplyCommitmentLines.Remove(removed);
            }
            var now = DateTime.UtcNow;
            for (var i = 0; i < next.Count; i++)
            {
                var wanted = next[i];
                if (request.Lines[i].Id is not { } id)
                {
                    wanted.CreatedAt = now;
                    if (order.Partij != null) wanted.Partij = order.Partij; else wanted.PartijId = order.PartijId;
                    order.Lines.Add(wanted);
                    fields.Add(new("line", null, PurchaseOrderDocumentRules.ActionAdded, null, Describe(wanted, names)));
                    continue;
                }
                var line = stored[id];
                void Field(string field, string before, string after, Action apply)
                {
                    if (string.Equals(before, after, StringComparison.Ordinal)) return;
                    fields.Add(new(field, id, PurchaseOrderDocumentRules.ActionChanged, before, after));
                    apply();
                }
                if (line.ProductVariantId != wanted.ProductVariantId)
                {
                    // Beslissen op het id (twee varianten kunnen dezelfde weergavenaam hebben); de namen alleen voor de audittekst.
                    fields.Add(new("article", id, PurchaseOrderDocumentRules.ActionChanged, names.GetValueOrDefault(line.ProductVariantId), names.GetValueOrDefault(wanted.ProductVariantId)));
                    line.ProductVariantId = wanted.ProductVariantId;
                }
                // Vergelijken op de decimale waarde zelf; de opmaak (Number) alleen voor de audittekst.
                void Amount(string field, decimal? before, decimal? after, Action apply)
                {
                    if (before == after) return;
                    fields.Add(new(field, id, PurchaseOrderDocumentRules.ActionChanged, Number(before), Number(after)));
                    apply();
                }
                Amount("expectedQuantity", line.ExpectedQuantity, wanted.ExpectedQuantity, () => line.ExpectedQuantity = wanted.ExpectedQuantity);
                Field("expectedUnit", line.ExpectedUnit, wanted.ExpectedUnit, () => line.ExpectedUnit = wanted.ExpectedUnit);
                Amount(PurchaseOrderDocumentRules.FieldUnitPrice, line.UnitPrice, wanted.UnitPrice, () => line.UnitPrice = wanted.UnitPrice);
                Amount("expectedNetWeightKg", line.ExpectedNetWeightKg, wanted.ExpectedNetWeightKg, () => line.ExpectedNetWeightKg = wanted.ExpectedNetWeightKg);
                Amount("expectedGrossWeightKg", line.ExpectedGrossWeightKg, wanted.ExpectedGrossWeightKg, () => line.ExpectedGrossWeightKg = wanted.ExpectedGrossWeightKg);
                Field("weightSource", line.WeightSource, wanted.WeightSource, () => line.WeightSource = wanted.WeightSource);
                if ((line.PalletCount, line.PackagesPerPallet, line.LoosePackages) != (wanted.PalletCount, wanted.PackagesPerPallet, wanted.LoosePackages))
                {
                    fields.Add(new("pallets", id, PurchaseOrderDocumentRules.ActionChanged, Pallets(line), Pallets(wanted)));
                    (line.PalletCount, line.PackagesPerPallet, line.LoosePackages) = (wanted.PalletCount, wanted.PackagesPerPallet, wanted.LoosePackages);
                }
                Field("lotNumber", line.LotNumber, wanted.LotNumber, () => line.LotNumber = wanted.LotNumber);
            }
        }

        private async Task<ServiceResult<PurchaseOrderDetailDto>> SaveChangeAsync(SupplyCommitment order, Guid requestId, string kind, string reason,
            List<PurchaseOrderChangeField> fields, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            order.EditVersion++;
            order.UpdatedAt = now;
            _db.PurchaseOrderChanges.Add(new PurchaseOrderChange
            {
                RequestId = requestId, SupplyCommitmentId = order.Id, Kind = kind, Reason = reason, ActorReference = Actor(), CreatedAt = now,
                ChangesJson = PurchaseOrderDocumentRules.Serialize(fields),
            });
            return await SaveAsync(order, requestId, cancellationToken);
        }

        private async Task<ServiceResult<PurchaseOrderDetailDto>> SaveAsync(SupplyCommitment order, Guid requestId, CancellationToken cancellationToken)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Ook hier kan een gelijktijdige poging met dezelfde requestId de wijziging al hebben opgeslagen.
                return await ReplayOrStaleAsync(order, requestId, $"Purchase order {order.OrderNumber} has been changed in the meantime; reload it first.", cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Een gelijktijdige herhaling (unieke requestId) of intussen een ontvangst op een te verwijderen regel.
                return await ReplayOrStaleAsync(order, requestId, $"Purchase order {order.OrderNumber} changed while saving (for example a receipt); reload it first.", cancellationToken);
            }
            _db.ChangeTracker.Clear();
            return await GetAsync(order.Id, cancellationToken);
        }

        private async Task<ServiceResult<PurchaseOrderDetailDto>> ReplayOrStaleAsync(SupplyCommitment order, Guid requestId, string staleMessage, CancellationToken cancellationToken)
        {
            // Een mislukte opslag breekt de PostgreSQL-transactie af: eerst terugdraaien, dan opnieuw lezen.
            if (_db.Database.CurrentTransaction != null) await _db.Database.CurrentTransaction.RollbackAsync(cancellationToken);
            var orderId = order.Id;
            _db.ChangeTracker.Clear();
            if (await _db.PurchaseOrderChanges.AsNoTracking().AnyAsync(c => c.RequestId == requestId && c.SupplyCommitmentId == orderId, cancellationToken))
            {
                return await GetAsync(orderId, cancellationToken);
            }
            return Conflict(StaleCode, staleMessage);
        }

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private static string ExpectedDate(DateTime? value) => value == null ? null : AmsterdamBusinessCalendar.ToAmsterdamDate(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private static string Number(decimal? value) => (value / 1.0000000000000000000000000000m)?.ToString(CultureInfo.InvariantCulture);
        private static string Pallets(SupplyCommitmentLine line) =>
            line.PalletCount == null ? null : $"{Number(line.PalletCount)} × {Number(line.PackagesPerPallet)}{(line.LoosePackages is > 0m ? $" + {Number(line.LoosePackages)}" : "")}";

        private static string Describe(SupplyCommitmentLine line, IReadOnlyDictionary<int, string> names = null)
        {
            var name = names?.GetValueOrDefault(line.ProductVariantId) ?? $"{line.ProductVariant?.Product?.Name} {line.ProductVariant?.Name}".Trim();
            var price = line.UnitPrice == null ? "" : $" × {Number(line.UnitPrice)}";
            return $"{name}: {Number(line.ExpectedQuantity)} {line.ExpectedUnit}{price}".Trim();
        }
    }
}
