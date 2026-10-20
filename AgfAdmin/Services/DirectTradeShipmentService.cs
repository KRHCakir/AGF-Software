using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public interface IDirectTradeShipmentService
    {
        Task<ServiceResult<DirectTradeShipmentDto>> ShipAsync(int salesOrderId, CreateDirectTradeShipmentDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<IReadOnlyList<DirectTradeShipmentDto>>> ListAsync(int salesOrderId, CancellationToken cancellationToken = default);
    }

    // DirectTrade-verzending (leverancier/herkomst -> klant, zonder ons magazijn): hele Active DirectTrade-toewijzingen van één Open order
    // worden verbruikt (Consumed) met de werkelijk verzonden hoeveelheid, eenheid, netto kg en partij en één verzenddatum, in één transactie
    // met de verzending, de audit en de versies. Geen voorraadmutatie, geen pallet, geen magazijnontvangst en geen magazijnverzending.
    // Verbruikte toewijzingen blijven de capaciteit van de bronregel bezetten. Verzonden betekent niet afgeleverd.
    public sealed class DirectTradeShipmentService : IDirectTradeShipmentService
    {
        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly AppDbContext _db;
        private readonly IBusinessCalendar _calendar;

        public DirectTradeShipmentService(AppDbContext db, IBusinessCalendar calendar)
        {
            _db = db;
            _calendar = calendar;
        }

        private sealed record LineCommand(int ReservationId, int ExpectedVersion, decimal Quantity, string Unit, decimal NetWeightKg, string LotNumber, string LotDeviationReason);

        private sealed record ShipCommand(Guid RequestId, int SalesOrderId, int ExpectedOrderVersion, string Actor, DateOnly ShippedOn, string Reference, IReadOnlyList<LineCommand> Lines);

        public async Task<ServiceResult<DirectTradeShipmentDto>> ShipAsync(int salesOrderId, CreateDirectTradeShipmentDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var command = Build(salesOrderId, request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<DirectTradeShipmentDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var replay = await ReplayAsync(command, cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var order = await _db.SalesOrders
                .Include(o => o.Customer)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.SupplyCommitmentLine).ThenInclude(s => s.SupplyCommitment).ThenInclude(c => c.Vendor)
                .AsSplitQuery()
                .SingleOrDefaultAsync(o => o.Id == salesOrderId, cancellationToken);
            if (order == null)
            {
                return ServiceResult<DirectTradeShipmentDto>.NotFound($"Sales order {salesOrderId} does not exist.");
            }
            if (order.Status != ReservationRules.OrderStatusOpen)
            {
                return ServiceResult<DirectTradeShipmentDto>.Conflict($"Sales order {salesOrderId} is {order.Status}; a DirectTrade shipment requires an open order.");
            }
            if (order.Version != command.ExpectedOrderVersion)
            {
                return ServiceResult<DirectTradeShipmentDto>.Conflict($"Sales order {salesOrderId} is at version {order.Version}, not {command.ExpectedOrderVersion}; reload it and decide again.");
            }

            var byId = order.Lines.SelectMany(l => l.Reservations.Select(r => (Line: l, Reservation: r))).ToDictionary(x => x.Reservation.Id);
            var selected = new List<(SalesOrderLine Line, StockReservation Reservation, LineCommand Command, string ReasonToStore)>();
            for (var i = 0; i < command.Lines.Count; i++)
            {
                var lineCommand = command.Lines[i];
                if (!byId.TryGetValue(lineCommand.ReservationId, out var found))
                {
                    return ServiceResult<DirectTradeShipmentDto>.Conflict($"Reservation {lineCommand.ReservationId} does not belong to sales order {salesOrderId}.");
                }
                var conflict = FindConflict(order, found.Reservation, lineCommand);
                if (conflict != null)
                {
                    return ServiceResult<DirectTradeShipmentDto>.Conflict(conflict);
                }
                // Afwijkende partij ten opzichte van een ingevulde bronpartij: toelichting verplicht. Zonder afwijking wordt geen toelichting bewaard.
                var sourceLot = found.Reservation.SupplyCommitmentLine.LotNumber;
                var deviates = sourceLot != null && !string.Equals(lineCommand.LotNumber, sourceLot, StringComparison.Ordinal);
                if (deviates && lineCommand.LotDeviationReason == null)
                {
                    ValidationErrors.Add(errors, $"lines[{i}].lotDeviationReason", $"Lot {lineCommand.LotNumber} differs from the source lot {sourceLot}; explain the deviation.");
                }
                selected.Add((found.Line, found.Reservation, lineCommand, deviates ? lineCommand.LotDeviationReason : null));
            }
            if (errors.Count > 0)
            {
                return ServiceResult<DirectTradeShipmentDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var now = DateTime.UtcNow;
            var shipment = new DirectTradeShipment
            {
                RequestId = command.RequestId,
                SalesOrder = order,
                CustomerId = order.CustomerId,
                CustomerNameSnapshot = order.Customer.Name,
                ShippedOn = command.ShippedOn,
                Reference = command.Reference,
                ActorReference = command.Actor,
                RecordedAt = now
            };
            _db.DirectTradeShipments.Add(shipment);

            // De order en elke geraakte regel aanraken: botst met gelijktijdig annuleren, verzenden, reserveren en toewijzen op die regel.
            order.UpdatedAt = now;
            _db.Entry(order).Property(o => o.Version).IsModified = true;
            foreach (var line in selected.Select(s => s.Line).Distinct())
            {
                _db.Entry(line).Property(l => l.Version).IsModified = true;
            }

            foreach (var (line, reservation, lineCommand, reason) in selected)
            {
                var source = reservation.SupplyCommitmentLine;
                var commitment = source.SupplyCommitment;
                reservation.Status = ReservationRules.StatusConsumed;
                reservation.ConsumedAt = now;
                reservation.UpdatedAt = now;
                shipment.Lines.Add(new DirectTradeShipmentLine
                {
                    StockReservation = reservation,
                    SalesOrderLine = line,
                    SupplyCommitmentLine = source,
                    SupplyCommitmentId = commitment.Id,
                    VendorId = commitment.VendorId,
                    VendorNameSnapshot = commitment.Vendor.Name,
                    OriginSnapshot = commitment.Origin,
                    DestinationSnapshot = commitment.Destination,
                    AllocatedQuantity = reservation.Quantity,
                    Quantity = lineCommand.Quantity,
                    Unit = lineCommand.Unit,
                    NetWeightKg = lineCommand.NetWeightKg,
                    LotNumber = lineCommand.LotNumber,
                    SourceLotNumber = source.LotNumber,
                    LotDeviationReason = reason,
                    ExpectedReservationVersion = lineCommand.ExpectedVersion
                });
            }

            // Begrensde samenvatting: aantallen en totalen, geen lijst van id's.
            _db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
            {
                SalesOrder = order,
                EventType = ReservationRules.DirectTradeShippedEvent,
                OldValue = JsonSerializer.Serialize(new { status = order.Status, version = command.ExpectedOrderVersion }, SnapshotJson),
                NewValue = JsonSerializer.Serialize(new
                {
                    status = order.Status,
                    shippedOn = command.ShippedOn.ToString("yyyy-MM-dd"),
                    reference = command.Reference,
                    customerId = order.CustomerId,
                    customerName = order.Customer.Name,
                    consumedAllocationCount = selected.Count,
                    shippedQuantityByUnit = selected.GroupBy(s => s.Command.Unit).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(s => s.Command.Quantity)),
                    shippedNetWeightKg = selected.Sum(s => s.Command.NetWeightKg),
                    lotDeviationCount = selected.Count(s => s.ReasonToStore != null),
                    deliveryRecorded = false
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
                _db.ChangeTracker.Clear();
                var concurrent = await ReplayAsync(command, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                return ServiceResult<DirectTradeShipmentDto>.Conflict($"Sales order {salesOrderId} or its allocations were changed concurrently; reload and decide again.");
            }

            _db.ChangeTracker.Clear();
            return ServiceResult<DirectTradeShipmentDto>.Created(ToDto(await Query().SingleAsync(d => d.Id == shipment.Id, cancellationToken)));
        }

        private ShipCommand Build(int salesOrderId, CreateDirectTradeShipmentDto request, Dictionary<string, List<string>> errors)
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
                ValidationErrors.Add(errors, "expectedOrderVersion", "The order version the shipment is based on is required.");
            }
            var actor = ValidationErrors.RequiredText(request.ActorReference, ReservationRules.MaxActorLength, "actorReference", errors);
            var reference = ValidationErrors.OptionalText(request.Reference, ReservationRules.MaxDispatchReferenceLength, "reference", errors);
            if (request.ShippedOn == null)
            {
                ValidationErrors.Add(errors, "shippedOn", "The shipping date (calendar date) is required.");
            }
            else
            {
                var today = _calendar.Today();
                if (request.ShippedOn.Value > today)
                {
                    ValidationErrors.Add(errors, "shippedOn", $"The shipping date {request.ShippedOn.Value:yyyy-MM-dd} is after today ({today:yyyy-MM-dd}, Europe/Amsterdam); only shipments that happened can be recorded.");
                }
            }

            var lines = new List<LineCommand>();
            if (request.Lines == null || request.Lines.Count == 0)
            {
                ValidationErrors.Add(errors, "lines", "Select at least one allocation to ship.");
            }
            else if (request.Lines.Count > ReservationRules.MaxDispatchLines)
            {
                ValidationErrors.Add(errors, "lines", $"At most {ReservationRules.MaxDispatchLines} allocations can be shipped at once.");
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
                        ValidationErrors.Add(errors, $"{key}.stockReservationId", "An allocation is required.");
                    }
                    if (line.ExpectedReservationVersion == null || line.ExpectedReservationVersion < 0)
                    {
                        ValidationErrors.Add(errors, $"{key}.expectedReservationVersion", "The allocation version the shipment is based on is required.");
                    }
                    if (line.Quantity == null || !SupplyRules.IsPositiveDecimal18_3(line.Quantity.Value))
                    {
                        ValidationErrors.Add(errors, $"{key}.quantity", "The shipped quantity is required, must be positive and may have at most 3 decimals.");
                    }
                    if (!SupplyRules.IsPackageUnit(line.Unit))
                    {
                        ValidationErrors.Add(errors, $"{key}.unit", $"Unit must be one of: {string.Join(", ", SupplyRules.PackageUnits)}.");
                    }
                    if (line.NetWeightKg == null || !SupplyRules.IsPositiveDecimal18_3(line.NetWeightKg.Value))
                    {
                        ValidationErrors.Add(errors, $"{key}.netWeightKg", "The actual net weight in kg is required, must be positive and may have at most 3 decimals; it is never derived from expected supply.");
                    }
                    var lot = ValidationErrors.RequiredText(line.LotNumber, ReservationRules.MaxLotNumberLength, $"{key}.lotNumber", errors);
                    var reason = ValidationErrors.OptionalText(line.LotDeviationReason, ReservationRules.MaxReasonLength, $"{key}.lotDeviationReason", errors);
                    if (errors.Count > before)
                    {
                        continue;
                    }
                    if (!seen.Add(line.StockReservationId!.Value))
                    {
                        ValidationErrors.Add(errors, key, "The same allocation occurs more than once.");
                        continue;
                    }
                    lines.Add(new LineCommand(line.StockReservationId.Value, line.ExpectedReservationVersion!.Value, line.Quantity!.Value, line.Unit, line.NetWeightKg!.Value, lot, reason));
                }
            }
            if (errors.Count > 0)
            {
                return null;
            }
            return new ShipCommand(request.RequestId!.Value, salesOrderId, request.ExpectedOrderVersion!.Value, actor, request.ShippedOn!.Value, reference, lines);
        }

        // Alleen Active DirectTrade-toewijzingen van dezelfde order en klant, met de verwachte versie en precies de toegewezen hoeveelheid en eenheid.
        private static string FindConflict(SalesOrder order, StockReservation reservation, LineCommand line)
        {
            if (reservation.Source != ReservationRules.SourceDirectTrade)
            {
                return $"Reservation {reservation.Id} is warehouse stock ({reservation.Source}); warehouse reservations are dispatched with the warehouse dispatch, never as a DirectTrade shipment.";
            }
            if (reservation.Status == ReservationRules.StatusConsumed)
            {
                return $"DirectTrade allocation {reservation.Id} has already been shipped.";
            }
            if (reservation.Status != ReservationRules.StatusActive)
            {
                return $"DirectTrade allocation {reservation.Id} is {reservation.Status} and cannot be shipped; only active allocations can.";
            }
            if (reservation.Version != line.ExpectedVersion)
            {
                return $"DirectTrade allocation {reservation.Id} is at version {reservation.Version}, not {line.ExpectedVersion}; reload it and decide again.";
            }
            var commitment = reservation.SupplyCommitmentLine?.SupplyCommitment;
            if (commitment == null || commitment.Route != SupplyRules.RouteDirectTrade || commitment.CustomerId != order.CustomerId)
            {
                return $"DirectTrade allocation {reservation.Id} is not customer-bound supply of customer {order.CustomerId}.";
            }
            if (!string.Equals(line.Unit, reservation.Unit, StringComparison.Ordinal))
            {
                return $"Unit {line.Unit} must exactly match the allocation unit {reservation.Unit}; units are never converted.";
            }
            if (line.Quantity != reservation.Quantity)
            {
                return line.Quantity < reservation.Quantity
                    ? $"Only whole allocations can be shipped: allocation {reservation.Id} is {reservation.Quantity} {reservation.Unit}, the shipment says {line.Quantity}. If the supplier shipped less, release this allocation, allocate the shipped quantity and ship that."
                    : $"Only whole allocations can be shipped: allocation {reservation.Id} is {reservation.Quantity} {reservation.Unit}, the shipment says {line.Quantity}. If the supplier shipped more, first allocate the extra quantity (within supply capacity and order demand) and ship both allocations.";
            }
            return null;
        }

        // Replay: dezelfde requestId voor dezelfde order, actor, referentie, datum, orderversie en exact dezelfde regels (versie, hoeveelheid,
        // eenheid, kg, partij en, bij afwijking van de bronpartij, de toelichting).
        private async Task<ServiceResult<DirectTradeShipmentDto>> ReplayAsync(ShipCommand command, CancellationToken cancellationToken)
        {
            var existing = await Query().SingleOrDefaultAsync(d => d.RequestId == command.RequestId, cancellationToken);
            if (existing == null)
            {
                return null;
            }
            var audit = await _db.SalesOrderAuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(e => e.RequestId == command.RequestId && e.EventType == ReservationRules.DirectTradeShippedEvent, cancellationToken);
            var existingLines = existing.Lines.ToDictionary(l => l.StockReservationId);
            var sameLines = existingLines.Count == command.Lines.Count && command.Lines.All(c =>
                existingLines.TryGetValue(c.ReservationId, out var l)
                && l.ExpectedReservationVersion == c.ExpectedVersion
                && l.Quantity == c.Quantity
                && string.Equals(l.Unit, c.Unit, StringComparison.Ordinal)
                && l.NetWeightKg == c.NetWeightKg
                && string.Equals(l.LotNumber, c.LotNumber, StringComparison.Ordinal)
                && string.Equals(l.LotDeviationReason,
                    l.SourceLotNumber != null && !string.Equals(c.LotNumber, l.SourceLotNumber, StringComparison.Ordinal) ? c.LotDeviationReason : null,
                    StringComparison.Ordinal));
            var same = existing.SalesOrderId == command.SalesOrderId
                && string.Equals(existing.ActorReference, command.Actor, StringComparison.Ordinal)
                && string.Equals(existing.Reference, command.Reference, StringComparison.Ordinal)
                && existing.ShippedOn == command.ShippedOn
                && audit != null
                && RecordedVersion(audit.OldValue) == command.ExpectedOrderVersion
                && sameLines;
            return same
                ? ServiceResult<DirectTradeShipmentDto>.Ok(ToDto(existing))
                : ServiceResult<DirectTradeShipmentDto>.Conflict($"RequestId {command.RequestId} was already used for a different DirectTrade shipment.");
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

        public async Task<ServiceResult<IReadOnlyList<DirectTradeShipmentDto>>> ListAsync(int salesOrderId, CancellationToken cancellationToken = default)
        {
            if (!await _db.SalesOrders.AnyAsync(o => o.Id == salesOrderId, cancellationToken))
            {
                return ServiceResult<IReadOnlyList<DirectTradeShipmentDto>>.NotFound($"Sales order {salesOrderId} does not exist.");
            }
            var shipments = await Query().Where(d => d.SalesOrderId == salesOrderId).OrderBy(d => d.ShippedOn).ThenBy(d => d.Id).ToListAsync(cancellationToken);
            return ServiceResult<IReadOnlyList<DirectTradeShipmentDto>>.Ok(shipments.Select(ToDto).ToList());
        }

        private IQueryable<DirectTradeShipment> Query() =>
            _db.DirectTradeShipments.AsNoTracking()
                .Include(d => d.Lines).ThenInclude(l => l.SalesOrderLine).ThenInclude(s => s.ProductVariant).ThenInclude(v => v.Product)
                .Include(d => d.Correction).ThenInclude(c => c.Lines)
                .AsSplitQuery();

        internal static DirectTradeShipmentDto ToDto(DirectTradeShipment shipment) => new()
        {
            Id = shipment.Id,
            RequestId = shipment.RequestId,
            SalesOrderId = shipment.SalesOrderId,
            CustomerId = shipment.CustomerId,
            CustomerName = shipment.CustomerNameSnapshot,
            ShippedOn = shipment.ShippedOn,
            Reference = shipment.Reference,
            ActorReference = shipment.ActorReference,
            RecordedAt = DateTime.SpecifyKind(shipment.RecordedAt, DateTimeKind.Utc),
            Correction = shipment.Correction == null ? null : DirectTradeShipmentCorrectionService.ToDto(shipment.Correction),
            Lines = shipment.Lines.OrderBy(l => l.Id).Select(l => new DirectTradeShipmentLineDto
            {
                Id = l.Id,
                StockReservationId = l.StockReservationId,
                SalesOrderLineId = l.SalesOrderLineId,
                SupplyCommitmentLineId = l.SupplyCommitmentLineId,
                SupplyCommitmentId = l.SupplyCommitmentId,
                VendorId = l.VendorId,
                VendorName = l.VendorNameSnapshot,
                Origin = l.OriginSnapshot,
                Destination = l.DestinationSnapshot,
                ProductName = l.SalesOrderLine?.ProductVariant?.Product?.Name,
                ProductVariantName = l.SalesOrderLine?.ProductVariant?.Name,
                Quantity = l.Quantity,
                Unit = l.Unit,
                NetWeightKg = l.NetWeightKg,
                LotNumber = l.LotNumber,
                SourceLotNumber = l.SourceLotNumber,
                LotDeviationReason = l.LotDeviationReason
            }).ToList()
        };
    }
}
