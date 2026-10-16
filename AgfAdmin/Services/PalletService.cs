using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Legt pallets en hun atomaire inhoudsregels vast, elk met een append-only auditrecord in dezelfde save.
    // Gewichten worden exact zoals gemeten/ingevoerd opgeslagen: nooit een gewicht per verpakking, eenheid of pallet
    // afleiden, en nooit een eenheid omrekenen. Deze service schrijft geen voorraadbewegingen of reserveringen.
    public sealed class PalletService : IPalletService
    {
        public const string PalletCreatedEvent = "PalletCreated";
        public const string ContentLineAddedEvent = "ContentLineAdded";

        private const int MaxReferenceLength = 64;
        private const int MaxPalletTypeLength = 32;
        private const int MaxWarehouseLocationLength = 64;
        private const int MaxLotNumberLength = 100;
        private const int MaxActorLength = 100;
        private const int MaxReasonLength = 500;

        private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly AppDbContext _db;

        public PalletService(AppDbContext db)
        {
            _db = db;
        }

        // ---- Pallet aanmaken ----

        public async Task<ServiceResult<PalletDto>> CreateAsync(CreatePalletDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var candidate = BuildPallet(request, errors, out var actor, out var reason);
            if (errors.Count > 0)
            {
                return Validation<PalletDto>(errors);
            }

            // Idempotentie: de unieke, scanbare referentie identificeert de pallet.
            var existing = await PalletQuery().SingleOrDefaultAsync(p => p.Reference == candidate.Reference, cancellationToken);
            if (existing != null)
            {
                return ResultForPalletRetry(existing, candidate);
            }

            if (candidate.Sscc != null && await _db.Pallets.AnyAsync(p => p.Sscc == candidate.Sscc, cancellationToken))
            {
                return ServiceResult<PalletDto>.Conflict($"SSCC {candidate.Sscc} is already registered on another pallet.");
            }

            _db.Pallets.Add(candidate);
            _db.PalletAuditEvents.Add(new PalletAuditEvent
            {
                Pallet = candidate,
                EventType = PalletCreatedEvent,
                NewValue = Snapshot(new
                {
                    candidate.Reference,
                    candidate.Sscc,
                    candidate.PalletType,
                    candidate.TareKg,
                    candidate.GrossKg,
                    candidate.DeclaredNetKg,
                    candidate.LogisticsStatus,
                    candidate.LocationContext,
                    candidate.WarehouseLocation
                }),
                Reason = reason,
                ActorReference = actor,
                OccurredAt = candidate.CreatedAt
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Gelijktijdige retry of race op referentie/SSCC: de unieke indexen hebben dit verzoek geweigerd.
                _db.ChangeTracker.Clear();
                var concurrent = await PalletQuery().SingleOrDefaultAsync(p => p.Reference == candidate.Reference, cancellationToken);
                if (concurrent != null)
                {
                    return ResultForPalletRetry(concurrent, candidate);
                }
                if (candidate.Sscc != null && await _db.Pallets.AnyAsync(p => p.Sscc == candidate.Sscc, cancellationToken))
                {
                    return ServiceResult<PalletDto>.Conflict($"SSCC {candidate.Sscc} is already registered on another pallet.");
                }
                throw;
            }

            var created = await PalletQuery().SingleAsync(p => p.Id == candidate.Id, cancellationToken);
            return ServiceResult<PalletDto>.Created(ToDto(created));
        }

        private static Pallet BuildPallet(CreatePalletDto request, Dictionary<string, List<string>> errors, out string actor, out string reason)
        {
            actor = null;
            reason = null;
            if (request == null)
            {
                AddError(errors, "request", "A request body is required.");
                return null;
            }

            var reference = RequiredText(request.Reference, MaxReferenceLength, "reference", errors);

            if (request.Sscc != null && !Sscc.IsValid(request.Sscc))
            {
                AddError(errors, "sscc", "SSCC must be exactly 18 digits with a valid GS1 check digit.");
            }

            var palletType = OptionalNonBlankText(request.PalletType, MaxPalletTypeLength, "palletType", errors);

            if (!PalletRules.IsInitialLogisticsStatus(request.LogisticsStatus))
            {
                AddError(errors, "logisticsStatus", $"A new pallet must start as one of: {string.Join(", ", PalletRules.InitialLogisticsStatuses)}.");
            }

            var locationIsValid = PalletRules.IsLocationContext(request.LocationContext);
            if (!locationIsValid)
            {
                AddError(errors, "locationContext", $"Location context must be one of: {string.Join(", ", PalletRules.LocationContexts)}.");
            }

            var isWarehouse = request.LocationContext == PalletRules.LocationWarehouse;
            var isReceived = request.LogisticsStatus == PalletRules.StatusReceived;
            if (isWarehouse && !isReceived)
            {
                AddError(errors, "locationContext", "Only a received pallet can be in the warehouse.");
            }

            // Optioneel, ook in het magazijn; leeg of alleen spaties telt als niet opgegeven.
            var warehouseLocation = string.IsNullOrWhiteSpace(request.WarehouseLocation) ? null : request.WarehouseLocation.Trim();
            if (warehouseLocation != null)
            {
                if (!isWarehouse || !isReceived)
                {
                    AddError(errors, "warehouseLocation", "A warehouse location is only allowed for a received pallet in the warehouse.");
                }
                else if (warehouseLocation.Length > MaxWarehouseLocationLength)
                {
                    AddError(errors, "warehouseLocation", $"This field may contain at most {MaxWarehouseLocationLength} characters.");
                }
            }

            ValidateOptionalMeasurement(request.TareKg, "tareKg", errors);
            ValidateOptionalMeasurement(request.GrossKg, "grossKg", errors);
            ValidateOptionalMeasurement(request.DeclaredNetKg, "declaredNetKg", errors);
            if (request.TareKg > 0m && request.GrossKg > 0m && request.GrossKg < request.TareKg)
            {
                AddError(errors, "grossKg", "Measured gross weight cannot be lower than the measured tare weight.");
            }

            actor = RequiredText(request.ActorReference, MaxActorLength, "actorReference", errors);
            reason = OptionalReason(request.Reason, errors);

            if (errors.Count > 0)
            {
                return null;
            }

            return new Pallet
            {
                Reference = reference,
                Sscc = request.Sscc,
                PalletType = palletType,
                TareKg = request.TareKg,
                GrossKg = request.GrossKg,
                DeclaredNetKg = request.DeclaredNetKg,
                LogisticsStatus = request.LogisticsStatus,
                LocationContext = request.LocationContext,
                WarehouseLocation = warehouseLocation,
                CreatedAt = DateTime.UtcNow
            };
        }

        private static ServiceResult<PalletDto> ResultForPalletRetry(Pallet existing, Pallet candidate)
        {
            var same = string.Equals(existing.Sscc, candidate.Sscc, StringComparison.Ordinal)
                && string.Equals(existing.PalletType, candidate.PalletType, StringComparison.Ordinal)
                && existing.TareKg == candidate.TareKg
                && existing.GrossKg == candidate.GrossKg
                && existing.DeclaredNetKg == candidate.DeclaredNetKg
                && string.Equals(existing.LogisticsStatus, candidate.LogisticsStatus, StringComparison.Ordinal)
                && string.Equals(existing.LocationContext, candidate.LocationContext, StringComparison.Ordinal)
                && string.Equals(existing.WarehouseLocation, candidate.WarehouseLocation, StringComparison.Ordinal);

            return same
                ? ServiceResult<PalletDto>.Ok(ToDto(existing))
                : ServiceResult<PalletDto>.Conflict($"Pallet reference {candidate.Reference} already exists with different content.");
        }

        // ---- Inhoudsregel toevoegen ----

        public async Task<ServiceResult<PalletContentLineDto>> AddContentLineAsync(int palletId, AddPalletContentLineDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var candidate = BuildContentLine(palletId, request, errors, out var actor, out var reason);
            if (errors.Count > 0)
            {
                return Validation<PalletContentLineDto>(errors);
            }

            // Idempotentie: dezelfde RequestId met dezelfde inhoud geeft de bestaande regel terug.
            var existing = await ContentLineQuery().SingleOrDefaultAsync(c => c.RequestId == candidate.RequestId, cancellationToken);
            if (existing != null)
            {
                return ResultForContentLineRetry(existing, candidate);
            }

            var pallet = await _db.Pallets
                .AsNoTracking()
                .Include(p => p.ContentLines)
                    .ThenInclude(c => c.SupplyCommitmentLine)
                        .ThenInclude(l => l.SupplyCommitment)
                .SingleOrDefaultAsync(p => p.Id == palletId, cancellationToken);
            if (pallet == null)
            {
                return ServiceResult<PalletContentLineDto>.NotFound($"Pallet {palletId} does not exist.");
            }

            var source = await _db.SupplyCommitmentLines
                .AsNoTracking()
                .Include(l => l.SupplyCommitment)
                .SingleOrDefaultAsync(l => l.Id == candidate.SupplyCommitmentLineId, cancellationToken);
            if (source == null)
            {
                return ServiceResult<PalletContentLineDto>.NotFound($"Supply commitment line {candidate.SupplyCommitmentLineId} does not exist.");
            }

            var conflict = FindBusinessRuleConflict(pallet, source, candidate);
            if (conflict != null)
            {
                return ServiceResult<PalletContentLineDto>.Conflict(conflict);
            }

            _db.PalletContentLines.Add(candidate);
            _db.PalletAuditEvents.Add(new PalletAuditEvent
            {
                PalletId = palletId,
                PalletContentLine = candidate,
                EventType = ContentLineAddedEvent,
                NewValue = Snapshot(new
                {
                    candidate.SupplyCommitmentLineId,
                    candidate.LotNumber,
                    candidate.Quantity,
                    candidate.Unit,
                    candidate.NetWeightKg,
                    candidate.QualityStatus
                }),
                Reason = reason,
                ActorReference = actor,
                OccurredAt = candidate.CreatedAt
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Gelijktijdige retry: de unieke index op RequestId heeft het tweede verzoek geweigerd.
                _db.ChangeTracker.Clear();
                var concurrent = await ContentLineQuery().SingleOrDefaultAsync(c => c.RequestId == candidate.RequestId, cancellationToken);
                if (concurrent == null)
                {
                    throw;
                }
                return ResultForContentLineRetry(concurrent, candidate);
            }

            var created = await ContentLineQuery().SingleAsync(c => c.Id == candidate.Id, cancellationToken);
            return ServiceResult<PalletContentLineDto>.Created(ToDto(created));
        }

        private static PalletContentLine BuildContentLine(int palletId, AddPalletContentLineDto request, Dictionary<string, List<string>> errors, out string actor, out string reason)
        {
            actor = null;
            reason = null;
            if (request == null)
            {
                AddError(errors, "request", "A request body is required.");
                return null;
            }

            if (request.RequestId == null || request.RequestId == Guid.Empty)
            {
                AddError(errors, "requestId", "A client-generated, non-empty requestId is required.");
            }

            if (request.SupplyCommitmentLineId == null)
            {
                AddError(errors, "supplyCommitmentLineId", "A source supply commitment line is required.");
            }

            var lotNumber = RequiredText(request.LotNumber, MaxLotNumberLength, "lotNumber", errors);

            if (request.Quantity == null || !SupplyRules.IsPositiveDecimal18_3(request.Quantity.Value))
            {
                AddError(errors, "quantity", "Package quantity is required, must be positive and may have at most 3 decimals.");
            }

            if (!SupplyRules.IsPackageUnit(request.Unit))
            {
                AddError(errors, "unit", $"Unit must be one of: {string.Join(", ", SupplyRules.PackageUnits)}.");
            }

            if (request.NetWeightKg == null || !SupplyRules.IsPositiveDecimal18_3(request.NetWeightKg.Value))
            {
                AddError(errors, "netWeightKg", "Actual net weight is required, must be positive and may have at most 3 decimals.");
            }

            actor = RequiredText(request.ActorReference, MaxActorLength, "actorReference", errors);
            reason = OptionalReason(request.Reason, errors);

            if (errors.Count > 0)
            {
                return null;
            }

            return new PalletContentLine
            {
                RequestId = request.RequestId!.Value,
                PalletId = palletId,
                SupplyCommitmentLineId = request.SupplyCommitmentLineId!.Value,
                LotNumber = lotNumber,
                Quantity = request.Quantity!.Value,
                Unit = request.Unit,
                NetWeightKg = request.NetWeightKg!.Value,
                QualityStatus = PalletRules.QualityQuarantine,
                CreatedAt = DateTime.UtcNow
            };
        }

        private static string FindBusinessRuleConflict(Pallet pallet, SupplyCommitmentLine source, PalletContentLine candidate)
        {
            if (PalletRules.IsTerminalLogisticsStatus(pallet.LogisticsStatus))
            {
                return $"Pallet {pallet.Reference} is {pallet.LogisticsStatus}; content can no longer be added.";
            }

            var commitment = source.SupplyCommitment;
            if (commitment.Status == SupplyRules.StatusCancelled)
            {
                return $"Supply commitment {commitment.Id} is cancelled.";
            }

            // Geen conversie: de eenheid moet gelijk zijn aan die van de bron, als de bron er een heeft.
            if (source.ExpectedUnit != null && !string.Equals(source.ExpectedUnit, candidate.Unit, StringComparison.Ordinal))
            {
                return $"Unit {candidate.Unit} does not match the source line unit {source.ExpectedUnit}; units are never converted.";
            }

            // Directe levering wordt nooit Nederlandse magazijnvoorraad.
            if (commitment.Route == SupplyRules.RouteDirectTrade && pallet.LocationContext == PalletRules.LocationWarehouse)
            {
                return "DirectTrade supply cannot be placed on a warehouse pallet.";
            }

            foreach (var line in pallet.ContentLines)
            {
                var other = line.SupplyCommitmentLine.SupplyCommitment;
                if (!string.Equals(other.Route, commitment.Route, StringComparison.Ordinal))
                {
                    return $"Pallet {pallet.Reference} already holds {other.Route} content; routes cannot be mixed on one pallet.";
                }
                if (commitment.Route == SupplyRules.RouteDirectTrade && other.CustomerId != commitment.CustomerId)
                {
                    return $"Pallet {pallet.Reference} already holds DirectTrade content for another customer.";
                }
            }

            // Alleen gemeten waarden: is bruto bekend, dan past tara (indien gemeten) plus alle ingevoerde netto-kg daarbinnen.
            if (pallet.GrossKg != null)
            {
                var totalKg = (pallet.TareKg ?? 0m) + pallet.ContentLines.Sum(c => c.NetWeightKg) + candidate.NetWeightKg;
                if (totalKg > pallet.GrossKg.Value)
                {
                    return $"Tare plus content net weight ({totalKg} kg) would exceed the measured gross weight ({pallet.GrossKg} kg).";
                }
            }

            return null;
        }

        private static ServiceResult<PalletContentLineDto> ResultForContentLineRetry(PalletContentLine existing, PalletContentLine candidate)
        {
            var same = existing.PalletId == candidate.PalletId
                && existing.SupplyCommitmentLineId == candidate.SupplyCommitmentLineId
                && string.Equals(existing.LotNumber, candidate.LotNumber, StringComparison.Ordinal)
                && existing.Quantity == candidate.Quantity
                && string.Equals(existing.Unit, candidate.Unit, StringComparison.Ordinal)
                && existing.NetWeightKg == candidate.NetWeightKg;

            return same
                ? ServiceResult<PalletContentLineDto>.Ok(ToDto(existing))
                : ServiceResult<PalletContentLineDto>.Conflict($"RequestId {candidate.RequestId} was already used for a different pallet content line.");
        }

        // ---- Lezen ----

        public async Task<ServiceResult<PalletDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var pallet = await PalletQuery().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            return pallet == null
                ? ServiceResult<PalletDto>.NotFound($"Pallet {id} does not exist.")
                : ServiceResult<PalletDto>.Ok(ToDto(pallet));
        }

        private IQueryable<Pallet> PalletQuery() =>
            _db.Pallets
                .AsNoTracking()
                .Include(p => p.ContentLines)
                    .ThenInclude(c => c.SupplyCommitmentLine)
                        .ThenInclude(l => l.SupplyCommitment)
                .Include(p => p.ContentLines)
                    .ThenInclude(c => c.SupplyCommitmentLine)
                        .ThenInclude(l => l.ProductVariant)
                            .ThenInclude(v => v.Product);

        private IQueryable<PalletContentLine> ContentLineQuery() =>
            _db.PalletContentLines
                .AsNoTracking()
                .Include(c => c.SupplyCommitmentLine)
                    .ThenInclude(l => l.SupplyCommitment)
                .Include(c => c.SupplyCommitmentLine)
                    .ThenInclude(l => l.ProductVariant)
                        .ThenInclude(v => v.Product);

        private static PalletDto ToDto(Pallet pallet) => new()
        {
            Id = pallet.Id,
            Reference = pallet.Reference,
            Sscc = pallet.Sscc,
            PalletType = pallet.PalletType,
            TareKg = pallet.TareKg,
            GrossKg = pallet.GrossKg,
            DeclaredNetKg = pallet.DeclaredNetKg,
            ContentNetKg = pallet.ContentLines.Sum(c => c.NetWeightKg),
            LogisticsStatus = pallet.LogisticsStatus,
            LocationContext = pallet.LocationContext,
            WarehouseLocation = pallet.WarehouseLocation,
            CreatedAt = DateTime.SpecifyKind(pallet.CreatedAt, DateTimeKind.Utc),
            ContentLines = pallet.ContentLines.OrderBy(c => c.Id).Select(ToDto).ToList()
        };

        private static PalletContentLineDto ToDto(PalletContentLine line) => new()
        {
            Id = line.Id,
            RequestId = line.RequestId,
            PalletId = line.PalletId,
            SupplyCommitmentLineId = line.SupplyCommitmentLineId,
            SupplyCommitmentId = line.SupplyCommitmentLine.SupplyCommitmentId,
            Route = line.SupplyCommitmentLine.SupplyCommitment?.Route,
            CustomerId = line.SupplyCommitmentLine.SupplyCommitment?.CustomerId,
            ProductVariantId = line.SupplyCommitmentLine.ProductVariantId,
            ProductName = line.SupplyCommitmentLine.ProductVariant?.Product?.Name,
            ProductVariantName = line.SupplyCommitmentLine.ProductVariant?.Name,
            LotNumber = line.LotNumber,
            Quantity = line.Quantity,
            Unit = line.Unit,
            NetWeightKg = line.NetWeightKg,
            QualityStatus = line.QualityStatus,
            CreatedAt = DateTime.SpecifyKind(line.CreatedAt, DateTimeKind.Utc)
        };

        // ---- Hulpfuncties ----

        private static void ValidateOptionalMeasurement(decimal? value, string key, Dictionary<string, List<string>> errors)
        {
            if (value != null && !SupplyRules.IsPositiveDecimal18_3(value.Value))
            {
                AddError(errors, key, "A measured weight, when given, must be positive and may have at most 3 decimals.");
            }
        }

        private static string RequiredText(string value, int maxLength, string key, Dictionary<string, List<string>> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                AddError(errors, key, "This field is required and must not be blank.");
                return null;
            }

            return WithinLength(value.Trim(), maxLength, key, errors);
        }

        // Ontbrekend is toegestaan; ingevuld betekent niet-leeg en binnen de maximale lengte.
        private static string OptionalNonBlankText(string value, int maxLength, string key, Dictionary<string, List<string>> errors)
        {
            if (value == null)
            {
                return null;
            }
            if (value.Trim().Length == 0)
            {
                AddError(errors, key, "This field must not be blank.");
                return null;
            }

            return WithinLength(value.Trim(), maxLength, key, errors);
        }

        // Vrije toelichting bij een auditrecord: leeg telt als niet opgegeven.
        private static string OptionalReason(string value, Dictionary<string, List<string>> errors) =>
            string.IsNullOrWhiteSpace(value) ? null : WithinLength(value.Trim(), MaxReasonLength, "reason", errors);

        private static string WithinLength(string trimmed, int maxLength, string key, Dictionary<string, List<string>> errors)
        {
            if (trimmed.Length > maxLength)
            {
                AddError(errors, key, $"This field may contain at most {maxLength} characters.");
                return null;
            }
            return trimmed;
        }

        private static string Snapshot(object value) => JsonSerializer.Serialize(value, SnapshotJson);

        private static ServiceResult<T> Validation<T>(Dictionary<string, List<string>> errors) =>
            ServiceResult<T>.Validation(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));

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
