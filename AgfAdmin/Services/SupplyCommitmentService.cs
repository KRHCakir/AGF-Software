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

        // Pogingen bij een gelijktijdig toegekend ordernummer (unieke index op jaar + volgnummer); daarna een echte fout.
        public const int MaxOrderNumberAttempts = 5;

        public async Task<ServiceResult<SupplyCommitmentDto>> CreateAsync(CreateSupplyCommitmentDto request, CancellationToken cancellationToken = default)
        {
            for (var attempt = 1; ; attempt++)
            {
                var (result, numberTaken) = await TryCreateAsync(request, cancellationToken);
                if (!numberTaken || attempt >= MaxOrderNumberAttempts)
                {
                    return result ?? throw new InvalidOperationException($"No free purchase order number after {attempt} attempts.");
                }
                _db.ChangeTracker.Clear();
            }
        }

        private async Task<(ServiceResult<SupplyCommitmentDto> Result, bool NumberTaken)> TryCreateAsync(CreateSupplyCommitmentDto request, CancellationToken cancellationToken)
        {
            var errors = new Dictionary<string, List<string>>();
            var candidate = BuildCandidate(request, _db.BusinessCalendar.Today(), errors);
            if (errors.Count > 0)
            {
                return (ServiceResult<SupplyCommitmentDto>.Validation(
                    errors.ToDictionary(e => e.Key, e => e.Value.ToArray())), false);
            }

            // Idempotentie: dezelfde RequestId met dezelfde inhoud geeft de bestaande toezegging terug.
            var existing = await FindByRequestIdAsync(candidate.RequestId, cancellationToken);
            if (existing != null)
            {
                return (await ResultForRetryAsync(existing, request, candidate, cancellationToken), false);
            }

            // Nieuwe invoer: hele pallets (na de replaycontrole, zodat een eerder opgeslagen opdracht nog steeds herkend wordt).
            var lines = candidate.Lines.ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                PackagingRules.WholePalletErrors(lines[i].PalletCount, lines[i].PackagesPerPallet, $"lines[{i}]", errors, AddError);
            }
            if (errors.Count > 0)
            {
                return (ServiceResult<SupplyCommitmentDto>.Validation(errors.ToDictionary(e => e.Key, e => e.Value.ToArray())), false);
            }

            var notFound = await FindMissingReferenceAsync(candidate, cancellationToken);
            if (notFound != null)
            {
                return (ServiceResult<SupplyCommitmentDto>.NotFound(notFound), false);
            }

            // Berekende kg: exact aantal × de huidige standaard van het artikel (na de replaycontrole, net als hele pallets).
            var variantIds = lines.Select(l => l.ProductVariantId).Distinct().ToList();
            var defaults = await _db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id))
                .Select(v => new { v.Id, v.DefaultNetKgPerPackage, v.DefaultGrossKgPerPackage }).ToDictionaryAsync(v => v.Id, cancellationToken);
            for (var i = 0; i < lines.Count; i++)
            {
                var variant = defaults[lines[i].ProductVariantId];
                LineWeightRules.CalculatedErrors(lines[i].ExpectedQuantity, lines[i].ExpectedNetWeightKg, lines[i].ExpectedGrossWeightKg, lines[i].WeightSource,
                    variant.DefaultNetKgPerPackage, variant.DefaultGrossKgPerPackage, $"lines[{i}]", "expectedNetWeightKg", "expectedGrossWeightKg", errors, AddError);
            }
            if (errors.Count > 0)
            {
                return (ServiceResult<SupplyCommitmentDto>.Validation(errors.ToDictionary(e => e.Key, e => e.Value.ToArray())), false);
            }

            // Het ordernummer kent AppDbContext bij opslaan toe (volgende vrije in het jaar). Een gelijktijdige aanmaak kan hetzelfde
            // nummer kiezen; de unieke index weigert dan de tweede en CreateAsync probeert het opnieuw met een nieuw nummer.
            _db.SupplyCommitments.Add(candidate);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                // Gelijktijdige retry: de unieke index op RequestId heeft het tweede verzoek geweigerd.
                _db.ChangeTracker.Clear();
                var concurrent = await FindByRequestIdAsync(candidate.RequestId, cancellationToken);
                if (concurrent != null)
                {
                    return (await ResultForRetryAsync(concurrent, request, candidate, cancellationToken), false);
                }
                // Anders bezette het nummer een gelijktijdige andere order: opnieuw met het volgende nummer.
                if (DbErrors.IsUniqueViolation(exception))
                {
                    return (null, true);
                }
                throw;
            }

            var created = await FindByRequestIdAsync(candidate.RequestId, cancellationToken);
            return (ServiceResult<SupplyCommitmentDto>.Created(await ToDtoAsync(created, cancellationToken)), false);
        }

        public async Task<ServiceResult<SupplyCommitmentDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var commitment = await Query().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            return commitment == null
                ? ServiceResult<SupplyCommitmentDto>.NotFound($"Supply commitment {id} does not exist.")
                : ServiceResult<SupplyCommitmentDto>.Ok(await ToDtoAsync(commitment, cancellationToken));
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

            var allocated = await AllocatedForAsync(commitments, cancellationToken);
            return ServiceResult<IReadOnlyList<SupplyCommitmentDto>>.Ok(commitments.Select(c => ToDto(c, allocated)).ToList());
        }

        // Toegewezen DirectTrade-capaciteit per bronregel (alleen voor DirectTrade-toezeggingen opgevraagd).
        private async Task<Dictionary<int, decimal>> AllocatedForAsync(IReadOnlyCollection<SupplyCommitment> commitments, CancellationToken cancellationToken)
        {
            var ids = commitments.Where(c => c.Route == SupplyRules.RouteDirectTrade).SelectMany(c => c.Lines).Select(l => l.Id).ToList();
            return await DirectTradeAllocationService.AllocatedBySourceLineAsync(_db, ids, cancellationToken);
        }

        private async Task<SupplyCommitmentDto> ToDtoAsync(SupplyCommitment commitment, CancellationToken cancellationToken) =>
            ToDto(commitment, await AllocatedForAsync(new[] { commitment }, cancellationToken));

        private static SupplyCommitment BuildCandidate(CreateSupplyCommitmentDto request, DateOnly today, Dictionary<string, List<string>> errors)
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

            // Inkoopordergegevens: alle optioneel; leeg telt als niet opgegeven.
            var supplierReference = OptionalText(request.SupplierReference, PurchaseOrderRules.MaxSupplierReferenceLength, "supplierReference", errors);
            var note = OptionalText(request.Note, PurchaseOrderRules.MaxNoteLength, "note", errors);
            var orderChannel = string.IsNullOrWhiteSpace(request.OrderChannel) ? null : request.OrderChannel.Trim();
            if (orderChannel != null && !PurchaseOrderRules.IsChannel(orderChannel))
            {
                AddError(errors, "orderChannel", $"Order channel must be one of: {string.Join(", ", PurchaseOrderRules.Channels)}.");
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
                SupplierReference = supplierReference,
                OrderedOn = request.OrderedOn ?? today,
                OrderChannel = orderChannel,
                Note = note,
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

            var palletInput = line.PalletCount != null || line.PackagesPerPallet != null;
            if (line.ExpectedQuantity == null ? !palletInput : !SupplyRules.IsPositiveDecimal18_3(line.ExpectedQuantity.Value))
            {
                AddError(errors, $"{key}.expectedQuantity", "Expected quantity is required (or pallet count × packages per pallet), must be positive and may have at most 3 decimals.");
            }

            if (!SupplyRules.IsPackageUnit(line.ExpectedUnit))
            {
                AddError(errors, $"{key}.expectedUnit", $"Expected unit must be one of: {string.Join(", ", SupplyRules.PackageUnits)}.");
            }

            // Kg optioneel: positief, bruto >= netto; een bron alleen met kg (zonder bron = Entered, zoals oudere opdrachten).
            var weightSource = LineWeightRules.Validate(line.ExpectedNetWeightKg, line.ExpectedGrossWeightKg, line.WeightSource, key, "expectedNetWeightKg", "expectedGrossWeightKg", errors, AddError);

            var lotNumber = ValidateText(line.LotNumber, required: false, MaxLotNumberLength, $"{key}.lotNumber", errors);

            // Palletinvoer: het totaal volgt uit pallets × verpakkingen per pallet + losse verpakkingen, of moet daar exact aan gelijk zijn.
            var total = PackagingRules.Validate(line.PalletCount, line.PackagesPerPallet, line.LoosePackages, line.ExpectedQuantity, key, "expectedQuantity", errors, AddError);

            if (errors.Count > errorCount || total == null)
            {
                return null;
            }

            return new SupplyCommitmentLine
            {
                ProductVariantId = line.ProductVariantId,
                ExpectedQuantity = total.Value,
                PalletCount = line.PalletCount,
                PackagesPerPallet = line.PackagesPerPallet,
                LoosePackages = line.LoosePackages,
                ExpectedUnit = line.ExpectedUnit,
                ExpectedNetWeightKg = line.ExpectedNetWeightKg,
                ExpectedGrossWeightKg = line.ExpectedGrossWeightKg,
                WeightSource = weightSource,
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

        // Optionele vrije tekst: leeg of alleen spaties = niet opgegeven (null); anders getrimd en op lengte gecontroleerd.
        private static string OptionalText(string value, int maxLength, string key, Dictionary<string, List<string>> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            var trimmed = value.Trim();
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
            var vendorState = await _db.Vendors.AsNoTracking().Where(v => v.Id == candidate.VendorId).Select(v => new { v.Relation.IsActive }).SingleOrDefaultAsync(cancellationToken);
            if (vendorState == null)
            {
                return $"Vendor {candidate.VendorId} does not exist.";
            }
            if (!vendorState.IsActive)
            {
                return $"Vendor {candidate.VendorId} belongs to an inactive relation; reactivate the relation first or choose another vendor.";
            }

            if (candidate.CustomerId != null)
            {
                var customerState = await _db.Customers.AsNoTracking().Where(c => c.Id == candidate.CustomerId).Select(c => new { c.Relation.IsActive }).SingleOrDefaultAsync(cancellationToken);
                if (customerState == null)
                {
                    return $"Customer {candidate.CustomerId} does not exist.";
                }
                if (!customerState.IsActive)
                {
                    return $"Customer {candidate.CustomerId} belongs to an inactive relation; reactivate the relation first or choose another customer.";
                }
            }

            var variantIds = candidate.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
            var existingVariantIds = await _db.ProductVariants
                .Where(v => variantIds.Contains(v.Id))
                .Select(v => v.Id)
                .ToListAsync(cancellationToken);
            var missingVariantId = variantIds.Except(existingVariantIds).Cast<int?>().FirstOrDefault();

            return missingVariantId == null ? null : $"Product variant {missingVariantId} does not exist.";
        }

        private async Task<ServiceResult<SupplyCommitmentDto>> ResultForRetryAsync(SupplyCommitment existing, CreateSupplyCommitmentDto request, SupplyCommitment candidate, CancellationToken cancellationToken) =>
            HasSameContent(existing, candidate, orderedOnGiven: request.OrderedOn != null)
                ? ServiceResult<SupplyCommitmentDto>.Ok(await ToDtoAsync(existing, cancellationToken))
                : ServiceResult<SupplyCommitmentDto>.Conflict(
                    $"RequestId {candidate.RequestId} was already used for a supply commitment with different content.");

        // Strikte vergelijking, inclusief de volgorde van de regels; decimals worden op waarde vergeleken (12.30 == 12.3).
        // Inkoopordervelden: weggelaten (null) betekent "niet opgegeven" en is gelijk aan een opgeslagen null; een weggelaten
        // besteldatum telt niet mee (die is bij opslag vandaag geworden), zodat een oudere bevroren opdracht later nog gelijk is.
        private static bool HasSameContent(SupplyCommitment existing, SupplyCommitment candidate, bool orderedOnGiven)
        {
            if (!string.Equals(existing.SupplierReference, candidate.SupplierReference, StringComparison.Ordinal)
                || !string.Equals(existing.OrderChannel, candidate.OrderChannel, StringComparison.Ordinal)
                || !string.Equals(existing.Note, candidate.Note, StringComparison.Ordinal)
                || (orderedOnGiven && existing.OrderedOn != candidate.OrderedOn))
            {
                return false;
            }

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
                && pair.First.ExpectedGrossWeightKg == pair.Second.ExpectedGrossWeightKg
                && string.Equals(pair.First.WeightSource, pair.Second.WeightSource, StringComparison.Ordinal)
                && pair.First.PalletCount == pair.Second.PalletCount
                && pair.First.PackagesPerPallet == pair.Second.PackagesPerPallet
                && pair.First.LoosePackages == pair.Second.LoosePackages
                && string.Equals(pair.First.LotNumber, pair.Second.LotNumber, StringComparison.Ordinal));
        }

        private Task<SupplyCommitment> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken) =>
            Query().SingleOrDefaultAsync(c => c.RequestId == requestId, cancellationToken);

        private IQueryable<SupplyCommitment> Query() =>
            _db.SupplyCommitments
                .AsNoTracking()
                .Include(c => c.Vendor).ThenInclude(v => v.Ggns)
                .Include(c => c.Customer)
                .Include(c => c.Lines)
                    .ThenInclude(l => l.ProductVariant)
                        .ThenInclude(v => v.Product);

        // DirectTrade-regels tonen hun toegewezen en resterende capaciteit; Warehouse-regels nooit (null): magazijnaanvoer
        // wordt pas voorraad na ontvangst en keuring en heeft geen toewijsbare verwachte capaciteit.
        private static SupplyCommitmentDto ToDto(SupplyCommitment commitment, IReadOnlyDictionary<int, decimal> allocated) => new()
        {
            Id = commitment.Id,
            RequestId = commitment.RequestId,
            VendorId = commitment.VendorId,
            VendorName = commitment.Vendor?.Name,
            // Actieve GGN's van de leverancier: voorstel bij ontvangst alleen als er precies één is; bij meer kiest de gebruiker.
            VendorGgns = (commitment.Vendor?.Ggns ?? new List<VendorGgn>()).Where(g => g.IsActive).OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
                .Select(g => new VendorGgnOptionDto { Ggn = g.Ggn, ProducerName = g.ProducerName, CheckedOn = g.CheckedOn, CertificateValidUntil = g.CertificateValidUntil, HasGgnLabel = g.HasGgnLabel }).ToList(),
            CustomerId = commitment.CustomerId,
            CustomerName = commitment.Customer?.Name,
            Route = commitment.Route,
            Status = commitment.Status,
            Origin = commitment.Origin,
            Destination = commitment.Destination,
            ExpectedAt = commitment.ExpectedAt == null ? null : DateTime.SpecifyKind(commitment.ExpectedAt.Value, DateTimeKind.Utc),
            CreatedAt = DateTime.SpecifyKind(commitment.CreatedAt, DateTimeKind.Utc),
            OrderNumber = commitment.OrderNumber,
            SupplierReference = commitment.SupplierReference,
            OrderedOn = commitment.OrderedOn,
            OrderChannel = commitment.OrderChannel,
            Note = commitment.Note,
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
                    ExpectedNetWeightKg = l.ExpectedNetWeightKg,
                    ExpectedGrossWeightKg = l.ExpectedGrossWeightKg,
                    WeightSource = l.WeightSource,
                    WeightMethod = l.ProductVariant?.WeightMethod ?? WeightRules.Weighed,
                    DefaultNetKgPerPackage = l.ProductVariant?.DefaultNetKgPerPackage,
                    DefaultGrossKgPerPackage = l.ProductVariant?.DefaultGrossKgPerPackage,
                    PalletCount = l.PalletCount,
                    PackagesPerPallet = l.PackagesPerPallet,
                    LoosePackages = l.LoosePackages,
                    DefaultPackagesPerPallet = l.ProductVariant?.DefaultPackagesPerPallet,
                    DirectTradeAllocatedQuantity = commitment.Route == SupplyRules.RouteDirectTrade ? allocated.GetValueOrDefault(l.Id) : null,
                    DirectTradeRemainingQuantity = commitment.Route == SupplyRules.RouteDirectTrade
                        ? DirectTradeAllocationService.Remaining(l.ExpectedQuantity, allocated.GetValueOrDefault(l.Id)) : null,
                    ReplacesLineId = l.ReplacesLineId
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
