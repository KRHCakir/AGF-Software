using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Kosten verdelen over partijen (P2): per regel van een inkoopfactuur (direct of via een order) bedragen excl. btw op inkoop- en/of
    // verkoop-partijen. De som mag het regelbedrag niet overschrijden (harde fout); een rest telt als algemene kosten (alleen een
    // waarschuwing in het scherm). Herverdelen vervangt de verdeling van de regel en schrijft per verschil een auditregel met oud en nieuw.
    public sealed class PartijCostService
    {
        private readonly AppDbContext _db;
        private readonly ICurrentUser _user;

        // Zonder ingelogde gebruiker (tests, seed) de standaardgebruiker; in de API altijd de gebruiker van het verzoek.
        public PartijCostService(AppDbContext db, ICurrentUser currentUser = null)
        {
            _db = db;
            _user = currentUser ?? CurrentUser.Default;
        }

        public async Task<ServiceResult<InvoiceAllocationsDto>> GetInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
        {
            var invoice = await _db.IncomingInvoices.AsNoTracking().Include(i => i.InvoiceLines).ThenInclude(l => l.CostArticle)
                .SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
            if (invoice == null)
            {
                return ServiceResult<InvoiceAllocationsDto>.NotFound($"Purchase invoice {invoiceId} does not exist.");
            }
            var lineIds = invoice.InvoiceLines.Select(l => l.Id).ToList();
            var costs = await _db.PartijCosts.AsNoTracking().Where(c => lineIds.Contains(c.IncomingInvoiceLineId))
                .Select(c => new { c.IncomingInvoiceLineId, c.PartijId, c.Partij.Number, c.Partij.Kind, c.AmountExclVat }).ToListAsync(cancellationToken);
            var events = await _db.PartijCostAuditEvents.AsNoTracking().Where(e => e.IncomingInvoiceId == invoiceId).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync(cancellationToken);
            var partijIds = events.Select(e => e.PartijId).Distinct().ToList();
            var numbers = await _db.Partijen.AsNoTracking().Where(p => partijIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, cancellationToken);
            return ServiceResult<InvoiceAllocationsDto>.Ok(new InvoiceAllocationsDto
            {
                InvoiceId = invoice.Id,
                Currency = invoice.Currency ?? PartijCostRules.Currency,
                CanAllocate = IsEuro(invoice),
                Lines = invoice.InvoiceLines.OrderBy(l => l.Id).Select(line =>
                {
                    var allocations = costs.Where(c => c.IncomingInvoiceLineId == line.Id)
                        .Select(c => new LineAllocationDto { PartijId = c.PartijId, Number = c.Number, PartijKind = c.Kind, Amount = c.AmountExclVat })
                        .OrderBy(a => a.Number).ToList();
                    var allocated = allocations.Sum(a => a.Amount);
                    return new InvoiceLineAllocationDto
                    {
                        LineId = line.Id, Description = line.Description, Kind = PartijCostRules.KindOf(line.ProductVariantId, line.CostArticle?.Name),
                        LineTotal = line.LineTotal, Allocated = allocated, General = line.LineTotal - allocated, Version = line.AllocationVersion,
                        Allocations = allocations,
                        History = events.Where(e => e.IncomingInvoiceLineId == line.Id).Select(e => new AllocationHistoryDto
                        {
                            OccurredAt = DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc), Type = e.EventType, PartijId = e.PartijId,
                            Number = numbers.GetValueOrDefault(e.PartijId), OldAmount = e.OldAmount, NewAmount = e.NewAmount, ActorReference = e.ActorReference
                        }).ToList()
                    };
                }).ToList()
            });
        }

        // De hele verdeling van één regel opslaan (vervangt de vorige): toevoegen, wijzigen en weghalen met per verschil een auditregel.
        // Idempotent via requestId; ExpectedVersion bewaakt gelijktijdig herverdelen.
        public async Task<ServiceResult<InvoiceAllocationsDto>> SaveLineAsync(int invoiceId, int lineId, SaveLineAllocationDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request?.RequestId == null || request.RequestId == Guid.Empty)
            {
                ValidationErrors.Add(errors, "requestId", "A client-generated, non-empty requestId is required.");
            }
            if (request?.ExpectedVersion == null)
            {
                ValidationErrors.Add(errors, "expectedVersion", "The version of the allocation you saw is required.");
            }
            if (request != null && request.Allocations == null)
            {
                ValidationErrors.Add(errors, "allocations", "The list of allocations is required; send an empty list to remove all allocations.");
            }
            var items = request?.Allocations ?? new List<SaveLineAllocationItemDto>();
            if (items.Count > PartijCostRules.MaxAllocationsPerLine)
            {
                ValidationErrors.Add(errors, "allocations", $"At most {PartijCostRules.MaxAllocationsPerLine} lots per line.");
            }
            var seen = new HashSet<int>();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item?.PartijId == null)
                {
                    ValidationErrors.Add(errors, $"allocations[{i}].partijId", "Choose a lot.");
                }
                else if (!seen.Add(item.PartijId.Value))
                {
                    ValidationErrors.Add(errors, $"allocations[{i}].partijId", "The same lot occurs more than once on this line.");
                }
                if (item?.Amount == null || !PartijCostRules.IsAmount(item.Amount.Value))
                {
                    ValidationErrors.Add(errors, $"allocations[{i}].amount", "An amount excl. VAT is required, must be positive and may have at most 2 decimals.");
                }
            }
            if (errors.Count > 0)
            {
                return Validation(errors);
            }

            var invoice = await _db.IncomingInvoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
            var line = await _db.IncomingInvoiceLines.Include(l => l.CostArticle).SingleOrDefaultAsync(l => l.Id == lineId && l.IncomingInvoiceId == invoiceId, cancellationToken);
            if (invoice == null || line == null)
            {
                return ServiceResult<InvoiceAllocationsDto>.NotFound($"Line {lineId} of purchase invoice {invoiceId} does not exist.");
            }
            // Herhaling van een opgeslagen opdracht: de huidige stand (de events van die opdracht staan er al).
            if (await _db.PartijCostAuditEvents.AsNoTracking().AnyAsync(e => e.RequestId == request.RequestId, cancellationToken))
            {
                return await GetInvoiceAsync(invoiceId, cancellationToken);
            }
            if (!IsEuro(invoice))
            {
                return ServiceResult<InvoiceAllocationsDto>.Validation(new Dictionary<string, string[]> { ["allocations"] = new[] { $"Costs are allocated in euro; this invoice is in {invoice.Currency}." } });
            }
            if (request.ExpectedVersion != line.AllocationVersion)
            {
                return ServiceResult<InvoiceAllocationsDto>.Conflict("The allocation of this line was changed at the same time; reload and try again.");
            }
            var wanted = items.ToDictionary(i => i.PartijId!.Value, i => i.Amount!.Value);
            var known = await _db.Partijen.AsNoTracking().Where(p => wanted.Keys.Contains(p.Id)).Select(p => p.Id).ToListAsync(cancellationToken);
            var missing = wanted.Keys.Except(known).Cast<int?>().FirstOrDefault();
            if (missing != null)
            {
                return ServiceResult<InvoiceAllocationsDto>.NotFound($"Lot {missing} does not exist.");
            }
            var total = wanted.Values.Sum();
            if (total > line.LineTotal)
            {
                return ServiceResult<InvoiceAllocationsDto>.Validation(new Dictionary<string, string[]>
                {
                    ["allocations"] = new[] { $"The lots together ({total:0.00}) exceed the line amount excl. VAT ({line.LineTotal:0.00})." }
                });
            }

            var current = await _db.PartijCosts.Where(c => c.IncomingInvoiceLineId == lineId).ToListAsync(cancellationToken);
            var now = DateTime.UtcNow;
            var actor = _user.Actor(PartijCostRules.MaxActorLength);
            var kind = PartijCostRules.KindOf(line.ProductVariantId, line.CostArticle?.Name);
            void Audit(int partijId, string type, decimal? oldAmount, decimal? newAmount) => _db.PartijCostAuditEvents.Add(new PartijCostAuditEvent
            {
                IncomingInvoiceId = invoiceId, IncomingInvoiceLineId = lineId, PartijId = partijId, EventType = type, OldAmount = oldAmount, NewAmount = newAmount,
                RequestId = request.RequestId.Value, ActorReference = actor, OccurredAt = now
            });
            var changed = false;
            foreach (var cost in current.Where(c => !wanted.ContainsKey(c.PartijId)))
            {
                Audit(cost.PartijId, PartijCostRules.EventRemoved, cost.AmountExclVat, null);
                _db.PartijCosts.Remove(cost);
                changed = true;
            }
            foreach (var (partijId, amount) in wanted)
            {
                var cost = current.SingleOrDefault(c => c.PartijId == partijId);
                if (cost == null)
                {
                    _db.PartijCosts.Add(new PartijCost { IncomingInvoiceLineId = lineId, PartijId = partijId, AmountExclVat = amount, Kind = kind, CreatedAt = now, CreatedBy = actor, UpdatedAt = now });
                    Audit(partijId, PartijCostRules.EventAllocated, null, amount);
                    changed = true;
                }
                else if (cost.AmountExclVat != amount)
                {
                    Audit(partijId, PartijCostRules.EventChanged, cost.AmountExclVat, amount);
                    cost.AmountExclVat = amount;
                    cost.UpdatedAt = now;
                    changed = true;
                }
            }
            if (!changed)
            {
                return await GetInvoiceAsync(invoiceId, cancellationToken);
            }
            line.AllocationVersion++;
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult<InvoiceAllocationsDto>.Conflict("The allocation of this line was changed at the same time; reload and try again.");
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
            {
                return ServiceResult<InvoiceAllocationsDto>.Conflict("The allocation of this line was changed at the same time; reload and try again.");
            }
            return await GetInvoiceAsync(invoiceId, cancellationToken);
        }

        private static bool IsEuro(IncomingInvoice invoice) => string.IsNullOrWhiteSpace(invoice.Currency) || invoice.Currency == PartijCostRules.Currency;

        private static ServiceResult<InvoiceAllocationsDto> Validation(Dictionary<string, List<string>> errors) =>
            ServiceResult<InvoiceAllocationsDto>.Validation(ValidationErrors.ToArrays(errors));
    }
}
