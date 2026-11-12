using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
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
        Task<ServiceResult<FifoReservationProposalDto>> GetFifoProposalAsync(int salesOrderId, int salesOrderLineId, CancellationToken cancellationToken = default);
        Task<ServiceResult<IReadOnlyList<AwaitingStockLineDto>>> GetAwaitingStockAsync(CancellationToken cancellationToken = default);
    }

    // Schone verkooporders voor het nieuwe domein. Raakt nooit legacy Order/OrderLine/OrderLineAllocation/Inventory.
    // Idempotent via RequestId: de volledige genormaliseerde payload wordt vergeleken.
    public sealed partial class SalesOrderService : ISalesOrderService
    {
        private readonly AppDbContext _db;
        private readonly ICurrentUser _user;

        // Zonder ingelogde gebruiker (tests, seed) de standaardgebruiker; in de API altijd de gebruiker van het verzoek.
        public SalesOrderService(AppDbContext db, ICurrentUser currentUser = null)
        {
            _user = currentUser ?? CurrentUser.Default;
            _db = db;
        }

        // Pogingen bij een gelijktijdig toegekend verkoopordernummer (unieke index op jaar + volgnummer); daarna een echte fout.
        public const int MaxOrderNumberAttempts = 5;

        public async Task<ServiceResult<SalesOrderDto>> CreateAsync(CreateSalesOrderDto request, CancellationToken cancellationToken = default)
        {
            for (var attempt = 1; ; attempt++)
            {
                var (result, numberTaken) = await TryCreateAsync(request, cancellationToken);
                if (!numberTaken || attempt >= MaxOrderNumberAttempts)
                {
                    return result ?? throw new InvalidOperationException($"No free sales order number after {attempt} attempts.");
                }
                _db.ChangeTracker.Clear();
            }
        }

        private async Task<(ServiceResult<SalesOrderDto> Result, bool NumberTaken)> TryCreateAsync(CreateSalesOrderDto request, CancellationToken cancellationToken)
        {
            var errors = new Dictionary<string, List<string>>();
            var candidate = Build(request, errors);
            if (errors.Count > 0)
            {
                return (ServiceResult<SalesOrderDto>.Validation(ValidationErrors.ToArrays(errors)), false);
            }

            var existing = await OrderQuery().SingleOrDefaultAsync(o => o.RequestId == candidate.RequestId, cancellationToken);
            if (existing != null)
            {
                return (await WithWarningsAsync(await ResultForRetryAsync(existing, candidate, cancellationToken), cancellationToken), false);
            }

            // Nieuwe invoer: hele pallets (na de replaycontrole, zodat een eerder opgeslagen opdracht nog steeds herkend wordt).
            var lines = candidate.Lines.ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                PackagingRules.WholePalletErrors(lines[i].PalletCount, lines[i].PackagesPerPallet, $"lines[{i}]", errors, ValidationErrors.Add);
            }
            if (errors.Count > 0)
            {
                return (ServiceResult<SalesOrderDto>.Validation(ValidationErrors.ToArrays(errors)), false);
            }

            var refusedPackaging = await ResolvePackagingAsync(candidate, cancellationToken);
            if (refusedPackaging != null)
            {
                return (refusedPackaging, false);
            }

            var customerState = await _db.Customers.AsNoTracking().Where(c => c.Id == candidate.CustomerId).Select(c => new { c.Relation.IsActive }).SingleOrDefaultAsync(cancellationToken);
            if (customerState == null)
            {
                return (ServiceResult<SalesOrderDto>.NotFound($"Customer {candidate.CustomerId} does not exist."), false);
            }
            if (!customerState.IsActive)
            {
                // Inactieve relatie: historie blijft leesbaar, maar een nieuwe order wordt expliciet geweigerd (nooit stilzwijgend).
                return (ServiceResult<SalesOrderDto>.Validation(new Dictionary<string, string[]> { ["customerId"] = new[] { $"Customer {candidate.CustomerId} belongs to an inactive relation; reactivate the relation first or choose another customer." } }), false);
            }
            var variantIds = candidate.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
            var known = await _db.ProductVariants.Where(v => variantIds.Contains(v.Id))
                .Select(v => new { v.Id, v.DefaultNetKgPerPackage, v.DefaultGrossKgPerPackage, v.IsArchived }).ToDictionaryAsync(v => v.Id, cancellationToken);
            var missing = variantIds.Except(known.Keys).Cast<int?>().FirstOrDefault();
            if (missing != null)
            {
                return (ServiceResult<SalesOrderDto>.NotFound($"Product variant {missing} does not exist."), false);
            }
            var archived = known.Values.FirstOrDefault(v => v.IsArchived);
            if (archived != null)
            {
                var index = lines.FindIndex(l => l.ProductVariantId == archived.Id);
                return (ServiceResult<SalesOrderDto>.Validation(new Dictionary<string, string[]> { [$"lines[{index}].productVariantId"] = new[] { ArticleRules.ArchivedMessage(archived.Id) } }), false);
            }

            // Berekende kg: exact aantal × de huidige standaard van het artikel (na de replaycontrole, net als hele pallets).
            for (var i = 0; i < lines.Count; i++)
            {
                var variant = known[lines[i].ProductVariantId];
                LineWeightRules.CalculatedErrors(lines[i].Quantity, lines[i].NetWeightKg, lines[i].GrossWeightKg, lines[i].WeightSource,
                    variant.DefaultNetKgPerPackage, variant.DefaultGrossKgPerPackage, $"lines[{i}]", "netWeightKg", "grossWeightKg", errors, ValidationErrors.Add);
            }
            if (errors.Count > 0)
            {
                return (ServiceResult<SalesOrderDto>.Validation(ValidationErrors.ToArrays(errors)), false);
            }

            // Bron per regel tegen de vrije voorraad van precies deze variant en eenheid (vóór deze order). "Uit voorraad" met meer dan er
            // vrij op voorraad is wordt geweigerd; weggelaten wordt uit voorraad als het past en anders uit verwachte aanvoer (nooit stil
            // gesplitst). Een tekort op Voorraad + Verwacht blokkeert nooit: dat volgt als waarschuwing in het antwoord.
            var balances = await SalesBalanceLoader.LoadAsync(_db, variantIds, cancellationToken);
            var sourceConflict = ResolveSources(lines, balances, errors);
            if (errors.Count > 0)
            {
                return (ServiceResult<SalesOrderDto>.Validation(ValidationErrors.ToArrays(errors)), false);
            }
            if (sourceConflict != null)
            {
                return (ServiceResult<SalesOrderDto>.Conflict(sourceConflict), false);
            }

            // Verkoop-partij: een zelf getypt nummer na de dubbelcontrole (409 PARTIJ_EXISTS bij een bestaand nummer); in modus Own is het
            // verplicht. Zonder nummer kent AppDbContext bij opslaan het volgende voorgestelde nummer toe.
            var refusedPartij = await AttachSalesPartijAsync(request, candidate, cancellationToken);
            if (refusedPartij != null)
            {
                return (refusedPartij, false);
            }

            // Het ordernummer kent AppDbContext bij opslaan toe (volgende vrije in het jaar). Een gelijktijdige aanmaak kan hetzelfde
            // nummer (of partijnummer) kiezen; de unieke index weigert dan de tweede en CreateAsync probeert het opnieuw met een nieuw nummer.
            _db.SalesOrders.Add(candidate);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                // Gelijktijdige retry: de unieke index op RequestId heeft het tweede verzoek geweigerd.
                _db.ChangeTracker.Clear();
                var concurrent = await OrderQuery().SingleOrDefaultAsync(o => o.RequestId == candidate.RequestId, cancellationToken);
                if (concurrent != null)
                {
                    return (await WithWarningsAsync(await ResultForRetryAsync(concurrent, candidate, cancellationToken), cancellationToken), false);
                }
                // Anders bezette het nummer een gelijktijdige andere order: opnieuw met het volgende nummer.
                if (DbErrors.IsUniqueViolation(exception))
                {
                    return (null, true);
                }
                throw;
            }

            var created = await OrderQuery().SingleAsync(o => o.Id == candidate.Id, cancellationToken);
            return (await WithWarningsAsync(ServiceResult<SalesOrderDto>.Created(await ToDtoWithBalancesAsync(created, null, cancellationToken)), cancellationToken), false);
        }

        private async Task<ServiceResult<SalesOrderDto>> AttachSalesPartijAsync(CreateSalesOrderDto request, SalesOrder candidate, CancellationToken cancellationToken)
        {
            var settings = await PartijService.SettingsAsync(_db, cancellationToken);
            if (string.IsNullOrWhiteSpace(request.SalesPartijNumber) && settings.PartijNumberMode == PartijRules.ModeAuto)
            {
                return null;
            }
            var built = await new PartijService(_db, _user).BuildNewAsync<SalesOrderDto>(request.SalesPartijNumber, "salesPartijNumber", PartijRules.KindSales, cancellationToken);
            if (built.Refused != null)
            {
                return built.Refused;
            }
            built.Partij.CreatedAt = candidate.CreatedAt;
            built.Partij.SalesOrder = candidate;
            candidate.SalesPartij = built.Partij;
            _db.Partijen.Add(built.Partij);
            return null;
        }

        // Vult de bron van elke regel in en geeft een 409-reden als een regel "Uit voorraad" niet in de vrije voorraad past. Daarna mag
        // dezelfde variant + eenheid + bron maar één keer voorkomen (ook na het invullen van een weggelaten bron).
        private static string ResolveSources(List<SalesOrderLine> lines, IReadOnlyDictionary<(int ProductVariantId, string Unit), SalesBalance> balances, Dictionary<string, List<string>> errors)
        {
            var seen = new HashSet<(int, string, string)>();
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var freeStock = balances.TryGetValue((line.ProductVariantId, line.Unit), out var balance) ? Math.Max(0m, balance.FreeStock) : 0m;
                line.SupplySource = SalesSupplyRules.Resolve(line.SupplySource, line.Quantity, freeStock);
                if (!seen.Add((line.ProductVariantId, line.Unit, line.SupplySource)))
                {
                    ValidationErrors.Add(errors, $"lines[{i}]", "The same product variant, unit and source occur more than once.");
                    continue;
                }
                if (line.SupplySource == SalesSupplyRules.SourceStock && line.Quantity > freeStock)
                {
                    var (stock, expected) = SalesSupplyRules.Propose(line.Quantity, freeStock);
                    static string N(decimal value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    return $"lines[{i}]: only {N(freeStock)} {line.Unit} of this article are free in stock, not {N(line.Quantity)}. "
                        + (stock > 0m
                            ? $"Split the line ({N(stock)} {line.Unit} from stock, {N(expected)} {line.Unit} from expected supply) or sell it all from expected supply."
                            : "Sell it from expected supply.");
                }
            }
            return null;
        }

        // Signalen bij aanmaken, nooit blokkerend: openstaand bedrag boven de kredietlimiet van de klant, en per variant + eenheid van deze
        // order een tekort (meer verkocht dan voorraad en verwachte aanvoer samen; de stand staat al in de regels van het antwoord).
        private async Task<ServiceResult<SalesOrderDto>> WithWarningsAsync(ServiceResult<SalesOrderDto> result, CancellationToken cancellationToken)
        {
            if (result.Status is ServiceResultStatus.Created or ServiceResultStatus.Ok && result.Value != null)
            {
                var exposure = await new CreditExposureService(_db).GetExposureAsync(result.Value.CustomerId, _db.BusinessCalendar.Today(), cancellationToken);
                result.Value.CreditWarning = CreditExposureService.WarningFor(exposure);
                result.Value.ShortageWarnings = result.Value.Lines
                    .Where(l => l.Balance != null && l.Balance.Shortage)
                    .GroupBy(l => (l.ProductVariantId, l.Unit))
                    .Select(g => g.First())
                    .Select(l => new SalesShortageWarningDto { ProductVariantId = l.ProductVariantId, ProductName = l.ProductName, VariantName = l.ProductVariantName, Unit = l.Unit, ShortageQuantity = l.Balance.ShortageQuantity })
                    .ToList();
            }
            return result;
        }

        private SalesOrder Build(CreateSalesOrderDto request, Dictionary<string, List<string>> errors)
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
            // Interne opmerking: hoort bij deze order, alleen intern zichtbaar, telt mee in de replayvergelijking.
            var internalNote = ValidationErrors.OptionalText(request.InternalNote, ReservationRules.MaxInternalNoteLength, "internalNote", errors);
            if (request.RequestedDeliveryDate == null)
            {
                ValidationErrors.Add(errors, "requestedDeliveryDate", "A requested delivery date is required.");
            }
            var actor = _user.Actor(ReservationRules.MaxActorLength);
            if (!string.IsNullOrWhiteSpace(request.SalesPartijNumber) && PartijService.NumberError(request.SalesPartijNumber) is { } partijError)
            {
                ValidationErrors.Add(errors, "salesPartijNumber", partijError);
            }

            var lines = new List<SalesOrderLine>();
            // Goederen, emballage of allebei: een order met alleen emballage (pallets, kratten) mag ook.
            var packagingLines = BuildPackagingLines(request.PackagingLines, errors);
            if ((request.Lines?.Count ?? 0) == 0)
            {
                if ((request.PackagingLines?.Count ?? 0) == 0)
                {
                    ValidationErrors.Add(errors, "lines", "At least one line is required.");
                }
            }
            else if (request.Lines.Count > ReservationRules.MaxOrderLines)
            {
                ValidationErrors.Add(errors, "lines", $"At most {ReservationRules.MaxOrderLines} lines are allowed.");
            }
            else
            {
                var seen = new HashSet<(int, string, string)>();
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
                    var palletInput = line.PalletCount != null || line.PackagesPerPallet != null;
                    if (line.Quantity == null ? !palletInput : !SupplyRules.IsPositiveDecimal18_3(line.Quantity.Value))
                    {
                        ValidationErrors.Add(errors, $"{key}.quantity", "Quantity is required (or pallet count × packages per pallet), must be positive and may have at most 3 decimals.");
                    }
                    if (!SupplyRules.IsPackageUnit(line.Unit))
                    {
                        ValidationErrors.Add(errors, $"{key}.unit", $"Unit must be one of: {string.Join(", ", SupplyRules.PackageUnits)}.");
                    }
                    // Palletinvoer: het totaal volgt uit pallets × verpakkingen per pallet + losse verpakkingen, of moet daar exact aan gelijk zijn.
                    var total = PackagingRules.Validate(line.PalletCount, line.PackagesPerPallet, line.LoosePackages, line.Quantity, key, "quantity", errors, ValidationErrors.Add);
                    // Kg optioneel: positief, bruto >= netto; een bron alleen met kg (zonder bron = Entered).
                    var weightSource = LineWeightRules.Validate(line.NetWeightKg, line.GrossWeightKg, line.WeightSource, key, "netWeightKg", "grossWeightKg", errors, ValidationErrors.Add);
                    if (line.SupplySource != null && !SalesSupplyRules.IsSource(line.SupplySource))
                    {
                        ValidationErrors.Add(errors, $"{key}.supplySource", $"Supply source must be one of: {string.Join(", ", SalesSupplyRules.Sources)}.");
                    }
                    if (errors.Count > before || total == null)
                    {
                        continue;
                    }
                    // Eén regel per variant + eenheid + bron: anders worden reserveringen dubbelzinnig. Een weggelaten bron wordt pas bij
                    // aanmaken ingevuld (ResolveSources controleert daarna opnieuw).
                    if (!seen.Add((line.ProductVariantId!.Value, line.Unit, line.SupplySource)))
                    {
                        ValidationErrors.Add(errors, key, "The same product variant, unit and source occur more than once.");
                        continue;
                    }
                    // SupplySource blijft null als de aanvrager er geen opgaf: de replayvergelijking accepteert dan elke opgeslagen bron.
                    lines.Add(new SalesOrderLine { ProductVariantId = line.ProductVariantId.Value, Quantity = total.Value, Unit = line.Unit, PalletCount = line.PalletCount, PackagesPerPallet = line.PackagesPerPallet, LoosePackages = line.LoosePackages,
                        NetWeightKg = line.NetWeightKg, GrossWeightKg = line.GrossWeightKg, WeightSource = weightSource, SupplySource = line.SupplySource });
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
            var partijNumber = string.IsNullOrWhiteSpace(request.SalesPartijNumber) ? null : request.SalesPartijNumber.Trim();
            return new SalesOrder
            {
                RequestId = request.RequestId!.Value,
                CustomerId = request.CustomerId!.Value,
                RequestPartijKey = partijNumber == null ? "auto" : $"new:{PartijRules.NumberKey(partijNumber)}",
                Status = ReservationRules.OrderStatusOpen,
                CustomerReference = reference,
                InternalNote = internalNote,
                RequestedDeliveryDate = UtcPrecision.ToStoredUtc(request.RequestedDeliveryDate!.Value),
                ActorReference = actor,
                CreatedAt = now,
                UpdatedAt = now,
                Lines = lines,
                PackagingLines = packagingLines
            };
        }

        // Strikt: klant, genormaliseerde referentie, leverdatum (UTC, microseconden), actor en de regels in volgorde (met kg en bron;
        // een oudere opdracht zonder kg komt overeen met een regel zonder kg; zonder opgegeven verkoopbron telt elke opgeslagen bron).
        private async Task<ServiceResult<SalesOrderDto>> ResultForRetryAsync(SalesOrder existing, SalesOrder candidate, CancellationToken cancellationToken)
        {
            var existingLines = existing.Lines.OrderBy(l => l.Id).ToList();
            var candidateLines = candidate.Lines.ToList();
            // Partij: vergeleken met de oorspronkelijke aanvraag (RequestPartijKey), niet met het huidige nummer (dat kan intussen aangepast
            // of gecorrigeerd zijn). Een order van vóór de verkoop-partij is gelijk aan een aanvraag zonder eigen nummer.
            var samePartij = existing.RequestPartijKey == null ? candidate.RequestPartijKey == "auto"
                : string.Equals(existing.RequestPartijKey, candidate.RequestPartijKey, StringComparison.Ordinal);
            var same = samePartij && existing.CustomerId == candidate.CustomerId
                && string.Equals(existing.CustomerReference, candidate.CustomerReference, StringComparison.Ordinal)
                && string.Equals(existing.InternalNote, candidate.InternalNote, StringComparison.Ordinal)
                && existing.RequestedDeliveryDate.Ticks == candidate.RequestedDeliveryDate.Ticks
                && string.Equals(existing.ActorReference, candidate.ActorReference, StringComparison.Ordinal)
                && SamePackaging(existing, candidate)
                && existingLines.Count == candidateLines.Count
                && existingLines.Zip(candidateLines).All(pair =>
                    pair.First.ProductVariantId == pair.Second.ProductVariantId
                    && pair.First.Quantity == pair.Second.Quantity
                    && pair.First.PalletCount == pair.Second.PalletCount
                    && pair.First.PackagesPerPallet == pair.Second.PackagesPerPallet
                    && pair.First.LoosePackages == pair.Second.LoosePackages
                    && pair.First.NetWeightKg == pair.Second.NetWeightKg
                    && pair.First.GrossWeightKg == pair.Second.GrossWeightKg
                    && string.Equals(pair.First.WeightSource, pair.Second.WeightSource, StringComparison.Ordinal)
                    && (pair.Second.SupplySource == null || string.Equals(pair.First.SupplySource, pair.Second.SupplySource, StringComparison.Ordinal))
                    && string.Equals(pair.First.Unit, pair.Second.Unit, StringComparison.Ordinal));

            return same
                ? ServiceResult<SalesOrderDto>.Ok(await ToDtoWithBalancesAsync(existing, null, cancellationToken))
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
            return await ToDtoWithBalancesAsync(order, actors.GetValueOrDefault(order.Id), cancellationToken, corrections);
        }

        // Met de verkoopstand van de varianten van deze order (alleen bij een open order: een geannuleerde order vraagt niets meer).
        private async Task<SalesOrderDto> ToDtoWithBalancesAsync(SalesOrder order, string cancelledBy, CancellationToken cancellationToken, IReadOnlyDictionary<int, int> correctionIds = null)
        {
            var balances = order.Status == ReservationRules.OrderStatusOpen
                ? await SalesBalanceLoader.LoadAsync(_db, order.Lines.Select(l => l.ProductVariantId).Distinct().ToList(), cancellationToken)
                : null;
            return ToDto(order, cancelledBy, correctionIds, balances);
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
            if (!string.IsNullOrWhiteSpace(filter.Q))
            {
                // Deel van de tekst, hoofdletterongevoelig (ToLower werkt in PostgreSQL en SQLite): VO-nummer, klantreferentie, klant,
                // artikel (soort of variant) en partij (van een reservering op een pallet of van de inkoopregel).
                var q = filter.Q.Trim().ToLower();
                var partijKey = PartijRules.NumberKey(filter.Q);
                query = query.Where(o => o.OrderNumber.ToLower().Contains(q)
                    || (partijKey != null && o.SalesPartij != null && o.SalesPartij.NumberKey.Contains(partijKey))
                    || (o.CustomerReference != null && o.CustomerReference.ToLower().Contains(q))
                    || o.Customer.Name.ToLower().Contains(q)
                    || o.PackagingLines.Any(p => p.CostArticle.Name.ToLower().Contains(q))
                    || o.Lines.Any(l => l.ProductVariant.Name.ToLower().Contains(q) || l.ProductVariant.Product.Name.ToLower().Contains(q)
                        || l.Reservations.Any(r => (r.PalletContentLine != null && r.PalletContentLine.LotNumber.ToLower().Contains(q))
                            || (r.SupplyCommitmentLine.LotNumber != null && r.SupplyCommitmentLine.LotNumber.ToLower().Contains(q)))));
            }
            var orders = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Take(ReservationRules.MaxListResults).ToListAsync(cancellationToken);
            var actors = await CancelledByAsync(orders.Where(o => o.Status == ReservationRules.OrderStatusCancelled).Select(o => o.Id).ToList(), cancellationToken);
            var corrections = await CorrectionIdsAsync(orders.Select(o => o.Id).ToList(), cancellationToken);
            // Eén stand voor alle varianten van de open orders in de lijst (de dekking hangt van alle open orders af, niet alleen van deze).
            var openVariants = orders.Where(o => o.Status == ReservationRules.OrderStatusOpen).SelectMany(o => o.Lines).Select(l => l.ProductVariantId).Distinct().ToList();
            var balances = await SalesBalanceLoader.LoadAsync(_db, openVariants, cancellationToken);
            return ServiceResult<IReadOnlyList<SalesOrderDto>>.Ok(orders.Select(o => ToDto(o, actors.GetValueOrDefault(o.Id), corrections, balances)).ToList());
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
                var actor = _user.Actor(ReservationRules.MaxActorLength);
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
                return ServiceResult<SalesOrderDto>.Conflict($"Sales order {salesOrderId} has consumed reservations (dispatched from the warehouse or shipped as DirectTrade); returns and shipment corrections are a separate flow.");
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
                    c.Id, c.PalletId, PalletReference = c.Pallet.Reference, c.Pallet.WarehouseLocation, c.LotNumber, c.Ggn, c.Unit,
                    c.Quantity, c.NetWeightKg, c.WeightMethod, c.NetKgPerPackage, c.DispatchedQuantity, c.DispatchedNetWeightKg, c.SplitOffQuantity, c.SplitOffNetWeightKg, c.QualityStatus, c.Version, PalletCreatedAt = c.Pallet.CreatedAt,
                    c.CreatedAt, c.SupplyCommitmentLine.SupplyCommitmentId, SupplyOrderNumber = c.SupplyCommitmentLine.SupplyCommitment.OrderNumber
                })
                .ToListAsync(cancellationToken);

            var reserved = await ReservedByContentLineAsync(_db, contents.Select(c => c.Id).ToList(), cancellationToken);
            var result = contents
                .Select(c =>
                {
                    var (quantity, kg) = reserved.GetValueOrDefault(c.Id);
                    var onHandQuantity = ReservationRules.OnHandQuantity(c.Quantity, c.DispatchedQuantity, c.SplitOffQuantity);
                    var onHandKg = ReservationRules.OnHandQuantity(c.NetWeightKg, c.DispatchedNetWeightKg, c.SplitOffNetWeightKg);
                    return new ReservableStockLineDto
                    {
                        PalletContentLineId = c.Id,
                        PalletId = c.PalletId,
                        PalletReference = c.PalletReference,
                        WarehouseLocation = c.WarehouseLocation,
                        LotNumber = c.LotNumber,
                        Ggn = c.Ggn,
                        Unit = c.Unit,
                        Quantity = c.Quantity,
                        NetWeightKg = c.NetWeightKg,
                        WeightMethod = c.WeightMethod,
                        NetKgPerPackage = c.NetKgPerPackage,
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
                        ContentLineVersion = c.Version,
                        ReceivedAt = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
                        SupplyCommitmentId = c.SupplyCommitmentId,
                        SupplyOrderNumber = c.SupplyOrderNumber
                    };
                })
                .Where(c => c.AvailableQuantity > 0m)
                .OrderBy(c => c.PalletReference, StringComparer.Ordinal)
                .ThenBy(c => c.PalletContentLineId)
                .ToList();
            return ServiceResult<IReadOnlyList<ReservableStockLineDto>>.Ok(result);
        }

        // ======================== Verkopen vóór aankomst: oudste partij eerst en de werklijst ========================

        // Oudste partij eerst (FIFO): op ontvangstmoment (registratie van de inhoudsregel; er is geen apart ontvangstveld), dan partijnummer,
        // dan inhoudsregel. Verdeelt wat de regel nog nodig heeft over zoveel pallets als nodig; niets wordt opgeslagen.
        public async Task<ServiceResult<FifoReservationProposalDto>> GetFifoProposalAsync(int salesOrderId, int salesOrderLineId, CancellationToken cancellationToken = default)
        {
            var reservable = await GetReservableStockAsync(salesOrderId, salesOrderLineId, cancellationToken);
            if (reservable.Status != ServiceResultStatus.Ok)
            {
                return ServiceResult<FifoReservationProposalDto>.NotFound(reservable.Message);
            }
            var line = await _db.SalesOrderLines.AsNoTracking().SingleAsync(l => l.Id == salesOrderLineId, cancellationToken);
            var needed = Math.Max(0m, line.Quantity - await CommittedOnOrderLineAsync(_db, line.Id, cancellationToken));
            var candidates = FifoOrder(reservable.Value).ToList();
            var items = ProposeFifo(candidates, needed);
            return ServiceResult<FifoReservationProposalDto>.Ok(new FifoReservationProposalDto
            {
                SalesOrderId = salesOrderId,
                SalesOrderLineId = salesOrderLineId,
                Unit = line.Unit,
                NeededQuantity = needed,
                ProposedQuantity = items.Sum(i => i.Quantity),
                Candidates = candidates,
                Items = items
            });
        }

        internal static IEnumerable<ReservableStockLineDto> FifoOrder(IEnumerable<ReservableStockLineDto> candidates) =>
            candidates
                .OrderBy(c => c.ReceivedAt)
                .ThenBy(c => c.LotNumber, StringComparer.Ordinal)
                .ThenBy(c => c.PalletContentLineId);

        // De hele rest van een inhoudsregel neemt exact de resterende kg; een deel van berekend gewicht exact aantal × kg per verpakking;
        // een deel van gewogen inhoud naar verhouding (3 decimalen, altijd onder de rest zodat de "geen gestrande rest"-regel klopt).
        internal static List<FifoReservationItemDto> ProposeFifo(IReadOnlyList<ReservableStockLineDto> candidates, decimal needed)
        {
            var items = new List<FifoReservationItemDto>();
            foreach (var candidate in candidates)
            {
                if (needed <= 0m) break;
                var quantity = Math.Min(needed, candidate.AvailableQuantity);
                if (quantity <= 0m) continue;
                decimal kg;
                if (quantity == candidate.AvailableQuantity)
                {
                    kg = candidate.AvailableNetWeightKg;
                }
                else if (candidate.WeightMethod == WeightRules.Calculated && candidate.NetKgPerPackage != null
                    && WeightRules.TryExpectedReservationKg(quantity, candidate.NetKgPerPackage.Value, candidate.AvailableQuantity, candidate.AvailableNetWeightKg, out var calculated))
                {
                    kg = calculated;
                }
                else
                {
                    kg = Math.Round(candidate.AvailableNetWeightKg * quantity / candidate.AvailableQuantity, 3, MidpointRounding.AwayFromZero);
                    if (kg >= candidate.AvailableNetWeightKg) kg = candidate.AvailableNetWeightKg - 0.001m;
                }
                if (kg <= 0m) continue;
                items.Add(new FifoReservationItemDto { PalletContentLineId = candidate.PalletContentLineId, Quantity = quantity, NetWeightKg = kg });
                needed -= quantity;
            }
            return items;
        }

        // Werklijst "Verkocht, wacht op voorraad": open regels uit verwachte aanvoer waarvoor nu vrije, goedgekeurde voorraad is, met de
        // dekking oudste verkoop eerst (eerst de regels uit voorraad). Vroegste leverdatum bovenaan. Reserveert nooit zelf.
        public async Task<ServiceResult<IReadOnlyList<AwaitingStockLineDto>>> GetAwaitingStockAsync(CancellationToken cancellationToken = default)
        {
            var variants = await _db.SalesOrderLines.AsNoTracking()
                .Where(l => l.SupplySource == SalesSupplyRules.SourceExpected && l.SalesOrder.Status == ReservationRules.OrderStatusOpen)
                .Select(l => l.ProductVariantId).Distinct().ToListAsync(cancellationToken);
            var balances = await SalesBalanceLoader.LoadAsync(_db, variants, cancellationToken);
            var result = balances.Values
                .SelectMany(b => b.Lines.Where(l => l.SupplySource == SalesSupplyRules.SourceExpected).Select(l => (Balance: b, Line: l, Coverage: b.Coverage[l.SalesOrderLineId])))
                .Where(x => x.Coverage.FromStock > 0m)
                .OrderBy(x => x.Line.RequestedDeliveryDate).ThenBy(x => x.Line.OrderCreatedAt).ThenBy(x => x.Line.SalesOrderLineId)
                .Select(x => new AwaitingStockLineDto
                {
                    SalesOrderId = x.Line.SalesOrderId,
                    OrderNumber = x.Line.OrderNumber,
                    CustomerId = x.Line.CustomerId,
                    CustomerName = x.Line.CustomerName,
                    CustomerReference = x.Line.CustomerReference,
                    RequestedDeliveryDate = x.Line.RequestedDeliveryDate,
                    SalesOrderLineId = x.Line.SalesOrderLineId,
                    ProductVariantId = x.Line.ProductVariantId,
                    ProductName = x.Line.ProductName,
                    VariantName = x.Line.VariantName,
                    Unit = x.Line.Unit,
                    UnreservedQuantity = x.Line.Unreserved,
                    ReservableNow = x.Coverage.FromStock,
                    Coverage = SalesBalanceLoader.ToCoverageDto(x.Coverage)
                })
                .ToList();
            return ServiceResult<IReadOnlyList<AwaitingStockLineDto>>.Ok(result);
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
                .Include(o => o.SalesPartij)
                .Include(o => o.Lines).ThenInclude(l => l.ProductVariant).ThenInclude(v => v.Product)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.PalletContentLine).ThenInclude(c => c.Pallet)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.PalletContentLine).ThenInclude(c => c.Partij)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.PalletContentLine).ThenInclude(c => c.SupplyCommitmentLine).ThenInclude(s => s.SupplyCommitment).ThenInclude(c => c.Vendor)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.SupplyCommitmentLine).ThenInclude(s => s.SupplyCommitment).ThenInclude(c => c.Vendor)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.SupplyCommitmentLine).ThenInclude(s => s.Partij)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.DispatchLine).ThenInclude(d => d.SalesDispatch).ThenInclude(s => s.DeliveryConfirmations).ThenInclude(c => c.Retraction)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.DirectTradeShipmentLine).ThenInclude(d => d.DirectTradeShipment).ThenInclude(s => s.DeliveryConfirmations).ThenInclude(c => c.Retraction)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.DirectTradeCorrectionLine)
                .Include(o => o.PackagingLines).ThenInclude(p => p.CostArticle)
                .AsSplitQuery();

        // Bevestigd afgeleverd op een regel: uitsluitend Consumed reserveringen waarvan de bijbehorende verzending een bevestiging heeft.
        internal static decimal DeliveredQuantity(SalesOrderLine line) =>
            line.Reservations.Where(r => r.Status == ReservationRules.StatusConsumed && StockReservationService.DeliveryOf(r) != null).Sum(r => r.Quantity);

        internal static SalesOrderDto ToDto(SalesOrder order, string cancelledBy = null, IReadOnlyDictionary<int, int> correctionIds = null,
            IReadOnlyDictionary<(int ProductVariantId, string Unit), SalesBalance> balances = null)
        {
            var dto = ToDtoCore(order, cancelledBy, correctionIds, order.Status == ReservationRules.OrderStatusOpen ? balances : null);
            dto.Shortage = dto.Lines.Any(l => l.Coverage != null && l.Coverage.Shortage > 0m);
            dto.AwaitingStock = dto.Lines.Any(l => l.AwaitingStock);
            return dto;
        }

        private static SalesOrderDto ToDtoCore(SalesOrder order, string cancelledBy, IReadOnlyDictionary<int, int> correctionIds,
            IReadOnlyDictionary<(int ProductVariantId, string Unit), SalesBalance> balances) => new()
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            SalesPartijId = order.SalesPartij?.Id,
            SalesPartijNumber = order.SalesPartij?.Number,
            // Zelfde regel als PartijService.SalesNumberFixedAsync: geannuleerd, of iets gereserveerd (ook toegewezen, verzonden of vrijgegeven).
            SalesPartijFixed = order.Status != ReservationRules.OrderStatusOpen || order.Lines.Any(l => l.Reservations.Count > 0),
            RequestId = order.RequestId,
            CustomerId = order.CustomerId,
            CustomerName = order.Customer?.Name,
            Status = order.Status,
            CustomerReference = order.CustomerReference,
            InternalNote = order.InternalNote,
            RequestedDeliveryDate = DateTime.SpecifyKind(order.RequestedDeliveryDate, DateTimeKind.Utc),
            ActorReference = order.ActorReference,
            CreatedAt = DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc),
            Version = order.Version,
            ConfirmedAt = order.ConfirmedAt == null ? null : DateTime.SpecifyKind(order.ConfirmedAt.Value, DateTimeKind.Utc),
            ConfirmedBy = order.ConfirmedBy,
            CancelledAt = order.CancelledAt == null ? null : DateTime.SpecifyKind(order.CancelledAt.Value, DateTimeKind.Utc),
            CancelReason = order.CancelReason,
            CancelledBy = order.Status == ReservationRules.OrderStatusCancelled ? cancelledBy : null,
            Fulfilled = order.Lines.Count > 0 && order.Lines.All(l => l.Reservations.Where(r => r.Status == ReservationRules.StatusConsumed).Sum(r => r.Quantity) == l.Quantity),
            // Per regel in de eigen eenheid; nooit één ordertotaal over box en colli heen.
            Delivered = order.Lines.Count > 0 && order.Lines.All(l => DeliveredQuantity(l) == l.Quantity),
            PackagingLines = order.PackagingLines.OrderBy(l => l.Id).Select(ToPackagingDto).ToList(),
            Lines = order.Lines.OrderBy(l => l.Id).Select(line =>
            {
                // Gereserveerd = Active Physical; toegewezen = Active DirectTrade; verzonden = Consumed. Open trekt alle drie af.
                var active = line.Reservations.Where(r => r.Status == ReservationRules.StatusActive && r.Source != ReservationRules.SourceDirectTrade).ToList();
                var allocated = line.Reservations.Where(r => r.Status == ReservationRules.StatusActive && r.Source == ReservationRules.SourceDirectTrade).ToList();
                var consumed = line.Reservations.Where(r => r.Status == ReservationRules.StatusConsumed).ToList();
                var reservedQuantity = active.Sum(r => r.Quantity);
                var allocatedQuantity = allocated.Sum(r => r.Quantity);
                var dispatchedQuantity = consumed.Sum(r => r.Quantity);
                var balance = balances != null && balances.TryGetValue((line.ProductVariantId, line.Unit), out var found) ? found : null;
                var coverage = balance != null && balance.Coverage.TryGetValue(line.Id, out var covered) ? covered : null;
                return new SalesOrderLineDto
                {
                    Id = line.Id,
                    ProductVariantId = line.ProductVariantId,
                    ProductName = line.ProductVariant?.Product?.Name,
                    ProductVariantName = line.ProductVariant?.Name,
                    Quantity = line.Quantity,
                    Unit = line.Unit,
                    PalletCount = line.PalletCount,
                    PackagesPerPallet = line.PackagesPerPallet,
                    LoosePackages = line.LoosePackages,
                    NetWeightKg = line.NetWeightKg,
                    GrossWeightKg = line.GrossWeightKg,
                    WeightSource = line.WeightSource,
                    SupplySource = line.SupplySource,
                    Balance = balance == null ? null : SalesBalanceLoader.ToBalanceDto(balance),
                    Coverage = coverage == null ? null : SalesBalanceLoader.ToCoverageDto(coverage),
                    AwaitingStock = line.SupplySource == SalesSupplyRules.SourceExpected && coverage != null
                        && coverage.FromStock + coverage.FromExpected + coverage.Shortage > 0m,
                    ReservedQuantity = reservedQuantity,
                    ReservedNetWeightKg = active.Sum(r => r.NetWeightKg ?? 0m),
                    AllocatedQuantity = allocatedQuantity,
                    DispatchedQuantity = dispatchedQuantity,
                    // Physical: kg van de reservering; DirectTrade: de werkelijk verzonden kg uit de verzendregel (nooit afgeleid).
                    DispatchedNetWeightKg = consumed.Sum(r => r.NetWeightKg ?? r.DirectTradeShipmentLine?.NetWeightKg ?? 0m),
                    DirectTradeShippedQuantity = consumed.Where(r => r.Source == ReservationRules.SourceDirectTrade).Sum(r => r.Quantity),
                    // Open = gevraagd - Active Physical - Active DirectTrade - Consumed: verzonden komt nooit terug als open vraag.
                    OpenQuantity = Math.Max(0m, line.Quantity - reservedQuantity - allocatedQuantity - dispatchedQuantity),
                    Fulfilled = dispatchedQuantity == line.Quantity,
                    DeliveredQuantity = DeliveredQuantity(line),
                    Delivered = DeliveredQuantity(line) == line.Quantity,
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
