using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public interface ISalesDispatchService
    {
        Task<ServiceResult<SalesDispatchDto>> DispatchAsync(int salesOrderId, CreateSalesDispatchDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<IReadOnlyList<SalesDispatchDto>>> ListAsync(int salesOrderId, CancellationToken cancellationToken = default);
    }

    // Verzenden (afboeken) van een Open verkooporder: gekozen Active Physical reserveringen worden in hun geheel verbruikt
    // (Consumed), elk met een Dispatch-voorraadmutatie en een verhoging van afgeboekt op de inhoudsregel, in één transactie
    // met de verzending, de audit en de versies. Hoeveelheid, eenheid en kg komen uit de reservering; niets wordt afgeleid.
    // De fysieke pallet verandert niet van status: alles verbruiken bewijst niet dat de pallet is vertrokken.
    public sealed class SalesDispatchService : ISalesDispatchService
    {
        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly AppDbContext _db;

        public SalesDispatchService(AppDbContext db)
        {
            _db = db;
        }

        private sealed record DispatchCommand(Guid RequestId, int SalesOrderId, int ExpectedOrderVersion, string Actor, string Reference, IReadOnlyList<(int ReservationId, int ExpectedVersion)> Lines);

        public async Task<ServiceResult<SalesDispatchDto>> DispatchAsync(int salesOrderId, CreateSalesDispatchDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var command = Build(salesOrderId, request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<SalesDispatchDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var replay = await ReplayAsync(command, cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var order = await _db.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.PalletContentLine).ThenInclude(c => c.Pallet)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.PalletContentLine).ThenInclude(c => c.SupplyCommitmentLine).ThenInclude(s => s.SupplyCommitment)
                .SingleOrDefaultAsync(o => o.Id == salesOrderId, cancellationToken);
            if (order == null)
            {
                return ServiceResult<SalesDispatchDto>.NotFound($"Sales order {salesOrderId} does not exist.");
            }
            if (order.Status != ReservationRules.OrderStatusOpen)
            {
                return ServiceResult<SalesDispatchDto>.Conflict($"Sales order {salesOrderId} is {order.Status}; dispatch requires an open order.");
            }
            if (order.Version != command.ExpectedOrderVersion)
            {
                return ServiceResult<SalesDispatchDto>.Conflict($"Sales order {salesOrderId} is at version {order.Version}, not {command.ExpectedOrderVersion}; reload it and decide again.");
            }

            var byId = order.Lines.SelectMany(l => l.Reservations.Select(r => (Line: l, Reservation: r))).ToDictionary(x => x.Reservation.Id);
            var selected = new List<(SalesOrderLine Line, StockReservation Reservation)>();
            foreach (var (reservationId, expectedVersion) in command.Lines)
            {
                if (!byId.TryGetValue(reservationId, out var found))
                {
                    return ServiceResult<SalesDispatchDto>.Conflict($"Reservation {reservationId} does not belong to sales order {salesOrderId}.");
                }
                var conflict = FindReservationConflict(found.Reservation, expectedVersion);
                if (conflict != null)
                {
                    return ServiceResult<SalesDispatchDto>.Conflict(conflict);
                }
                selected.Add(found);
            }

            // Saldi per inhoudsregel na alle gekozen reserveringen: nooit boven de voorraad, en hoeveelheid en kg bereiken samen nul.
            var balanceConflict = FindBalanceConflict(selected.Select(s => s.Reservation).ToList());
            if (balanceConflict != null)
            {
                return ServiceResult<SalesDispatchDto>.Conflict(balanceConflict);
            }

            var now = DateTime.UtcNow;
            var dispatch = new SalesDispatch
            {
                RequestId = command.RequestId,
                SalesOrder = order,
                Reference = command.Reference,
                ActorReference = command.Actor,
                DispatchedAt = now
            };
            _db.SalesDispatches.Add(dispatch);

            // De order en elke geraakte regel aanraken: botst met gelijktijdig annuleren en met reserveren op die regel.
            order.UpdatedAt = now;
            _db.Entry(order).Property(o => o.Version).IsModified = true;
            foreach (var line in selected.Select(s => s.Line).Distinct())
            {
                _db.Entry(line).Property(l => l.Version).IsModified = true;
            }

            foreach (var ((line, reservation), expectedVersion) in selected.Zip(command.Lines.Select(l => l.ExpectedVersion)))
            {
                var content = reservation.PalletContentLine;
                reservation.Status = ReservationRules.StatusConsumed;
                reservation.ConsumedAt = now;
                reservation.UpdatedAt = now;

                content.DispatchedQuantity += reservation.Quantity;
                content.DispatchedNetWeightKg += reservation.NetWeightKg ?? 0m;
                _db.Entry(content.Pallet).Property(p => p.Version).IsModified = true;

                var movement = new StockMovement
                {
                    Type = ReservationRules.MovementTypeDispatch,
                    Quantity = reservation.Quantity,
                    Unit = reservation.Unit,
                    NetWeightKg = reservation.NetWeightKg,
                    SupplyCommitmentLineId = content.SupplyCommitmentLineId,
                    PalletContentLine = content,
                    StockReservation = reservation,
                    Reason = command.Reference,
                    CreatedAt = now
                };
                _db.StockMovements.Add(movement);
                dispatch.Lines.Add(new SalesDispatchLine
                {
                    StockReservation = reservation,
                    SalesOrderLine = line,
                    PalletContentLine = content,
                    Quantity = reservation.Quantity,
                    Unit = reservation.Unit,
                    NetWeightKg = reservation.NetWeightKg ?? 0m,
                    ExpectedReservationVersion = expectedVersion,
                    StockMovement = movement
                });

                _db.PalletAuditEvents.Add(new PalletAuditEvent
                {
                    PalletId = content.PalletId,
                    PalletContentLine = content,
                    EventType = ReservationRules.ReservationConsumedEvent,
                    OldValue = JsonSerializer.Serialize(new { reservationId = reservation.Id, status = ReservationRules.StatusActive, version = expectedVersion }, SnapshotJson),
                    NewValue = JsonSerializer.Serialize(new
                    {
                        reservationId = reservation.Id,
                        status = ReservationRules.StatusConsumed,
                        quantity = reservation.Quantity,
                        unit = reservation.Unit,
                        netWeightKg = reservation.NetWeightKg,
                        salesOrderId = order.Id,
                        dispatchRequestId = command.RequestId,
                        reference = command.Reference
                    }, SnapshotJson),
                    Reason = command.Reference,
                    ActorReference = command.Actor,
                    OccurredAt = now,
                    // De unieke RequestId hoort bij het orderevent; de palletevents verwijzen via dispatchRequestId.
                    RequestId = null
                });
            }

            // Begrensde samenvatting: aantallen en totalen, geen lijst van id's.
            var reservations = selected.Select(s => s.Reservation).ToList();
            _db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
            {
                SalesOrder = order,
                EventType = ReservationRules.OrderDispatchedEvent,
                OldValue = JsonSerializer.Serialize(new { status = order.Status, version = command.ExpectedOrderVersion }, SnapshotJson),
                NewValue = JsonSerializer.Serialize(new
                {
                    status = order.Status,
                    reference = command.Reference,
                    consumedReservationCount = reservations.Count,
                    dispatchedQuantityByUnit = reservations.GroupBy(r => r.Unit).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key ?? "", g => g.Sum(r => r.Quantity)),
                    dispatchedNetWeightKg = reservations.Sum(r => r.NetWeightKg ?? 0m),
                    lineCount = selected.Select(s => s.Line.Id).Distinct().Count()
                }, SnapshotJson),
                Reason = command.Reference,
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
                // Alleen een herkende botsing wordt 409: een andere schrijver (annuleren, vrijgeven, blokkeren, een andere verzending,
                // reserveren op dezelfde inhoud) was eerder, of dezelfde requestId is net door een gelijktijdige retry vastgelegd.
                _db.ChangeTracker.Clear();
                var concurrent = await ReplayAsync(command, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                return ServiceResult<SalesDispatchDto>.Conflict($"Sales order {salesOrderId} or its reservations were changed concurrently; reload and decide again.");
            }

            return ServiceResult<SalesDispatchDto>.Created(await LoadDtoAsync(dispatch.Id, cancellationToken));
        }

        private static DispatchCommand Build(int salesOrderId, CreateSalesDispatchDto request, Dictionary<string, List<string>> errors)
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
                ValidationErrors.Add(errors, "expectedOrderVersion", "The order version the dispatch is based on is required.");
            }
            var actor = ValidationErrors.RequiredText(request.ActorReference, ReservationRules.MaxActorLength, "actorReference", errors);
            var reference = ValidationErrors.OptionalText(request.Reference, ReservationRules.MaxDispatchReferenceLength, "reference", errors);

            var lines = new List<(int, int)>();
            if (request.Lines == null || request.Lines.Count == 0)
            {
                ValidationErrors.Add(errors, "lines", "Select at least one reservation to dispatch.");
            }
            else if (request.Lines.Count > ReservationRules.MaxDispatchLines)
            {
                ValidationErrors.Add(errors, "lines", $"At most {ReservationRules.MaxDispatchLines} reservations can be dispatched at once.");
            }
            else
            {
                var seen = new HashSet<int>();
                for (var i = 0; i < request.Lines.Count; i++)
                {
                    var key = $"lines[{i}]";
                    var line = request.Lines[i];
                    if (line == null)
                    {
                        ValidationErrors.Add(errors, key, "A line is required.");
                        continue;
                    }
                    var before = errors.Count;
                    if (line.StockReservationId == null || line.StockReservationId <= 0)
                    {
                        ValidationErrors.Add(errors, $"{key}.stockReservationId", "A reservation is required.");
                    }
                    if (line.ExpectedReservationVersion == null || line.ExpectedReservationVersion < 0)
                    {
                        ValidationErrors.Add(errors, $"{key}.expectedReservationVersion", "The reservation version the dispatch is based on is required.");
                    }
                    if (errors.Count > before)
                    {
                        continue;
                    }
                    if (!seen.Add(line.StockReservationId!.Value))
                    {
                        ValidationErrors.Add(errors, key, "The same reservation occurs more than once.");
                        continue;
                    }
                    lines.Add((line.StockReservationId.Value, line.ExpectedReservationVersion!.Value));
                }
            }
            if (errors.Count > 0)
            {
                return null;
            }
            return new DispatchCommand(request.RequestId!.Value, salesOrderId, request.ExpectedOrderVersion!.Value, actor, reference, lines);
        }

        // Alleen Active Physical verkoopreserveringen op Approved inhoud van een Received pallet in het magazijn, met de verwachte versie.
        private static string FindReservationConflict(StockReservation reservation, int expectedVersion)
        {
            if (reservation.Status == ReservationRules.StatusConsumed)
            {
                return $"Reservation {reservation.Id} is already consumed by a dispatch.";
            }
            if (reservation.Source == ReservationRules.SourceDirectTrade)
            {
                return $"Reservation {reservation.Id} is a DirectTrade allocation of expected, customer-bound supply and can never be dispatched as warehouse stock; DirectTrade shipment is a separate flow.";
            }
            if (reservation.Status != ReservationRules.StatusActive || reservation.Source != ReservationRules.SourcePhysical || reservation.PalletContentLine == null)
            {
                return $"Reservation {reservation.Id} is {reservation.Status} and cannot be dispatched; only active physical reservations can.";
            }
            if (reservation.Version != expectedVersion)
            {
                return $"Reservation {reservation.Id} is at version {reservation.Version}, not {expectedVersion}; reload it and decide again.";
            }
            var content = reservation.PalletContentLine;
            if (content.QualityStatus != PalletRules.QualityApproved)
            {
                return $"Reservation {reservation.Id} is on hold: its content is {content.QualityStatus}, not {PalletRules.QualityApproved}.";
            }
            if (content.Pallet.LogisticsStatus != PalletRules.StatusReceived || content.Pallet.LocationContext != PalletRules.LocationWarehouse)
            {
                return $"Reservation {reservation.Id} is on content outside the warehouse ({content.Pallet.LogisticsStatus}, {content.Pallet.LocationContext}).";
            }
            if (content.SupplyCommitmentLine?.SupplyCommitment?.Route != SupplyRules.RouteWarehouse)
            {
                return $"Reservation {reservation.Id} is on DirectTrade content and can never be dispatched as warehouse stock.";
            }
            if (!string.Equals(reservation.Unit, content.Unit, StringComparison.Ordinal))
            {
                return $"Reservation {reservation.Id} has unit {reservation.Unit} while the content is in {content.Unit}; units are never converted.";
            }
            return null;
        }

        // Nooit meer verbruiken dan er op voorraad is, en voorraad en kg bereiken samen nul (anders blijft een onbruikbare rest achter).
        private static string FindBalanceConflict(List<StockReservation> reservations)
        {
            foreach (var group in reservations.GroupBy(r => r.PalletContentLine))
            {
                var content = group.Key;
                var quantity = group.Sum(r => r.Quantity);
                var kg = group.Sum(r => r.NetWeightKg ?? 0m);
                var onHandQuantity = ReservationRules.OnHandQuantity(content.Quantity, content.DispatchedQuantity);
                var onHandKg = ReservationRules.OnHandQuantity(content.NetWeightKg, content.DispatchedNetWeightKg);
                if (quantity > onHandQuantity || kg > onHandKg)
                {
                    return $"Pallet content line {content.Id} has only {onHandQuantity} {content.Unit} and {onHandKg} kg on hand; the selected reservations need {quantity} and {kg} kg.";
                }
                var remainingQuantity = onHandQuantity - quantity;
                var remainingKg = onHandKg - kg;
                if ((remainingQuantity == 0m) != (remainingKg == 0m))
                {
                    return $"Dispatching these reservations would leave pallet content line {content.Id} with {remainingQuantity} {content.Unit} but {remainingKg} kg; quantity and net weight must reach zero together.";
                }
            }
            return null;
        }

        // Replay: dezelfde requestId voor dezelfde order, actor, referentie en exact dezelfde reserveringen met dezelfde verwachte versies.
        private async Task<ServiceResult<SalesDispatchDto>> ReplayAsync(DispatchCommand command, CancellationToken cancellationToken)
        {
            var existing = await DispatchQuery().SingleOrDefaultAsync(d => d.RequestId == command.RequestId, cancellationToken);
            if (existing == null)
            {
                return null;
            }
            var audit = await _db.SalesOrderAuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(e => e.RequestId == command.RequestId && e.EventType == ReservationRules.OrderDispatchedEvent, cancellationToken);
            var existingLines = existing.Lines.OrderBy(l => l.StockReservationId).Select(l => (l.StockReservationId, l.ExpectedReservationVersion)).ToList();
            var commandLines = command.Lines.OrderBy(l => l.ReservationId).Select(l => (l.ReservationId, l.ExpectedVersion)).ToList();
            var same = existing.SalesOrderId == command.SalesOrderId
                && string.Equals(existing.ActorReference, command.Actor, StringComparison.Ordinal)
                && string.Equals(existing.Reference, command.Reference, StringComparison.Ordinal)
                && audit != null
                && RecordedVersion(audit.OldValue) == command.ExpectedOrderVersion
                && existingLines.SequenceEqual(commandLines);
            if (!same)
            {
                return ServiceResult<SalesDispatchDto>.Conflict($"RequestId {command.RequestId} was already used for a different dispatch.");
            }
            var corrections = await CorrectionsAsync(new List<int> { existing.Id }, cancellationToken);
            return ServiceResult<SalesDispatchDto>.Ok(ToDto(existing, corrections.GetValueOrDefault(existing.Id)));
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

        public async Task<ServiceResult<IReadOnlyList<SalesDispatchDto>>> ListAsync(int salesOrderId, CancellationToken cancellationToken = default)
        {
            if (!await _db.SalesOrders.AnyAsync(o => o.Id == salesOrderId, cancellationToken))
            {
                return ServiceResult<IReadOnlyList<SalesDispatchDto>>.NotFound($"Sales order {salesOrderId} does not exist.");
            }
            var dispatches = await DispatchQuery().Where(d => d.SalesOrderId == salesOrderId).OrderBy(d => d.DispatchedAt).ThenBy(d => d.Id).ToListAsync(cancellationToken);
            var corrections = await CorrectionsAsync(dispatches.Select(d => d.Id).ToList(), cancellationToken);
            return ServiceResult<IReadOnlyList<SalesDispatchDto>>.Ok(dispatches.Select(d => ToDto(d, corrections.GetValueOrDefault(d.Id))).ToList());
        }

        private async Task<SalesDispatchDto> LoadDtoAsync(int dispatchId, CancellationToken cancellationToken)
        {
            _db.ChangeTracker.Clear();
            var corrections = await CorrectionsAsync(new List<int> { dispatchId }, cancellationToken);
            return ToDto(await DispatchQuery().SingleAsync(d => d.Id == dispatchId, cancellationToken), corrections.GetValueOrDefault(dispatchId));
        }

        private IQueryable<SalesDispatch> DispatchQuery() =>
            _db.SalesDispatches.AsNoTracking()
                .Include(d => d.Lines).ThenInclude(l => l.PalletContentLine).ThenInclude(c => c.Pallet)
                .Include(d => d.Lines).ThenInclude(l => l.SalesOrderLine).ThenInclude(s => s.ProductVariant).ThenInclude(v => v.Product)
                .AsSplitQuery();

        private async Task<Dictionary<int, SalesDispatchCorrection>> CorrectionsAsync(List<int> dispatchIds, CancellationToken cancellationToken)
        {
            if (dispatchIds.Count == 0) return new Dictionary<int, SalesDispatchCorrection>();
            var corrections = await _db.SalesDispatchCorrections.AsNoTracking()
                .Include(c => c.Lines).ThenInclude(l => l.RestoredContentLine).ThenInclude(r => r.Pallet)
                .Include(c => c.Lines).ThenInclude(l => l.StockMovement)
                .Where(c => dispatchIds.Contains(c.SalesDispatchId))
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            return corrections.ToDictionary(c => c.SalesDispatchId);
        }

        internal static SalesDispatchDto ToDto(SalesDispatch dispatch, SalesDispatchCorrection correction = null) => new()
        {
            Id = dispatch.Id,
            RequestId = dispatch.RequestId,
            SalesOrderId = dispatch.SalesOrderId,
            Reference = dispatch.Reference,
            ActorReference = dispatch.ActorReference,
            DispatchedAt = DateTime.SpecifyKind(dispatch.DispatchedAt, DateTimeKind.Utc),
            Correction = correction == null ? null : SalesDispatchCorrectionService.ToDto(correction),
            Lines = dispatch.Lines.OrderBy(l => l.Id).Select(l => new SalesDispatchLineDto
            {
                Id = l.Id,
                StockReservationId = l.StockReservationId,
                SalesOrderLineId = l.SalesOrderLineId,
                PalletContentLineId = l.PalletContentLineId,
                PalletId = l.PalletContentLine?.PalletId ?? 0,
                PalletReference = l.PalletContentLine?.Pallet?.Reference,
                LotNumber = l.PalletContentLine?.LotNumber,
                ProductName = l.SalesOrderLine?.ProductVariant?.Product?.Name,
                ProductVariantName = l.SalesOrderLine?.ProductVariant?.Name,
                Quantity = l.Quantity,
                Unit = l.Unit,
                NetWeightKg = l.NetWeightKg,
                StockMovementId = l.StockMovementId
            }).ToList()
        };
    }
}
