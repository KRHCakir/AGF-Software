using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public interface IDeliveryRetractionService
    {
        Task<ServiceResult<DeliveryRetractionDto>> RetractWarehouseAsync(int salesOrderId, int dispatchId, int confirmationId, RetractDeliveryDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<DeliveryRetractionDto>> RetractDirectTradeAsync(int salesOrderId, int shipmentId, int confirmationId, RetractDeliveryDto request, CancellationToken cancellationToken = default);
    }

    // Intrekken van een administratief onjuiste afleverbevestiging, voor magazijn en DirectTrade (de soort komt uit het endpoint). De
    // bevestiging blijft bewaard; er komt een gekoppeld append-only intrekkingsrecord bij. Daarna is de verzending niet meer afgeleverd
    // (DeliveryState) en kan ze met een nieuw commando opnieuw worden bevestigd of, als de overige voorwaarden gelden, gecorrigeerd.
    // Verandert geen voorraad, broncapaciteit, reservering of verzonden hoeveelheid; alleen de orderversie gaat omhoog, zodat intrekken
    // serialiseert met bevestigen, correcties, verzenden en annuleren, maar niet met reserveren of toewijzen.
    public sealed class DeliveryRetractionService : IDeliveryRetractionService
    {
        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly AppDbContext _db;

        public DeliveryRetractionService(AppDbContext db)
        {
            _db = db;
        }

        private sealed record RetractCommand(Guid RequestId, string Kind, int SalesOrderId, int ShipmentId, int ConfirmationId, int ExpectedOrderVersion, string Actor, string Reason);

        private sealed record ShipmentLines(int LineCount, IReadOnlyDictionary<string, decimal> QuantityByUnit, decimal NetWeightKg);

        public Task<ServiceResult<DeliveryRetractionDto>> RetractWarehouseAsync(int salesOrderId, int dispatchId, int confirmationId, RetractDeliveryDto request, CancellationToken cancellationToken = default) =>
            RetractAsync(ReservationRules.ShipmentKindWarehouse, salesOrderId, dispatchId, confirmationId, request, async () =>
            {
                var lines = await _db.SalesDispatchLines.AsNoTracking().Where(l => l.SalesDispatchId == dispatchId).Select(l => new { l.Unit, l.Quantity, l.NetWeightKg }).ToListAsync(cancellationToken);
                return new ShipmentLines(lines.Count, lines.GroupBy(l => l.Unit).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity)), lines.Sum(l => l.NetWeightKg));
            }, cancellationToken);

        public Task<ServiceResult<DeliveryRetractionDto>> RetractDirectTradeAsync(int salesOrderId, int shipmentId, int confirmationId, RetractDeliveryDto request, CancellationToken cancellationToken = default) =>
            RetractAsync(ReservationRules.ShipmentKindDirectTrade, salesOrderId, shipmentId, confirmationId, request, async () =>
            {
                var lines = await _db.DirectTradeShipmentLines.AsNoTracking().Where(l => l.DirectTradeShipmentId == shipmentId).Select(l => new { l.Unit, l.Quantity, l.NetWeightKg }).ToListAsync(cancellationToken);
                return new ShipmentLines(lines.Count, lines.GroupBy(l => l.Unit).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity)), lines.Sum(l => l.NetWeightKg));
            }, cancellationToken);

        private async Task<ServiceResult<DeliveryRetractionDto>> RetractAsync(string kind, int salesOrderId, int shipmentId, int confirmationId, RetractDeliveryDto request,
            Func<Task<ShipmentLines>> loadLines, CancellationToken cancellationToken)
        {
            var errors = new Dictionary<string, List<string>>();
            var command = Build(kind, salesOrderId, shipmentId, confirmationId, request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<DeliveryRetractionDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var replay = await ReplayAsync(command, cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var label = kind == ReservationRules.ShipmentKindWarehouse ? "dispatch" : "DirectTrade shipment";
            var confirmation = await _db.ShipmentDeliveryConfirmations.AsNoTracking().Include(c => c.Retraction).SingleOrDefaultAsync(c => c.Id == confirmationId, cancellationToken);
            var confirmationShipment = confirmation == null ? null : kind == ReservationRules.ShipmentKindWarehouse ? confirmation.SalesDispatchId : confirmation.DirectTradeShipmentId;
            if (confirmation == null || confirmation.ShipmentKind != kind || confirmationShipment != shipmentId || confirmation.SalesOrderId != salesOrderId)
            {
                return ServiceResult<DeliveryRetractionDto>.NotFound($"Delivery confirmation {confirmationId} does not exist for {label} {shipmentId} on sales order {salesOrderId}.");
            }
            var order = await _db.SalesOrders.SingleAsync(o => o.Id == salesOrderId, cancellationToken);
            if (order.Status != ReservationRules.OrderStatusOpen)
            {
                return ServiceResult<DeliveryRetractionDto>.Conflict($"Sales order {salesOrderId} is {order.Status}; retracting a delivery confirmation requires an open order.");
            }
            if (order.Version != command.ExpectedOrderVersion)
            {
                return ServiceResult<DeliveryRetractionDto>.Conflict($"Sales order {salesOrderId} is at version {order.Version}, not {command.ExpectedOrderVersion}; reload it and decide again.");
            }
            if (!DeliveryState.Active(confirmation))
            {
                return ServiceResult<DeliveryRetractionDto>.Conflict($"Delivery confirmation {confirmationId} has already been retracted.");
            }

            var lines = await loadLines();
            var now = DateTime.UtcNow;
            var retraction = new ShipmentDeliveryRetraction
            {
                RequestId = command.RequestId,
                ShipmentDeliveryConfirmationId = confirmation.Id,
                ShipmentKind = confirmation.ShipmentKind,
                SalesDispatchId = confirmation.SalesDispatchId,
                DirectTradeShipmentId = confirmation.DirectTradeShipmentId,
                SalesOrder = order,
                Reason = command.Reason,
                ActorReference = command.Actor,
                RecordedInErrorConfirmed = true,
                ConfirmationCode = ReservationRules.RecordedInErrorCode,
                ConfirmationStatement = ReservationRules.RecordedInErrorStatement,
                RecordedAt = now
            };
            _db.ShipmentDeliveryRetractions.Add(retraction);

            order.UpdatedAt = now;
            _db.Entry(order).Property(o => o.Version).IsModified = true;

            _db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
            {
                SalesOrder = order,
                EventType = ReservationRules.DeliveryRetractedEvent,
                OldValue = JsonSerializer.Serialize(new { status = order.Status, version = command.ExpectedOrderVersion, shipmentKind = kind, shipmentId, confirmationId }, SnapshotJson),
                NewValue = JsonSerializer.Serialize(new
                {
                    shipmentKind = kind,
                    shipmentId,
                    confirmationId,
                    confirmationSequence = confirmation.Sequence,
                    deliveredOn = confirmation.DeliveredOn.ToString("yyyy-MM-dd"),
                    lineCount = lines.LineCount,
                    retractedQuantityByUnit = lines.QuantityByUnit.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value),
                    retractedNetWeightKg = lines.NetWeightKg,
                    confirmationCode = ReservationRules.RecordedInErrorCode
                }, SnapshotJson),
                Reason = command.Reason,
                ActorReference = command.Actor,
                OccurredAt = now,
                RequestId = command.RequestId
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (ShipmentDeliveryConflictException conflict)
            {
                // Een andere schrijver heeft na onze controles en vóór de guard deze bevestiging ingetrokken. Er is niets opgeslagen.
                _db.ChangeTracker.Clear();
                var concurrent = await ReplayAsync(command, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                return ServiceResult<DeliveryRetractionDto>.Conflict($"{conflict.Message} The confirmation was changed concurrently; reload and decide again.");
            }
            catch (DbUpdateException exception) when (DbErrors.IsConcurrencyConflict(exception) || DbErrors.IsUniqueViolation(exception))
            {
                _db.ChangeTracker.Clear();
                var concurrent = await ReplayAsync(command, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                return ServiceResult<DeliveryRetractionDto>.Conflict($"Sales order {salesOrderId} or delivery confirmation {confirmationId} was changed concurrently; reload and decide again.");
            }

            _db.ChangeTracker.Clear();
            return ServiceResult<DeliveryRetractionDto>.Created(ToDto(await _db.ShipmentDeliveryRetractions.AsNoTracking().SingleAsync(r => r.Id == retraction.Id, cancellationToken)));
        }

        private static RetractCommand Build(string kind, int salesOrderId, int shipmentId, int confirmationId, RetractDeliveryDto request, Dictionary<string, List<string>> errors)
        {
            if (request == null)
            {
                ValidationErrors.Add(errors, "request", "A request body is required.");
                return null;
            }
            if (request.RequestId == null || request.RequestId == Guid.Empty)
            {
                ValidationErrors.Add(errors, "requestId", "A client-generated, non-empty requestId is required.");
            }
            if (request.ExpectedOrderVersion == null)
            {
                ValidationErrors.Add(errors, "expectedOrderVersion", "The order version the retraction is based on is required.");
            }
            var actor = ValidationErrors.RequiredText(request.ActorReference, ReservationRules.MaxActorLength, "actorReference", errors);
            var reason = ValidationErrors.RequiredText(request.Reason, ReservationRules.MaxReasonLength, "reason", errors);
            if (request.RecordedInError != true)
            {
                ValidationErrors.Add(errors, "recordedInError", $"Confirm explicitly: \"{ReservationRules.RecordedInErrorStatement}\" A real return, damage or shortage is not a retraction.");
            }
            if (errors.Count > 0)
            {
                return null;
            }
            return new RetractCommand(request.RequestId!.Value, kind, salesOrderId, shipmentId, confirmationId, request.ExpectedOrderVersion!.Value, actor, reason);
        }

        // Replay: dezelfde requestId voor dezelfde soort, verzending, bevestiging, order, actor, reden en orderversie.
        private async Task<ServiceResult<DeliveryRetractionDto>> ReplayAsync(RetractCommand command, CancellationToken cancellationToken)
        {
            var existing = await _db.ShipmentDeliveryRetractions.AsNoTracking().SingleOrDefaultAsync(r => r.RequestId == command.RequestId, cancellationToken);
            if (existing == null)
            {
                return null;
            }
            var audit = await _db.SalesOrderAuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(e => e.RequestId == command.RequestId && e.EventType == ReservationRules.DeliveryRetractedEvent, cancellationToken);
            var existingShipment = existing.ShipmentKind == ReservationRules.ShipmentKindWarehouse ? existing.SalesDispatchId : existing.DirectTradeShipmentId;
            var same = string.Equals(existing.ShipmentKind, command.Kind, StringComparison.Ordinal)
                && existingShipment == command.ShipmentId
                && existing.ShipmentDeliveryConfirmationId == command.ConfirmationId
                && existing.SalesOrderId == command.SalesOrderId
                && existing.RecordedInErrorConfirmed
                && string.Equals(existing.ActorReference, command.Actor, StringComparison.Ordinal)
                && string.Equals(existing.Reason, command.Reason, StringComparison.Ordinal)
                && audit != null
                && RecordedVersion(audit.OldValue) == command.ExpectedOrderVersion;
            return same
                ? ServiceResult<DeliveryRetractionDto>.Ok(ToDto(existing))
                : ServiceResult<DeliveryRetractionDto>.Conflict($"RequestId {command.RequestId} was already used for a different retraction.");
        }

        private static int? RecordedVersion(string json)
        {
            if (json == null) return null;
            try
            {
                using var document = JsonDocument.Parse(json);
                return document.RootElement.TryGetProperty("version", out var version) && version.TryGetInt32(out var value) ? value : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        internal static DeliveryRetractionDto ToDto(ShipmentDeliveryRetraction r) => new()
        {
            Id = r.Id,
            RequestId = r.RequestId,
            ShipmentDeliveryConfirmationId = r.ShipmentDeliveryConfirmationId,
            ShipmentKind = r.ShipmentKind,
            SalesDispatchId = r.SalesDispatchId,
            DirectTradeShipmentId = r.DirectTradeShipmentId,
            SalesOrderId = r.SalesOrderId,
            Reason = r.Reason,
            ActorReference = r.ActorReference,
            RecordedInErrorConfirmed = r.RecordedInErrorConfirmed,
            ConfirmationCode = r.ConfirmationCode,
            ConfirmationStatement = r.ConfirmationStatement,
            RecordedAt = DateTime.SpecifyKind(r.RecordedAt, DateTimeKind.Utc)
        };
    }
}
