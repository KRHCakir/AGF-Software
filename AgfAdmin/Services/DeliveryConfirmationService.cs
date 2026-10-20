using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public interface IDeliveryConfirmationService
    {
        Task<ServiceResult<DeliveryConfirmationDto>> ConfirmWarehouseAsync(int salesOrderId, int dispatchId, ConfirmDeliveryDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<DeliveryConfirmationDto>> ConfirmDirectTradeAsync(int salesOrderId, int shipmentId, ConfirmDeliveryDto request, CancellationToken cancellationToken = default);
    }

    // Afleverbevestiging van één volledige verzending: gedeelde kern (validatie, idempotentie, orderversie, audit, uitsluiting met correcties)
    // met twee adapters die de verzending van hun eigen soort laden. De soort komt uitsluitend uit het endpoint; magazijn- en DirectTrade-id's
    // hebben elk een eigen reeks en worden nooit door elkaar gebruikt. Verandert geen voorraad, broncapaciteit, reservering of verzonden
    // hoeveelheid. Versies: alleen de order (botst met correcties, verzenden, annuleren en een tweede bevestiging), niet de orderregels:
    // reserveren of toewijzen op dezelfde order mag gelijktijdig slagen, want aflevering verandert geen vraag of voorraad.
    public sealed class DeliveryConfirmationService : IDeliveryConfirmationService
    {
        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly AppDbContext _db;
        private readonly IBusinessCalendar _calendar;

        public DeliveryConfirmationService(AppDbContext db, IBusinessCalendar calendar)
        {
            _db = db;
            _calendar = calendar ?? db.BusinessCalendar;
        }

        private sealed record ConfirmCommand(Guid RequestId, string Kind, int SalesOrderId, int ShipmentId, int ExpectedOrderVersion, string Actor, DateOnly DeliveredOn, string ReceiptReference);

        // Wat de kern van een verzending moet weten, ongeacht de soort.
        private sealed record ShipmentFacts(int SalesOrderId, DateOnly ShippedOn, bool Corrected, bool Delivered, int LineCount, IReadOnlyDictionary<string, decimal> QuantityByUnit, decimal NetWeightKg);

        public Task<ServiceResult<DeliveryConfirmationDto>> ConfirmWarehouseAsync(int salesOrderId, int dispatchId, ConfirmDeliveryDto request, CancellationToken cancellationToken = default) =>
            ConfirmAsync(ReservationRules.ShipmentKindWarehouse, salesOrderId, dispatchId, request, async () =>
            {
                var dispatch = await _db.SalesDispatches.AsNoTracking().Include(d => d.Lines).SingleOrDefaultAsync(d => d.Id == dispatchId, cancellationToken);
                if (dispatch == null) return null;
                return new ShipmentFacts(dispatch.SalesOrderId, _calendar.DateOf(dispatch.DispatchedAt),
                    await _db.SalesDispatchCorrections.AnyAsync(c => c.SalesDispatchId == dispatchId, cancellationToken),
                    await _db.ShipmentDeliveryConfirmations.AnyAsync(c => c.SalesDispatchId == dispatchId, cancellationToken),
                    dispatch.Lines.Count, dispatch.Lines.GroupBy(l => l.Unit).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity)), dispatch.Lines.Sum(l => l.NetWeightKg));
            }, (confirmation, id) => confirmation.SalesDispatchId = id, cancellationToken);

        public Task<ServiceResult<DeliveryConfirmationDto>> ConfirmDirectTradeAsync(int salesOrderId, int shipmentId, ConfirmDeliveryDto request, CancellationToken cancellationToken = default) =>
            ConfirmAsync(ReservationRules.ShipmentKindDirectTrade, salesOrderId, shipmentId, request, async () =>
            {
                var shipment = await _db.DirectTradeShipments.AsNoTracking().Include(s => s.Lines).SingleOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
                if (shipment == null) return null;
                return new ShipmentFacts(shipment.SalesOrderId, shipment.ShippedOn,
                    await _db.DirectTradeShipmentCorrections.AnyAsync(c => c.DirectTradeShipmentId == shipmentId, cancellationToken),
                    await _db.ShipmentDeliveryConfirmations.AnyAsync(c => c.DirectTradeShipmentId == shipmentId, cancellationToken),
                    shipment.Lines.Count, shipment.Lines.GroupBy(l => l.Unit).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity)), shipment.Lines.Sum(l => l.NetWeightKg));
            }, (confirmation, id) => confirmation.DirectTradeShipmentId = id, cancellationToken);

        private async Task<ServiceResult<DeliveryConfirmationDto>> ConfirmAsync(string kind, int salesOrderId, int shipmentId, ConfirmDeliveryDto request,
            Func<Task<ShipmentFacts>> loadShipment, Action<ShipmentDeliveryConfirmation, int> link, CancellationToken cancellationToken)
        {
            var errors = new Dictionary<string, List<string>>();
            var command = Build(kind, salesOrderId, shipmentId, request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<DeliveryConfirmationDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var replay = await ReplayAsync(command, cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var facts = await loadShipment();
            var label = kind == ReservationRules.ShipmentKindWarehouse ? "Dispatch" : "DirectTrade shipment";
            if (facts == null || facts.SalesOrderId != salesOrderId)
            {
                return ServiceResult<DeliveryConfirmationDto>.NotFound($"{label} {shipmentId} does not exist on sales order {salesOrderId}.");
            }
            if (command.DeliveredOn < facts.ShippedOn)
            {
                ValidationErrors.Add(errors, "deliveredOn", $"{label} {shipmentId} was shipped on {facts.ShippedOn:yyyy-MM-dd} (Europe/Amsterdam); the delivery date cannot be earlier.");
                return ServiceResult<DeliveryConfirmationDto>.Validation(ValidationErrors.ToArrays(errors));
            }
            var order = await _db.SalesOrders.SingleAsync(o => o.Id == salesOrderId, cancellationToken);
            if (order.Status != ReservationRules.OrderStatusOpen)
            {
                return ServiceResult<DeliveryConfirmationDto>.Conflict($"Sales order {salesOrderId} is {order.Status}; a delivery confirmation requires an open order.");
            }
            if (order.Version != command.ExpectedOrderVersion)
            {
                return ServiceResult<DeliveryConfirmationDto>.Conflict($"Sales order {salesOrderId} is at version {order.Version}, not {command.ExpectedOrderVersion}; reload it and decide again.");
            }
            if (facts.Corrected)
            {
                return ServiceResult<DeliveryConfirmationDto>.Conflict($"{label} {shipmentId} has been corrected or taken back and can never be confirmed as delivered.");
            }
            if (facts.Delivered)
            {
                return ServiceResult<DeliveryConfirmationDto>.Conflict($"{label} {shipmentId} is already confirmed as delivered.");
            }

            var now = DateTime.UtcNow;
            var confirmation = new ShipmentDeliveryConfirmation
            {
                RequestId = command.RequestId,
                ShipmentKind = kind,
                SalesOrder = order,
                DeliveredOn = command.DeliveredOn,
                ReceiptReference = command.ReceiptReference,
                ActorReference = command.Actor,
                DeliveredInFullConfirmed = true,
                ConfirmationCode = ReservationRules.DeliveredInFullCode,
                ConfirmationStatement = ReservationRules.DeliveredInFullStatement,
                RecordedAt = now
            };
            link(confirmation, shipmentId);
            _db.ShipmentDeliveryConfirmations.Add(confirmation);

            order.UpdatedAt = now;
            _db.Entry(order).Property(o => o.Version).IsModified = true;

            _db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
            {
                SalesOrder = order,
                EventType = ReservationRules.DeliveryConfirmedEvent,
                OldValue = JsonSerializer.Serialize(new { status = order.Status, version = command.ExpectedOrderVersion, shipmentKind = kind, shipmentId }, SnapshotJson),
                NewValue = JsonSerializer.Serialize(new
                {
                    shipmentKind = kind,
                    shipmentId,
                    deliveredOn = command.DeliveredOn.ToString("yyyy-MM-dd"),
                    receiptReference = command.ReceiptReference,
                    lineCount = facts.LineCount,
                    deliveredQuantityByUnit = facts.QuantityByUnit.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value),
                    deliveredNetWeightKg = facts.NetWeightKg,
                    confirmationCode = ReservationRules.DeliveredInFullCode
                }, SnapshotJson),
                Reason = command.ReceiptReference,
                ActorReference = command.Actor,
                OccurredAt = now,
                RequestId = command.RequestId
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (DbErrors.IsConcurrencyConflict(exception) || DbErrors.IsUniqueViolation(exception))
            {
                _db.ChangeTracker.Clear();
                var concurrent = await ReplayAsync(command, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                return ServiceResult<DeliveryConfirmationDto>.Conflict($"Sales order {salesOrderId} or {label.ToLowerInvariant()} {shipmentId} was changed concurrently; reload and decide again.");
            }

            _db.ChangeTracker.Clear();
            return ServiceResult<DeliveryConfirmationDto>.Created(ToDto(await _db.ShipmentDeliveryConfirmations.AsNoTracking().SingleAsync(c => c.Id == confirmation.Id, cancellationToken)));
        }

        private ConfirmCommand Build(string kind, int salesOrderId, int shipmentId, ConfirmDeliveryDto request, Dictionary<string, List<string>> errors)
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
                ValidationErrors.Add(errors, "expectedOrderVersion", "The order version the confirmation is based on is required.");
            }
            var actor = ValidationErrors.RequiredText(request.ActorReference, ReservationRules.MaxActorLength, "actorReference", errors);
            var reference = ValidationErrors.OptionalText(request.ReceiptReference, ReservationRules.MaxReceiptReferenceLength, "receiptReference", errors);
            if (request.DeliveredInFull != true)
            {
                ValidationErrors.Add(errors, "deliveredInFull", $"Confirm explicitly: \"{ReservationRules.DeliveredInFullStatement}\" Partial receipt, shortages, damage or returns cannot be recorded as delivered in full.");
            }
            if (request.DeliveredOn == null)
            {
                ValidationErrors.Add(errors, "deliveredOn", "The actual delivery date (calendar date) is required.");
            }
            else
            {
                var today = _calendar.Today();
                if (request.DeliveredOn.Value > today)
                {
                    ValidationErrors.Add(errors, "deliveredOn", $"The delivery date {request.DeliveredOn.Value:yyyy-MM-dd} is after today ({today:yyyy-MM-dd}, Europe/Amsterdam); only deliveries that happened can be confirmed.");
                }
            }
            if (errors.Count > 0)
            {
                return null;
            }
            return new ConfirmCommand(request.RequestId!.Value, kind, salesOrderId, shipmentId, request.ExpectedOrderVersion!.Value, actor, request.DeliveredOn!.Value, reference);
        }

        // Replay: dezelfde requestId voor dezelfde soort, verzending, order, actor, referentie, datum, verklaring en orderversie.
        private async Task<ServiceResult<DeliveryConfirmationDto>> ReplayAsync(ConfirmCommand command, CancellationToken cancellationToken)
        {
            var existing = await _db.ShipmentDeliveryConfirmations.AsNoTracking().SingleOrDefaultAsync(c => c.RequestId == command.RequestId, cancellationToken);
            if (existing == null)
            {
                return null;
            }
            var audit = await _db.SalesOrderAuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(e => e.RequestId == command.RequestId && e.EventType == ReservationRules.DeliveryConfirmedEvent, cancellationToken);
            var existingShipment = existing.ShipmentKind == ReservationRules.ShipmentKindWarehouse ? existing.SalesDispatchId : existing.DirectTradeShipmentId;
            var same = string.Equals(existing.ShipmentKind, command.Kind, StringComparison.Ordinal)
                && existingShipment == command.ShipmentId
                && existing.SalesOrderId == command.SalesOrderId
                && existing.DeliveredInFullConfirmed
                && string.Equals(existing.ActorReference, command.Actor, StringComparison.Ordinal)
                && string.Equals(existing.ReceiptReference, command.ReceiptReference, StringComparison.Ordinal)
                && existing.DeliveredOn == command.DeliveredOn
                && audit != null
                && RecordedVersion(audit.OldValue) == command.ExpectedOrderVersion;
            return same
                ? ServiceResult<DeliveryConfirmationDto>.Ok(ToDto(existing))
                : ServiceResult<DeliveryConfirmationDto>.Conflict($"RequestId {command.RequestId} was already used for a different delivery confirmation.");
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

        internal static DeliveryConfirmationDto ToDto(ShipmentDeliveryConfirmation c) => new()
        {
            Id = c.Id,
            RequestId = c.RequestId,
            ShipmentKind = c.ShipmentKind,
            SalesDispatchId = c.SalesDispatchId,
            DirectTradeShipmentId = c.DirectTradeShipmentId,
            SalesOrderId = c.SalesOrderId,
            DeliveredOn = c.DeliveredOn,
            ReceiptReference = c.ReceiptReference,
            ActorReference = c.ActorReference,
            DeliveredInFullConfirmed = c.DeliveredInFullConfirmed,
            ConfirmationCode = c.ConfirmationCode,
            ConfirmationStatement = c.ConfirmationStatement,
            RecordedAt = DateTime.SpecifyKind(c.RecordedAt, DateTimeKind.Utc)
        };
    }
}
