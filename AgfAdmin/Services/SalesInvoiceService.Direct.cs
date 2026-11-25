using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Verkoopfactuur 1, directe factuur (zonder order): een eigen V-nummer (zoals op 23 november gebouwd), productregels uit een gekozen
    // inkoop-partij en vrije of dienstregels met een omzetsoort. Een productregel reserveert bij het concept de oudste pallets van die partij
    // (regel 46); Definitief boekt ze af, weggooien of de regel wijzigen geeft ze vrij. Meer dan vrij in de partij kan niet.
    public sealed partial class SalesInvoiceService
    {
        public const string StockChangedCode = "STOCK_CHANGED";
        private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

        // Een geplande regel: de nieuwe factuurregel en, bij een productregel, de verdeling over pallets (oudste eerst).
        private sealed record PlannedLine(InvoiceLine Line, List<(int ContentId, decimal Quantity, decimal Kg, string Ggn)> Allocations);

        public async Task<ServiceResult<SalesInvoiceDto>> CreateDirectAsync(CreateDirectInvoiceDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, string[]>();
            if (request?.RequestId == null || request.RequestId == Guid.Empty)
            {
                errors["requestId"] = new[] { "A client-generated, non-empty requestId is required." };
            }
            if (request?.CustomerId == null)
            {
                errors["customerId"] = new[] { "Choose the customer." };
            }
            var lines = request?.Lines ?? new List<SalesInvoiceNewLineDto>();
            if (lines.Count == 0)
            {
                errors["lines"] = new[] { "Add at least one line." };
            }
            else if (lines.Count > SalesInvoiceRules.MaxLinesPerInvoice)
            {
                errors["lines"] = new[] { $"At most {SalesInvoiceRules.MaxLinesPerInvoice} lines per invoice." };
            }
            var reference = string.IsNullOrWhiteSpace(request?.ReferenceNumber) ? null : request.ReferenceNumber.Trim();
            if (reference != null && reference.Length > SalesInvoiceRules.MaxReferenceLength)
            {
                errors["referenceNumber"] = new[] { $"This field may contain at most {SalesInvoiceRules.MaxReferenceLength} characters." };
            }
            if (errors.Count > 0)
            {
                return ServiceResult<SalesInvoiceDto>.Validation(errors);
            }
            var replay = await db.Invoices.AsNoTracking().Where(i => i.RequestId == request.RequestId).Select(i => new { i.Id, i.Kind, i.CustomerId }).SingleOrDefaultAsync(cancellationToken);
            if (replay != null)
            {
                return replay.Kind == SalesInvoiceRules.KindDirect && replay.CustomerId == request.CustomerId
                    ? ServiceResult<SalesInvoiceDto>.Ok(await DtoAsync(replay.Id, cancellationToken))
                    : ServiceResult<SalesInvoiceDto>.Conflict($"RequestId {request.RequestId} was already used for another invoice.");
            }
            var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
            if (customer == null)
            {
                return ServiceResult<SalesInvoiceDto>.Validation(new Dictionary<string, string[]> { ["customerId"] = new[] { $"Customer {request.CustomerId} does not exist." } });
            }

            var partijen = new PartijService(db, _user);
            for (var attempt = 1; ; attempt++)
            {
                var planned = await PlanNewLinesAsync(lines, "lines", null, errors, cancellationToken);
                if (errors.Count > 0)
                {
                    return ServiceResult<SalesInvoiceDto>.Validation(errors);
                }
                var lot = await partijen.BuildDirectInvoiceLotAsync<SalesInvoiceDto>(request.LotNumber, "lotNumber", cancellationToken);
                if (lot.Refused != null)
                {
                    return lot.Refused;
                }
                var now = DateTime.UtcNow;
                var invoice = new Invoice
                {
                    Kind = SalesInvoiceRules.KindDirect, Status = SalesInvoiceRules.StatusConcept, RequestId = request.RequestId, CreatedBy = Actor(), CreatedAt = now,
                    CustomerId = customer.Id, CustomCustomerName = customer.Name, InvoiceDate = now, PaymentStatus = "Pending", ReferenceNumber = reference,
                    DeliveryDate = DeliveryDateOf(request.DeliveryDate, now), BatchNumber = lot.Partij.Number, SalesPartij = lot.Partij, UpdatedAt = now
                };
                lot.Partij.Invoice = invoice;
                AddPlanned(invoice, planned, await ContentsAsync(planned, cancellationToken), now);
                SalesInvoiceRules.RecalculateTotals(invoice);
                db.Invoices.Add(invoice);
                db.Partijen.Add(lot.Partij);
                try
                {
                    await SaveLockedAsync(invoice, cancellationToken);
                    return ServiceResult<SalesInvoiceDto>.Created(await DtoAsync(invoice.Id, cancellationToken));
                }
                catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception) && attempt < PartijService.MaxNumberAttempts)
                {
                    // Een gelijktijdige schrijver koos hetzelfde V-nummer (of dezelfde sleutel): opnieuw.
                    db.ChangeTracker.Clear();
                    var again = await db.Invoices.AsNoTracking().AnyAsync(i => i.RequestId == request.RequestId, cancellationToken);
                    if (again)
                    {
                        return await CreateDirectAsync(request, cancellationToken);
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    db.ChangeTracker.Clear();
                    return StockChanged();
                }
            }
        }

        private static DateTime DeliveryDateOf(DateOnly? date, DateTime now) =>
            date == null ? now : DateTime.SpecifyKind(date.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        private static ServiceResult<SalesInvoiceDto> StockChanged()
        {
            const string message = "The free stock of a lot changed in the meantime. Check the lot and try again.";
            return ServiceResult<SalesInvoiceDto>.Conflict(message, new { code = StockChangedCode, message });
        }

        // Nieuwe regels controleren en, voor productregels, verdelen over de oudste vrije pallets van de gekozen partij. Eigen actieve
        // reserveringen van deze factuur (releasing) tellen weer als vrij, omdat ze in dezelfde save worden vrijgegeven.
        private async Task<List<PlannedLine>> PlanNewLinesAsync(List<SalesInvoiceNewLineDto> lines, string field, Dictionary<int, decimal> releasing,
            Dictionary<string, string[]> errors, CancellationToken cancellationToken)
        {
            var planned = new List<PlannedLine>();
            var variantIds = lines.Where(l => l?.ProductVariantId != null).Select(l => l.ProductVariantId.Value).Distinct().ToList();
            var variants = await db.ProductVariants.AsNoTracking().Include(v => v.Product).Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken);
            var lotIds = lines.Where(l => l?.PartijId != null).Select(l => l.PartijId.Value).Distinct().ToList();
            var lots = await db.Partijen.AsNoTracking().Where(p => lotIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
            var articleIds = lines.Where(l => l?.CostArticleId != null).Select(l => l.CostArticleId.Value).Distinct().ToList();
            var articles = await db.CostArticles.AsNoTracking().Where(a => articleIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
            var revenueTypes = await db.RevenueTypes.AsNoTracking().Where(r => r.IsActive).ToDictionaryAsync(r => r.Id, cancellationToken);
            var defaultRevenueType = revenueTypes.Values.FirstOrDefault(r => r.IsDefault)?.Id;
            var used = new Dictionary<int, (decimal Quantity, decimal Kg)>();
            var stockCache = new Dictionary<(int, string), List<ReservableStockLineDto>>();

            for (var i = 0; i < lines.Count; i++)
            {
                var dto = lines[i];
                var key = $"{field}[{i}]";
                if (dto == null)
                {
                    errors[key] = new[] { "A line is required." };
                    continue;
                }
                var quantity = dto.Quantity ?? 0m;
                var line = new InvoiceLine { LineKind = dto.LineKind, Quantity = quantity, UnitPrice = dto.UnitPrice, DiscountPercentage = dto.DiscountPercentage ?? 0m };
                if (dto.LineKind == SalesInvoiceRules.LineProduct)
                {
                    if (dto.ProductVariantId == null || !variants.TryGetValue(dto.ProductVariantId.Value, out var variant))
                    {
                        errors[$"{key}.productVariantId"] = new[] { "Choose an article." };
                        continue;
                    }
                    if (dto.PartijId == null)
                    {
                        errors[$"{key}.partijId"] = new[] { "Choose the lot this comes from." };
                        continue;
                    }
                    if (!lots.TryGetValue(dto.PartijId.Value, out var lot) || lot.Kind != PartijRules.KindPurchase)
                    {
                        errors[$"{key}.partijId"] = new[] { "A line of a direct invoice comes from a purchase lot." };
                        continue;
                    }
                    if (!SupplyRules.IsPackageUnit(dto.Unit))
                    {
                        errors[$"{key}.unit"] = new[] { $"Unit must be one of: {string.Join(", ", SupplyRules.PackageUnits)}." };
                        continue;
                    }
                    line.ProductVariantId = variant.Id;
                    line.PartijId = lot.Id;
                    line.Unit = dto.Unit;
                    line.VATPercentage = dto.VatPercentage ?? variant.VatPercentage ?? ArticleRules.DefaultVat;
                    line.Description = SalesInvoiceRules.Describe(variant);
                    line.OriginCountry = SalesInvoiceRules.Origin(variant);
                    line.QualityClass = variant.TradeClass;
                    line.LotNumber = lot.Number;
                    ValidateLine(line, quantity, dto.UnitPrice, dto.DiscountPercentage, dto.VatPercentage, null, key, errors);
                    if (errors.ContainsKey($"{key}.quantity"))
                    {
                        continue;
                    }
                    if (!stockCache.TryGetValue((variant.Id, dto.Unit), out var stock))
                    {
                        stock = await SalesOrderService.ReservableAsync(db, variant.Id, dto.Unit, cancellationToken);
                        stockCache[(variant.Id, dto.Unit)] = stock;
                    }
                    // Vrij in deze partij, minus wat eerdere regels van deze aanvraag al nemen, plus wat deze factuur zelf vrijgeeft.
                    var candidates = SalesOrderService.FifoOrder(stock.Where(s => s.PartijId == lot.Id)).Select(s =>
                    {
                        var (q, kg) = used.GetValueOrDefault(s.PalletContentLineId);
                        var back = releasing?.GetValueOrDefault(s.PalletContentLineId) ?? 0m;
                        return new ReservableStockLineDto
                        {
                            PalletContentLineId = s.PalletContentLineId, ReceivedAt = s.ReceivedAt, LotNumber = s.LotNumber, WeightMethod = s.WeightMethod,
                            NetKgPerPackage = s.NetKgPerPackage, Ggn = s.Ggn,
                            AvailableQuantity = s.AvailableQuantity - q + back,
                            AvailableNetWeightKg = s.AvailableNetWeightKg - kg + (s.OnHandQuantity > 0m ? Math.Round(back * s.OnHandNetWeightKg / s.OnHandQuantity, 3, MidpointRounding.AwayFromZero) : 0m)
                        };
                    }).Where(s => s.AvailableQuantity > 0m).ToList();
                    var items = SalesOrderService.ProposeFifo(candidates, quantity);
                    var free = candidates.Sum(c => c.AvailableQuantity);
                    if (items.Sum(x => x.Quantity) < quantity)
                    {
                        errors[$"{key}.quantity"] = new[] { $"Only {free.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} {dto.Unit} is free in lot {lot.Number}." };
                        continue;
                    }
                    foreach (var item in items)
                    {
                        var (q, kg) = used.GetValueOrDefault(item.PalletContentLineId);
                        used[item.PalletContentLineId] = (q + item.Quantity, kg + item.NetWeightKg);
                    }
                    var ggns = candidates.ToDictionary(c => c.PalletContentLineId, c => c.Ggn);
                    line.NetWeight = items.Sum(x => x.NetWeightKg);
                    line.Ggn = SalesInvoiceRules.Join(items.Select(x => ggns.GetValueOrDefault(x.PalletContentLineId)).Where(g => g != null).Distinct().OrderBy(g => g, StringComparer.Ordinal), SalesInvoiceRules.MaxGgnLength);
                    planned.Add(new PlannedLine(line, items.Select(x => (x.PalletContentLineId, x.Quantity, x.NetWeightKg, ggns.GetValueOrDefault(x.PalletContentLineId))).ToList()));
                }
                else if (dto.LineKind == SalesInvoiceRules.LineService)
                {
                    var description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
                    CostArticle article = null;
                    if (dto.CostArticleId != null && (!articles.TryGetValue(dto.CostArticleId.Value, out article) || article.Kind != CostArticleRules.KindCost || !article.IsActive))
                    {
                        errors[$"{key}.costArticleId"] = new[] { "Choose an active service article, or type a description." };
                        continue;
                    }
                    description ??= article?.Name;
                    if (description == null)
                    {
                        errors[$"{key}.description"] = new[] { "Choose a service article or type a description." };
                        continue;
                    }
                    if (description.Length > SalesInvoiceRules.MaxDescriptionLength)
                    {
                        errors[$"{key}.description"] = new[] { $"This field may contain at most {SalesInvoiceRules.MaxDescriptionLength} characters." };
                        continue;
                    }
                    var revenueType = dto.RevenueTypeId ?? defaultRevenueType;
                    if (revenueType == null || !revenueTypes.ContainsKey(revenueType.Value))
                    {
                        errors[$"{key}.revenueTypeId"] = new[] { "Choose an active revenue type." };
                        continue;
                    }
                    line.CostArticleId = article?.Id;
                    line.Description = description;
                    line.Unit = SalesInvoiceRules.PieceUnit;
                    line.RevenueTypeId = revenueType;
                    line.VATPercentage = dto.VatPercentage ?? article?.VatPercentage ?? ArticleRules.VatHigh;
                    ValidateLine(line, quantity, dto.UnitPrice, dto.DiscountPercentage, dto.VatPercentage, null, key, errors);
                    planned.Add(new PlannedLine(line, new()));
                }
                else
                {
                    errors[$"{key}.lineKind"] = new[] { "A line of a direct invoice is a product line (from a lot) or a service line." };
                }
            }
            return planned;
        }

        private async Task<Dictionary<int, PalletContentLine>> ContentsAsync(IEnumerable<PlannedLine> planned, CancellationToken cancellationToken)
        {
            var ids = planned.SelectMany(p => p.Allocations.Select(a => a.ContentId)).Distinct().ToList();
            return await db.PalletContentLines.Include(c => c.Pallet).Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        }

        // Nieuwe regels met hun reserveringen toevoegen.
        private void AddPlanned(Invoice invoice, List<PlannedLine> planned, Dictionary<int, PalletContentLine> contents, DateTime now)
        {
            var sort = invoice.InvoiceLines.Select(l => l.SortOrder).DefaultIfEmpty(-1).Max();
            foreach (var plan in planned)
            {
                plan.Line.SortOrder = ++sort;
                invoice.InvoiceLines.Add(plan.Line);
                Reserve(plan.Line, plan.Allocations, contents, now);
            }
        }

        // Reserveringen voor een (nieuwe of bestaande) regel; elke geraakte inhoudsregel en pallet krijgt een nieuwe versie, zodat twee
        // gelijktijdige concepten nooit allebei dezelfde vrije voorraad krijgen (de tweede krijgt een 409).
        private void Reserve(InvoiceLine line, List<(int ContentId, decimal Quantity, decimal Kg, string Ggn)> allocations, Dictionary<int, PalletContentLine> contents, DateTime now)
        {
            foreach (var (contentId, quantity, kg, _) in allocations)
            {
                var content = contents[contentId];
                var reservation = new StockReservation
                {
                    RequestId = Guid.NewGuid(), InvoiceLine = line, PalletContentLineId = content.Id, SupplyCommitmentLineId = content.SupplyCommitmentLineId,
                    Quantity = quantity, Unit = line.Unit, NetWeightKg = kg, Source = ReservationRules.SourcePhysical, Status = ReservationRules.StatusActive,
                    CreatedAt = now, UpdatedAt = now
                };
                db.StockReservations.Add(reservation);
                db.Entry(content).Property(c => c.Version).IsModified = true;
                db.Entry(content.Pallet).Property(p => p.Version).IsModified = true;
                db.PalletAuditEvents.Add(new PalletAuditEvent
                {
                    PalletId = content.PalletId, PalletContentLine = content, EventType = ReservationRules.ReservationCreatedEvent, RequestId = reservation.RequestId,
                    NewValue = JsonSerializer.Serialize(new { directInvoice = true, lotNumber = line.LotNumber, quantity, unit = line.Unit, netWeightKg = kg }, AuditJson),
                    Reason = "Directe verkoopfactuur (concept)", ActorReference = Actor(), OccurredAt = now
                });
            }
        }

        // ---------------- Concept van een directe factuur bijwerken ----------------

        // Wijzigingen die alleen bij een directe factuur horen: nieuwe regels, een ander aantal op een productregel (opnieuw reserveren),
        // weggehaalde productregels (vrijgeven), vrije omschrijving en omzetsoort, en de leverdatum. Null = niets te doen.
        private async Task<Action<DateTime>> PrepareDirectChangesAsync(Invoice invoice, UpdateSalesInvoiceDto request, Dictionary<string, string[]> errors, CancellationToken cancellationToken)
        {
            var changes = request.Lines ?? new List<UpdateSalesInvoiceLineDto>();
            var byId = invoice.InvoiceLines.ToDictionary(l => l.Id);
            var lineIds = invoice.InvoiceLines.Select(l => l.Id).ToList();
            var active = await db.StockReservations.Include(r => r.PalletContentLine).ThenInclude(c => c.Pallet)
                .Where(r => r.InvoiceLineId != null && lineIds.Contains(r.InvoiceLineId.Value) && r.Status == ReservationRules.StatusActive).ToListAsync(cancellationToken);

            // Productregels die vrijgegeven worden: weggehaald of met een ander aantal (die krijgen een nieuwe verdeling).
            var requantified = new List<(InvoiceLine Line, int Index)>();
            var releasing = new List<StockReservation>();
            for (var i = 0; i < changes.Count; i++)
            {
                if (!byId.TryGetValue(changes[i].Id, out var line) || line.LineKind != SalesInvoiceRules.LineProduct) continue;
                if (changes[i].Remove || (changes[i].Quantity is decimal q && q != line.Quantity))
                {
                    releasing.AddRange(active.Where(r => r.InvoiceLineId == line.Id));
                    if (!changes[i].Remove) requantified.Add((line, i));
                }
            }
            var back = releasing.GroupBy(r => r.PalletContentLineId!.Value).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
            var replans = new List<(InvoiceLine Line, PlannedLine Plan)>();
            foreach (var (line, index) in requantified)
            {
                var dto = new SalesInvoiceNewLineDto
                {
                    LineKind = SalesInvoiceRules.LineProduct, ProductVariantId = line.ProductVariantId, PartijId = line.PartijId, Unit = line.Unit,
                    Quantity = changes[index].Quantity, UnitPrice = changes[index].UnitPrice, DiscountPercentage = changes[index].DiscountPercentage, VatPercentage = changes[index].VatPercentage
                };
                var lineErrors = new Dictionary<string, string[]>();
                var plan = (await PlanNewLinesAsync(new List<SalesInvoiceNewLineDto> { dto }, "x", back, lineErrors, cancellationToken)).FirstOrDefault();
                foreach (var (k, v) in lineErrors) errors[k.Replace("x[0]", $"lines[{index}]")] = v;
                if (plan != null) replans.Add((line, plan));
            }
            for (var i = 0; i < changes.Count; i++)
            {
                if (!byId.TryGetValue(changes[i].Id, out var line) || line.LineKind != SalesInvoiceRules.LineService || changes[i].Remove) continue;
                if (changes[i].Description != null && (string.IsNullOrWhiteSpace(changes[i].Description) || changes[i].Description.Trim().Length > SalesInvoiceRules.MaxDescriptionLength))
                {
                    errors[$"lines[{i}].description"] = new[] { $"A description of 1 to {SalesInvoiceRules.MaxDescriptionLength} characters is required." };
                }
                if (changes[i].RevenueTypeId != null && !await db.RevenueTypes.AnyAsync(r => r.Id == changes[i].RevenueTypeId && r.IsActive, cancellationToken))
                {
                    errors[$"lines[{i}].revenueTypeId"] = new[] { "Choose an active revenue type." };
                }
            }
            var newLines = request.NewLines ?? new List<SalesInvoiceNewLineDto>();
            if (invoice.InvoiceLines.Count(SalesInvoiceRules.Counts) + newLines.Count > SalesInvoiceRules.MaxLinesPerInvoice)
            {
                errors["newLines"] = new[] { $"At most {SalesInvoiceRules.MaxLinesPerInvoice} lines per invoice." };
            }
            // Nieuwe regels mogen ook de voorraad gebruiken die bijgestelde regels vrijgeven, maar niet wat die opnieuw nemen.
            var freed = new Dictionary<int, decimal>(back);
            foreach (var (_, plan) in replans)
            {
                foreach (var a in plan.Allocations) freed[a.ContentId] = freed.GetValueOrDefault(a.ContentId) - a.Quantity;
            }
            var planned = newLines.Count == 0 ? new List<PlannedLine>() : await PlanNewLinesAsync(newLines, "newLines", freed, errors, cancellationToken);

            var contents = await ContentsAsync(replans.Select(r => r.Plan).Concat(planned), cancellationToken);
            return now =>
            {
                foreach (var reservation in releasing) Release(reservation, "Regel van het concept gewijzigd of weggehaald", now);
                foreach (var (line, plan) in replans)
                {
                    line.NetWeight = plan.Line.NetWeight;
                    line.Ggn = plan.Line.Ggn;
                    Reserve(line, plan.Allocations, contents, now);
                }
                for (var i = 0; i < changes.Count; i++)
                {
                    if (!byId.TryGetValue(changes[i].Id, out var line) || line.LineKind != SalesInvoiceRules.LineService || changes[i].Remove) continue;
                    if (changes[i].Description != null) line.Description = changes[i].Description.Trim();
                    if (changes[i].RevenueTypeId != null) line.RevenueTypeId = changes[i].RevenueTypeId;
                }
                if (request.DeliveryDate != null) invoice.DeliveryDate = DeliveryDateOf(request.DeliveryDate, now);
                AddPlanned(invoice, planned, contents, now);
            };
        }

        private void Release(StockReservation reservation, string reason, DateTime now)
        {
            reservation.Status = ReservationRules.StatusReleased;
            reservation.ReleasedAt = now;
            reservation.ReleaseReason = reason;
            reservation.UpdatedAt = now;
            db.Entry(reservation.PalletContentLine).Property(c => c.Version).IsModified = true;
            db.PalletAuditEvents.Add(new PalletAuditEvent
            {
                PalletId = reservation.PalletContentLine.PalletId, PalletContentLineId = reservation.PalletContentLineId, EventType = ReservationRules.ReservationReleasedEvent,
                OldValue = JsonSerializer.Serialize(new { directInvoice = true, invoiceLineId = reservation.InvoiceLineId, quantity = reservation.Quantity, unit = reservation.Unit }, AuditJson),
                Reason = reason, ActorReference = Actor(), OccurredAt = now
            });
        }

        // Reserveringen van een concept vrijgeven (weggooien).
        private async Task ReleaseInvoiceStockAsync(Invoice invoice, string reason, CancellationToken cancellationToken)
        {
            var lineIds = invoice.InvoiceLines.Select(l => l.Id).ToList();
            var active = await db.StockReservations.Include(r => r.PalletContentLine)
                .Where(r => r.InvoiceLineId != null && lineIds.Contains(r.InvoiceLineId.Value) && r.Status == ReservationRules.StatusActive).ToListAsync(cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var reservation in active)
            {
                Release(reservation, reason, now);
            }
        }
    }
}
