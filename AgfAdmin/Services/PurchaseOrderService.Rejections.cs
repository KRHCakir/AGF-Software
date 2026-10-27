using System.Text.Json;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public sealed partial class PurchaseOrderService
    {
        public const string RejectionSplitEvent = "ContentLineSplitForRejection";

        // Zelfde vorm als de auditrecords van PalletService (camelCase JSON).
        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private static string Snapshot(object value) => JsonSerializer.Serialize(value, SnapshotJson);

        // ---------------- Afkeuren ----------------
        //
        // Per regel: hoeveelheid (en kg) uit de voorraad van een ontvangen inhoudsregel, nooit meer dan voorraad minus actieve
        // verkoopreserveringen. De hele voorraad van een regel zonder reserveringen: de regel zelf wordt Rejected (zelfde auditrecord
        // als een kwaliteitswijziging). Een deel: de regel houdt haar historische ontvangst en krijgt "afgesplitst" erbij; het deel
        // wordt een eigen inhoudsregel (Rejected, herkomst = afkeuringsregel) op dezelfde pallet, zoals correctie-inhoud bij een
        // verzendcorrectie. Zo blijft kwaliteit per inhoudsregel, telt de ontvangst nooit dubbel en blijft de palletvoorraad gelijk.
        // Alles wat ontvangen is afgekeurd: de order wordt Rejected en telt niet meer als verwacht.
        public async Task<ServiceResult<PurchaseOrderDetailDto>> RejectAsync(int id, RejectPurchaseOrderDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var command = ValidateRejection(request, errors);
            if (errors.Count > 0)
            {
                return Validation<PurchaseOrderDetailDto>(errors);
            }

            var replay = await RejectionReplayAsync(id, command, cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var commitment = await _db.SupplyCommitments.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (commitment == null)
            {
                return ServiceResult<PurchaseOrderDetailDto>.NotFound($"Purchase order {id} does not exist.");
            }
            if (commitment.Status == SupplyRules.StatusCancelled)
            {
                return ServiceResult<PurchaseOrderDetailDto>.Conflict($"Purchase order {commitment.OrderNumber} is cancelled.");
            }

            var contentIds = command.Lines.Select(l => l.PalletContentLineId).ToList();
            var contents = await _db.PalletContentLines
                .Include(c => c.Pallet)
                .Include(c => c.SupplyCommitmentLine)
                .Where(c => contentIds.Contains(c.Id))
                .ToListAsync(cancellationToken);
            var reserved = await ReservedByContentAsync(contentIds, cancellationToken);

            var now = DateTime.UtcNow;
            var rejection = new SupplyRejection
            {
                RequestId = command.RequestId,
                SupplyCommitmentId = id,
                Reason = command.Reason,
                Explanation = command.Explanation,
                Decision = command.Decision,
                ActorReference = command.Actor,
                RejectedAt = now
            };
            var qualityReason = $"{commitment.OrderNumber}: {command.Explanation ?? command.Reason}";
            for (var i = 0; i < command.Lines.Count; i++)
            {
                var key = $"lines[{i}]";
                var requested = command.Lines[i];
                var content = contents.SingleOrDefault(c => c.Id == requested.PalletContentLineId);
                if (content == null || content.SupplyCommitmentLine.SupplyCommitmentId != id)
                {
                    return ServiceResult<PurchaseOrderDetailDto>.NotFound($"Pallet content line {requested.PalletContentLineId} is not received on purchase order {commitment.OrderNumber}.");
                }
                if (content.Version != requested.ExpectedVersion)
                {
                    return ServiceResult<PurchaseOrderDetailDto>.Conflict($"Content line {content.Id} is at version {content.Version}, not {requested.ExpectedVersion}; reload it and decide again.");
                }
                if (content.Pallet.LogisticsStatus != PalletRules.StatusReceived)
                {
                    return ServiceResult<PurchaseOrderDetailDto>.Conflict($"Pallet {content.Pallet.Reference} is {content.Pallet.LogisticsStatus}; only content of a received pallet can be rejected.");
                }
                if (content.QualityStatus == PalletRules.QualityRejected)
                {
                    return ServiceResult<PurchaseOrderDetailDto>.Conflict($"Content line {content.Id} is already rejected.");
                }

                var onHand = ReservationRules.OnHandQuantity(content.Quantity, content.DispatchedQuantity, content.SplitOffQuantity);
                var onHandKg = ReservationRules.OnHandQuantity(content.NetWeightKg, content.DispatchedNetWeightKg, content.SplitOffNetWeightKg);
                var (reservedQuantity, reservedKg) = reserved.GetValueOrDefault(content.Id);
                var free = onHand - reservedQuantity;
                if (requested.Quantity > free)
                {
                    AddError(errors, $"{key}.quantity", reservedQuantity > 0m
                        ? $"At most {free} {content.Unit} can be rejected: {onHand} on hand, of which {reservedQuantity} reserved for sales orders."
                        : $"At most {free} {content.Unit} can be rejected (on hand); dispatched goods cannot be rejected here.");
                    continue;
                }

                var whole = requested.Quantity == onHand && reservedQuantity == 0m;
                decimal kg;
                if (whole)
                {
                    kg = onHandKg;
                    if (requested.NetWeightKg != null && requested.NetWeightKg != onHandKg)
                    {
                        AddError(errors, $"{key}.netWeightKg", $"Rejecting all {onHand} {content.Unit} rejects exactly the remaining {onHandKg} kg.");
                        continue;
                    }
                }
                else if (content.WeightMethod == WeightRules.Calculated && content.NetKgPerPackage != null)
                {
                    kg = WeightRules.CalculatedTotal(requested.Quantity, content.NetKgPerPackage.Value);
                    if (requested.NetWeightKg != null && requested.NetWeightKg != kg)
                    {
                        AddError(errors, $"{key}.netWeightKg", $"For a calculated weight the rejected kg is quantity × kg per package = {kg} kg, or omitted.");
                        continue;
                    }
                }
                else
                {
                    kg = requested.NetWeightKg ?? PurchaseOrderRules.DefaultRejectedKg(requested.Quantity, onHand, onHandKg, content.WeightMethod, content.NetKgPerPackage);
                }
                // Een deel laat altijd voorraad met kg achter, en de achterblijvende reserveringen houden hun kg.
                if (!whole && (kg <= 0m || kg >= onHandKg || kg > onHandKg - reservedKg))
                {
                    AddError(errors, $"{key}.netWeightKg", $"The rejected kg must be positive and leave kg for the remaining {onHand - requested.Quantity} {content.Unit} ({onHandKg} kg on hand, {reservedKg} kg reserved).");
                    continue;
                }

                var line = new SupplyRejectionLine
                {
                    PalletContentLine = content,
                    SupplyCommitmentLineId = content.SupplyCommitmentLineId,
                    Quantity = requested.Quantity,
                    Unit = content.Unit,
                    NetWeightKg = kg,
                    SplitOff = !whole
                };
                rejection.Lines.Add(line);

                if (whole)
                {
                    var oldValue = Snapshot(new { qualityStatus = content.QualityStatus, qualityReason = content.QualityReason, version = content.Version });
                    content.QualityStatus = PalletRules.QualityRejected;
                    content.QualityReason = qualityReason;
                    _db.PalletAuditEvents.Add(new PalletAuditEvent
                    {
                        PalletId = content.PalletId,
                        PalletContentLineId = content.Id,
                        EventType = PalletService.QualityStatusChangedEvent,
                        OldValue = oldValue,
                        NewValue = Snapshot(new { qualityStatus = PalletRules.QualityRejected, qualityReason }),
                        Reason = qualityReason,
                        ActorReference = command.Actor,
                        OccurredAt = now
                    });
                }
                else
                {
                    var oldValue = Snapshot(new { splitOffQuantity = content.SplitOffQuantity, splitOffNetWeightKg = content.SplitOffNetWeightKg, version = content.Version });
                    content.SplitOffQuantity += requested.Quantity;
                    content.SplitOffNetWeightKg += kg;
                    var split = new PalletContentLine
                    {
                        RequestId = Guid.NewGuid(),
                        PalletId = content.PalletId,
                        SupplyCommitmentLineId = content.SupplyCommitmentLineId,
                        LotNumber = content.LotNumber,
                        Quantity = requested.Quantity,
                        Unit = content.Unit,
                        NetWeightKg = kg,
                        WeightMethod = content.WeightMethod,
                        NetKgPerPackage = content.NetKgPerPackage,
                        Ggn = content.Ggn,
                        QualityStatus = PalletRules.QualityRejected,
                        QualityReason = qualityReason,
                        OriginRejectionLine = line,
                        CreatedAt = now
                    };
                    _db.PalletContentLines.Add(split);
                    _db.PalletAuditEvents.Add(new PalletAuditEvent
                    {
                        PalletId = content.PalletId,
                        PalletContentLineId = content.Id,
                        EventType = RejectionSplitEvent,
                        OldValue = oldValue,
                        NewValue = Snapshot(new { splitOffQuantity = content.SplitOffQuantity, splitOffNetWeightKg = content.SplitOffNetWeightKg, rejectedQuantity = requested.Quantity, rejectedNetWeightKg = kg }),
                        Reason = qualityReason,
                        ActorReference = command.Actor,
                        OccurredAt = now
                    });
                    _db.PalletAuditEvents.Add(new PalletAuditEvent
                    {
                        PalletId = content.PalletId,
                        PalletContentLine = split,
                        EventType = PalletService.ContentLineAddedEvent,
                        NewValue = Snapshot(new { split.SupplyCommitmentLineId, split.LotNumber, split.Quantity, split.Unit, split.NetWeightKg, split.Ggn, split.QualityStatus, splitFromContentLineId = content.Id }),
                        Reason = qualityReason,
                        ActorReference = command.Actor,
                        OccurredAt = now
                    });
                }
                // Ook de pallet aanraken (versie +1), net als bij een kwaliteitswijziging: een gelijktijdige verzending wint of verliest als geheel.
                _db.Entry(content.Pallet).Property(p => p.Version).IsModified = true;
            }
            if (errors.Count > 0)
            {
                _db.ChangeTracker.Clear();
                return Validation<PurchaseOrderDetailDto>(errors);
            }
            _db.SupplyRejections.Add(rejection);

            // Alles wat ontvangen is afgekeurd: de order is Afgekeurd en telt niet meer als verwachte aanvoer.
            var facts = (await LoadFactsAsync(new List<int> { id }, cancellationToken)).For(id);
            var receivedQuantity = facts.Lines.Sum(l => l.ReceivedAllUnitsQuantity);
            if (PurchaseOrderRules.IsFullyRejected(receivedQuantity, facts.RejectedQuantity + rejection.Lines.Sum(l => l.Quantity))
                && commitment.Status is SupplyRules.StatusExpected or SupplyRules.StatusPartiallyReceived or SupplyRules.StatusReceived)
            {
                commitment.Status = SupplyRules.StatusRejected;
                commitment.UpdatedAt = now;
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                _db.ChangeTracker.Clear();
                var concurrent = await RejectionReplayAsync(id, command, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                if (exception is DbUpdateConcurrencyException)
                {
                    return ServiceResult<PurchaseOrderDetailDto>.Conflict("The received content was changed concurrently; reload the order and decide again.");
                }
                throw;
            }

            var detail = await GetAsync(id, cancellationToken);
            return ServiceResult<PurchaseOrderDetailDto>.Created(detail.Value);
        }

        private sealed record RejectionLineCommand(int PalletContentLineId, int ExpectedVersion, decimal Quantity, decimal? NetWeightKg);

        private sealed record RejectionCommand(Guid RequestId, string Reason, string Explanation, string Decision, string Actor, List<RejectionLineCommand> Lines);

        private static RejectionCommand ValidateRejection(RejectPurchaseOrderDto request, Dictionary<string, List<string>> errors)
        {
            if (request == null)
            {
                AddError(errors, "request", "A request body is required.");
                return null;
            }
            if (request.RequestId == null || request.RequestId == Guid.Empty)
            {
                AddError(errors, "requestId", "A client-generated, non-empty requestId is required.");
            }
            if (!PurchaseOrderRules.IsRejectionReason(request.Reason))
            {
                AddError(errors, "reason", $"Reason must be one of: {string.Join(", ", PurchaseOrderRules.RejectionReasons)}.");
            }
            if (!PurchaseOrderRules.IsRejectionDecision(request.Decision))
            {
                AddError(errors, "decision", $"Decision must be one of: {string.Join(", ", PurchaseOrderRules.RejectionDecisions)}.");
            }
            var explanation = string.IsNullOrWhiteSpace(request.Explanation) ? null : request.Explanation.Trim();
            if (explanation != null && explanation.Length > PurchaseOrderRules.MaxExplanationLength)
            {
                AddError(errors, "explanation", $"This field may contain at most {PurchaseOrderRules.MaxExplanationLength} characters.");
            }
            if (request.Reason == PurchaseOrderRules.ReasonOther && explanation == null)
            {
                AddError(errors, "explanation", "An explanation is required for reason Other.");
            }
            // Afkeuren is een kwaliteitsbesluit: net als bij de keuring is de medewerker verplicht.
            var actor = string.IsNullOrWhiteSpace(request.ActorReference) ? null : request.ActorReference.Trim();
            if (actor == null)
            {
                AddError(errors, "actorReference", "This field is required and must not be blank.");
            }
            else if (actor.Length > PurchaseOrderRules.MaxActorLength)
            {
                AddError(errors, "actorReference", $"This field may contain at most {PurchaseOrderRules.MaxActorLength} characters.");
            }

            var lines = new List<RejectionLineCommand>();
            if (request.Lines == null || request.Lines.Count == 0)
            {
                AddError(errors, "lines", "At least one line is required.");
            }
            else if (request.Lines.Count > PurchaseOrderRules.MaxRejectionLines)
            {
                AddError(errors, "lines", $"At most {PurchaseOrderRules.MaxRejectionLines} lines are allowed.");
            }
            else
            {
                var seen = new HashSet<int>();
                for (var i = 0; i < request.Lines.Count; i++)
                {
                    var line = request.Lines[i];
                    var key = $"lines[{i}]";
                    if (line == null || line.PalletContentLineId == null)
                    {
                        AddError(errors, $"{key}.palletContentLineId", "A received pallet content line is required.");
                        continue;
                    }
                    if (!seen.Add(line.PalletContentLineId.Value))
                    {
                        AddError(errors, key, "The same content line occurs more than once.");
                    }
                    if (line.ExpectedContentLineVersion == null)
                    {
                        AddError(errors, $"{key}.expectedContentLineVersion", "The content line version the rejection is based on is required.");
                    }
                    if (line.Quantity == null || !SupplyRules.IsPositiveDecimal18_3(line.Quantity.Value))
                    {
                        AddError(errors, $"{key}.quantity", "Quantity is required, must be positive and may have at most 3 decimals.");
                    }
                    if (line.NetWeightKg != null && !SupplyRules.IsPositiveDecimal18_3(line.NetWeightKg.Value))
                    {
                        AddError(errors, $"{key}.netWeightKg", "Net kg, when given, must be positive and may have at most 3 decimals.");
                    }
                    if (line.ExpectedContentLineVersion != null && line.Quantity != null)
                    {
                        lines.Add(new RejectionLineCommand(line.PalletContentLineId.Value, line.ExpectedContentLineVersion.Value, line.Quantity.Value, line.NetWeightKg));
                    }
                }
            }

            return errors.Count > 0 ? null : new RejectionCommand(request.RequestId!.Value, request.Reason, explanation, request.Decision, actor, lines);
        }

        // Dezelfde RequestId met dezelfde inhoud geeft de huidige order terug (200); andere inhoud is een conflict.
        private async Task<ServiceResult<PurchaseOrderDetailDto>> RejectionReplayAsync(int id, RejectionCommand command, CancellationToken cancellationToken)
        {
            var existing = await _db.SupplyRejections.AsNoTracking().Include(r => r.Lines).SingleOrDefaultAsync(r => r.RequestId == command.RequestId, cancellationToken);
            if (existing == null)
            {
                return null;
            }
            var stored = existing.Lines.OrderBy(l => l.Id).ToList();
            var same = existing.SupplyCommitmentId == id
                && existing.Reason == command.Reason
                && string.Equals(existing.Explanation, command.Explanation, StringComparison.Ordinal)
                && existing.Decision == command.Decision
                && string.Equals(existing.ActorReference, command.Actor, StringComparison.Ordinal)
                && stored.Count == command.Lines.Count
                && stored.Zip(command.Lines).All(p => p.First.PalletContentLineId == p.Second.PalletContentLineId
                    && p.First.Quantity == p.Second.Quantity
                    && (p.Second.NetWeightKg == null || p.First.NetWeightKg == p.Second.NetWeightKg));
            return same
                ? await GetAsync(id, cancellationToken)
                : ServiceResult<PurchaseOrderDetailDto>.Conflict($"RequestId {command.RequestId} was already used for a different rejection.");
        }
    }
}
