using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Verkoopfactuur 1, Definitief maken. Per factuur één transactie:
    //  1. de regels krijgen hun momentopname (omschrijving, herkomst, klasse, bedrag en btw op centen) en een directe factuur boekt haar
    //     gereserveerde voorraad af (Dispatch-mutatie en afboekregel, regels 8 en 46);
    //  2. de kop krijgt het volgende nummer van het jaar (VF-jjjj-nnnnn), wie en wanneer, de factuurdatum, betalingstermijn en vervaldatum
    //     en de momentopname van de klant (naam, factuuradres, btw-nummer).
    // In PostgreSQL houdt een slot per jaar de nummering zonder gaten en dubbelen, ook bij gelijktijdig werken; een mislukte factuur
    // (validatie of een ander die intussen wijzigde) gebruikt geen nummer, want haar transactie wordt teruggedraaid.
    public sealed partial class SalesInvoiceService
    {
        private const int NumberLockClass = 44_044;

        // Eén factuur; dezelfde vraag op een al definitieve factuur geeft die factuur terug (herhaling na een netwerkfout).
        public async Task<ServiceResult<SalesInvoiceDto>> FinalizeAsync(int id, FinalizeSalesInvoiceDto request, CancellationToken cancellationToken = default)
        {
            if (request?.ExpectedVersion == null)
            {
                return ServiceResult<SalesInvoiceDto>.Validation(new Dictionary<string, string[]> { ["expectedVersion"] = new[] { "The version you saw is required." } });
            }
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await FinalizeOnceAsync(id, request.ExpectedVersion.Value, cancellationToken);
                }
                catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception) && attempt < 5)
                {
                    // Een ander nam tegelijk hetzelfde nummer (alleen zonder slot, bijvoorbeeld SQLite): opnieuw, met het volgende nummer.
                    db.ChangeTracker.Clear();
                }
            }
        }

        // Een selectie uit de lijst: elke factuur apart, in de gegeven volgorde; een geweigerde factuur houdt de rest niet tegen en kost
        // geen nummer.
        public async Task<ServiceResult<FinalizeSalesInvoicesResultDto>> FinalizeManyAsync(FinalizeSalesInvoicesDto request, CancellationToken cancellationToken = default)
        {
            var items = request?.Invoices ?? new List<FinalizeSalesInvoiceItemDto>();
            if (items.Count == 0 || items.Count > SalesInvoiceRules.MaxFinalizeSelection)
            {
                return ServiceResult<FinalizeSalesInvoicesResultDto>.Validation(new Dictionary<string, string[]>
                {
                    ["invoices"] = new[] { $"Choose 1 to {SalesInvoiceRules.MaxFinalizeSelection} invoices." }
                });
            }
            var results = new List<FinalizeSalesInvoiceResultDto>();
            foreach (var item in items.DistinctBy(i => i.Id))
            {
                db.ChangeTracker.Clear();
                var result = await FinalizeAsync(item.Id, new FinalizeSalesInvoiceDto { ExpectedVersion = item.ExpectedVersion }, cancellationToken);
                results.Add(new FinalizeSalesInvoiceResultDto(item.Id, result.Status == ServiceResultStatus.Ok, result.Value?.InvoiceNumber, result.Value?.Version,
                    result.Status == ServiceResultStatus.Ok ? null : result.Message ?? "Not final: check the lines.", result.Errors));
            }
            return ServiceResult<FinalizeSalesInvoicesResultDto>.Ok(new FinalizeSalesInvoicesResultDto(results));
        }

        private async Task<ServiceResult<SalesInvoiceDto>> FinalizeOnceAsync(int id, int expectedVersion, CancellationToken cancellationToken)
        {
            var stored = await db.Invoices.AsNoTracking().Where(i => i.Id == id && i.Kind != null).Select(i => new { i.Status, i.Version }).SingleOrDefaultAsync(cancellationToken);
            if (stored?.Status == SalesInvoiceRules.StatusFinal && stored.Version >= expectedVersion)
            {
                return ServiceResult<SalesInvoiceDto>.Ok(await DtoAsync(id, cancellationToken));
            }
            // Eerst de transactie en, in PostgreSQL, de factuurrij FOR UPDATE: een gelijktijdige wijziging of regel (de regeltrigger vraagt
            // FOR SHARE) wacht tot deze factuur klaar is, en wij lezen de regels pas daarna.
            var transaction = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            try
            {
                if (db.Database.IsNpgsql())
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Invoices\" WHERE \"Id\" = {id} FOR UPDATE", cancellationToken);
                }
                var (invoice, refused) = await ConceptForChangeAsync(id, expectedVersion, cancellationToken);
                if (refused != null)
                {
                    return refused;
                }
                var errors = SalesInvoiceRules.FinalizeErrors(invoice);
                var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken);
                if (customer == null && !errors.ContainsKey("customerId"))
                {
                    errors["customerId"] = new[] { "The customer of this invoice does not exist anymore." };
                }
                var lines = invoice.InvoiceLines.Where(SalesInvoiceRules.Counts).ToList();
                var lineIds = lines.Select(l => l.Id).ToList();
                var active = await db.StockReservations.Include(r => r.PalletContentLine).ThenInclude(c => c.Pallet)
                    .Where(r => r.InvoiceLineId != null && lineIds.Contains(r.InvoiceLineId.Value) && r.Status == ReservationRules.StatusActive)
                    .ToListAsync(cancellationToken);
                foreach (var line in lines.Where(l => invoice.Kind == SalesInvoiceRules.KindDirect && l.LineKind == SalesInvoiceRules.LineProduct))
                {
                    if (active.Where(r => r.InvoiceLineId == line.Id).Sum(r => r.Quantity) != line.Quantity)
                    {
                        errors[$"lines[{lines.IndexOf(line)}].quantity"] = new[] { "The stock of this line is no longer held in full; change the quantity again to reserve it." };
                    }
                }
                if (errors.Count > 0)
                {
                    return ServiceResult<SalesInvoiceDto>.Validation(errors);
                }

                var now = DateTime.UtcNow;
                var year = calendar.DateOf(now).Year;
                if (db.Database.IsNpgsql())
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({NumberLockClass}, {year})", cancellationToken);
                }

                // 1. Momentopname van de regels en afboeken (de factuur is nog een concept).
                var variantIds = lines.Where(l => l.ProductVariantId != null).Select(l => l.ProductVariantId.Value).Distinct().ToList();
                var variants = await db.ProductVariants.AsNoTracking().Include(v => v.Product).Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken);
                foreach (var line in lines)
                {
                    if (line.ProductVariantId != null && variants.TryGetValue(line.ProductVariantId.Value, out var variant))
                    {
                        line.Description = SalesInvoiceRules.Describe(variant) ?? line.Description;
                        line.OriginCountry = SalesInvoiceRules.Origin(variant);
                        line.QualityClass = variant.TradeClass;
                    }
                    SalesInvoiceRules.Recalculate(line);
                }
                SalesInvoiceRules.RecalculateTotals(invoice);
                foreach (var reservation in active)
                {
                    BookOut(invoice, reservation, now);
                }
                await db.SaveChangesAsync(cancellationToken);

                // 2. Nummer en momentopname van de klant.
                var last = await db.Invoices.AsNoTracking().Where(i => i.InvoiceYear == year).MaxAsync(i => (int?)i.InvoiceSequence, cancellationToken) ?? 0;
                var termDays = customer.PaymentTermsDays > 0 ? customer.PaymentTermsDays : SalesInvoiceRules.DefaultPaymentTermDays;
                invoice.Status = SalesInvoiceRules.StatusFinal;
                invoice.InvoiceYear = year;
                invoice.InvoiceSequence = last + 1;
                invoice.InvoiceNumber = SalesInvoiceRules.FormatNumber(year, last + 1);
                invoice.FinalizedAt = now;
                invoice.FinalizedBy = Actor();
                invoice.InvoiceDate = now;
                invoice.PaymentTermDays = termDays;
                invoice.DueDate = SalesInvoiceRules.DueDate(now, termDays);
                invoice.CustomCustomerName = customer.Name;
                invoice.CustomerAddress = string.IsNullOrWhiteSpace(customer.BillingAddress) ? null : customer.BillingAddress.Trim();
                invoice.CustomerVatNumber = string.IsNullOrWhiteSpace(customer.VATNumber) ? null : customer.VATNumber.Trim();
                invoice.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
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
                return Reload(id);
            }
            finally
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }
            }
            return ServiceResult<SalesInvoiceDto>.Ok(await DtoAsync(id, cancellationToken));
        }

        // Eén reservering van een directe factuur afboeken, zoals een verzending dat doet: verbruikt, afgeboekt op de pallet, een
        // Dispatch-mutatie (het grootboek) en een afboekregel van de factuur, met historie op de pallet.
        private void BookOut(Invoice invoice, StockReservation reservation, DateTime now)
        {
            var content = reservation.PalletContentLine;
            var line = invoice.InvoiceLines.Single(l => l.Id == reservation.InvoiceLineId);
            reservation.Status = ReservationRules.StatusConsumed;
            reservation.ConsumedAt = now;
            reservation.UpdatedAt = now;
            content.DispatchedQuantity += reservation.Quantity;
            content.DispatchedNetWeightKg += reservation.NetWeightKg ?? 0m;
            db.Entry(content.Pallet).Property(p => p.Version).IsModified = true;
            var movement = new StockMovement
            {
                Type = ReservationRules.MovementTypeDispatch, Quantity = reservation.Quantity, Unit = reservation.Unit, NetWeightKg = reservation.NetWeightKg,
                SupplyCommitmentLineId = content.SupplyCommitmentLineId, PalletContentLine = content, StockReservation = reservation, CreatedAt = now,
                Reason = $"Directe verkoopfactuur {invoice.BatchNumber}"
            };
            db.StockMovements.Add(movement);
            db.SalesInvoiceStockLines.Add(new SalesInvoiceStockLine
            {
                InvoiceId = invoice.Id, InvoiceLine = line, StockReservation = reservation, PalletContentLine = content, Quantity = reservation.Quantity,
                Unit = reservation.Unit, NetWeightKg = reservation.NetWeightKg ?? 0m, StockMovement = movement, CreatedAt = now
            });
            db.PalletAuditEvents.Add(new PalletAuditEvent
            {
                PalletId = content.PalletId, PalletContentLine = content, EventType = ReservationRules.ReservationConsumedEvent,
                NewValue = JsonSerializer.Serialize(new { directInvoice = true, salesLot = invoice.BatchNumber, quantity = reservation.Quantity, unit = reservation.Unit, netWeightKg = reservation.NetWeightKg }, AuditJson),
                Reason = "Directe verkoopfactuur definitief", ActorReference = Actor(), OccurredAt = now
            });
        }
    }
}
