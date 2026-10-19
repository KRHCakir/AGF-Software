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
                : ServiceResult<SalesOrderDto>.Ok(ToDto(order));
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
            return ServiceResult<IReadOnlyList<SalesOrderDto>>.Ok(orders.Select(ToDto).ToList());
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
                    c.Quantity, c.NetWeightKg, c.QualityStatus, c.Version, PalletCreatedAt = c.Pallet.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var reserved = await ReservedByContentLineAsync(_db, contents.Select(c => c.Id).ToList(), cancellationToken);
            var result = contents
                .Select(c =>
                {
                    var (quantity, kg) = reserved.GetValueOrDefault(c.Id);
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
                        ReservedQuantity = quantity,
                        ReservedNetWeightKg = kg,
                        AvailableQuantity = Math.Max(0m, c.Quantity - quantity),
                        AvailableNetWeightKg = Math.Max(0m, c.NetWeightKg - kg),
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

        // Som (in het geheugen, exact decimal) van Active verkoopreserveringen per inhoudsregel.
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
                .AsSplitQuery();

        internal static SalesOrderDto ToDto(SalesOrder order) => new()
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
            Lines = order.Lines.OrderBy(l => l.Id).Select(line =>
            {
                var active = line.Reservations.Where(r => r.Status == ReservationRules.StatusActive).ToList();
                var reservedQuantity = active.Sum(r => r.Quantity);
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
                    OpenQuantity = Math.Max(0m, line.Quantity - reservedQuantity),
                    Version = line.Version,
                    Reservations = line.Reservations.OrderBy(r => r.Id).Select(r => StockReservationService.ToDto(r, order.Id)).ToList()
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
