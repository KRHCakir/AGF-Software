using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public interface ISalesDispatchCorrectionService
    {
        Task<ServiceResult<SalesDispatchCorrectionDto>> CorrectAsync(int salesOrderId, int dispatchId, CorrectSalesDispatchDto request, CancellationToken cancellationToken = default);
    }

    // Administratieve correctie van een hele verzending. De oorspronkelijke verzending, regels en Dispatch-mutaties blijven
    // onaangeroerd. Per verzendregel: een nieuwe inhoudsregel in quarantaine op dezelfde pallet (zelfde partij en bronregel,
    // exact de verzonden hoeveelheid en kg), één compenserende Correction-mutatie en de reservering Consumed -> Corrected.
    // Er wordt geen reservering hersteld en de pallet verandert niet van status of locatie. De gebruiker bevestigt expliciet
    // dat de goederen het magazijn niet hebben verlaten; de software stelt dat niet zelf vast.
    public sealed class SalesDispatchCorrectionService : ISalesDispatchCorrectionService
    {
        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly AppDbContext _db;

        public SalesDispatchCorrectionService(AppDbContext db)
        {
            _db = db;
        }

        private sealed record CorrectCommand(Guid RequestId, int SalesOrderId, int DispatchId, int ExpectedOrderVersion, string Actor, string Reason, IReadOnlyList<(int ReservationId, int ExpectedVersion)> Lines);

        public async Task<ServiceResult<SalesDispatchCorrectionDto>> CorrectAsync(int salesOrderId, int dispatchId, CorrectSalesDispatchDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var command = Build(salesOrderId, dispatchId, request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<SalesDispatchCorrectionDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var replay = await ReplayAsync(command, cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var dispatch = await _db.SalesDispatches
                .Include(d => d.Lines).ThenInclude(l => l.StockReservation).ThenInclude(r => r.SalesOrderLine)
                .Include(d => d.Lines).ThenInclude(l => l.PalletContentLine).ThenInclude(c => c.Pallet).ThenInclude(p => p.ContentLines)
                .SingleOrDefaultAsync(d => d.Id == dispatchId, cancellationToken);
            if (dispatch == null || dispatch.SalesOrderId != salesOrderId)
            {
                return ServiceResult<SalesDispatchCorrectionDto>.NotFound($"Dispatch {dispatchId} does not exist on sales order {salesOrderId}.");
            }
            var order = await _db.SalesOrders.Include(o => o.Lines).SingleAsync(o => o.Id == salesOrderId, cancellationToken);
            if (order.Status != ReservationRules.OrderStatusOpen)
            {
                return ServiceResult<SalesDispatchCorrectionDto>.Conflict($"Sales order {salesOrderId} is {order.Status}; a dispatch correction requires an open order.");
            }
            if (order.Version != command.ExpectedOrderVersion)
            {
                return ServiceResult<SalesDispatchCorrectionDto>.Conflict($"Sales order {salesOrderId} is at version {order.Version}, not {command.ExpectedOrderVersion}; reload it and decide again.");
            }
            if (await _db.SalesDispatchCorrections.AnyAsync(c => c.SalesDispatchId == dispatchId, cancellationToken))
            {
                return ServiceResult<SalesDispatchCorrectionDto>.Conflict($"Dispatch {dispatchId} has already been corrected.");
            }

            // Hele verzending: exact de verzendregels, elk met de verwachte reserveringsversie.
            var byReservation = dispatch.Lines.ToDictionary(l => l.StockReservationId);
            if (command.Lines.Count != dispatch.Lines.Count || command.Lines.Any(l => !byReservation.ContainsKey(l.ReservationId)))
            {
                return ServiceResult<SalesDispatchCorrectionDto>.Conflict($"A correction covers the whole dispatch: all {dispatch.Lines.Count} reservations of dispatch {dispatchId} must be listed, and no others.");
            }
            foreach (var (reservationId, expectedVersion) in command.Lines)
            {
                var line = byReservation[reservationId];
                var conflict = FindLineConflict(line, expectedVersion);
                if (conflict != null)
                {
                    return ServiceResult<SalesDispatchCorrectionDto>.Conflict(conflict);
                }
            }
            // Fysieke gewichtsgrens per pallet op basis van voorraad (ontvangst minus afgeboekt), nooit historische ontvangsten.
            foreach (var pallet in dispatch.Lines.Select(l => l.PalletContentLine.Pallet).Distinct())
            {
                var restoredKg = dispatch.Lines.Where(l => l.PalletContentLine.PalletId == pallet.Id).Sum(l => l.NetWeightKg);
                var weightConflict = PalletInvariants.CheckGrossWeight(pallet.GrossKg, pallet.TareKg, pallet.ContentLines, restoredKg);
                if (weightConflict != null)
                {
                    return ServiceResult<SalesDispatchCorrectionDto>.Conflict($"Pallet {pallet.Reference}: {weightConflict}");
                }
            }

            var now = DateTime.UtcNow;
            var correction = new SalesDispatchCorrection
            {
                RequestId = command.RequestId,
                SalesDispatch = dispatch,
                SalesOrder = order,
                Reason = command.Reason,
                ActorReference = command.Actor,
                GoodsNeverLeftWarehouseConfirmed = true,
                CorrectedAt = now
            };
            _db.SalesDispatchCorrections.Add(correction);

            order.UpdatedAt = now;
            _db.Entry(order).Property(o => o.Version).IsModified = true;
            foreach (var orderLine in order.Lines.Where(l => dispatch.Lines.Any(d => d.SalesOrderLineId == l.Id)))
            {
                _db.Entry(orderLine).Property(l => l.Version).IsModified = true;
            }

            foreach (var ((reservationId, expectedVersion), _) in command.Lines.Select(l => (l, 0)))
            {
                var dispatchLine = byReservation[reservationId];
                var reservation = dispatchLine.StockReservation;
                var original = dispatchLine.PalletContentLine;

                reservation.Status = ReservationRules.StatusCorrected;
                reservation.CorrectedAt = now;
                reservation.UpdatedAt = now;
                _db.Entry(original.Pallet).Property(p => p.Version).IsModified = true;

                var correctionLine = new SalesDispatchCorrectionLine
                {
                    SalesDispatchLine = dispatchLine,
                    StockReservation = reservation,
                    Quantity = dispatchLine.Quantity,
                    Unit = dispatchLine.Unit,
                    NetWeightKg = dispatchLine.NetWeightKg,
                    ExpectedReservationVersion = expectedVersion
                };
                correction.Lines.Add(correctionLine);

                // Herstelde inhoud: nieuwe regel in quarantaine, zelfde pallet, partij en bronregel; gezaghebbende FK naar de correctieregel.
                var restored = new PalletContentLine
                {
                    RequestId = Guid.NewGuid(),
                    PalletId = original.PalletId,
                    Pallet = original.Pallet,
                    SupplyCommitmentLineId = original.SupplyCommitmentLineId,
                    LotNumber = original.LotNumber,
                    Quantity = dispatchLine.Quantity,
                    Unit = dispatchLine.Unit,
                    NetWeightKg = dispatchLine.NetWeightKg,
                    QualityStatus = PalletRules.QualityQuarantine,
                    QualityReason = $"Restored by dispatch correction of dispatch {dispatch.Id}",
                    OriginCorrectionLine = correctionLine,
                    CreatedAt = now
                };
                _db.PalletContentLines.Add(restored);

                _db.StockMovements.Add(new StockMovement
                {
                    Type = ReservationRules.MovementTypeCorrection,
                    Quantity = dispatchLine.Quantity,
                    Unit = dispatchLine.Unit,
                    NetWeightKg = dispatchLine.NetWeightKg,
                    SupplyCommitmentLineId = original.SupplyCommitmentLineId,
                    PalletContentLine = restored,
                    StockReservation = reservation,
                    SalesDispatchCorrectionLine = correctionLine,
                    Reason = command.Reason,
                    CreatedAt = now
                });

                _db.PalletAuditEvents.Add(new PalletAuditEvent
                {
                    PalletId = original.PalletId,
                    PalletContentLine = restored,
                    EventType = PalletService.ContentLineAddedEvent,
                    NewValue = JsonSerializer.Serialize(new
                    {
                        origin = "DispatchCorrection",
                        lotNumber = original.LotNumber,
                        quantity = dispatchLine.Quantity,
                        unit = dispatchLine.Unit,
                        netWeightKg = dispatchLine.NetWeightKg,
                        qualityStatus = PalletRules.QualityQuarantine,
                        fromPalletContentLineId = original.Id,
                        dispatchId = dispatch.Id,
                        correctionRequestId = command.RequestId
                    }, SnapshotJson),
                    Reason = command.Reason,
                    ActorReference = command.Actor,
                    OccurredAt = now,
                    RequestId = null
                });
                _db.PalletAuditEvents.Add(new PalletAuditEvent
                {
                    PalletId = original.PalletId,
                    PalletContentLineId = original.Id,
                    EventType = ReservationRules.ReservationCorrectedEvent,
                    OldValue = JsonSerializer.Serialize(new { reservationId = reservation.Id, status = ReservationRules.StatusConsumed, version = expectedVersion }, SnapshotJson),
                    NewValue = JsonSerializer.Serialize(new
                    {
                        reservationId = reservation.Id,
                        status = ReservationRules.StatusCorrected,
                        quantity = dispatchLine.Quantity,
                        unit = dispatchLine.Unit,
                        netWeightKg = dispatchLine.NetWeightKg,
                        salesOrderId = order.Id,
                        dispatchId = dispatch.Id,
                        correctionRequestId = command.RequestId,
                        goodsNeverLeftWarehouseConfirmedByUser = true
                    }, SnapshotJson),
                    Reason = command.Reason,
                    ActorReference = command.Actor,
                    OccurredAt = now,
                    RequestId = null
                });
            }

            _db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
            {
                SalesOrder = order,
                EventType = ReservationRules.OrderDispatchCorrectedEvent,
                OldValue = JsonSerializer.Serialize(new { status = order.Status, version = command.ExpectedOrderVersion, dispatchId = dispatch.Id }, SnapshotJson),
                NewValue = JsonSerializer.Serialize(new
                {
                    status = order.Status,
                    dispatchId = dispatch.Id,
                    correctedReservationCount = dispatch.Lines.Count,
                    restoredQuantityByUnit = dispatch.Lines.GroupBy(l => l.Unit).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key ?? "", g => g.Sum(l => l.Quantity)),
                    restoredNetWeightKg = dispatch.Lines.Sum(l => l.NetWeightKg),
                    restoredQualityStatus = PalletRules.QualityQuarantine,
                    goodsNeverLeftWarehouseConfirmedByUser = true
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
            catch (DbUpdateException exception) when (DbErrors.IsConcurrencyConflict(exception) || DbErrors.IsUniqueViolation(exception))
            {
                _db.ChangeTracker.Clear();
                var concurrent = await ReplayAsync(command, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                return ServiceResult<SalesDispatchCorrectionDto>.Conflict($"Sales order {salesOrderId}, dispatch {dispatchId} or its reservations were changed concurrently; reload and decide again.");
            }

            _db.ChangeTracker.Clear();
            return ServiceResult<SalesDispatchCorrectionDto>.Created(ToDto(await CorrectionQuery().SingleAsync(c => c.Id == correction.Id, cancellationToken)));
        }

        private static CorrectCommand Build(int salesOrderId, int dispatchId, CorrectSalesDispatchDto request, Dictionary<string, List<string>> errors)
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
            if (request.GoodsNeverLeftWarehouse != true)
            {
                ValidationErrors.Add(errors, "goodsNeverLeftWarehouse", "Confirm explicitly that the goods never left the warehouse; a physical return needs the receipt flow.");
            }
            var lines = new List<(int, int)>();
            if (request.Lines == null || request.Lines.Count == 0)
            {
                ValidationErrors.Add(errors, "lines", "List every reservation of the dispatch with its expected version.");
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
                        ValidationErrors.Add(errors, $"{key}.stockReservationId", "A reservation is required.");
                        continue;
                    }
                    if (line.ExpectedReservationVersion == null || line.ExpectedReservationVersion < 0)
                    {
                        ValidationErrors.Add(errors, $"{key}.expectedReservationVersion", "The reservation version the correction is based on is required.");
                        continue;
                    }
                    if (!seen.Add(line.StockReservationId.Value))
                    {
                        ValidationErrors.Add(errors, key, "The same reservation occurs more than once.");
                        continue;
                    }
                    lines.Add((line.StockReservationId.Value, line.ExpectedReservationVersion.Value));
                }
            }
            if (errors.Count > 0)
            {
                return null;
            }
            return new CorrectCommand(request.RequestId!.Value, salesOrderId, dispatchId, request.ExpectedOrderVersion!.Value, actor, reason, lines);
        }

        // Geschiktheid: de reservering is nog Consumed met de verwachte versie, en de pallet van de oorspronkelijke inhoud staat nog
        // Received in het magazijn. Dat bewijst niet dat de goederen nooit weg zijn geweest (dat bevestigt de gebruiker); zonder deze
        // aannames kan de correctie niet worden vastgelegd en hoort een fysieke retour in de ontvangstflow.
        private static string FindLineConflict(SalesDispatchLine line, int expectedVersion)
        {
            var reservation = line.StockReservation;
            if (reservation.Status != ReservationRules.StatusConsumed)
            {
                return $"Reservation {reservation.Id} is {reservation.Status}, not Consumed; it cannot be corrected (again).";
            }
            if (reservation.Version != expectedVersion)
            {
                return $"Reservation {reservation.Id} is at version {reservation.Version}, not {expectedVersion}; reload it and decide again.";
            }
            var pallet = line.PalletContentLine.Pallet;
            if (pallet.LogisticsStatus != PalletRules.StatusReceived || pallet.LocationContext != PalletRules.LocationWarehouse)
            {
                return $"Pallet {pallet.Reference} is {pallet.LogisticsStatus} at {pallet.LocationContext}; the goods cannot be assumed to be in the warehouse, so this dispatch cannot be corrected administratively. A physical return needs the receipt flow.";
            }
            return null;
        }

        // Replay: dezelfde requestId voor dezelfde order, verzending, actor, genormaliseerde reden, bevestiging, orderversie en reserveringsset met versies.
        private async Task<ServiceResult<SalesDispatchCorrectionDto>> ReplayAsync(CorrectCommand command, CancellationToken cancellationToken)
        {
            var existing = await CorrectionQuery().SingleOrDefaultAsync(c => c.RequestId == command.RequestId, cancellationToken);
            if (existing == null)
            {
                return null;
            }
            var audit = await _db.SalesOrderAuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(e => e.RequestId == command.RequestId && e.EventType == ReservationRules.OrderDispatchCorrectedEvent, cancellationToken);
            var existingLines = existing.Lines.OrderBy(l => l.StockReservationId).Select(l => (l.StockReservationId, l.ExpectedReservationVersion)).ToList();
            var commandLines = command.Lines.OrderBy(l => l.ReservationId).Select(l => (l.ReservationId, l.ExpectedVersion)).ToList();
            var same = existing.SalesOrderId == command.SalesOrderId
                && existing.SalesDispatchId == command.DispatchId
                && existing.GoodsNeverLeftWarehouseConfirmed
                && string.Equals(existing.ActorReference, command.Actor, StringComparison.Ordinal)
                && string.Equals(existing.Reason, command.Reason, StringComparison.Ordinal)
                && audit != null
                && RecordedVersion(audit.OldValue) == command.ExpectedOrderVersion
                && existingLines.SequenceEqual(commandLines);
            return same
                ? ServiceResult<SalesDispatchCorrectionDto>.Ok(ToDto(existing))
                : ServiceResult<SalesDispatchCorrectionDto>.Conflict($"RequestId {command.RequestId} was already used for a different dispatch correction.");
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

        private IQueryable<SalesDispatchCorrection> CorrectionQuery() =>
            _db.SalesDispatchCorrections.AsNoTracking()
                .Include(c => c.Lines).ThenInclude(l => l.RestoredContentLine).ThenInclude(r => r.Pallet)
                .Include(c => c.Lines).ThenInclude(l => l.StockMovement)
                .AsSplitQuery();

        internal static SalesDispatchCorrectionDto ToDto(SalesDispatchCorrection correction) => new()
        {
            Id = correction.Id,
            RequestId = correction.RequestId,
            SalesDispatchId = correction.SalesDispatchId,
            SalesOrderId = correction.SalesOrderId,
            Reason = correction.Reason,
            ActorReference = correction.ActorReference,
            GoodsNeverLeftWarehouseConfirmed = correction.GoodsNeverLeftWarehouseConfirmed,
            CorrectedAt = DateTime.SpecifyKind(correction.CorrectedAt, DateTimeKind.Utc),
            Lines = correction.Lines.OrderBy(l => l.Id).Select(l => new SalesDispatchCorrectionLineDto
            {
                Id = l.Id,
                SalesDispatchLineId = l.SalesDispatchLineId,
                StockReservationId = l.StockReservationId,
                RestoredPalletContentLineId = l.RestoredContentLine?.Id ?? 0,
                PalletId = l.RestoredContentLine?.PalletId ?? 0,
                PalletReference = l.RestoredContentLine?.Pallet?.Reference,
                LotNumber = l.RestoredContentLine?.LotNumber,
                Quantity = l.Quantity,
                Unit = l.Unit,
                NetWeightKg = l.NetWeightKg,
                RestoredQualityStatus = l.RestoredContentLine?.QualityStatus,
                StockMovementId = l.StockMovement?.Id ?? 0
            }).ToList()
        };
    }
}
