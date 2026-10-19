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
        public const string LogisticsStatusChangedEvent = "LogisticsStatusChanged";
        public const string LocationChangedEvent = "LocationChanged";
        public const string QualityStatusChangedEvent = "QualityStatusChanged";

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

            // Gevolgd geladen: de pallet wordt hieronder "aangeraakt" (versie +1), zodat twee gelijktijdige
            // toevoegingen niet samen de gewichts- of mengregels kunnen omzeilen.
            var pallet = await TrackedPalletWithContentAsync(palletId, cancellationToken);
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
            _db.Entry(pallet).Property(p => p.Version).IsModified = true;
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
            catch (DbUpdateException exception)
            {
                // Gelijktijdige retry (unieke RequestId) of gelijktijdige wijziging van de pallet (versie).
                _db.ChangeTracker.Clear();
                var concurrent = await ContentLineQuery().SingleOrDefaultAsync(c => c.RequestId == candidate.RequestId, cancellationToken);
                if (concurrent != null)
                {
                    return ResultForContentLineRetry(concurrent, candidate);
                }
                if (exception is DbUpdateConcurrencyException)
                {
                    return ServiceResult<PalletContentLineDto>.Conflict(ConcurrentChangeMessage($"Pallet {palletId}"));
                }
                throw;
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

            // Directe levering wordt nooit magazijnvoorraad en routes/klanten worden niet gemengd (centrale controle).
            var sources = pallet.ContentLines
                .Select(c => new PalletInvariants.ContentSource(c.SupplyCommitmentLine.SupplyCommitment.Route, c.SupplyCommitmentLine.SupplyCommitment.CustomerId))
                .Append(new PalletInvariants.ContentSource(commitment.Route, commitment.CustomerId))
                .ToList();
            var contentConflict = PalletInvariants.CheckContent(pallet.LocationContext, sources);
            if (contentConflict != null)
            {
                return contentConflict;
            }

            // Alleen gemeten waarden: is bruto bekend, dan past tara plus de voorraad-kg (ontvangst minus afgeboekt) plus het nieuwe kg daarbinnen.
            return PalletInvariants.CheckGrossWeight(pallet.GrossKg, pallet.TareKg, pallet.ContentLines, candidate.NetWeightKg);
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

        // ---- Logistieke status, locatie en kwaliteit (commando's) ----
        //
        // Elk commando: verplichte requestId, verwachte versie en actor; één append-only auditrecord in dezelfde save.
        // Volgorde: invoer (400) -> idempotente replay -> bestaan (404) -> versie (409) -> regels (409) -> opslaan.
        // Idempotentie vergelijkt het volledige genormaliseerde commando: doel, eventtype, actor, reden, de gevraagde
        // nieuwe waarden (NewValue) en de verwachte versie (vastgelegd als "version" in OldValue).

        private sealed record StateCommand(
            Guid RequestId,
            string EventType,
            int PalletId,
            int? ContentLineId,
            string Actor,
            string Reason,
            string NewValue,
            int ExpectedVersion);

        public async Task<ServiceResult<PalletDto>> ChangeLogisticsStatusAsync(int palletId, ChangeLogisticsStatusDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request == null)
            {
                AddError(errors, "request", "A request body is required.");
                return Validation<PalletDto>(errors);
            }

            ValidateRequestId(request.RequestId, errors);
            if (request.ExpectedPalletVersion == null)
            {
                AddError(errors, "expectedPalletVersion", "The pallet version the change is based on is required.");
            }
            if (!PalletRules.IsLogisticsStatus(request.ToStatus))
            {
                AddError(errors, "toStatus", $"Status must be one of: {string.Join(", ", PalletRules.LogisticsStatuses)}.");
            }

            var locationRequested = request.LocationContext != null;
            if (locationRequested && !PalletRules.IsLocationContext(request.LocationContext))
            {
                AddError(errors, "locationContext", $"Location context must be one of: {string.Join(", ", PalletRules.LocationContexts)}.");
            }
            var bin = NormalizeCommandBin(request.WarehouseLocation, request.LocationContext, errors);
            var actor = RequiredText(request.ActorReference, MaxActorLength, "actorReference", errors);
            var reason = OptionalReason(request.Reason, errors);
            if (reason == null && !errors.ContainsKey("reason") && PalletRules.LogisticsTransitionRequiresReason(null, request.ToStatus))
            {
                AddError(errors, "reason", $"A reason is required to change a pallet to {request.ToStatus}.");
            }
            if (errors.Count > 0)
            {
                return Validation<PalletDto>(errors);
            }

            var command = new StateCommand(
                request.RequestId!.Value,
                LogisticsStatusChangedEvent,
                palletId,
                null,
                actor,
                reason,
                locationRequested
                    ? Snapshot(new { logisticsStatus = request.ToStatus, locationContext = request.LocationContext, warehouseLocation = bin })
                    : Snapshot(new { logisticsStatus = request.ToStatus }),
                request.ExpectedPalletVersion!.Value);

            var replay = await ReplayAsync(command, () => GetAsync(palletId, cancellationToken), cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var pallet = await TrackedPalletWithContentAsync(palletId, cancellationToken);
            if (pallet == null)
            {
                return ServiceResult<PalletDto>.NotFound($"Pallet {palletId} does not exist.");
            }
            if (pallet.Version != command.ExpectedVersion)
            {
                return ServiceResult<PalletDto>.Conflict(StaleVersionMessage($"Pallet {palletId}", pallet.Version, command.ExpectedVersion));
            }

            var from = pallet.LogisticsStatus;
            var to = request.ToStatus;
            var newLocation = locationRequested ? request.LocationContext : pallet.LocationContext;
            var newBin = locationRequested ? bin : pallet.WarehouseLocation;
            var conflict = FindLogisticsConflict(pallet, from, to, newLocation, newBin, locationRequested)
                ?? await FindReservationBlockAsync(pallet, to, newLocation, cancellationToken);
            if (conflict != null)
            {
                return ServiceResult<PalletDto>.Conflict(conflict);
            }

            var oldValue = Snapshot(new
            {
                logisticsStatus = from,
                locationContext = pallet.LocationContext,
                warehouseLocation = pallet.WarehouseLocation,
                version = pallet.Version
            });
            pallet.LogisticsStatus = to;
            pallet.LocationContext = newLocation;
            pallet.WarehouseLocation = newBin;
            AddCommandEvent(command, oldValue);

            return await SaveCommandAsync(command, () => GetAsync(palletId, cancellationToken), $"Pallet {palletId}", cancellationToken);
        }

        // B6: zolang dispatch reserveringen nog niet afboekt (PR #26), mag een pallet met Active Physical verkoopreserveringen
        // (SalesOrderLineId) niet verzonden/afgeleverd worden of het magazijn verlaten. Legacy OrderLineId-reserveringen tellen niet.
        private async Task<string> FindReservationBlockAsync(Pallet pallet, string toStatus, string newLocation, CancellationToken cancellationToken)
        {
            var leavesWarehouse = toStatus == PalletRules.StatusDispatched
                || toStatus == PalletRules.StatusDelivered
                || (pallet.LocationContext == PalletRules.LocationWarehouse && newLocation != PalletRules.LocationWarehouse);
            if (!leavesWarehouse)
            {
                return null;
            }
            var reserved = await _db.StockReservations.AsNoTracking().AnyAsync(r =>
                r.SalesOrderLineId != null
                && r.Status == ReservationRules.StatusActive
                && r.Source == ReservationRules.SourcePhysical
                && r.PalletContentLine.PalletId == pallet.Id, cancellationToken);
            return reserved
                ? "Release the active reservations on this pallet first; consuming them through dispatch follows later."
                : null;
        }

        private static string FindLogisticsConflict(Pallet pallet, string from, string to, string newLocation, string newBin, bool locationRequested)
        {
            if (from == to)
            {
                return $"Pallet {pallet.Reference} is already {to}.";
            }
            if (!PalletRules.IsAllowedLogisticsTransition(from, to))
            {
                return $"A pallet cannot go from {from} to {to}.";
            }

            if (to == PalletRules.StatusDispatched || to == PalletRules.StatusDelivered)
            {
                if (pallet.ContentLines.Count == 0)
                {
                    return $"An empty pallet cannot become {to}.";
                }
                if (pallet.ContentLines.Any(c => !PalletRules.IsSellableQuality(c.QualityStatus)))
                {
                    return $"Every content line must be {PalletRules.QualityApproved} before the pallet can become {to}.";
                }
            }

            if (from == PalletRules.StatusInTransit && to == PalletRules.StatusDelivered
                && pallet.ContentLines.Any(c => c.SupplyCommitmentLine.SupplyCommitment.Route != SupplyRules.RouteDirectTrade))
            {
                return "Only a pallet with DirectTrade content can be delivered directly from transit.";
            }

            var stateConflict = PalletInvariants.CheckStateCombination(to, newLocation, newBin);
            if (stateConflict != null)
            {
                return locationRequested ? stateConflict : $"{stateConflict} Provide a new location together with this status change.";
            }

            return PalletInvariants.CheckContent(newLocation, ContentSources(pallet));
        }

        public async Task<ServiceResult<PalletDto>> ChangeLocationAsync(int palletId, ChangePalletLocationDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request == null)
            {
                AddError(errors, "request", "A request body is required.");
                return Validation<PalletDto>(errors);
            }

            ValidateRequestId(request.RequestId, errors);
            if (request.ExpectedPalletVersion == null)
            {
                AddError(errors, "expectedPalletVersion", "The pallet version the change is based on is required.");
            }
            if (!PalletRules.IsLocationContext(request.LocationContext))
            {
                AddError(errors, "locationContext", $"Location context must be one of: {string.Join(", ", PalletRules.LocationContexts)}.");
            }
            var bin = NormalizeCommandBin(request.WarehouseLocation, request.LocationContext, errors);
            var actor = RequiredText(request.ActorReference, MaxActorLength, "actorReference", errors);
            var reason = OptionalReason(request.Reason, errors);
            if (errors.Count > 0)
            {
                return Validation<PalletDto>(errors);
            }

            var command = new StateCommand(
                request.RequestId!.Value,
                LocationChangedEvent,
                palletId,
                null,
                actor,
                reason,
                Snapshot(new { locationContext = request.LocationContext, warehouseLocation = bin }),
                request.ExpectedPalletVersion!.Value);

            var replay = await ReplayAsync(command, () => GetAsync(palletId, cancellationToken), cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var pallet = await TrackedPalletWithContentAsync(palletId, cancellationToken);
            if (pallet == null)
            {
                return ServiceResult<PalletDto>.NotFound($"Pallet {palletId} does not exist.");
            }
            if (pallet.Version != command.ExpectedVersion)
            {
                return ServiceResult<PalletDto>.Conflict(StaleVersionMessage($"Pallet {palletId}", pallet.Version, command.ExpectedVersion));
            }

            var conflict = FindLocationConflict(pallet, request.LocationContext, bin)
                ?? await FindReservationBlockAsync(pallet, pallet.LogisticsStatus, request.LocationContext, cancellationToken);
            if (conflict != null)
            {
                return ServiceResult<PalletDto>.Conflict(conflict);
            }

            var oldValue = Snapshot(new
            {
                locationContext = pallet.LocationContext,
                warehouseLocation = pallet.WarehouseLocation,
                version = pallet.Version
            });
            pallet.LocationContext = request.LocationContext;
            pallet.WarehouseLocation = bin;
            AddCommandEvent(command, oldValue);

            return await SaveCommandAsync(command, () => GetAsync(palletId, cancellationToken), $"Pallet {palletId}", cancellationToken);
        }

        private static string FindLocationConflict(Pallet pallet, string newLocation, string newBin)
        {
            if (PalletRules.LocationLockedLogisticsStatuses.Contains(pallet.LogisticsStatus, StringComparer.Ordinal))
            {
                return $"The location of a {pallet.LogisticsStatus} pallet can no longer change.";
            }
            if (pallet.LocationContext == newLocation && pallet.WarehouseLocation == newBin)
            {
                return $"Pallet {pallet.Reference} is already at this location.";
            }

            return PalletInvariants.CheckStateCombination(pallet.LogisticsStatus, newLocation, newBin)
                ?? PalletInvariants.CheckContent(newLocation, ContentSources(pallet));
        }

        public async Task<ServiceResult<PalletContentLineDto>> ChangeQualityStatusAsync(int palletId, int contentLineId, ChangeQualityStatusDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request == null)
            {
                AddError(errors, "request", "A request body is required.");
                return Validation<PalletContentLineDto>(errors);
            }

            ValidateRequestId(request.RequestId, errors);
            if (request.ExpectedContentLineVersion == null)
            {
                AddError(errors, "expectedContentLineVersion", "The content line version the change is based on is required.");
            }
            if (!PalletRules.IsQualityStatus(request.ToStatus))
            {
                AddError(errors, "toStatus", $"Quality status must be one of: {string.Join(", ", PalletRules.QualityStatuses)}.");
            }
            var actor = RequiredText(request.ActorReference, MaxActorLength, "actorReference", errors);
            var reason = OptionalReason(request.Reason, errors);
            if (errors.Count > 0)
            {
                return Validation<PalletContentLineDto>(errors);
            }

            var command = new StateCommand(
                request.RequestId!.Value,
                QualityStatusChangedEvent,
                palletId,
                contentLineId,
                actor,
                reason,
                Snapshot(new { qualityStatus = request.ToStatus, qualityReason = reason }),
                request.ExpectedContentLineVersion!.Value);

            var replay = await ReplayAsync(command, () => GetContentLineAsync(contentLineId, cancellationToken), cancellationToken);
            if (replay != null)
            {
                return replay;
            }

            var line = await _db.PalletContentLines
                .Include(c => c.Pallet)
                .SingleOrDefaultAsync(c => c.Id == contentLineId, cancellationToken);
            if (line == null || line.PalletId != palletId)
            {
                return ServiceResult<PalletContentLineDto>.NotFound($"Content line {contentLineId} does not exist on pallet {palletId}.");
            }
            if (line.Version != command.ExpectedVersion)
            {
                return ServiceResult<PalletContentLineDto>.Conflict(StaleVersionMessage($"Content line {contentLineId}", line.Version, command.ExpectedVersion));
            }

            var from = line.QualityStatus;
            var to = request.ToStatus;
            if (PalletRules.QualityLockedLogisticsStatuses.Contains(line.Pallet.LogisticsStatus, StringComparer.Ordinal))
            {
                return ServiceResult<PalletContentLineDto>.Conflict($"Quality cannot change on a {line.Pallet.LogisticsStatus} pallet.");
            }
            if (from == to)
            {
                return ServiceResult<PalletContentLineDto>.Conflict($"Content line {contentLineId} is already {to}.");
            }
            if (!PalletRules.IsAllowedQualityTransition(from, to))
            {
                return ServiceResult<PalletContentLineDto>.Conflict($"Quality cannot go from {from} to {to}.");
            }
            if (reason == null && PalletRules.QualityTransitionRequiresReason(from, to))
            {
                AddError(errors, "reason", $"A reason is required to change quality from {from} to {to}.");
                return Validation<PalletContentLineDto>(errors);
            }

            var oldValue = Snapshot(new { qualityStatus = from, qualityReason = line.QualityReason, version = line.Version });
            line.QualityStatus = to;
            line.QualityReason = reason;
            // Ook de pallet "aanraken" (versie +1 via IncrementVersions): een gelijktijdige dispatch beslist op de
            // kwaliteit van alle regels en beschermt alleen de palletversie. Zo wint precies één van beide, in één transactie
            // met de kwaliteitswijziging, het auditrecord en beide versieverhogingen.
            _db.Entry(line.Pallet).Property(p => p.Version).IsModified = true;
            AddCommandEvent(command, oldValue);

            return await SaveCommandAsync(command, () => GetContentLineAsync(contentLineId, cancellationToken), $"Content line {contentLineId}", cancellationToken);
        }

        public async Task<ServiceResult<IReadOnlyList<PalletAuditEventDto>>> GetAuditEventsAsync(int palletId, CancellationToken cancellationToken = default)
        {
            if (!await _db.Pallets.AnyAsync(p => p.Id == palletId, cancellationToken))
            {
                return ServiceResult<IReadOnlyList<PalletAuditEventDto>>.NotFound($"Pallet {palletId} does not exist.");
            }

            var events = await _db.PalletAuditEvents
                .AsNoTracking()
                .Where(e => e.PalletId == palletId || (e.PalletContentLine != null && e.PalletContentLine.PalletId == palletId))
                .OrderBy(e => e.OccurredAt)
                .ThenBy(e => e.Id)
                .ToListAsync(cancellationToken);

            return ServiceResult<IReadOnlyList<PalletAuditEventDto>>.Ok(events.Select(e => new PalletAuditEventDto
            {
                Id = e.Id,
                PalletId = e.PalletId,
                PalletContentLineId = e.PalletContentLineId,
                EventType = e.EventType,
                OldValue = e.OldValue,
                NewValue = e.NewValue,
                Reason = e.Reason,
                ActorReference = e.ActorReference,
                OccurredAt = DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc),
                RequestId = e.RequestId
            }).ToList());
        }

        // ---- Gedeelde stappen van de commando's ----

        private async Task<ServiceResult<T>> ReplayAsync<T>(StateCommand command, Func<Task<ServiceResult<T>>> currentState, CancellationToken cancellationToken)
        {
            var existing = await _db.PalletAuditEvents.AsNoTracking().SingleOrDefaultAsync(e => e.RequestId == command.RequestId, cancellationToken);
            if (existing == null)
            {
                return null;
            }

            return IsSameCommand(existing, command)
                ? await currentState()
                : ServiceResult<T>.Conflict($"RequestId {command.RequestId} was already used for a different command.");
        }

        private static bool IsSameCommand(PalletAuditEvent existing, StateCommand command) =>
            string.Equals(existing.EventType, command.EventType, StringComparison.Ordinal)
            && existing.PalletId == command.PalletId
            && existing.PalletContentLineId == command.ContentLineId
            && string.Equals(existing.ActorReference, command.Actor, StringComparison.Ordinal)
            && string.Equals(existing.Reason, command.Reason, StringComparison.Ordinal)
            && string.Equals(existing.NewValue, command.NewValue, StringComparison.Ordinal)
            && RecordedVersion(existing.OldValue) == command.ExpectedVersion;

        private static int? RecordedVersion(string oldValue)
        {
            if (oldValue == null)
            {
                return null;
            }
            try
            {
                using var document = JsonDocument.Parse(oldValue);
                return document.RootElement.TryGetProperty("version", out var version) && version.TryGetInt32(out var value) ? value : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private void AddCommandEvent(StateCommand command, string oldValue) =>
            _db.PalletAuditEvents.Add(new PalletAuditEvent
            {
                PalletId = command.PalletId,
                PalletContentLineId = command.ContentLineId,
                EventType = command.EventType,
                OldValue = oldValue,
                NewValue = command.NewValue,
                Reason = command.Reason,
                ActorReference = command.Actor,
                OccurredAt = DateTime.UtcNow,
                RequestId = command.RequestId
            });

        private async Task<ServiceResult<T>> SaveCommandAsync<T>(StateCommand command, Func<Task<ServiceResult<T>>> currentState, string target, CancellationToken cancellationToken)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                // Een gelijktijdige retry (unieke RequestId) wordt alsnog als replay beoordeeld; een gelijktijdige
                // andere wijziging (versie) wordt 409. In beide gevallen is niets van dit verzoek opgeslagen.
                _db.ChangeTracker.Clear();
                var replay = await ReplayAsync(command, currentState, cancellationToken);
                if (replay != null)
                {
                    return replay;
                }
                if (exception is DbUpdateConcurrencyException)
                {
                    return ServiceResult<T>.Conflict(ConcurrentChangeMessage(target));
                }
                throw;
            }

            return await currentState();
        }

        private async Task<ServiceResult<PalletContentLineDto>> GetContentLineAsync(int contentLineId, CancellationToken cancellationToken)
        {
            var line = await ContentLineQuery().SingleOrDefaultAsync(c => c.Id == contentLineId, cancellationToken);
            return line == null
                ? ServiceResult<PalletContentLineDto>.NotFound($"Content line {contentLineId} does not exist.")
                : ServiceResult<PalletContentLineDto>.Ok(ToDto(line));
        }

        private Task<Pallet> TrackedPalletWithContentAsync(int palletId, CancellationToken cancellationToken) =>
            _db.Pallets
                .Include(p => p.ContentLines)
                    .ThenInclude(c => c.SupplyCommitmentLine)
                        .ThenInclude(l => l.SupplyCommitment)
                .SingleOrDefaultAsync(p => p.Id == palletId, cancellationToken);

        private static List<PalletInvariants.ContentSource> ContentSources(Pallet pallet) =>
            pallet.ContentLines
                .Select(c => new PalletInvariants.ContentSource(c.SupplyCommitmentLine.SupplyCommitment.Route, c.SupplyCommitmentLine.SupplyCommitment.CustomerId))
                .ToList();

        private static void ValidateRequestId(Guid? requestId, Dictionary<string, List<string>> errors)
        {
            if (requestId == null || requestId == Guid.Empty)
            {
                AddError(errors, "requestId", "A client-generated, non-empty requestId is required.");
            }
        }

        // Leeg of alleen spaties telt als geen bin; een bin hoort alleen bij LocationContext Warehouse.
        private static string NormalizeCommandBin(string value, string locationContext, Dictionary<string, List<string>> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            if (locationContext != PalletRules.LocationWarehouse)
            {
                AddError(errors, "warehouseLocation", "A warehouse location can only be given together with location context Warehouse.");
                return null;
            }

            return WithinLength(value.Trim(), MaxWarehouseLocationLength, "warehouseLocation", errors);
        }

        private static string StaleVersionMessage(string target, int actual, int expected) =>
            $"{target} is at version {actual}, not {expected}; reload it and decide again.";

        private static string ConcurrentChangeMessage(string target) =>
            $"{target} was changed concurrently; reload it and retry with a new requestId.";

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
                .Include(c => c.Pallet)
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
            ContentNetKg = pallet.ContentLines.Sum(c => ReservationRules.OnHandQuantity(c.NetWeightKg, c.DispatchedNetWeightKg)),
            // Historische ontvangst apart herkenbaar: alleen fysiek ontvangen inhoud, niet de teruggeboekte correctie-inhoud.
            ReceivedNetKg = pallet.ContentLines.Where(c => c.OriginCorrectionLineId == null).Sum(c => c.NetWeightKg),
            Depleted = pallet.ContentLines.Count > 0 && pallet.ContentLines.All(c => ReservationRules.IsDepleted(c.Quantity, c.NetWeightKg, c.DispatchedQuantity, c.DispatchedNetWeightKg)),
            LogisticsStatus = pallet.LogisticsStatus,
            LocationContext = pallet.LocationContext,
            WarehouseLocation = pallet.WarehouseLocation,
            CreatedAt = DateTime.SpecifyKind(pallet.CreatedAt, DateTimeKind.Utc),
            Version = pallet.Version,
            ContentLines = pallet.ContentLines.OrderBy(c => c.Id).Select(c => ToDto(c, pallet.Version)).ToList()
        };

        private static PalletContentLineDto ToDto(PalletContentLine line) => ToDto(line, line.Pallet.Version);

        private static PalletContentLineDto ToDto(PalletContentLine line, int palletVersion) => new()
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
            DispatchedQuantity = line.DispatchedQuantity,
            DispatchedNetWeightKg = line.DispatchedNetWeightKg,
            OnHandQuantity = ReservationRules.OnHandQuantity(line.Quantity, line.DispatchedQuantity),
            OnHandNetWeightKg = ReservationRules.OnHandQuantity(line.NetWeightKg, line.DispatchedNetWeightKg),
            Depleted = ReservationRules.IsDepleted(line.Quantity, line.NetWeightKg, line.DispatchedQuantity, line.DispatchedNetWeightKg),
            Origin = line.OriginCorrectionLineId == null ? "Receipt" : "DispatchCorrection",
            OriginCorrectionLineId = line.OriginCorrectionLineId,
            QualityStatus = line.QualityStatus,
            QualityReason = line.QualityReason,
            CreatedAt = DateTime.SpecifyKind(line.CreatedAt, DateTimeKind.Utc),
            Version = line.Version,
            PalletVersion = palletVersion
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
