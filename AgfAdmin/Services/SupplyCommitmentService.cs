using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Legt verwachte aanvoer vast (Warehouse of DirectTrade). Een toezegging is nooit fysieke voorraad:
    // deze service schrijft geen ontvangsten, voorraadbewegingen, reserveringen of legacy-voorraad.
    // Hoeveelheden en gewichten worden exact zoals ingevoerd opgeslagen; niets wordt afgeleid of omgerekend.
    public sealed class SupplyCommitmentService : ISupplyCommitmentService
    {
        public const int MaxLines = 200;
        public const int MaxListResults = 200;
        private const int MaxOriginLength = 200;
        private const int MaxDestinationLength = 200;
        private const int MaxLotNumberLength = 100;

        private readonly AppDbContext _db;

        public SupplyCommitmentService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<ServiceResult<SupplyCommitmentDto>> CreateAsync(CreateSupplyCommitmentDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var candidate = BuildCandidate(request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<SupplyCommitmentDto>.Validation(
                    errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
            }

            // Idempotentie: dezelfde RequestId met dezelfde inhoud geeft de bestaande toezegging terug.
            var existing = await FindByRequestIdAsync(candidate.RequestId, cancellationToken);
            if (existing != null)
            {
                return ResultForRetry(existing, candidate);
            }

            var notFound = await FindMissingReferenceAsync(candidate, cancellationToken);
            if (notFound != null)
            {
                return ServiceResult<SupplyCommitmentDto>.NotFound(notFound);
            }

            _db.SupplyCommitments.Add(candidate);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Gelijktijdige retry: de unieke index op RequestId heeft het tweede verzoek geweigerd.
                _db.ChangeTracker.Clear();
                var concurrent = await FindByRequestIdAsync(candidate.RequestId, cancellationToken);
                if (concurrent == null)
                {
                    throw;
                }

                return ResultForRetry(concurrent, candidate);
            }

            var created = await FindByRequestIdAsync(candidate.RequestId, cancellationToken);
            return ServiceResult<SupplyCommitmentDto>.Created(ToDto(created));
        }

        public async Task<ServiceResult<SupplyCommitmentDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var commitment = await Query().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            return commitment == null
                ? ServiceResult<SupplyCommitmentDto>.NotFound($"Supply commitment {id} does not exist.")
                : ServiceResult<SupplyCommitmentDto>.Ok(ToDto(commitment));
        }

        public async Task<ServiceResult<IReadOnlyList<SupplyCommitmentDto>>> ListAsync(SupplyCommitmentFilter filter, CancellationToken cancellationToken = default)
        {
            filter ??= new SupplyCommitmentFilter();
            var errors = new Dictionary<string, List<string>>();
            if (filter.Route != null && !SupplyRules.IsRoute(filter.Route))
            {
                AddError(errors, "route", $"Route must be one of: {string.Join(", ", SupplyRules.Routes)}.");
            }
            if (filter.Status != null && !SupplyRules.IsCommitmentStatus(filter.Status))
            {
                AddError(errors, "status", $"Status must be one of: {string.Join(", ", SupplyRules.CommitmentStatuses)}.");
            }
            if (errors.Count > 0)
            {
                return ServiceResult<IReadOnlyList<SupplyCommitmentDto>>.Validation(
                    errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
            }

            var query = Query();
            if (filter.Route != null) query = query.Where(c => c.Route == filter.Route);
            if (filter.Status != null) query = query.Where(c => c.Status == filter.Status);
            if (filter.VendorId != null) query = query.Where(c => c.VendorId == filter.VendorId);
            if (filter.CustomerId != null) query = query.Where(c => c.CustomerId == filter.CustomerId);

            var commitments = await query
                .OrderBy(c => c.ExpectedAt)
                .ThenBy(c => c.Id)
                .Take(MaxListResults)
                .ToListAsync(cancellationToken);

            return ServiceResult<IReadOnlyList<SupplyCommitmentDto>>.Ok(commitments.Select(ToDto).ToList());
        }

        private static SupplyCommitment BuildCandidate(CreateSupplyCommitmentDto request, Dictionary<string, List<string>> errors)
        {
            if (request == null)
            {
                AddError(errors, "request", "A request body is required.");
                return null;
            }

            if (request.RequestId == null || request.RequestId == Guid.Empty)
            {
                AddError(errors, "requestId", "A client-generated, non-empty requestId is required.");
            }

            var routeIsValid = SupplyRules.IsRoute(request.Route);
            if (!routeIsValid)
            {
                AddError(errors, "route", $"Route must be one of: {string.Join(", ", SupplyRules.Routes)}.");
            }
            var isDirectTrade = request.Route == SupplyRules.RouteDirectTrade;

            if (routeIsValid && isDirectTrade && request.CustomerId == null)
            {
                AddError(errors, "customerId", "A DirectTrade commitment must be linked to a customer.");
            }
            if (routeIsValid && !isDirectTrade && request.CustomerId != null)
            {
                AddError(errors, "customerId", "A Warehouse commitment must not be linked to a customer.");
            }

            var origin = ValidateText(request.Origin, required: true, MaxOriginLength, "origin", errors);
            var destination = ValidateText(request.Destination, required: routeIsValid && isDirectTrade, MaxDestinationLength, "destination", errors);

            if (request.ExpectedAt == null)
            {
                AddError(errors, "expectedAt", "An expected arrival/delivery date is required.");
            }

            var lines = new List<SupplyCommitmentLine>();
            if (request.Lines == null || request.Lines.Count == 0)
            {
                AddError(errors, "lines", "At least one line is required.");
            }
            else if (request.Lines.Count > MaxLines)
            {
                AddError(errors, "lines", $"At most {MaxLines} lines are allowed.");
            }
            else
            {
                var seenVariantLots = new HashSet<(int, string)>();
                for (var i = 0; i < request.Lines.Count; i++)
                {
                    var line = BuildLine(request.Lines[i], $"lines[{i}]", errors);
                    if (line == null)
                    {
                        continue;
                    }

                    if (line.LotNumber != null && !seenVariantLots.Add((line.ProductVariantId, line.LotNumber)))
                    {
                        AddError(errors, $"lines[{i}]", "The same product variant and lot number occur more than once.");
                    }

                    lines.Add(line);
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

            return new SupplyCommitment
            {
                RequestId = request.RequestId!.Value,
                VendorId = request.VendorId,
                CustomerId = request.CustomerId,
                Route = request.Route,
                Status = SupplyRules.StatusExpected,
                Origin = origin,
                Destination = destination,
                ExpectedAt = ToStoredUtc(request.ExpectedAt!.Value),
                CreatedAt = now,
                UpdatedAt = now,
                Lines = lines
            };
        }

        private static SupplyCommitmentLine BuildLine(CreateSupplyCommitmentLineDto line, string key, Dictionary<string, List<string>> errors)
        {
            if (line == null)
            {
                AddError(errors, key, "A line is required.");
                return null;
            }

            var errorCount = errors.Count;

            if (line.ExpectedQuantity == null || !SupplyRules.IsPositiveDecimal18_3(line.ExpectedQuantity.Value))
            {
                AddError(errors, $"{key}.expectedQuantity", "Expected quantity is required, must be positive and may have at most 3 decimals.");
            }

            if (!SupplyRules.IsPackageUnit(line.ExpectedUnit))
            {
                AddError(errors, $"{key}.expectedUnit", $"Expected unit must be one of: {string.Join(", ", SupplyRules.PackageUnits)}.");
            }

            if (line.ExpectedNetWeightKg != null && !SupplyRules.IsPositiveDecimal18_3(line.ExpectedNetWeightKg.Value))
            {
                AddError(errors, $"{key}.expectedNetWeightKg", "Expected net weight, when given, must be positive and may have at most 3 decimals.");
            }

            var lotNumber = ValidateText(line.LotNumber, required: false, MaxLotNumberLength, $"{key}.lotNumber", errors);

            if (errors.Count > errorCount)
            {
                return null;
            }

            return new SupplyCommitmentLine
            {
                ProductVariantId = line.ProductVariantId,
                ExpectedQuantity = line.ExpectedQuantity!.Value,
                ExpectedUnit = line.ExpectedUnit,
                ExpectedNetWeightKg = line.ExpectedNetWeightKg,
                LotNumber = lotNumber
            };
        }

        // Ontbrekend is toegestaan als het veld optioneel is; ingevuld betekent altijd niet-leeg en binnen de maximale lengte.
        private static string ValidateText(string value, bool required, int maxLength, string key, Dictionary<string, List<string>> errors)
        {
            if (value == null)
            {
                if (required)
                {
                    AddError(errors, key, "This field is required.");
                }
                return null;
            }

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                AddError(errors, key, "This field must not be blank.");
                return null;
            }
            if (trimmed.Length > maxLength)
            {
                AddError(errors, key, $"This field may contain at most {maxLength} characters.");
                return null;
            }

            return trimmed;
        }

        // PostgreSQL timestamptz bewaart microseconden; afkappen houdt een retry na opslag exact vergelijkbaar.
        private static DateTime ToStoredUtc(DateTimeOffset value)
        {
            var utc = value.UtcDateTime;
            return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
        }

        private async Task<string> FindMissingReferenceAsync(SupplyCommitment candidate, CancellationToken cancellationToken)
        {
            if (!await _db.Vendors.AnyAsync(v => v.Id == candidate.VendorId, cancellationToken))
            {
                return $"Vendor {candidate.VendorId} does not exist.";
            }

            if (candidate.CustomerId != null && !await _db.Customers.AnyAsync(c => c.Id == candidate.CustomerId, cancellationToken))
            {
                return $"Customer {candidate.CustomerId} does not exist.";
            }

            var variantIds = candidate.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
            var existingVariantIds = await _db.ProductVariants
                .Where(v => variantIds.Contains(v.Id))
                .Select(v => v.Id)
                .ToListAsync(cancellationToken);
            var missingVariantId = variantIds.Except(existingVariantIds).Cast<int?>().FirstOrDefault();

            return missingVariantId == null ? null : $"Product variant {missingVariantId} does not exist.";
        }

        private static ServiceResult<SupplyCommitmentDto> ResultForRetry(SupplyCommitment existing, SupplyCommitment candidate) =>
            HasSameContent(existing, candidate)
                ? ServiceResult<SupplyCommitmentDto>.Ok(ToDto(existing))
                : ServiceResult<SupplyCommitmentDto>.Conflict(
                    $"RequestId {candidate.RequestId} was already used for a supply commitment with different content.");

        // Strikte vergelijking, inclusief de volgorde van de regels; decimals worden op waarde vergeleken (12.30 == 12.3).
        private static bool HasSameContent(SupplyCommitment existing, SupplyCommitment candidate)
        {
            if (existing.VendorId != candidate.VendorId
                || existing.CustomerId != candidate.CustomerId
                || !string.Equals(existing.Route, candidate.Route, StringComparison.Ordinal)
                || !string.Equals(existing.Origin, candidate.Origin, StringComparison.Ordinal)
                || !string.Equals(existing.Destination, candidate.Destination, StringComparison.Ordinal)
                || existing.ExpectedAt?.Ticks != candidate.ExpectedAt?.Ticks)
            {
                return false;
            }

            var existingLines = existing.Lines.OrderBy(l => l.Id).ToList();
            var candidateLines = candidate.Lines.ToList();
            if (existingLines.Count != candidateLines.Count)
            {
                return false;
            }

            return existingLines.Zip(candidateLines).All(pair =>
                pair.First.ProductVariantId == pair.Second.ProductVariantId
                && pair.First.ExpectedQuantity == pair.Second.ExpectedQuantity
                && string.Equals(pair.First.ExpectedUnit, pair.Second.ExpectedUnit, StringComparison.Ordinal)
                && pair.First.ExpectedNetWeightKg == pair.Second.ExpectedNetWeightKg
                && string.Equals(pair.First.LotNumber, pair.Second.LotNumber, StringComparison.Ordinal));
        }

        private Task<SupplyCommitment> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken) =>
            Query().SingleOrDefaultAsync(c => c.RequestId == requestId, cancellationToken);

        private IQueryable<SupplyCommitment> Query() =>
            _db.SupplyCommitments
                .AsNoTracking()
                .Include(c => c.Vendor)
                .Include(c => c.Customer)
                .Include(c => c.Lines)
                    .ThenInclude(l => l.ProductVariant)
                        .ThenInclude(v => v.Product);

        private static SupplyCommitmentDto ToDto(SupplyCommitment commitment) => new()
        {
            Id = commitment.Id,
            RequestId = commitment.RequestId,
            VendorId = commitment.VendorId,
            VendorName = commitment.Vendor?.Name,
            CustomerId = commitment.CustomerId,
            CustomerName = commitment.Customer?.Name,
            Route = commitment.Route,
            Status = commitment.Status,
            Origin = commitment.Origin,
            Destination = commitment.Destination,
            ExpectedAt = commitment.ExpectedAt == null ? null : DateTime.SpecifyKind(commitment.ExpectedAt.Value, DateTimeKind.Utc),
            CreatedAt = DateTime.SpecifyKind(commitment.CreatedAt, DateTimeKind.Utc),
            Lines = commitment.Lines
                .OrderBy(l => l.Id)
                .Select(l => new SupplyCommitmentLineDto
                {
                    Id = l.Id,
                    ProductVariantId = l.ProductVariantId,
                    ProductName = l.ProductVariant?.Product?.Name,
                    ProductVariantName = l.ProductVariant?.Name,
                    LotNumber = l.LotNumber,
                    ExpectedQuantity = l.ExpectedQuantity,
                    ExpectedUnit = l.ExpectedUnit,
                    ExpectedNetWeightKg = l.ExpectedNetWeightKg
                })
                .ToList()
        };

        private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
        {
            if (!errors.TryGetValue(key, out var messages))
            {
                messages = new List<string>();
                errors[key] = messages;
            }
            messages.Add(message);
        }
    }
}
