using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public interface IStockReservationService
    {
        Task<ServiceResult<StockReservationDto>> ReserveAsync(int salesOrderId, int salesOrderLineId, CreateStockReservationDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<StockReservationDto>> ReleaseAsync(int reservationId, ReleaseStockReservationDto request, CancellationToken cancellationToken = default);
    }

    // Reserveren op precies één PalletContentLine (Warehouse-route, Received + Warehouse, Approved) en weer vrijgeven.
    // Hoeveelheid, eenheid en netto-kg zijn altijd expliciet; er wordt niets afgeleid of omgerekend.
    // Concurrency: reserveren raakt de inhoudsregel, de orderregel en de pallet aan (versie +1, concurrency-token);
    // vrijgeven controleert expectedReservationVersion en raakt de inhoudsregel aan. Eén save = één transactie met audit.
    public sealed class StockReservationService : IStockReservationService
    {
        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly AppDbContext _db;

        public StockReservationService(AppDbContext db)
        {
            _db = db;
        }

        // ======================== Reserveren ========================

        private sealed record ReserveCommand(Guid RequestId, int SalesOrderId, int SalesOrderLineId, int PalletContentLineId, decimal Quantity, string Unit, decimal NetWeightKg, string Actor, string Reason);

        public async Task<ServiceResult<StockReservationDto>> ReserveAsync(int salesOrderId, int salesOrderLineId, CreateStockReservationDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var command = BuildReserve(salesOrderId, salesOrderLineId, request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<StockReservationDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var replay = await ReplayReserveAsync(command, cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var line = await _db.SalesOrderLines.Include(l => l.SalesOrder)
                .SingleOrDefaultAsync(l => l.Id == salesOrderLineId && l.SalesOrderId == salesOrderId, cancellationToken);
            if (line == null)
            {
                return ServiceResult<StockReservationDto>.NotFound($"Sales order line {salesOrderLineId} does not exist on sales order {salesOrderId}.");
            }
            var content = await _db.PalletContentLines
                .Include(c => c.Pallet)
                .Include(c => c.SupplyCommitmentLine).ThenInclude(s => s.SupplyCommitment)
                .SingleOrDefaultAsync(c => c.Id == command.PalletContentLineId, cancellationToken);
            if (content == null)
            {
                return ServiceResult<StockReservationDto>.NotFound($"Pallet content line {command.PalletContentLineId} does not exist.");
            }

            var reservedOnContent = (await SalesOrderService.ReservedByContentLineAsync(_db, new List<int> { content.Id }, cancellationToken)).GetValueOrDefault(content.Id);
            var reservedOnLine = await ActiveOnOrderLineAsync(line.Id, cancellationToken);
            var conflict = FindReserveConflict(line, content, command, reservedOnContent, reservedOnLine);
            if (conflict != null)
            {
                return ServiceResult<StockReservationDto>.Conflict(conflict);
            }

            var now = DateTime.UtcNow;
            var reservation = new StockReservation
            {
                RequestId = command.RequestId,
                SalesOrderLineId = line.Id,
                PalletContentLineId = content.Id,
                SupplyCommitmentLineId = content.SupplyCommitmentLineId,
                Quantity = command.Quantity,
                Unit = command.Unit,
                NetWeightKg = command.NetWeightKg,
                Source = ReservationRules.SourcePhysical,
                Status = ReservationRules.StatusActive,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.StockReservations.Add(reservation);

            // Aanraken (versie +1): twee gelijktijdige reserveringen op dezelfde inhoud, orderregel of pallet kunnen nooit allebei slagen.
            _db.Entry(content).Property(c => c.Version).IsModified = true;
            _db.Entry(line).Property(l => l.Version).IsModified = true;
            _db.Entry(content.Pallet).Property(p => p.Version).IsModified = true;

            _db.PalletAuditEvents.Add(new PalletAuditEvent
            {
                PalletId = content.PalletId,
                PalletContentLine = content,
                EventType = ReservationRules.ReservationCreatedEvent,
                NewValue = JsonSerializer.Serialize(new
                {
                    salesOrderId = line.SalesOrderId,
                    salesOrderLineId = line.Id,
                    lotNumber = content.LotNumber,
                    quantity = command.Quantity,
                    unit = command.Unit,
                    netWeightKg = command.NetWeightKg
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
            catch (DbUpdateException exception)
            {
                _db.ChangeTracker.Clear();
                var concurrent = await ReplayReserveAsync(command, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                if (exception is DbUpdateConcurrencyException)
                {
                    return ServiceResult<StockReservationDto>.Conflict("The stock or order line was changed concurrently; reload and try again with a new requestId.");
                }
                throw;
            }

            return ServiceResult<StockReservationDto>.Created(await LoadDtoAsync(reservation.Id, cancellationToken));
        }

        private static ReserveCommand BuildReserve(int salesOrderId, int salesOrderLineId, CreateStockReservationDto request, Dictionary<string, List<string>> errors)
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
            if (request.PalletContentLineId == null)
            {
                ValidationErrors.Add(errors, "palletContentLineId", "One exact pallet content line is required.");
            }
            if (request.Quantity == null || !SupplyRules.IsPositiveDecimal18_3(request.Quantity.Value))
            {
                ValidationErrors.Add(errors, "quantity", "Quantity is required, must be positive and may have at most 3 decimals.");
            }
            if (!SupplyRules.IsPackageUnit(request.Unit))
            {
                ValidationErrors.Add(errors, "unit", $"Unit must be one of: {string.Join(", ", SupplyRules.PackageUnits)}.");
            }
            if (request.NetWeightKg == null || !SupplyRules.IsPositiveDecimal18_3(request.NetWeightKg.Value))
            {
                ValidationErrors.Add(errors, "netWeightKg", "Net weight in kg is required, must be positive and may have at most 3 decimals; it is never derived.");
            }
            var actor = ValidationErrors.RequiredText(request.ActorReference, ReservationRules.MaxActorLength, "actorReference", errors);
            var reason = ValidationErrors.OptionalText(request.Reason, ReservationRules.MaxReasonLength, "reason", errors);
            if (errors.Count > 0)
            {
                return null;
            }
            return new ReserveCommand(request.RequestId!.Value, salesOrderId, salesOrderLineId, request.PalletContentLineId!.Value,
                request.Quantity!.Value, request.Unit, request.NetWeightKg!.Value, actor, reason);
        }

        private static string FindReserveConflict(SalesOrderLine line, PalletContentLine content, ReserveCommand command,
            (decimal Quantity, decimal NetWeightKg) reservedOnContent, decimal reservedOnLine)
        {
            if (line.SalesOrder.Status != ReservationRules.OrderStatusOpen)
            {
                return $"Sales order {line.SalesOrderId} is {line.SalesOrder.Status}; reservations require an open order.";
            }
            if (content.SupplyCommitmentLine.SupplyCommitment.Route != SupplyRules.RouteWarehouse)
            {
                return "DirectTrade supply is customer-bound and can never be reserved as warehouse stock.";
            }
            if (content.Pallet.LogisticsStatus != PalletRules.StatusReceived || content.Pallet.LocationContext != PalletRules.LocationWarehouse)
            {
                return "Only content on a Received pallet in the Warehouse can be reserved.";
            }
            if (content.QualityStatus != PalletRules.QualityApproved)
            {
                return $"Content with quality {content.QualityStatus} can never be reserved; only Approved content can.";
            }
            if (content.SupplyCommitmentLine.ProductVariantId != line.ProductVariantId)
            {
                return "The pallet content is a different product variant than the sales order line.";
            }
            if (!string.Equals(command.Unit, content.Unit, StringComparison.Ordinal) || !string.Equals(command.Unit, line.Unit, StringComparison.Ordinal))
            {
                return $"Unit {command.Unit} must exactly match the content unit {content.Unit} and the order line unit {line.Unit}; units are never converted.";
            }

            // Voorraad = ontvangst - afgeboekt; verbruikte reserveringen zitten in afgeboekt en tellen niet dubbel.
            var remainingQuantity = ReservationRules.OnHandQuantity(content.Quantity, content.DispatchedQuantity) - reservedOnContent.Quantity;
            var remainingKg = ReservationRules.OnHandQuantity(content.NetWeightKg, content.DispatchedNetWeightKg) - reservedOnContent.NetWeightKg;
            if (command.Quantity > remainingQuantity || command.NetWeightKg > remainingKg)
            {
                return $"Only {remainingQuantity} {content.Unit} and {remainingKg} kg of this content line are still unreserved.";
            }
            // Geen gestrande rest: de volledige resterende hoeveelheid hoort bij het volledige resterende kg (en omgekeerd).
            if ((command.Quantity == remainingQuantity) != (command.NetWeightKg == remainingKg))
            {
                return $"Reserving the full remainder requires both the remaining quantity ({remainingQuantity}) and the remaining net weight ({remainingKg} kg).";
            }
            // Vastgelegd op de regel = Active gereserveerd + Consumed verzonden; verzonden komt nooit terug als open vraag.
            if (reservedOnLine + command.Quantity > line.Quantity)
            {
                return $"The sales order line still needs only {line.Quantity - reservedOnLine} {line.Unit}.";
            }
            return null;
        }

        // Active + Consumed: beide leggen klantvraag vast; alleen Released komt terug als open vraag.
        private async Task<decimal> ActiveOnOrderLineAsync(int salesOrderLineId, CancellationToken cancellationToken)
        {
            var quantities = await _db.StockReservations.AsNoTracking()
                .Where(r => r.SalesOrderLineId == salesOrderLineId && (r.Status == ReservationRules.StatusActive || r.Status == ReservationRules.StatusConsumed))
                .Select(r => r.Quantity)
                .ToListAsync(cancellationToken);
            return quantities.Sum();
        }

        private async Task<ServiceResult<StockReservationDto>> ReplayReserveAsync(ReserveCommand command, CancellationToken cancellationToken)
        {
            var existing = await _db.StockReservations.AsNoTracking().Include(r => r.SalesOrderLine)
                .SingleOrDefaultAsync(r => r.RequestId == command.RequestId, cancellationToken);
            if (existing == null)
            {
                return null;
            }
            var audit = await _db.PalletAuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(e => e.RequestId == command.RequestId && e.EventType == ReservationRules.ReservationCreatedEvent, cancellationToken);
            var same = existing.SalesOrderLine?.SalesOrderId == command.SalesOrderId
                && existing.SalesOrderLineId == command.SalesOrderLineId
                && existing.PalletContentLineId == command.PalletContentLineId
                && existing.Quantity == command.Quantity
                && string.Equals(existing.Unit, command.Unit, StringComparison.Ordinal)
                && existing.NetWeightKg == command.NetWeightKg
                && audit != null
                && string.Equals(audit.ActorReference, command.Actor, StringComparison.Ordinal)
                && string.Equals(audit.Reason, command.Reason, StringComparison.Ordinal);
            return same
                ? ServiceResult<StockReservationDto>.Ok(await LoadDtoAsync(existing.Id, cancellationToken))
                : ServiceResult<StockReservationDto>.Conflict($"RequestId {command.RequestId} was already used for a different reservation.");
        }

        // ======================== Vrijgeven ========================

        private sealed record ReleaseCommand(Guid RequestId, int ExpectedVersion, string Actor, string Reason);

        public async Task<ServiceResult<StockReservationDto>> ReleaseAsync(int reservationId, ReleaseStockReservationDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            ReleaseCommand command = null;
            if (request == null)
            {
                ValidationErrors.Add(errors, "request", "A request body is required.");
            }
            else
            {
                if (request.RequestId == null || request.RequestId == Guid.Empty)
                {
                    ValidationErrors.Add(errors, "requestId", "A client-generated, non-empty requestId is required.");
                }
                if (request.ExpectedReservationVersion == null)
                {
                    ValidationErrors.Add(errors, "expectedReservationVersion", "The reservation version the release is based on is required.");
                }
                var actor = ValidationErrors.RequiredText(request.ActorReference, ReservationRules.MaxActorLength, "actorReference", errors);
                var reason = ValidationErrors.RequiredText(request.Reason, ReservationRules.MaxReasonLength, "reason", errors);
                if (errors.Count == 0)
                {
                    command = new ReleaseCommand(request.RequestId!.Value, request.ExpectedReservationVersion!.Value, actor, reason);
                }
            }
            if (errors.Count > 0)
            {
                return ServiceResult<StockReservationDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var replay = await ReplayReleaseAsync(reservationId, command, cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var reservation = await _db.StockReservations.Include(r => r.PalletContentLine).SingleOrDefaultAsync(r => r.Id == reservationId, cancellationToken);
            if (reservation == null)
            {
                return ServiceResult<StockReservationDto>.NotFound($"Reservation {reservationId} does not exist.");
            }
            if (reservation.SalesOrderLineId == null || reservation.PalletContentLine == null)
            {
                return ServiceResult<StockReservationDto>.Conflict($"Reservation {reservationId} is not part of the sales reservation flow.");
            }
            if (reservation.Version != command.ExpectedVersion)
            {
                return ServiceResult<StockReservationDto>.Conflict($"Reservation {reservationId} is at version {reservation.Version}, not {command.ExpectedVersion}; reload it and decide again.");
            }
            if (reservation.Status != ReservationRules.StatusActive)
            {
                return ServiceResult<StockReservationDto>.Conflict($"Reservation {reservationId} is {reservation.Status} and can no longer be released.");
            }

            var now = DateTime.UtcNow;
            reservation.Status = ReservationRules.StatusReleased;
            reservation.ReleasedAt = now;
            reservation.ReleaseReason = command.Reason;
            reservation.ReleaseRequestId = command.RequestId;
            reservation.UpdatedAt = now;
            _db.Entry(reservation.PalletContentLine).Property(c => c.Version).IsModified = true;

            _db.PalletAuditEvents.Add(new PalletAuditEvent
            {
                PalletId = reservation.PalletContentLine.PalletId,
                PalletContentLineId = reservation.PalletContentLine.Id,
                EventType = ReservationRules.ReservationReleasedEvent,
                OldValue = JsonSerializer.Serialize(new { reservationId = reservation.Id, status = ReservationRules.StatusActive, version = command.ExpectedVersion }, SnapshotJson),
                NewValue = JsonSerializer.Serialize(new
                {
                    reservationId = reservation.Id,
                    status = ReservationRules.StatusReleased,
                    quantity = reservation.Quantity,
                    unit = reservation.Unit,
                    netWeightKg = reservation.NetWeightKg
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
            catch (DbUpdateException exception)
            {
                _db.ChangeTracker.Clear();
                var concurrent = await ReplayReleaseAsync(reservationId, command, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                if (exception is DbUpdateConcurrencyException)
                {
                    return ServiceResult<StockReservationDto>.Conflict($"Reservation {reservationId} was changed concurrently; reload it and decide again.");
                }
                throw;
            }

            return ServiceResult<StockReservationDto>.Ok(await LoadDtoAsync(reservation.Id, cancellationToken));
        }

        // Replay: dezelfde vrijgave-requestId met dezelfde actor, reden en verwachte versie geeft de huidige toestand terug.
        private async Task<ServiceResult<StockReservationDto>> ReplayReleaseAsync(int reservationId, ReleaseCommand command, CancellationToken cancellationToken)
        {
            var existing = await _db.StockReservations.AsNoTracking().SingleOrDefaultAsync(r => r.ReleaseRequestId == command.RequestId, cancellationToken);
            if (existing == null)
            {
                return null;
            }
            var audit = await _db.PalletAuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(e => e.RequestId == command.RequestId && e.EventType == ReservationRules.ReservationReleasedEvent, cancellationToken);
            var same = existing.Id == reservationId
                && audit != null
                && string.Equals(audit.ActorReference, command.Actor, StringComparison.Ordinal)
                && string.Equals(audit.Reason, command.Reason, StringComparison.Ordinal)
                && RecordedVersion(audit.OldValue) == command.ExpectedVersion;
            return same
                ? ServiceResult<StockReservationDto>.Ok(await LoadDtoAsync(existing.Id, cancellationToken))
                : ServiceResult<StockReservationDto>.Conflict($"RequestId {command.RequestId} was already used for a different release.");
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

        // ======================== Mapping ========================

        private async Task<StockReservationDto> LoadDtoAsync(int reservationId, CancellationToken cancellationToken)
        {
            var reservation = await _db.StockReservations.AsNoTracking()
                .Include(r => r.SalesOrderLine)
                .Include(r => r.PalletContentLine).ThenInclude(c => c.Pallet)
                .Include(r => r.DispatchLine).ThenInclude(l => l.SalesDispatch)
                .SingleAsync(r => r.Id == reservationId, cancellationToken);
            return ToDto(reservation, reservation.SalesOrderLine?.SalesOrderId ?? 0);
        }

        internal static StockReservationDto ToDto(StockReservation reservation, int salesOrderId) => new()
        {
            Id = reservation.Id,
            RequestId = reservation.RequestId,
            SalesOrderId = salesOrderId,
            SalesOrderLineId = reservation.SalesOrderLineId ?? 0,
            PalletContentLineId = reservation.PalletContentLineId ?? 0,
            PalletId = reservation.PalletContentLine?.PalletId ?? 0,
            PalletReference = reservation.PalletContentLine?.Pallet?.Reference,
            WarehouseLocation = reservation.PalletContentLine?.Pallet?.WarehouseLocation,
            LotNumber = reservation.PalletContentLine?.LotNumber,
            Quantity = reservation.Quantity,
            Unit = reservation.Unit,
            NetWeightKg = reservation.NetWeightKg ?? 0m,
            Source = reservation.Source,
            Status = reservation.Status,
            CreatedAt = DateTime.SpecifyKind(reservation.CreatedAt, DateTimeKind.Utc),
            ReleasedAt = reservation.ReleasedAt == null ? null : DateTime.SpecifyKind(reservation.ReleasedAt.Value, DateTimeKind.Utc),
            ReleaseReason = reservation.ReleaseReason,
            Version = reservation.Version,
            ContentQualityStatus = reservation.PalletContentLine?.QualityStatus,
            OnHold = reservation.Status == ReservationRules.StatusActive
                && reservation.PalletContentLine != null
                && reservation.PalletContentLine.QualityStatus != PalletRules.QualityApproved,
            ConsumedAt = reservation.ConsumedAt == null ? null : DateTime.SpecifyKind(reservation.ConsumedAt.Value, DateTimeKind.Utc),
            DispatchId = reservation.DispatchLine?.SalesDispatchId,
            DispatchReference = reservation.DispatchLine?.SalesDispatch?.Reference
        };
    }
}
