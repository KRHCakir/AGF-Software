using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Verkoopfactuur 1: een concept maken (uit leveringen), bijwerken en weggooien. Opslaan gebeurt in PostgreSQL met een slot per
    // verkooporder, zodat regel 45 (nooit meer dan geleverd) ook bij gelijktijdig werken klopt.
    public sealed partial class SalesInvoiceService
    {
        // ---------------- Concept uit leveringen ----------------

        public async Task<ServiceResult<SalesInvoiceDto>> CreateFromDeliveriesAsync(CreateInvoiceFromDeliveriesDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, string[]>();
            if (request?.RequestId == null || request.RequestId == Guid.Empty)
            {
                errors["requestId"] = new[] { "A client-generated, non-empty requestId is required." };
            }
            var deliveryIds = (request?.DeliveryIds ?? new List<int>()).Distinct().OrderBy(id => id).ToList();
            if (deliveryIds.Count == 0)
            {
                errors["deliveryIds"] = new[] { "Choose at least one delivery." };
            }
            else if (deliveryIds.Count > SalesInvoiceRules.MaxDeliveriesPerInvoice)
            {
                errors["deliveryIds"] = new[] { $"At most {SalesInvoiceRules.MaxDeliveriesPerInvoice} deliveries per invoice." };
            }
            if (errors.Count > 0)
            {
                return ServiceResult<SalesInvoiceDto>.Validation(errors);
            }

            var fingerprint = SalesInvoiceRules.Fingerprint(new { kind = SalesInvoiceRules.KindDelivery, deliveryIds });
            for (var attempt = 1; ; attempt++)
            {
                // Een herhaling is precies dezelfde aanvraag (dezelfde leveringen), vergeleken met de vingerafdruk die bij het aanmaken is
                // bewaard; los van wat er daarna op het concept veranderde.
                var replay = await db.Invoices.AsNoTracking().Where(i => i.RequestId == request.RequestId).Select(i => new { i.Id, i.RequestFingerprint }).SingleOrDefaultAsync(cancellationToken);
                if (replay != null)
                {
                    return replay.RequestFingerprint == fingerprint
                        ? ServiceResult<SalesInvoiceDto>.Ok(await DtoAsync(replay.Id, cancellationToken))
                        : ServiceResult<SalesInvoiceDto>.Conflict($"RequestId {request.RequestId} was already used for another invoice.");
                }
                var invoice = new Invoice
                {
                    Kind = SalesInvoiceRules.KindDelivery, Status = SalesInvoiceRules.StatusConcept, RequestId = request.RequestId, RequestFingerprint = fingerprint,
                    CreatedBy = Actor(), CreatedAt = DateTime.UtcNow, InvoiceDate = DateTime.UtcNow, PaymentStatus = "Pending"
                };
                var refused = await AddDeliveriesAsync(invoice, deliveryIds, "deliveryIds", cancellationToken);
                if (refused != null)
                {
                    // Een gelijktijdige identieke aanvraag kan de leveringen net gefactureerd hebben: dan is dit een herhaling, geen conflict.
                    if (refused.Status == ServiceResultStatus.Conflict && attempt < 3 && await RequestIdExistsAsync(request.RequestId.Value, cancellationToken))
                    {
                        db.ChangeTracker.Clear();
                        continue;
                    }
                    return refused;
                }
                db.Invoices.Add(invoice);
                try
                {
                    var saved = await TrySaveAsync(invoice, cancellationToken);
                    if (saved == null)
                    {
                        return ServiceResult<SalesInvoiceDto>.Created(await DtoAsync(invoice.Id, cancellationToken));
                    }
                    if (saved.Status == ServiceResultStatus.Conflict && attempt < 3 && await RequestIdExistsAsync(request.RequestId.Value, cancellationToken))
                    {
                        continue; // de andere identieke aanvraag was eerst: de herhaling bovenaan geeft die factuur terug
                    }
                    return saved;
                }
                catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception) && attempt < 3)
                {
                    // Dezelfde aanvraag tegelijk: de tweede vindt de eerste bij de herhaling.
                    db.ChangeTracker.Clear();
                }
            }
        }

        private Task<bool> RequestIdExistsAsync(Guid requestId, CancellationToken cancellationToken) =>
            db.Invoices.AsNoTracking().AnyAsync(i => i.RequestId == requestId, cancellationToken);

        // Leveringen bij een bestaand concept: de rest per orderregel komt erbij (regels die er al staan blijven ongemoeid; dienst- en emballageregels met een rest komen ook mee zonder nieuwe productregel, behalve als ze bewust van dit concept zijn gehaald).
        public async Task<ServiceResult<SalesInvoiceDto>> AddDeliveriesAsync(int id, AddInvoiceDeliveriesDto request, CancellationToken cancellationToken = default)
        {
            var deliveryIds = (request?.DeliveryIds ?? new List<int>()).Distinct().OrderBy(d => d).ToList();
            if (deliveryIds.Count == 0)
            {
                return ServiceResult<SalesInvoiceDto>.Validation(new Dictionary<string, string[]> { ["deliveryIds"] = new[] { "Choose at least one delivery." } });
            }
            var (invoice, refused) = await ConceptForChangeAsync(id, request?.ExpectedVersion, cancellationToken);
            if (refused != null)
            {
                return refused;
            }
            if (invoice.Kind != SalesInvoiceRules.KindDelivery)
            {
                return ServiceResult<SalesInvoiceDto>.Conflict("A direct invoice has no deliveries.");
            }
            var added = await AddDeliveriesAsync(invoice, deliveryIds, "deliveryIds", cancellationToken);
            if (added != null)
            {
                return added;
            }
            return await TrySaveAsync(invoice, cancellationToken) ?? ServiceResult<SalesInvoiceDto>.Ok(await DtoAsync(invoice.Id, cancellationToken));
        }

        // Zet de rest van deze leveringen op de factuur (nieuw of concept). Null = gelukt; anders de weigering.
        private async Task<ServiceResult<SalesInvoiceDto>> AddDeliveriesAsync(Invoice invoice, List<int> deliveryIds, string field, CancellationToken cancellationToken)
        {
            var deliveries = await db.SalesDeliveries.AsNoTracking().Where(d => deliveryIds.Contains(d.Id))
                .Select(d => new { d.Id, d.SalesOrderId, d.Sequence, d.SalesOrder.CustomerId, CustomerName = d.SalesOrder.Customer.Name, d.SalesOrder.OrderNumber, d.SalesOrder.CustomerReference })
                .ToListAsync(cancellationToken);
            var unknown = deliveryIds.Except(deliveries.Select(d => d.Id)).ToList();
            if (unknown.Count > 0)
            {
                return ServiceResult<SalesInvoiceDto>.NotFound($"Delivery {unknown[0]} does not exist.");
            }
            var customers = deliveries.Select(d => d.CustomerId).Append(invoice.CustomerId ?? deliveries[0].CustomerId).Distinct().ToList();
            if (customers.Count > 1)
            {
                return ServiceResult<SalesInvoiceDto>.Validation(new Dictionary<string, string[]>
                {
                    [field] = new[] { "Deliveries of another customer cannot go on this invoice: one invoice is for one customer." }
                });
            }
            invoice.CustomerId = customers[0];
            invoice.CustomCustomerName ??= deliveries[0].CustomerName;

            var orderIds = deliveries.Select(d => d.SalesOrderId).Distinct().ToList();
            var states = (await DeliveryStatesAsync(orderIds, invoice.Id == 0 ? null : invoice.Id, cancellationToken))
                .Where(s => deliveryIds.Contains(s.DeliveryId) && s.Rest > 0m).ToList();
            var extras = (await ExtraStatesAsync(orderIds, invoice.Id == 0 ? null : invoice.Id, cancellationToken)).Where(s => s.Rest > 0m).ToList();
            var current = invoice.InvoiceLines.Where(SalesInvoiceRules.Counts).ToList();
            var orderLines = await db.SalesOrderLines.AsNoTracking().Include(l => l.ProductVariant).ThenInclude(v => v.Product)
                .Where(l => orderIds.Contains(l.SalesOrderId)).ToDictionaryAsync(l => l.Id, cancellationToken);
            var serviceLines = await db.SalesOrderServiceLines.AsNoTracking().Include(s => s.CostArticle).Where(s => orderIds.Contains(s.SalesOrderId)).ToDictionaryAsync(s => s.Id, cancellationToken);
            var packagingLines = await db.SalesOrderPackagingLines.AsNoTracking().Include(p => p.CostArticle).Where(p => orderIds.Contains(p.SalesOrderId)).ToDictionaryAsync(p => p.Id, cancellationToken);
            var defaultRevenueType = await DefaultRevenueTypeIdAsync(cancellationToken);
            var order = deliveries.OrderBy(d => d.OrderNumber, StringComparer.Ordinal).ThenBy(d => d.Sequence).Select(d => d.Id).ToList();
            var sort = invoice.InvoiceLines.Select(l => l.SortOrder).DefaultIfEmpty(-1).Max();
            var changed = false;
            var newOrderIds = new HashSet<int>(); // orders met een nieuwe productregel

            foreach (var state in states.OrderBy(s => order.IndexOf(s.DeliveryId)).ThenBy(s => s.OrderLineId))
            {
                var existing = current.FirstOrDefault(l => l.SalesDeliveryId == state.DeliveryId && l.SalesOrderLineId == state.OrderLineId);
                if (existing != null)
                {
                    // Een regel die al op het concept staat, is misschien bewust aangepast: laat hem ongemoeid.
                    continue;
                }
                newOrderIds.Add(state.OrderId);
                var orderLine = orderLines[state.OrderLineId];
                var variant = orderLine.ProductVariant;
                invoice.InvoiceLines.Add(new InvoiceLine
                {
                    LineKind = SalesInvoiceRules.LineProduct, SortOrder = ++sort, SalesDeliveryId = state.DeliveryId, SalesOrderLineId = state.OrderLineId,
                    ProductVariantId = orderLine.ProductVariantId, Description = SalesInvoiceRules.Describe(variant), Quantity = state.Rest, Unit = orderLine.Unit,
                    UnitPrice = orderLine.UnitPrice, DiscountPercentage = orderLine.DiscountPercentage ?? 0m,
                    VATPercentage = orderLine.VatPercentage ?? variant?.VatPercentage ?? ArticleRules.DefaultVat,
                    NetWeight = ShareKg(state, state.Rest), OriginCountry = SalesInvoiceRules.Origin(variant), QualityClass = variant?.TradeClass,
                    LotNumber = SalesInvoiceRules.Join(state.Lots, SalesInvoiceRules.MaxLotNumberLength), Ggn = SalesInvoiceRules.Join(state.Ggns, SalesInvoiceRules.MaxGgnLength)
                });
                changed = true;
            }
            foreach (var extra in extras)
            {
                var existing = current.FirstOrDefault(l => extra.Packaging ? l.SalesOrderPackagingLineId == extra.LineId : l.SalesOrderServiceLineId == extra.LineId);
                if (existing != null)
                {
                    // Een dienst- of emballageregel die al op het concept staat, is misschien bewust aangepast: laat hem ongemoeid.
                    continue;
                }
                if (invoice.InvoiceLines.Any(l => l.RemovedAt != null && (extra.Packaging ? l.SalesOrderPackagingLineId == extra.LineId : l.SalesOrderServiceLineId == extra.LineId)))
                {
                    // Bewust van dit concept afgehaald: komt er niet opnieuw op, ook niet met een nieuwe levering van dezelfde order. De rest
                    // blijft open voor een volgende factuur.
                    continue;
                }
                if (extra.Packaging)
                {
                    var packaging = packagingLines[extra.LineId];
                    invoice.InvoiceLines.Add(new InvoiceLine
                    {
                        LineKind = SalesInvoiceRules.LinePackaging, SortOrder = ++sort, SalesOrderPackagingLineId = packaging.Id, CostArticleId = packaging.CostArticleId,
                        Description = packaging.CostArticle?.Name, Quantity = extra.Rest, Unit = SalesInvoiceRules.PieceUnit, UnitPrice = packaging.UnitPrice,
                        VATPercentage = packaging.VatPercentage
                    });
                }
                else
                {
                    var service = serviceLines[extra.LineId];
                    invoice.InvoiceLines.Add(new InvoiceLine
                    {
                        LineKind = SalesInvoiceRules.LineService, SortOrder = ++sort, SalesOrderServiceLineId = service.Id, CostArticleId = service.CostArticleId,
                        Description = service.CostArticle?.Name, Quantity = extra.Rest, Unit = SalesInvoiceRules.PieceUnit, UnitPrice = service.UnitPrice,
                        DiscountPercentage = service.DiscountPercentage ?? 0m, VATPercentage = service.VatPercentage, RevenueTypeId = defaultRevenueType
                    });
                }
                changed = true;
            }
            if (!changed)
            {
                const string message = "There is nothing left to invoice on these deliveries.";
                return ServiceResult<SalesInvoiceDto>.Conflict(message, new { code = NothingToInvoiceCode, message });
            }

            var chosen = deliveries.Where(d => states.Any(s => s.DeliveryId == d.Id)).ToList();
            var delivered = states.Select(s => s.DeliveredAt).Where(d => d != null).Select(d => d.Value).ToList();
            if (delivered.Count > 0 && (invoice.DeliveryDate == null || delivered.Max() > invoice.DeliveryDate))
            {
                invoice.DeliveryDate = delivered.Max();
            }
            var references = chosen.Select(d => d.CustomerReference).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
            if (invoice.ReferenceNumber == null && references.Count > 0)
            {
                invoice.ReferenceNumber = SalesInvoiceRules.Join(references, SalesInvoiceRules.MaxReferenceLength);
            }
            SalesInvoiceRules.RecalculateTotals(invoice);
            invoice.UpdatedAt = DateTime.UtcNow;
            return null;
        }

        private static decimal ShareKg(DeliveryLineState state, decimal quantity) =>
            state.DeliveredKg is decimal kg && state.Delivered > 0m ? Math.Round(kg * quantity / state.Delivered, 3, MidpointRounding.AwayFromZero) : 0m;

        // ---------------- Concept bijwerken ----------------

        public async Task<ServiceResult<SalesInvoiceDto>> UpdateAsync(int id, UpdateSalesInvoiceDto request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                return ServiceResult<SalesInvoiceDto>.Validation(new Dictionary<string, string[]> { ["request"] = new[] { "A request body is required." } });
            }
            var (invoice, refused) = await ConceptForChangeAsync(id, request.ExpectedVersion, cancellationToken);
            if (refused != null)
            {
                return refused;
            }
            var errors = new Dictionary<string, string[]>();
            var reference = string.IsNullOrWhiteSpace(request.ReferenceNumber) ? null : request.ReferenceNumber.Trim();
            if (reference != null && reference.Length > SalesInvoiceRules.MaxReferenceLength)
            {
                errors["referenceNumber"] = new[] { $"This field may contain at most {SalesInvoiceRules.MaxReferenceLength} characters." };
            }
            var maxima = await MaximaAsync(invoice, cancellationToken);
            var byId = invoice.InvoiceLines.ToDictionary(l => l.Id);
            var changes = request.Lines ?? new List<UpdateSalesInvoiceLineDto>();
            for (var i = 0; i < changes.Count; i++)
            {
                var change = changes[i];
                var key = $"lines[{i}]";
                if (!byId.TryGetValue(change.Id, out var line) || line.RemovedAt != null)
                {
                    errors[$"{key}.id"] = new[] { $"Line {change.Id} is not on this concept." };
                    continue;
                }
                if (change.Remove)
                {
                    continue;
                }
                ValidateLine(line, change.Quantity ?? line.Quantity, change.UnitPrice, change.DiscountPercentage, change.VatPercentage, maxima.GetValueOrDefault(line.Id), key, errors);
            }
            if (invoice.Kind == SalesInvoiceRules.KindDelivery && (request.NewLines?.Count ?? 0) > 0)
            {
                errors["newLines"] = new[] { "A line of an invoice from deliveries always comes from a delivery or from the order." };
            }
            if (invoice.Kind == SalesInvoiceRules.KindDelivery && request.DeliveryDate != null)
            {
                errors["deliveryDate"] = new[] { "The delivery date of an invoice from deliveries comes from the dispatch." };
            }
            var direct = invoice.Kind == SalesInvoiceRules.KindDirect ? await PrepareDirectChangesAsync(invoice, request, errors, cancellationToken) : null;
            if (errors.Count > 0)
            {
                return ServiceResult<SalesInvoiceDto>.Validation(errors);
            }

            var now = DateTime.UtcNow;
            foreach (var change in changes)
            {
                var line = byId[change.Id];
                if (change.Remove)
                {
                    line.RemovedAt = now;
                    continue;
                }
                line.Quantity = change.Quantity ?? line.Quantity;
                line.UnitPrice = change.UnitPrice;
                line.DiscountPercentage = change.DiscountPercentage ?? 0m;
                line.VATPercentage = change.VatPercentage ?? line.VATPercentage;
                if (line.SalesDeliveryId != null && maxima.TryGetValue(line.Id, out var max) && max.Kg != null)
                {
                    line.NetWeight = max.Max > 0m ? Math.Round(max.Kg.Value * line.Quantity / max.Max, 3, MidpointRounding.AwayFromZero) : 0m;
                }
            }
            invoice.ReferenceNumber = reference;
            direct?.Invoke(now);
            SalesInvoiceRules.RecalculateTotals(invoice);
            invoice.UpdatedAt = now;
            return await TrySaveAsync(invoice, cancellationToken) ?? ServiceResult<SalesInvoiceDto>.Ok(await DtoAsync(invoice.Id, cancellationToken));
        }

        private static void ValidateLine(InvoiceLine line, decimal quantity, decimal? unitPrice, decimal? discount, decimal? vat, LineMax max, string key, Dictionary<string, string[]> errors)
        {
            if (quantity <= 0m || decimal.Round(quantity, 3) != quantity)
            {
                errors[$"{key}.quantity"] = new[] { "Quantity must be more than 0 with at most 3 decimals." };
            }
            else if (line.LineKind == SalesInvoiceRules.LinePackaging && decimal.Truncate(quantity) != quantity)
            {
                errors[$"{key}.quantity"] = new[] { "Packaging is invoiced in whole pieces." };
            }
            else if (max != null && quantity > max.Max)
            {
                errors[$"{key}.quantity"] = new[] { max.Max == 0m
                    ? "Everything delivered on this line is already on another invoice."
                    : $"At most {max.Max.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} can be invoiced: never more than delivered." };
            }
            if (unitPrice != null && !CostArticleRules.IsUnitPrice(unitPrice.Value))
            {
                errors[$"{key}.unitPrice"] = new[] { "Unit price, when given, must be 0 or more with at most 2 decimals." };
            }
            if (discount != null && !OrderLineMath.IsDiscountPercentage(discount.Value))
            {
                errors[$"{key}.discountPercentage"] = new[] { "Discount, when given, must be from 0 to 100 percent with at most 2 decimals." };
            }
            if (vat != null && !ArticleRules.IsVatRate(vat.Value))
            {
                errors[$"{key}.vatPercentage"] = new[] { $"VAT must be one of: {string.Join(", ", ArticleRules.VatRates)} percent." };
            }
        }

        // ---------------- Weggooien ----------------

        // Een concept vervalt (blijft bestaan, telt nergens meer mee); de leveringen zijn daarna weer te factureren. Een definitieve factuur
        // kan niet vervallen: die herstel je later met een creditfactuur.
        public async Task<ServiceResult<SalesInvoiceDto>> DiscardAsync(int id, DiscardSalesInvoiceDto request, CancellationToken cancellationToken = default)
        {
            var reason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request.Reason.Trim();
            if (reason != null && reason.Length > SalesInvoiceRules.MaxReasonLength)
            {
                return ServiceResult<SalesInvoiceDto>.Validation(new Dictionary<string, string[]> { ["reason"] = new[] { $"This field may contain at most {SalesInvoiceRules.MaxReasonLength} characters." } });
            }
            var stored = await db.Invoices.AsNoTracking().Where(i => i.Id == id && i.Kind != null).Select(i => i.Status).SingleOrDefaultAsync(cancellationToken);
            if (stored == SalesInvoiceRules.StatusDiscarded)
            {
                return ServiceResult<SalesInvoiceDto>.Ok(await DtoAsync(id, cancellationToken));
            }
            var (invoice, refused) = await ConceptForChangeAsync(id, request?.ExpectedVersion, cancellationToken);
            if (refused != null)
            {
                return refused;
            }
            invoice.Status = SalesInvoiceRules.StatusDiscarded;
            invoice.DiscardedAt = DateTime.UtcNow;
            invoice.DiscardedBy = Actor();
            invoice.DiscardReason = reason;
            invoice.UpdatedAt = invoice.DiscardedAt;
            await ReleaseInvoiceStockAsync(invoice, "Concept weggegooid", cancellationToken);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Reload(id);
            }
            return ServiceResult<SalesInvoiceDto>.Ok(await DtoAsync(id, cancellationToken));
        }

        // ---------------- Hulpjes ----------------

        // Een concept laden om te wijzigen; geweigerd als het niet (meer) bestaat, geen concept is of intussen veranderd is.
        private async Task<(Invoice Invoice, ServiceResult<SalesInvoiceDto> Refused)> ConceptForChangeAsync(int id, int? expectedVersion, CancellationToken cancellationToken)
        {
            if (expectedVersion == null)
            {
                return (null, ServiceResult<SalesInvoiceDto>.Validation(new Dictionary<string, string[]> { ["expectedVersion"] = new[] { "The version you saw is required." } }));
            }
            var invoice = await db.Invoices.Include(i => i.InvoiceLines).SingleOrDefaultAsync(i => i.Id == id && i.Kind != null, cancellationToken);
            if (invoice == null)
            {
                return (null, ServiceResult<SalesInvoiceDto>.NotFound($"Sales invoice {id} does not exist."));
            }
            if (invoice.Status != SalesInvoiceRules.StatusConcept)
            {
                var message = invoice.Status == SalesInvoiceRules.StatusFinal
                    ? $"Sales invoice {invoice.InvoiceNumber} is final and never changes; correct it later with a credit invoice."
                    : "This concept was discarded.";
                return (null, ServiceResult<SalesInvoiceDto>.Conflict(message, new { code = "INVOICE_NOT_CONCEPT", message, status = invoice.Status }));
            }
            if (invoice.Version != expectedVersion)
            {
                return (null, Reload(id));
            }
            return (invoice, null);
        }

        private static ServiceResult<SalesInvoiceDto> Reload(int id)
        {
            var message = $"Sales invoice {id} was changed in the meantime. Reload it and decide again.";
            return ServiceResult<SalesInvoiceDto>.Conflict(message, new { code = ReloadCode, message });
        }

        // Opslaan met, in PostgreSQL, een slot per verkooporder van de leveringen op deze factuur: twee concepten voor dezelfde levering
        // worden na elkaar gecontroleerd (regel 45 telt dan wat de ander al opsloeg). Null = opgeslagen; anders een 409 met uitleg.
        private async Task<ServiceResult<SalesInvoiceDto>> TrySaveAsync(Invoice invoice, CancellationToken cancellationToken)
        {
            try
            {
                await SaveLockedAsync(invoice, cancellationToken);
                return null;
            }
            catch (InvoiceQuantityConflictException)
            {
                db.ChangeTracker.Clear();
                const string message = "Someone else invoiced (part of) this delivery in the meantime: never more than delivered. Reload and decide again.";
                return ServiceResult<SalesInvoiceDto>.Conflict(message, new { code = QuantityCode, message });
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                return Reload(invoice.Id);
            }
        }

        private async Task SaveLockedAsync(Invoice invoice, CancellationToken cancellationToken)
        {
            if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null)
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            var lines = invoice.InvoiceLines.ToList();
            var orderIds = await OrderIdsAsync(
                lines.Where(l => l.SalesDeliveryId != null).Select(l => l.SalesDeliveryId.Value).Distinct().ToList(),
                lines.Where(l => l.SalesOrderServiceLineId != null).Select(l => l.SalesOrderServiceLineId.Value).ToList(),
                lines.Where(l => l.SalesOrderPackagingLineId != null).Select(l => l.SalesOrderPackagingLineId.Value).ToList(), cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            foreach (var orderId in orderIds)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({OrderLockClass}, {orderId})", cancellationToken);
            }
            // De factuurrij eerst vastzetten (zoals Definitief maken doet): de regeltrigger wacht daar ook op.
            if (invoice.Id > 0)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Invoices\" WHERE \"Id\" = {invoice.Id} FOR UPDATE", cancellationToken);
            }
            await LockInvoiceStockAsync(invoice, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        private async Task<int?> DefaultRevenueTypeIdAsync(CancellationToken cancellationToken) =>
            await db.RevenueTypes.AsNoTracking().Where(r => r.IsDefault && r.IsActive).Select(r => (int?)r.Id).FirstOrDefaultAsync(cancellationToken);
    }
}
