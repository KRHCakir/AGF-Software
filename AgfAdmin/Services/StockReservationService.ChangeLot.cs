using System.Globalization;
using System.Text.Json;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Feedback 22 november: bij het picken blijft de partij aan te passen. Hergebruikt het bestaande reserveringsmodel: de oude reservering
    // wordt vrijgegeven (reden "Partij gewijzigd") en dezelfde hoeveelheid wordt uit de gekozen partij gereserveerd, oudste pallet eerst,
    // met dezelfde controles en auditregels als gewoon reserveren en vrijgeven. Alles in één opslag: er is nooit een moment zonder reservering.
    // De nieuwe reserveringen gaan mee met dezelfde levering. Op de order staat een auditregel met de oude en de nieuwe partij.
    public sealed partial class StockReservationService
    {
        public const string ReservationLotChangedEvent = "ReservationLotChanged";

        public async Task<ServiceResult<IReadOnlyList<StockReservationDto>>> ChangeLotAsync(int reservationId, ChangeReservationLotDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request?.RequestId == null || request.RequestId == Guid.Empty) ValidationErrors.Add(errors, "requestId", "A client-generated, non-empty requestId is required.");
            if (request?.ExpectedReservationVersion == null) ValidationErrors.Add(errors, "expectedReservationVersion", "The reservation version the change is based on is required.");
            if (request?.PartijId == null) ValidationErrors.Add(errors, "partijId", "Choose the lot to take the goods from.");
            if (errors.Count > 0) return ServiceResult<IReadOnlyList<StockReservationDto>>.Validation(ValidationErrors.ToArrays(errors));
            var requestId = request.RequestId!.Value;

            // Herhaling: dezelfde sleutel gaf al een wijziging van deze reservering naar deze partij.
            var done = await _db.SalesOrderAuditEvents.AsNoTracking().SingleOrDefaultAsync(e => e.RequestId == requestId && e.EventType == ReservationLotChangedEvent, cancellationToken);
            if (done != null)
            {
                using var json = JsonDocument.Parse(done.NewValue);
                var sameChange = json.RootElement.GetProperty("reservationId").GetInt32() == reservationId && json.RootElement.GetProperty("partijId").GetInt32() == request.PartijId;
                if (!sameChange) return ServiceResult<IReadOnlyList<StockReservationDto>>.Conflict($"RequestId {requestId} was already used for another lot change.");
                // De nieuwe reserveringen hebben afgeleide sleutels (zoals een reservering over meerdere pallets).
                var count = json.RootElement.GetProperty("count").GetInt32();
                var keys = Enumerable.Range(0, count).Select(i => (Guid?)ItemRequestId(requestId, i)).ToList();
                var ids = await _db.StockReservations.AsNoTracking().Where(r => keys.Contains(r.RequestId)).OrderBy(r => r.Id).Select(r => r.Id).ToListAsync(cancellationToken);
                var replayed = new List<StockReservationDto>();
                foreach (var id in ids) replayed.Add(await LoadDtoAsync(id, cancellationToken));
                return ServiceResult<IReadOnlyList<StockReservationDto>>.Ok(replayed);
            }

            var reservation = await _db.StockReservations
                .Include(r => r.PalletContentLine).ThenInclude(c => c.Partij)
                .Include(r => r.SalesOrderLine).ThenInclude(l => l.SalesOrder).ThenInclude(o => o.Deliveries)
                .SingleOrDefaultAsync(r => r.Id == reservationId, cancellationToken);
            if (reservation == null) return ServiceResult<IReadOnlyList<StockReservationDto>>.NotFound($"Reservation {reservationId} does not exist.");
            if (reservation.SalesOrderLine == null || reservation.PalletContentLine == null || reservation.Source != ReservationRules.SourcePhysical)
            {
                return ServiceResult<IReadOnlyList<StockReservationDto>>.Conflict($"Reservation {reservationId} is not a reservation of stock on a pallet.");
            }
            if (reservation.Version != request.ExpectedReservationVersion)
            {
                return ServiceResult<IReadOnlyList<StockReservationDto>>.Conflict($"Reservation {reservationId} is at version {reservation.Version}, not {request.ExpectedReservationVersion}; reload it and decide again.");
            }
            if (reservation.Status != ReservationRules.StatusActive)
            {
                return ServiceResult<IReadOnlyList<StockReservationDto>>.Conflict($"Reservation {reservationId} is {reservation.Status}; only an active reservation changes lot.");
            }
            var target = await _db.Partijen.AsNoTracking().Where(p => p.Id == request.PartijId).Select(p => new { p.Id, p.Number, p.Kind }).SingleOrDefaultAsync(cancellationToken);
            if (target == null) return ServiceResult<IReadOnlyList<StockReservationDto>>.NotFound($"Lot {request.PartijId} does not exist.");
            if (target.Kind != PartijRules.KindPurchase || target.Id == reservation.PalletContentLine.PartijId)
            {
                return ServiceResult<IReadOnlyList<StockReservationDto>>.Validation(new Dictionary<string, string[]> { ["partijId"] = new[] { "Choose another purchase lot than the current one." } });
            }

            // Oudste pallet eerst uit de gekozen partij, voor precies de hoeveelheid van de reservering.
            var line = reservation.SalesOrderLine;
            var candidates = SalesOrderService.FifoOrder((await SalesOrderService.ReservableAsync(_db, line.ProductVariantId, line.Unit, cancellationToken)).Where(c => c.PartijId == target.Id)).ToList();
            var items = SalesOrderService.ProposeFifo(candidates, reservation.Quantity);
            var free = candidates.Sum(c => c.AvailableQuantity);
            if (items.Sum(i => i.Quantity) < reservation.Quantity)
            {
                return ServiceResult<IReadOnlyList<StockReservationDto>>.Conflict($"Lot {target.Number} has {free.ToString("0.###", CultureInfo.InvariantCulture)} {line.Unit} free, not {reservation.Quantity.ToString("0.###", CultureInfo.InvariantCulture)}; choose another lot or split the line.");
            }

            var now = DateTime.UtcNow;
            var actor = _user.Actor(ReservationRules.MaxActorLength);
            var oldLot = reservation.PalletContentLine.Partij?.Number ?? reservation.PalletContentLine.LotNumber;
            var reason = $"Partij gewijzigd: {oldLot} → {target.Number}";
            var oldValue = JsonSerializer.Serialize(new { reservationId = reservation.Id, status = ReservationRules.StatusActive, version = reservation.Version }, SnapshotJson);
            reservation.Status = ReservationRules.StatusReleased;
            reservation.ReleasedAt = now;
            reservation.ReleaseReason = reason.Length > ReservationRules.MaxReasonLength ? reason[..ReservationRules.MaxReasonLength] : reason;
            reservation.ReleaseRequestId = requestId;
            reservation.UpdatedAt = now;
            _db.Entry(reservation.PalletContentLine).Property(c => c.Version).IsModified = true;
            _db.PalletAuditEvents.Add(new PalletAuditEvent
            {
                PalletId = reservation.PalletContentLine.PalletId, PalletContentLineId = reservation.PalletContentLine.Id, EventType = ReservationRules.ReservationReleasedEvent,
                OldValue = oldValue,
                NewValue = JsonSerializer.Serialize(new { reservationId = reservation.Id, status = ReservationRules.StatusReleased, quantity = reservation.Quantity, unit = reservation.Unit, netWeightKg = reservation.NetWeightKg }, SnapshotJson),
                Reason = reservation.ReleaseReason, ActorReference = actor, OccurredAt = now, RequestId = requestId,
            });

            var contentIds = items.Select(i => i.PalletContentLineId).ToList();
            var contents = await _db.PalletContentLines.Include(c => c.Pallet).Include(c => c.SupplyCommitmentLine).ThenInclude(l => l.SupplyCommitment)
                .Where(c => contentIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
            var reservedOnContent = await SalesOrderService.ReservedByContentLineAsync(_db, contentIds, cancellationToken);
            var reservedOnLine = await SalesOrderService.CommittedOnOrderLineAsync(_db, line.Id, cancellationToken) - reservation.Quantity;
            // De eerste levering is een lege levering; bij meer leveringen blijft de nieuwe reservering expliciet bij dezelfde levering.
            var deliveryId = reservation.SalesDeliveryId ?? (line.SalesOrder.Deliveries.Count > 1 ? line.SalesOrder.Deliveries.OrderBy(d => d.Sequence).First().Id : null);
            var created = new List<StockReservation>();
            for (var i = 0; i < items.Count; i++)
            {
                var command = new ReserveCommand(ItemRequestId(requestId, i), line.SalesOrderId, line.Id, items[i].PalletContentLineId, items[i].Quantity, line.Unit, items[i].NetWeightKg, actor, reason);
                var content = contents[command.PalletContentLineId];
                var conflict = FindReserveConflict(line, content, command, reservedOnContent.GetValueOrDefault(content.Id), reservedOnLine);
                if (conflict != null)
                {
                    _db.ChangeTracker.Clear();
                    return ServiceResult<IReadOnlyList<StockReservationDto>>.Conflict(conflict);
                }
                var added = AddReservation(line, content, command, now);
                added.SalesDeliveryId = deliveryId;
                created.Add(added);
                reservedOnLine += command.Quantity;
            }
            _db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
            {
                SalesOrderId = line.SalesOrderId, EventType = ReservationLotChangedEvent, RequestId = requestId, ActorReference = actor, OccurredAt = now, Reason = reason,
                OldValue = JsonSerializer.Serialize(new { reservationId = reservation.Id, partijId = reservation.PalletContentLine.PartijId, partij = oldLot, quantity = reservation.Quantity }, SnapshotJson),
                NewValue = JsonSerializer.Serialize(new { reservationId = reservation.Id, partijId = target.Id, partij = target.Number, count = created.Count }, SnapshotJson),
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                _db.ChangeTracker.Clear();
                if (exception is DbUpdateConcurrencyException || DbErrors.IsUniqueViolation(exception))
                {
                    return ServiceResult<IReadOnlyList<StockReservationDto>>.Conflict($"Reservation {reservationId} or the stock was changed at the same time; reload and try again.");
                }
                throw;
            }
            var dtos = new List<StockReservationDto>();
            foreach (var r in created) dtos.Add(await LoadDtoAsync(r.Id, cancellationToken));
            return ServiceResult<IReadOnlyList<StockReservationDto>>.Created(dtos);
        }
    }
}
