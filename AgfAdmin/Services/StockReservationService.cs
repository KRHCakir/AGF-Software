using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public interface IStockReservationService
    {
        Task<ServiceResult<StockReservationDto>> ReserveAsync(int salesOrderId, int salesOrderLineId, CreateStockReservationDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<StockReservationDto>> ReleaseAsync(int reservationId, ReleaseStockReservationDto request, CancellationToken cancellationToken = default);
        Task<ServiceResult<IReadOnlyList<StockReservationDto>>> ReserveBatchAsync(int salesOrderId, int salesOrderLineId, CreateStockReservationBatchDto request, CancellationToken cancellationToken = default);
    }

    // Reserveren op precies één PalletContentLine (Warehouse-route, Received + Warehouse, Approved) en weer vrijgeven.
    // Hoeveelheid, eenheid en netto-kg zijn altijd expliciet; er wordt niets afgeleid of omgerekend.
    // Concurrency: reserveren raakt de inhoudsregel, de orderregel en de pallet aan (versie +1, concurrency-token);
    // vrijgeven controleert expectedReservationVersion en raakt de inhoudsregel aan. Eén save = één transactie met audit.
    public sealed class StockReservationService : IStockReservationService
    {
        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly AppDbContext _db;
        private readonly ICurrentUser _user;

        // Zonder ingelogde gebruiker (tests, seed) de standaardgebruiker; in de API altijd de gebruiker van het verzoek.
        public StockReservationService(AppDbContext db, ICurrentUser currentUser = null)
        {
            _user = currentUser ?? CurrentUser.Default;
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
            var reservedOnLine = await SalesOrderService.CommittedOnOrderLineAsync(_db, line.Id, cancellationToken);
            var conflict = FindReserveConflict(line, content, command, reservedOnContent, reservedOnLine);
            if (conflict != null)
            {
                return ServiceResult<StockReservationDto>.Conflict(conflict);
            }

            var reservation = AddReservation(line, content, command, DateTime.UtcNow);

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

        // Eén exacte reservering met audit; raakt inhoudsregel, orderregel en pallet aan (versie +1): twee gelijktijdige reserveringen op
        // dezelfde inhoud, orderregel of pallet kunnen nooit allebei slagen. Slaat niet zelf op.
        private StockReservation AddReservation(SalesOrderLine line, PalletContentLine content, ReserveCommand command, DateTime now)
        {
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
            return reservation;
        }

        // ======================== Reserveren over meerdere pallets (oudste partij eerst) ========================

        public const int MaxBatchItems = 50;

        // Elk item krijgt een eigen, vaste RequestId afgeleid van de batch-RequestId en zijn plaats: een herhaling vindt dezelfde reserveringen terug.
        internal static Guid ItemRequestId(Guid batchRequestId, int index)
        {
            var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"{batchRequestId:N}/{index}"));
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes);
        }

        // Alles of niets: elk item is een gewone exacte reservering (zelfde controles als ReserveAsync, opgeteld binnen de batch), in één
        // transactie. Gelijktijdige schrijvers botsen op de versie van inhoudsregel, orderregel of pallet: precies één wint, de ander krijgt 409.
        public async Task<ServiceResult<IReadOnlyList<StockReservationDto>>> ReserveBatchAsync(int salesOrderId, int salesOrderLineId, CreateStockReservationBatchDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var commands = BuildBatch(salesOrderId, salesOrderLineId, request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<IReadOnlyList<StockReservationDto>>.Validation(ValidationErrors.ToArrays(errors));
            }

            var replay = await ReplayBatchAsync(request.RequestId!.Value, commands, cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var line = await _db.SalesOrderLines.Include(l => l.SalesOrder)
                .SingleOrDefaultAsync(l => l.Id == salesOrderLineId && l.SalesOrderId == salesOrderId, cancellationToken);
            if (line == null)
            {
                return ServiceResult<IReadOnlyList<StockReservationDto>>.NotFound($"Sales order line {salesOrderLineId} does not exist on sales order {salesOrderId}.");
            }
            var contentIds = commands.Select(c => c.PalletContentLineId).ToList();
            var contents = await _db.PalletContentLines
                .Include(c => c.Pallet)
                .Include(c => c.SupplyCommitmentLine).ThenInclude(l => l.SupplyCommitment)
                .Where(c => contentIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken);
            var missing = contentIds.FirstOrDefault(id => !contents.ContainsKey(id));
            if (missing != 0)
            {
                return ServiceResult<IReadOnlyList<StockReservationDto>>.NotFound($"Pallet content line {missing} does not exist.");
            }

            var reservedOnContent = await SalesOrderService.ReservedByContentLineAsync(_db, contentIds, cancellationToken);
            var reservedOnLine = await SalesOrderService.CommittedOnOrderLineAsync(_db, line.Id, cancellationToken);
            var now = DateTime.UtcNow;
            var created = new List<StockReservation>();
            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                var content = contents[command.PalletContentLineId];
                var conflict = FindReserveConflict(line, content, command, reservedOnContent.GetValueOrDefault(content.Id), reservedOnLine);
                if (conflict != null)
                {
                    _db.ChangeTracker.Clear();
                    return ServiceResult<IReadOnlyList<StockReservationDto>>.Conflict($"items[{i}]: {conflict}");
                }
                created.Add(AddReservation(line, content, command, now));
                reservedOnLine += command.Quantity;
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                _db.ChangeTracker.Clear();
                var concurrent = await ReplayBatchAsync(request.RequestId!.Value, commands, cancellationToken);
                if (concurrent != null)
                {
                    return concurrent;
                }
                if (exception is DbUpdateConcurrencyException)
                {
                    return ServiceResult<IReadOnlyList<StockReservationDto>>.Conflict("The stock or order line was changed concurrently; reload the proposal and try again with a new requestId.");
                }
                throw;
            }

            var dtos = new List<StockReservationDto>();
            foreach (var reservation in created)
            {
                dtos.Add(await LoadDtoAsync(reservation.Id, cancellationToken));
            }
            return ServiceResult<IReadOnlyList<StockReservationDto>>.Created(dtos);
        }

        private List<ReserveCommand> BuildBatch(int salesOrderId, int salesOrderLineId, CreateStockReservationBatchDto request, Dictionary<string, List<string>> errors)
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
            if (!SupplyRules.IsPackageUnit(request.Unit))
            {
                ValidationErrors.Add(errors, "unit", $"Unit must be one of: {string.Join(", ", SupplyRules.PackageUnits)}.");
            }
            var actor = _user.Actor(ReservationRules.MaxActorLength);
            var reason = ValidationErrors.OptionalText(request.Reason, ReservationRules.MaxReasonLength, "reason", errors);
            if (request.Items == null || request.Items.Count == 0)
            {
                ValidationErrors.Add(errors, "items", "At least one pallet content line is required.");
            }
            else if (request.Items.Count > MaxBatchItems)
            {
                ValidationErrors.Add(errors, "items", $"At most {MaxBatchItems} pallet content lines are allowed.");
            }
            else
            {
                var seen = new HashSet<int>();
                for (var i = 0; i < request.Items.Count; i++)
                {
                    var item = request.Items[i];
                    if (item == null)
                    {
                        ValidationErrors.Add(errors, $"items[{i}]", "An item is required.");
                        continue;
                    }
                    if (!seen.Add(item.PalletContentLineId))
                    {
                        ValidationErrors.Add(errors, $"items[{i}].palletContentLineId", "Each pallet content line occurs at most once.");
                    }
                    if (!SupplyRules.IsPositiveDecimal18_3(item.Quantity))
                    {
                        ValidationErrors.Add(errors, $"items[{i}].quantity", "Quantity must be positive and may have at most 3 decimals.");
                    }
                    if (!SupplyRules.IsPositiveDecimal18_3(item.NetWeightKg))
                    {
                        ValidationErrors.Add(errors, $"items[{i}].netWeightKg", "Net weight in kg must be positive and may have at most 3 decimals; it is never derived.");
                    }
                }
            }
            if (errors.Count > 0)
            {
                return null;
            }
            return request.Items.Select((item, index) => new ReserveCommand(ItemRequestId(request.RequestId!.Value, index), salesOrderId, salesOrderLineId,
                item.PalletContentLineId, item.Quantity, request.Unit, item.NetWeightKg, actor, reason)).ToList();
        }

        // Replay: bestaat de eerste afgeleide RequestId, dan moet de hele batch exact overeenkomen (atomair opgeslagen, dus nooit half).
        private async Task<ServiceResult<IReadOnlyList<StockReservationDto>>> ReplayBatchAsync(Guid batchRequestId, List<ReserveCommand> commands, CancellationToken cancellationToken)
        {
            if (!await _db.StockReservations.AsNoTracking().AnyAsync(r => r.RequestId == commands[0].RequestId, cancellationToken))
            {
                return null;
            }
            var dtos = new List<StockReservationDto>();
            foreach (var command in commands)
            {
                var single = await ReplayReserveAsync(command, cancellationToken);
                if (single == null || single.Status != ServiceResultStatus.Ok)
                {
                    return ServiceResult<IReadOnlyList<StockReservationDto>>.Conflict("This requestId was already used for a different set of reservations.");
                }
                dtos.Add(single.Value);
            }
            // Een eerdere batch met meer items onder dezelfde sleutel is ook een andere opdracht.
            var derivedNext = ItemRequestId(batchRequestId, commands.Count);
            if (await _db.StockReservations.AsNoTracking().AnyAsync(r => r.RequestId == derivedNext, cancellationToken))
            {
                return ServiceResult<IReadOnlyList<StockReservationDto>>.Conflict("This requestId was already used for a different set of reservations.");
            }
            return ServiceResult<IReadOnlyList<StockReservationDto>>.Ok(dtos);
        }

        private ReserveCommand BuildReserve(int salesOrderId, int salesOrderLineId, CreateStockReservationDto request, Dictionary<string, List<string>> errors)
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
            var actor = _user.Actor(ReservationRules.MaxActorLength);
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
            var remainingQuantity = ReservationRules.OnHandQuantity(content.Quantity, content.DispatchedQuantity, content.SplitOffQuantity) - reservedOnContent.Quantity;
            var remainingKg = ReservationRules.OnHandQuantity(content.NetWeightKg, content.DispatchedNetWeightKg, content.SplitOffNetWeightKg) - reservedOnContent.NetWeightKg;
            if (command.Quantity > remainingQuantity || command.NetWeightKg > remainingKg)
            {
                return $"Only {remainingQuantity} {content.Unit} and {remainingKg} kg of this content line are still unreserved.";
            }

            // Berekend gewicht: een deelreservering weegt round(aantal × kg per verpakking); de volledige rest neemt exact de
            // resterende kg, zodat afronding nooit een rest onverkoopbaar maakt. Er wordt nooit een ander kg-getal geaccepteerd.
            // Gewogen inhoud houdt de expliciete kg-invoer. (Hier kan geen overflow optreden: aantal ≤ resterend ≤ regeltotaal.)
            if (content.WeightMethod == WeightRules.Calculated && content.NetKgPerPackage != null)
            {
                if (!WeightRules.TryExpectedReservationKg(command.Quantity, content.NetKgPerPackage.Value, remainingQuantity, remainingKg, out var expectedKg))
                {
                    return "Quantity × kg per package is out of range for this reservation.";
                }
                if (command.NetWeightKg != expectedKg)
                {
                    return command.Quantity == remainingQuantity
                        ? $"This content line has a calculated weight: the full remainder of {remainingQuantity} {content.Unit} is exactly the remaining {remainingKg} kg, not {command.NetWeightKg} kg."
                        : $"This content line has a calculated weight of {content.NetKgPerPackage} kg per {content.Unit}: {command.Quantity} {content.Unit} is {expectedKg} kg, not {command.NetWeightKg} kg.";
                }
            }
            // Geen gestrande rest: de volledige resterende hoeveelheid hoort bij het volledige resterende kg (en omgekeerd).
            if ((command.Quantity == remainingQuantity) != (command.NetWeightKg == remainingKg))
            {
                return $"Reserving the full remainder requires both the remaining quantity ({remainingQuantity}) and the remaining net weight ({remainingKg} kg).";
            }
            // Vastgelegd op de regel = Active (Physical + DirectTrade) + Consumed; verzonden komt nooit terug als open vraag.
            if (reservedOnLine + command.Quantity > line.Quantity)
            {
                return $"The sales order line still needs only {line.Quantity - reservedOnLine} {line.Unit}.";
            }
            return null;
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
                var actor = _user.Actor(ReservationRules.MaxActorLength);
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

            var reservation = await _db.StockReservations
                .Include(r => r.PalletContentLine)
                .Include(r => r.SupplyCommitmentLine)
                .Include(r => r.SalesOrderLine)
                .SingleOrDefaultAsync(r => r.Id == reservationId, cancellationToken);
            if (reservation == null)
            {
                return ServiceResult<StockReservationDto>.NotFound($"Reservation {reservationId} does not exist.");
            }
            var isAllocation = reservation.Source == ReservationRules.SourceDirectTrade;
            if (reservation.SalesOrderLineId == null || reservation.SalesOrderLine == null || (!isAllocation && reservation.PalletContentLine == null) || (isAllocation && reservation.SupplyCommitmentLine == null))
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

            var oldValue = JsonSerializer.Serialize(new { reservationId = reservation.Id, status = ReservationRules.StatusActive, version = command.ExpectedVersion }, SnapshotJson);
            if (isAllocation)
            {
                // Toewijzing van verwachte aanvoer: de bronregel wordt aangeraakt (botst met gelijktijdig toewijzen) en het event hoort bij de order,
                // niet bij een pallet (er is geen pallet).
                _db.Entry(reservation.SupplyCommitmentLine).Property(l => l.Version).IsModified = true;
                _db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
                {
                    SalesOrderId = reservation.SalesOrderLine.SalesOrderId,
                    EventType = ReservationRules.DirectTradeReleasedEvent,
                    OldValue = oldValue,
                    NewValue = JsonSerializer.Serialize(new
                    {
                        reservationId = reservation.Id,
                        status = ReservationRules.StatusReleased,
                        salesOrderLineId = reservation.SalesOrderLineId,
                        supplyCommitmentLineId = reservation.SupplyCommitmentLineId,
                        quantity = reservation.Quantity,
                        unit = reservation.Unit
                    }, SnapshotJson),
                    Reason = command.Reason,
                    ActorReference = command.Actor,
                    OccurredAt = now,
                    RequestId = command.RequestId
                });
            }
            else
            {
                _db.Entry(reservation.PalletContentLine).Property(c => c.Version).IsModified = true;
                _db.PalletAuditEvents.Add(new PalletAuditEvent
                {
                    PalletId = reservation.PalletContentLine.PalletId,
                    PalletContentLineId = reservation.PalletContentLine.Id,
                    EventType = ReservationRules.ReservationReleasedEvent,
                    OldValue = oldValue,
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
            }

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
            var (actor, reason, oldValue) = existing.Source == ReservationRules.SourceDirectTrade
                ? await (_db.SalesOrderAuditEvents.AsNoTracking()
                    .Where(e => e.RequestId == command.RequestId && e.EventType == ReservationRules.DirectTradeReleasedEvent)
                    .Select(e => new ValueTuple<string, string, string>(e.ActorReference, e.Reason, e.OldValue))
                    .SingleOrDefaultAsync(cancellationToken))
                : await (_db.PalletAuditEvents.AsNoTracking()
                    .Where(e => e.RequestId == command.RequestId && e.EventType == ReservationRules.ReservationReleasedEvent)
                    .Select(e => new ValueTuple<string, string, string>(e.ActorReference, e.Reason, e.OldValue))
                    .SingleOrDefaultAsync(cancellationToken));
            var same = existing.Id == reservationId
                && oldValue != null
                && string.Equals(actor, command.Actor, StringComparison.Ordinal)
                && string.Equals(reason, command.Reason, StringComparison.Ordinal)
                && RecordedVersion(oldValue) == command.ExpectedVersion;
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

        private Task<StockReservationDto> LoadDtoAsync(int reservationId, CancellationToken cancellationToken) => LoadDtoAsync(_db, reservationId, cancellationToken);

        internal static async Task<StockReservationDto> LoadDtoAsync(AppDbContext db, int reservationId, CancellationToken cancellationToken)
        {
            var reservation = await db.StockReservations.AsNoTracking()
                .Include(r => r.SalesOrderLine)
                .Include(r => r.PalletContentLine).ThenInclude(c => c.Pallet)
                .Include(r => r.SupplyCommitmentLine).ThenInclude(s => s.SupplyCommitment).ThenInclude(c => c.Vendor)
                .Include(r => r.DispatchLine).ThenInclude(l => l.SalesDispatch).ThenInclude(d => d.DeliveryConfirmations).ThenInclude(c => c.Retraction)
                .Include(r => r.DirectTradeShipmentLine).ThenInclude(l => l.DirectTradeShipment).ThenInclude(s => s.DeliveryConfirmations).ThenInclude(c => c.Retraction)
                .Include(r => r.DirectTradeCorrectionLine)
                .SingleAsync(r => r.Id == reservationId, cancellationToken);
            return ToDto(reservation, reservation.SalesOrderLine?.SalesOrderId ?? 0);
        }

        // De afleverbevestiging van de verzending van deze reservering (magazijn of DirectTrade), als die geladen is.
        // De ACTIEVE bevestiging van de verzending die deze reservering verbruikte (DeliveryState), of null. Ingetrokken bevestigingen tellen nooit.
        internal static ShipmentDeliveryConfirmation DeliveryOf(StockReservation reservation) =>
            DeliveryState.ActiveOf(reservation.DispatchLine?.SalesDispatch?.DeliveryConfirmations)
            ?? DeliveryState.ActiveOf(reservation.DirectTradeShipmentLine?.DirectTradeShipment?.DeliveryConfirmations);

        // Physical: pallet, partij en gemeten kg van de inhoudsregel. DirectTrade: geen pallet en geen kg (null), partij en bron van de toezegging.
        internal static StockReservationDto ToDto(StockReservation reservation, int salesOrderId, int? correctionId = null) => new()
        {
            Id = reservation.Id,
            RequestId = reservation.RequestId,
            SalesOrderId = salesOrderId,
            SalesOrderLineId = reservation.SalesOrderLineId ?? 0,
            PalletContentLineId = reservation.PalletContentLineId,
            PalletId = reservation.PalletContentLine?.PalletId,
            PalletReference = reservation.PalletContentLine?.Pallet?.Reference,
            WarehouseLocation = reservation.PalletContentLine?.Pallet?.WarehouseLocation,
            LotNumber = reservation.PalletContentLine?.LotNumber ?? reservation.SupplyCommitmentLine?.LotNumber,
            PartijId = reservation.PalletContentLine?.PartijId ?? (reservation.PalletContentLineId == null ? reservation.SupplyCommitmentLine?.PartijId : null),
            PartijNumber = reservation.PalletContentLine?.Partij?.Number ?? (reservation.PalletContentLineId == null ? reservation.SupplyCommitmentLine?.Partij?.Number : null),
            Quantity = reservation.Quantity,
            Unit = reservation.Unit,
            NetWeightKg = reservation.NetWeightKg,
            Source = reservation.Source,
            SupplyCommitmentLineId = reservation.SupplyCommitmentLineId,
            SupplyCommitmentId = reservation.SupplyCommitmentLine?.SupplyCommitmentId ?? 0,
            SupplyOrderNumber = reservation.SupplyCommitmentLine?.SupplyCommitment?.OrderNumber,
            VendorName = reservation.SupplyCommitmentLine?.SupplyCommitment?.Vendor?.Name,
            Origin = reservation.SupplyCommitmentLine?.SupplyCommitment?.Origin,
            Destination = reservation.SupplyCommitmentLine?.SupplyCommitment?.Destination,
            ExpectedAt = reservation.SupplyCommitmentLine?.SupplyCommitment?.ExpectedAt == null ? null : DateTime.SpecifyKind(reservation.SupplyCommitmentLine.SupplyCommitment.ExpectedAt.Value, DateTimeKind.Utc),
            DirectTradeShipmentId = reservation.DirectTradeShipmentLine?.DirectTradeShipmentId,
            ShippedOn = reservation.DirectTradeShipmentLine?.DirectTradeShipment?.ShippedOn,
            ShippedNetWeightKg = reservation.DirectTradeShipmentLine?.NetWeightKg,
            ShippedLotNumber = reservation.DirectTradeShipmentLine?.LotNumber,
            DirectTradeCorrectionId = reservation.DirectTradeCorrectionLine?.DirectTradeShipmentCorrectionId,
            DeliveryConfirmationId = reservation.Status == ReservationRules.StatusConsumed ? DeliveryOf(reservation)?.Id : null,
            DeliveredOn = reservation.Status == ReservationRules.StatusConsumed ? DeliveryOf(reservation)?.DeliveredOn : null,
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
            DispatchReference = reservation.DispatchLine?.SalesDispatch?.Reference,
            CorrectedAt = reservation.CorrectedAt == null ? null : DateTime.SpecifyKind(reservation.CorrectedAt.Value, DateTimeKind.Utc),
            CorrectionId = correctionId
        };
    }
}
