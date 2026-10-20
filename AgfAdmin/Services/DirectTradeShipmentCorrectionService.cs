using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public interface IDirectTradeShipmentCorrectionService
    {
        Task<ServiceResult<DirectTradeShipmentCorrectionDto>> CorrectAsync(int salesOrderId, int shipmentId, CorrectDirectTradeShipmentDto request, CancellationToken cancellationToken = default);
    }

    // Administratieve terugname van een volledige DirectTrade-verzending die niet heeft plaatsgevonden. De gebruiker verklaart dit expliciet;
    // de software stelt het niet zelf vast. De oorspronkelijke verzending, regels en snapshots blijven ongewijzigd. Per verzendregel een
    // correctieregel en de toewijzing Consumed -> Corrected (eindtoestand), in één transactie met de audit en de versies. Er wordt geen
    // toewijzing hersteld en er ontstaat geen voorraad, pallet, mutatie of retour. Gecorrigeerde hoeveelheden tellen niet meer als verzonden
    // of als bezette broncapaciteit: de ordervraag komt open en de capaciteit is opnieuw toewijsbaar onder de bestaande voorwaarden.
    // Versies: order en elke betrokken orderregel worden aangeraakt, de reserveringen via hun statuswijziging; de bronregel bewust niet
    // (de correctie geeft alleen capaciteit vrij; gelijktijdige toewijzingen blijven onderling via de bronversie beschermd).
    public sealed class DirectTradeShipmentCorrectionService : IDirectTradeShipmentCorrectionService
    {
        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly AppDbContext _db;

        public DirectTradeShipmentCorrectionService(AppDbContext db)
        {
            _db = db;
        }

        private sealed record CorrectCommand(Guid RequestId, int SalesOrderId, int ShipmentId, int ExpectedOrderVersion, string Actor, string Reason, IReadOnlyList<(int ReservationId, int ExpectedVersion)> Lines);

        public async Task<ServiceResult<DirectTradeShipmentCorrectionDto>> CorrectAsync(int salesOrderId, int shipmentId, CorrectDirectTradeShipmentDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var command = Build(salesOrderId, shipmentId, request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<DirectTradeShipmentCorrectionDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var replay = await ReplayAsync(command, cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var shipment = await _db.DirectTradeShipments
                .Include(s => s.Lines).ThenInclude(l => l.StockReservation)
                .AsSplitQuery()
                .SingleOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
            if (shipment == null || shipment.SalesOrderId != salesOrderId)
            {
                return ServiceResult<DirectTradeShipmentCorrectionDto>.NotFound($"DirectTrade shipment {shipmentId} does not exist on sales order {salesOrderId}.");
            }
            var order = await _db.SalesOrders.Include(o => o.Lines).SingleAsync(o => o.Id == salesOrderId, cancellationToken);
            if (order.Status != ReservationRules.OrderStatusOpen)
            {
                return ServiceResult<DirectTradeShipmentCorrectionDto>.Conflict($"Sales order {salesOrderId} is {order.Status}; a DirectTrade shipment correction requires an open order.");
            }
            if (order.Version != command.ExpectedOrderVersion)
            {
                return ServiceResult<DirectTradeShipmentCorrectionDto>.Conflict($"Sales order {salesOrderId} is at version {order.Version}, not {command.ExpectedOrderVersion}; reload it and decide again.");
            }
            if (await _db.DirectTradeShipmentCorrections.AnyAsync(c => c.DirectTradeShipmentId == shipmentId, cancellationToken))
            {
                return ServiceResult<DirectTradeShipmentCorrectionDto>.Conflict($"DirectTrade shipment {shipmentId} has already been taken back.");
            }
            if (await _db.ShipmentDeliveryConfirmations.Where(c => c.DirectTradeShipmentId == shipmentId).AnyAsync(DeliveryState.IsActive, cancellationToken))
            {
                return ServiceResult<DirectTradeShipmentCorrectionDto>.Conflict($"DirectTrade shipment {shipmentId} is confirmed as delivered; it cannot be taken back as a shipment that did not take place. Retract the active delivery confirmation first if it was recorded in error.");
            }

            // Hele verzending: exact de toewijzingen van deze verzending, elk met de verwachte versie. Een id van een magazijnreservering,
            // magazijnverzending of andere verzending hoort daar nooit bij, ook niet als het nummer toevallig gelijk is.
            var byReservation = shipment.Lines.ToDictionary(l => l.StockReservationId);
            if (command.Lines.Count != shipment.Lines.Count || command.Lines.Any(l => !byReservation.ContainsKey(l.ReservationId)))
            {
                return ServiceResult<DirectTradeShipmentCorrectionDto>.Conflict($"A correction takes back the whole DirectTrade shipment: all {shipment.Lines.Count} allocations of shipment {shipmentId} must be listed, and no others.");
            }
            foreach (var (reservationId, expectedVersion) in command.Lines)
            {
                var reservation = byReservation[reservationId].StockReservation;
                if (reservation.Source != ReservationRules.SourceDirectTrade)
                {
                    return ServiceResult<DirectTradeShipmentCorrectionDto>.Conflict($"Reservation {reservationId} is not a DirectTrade allocation.");
                }
                if (reservation.Status != ReservationRules.StatusConsumed)
                {
                    return ServiceResult<DirectTradeShipmentCorrectionDto>.Conflict($"DirectTrade allocation {reservationId} is {reservation.Status}, not shipped (Consumed); it cannot be taken back.");
                }
                if (reservation.Version != expectedVersion)
                {
                    return ServiceResult<DirectTradeShipmentCorrectionDto>.Conflict($"DirectTrade allocation {reservationId} is at version {reservation.Version}, not {expectedVersion}; reload it and decide again.");
                }
            }

            var now = DateTime.UtcNow;
            var correction = new DirectTradeShipmentCorrection
            {
                RequestId = command.RequestId,
                DirectTradeShipment = shipment,
                SalesOrder = order,
                Reason = command.Reason,
                ActorReference = command.Actor,
                ShipmentDidNotTakePlaceConfirmed = true,
                ConfirmationCode = ReservationRules.ShipmentDidNotTakePlaceCode,
                ConfirmationStatement = ReservationRules.ShipmentDidNotTakePlaceStatement,
                CorrectedAt = now
            };
            _db.DirectTradeShipmentCorrections.Add(correction);

            order.UpdatedAt = now;
            _db.Entry(order).Property(o => o.Version).IsModified = true;
            var touchedLines = shipment.Lines.Select(l => l.SalesOrderLineId).ToHashSet();
            foreach (var orderLine in order.Lines.Where(l => touchedLines.Contains(l.Id)))
            {
                _db.Entry(orderLine).Property(l => l.Version).IsModified = true;
            }

            foreach (var (reservationId, expectedVersion) in command.Lines)
            {
                var shipmentLine = byReservation[reservationId];
                var reservation = shipmentLine.StockReservation;
                reservation.Status = ReservationRules.StatusCorrected;
                reservation.CorrectedAt = now;
                reservation.UpdatedAt = now;
                correction.Lines.Add(new DirectTradeShipmentCorrectionLine
                {
                    DirectTradeShipmentLine = shipmentLine,
                    StockReservation = reservation,
                    SupplyCommitmentLineId = shipmentLine.SupplyCommitmentLineId,
                    SalesOrderLineId = shipmentLine.SalesOrderLineId,
                    Quantity = shipmentLine.Quantity,
                    Unit = shipmentLine.Unit,
                    NetWeightKg = shipmentLine.NetWeightKg,
                    ExpectedReservationVersion = expectedVersion
                });
            }

            // Begrensd: alleen aantallen en totalen per eenheid (er zijn maar vijf eenheden) en kg. De details per bronregel staan in de correctieregels.
            _db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
            {
                SalesOrder = order,
                EventType = ReservationRules.DirectTradeShipmentCorrectedEvent,
                OldValue = JsonSerializer.Serialize(new { status = order.Status, version = command.ExpectedOrderVersion, directTradeShipmentId = shipment.Id }, SnapshotJson),
                NewValue = JsonSerializer.Serialize(new
                {
                    status = order.Status,
                    directTradeShipmentId = shipment.Id,
                    correctedAllocationCount = shipment.Lines.Count,
                    sourceLineCount = shipment.Lines.Select(l => l.SupplyCommitmentLineId).Distinct().Count(),
                    freedQuantityByUnit = shipment.Lines.GroupBy(l => l.Unit).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity)),
                    freedNetWeightKg = shipment.Lines.Sum(l => l.NetWeightKg),
                    confirmationCode = ReservationRules.ShipmentDidNotTakePlaceCode,
                    shipmentDidNotTakePlaceConfirmedByUser = true
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
                // Een andere schrijver heeft na onze controles en vóór de guard een aflevering of correctie van deze verzending gecommit.
                // Er is niets opgeslagen (de guard draait vóór de database-update). Alleen dit herkenbare conflict wordt 409.
                _db.ChangeTracker.Clear();
                var concurrent = await ReplayAsync(command, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                return ServiceResult<DirectTradeShipmentCorrectionDto>.Conflict($"{conflict.Message} The delivery was confirmed concurrently; reload and decide again.");
            }
            catch (DbUpdateException exception) when (DbErrors.IsConcurrencyConflict(exception) || DbErrors.IsUniqueViolation(exception))
            {
                _db.ChangeTracker.Clear();
                var concurrent = await ReplayAsync(command, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                return ServiceResult<DirectTradeShipmentCorrectionDto>.Conflict($"Sales order {salesOrderId}, DirectTrade shipment {shipmentId} or its allocations were changed concurrently; reload and decide again.");
            }

            _db.ChangeTracker.Clear();
            return ServiceResult<DirectTradeShipmentCorrectionDto>.Created(ToDto(await Query().SingleAsync(c => c.Id == correction.Id, cancellationToken)));
        }

        private static CorrectCommand Build(int salesOrderId, int shipmentId, CorrectDirectTradeShipmentDto request, Dictionary<string, List<string>> errors)
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
                ValidationErrors.Add(errors, "expectedOrderVersion", "The order version the correction is based on is required.");
            }
            var actor = ValidationErrors.RequiredText(request.ActorReference, ReservationRules.MaxActorLength, "actorReference", errors);
            var reason = ValidationErrors.RequiredText(request.Reason, ReservationRules.MaxReasonLength, "reason", errors);
            if (request.ShipmentDidNotTakePlace != true)
            {
                ValidationErrors.Add(errors, "shipmentDidNotTakePlace", $"Confirm explicitly: \"{ReservationRules.ShipmentDidNotTakePlaceStatement}\" A real return or a correction of only kg, lot or part of the quantity is a different flow.");
            }
            var lines = new List<(int, int)>();
            if (request.Lines == null || request.Lines.Count == 0)
            {
                ValidationErrors.Add(errors, "lines", "List every allocation of the shipment with its expected version.");
            }
            else if (request.Lines.Count > ReservationRules.MaxDispatchLines)
            {
                ValidationErrors.Add(errors, "lines", $"At most {ReservationRules.MaxDispatchLines} allocations can be listed.");
            }
            else
            {
                var seen = new HashSet<int>();
                for (var i = 0; i < request.Lines.Count; i++)
                {
                    var key = $"lines[{i}]";
                    var line = request.Lines[i];
                    if (line?.StockReservationId == null || line.StockReservationId <= 0)
                    {
                        ValidationErrors.Add(errors, $"{key}.stockReservationId", "An allocation is required.");
                        continue;
                    }
                    if (line.ExpectedReservationVersion == null || line.ExpectedReservationVersion < 0)
                    {
                        ValidationErrors.Add(errors, $"{key}.expectedReservationVersion", "The allocation version the correction is based on is required.");
                        continue;
                    }
                    if (!seen.Add(line.StockReservationId.Value))
                    {
                        ValidationErrors.Add(errors, key, "The same allocation occurs more than once.");
                        continue;
                    }
                    lines.Add((line.StockReservationId.Value, line.ExpectedReservationVersion.Value));
                }
            }
            if (errors.Count > 0)
            {
                return null;
            }
            return new CorrectCommand(request.RequestId!.Value, salesOrderId, shipmentId, request.ExpectedOrderVersion!.Value, actor, reason, lines);
        }

        // Replay: dezelfde requestId voor dezelfde order, verzending, actor, genormaliseerde reden, verklaring, orderversie en regelset met versies.
        private async Task<ServiceResult<DirectTradeShipmentCorrectionDto>> ReplayAsync(CorrectCommand command, CancellationToken cancellationToken)
        {
            var existing = await Query().SingleOrDefaultAsync(c => c.RequestId == command.RequestId, cancellationToken);
            if (existing == null)
            {
                return null;
            }
            var audit = await _db.SalesOrderAuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(e => e.RequestId == command.RequestId && e.EventType == ReservationRules.DirectTradeShipmentCorrectedEvent, cancellationToken);
            var existingLines = existing.Lines.OrderBy(l => l.StockReservationId).Select(l => (l.StockReservationId, l.ExpectedReservationVersion)).ToList();
            var commandLines = command.Lines.OrderBy(l => l.ReservationId).Select(l => (l.ReservationId, l.ExpectedVersion)).ToList();
            var same = existing.SalesOrderId == command.SalesOrderId
                && existing.DirectTradeShipmentId == command.ShipmentId
                && existing.ShipmentDidNotTakePlaceConfirmed
                && string.Equals(existing.ConfirmationCode, ReservationRules.ShipmentDidNotTakePlaceCode, StringComparison.Ordinal)
                && string.Equals(existing.ActorReference, command.Actor, StringComparison.Ordinal)
                && string.Equals(existing.Reason, command.Reason, StringComparison.Ordinal)
                && audit != null
                && RecordedVersion(audit.OldValue) == command.ExpectedOrderVersion
                && existingLines.SequenceEqual(commandLines);
            return same
                ? ServiceResult<DirectTradeShipmentCorrectionDto>.Ok(ToDto(existing))
                : ServiceResult<DirectTradeShipmentCorrectionDto>.Conflict($"RequestId {command.RequestId} was already used for a different DirectTrade shipment correction.");
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

        private IQueryable<DirectTradeShipmentCorrection> Query() =>
            _db.DirectTradeShipmentCorrections.AsNoTracking().Include(c => c.Lines);

        internal static DirectTradeShipmentCorrectionDto ToDto(DirectTradeShipmentCorrection correction) => new()
        {
            Id = correction.Id,
            RequestId = correction.RequestId,
            DirectTradeShipmentId = correction.DirectTradeShipmentId,
            SalesOrderId = correction.SalesOrderId,
            Reason = correction.Reason,
            ActorReference = correction.ActorReference,
            ShipmentDidNotTakePlaceConfirmed = correction.ShipmentDidNotTakePlaceConfirmed,
            ConfirmationCode = correction.ConfirmationCode,
            ConfirmationStatement = correction.ConfirmationStatement,
            CorrectedAt = DateTime.SpecifyKind(correction.CorrectedAt, DateTimeKind.Utc),
            Lines = correction.Lines.OrderBy(l => l.Id).Select(l => new DirectTradeShipmentCorrectionLineDto
            {
                Id = l.Id,
                DirectTradeShipmentLineId = l.DirectTradeShipmentLineId,
                StockReservationId = l.StockReservationId,
                SupplyCommitmentLineId = l.SupplyCommitmentLineId,
                SalesOrderLineId = l.SalesOrderLineId,
                Quantity = l.Quantity,
                Unit = l.Unit,
                NetWeightKg = l.NetWeightKg
            }).ToList()
        };
    }
}
