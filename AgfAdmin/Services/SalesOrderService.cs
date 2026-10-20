using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public interface ISalesOrderService
    {
        Task<ServiceResult<SalesOrderDto>> CreateAsync(CreateSalesOrderDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<SalesOrderDto>> GetAsync(int id, CancellationToken cancellationToken = default);
        Task<ServiceResult<IReadOnlyList<SalesOrderDto>>> ListAsync(SalesOrderFilter filter, CancellationToken cancellationToken = default);
        Task<ServiceResult<IReadOnlyList<ReservableStockLineDto>>> GetReservableStockAsync(int salesOrderId, int salesOrderLineId, CancellationToken cancellationToken = default);
        Task<ServiceResult<SalesOrderDto>> CancelAsync(int salesOrderId, CancelSalesOrderDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<IReadOnlyList<SalesOrderAuditEventDto>>> GetAuditEventsAsync(int salesOrderId, CancellationToken cancellationToken = default);
    }

    // Schone verkooporders voor het nieuwe domein. Raakt nooit legacy Order/OrderLine/OrderLineAllocation/Inventory.
    // Idempotent via RequestId: de volledige genormaliseerde payload wordt vergeleken.
    public sealed class SalesOrderService : ISalesOrderService
    {
        private readonly AppDbContext _db;

        public SalesOrderService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<ServiceResult<SalesOrderDto>> CreateAsync(CreateSalesOrderDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var candidate = Build(request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<SalesOrderDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var existing = await OrderQuery().SingleOrDefaultAsync(o => o.RequestId == candidate.RequestId, cancellationToken);
            if (existing != null)
            {
                return ResultForRetry(existing, candidate);
            }

            if (!await _db.Customers.AnyAsync(c => c.Id == candidate.CustomerId, cancellationToken))
            {
                return ServiceResult<SalesOrderDto>.NotFound($"Customer {candidate.CustomerId} does not exist.");
            }
            var variantIds = candidate.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
            var known = await _db.ProductVariants.Where(v => variantIds.Contains(v.Id)).Select(v => v.Id).ToListAsync(cancellationToken);
            var missing = variantIds.Except(known).Cast<int?>().FirstOrDefault();
            if (missing != null)
            {
                return ServiceResult<SalesOrderDto>.NotFound($"Product variant {missing} does not exist.");
            }

            _db.SalesOrders.Add(candidate);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Gelijktijdige retry: de unieke index op RequestId heeft het tweede verzoek geweigerd.
                _db.ChangeTracker.Clear();
                var concurrent = await OrderQuery().SingleOrDefaultAsync(o => o.RequestId == candidate.RequestId, cancellationToken);
                if (concurrent == null)
                {
                    throw;
                }
                return ResultForRetry(concurrent, candidate);
            }

            var created = await OrderQuery().SingleAsync(o => o.Id == candidate.Id, cancellationToken);
            return ServiceResult<SalesOrderDto>.Created(ToDto(created));
        }

        private static SalesOrder Build(CreateSalesOrderDto request, Dictionary<string, List<string>> errors)
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
            if (request.CustomerId == null)
            {
                ValidationErrors.Add(errors, "customerId", "A customer is required.");
            }
            var reference = ValidationErrors.OptionalText(request.CustomerReference, ReservationRules.MaxCustomerReferenceLength, "customerReference", errors);
            if (request.RequestedDeliveryDate == null)
            {
                ValidationErrors.Add(errors, "requestedDeliveryDate", "A requested delivery date is required.");
            }
            var actor = ValidationErrors.RequiredText(request.ActorReference, ReservationRules.MaxActorLength, "actorReference", errors);

            var lines = new List<SalesOrderLine>();
            if (request.Lines == null || request.Lines.Count == 0)
            {
                ValidationErrors.Add(errors, "lines", "At least one line is required.");
            }
            else if (request.Lines.Count > ReservationRules.MaxOrderLines)
            {
                ValidationErrors.Add(errors, "lines", $"At most {ReservationRules.MaxOrderLines} lines are allowed.");
            }
            else
            {
                var seen = new HashSet<(int, string)>();
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
                    if (line.ProductVariantId == null)
                    {
                        ValidationErrors.Add(errors, $"{key}.productVariantId", "A product variant is required.");
                    }
                    if (line.Quantity == null || !SupplyRules.IsPositiveDecimal18_3(line.Quantity.Value))
                    {
                        ValidationErrors.Add(errors, $"{key}.quantity", "Quantity is required, must be positive and may have at most 3 decimals.");
                    }
                    if (!SupplyRules.IsPackageUnit(line.Unit))
                    {
                        ValidationErrors.Add(errors, $"{key}.unit", $"Unit must be one of: {string.Join(", ", SupplyRules.PackageUnits)}.");
                    }
                    if (errors.Count > before)
                    {
                        continue;
                    }
                    // Eén regel per variant + eenheid: anders worden reserveringen dubbelzinnig.
                    if (!seen.Add((line.ProductVariantId!.Value, line.Unit)))
                    {
                        ValidationErrors.Add(errors, key, "The same product variant and unit occur more than once.");
                        continue;
                    }
                    lines.Add(new SalesOrderLine { ProductVariantId = line.ProductVariantId.Value, Quantity = line.Quantity!.Value, Unit = line.Unit });
                }
            }

            if (errors.Count > 0)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            foreach (var line in lines)
            {
                line.CreatedAt = now;
            }
            return new SalesOrder
            {
                RequestId = request.RequestId!.Value,
                CustomerId = request.CustomerId!.Value,
                Status = ReservationRules.OrderStatusOpen,
                CustomerReference = reference,
                RequestedDeliveryDate = UtcPrecision.ToStoredUtc(request.RequestedDeliveryDate!.Value),
                ActorReference = actor,
                CreatedAt = now,
                UpdatedAt = now,
                Lines = lines
            };
        }

        // Strikt: klant, genormaliseerde referentie, leverdatum (UTC, microseconden), actor en de regels in volgorde.
        private static ServiceResult<SalesOrderDto> ResultForRetry(SalesOrder existing, SalesOrder candidate)
        {
            var existingLines = existing.Lines.OrderBy(l => l.Id).ToList();
            var candidateLines = candidate.Lines.ToList();
            var same = existing.CustomerId == candidate.CustomerId
                && string.Equals(existing.CustomerReference, candidate.CustomerReference, StringComparison.Ordinal)
                && existing.RequestedDeliveryDate.Ticks == candidate.RequestedDeliveryDate.Ticks
                && string.Equals(existing.ActorReference, candidate.ActorReference, StringComparison.Ordinal)
                && existingLines.Count == candidateLines.Count
                && existingLines.Zip(candidateLines).All(pair =>
                    pair.First.ProductVariantId == pair.Second.ProductVariantId
                    && pair.First.Quantity == pair.Second.Quantity
                    && string.Equals(pair.First.Unit, pair.Second.Unit, StringComparison.Ordinal));

            return same
                ? ServiceResult<SalesOrderDto>.Ok(ToDto(existing))
                : ServiceResult<SalesOrderDto>.Conflict($"RequestId {candidate.RequestId} was already used for a different sales order.");
        }

        public async Task<ServiceResult<SalesOrderDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var order = await OrderQuery().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
            return order == null
                ? ServiceResult<SalesOrderDto>.NotFound($"Sales order {id} does not exist.")
                : ServiceResult<SalesOrderDto>.Ok(await ToDtoWithCancellationAsync(order, cancellationToken));
        }

        // De actor van een annulering staat alleen in het SalesOrderCancelled-event (één extra leesactie per order).
        private async Task<SalesOrderDto> ToDtoWithCancellationAsync(SalesOrder order, CancellationToken cancellationToken)
        {
            var actors = await CancelledByAsync(new List<int> { order.Id }, cancellationToken);
            var corrections = await CorrectionIdsAsync(new List<int> { order.Id }, cancellationToken);
            return ToDto(order, actors.GetValueOrDefault(order.Id), corrections);
        }

        // Reservering -> correctie-id voor Corrected reserveringen (één leesactie per aanroep).
        private async Task<Dictionary<int, int>> CorrectionIdsAsync(List<int> orderIds, CancellationToken cancellationToken)
        {
            if (orderIds.Count == 0) return new Dictionary<int, int>();
            return await _db.SalesDispatchCorrectionLines.AsNoTracking()
                .Where(l => orderIds.Contains(l.SalesDispatchCorrection.SalesOrderId))
                .Select(l => new { l.StockReservationId, l.SalesDispatchCorrectionId })
                .ToDictionaryAsync(l => l.StockReservationId, l => l.SalesDispatchCorrectionId, cancellationToken);
        }

        private async Task<Dictionary<int, string>> CancelledByAsync(List<int> orderIds, CancellationToken cancellationToken)
        {
            if (orderIds.Count == 0)
            {
                return new Dictionary<int, string>();
            }
            var events = await _db.SalesOrderAuditEvents.AsNoTracking()
                .Where(e => orderIds.Contains(e.SalesOrderId) && e.EventType == ReservationRules.OrderCancelledEvent)
                .Select(e => new { e.SalesOrderId, e.ActorReference, e.Id })
                .ToListAsync(cancellationToken);
            return events.GroupBy(e => e.SalesOrderId).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Id).Last().ActorReference);
        }

        public async Task<ServiceResult<IReadOnlyList<SalesOrderDto>>> ListAsync(SalesOrderFilter filter, CancellationToken cancellationToken = default)
        {
            filter ??= new SalesOrderFilter();
            if (filter.Status != null && !ReservationRules.IsOrderStatus(filter.Status))
            {
                return ServiceResult<IReadOnlyList<SalesOrderDto>>.Validation(new Dictionary<string, string[]>
                {
                    ["status"] = new[] { $"Status must be one of: {string.Join(", ", ReservationRules.OrderStatuses)}." }
                });
            }

            var query = OrderQuery();
            if (filter.CustomerId != null) query = query.Where(o => o.CustomerId == filter.CustomerId);
            if (filter.Status != null) query = query.Where(o => o.Status == filter.Status);
            var orders = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Take(ReservationRules.MaxListResults).ToListAsync(cancellationToken);
            var actors = await CancelledByAsync(orders.Where(o => o.Status == ReservationRules.OrderStatusCancelled).Select(o => o.Id).ToList(), cancellationToken);
            var corrections = await CorrectionIdsAsync(orders.Select(o => o.Id).ToList(), cancellationToken);
            return ServiceResult<IReadOnlyList<SalesOrderDto>>.Ok(orders.Select(o => ToDto(o, actors.GetValueOrDefault(o.Id), corrections)).ToList());
        }

        // ======================== Annuleren ========================

        private sealed record CancelCommand(Guid RequestId, int SalesOrderId, int ExpectedVersion, string Actor, string Reason);

        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Annuleert een Open order en geeft alle Active reserveringen (ook on hold) vrij in één transactie, met één
        // orderevent en per reservering één palletevent. Concurrency: de order (expectedOrderVersion), elke orderregel
        // (botst met gelijktijdig reserveren), elke vrijgegeven reservering (botst met gelijktijdig vrijgeven) en elke
        // inhoudsregel worden aangeraakt; precies één van twee gelijktijdige schrijvers wint.
        public async Task<ServiceResult<SalesOrderDto>> CancelAsync(int salesOrderId, CancelSalesOrderDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            CancelCommand command = null;
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
                if (request.ExpectedOrderVersion == null)
                {
                    ValidationErrors.Add(errors, "expectedOrderVersion", "The order version the cancellation is based on is required.");
                }
                var actor = ValidationErrors.RequiredText(request.ActorReference, ReservationRules.MaxActorLength, "actorReference", errors);
                var reason = ValidationErrors.RequiredText(request.Reason, ReservationRules.MaxReasonLength, "reason", errors);
                if (errors.Count == 0)
                {
                    command = new CancelCommand(request.RequestId!.Value, salesOrderId, request.ExpectedOrderVersion!.Value, actor, reason);
                }
            }
            if (errors.Count > 0)
            {
                return ServiceResult<SalesOrderDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var replay = await ReplayCancelAsync(command, cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var order = await _db.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.PalletContentLine)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.SupplyCommitmentLine)
                .SingleOrDefaultAsync(o => o.Id == salesOrderId, cancellationToken);
            if (order == null)
            {
                return ServiceResult<SalesOrderDto>.NotFound($"Sales order {salesOrderId} does not exist.");
            }
            if (order.Status != ReservationRules.OrderStatusOpen)
            {
                return ServiceResult<SalesOrderDto>.Conflict($"Sales order {salesOrderId} is {order.Status} and cannot be cancelled again.");
            }
            if (order.Version != command.ExpectedVersion)
            {
                return ServiceResult<SalesOrderDto>.Conflict($"Sales order {salesOrderId} is at version {order.Version}, not {command.ExpectedVersion}; reload it and decide again.");
            }
            var reservations = order.Lines.SelectMany(l => l.Reservations).ToList();
            if (reservations.Any(r => r.Status == ReservationRules.StatusConsumed))
            {
                return ServiceResult<SalesOrderDto>.Conflict($"Sales order {salesOrderId} has consumed reservations; returns and shipment corrections are a separate flow.");
            }

            var now = DateTime.UtcNow;
            order.Status = ReservationRules.OrderStatusCancelled;
            order.CancelledAt = now;
            order.CancelReason = command.Reason;
            order.CancelRequestId = command.RequestId;
            order.UpdatedAt = now;

            // Elke regel aanraken: een gelijktijdige reservering op welke regel dan ook kan nooit samen met deze annulering slagen.
            foreach (var line in order.Lines)
            {
                _db.Entry(line).Property(l => l.Version).IsModified = true;
            }

            var active = reservations.Where(r => r.Status == ReservationRules.StatusActive).OrderBy(r => r.Id).ToList();
            foreach (var reservation in active)
            {
                var expectedReservationVersion = reservation.Version;
                reservation.Status = ReservationRules.StatusReleased;
                reservation.ReleasedAt = now;
                // De genormaliseerde reden ongewijzigd (geen voorvoegsel: dat zou de 500 tekens kunnen overschrijden);
                // dat de vrijgave uit een annulering komt, staat in het event.
                reservation.ReleaseReason = command.Reason;
                reservation.UpdatedAt = now;
                var oldValue = JsonSerializer.Serialize(new { reservationId = reservation.Id, status = ReservationRules.StatusActive, version = expectedReservationVersion }, SnapshotJson);
                if (reservation.PalletContentLine != null)
                {
                    _db.Entry(reservation.PalletContentLine).Property(c => c.Version).IsModified = true;
                    _db.PalletAuditEvents.Add(new PalletAuditEvent
                    {
                        PalletId = reservation.PalletContentLine.PalletId,
                        PalletContentLineId = reservation.PalletContentLineId,
                        EventType = ReservationRules.ReservationReleasedEvent,
                        OldValue = oldValue,
                        NewValue = JsonSerializer.Serialize(new
                        {
                            reservationId = reservation.Id,
                            status = ReservationRules.StatusReleased,
                            quantity = reservation.Quantity,
                            unit = reservation.Unit,
                            netWeightKg = reservation.NetWeightKg,
                            cause = ReservationRules.OrderCancelledEvent,
                            salesOrderId = order.Id,
                            cancellationRequestId = command.RequestId
                        }, SnapshotJson),
                        Reason = command.Reason,
                        ActorReference = command.Actor,
                        OccurredAt = now,
                        // De unieke RequestId hoort bij het orderevent; de palletevents verwijzen via cancellationRequestId.
                        RequestId = null
                    });
                }
                else
                {
                    // DirectTrade-toewijzing (geen pallet): de bronregel wordt aangeraakt en het event hoort bij de order.
                    if (reservation.SupplyCommitmentLine != null)
                    {
                        _db.Entry(reservation.SupplyCommitmentLine).Property(l => l.Version).IsModified = true;
                    }
                    _db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
                    {
                        SalesOrder = order,
                        EventType = ReservationRules.DirectTradeReleasedEvent,
                        OldValue = oldValue,
                        NewValue = JsonSerializer.Serialize(new
                        {
                            reservationId = reservation.Id,
                            status = ReservationRules.StatusReleased,
                            salesOrderLineId = reservation.SalesOrderLineId,
                            supplyCommitmentLineId = reservation.SupplyCommitmentLineId,
                            quantity = reservation.Quantity,
                            unit = reservation.Unit,
                            cause = ReservationRules.OrderCancelledEvent,
                            cancellationRequestId = command.RequestId
                        }, SnapshotJson),
                        Reason = command.Reason,
                        ActorReference = command.Actor,
                        OccurredAt = now,
                        RequestId = null
                    });
                }
            }

            // Begrensde snapshot: aantallen, geen lijst van reserverings-id's; fysieke reserveringen en toewijzingen apart.
            var releasedPhysical = active.Where(r => r.Source != ReservationRules.SourceDirectTrade).ToList();
            var releasedAllocations = active.Where(r => r.Source == ReservationRules.SourceDirectTrade).ToList();
            _db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
            {
                SalesOrder = order,
                EventType = ReservationRules.OrderCancelledEvent,
                OldValue = JsonSerializer.Serialize(new { status = ReservationRules.OrderStatusOpen, version = command.ExpectedVersion }, SnapshotJson),
                NewValue = JsonSerializer.Serialize(new
                {
                    status = ReservationRules.OrderStatusCancelled,
                    releasedReservationCount = releasedPhysical.Count,
                    releasedQuantityByUnit = releasedPhysical.GroupBy(r => r.Unit).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key ?? "", g => g.Sum(r => r.Quantity)),
                    releasedNetWeightKg = releasedPhysical.Sum(r => r.NetWeightKg ?? 0m),
                    releasedAllocationCount = releasedAllocations.Count,
                    releasedAllocationQuantityByUnit = releasedAllocations.GroupBy(r => r.Unit).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key ?? "", g => g.Sum(r => r.Quantity)),
                    lineCount = order.Lines.Count
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
                // Alleen een herkende botsing wordt 409: een andere schrijver (reserveren, vrijgeven, annuleren) was eerder,
                // of dezelfde requestId is net door een gelijktijdige retry vastgelegd. Andere databasefouten blijven zichtbaar.
                _db.ChangeTracker.Clear();
                var concurrent = await ReplayCancelAsync(command, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                return ServiceResult<SalesOrderDto>.Conflict($"Sales order {salesOrderId} or its reservations were changed concurrently; reload and decide again.");
            }

            _db.ChangeTracker.Clear();
            var cancelled = await OrderQuery().SingleAsync(o => o.Id == salesOrderId, cancellationToken);
            return ServiceResult<SalesOrderDto>.Ok(ToDto(cancelled, command.Actor));
        }

        // Replay: dezelfde requestId voor dezelfde order, actor, genormaliseerde reden en oorspronkelijke versie geeft de huidige toestand terug.
        private async Task<ServiceResult<SalesOrderDto>> ReplayCancelAsync(CancelCommand command, CancellationToken cancellationToken)
        {
            var existing = await OrderQuery().SingleOrDefaultAsync(o => o.CancelRequestId == command.RequestId, cancellationToken);
            var audit = await _db.SalesOrderAuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(e => e.RequestId == command.RequestId && e.EventType == ReservationRules.OrderCancelledEvent, cancellationToken);
            if (existing == null && audit == null)
            {
                return null;
            }
            var same = existing != null
                && audit != null
                && existing.Id == command.SalesOrderId
                && audit.SalesOrderId == command.SalesOrderId
                && string.Equals(audit.ActorReference, command.Actor, StringComparison.Ordinal)
                && string.Equals(audit.Reason, command.Reason, StringComparison.Ordinal)
                && RecordedVersion(audit.OldValue) == command.ExpectedVersion;
            return same
                ? ServiceResult<SalesOrderDto>.Ok(ToDto(existing, audit.ActorReference))
                : ServiceResult<SalesOrderDto>.Conflict($"RequestId {command.RequestId} was already used for a different cancellation.");
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

        public async Task<ServiceResult<IReadOnlyList<SalesOrderAuditEventDto>>> GetAuditEventsAsync(int salesOrderId, CancellationToken cancellationToken = default)
        {
            if (!await _db.SalesOrders.AnyAsync(o => o.Id == salesOrderId, cancellationToken))
            {
                return ServiceResult<IReadOnlyList<SalesOrderAuditEventDto>>.NotFound($"Sales order {salesOrderId} does not exist.");
            }
            var events = await _db.SalesOrderAuditEvents.AsNoTracking()
                .Where(e => e.SalesOrderId == salesOrderId)
                .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
                .Select(e => new SalesOrderAuditEventDto
                {
                    Id = e.Id,
                    SalesOrderId = e.SalesOrderId,
                    EventType = e.EventType,
                    OldValue = e.OldValue,
                    NewValue = e.NewValue,
                    Reason = e.Reason,
                    ActorReference = e.ActorReference,
                    OccurredAt = e.OccurredAt,
                    RequestId = e.RequestId
                })
                .ToListAsync(cancellationToken);
            foreach (var item in events)
            {
                item.OccurredAt = DateTime.SpecifyKind(item.OccurredAt, DateTimeKind.Utc);
            }
            return ServiceResult<IReadOnlyList<SalesOrderAuditEventDto>>.Ok(events);
        }

        public async Task<ServiceResult<IReadOnlyList<ReservableStockLineDto>>> GetReservableStockAsync(int salesOrderId, int salesOrderLineId, CancellationToken cancellationToken = default)
        {
            var line = await _db.SalesOrderLines.AsNoTracking().SingleOrDefaultAsync(l => l.Id == salesOrderLineId && l.SalesOrderId == salesOrderId, cancellationToken);
            if (line == null)
            {
                return ServiceResult<IReadOnlyList<ReservableStockLineDto>>.NotFound($"Sales order line {salesOrderLineId} does not exist on sales order {salesOrderId}.");
            }

            var contents = await _db.PalletContentLines.AsNoTracking()
                .Where(c => c.SupplyCommitmentLine.ProductVariantId == line.ProductVariantId
                    && c.Unit == line.Unit
                    && c.QualityStatus == PalletRules.QualityApproved
                    && c.Pallet.LogisticsStatus == PalletRules.StatusReceived
                    && c.Pallet.LocationContext == PalletRules.LocationWarehouse
                    && c.SupplyCommitmentLine.SupplyCommitment.Route == SupplyRules.RouteWarehouse)
                .Select(c => new
                {
                    c.Id, c.PalletId, PalletReference = c.Pallet.Reference, c.Pallet.WarehouseLocation, c.LotNumber, c.Unit,
                    c.Quantity, c.NetWeightKg, c.DispatchedQuantity, c.DispatchedNetWeightKg, c.QualityStatus, c.Version, PalletCreatedAt = c.Pallet.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var reserved = await ReservedByContentLineAsync(_db, contents.Select(c => c.Id).ToList(), cancellationToken);
            var result = contents
                .Select(c =>
                {
                    var (quantity, kg) = reserved.GetValueOrDefault(c.Id);
                    var onHandQuantity = ReservationRules.OnHandQuantity(c.Quantity, c.DispatchedQuantity);
                    var onHandKg = ReservationRules.OnHandQuantity(c.NetWeightKg, c.DispatchedNetWeightKg);
                    return new ReservableStockLineDto
                    {
                        PalletContentLineId = c.Id,
                        PalletId = c.PalletId,
                        PalletReference = c.PalletReference,
                        WarehouseLocation = c.WarehouseLocation,
                        LotNumber = c.LotNumber,
                        Unit = c.Unit,
                        Quantity = c.Quantity,
                        NetWeightKg = c.NetWeightKg,
                        DispatchedQuantity = c.DispatchedQuantity,
                        DispatchedNetWeightKg = c.DispatchedNetWeightKg,
                        OnHandQuantity = onHandQuantity,
                        OnHandNetWeightKg = onHandKg,
                        ReservedQuantity = quantity,
                        ReservedNetWeightKg = kg,
                        // Beschikbaar = voorraad - Active gereserveerd; verbruikte reserveringen zitten al in afgeboekt.
                        AvailableQuantity = Math.Max(0m, onHandQuantity - quantity),
                        AvailableNetWeightKg = Math.Max(0m, onHandKg - kg),
                        QualityStatus = c.QualityStatus,
                        ContentLineVersion = c.Version
                    };
                })
                .Where(c => c.AvailableQuantity > 0m)
                .OrderBy(c => c.PalletReference, StringComparer.Ordinal)
                .ThenBy(c => c.PalletContentLineId)
                .ToList();
            return ServiceResult<IReadOnlyList<ReservableStockLineDto>>.Ok(result);
        }

        // Vastgelegde klantvraag op een orderregel: Active (Physical én DirectTrade) + Consumed; alleen Released komt terug als open vraag.
        // Eén definitie voor reserveren, toewijzen en de DTO, zodat de regelbewaking nooit uiteenloopt.
        internal static async Task<decimal> CommittedOnOrderLineAsync(AppDbContext db, int salesOrderLineId, CancellationToken cancellationToken)
        {
            var quantities = await db.StockReservations.AsNoTracking()
                .Where(r => r.SalesOrderLineId == salesOrderLineId && (r.Status == ReservationRules.StatusActive || r.Status == ReservationRules.StatusConsumed))
                .Select(r => r.Quantity)
                .ToListAsync(cancellationToken);
            return quantities.Sum();
        }

        // Som (in het geheugen, exact decimal) van Active Physical verkoopreserveringen per inhoudsregel (toewijzingen staan nooit op inhoud).
        internal static async Task<Dictionary<int, (decimal Quantity, decimal NetWeightKg)>> ReservedByContentLineAsync(AppDbContext db, List<int> contentLineIds, CancellationToken cancellationToken)
        {
            if (contentLineIds.Count == 0)
            {
                return new Dictionary<int, (decimal, decimal)>();
            }
            var rows = await db.StockReservations.AsNoTracking()
                .Where(r => r.PalletContentLineId != null && contentLineIds.Contains(r.PalletContentLineId.Value)
                    && r.SalesOrderLineId != null && r.Status == ReservationRules.StatusActive && r.Source == ReservationRules.SourcePhysical)
                .Select(r => new { ContentId = r.PalletContentLineId!.Value, r.Quantity, r.NetWeightKg })
                .ToListAsync(cancellationToken);
            return rows.GroupBy(r => r.ContentId).ToDictionary(g => g.Key, g => (g.Sum(r => r.Quantity), g.Sum(r => r.NetWeightKg ?? 0m)));
        }

        private IQueryable<SalesOrder> OrderQuery() =>
            _db.SalesOrders.AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Lines).ThenInclude(l => l.ProductVariant).ThenInclude(v => v.Product)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.PalletContentLine).ThenInclude(c => c.Pallet)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.SupplyCommitmentLine).ThenInclude(s => s.SupplyCommitment).ThenInclude(c => c.Vendor)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.DispatchLine).ThenInclude(d => d.SalesDispatch)
                .AsSplitQuery();

        internal static SalesOrderDto ToDto(SalesOrder order, string cancelledBy = null, IReadOnlyDictionary<int, int> correctionIds = null) => new()
        {
            Id = order.Id,
            RequestId = order.RequestId,
            CustomerId = order.CustomerId,
            CustomerName = order.Customer?.Name,
            Status = order.Status,
            CustomerReference = order.CustomerReference,
            RequestedDeliveryDate = DateTime.SpecifyKind(order.RequestedDeliveryDate, DateTimeKind.Utc),
            ActorReference = order.ActorReference,
            CreatedAt = DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc),
            Version = order.Version,
            CancelledAt = order.CancelledAt == null ? null : DateTime.SpecifyKind(order.CancelledAt.Value, DateTimeKind.Utc),
            CancelReason = order.CancelReason,
            CancelledBy = order.Status == ReservationRules.OrderStatusCancelled ? cancelledBy : null,
            Fulfilled = order.Lines.Count > 0 && order.Lines.All(l => l.Reservations.Where(r => r.Status == ReservationRules.StatusConsumed).Sum(r => r.Quantity) == l.Quantity),
            Lines = order.Lines.OrderBy(l => l.Id).Select(line =>
            {
                // Gereserveerd = Active Physical; toegewezen = Active DirectTrade; verzonden = Consumed. Open trekt alle drie af.
                var active = line.Reservations.Where(r => r.Status == ReservationRules.StatusActive && r.Source != ReservationRules.SourceDirectTrade).ToList();
                var allocated = line.Reservations.Where(r => r.Status == ReservationRules.StatusActive && r.Source == ReservationRules.SourceDirectTrade).ToList();
                var consumed = line.Reservations.Where(r => r.Status == ReservationRules.StatusConsumed).ToList();
                var reservedQuantity = active.Sum(r => r.Quantity);
                var allocatedQuantity = allocated.Sum(r => r.Quantity);
                var dispatchedQuantity = consumed.Sum(r => r.Quantity);
                return new SalesOrderLineDto
                {
                    Id = line.Id,
                    ProductVariantId = line.ProductVariantId,
                    ProductName = line.ProductVariant?.Product?.Name,
                    ProductVariantName = line.ProductVariant?.Name,
                    Quantity = line.Quantity,
                    Unit = line.Unit,
                    ReservedQuantity = reservedQuantity,
                    ReservedNetWeightKg = active.Sum(r => r.NetWeightKg ?? 0m),
                    AllocatedQuantity = allocatedQuantity,
                    DispatchedQuantity = dispatchedQuantity,
                    DispatchedNetWeightKg = consumed.Sum(r => r.NetWeightKg ?? 0m),
                    // Open = gevraagd - Active Physical - Active DirectTrade - Consumed: verzonden komt nooit terug als open vraag.
                    OpenQuantity = Math.Max(0m, line.Quantity - reservedQuantity - allocatedQuantity - dispatchedQuantity),
                    Fulfilled = dispatchedQuantity == line.Quantity,
                    Version = line.Version,
                    Reservations = line.Reservations.OrderBy(r => r.Id).Select(r => StockReservationService.ToDto(r, order.Id,
                        correctionIds != null && correctionIds.TryGetValue(r.Id, out var correctionId) ? correctionId : null)).ToList()
                };
            }).ToList()
        };
    }

    // Afkappen op microseconden (PostgreSQL timestamptz), zodat een retry na opslag exact vergelijkbaar blijft.
    public static class UtcPrecision
    {
        public static DateTime ToStoredUtc(DateTimeOffset value)
        {
            var utc = value.UtcDateTime;
            return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
        }
    }

    internal static class ValidationErrors
    {
        public static void Add(Dictionary<string, List<string>> errors, string key, string message)
        {
            if (!errors.TryGetValue(key, out var messages))
            {
                messages = new List<string>();
                errors[key] = messages;
            }
            messages.Add(message);
        }

        public static IReadOnlyDictionary<string, string[]> ToArrays(Dictionary<string, List<string>> errors) =>
            errors.ToDictionary(e => e.Key, e => e.Value.ToArray());

        public static string RequiredText(string value, int maxLength, string key, Dictionary<string, List<string>> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Add(errors, key, "This field is required and must not be blank.");
                return null;
            }
            var trimmed = value.Trim();
            if (trimmed.Length > maxLength)
            {
                Add(errors, key, $"This field may contain at most {maxLength} characters.");
                return null;
            }
            return trimmed;
        }

        // Getrimd; leeg of alleen spaties wordt null.
        public static string OptionalText(string value, int maxLength, string key, Dictionary<string, List<string>> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            var trimmed = value.Trim();
            if (trimmed.Length > maxLength)
            {
                Add(errors, key, $"This field may contain at most {maxLength} characters.");
                return null;
            }
            return trimmed;
        }
    }
}
