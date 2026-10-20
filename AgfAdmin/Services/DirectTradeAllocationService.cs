using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public interface IDirectTradeAllocationService
    {
        Task<ServiceResult<IReadOnlyList<DirectTradeSupplyLineDto>>> GetAllocatableSupplyAsync(int salesOrderId, int salesOrderLineId, CancellationToken cancellationToken = default);
        Task<ServiceResult<StockReservationDto>> AllocateAsync(int salesOrderId, int salesOrderLineId, CreateDirectTradeAllocationDto request, CancellationToken cancellationToken = default);
    }

    // Toewijzen van klantgebonden, verwachte DirectTrade-aanvoer aan een orderregel van dezelfde klant (leverancier/herkomst -> klant,
    // zonder ons magazijn). Een toewijzing is een StockReservation met Source DirectTrade op de bronregel, zonder inhoudsregel en zonder
    // kg: het is een belofte op verwachte aanvoer, geen voorraad en geen verzending. Er wordt geen voorraadmutatie geschreven en de
    // magazijnbeschikbaarheid verandert niet. Hoeveelheid en eenheid zijn expliciet en worden nooit omgerekend.
    // Concurrency: de bronregel en de orderregel worden aangeraakt (versie +1); twee gelijktijdige toewijzingen op dezelfde bronregel
    // kunnen nooit allebei slagen. Eén save = één transactie met één orderauditrecord (RequestId = commando).
    public sealed class DirectTradeAllocationService : IDirectTradeAllocationService
    {
        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly AppDbContext _db;

        public DirectTradeAllocationService(AppDbContext db)
        {
            _db = db;
        }

        // ======================== Toewijsbare aanvoer ========================

        public async Task<ServiceResult<IReadOnlyList<DirectTradeSupplyLineDto>>> GetAllocatableSupplyAsync(int salesOrderId, int salesOrderLineId, CancellationToken cancellationToken = default)
        {
            var line = await _db.SalesOrderLines.AsNoTracking().Include(l => l.SalesOrder)
                .SingleOrDefaultAsync(l => l.Id == salesOrderLineId && l.SalesOrderId == salesOrderId, cancellationToken);
            if (line == null)
            {
                return ServiceResult<IReadOnlyList<DirectTradeSupplyLineDto>>.NotFound($"Sales order line {salesOrderLineId} does not exist on sales order {salesOrderId}.");
            }

            var allocatable = SupplyRules.AllocatableCommitmentStatuses.ToList();
            var sources = await _db.SupplyCommitmentLines.AsNoTracking()
                .Where(s => s.SupplyCommitment.Route == SupplyRules.RouteDirectTrade
                    && s.SupplyCommitment.CustomerId == line.SalesOrder.CustomerId
                    && allocatable.Contains(s.SupplyCommitment.Status)
                    && s.ProductVariantId == line.ProductVariantId
                    && s.ExpectedUnit == line.Unit)
                .Select(s => new
                {
                    s.Id, s.SupplyCommitmentId, s.SupplyCommitment.CustomerId, CustomerName = s.SupplyCommitment.Customer.Name,
                    VendorName = s.SupplyCommitment.Vendor.Name, s.SupplyCommitment.Origin, s.SupplyCommitment.Destination,
                    s.SupplyCommitment.Status, s.SupplyCommitment.ExpectedAt, s.LotNumber, s.ExpectedUnit, s.ExpectedQuantity, s.ExpectedNetWeightKg, s.Version
                })
                .ToListAsync(cancellationToken);

            var ids = sources.Select(s => s.Id).ToList();
            var allocated = await AllocatedBySourceLineAsync(_db, ids, cancellationToken);
            var allocatedHere = await AllocatedBySourceLineAsync(_db, ids, cancellationToken, salesOrderLineId);
            var result = sources
                .Select(s =>
                {
                    var total = allocated.GetValueOrDefault(s.Id);
                    return new DirectTradeSupplyLineDto
                    {
                        SupplyCommitmentLineId = s.Id,
                        SupplyCommitmentId = s.SupplyCommitmentId,
                        CustomerId = s.CustomerId ?? 0,
                        CustomerName = s.CustomerName,
                        VendorName = s.VendorName,
                        Origin = s.Origin,
                        Destination = s.Destination,
                        CommitmentStatus = s.Status,
                        ExpectedAt = s.ExpectedAt == null ? null : DateTime.SpecifyKind(s.ExpectedAt.Value, DateTimeKind.Utc),
                        LotNumber = s.LotNumber,
                        Unit = s.ExpectedUnit,
                        ExpectedQuantity = s.ExpectedQuantity,
                        DeclaredExpectedNetWeightKg = s.ExpectedNetWeightKg,
                        AllocatedQuantity = total,
                        RemainingQuantity = Remaining(s.ExpectedQuantity, total),
                        AllocatedToThisLineQuantity = allocatedHere.GetValueOrDefault(s.Id),
                        SourceLineVersion = s.Version
                    };
                })
                .Where(s => s.RemainingQuantity > 0m)
                .OrderBy(s => s.ExpectedAt)
                .ThenBy(s => s.SupplyCommitmentLineId)
                .ToList();
            return ServiceResult<IReadOnlyList<DirectTradeSupplyLineDto>>.Ok(result);
        }

        // Resterend = verwacht - toegewezen; nooit negatief.
        public static decimal Remaining(decimal expectedQuantity, decimal allocatedQuantity) => Math.Max(0m, expectedQuantity - allocatedQuantity);

        // Som (in het geheugen, exact decimal) van Active DirectTrade-toewijzingen per bronregel, over alle verkooporders
        // (of alleen die van één orderregel). Geregistreerde palletinhoud tegen de bronregel verandert hier niets aan.
        internal static async Task<Dictionary<int, decimal>> AllocatedBySourceLineAsync(AppDbContext db, List<int> sourceLineIds, CancellationToken cancellationToken, int? salesOrderLineId = null)
        {
            if (sourceLineIds.Count == 0)
            {
                return new Dictionary<int, decimal>();
            }
            var query = db.StockReservations.AsNoTracking()
                .Where(r => sourceLineIds.Contains(r.SupplyCommitmentLineId)
                    && r.SalesOrderLineId != null && r.Source == ReservationRules.SourceDirectTrade && r.Status == ReservationRules.StatusActive);
            if (salesOrderLineId != null)
            {
                query = query.Where(r => r.SalesOrderLineId == salesOrderLineId);
            }
            var rows = await query.Select(r => new { r.SupplyCommitmentLineId, r.Quantity }).ToListAsync(cancellationToken);
            return rows.GroupBy(r => r.SupplyCommitmentLineId).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
        }

        // ======================== Toewijzen ========================

        private sealed record AllocateCommand(Guid RequestId, int SalesOrderId, int SalesOrderLineId, int SupplyCommitmentLineId, decimal Quantity, string Unit, string Actor, string Reason);

        public async Task<ServiceResult<StockReservationDto>> AllocateAsync(int salesOrderId, int salesOrderLineId, CreateDirectTradeAllocationDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var command = Build(salesOrderId, salesOrderLineId, request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<StockReservationDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var replay = await ReplayAsync(command, cancellationToken);
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
            var source = await _db.SupplyCommitmentLines
                .Include(s => s.SupplyCommitment)
                .SingleOrDefaultAsync(s => s.Id == command.SupplyCommitmentLineId, cancellationToken);
            if (source == null)
            {
                return ServiceResult<StockReservationDto>.NotFound($"Supply commitment line {command.SupplyCommitmentLineId} does not exist.");
            }

            var allocated = (await AllocatedBySourceLineAsync(_db, new List<int> { source.Id }, cancellationToken)).GetValueOrDefault(source.Id);
            var committedOnLine = await SalesOrderService.CommittedOnOrderLineAsync(_db, line.Id, cancellationToken);
            var conflict = FindAllocateConflict(line, source, command, allocated, committedOnLine);
            if (conflict != null)
            {
                return ServiceResult<StockReservationDto>.Conflict(conflict);
            }

            var now = DateTime.UtcNow;
            var reservation = new StockReservation
            {
                RequestId = command.RequestId,
                SalesOrderLineId = line.Id,
                SupplyCommitmentLineId = source.Id,
                PalletContentLineId = null,
                Quantity = command.Quantity,
                Unit = command.Unit,
                NetWeightKg = null,
                Source = ReservationRules.SourceDirectTrade,
                Status = ReservationRules.StatusActive,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.StockReservations.Add(reservation);

            // Aanraken (versie +1): twee gelijktijdige toewijzingen op dezelfde bronregel of orderregel kunnen nooit allebei slagen;
            // botst ook met gelijktijdig annuleren, verzenden en reserveren op die regel.
            _db.Entry(source).Property(s => s.Version).IsModified = true;
            _db.Entry(line).Property(l => l.Version).IsModified = true;

            _db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent
            {
                SalesOrderId = line.SalesOrderId,
                EventType = ReservationRules.DirectTradeAllocatedEvent,
                NewValue = JsonSerializer.Serialize(new
                {
                    salesOrderLineId = line.Id,
                    supplyCommitmentLineId = source.Id,
                    supplyCommitmentId = source.SupplyCommitmentId,
                    customerId = source.SupplyCommitment.CustomerId,
                    quantity = command.Quantity,
                    unit = command.Unit,
                    expectedAt = source.SupplyCommitment.ExpectedAt,
                    expectedSupplyAcknowledgedByUser = true,
                    remainingAfter = Remaining(source.ExpectedQuantity, allocated + command.Quantity)
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
                return ServiceResult<StockReservationDto>.Conflict("The supply line or order line was changed concurrently; reload and try again with a new requestId.");
            }

            return ServiceResult<StockReservationDto>.Created(await StockReservationService.LoadDtoAsync(_db, reservation.Id, cancellationToken));
        }

        private static AllocateCommand Build(int salesOrderId, int salesOrderLineId, CreateDirectTradeAllocationDto request, Dictionary<string, List<string>> errors)
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
            if (request.SupplyCommitmentLineId == null || request.SupplyCommitmentLineId <= 0)
            {
                ValidationErrors.Add(errors, "supplyCommitmentLineId", "One exact DirectTrade supply commitment line is required.");
            }
            if (request.Quantity == null || !SupplyRules.IsPositiveDecimal18_3(request.Quantity.Value))
            {
                ValidationErrors.Add(errors, "quantity", "Quantity is required, must be positive and may have at most 3 decimals.");
            }
            if (!SupplyRules.IsPackageUnit(request.Unit))
            {
                ValidationErrors.Add(errors, "unit", $"Unit must be one of: {string.Join(", ", SupplyRules.PackageUnits)}.");
            }
            if (request.AcknowledgeExpectedSupply != true)
            {
                ValidationErrors.Add(errors, "acknowledgeExpectedSupply", "Confirm explicitly that this is expected supply: the goods are not in our warehouse and have not been shipped.");
            }
            var actor = ValidationErrors.RequiredText(request.ActorReference, ReservationRules.MaxActorLength, "actorReference", errors);
            var reason = ValidationErrors.OptionalText(request.Reason, ReservationRules.MaxReasonLength, "reason", errors);
            if (errors.Count > 0)
            {
                return null;
            }
            return new AllocateCommand(request.RequestId!.Value, salesOrderId, salesOrderLineId, request.SupplyCommitmentLineId!.Value, request.Quantity!.Value, request.Unit, actor, reason);
        }

        // Dezelfde regels als de picker en guard-regel 10: route, toelatingslijst van statussen, exact dezelfde klant, variant en eenheid.
        private static string FindAllocateConflict(SalesOrderLine line, SupplyCommitmentLine source, AllocateCommand command, decimal allocated, decimal committedOnLine)
        {
            if (line.SalesOrder.Status != ReservationRules.OrderStatusOpen)
            {
                return $"Sales order {line.SalesOrderId} is {line.SalesOrder.Status}; allocations require an open order.";
            }
            var commitment = source.SupplyCommitment;
            if (commitment.Route != SupplyRules.RouteDirectTrade)
            {
                return "Only DirectTrade supply can be allocated to a customer; Warehouse supply becomes stock after receipt and inspection and is reserved from pallets.";
            }
            if (!SupplyRules.IsAllocatableCommitmentStatus(commitment.Status))
            {
                return $"Supply commitment {commitment.Id} is {commitment.Status}; only {string.Join(" or ", SupplyRules.AllocatableCommitmentStatuses)} supply can be allocated.";
            }
            if (commitment.CustomerId == null || commitment.CustomerId != line.SalesOrder.CustomerId)
            {
                return $"Supply commitment {commitment.Id} is bound to customer {commitment.CustomerId}, not to customer {line.SalesOrder.CustomerId} of this order; DirectTrade supply is never reallocated to another customer.";
            }
            if (commitment.ExpectedAt == null)
            {
                return $"Supply commitment {commitment.Id} has no expected delivery date; expected supply can only be allocated with a visible delivery date.";
            }
            if (source.ProductVariantId != line.ProductVariantId)
            {
                return "The supply line is a different product variant than the sales order line.";
            }
            if (source.ExpectedUnit == null)
            {
                return "The supply line has no explicit unit and can never be allocated; units are never inferred.";
            }
            if (!string.Equals(command.Unit, source.ExpectedUnit, StringComparison.Ordinal) || !string.Equals(command.Unit, line.Unit, StringComparison.Ordinal))
            {
                return $"Unit {command.Unit} must exactly match the supply line unit {source.ExpectedUnit} and the order line unit {line.Unit}; units are never converted.";
            }
            var remaining = Remaining(source.ExpectedQuantity, allocated);
            if (command.Quantity > remaining)
            {
                return $"Only {remaining} {source.ExpectedUnit} of this supply line is still unallocated (expected {source.ExpectedQuantity}, allocated {allocated}).";
            }
            // Vastgelegd op de regel = Active (Physical + DirectTrade) + Consumed; verzonden komt nooit terug als open vraag.
            if (committedOnLine + command.Quantity > line.Quantity)
            {
                return $"The sales order line still needs only {line.Quantity - committedOnLine} {line.Unit}.";
            }
            return null;
        }

        // Replay: dezelfde requestId voor dezelfde order, regel, bronregel, hoeveelheid, eenheid, actor, reden en bevestiging geeft de huidige toestand terug.
        private async Task<ServiceResult<StockReservationDto>> ReplayAsync(AllocateCommand command, CancellationToken cancellationToken)
        {
            var existing = await _db.StockReservations.AsNoTracking().Include(r => r.SalesOrderLine)
                .SingleOrDefaultAsync(r => r.RequestId == command.RequestId, cancellationToken);
            if (existing == null)
            {
                return null;
            }
            var audit = await _db.SalesOrderAuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(e => e.RequestId == command.RequestId && e.EventType == ReservationRules.DirectTradeAllocatedEvent, cancellationToken);
            var same = existing.Source == ReservationRules.SourceDirectTrade
                && existing.SalesOrderLine?.SalesOrderId == command.SalesOrderId
                && existing.SalesOrderLineId == command.SalesOrderLineId
                && existing.SupplyCommitmentLineId == command.SupplyCommitmentLineId
                && existing.Quantity == command.Quantity
                && string.Equals(existing.Unit, command.Unit, StringComparison.Ordinal)
                && audit != null
                && string.Equals(audit.ActorReference, command.Actor, StringComparison.Ordinal)
                && string.Equals(audit.Reason, command.Reason, StringComparison.Ordinal)
                && RecordedAcknowledgement(audit.NewValue);
            return same
                ? ServiceResult<StockReservationDto>.Ok(await StockReservationService.LoadDtoAsync(_db, existing.Id, cancellationToken))
                : ServiceResult<StockReservationDto>.Conflict($"RequestId {command.RequestId} was already used for a different allocation or reservation.");
        }

        private static bool RecordedAcknowledgement(string json)
        {
            if (json == null) return false;
            try
            {
                using var document = JsonDocument.Parse(json);
                return document.RootElement.TryGetProperty("expectedSupplyAcknowledgedByUser", out var value) && value.ValueKind == JsonValueKind.True;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
