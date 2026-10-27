using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    public sealed partial class PurchaseOrderService
    {
        // ---------------- Factuurnummer koppelen ----------------

        public async Task<ServiceResult<PurchaseOrderDetailDto>> AddInvoiceRefAsync(int id, AddPurchaseOrderInvoiceRefDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var candidate = BuildInvoiceRef(id, request, errors);
            if (errors.Count > 0)
            {
                return Validation<PurchaseOrderDetailDto>(errors);
            }

            var existing = await _db.SupplyCommitmentInvoiceRefs.AsNoTracking().SingleOrDefaultAsync(r => r.RequestId == candidate.RequestId, cancellationToken);
            if (existing != null)
            {
                return await InvoiceRefRetryAsync(existing, candidate, cancellationToken);
            }
            if (!await _db.SupplyCommitments.AnyAsync(c => c.Id == id, cancellationToken))
            {
                return ServiceResult<PurchaseOrderDetailDto>.NotFound($"Purchase order {id} does not exist.");
            }
            var duplicate = await DuplicateInvoiceRefAsync(candidate, cancellationToken);
            if (duplicate != null)
            {
                return duplicate;
            }

            _db.SupplyCommitmentInvoiceRefs.Add(candidate);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                // Gelijktijdige retry (RequestId) of gelijktijdig hetzelfde nummer (unieke index op order + soort + nummer).
                _db.ChangeTracker.Clear();
                var concurrent = await _db.SupplyCommitmentInvoiceRefs.AsNoTracking().SingleOrDefaultAsync(r => r.RequestId == candidate.RequestId, cancellationToken);
                if (concurrent != null)
                {
                    return await InvoiceRefRetryAsync(concurrent, candidate, cancellationToken);
                }
                if (DbErrors.IsUniqueViolation(exception))
                {
                    return await DuplicateInvoiceRefAsync(candidate, cancellationToken) ?? throw exception;
                }
                throw;
            }

            var detail = await GetAsync(id, cancellationToken);
            return ServiceResult<PurchaseOrderDetailDto>.Created(detail.Value);
        }

        private static SupplyCommitmentInvoiceRef BuildInvoiceRef(int id, AddPurchaseOrderInvoiceRefDto request, Dictionary<string, List<string>> errors)
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
            if (!PurchaseOrderRules.IsInvoiceKind(request.Kind))
            {
                AddError(errors, "kind", $"Kind must be one of: {string.Join(", ", PurchaseOrderRules.InvoiceKinds)}.");
            }
            var number = PurchaseOrderRules.NormalizeInvoiceNumber(request.InvoiceNumber);
            if (number == null)
            {
                AddError(errors, "invoiceNumber", "An invoice number is required.");
            }
            else if (number.Length > PurchaseOrderRules.MaxInvoiceNumberLength)
            {
                AddError(errors, "invoiceNumber", $"This field may contain at most {PurchaseOrderRules.MaxInvoiceNumberLength} characters.");
            }
            // Bedrag excl. btw: optioneel, hooguit 2 decimalen en binnen decimal(18,2); een creditnota mag positief of negatief zijn.
            if (request.AmountExclVat != null && (decimal.Round(request.AmountExclVat.Value, 2) != request.AmountExclVat.Value || Math.Abs(request.AmountExclVat.Value) >= 10_000_000_000_000_000m))
            {
                AddError(errors, "amountExclVat", "The amount may have at most 2 decimals and must fit in 16 digits.");
            }
            // Factuurdatum is optioneel, maar mag niet in de toekomst liggen.
            if (request.InvoiceDate != null && request.InvoiceDate.Value > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                AddError(errors, "invoiceDate", "The invoice date cannot be in the future.");
            }
            var actor = string.IsNullOrWhiteSpace(request.ActorReference) ? null : request.ActorReference.Trim();
            if (actor != null && actor.Length > PurchaseOrderRules.MaxActorLength)
            {
                AddError(errors, "actorReference", $"This field may contain at most {PurchaseOrderRules.MaxActorLength} characters.");
            }
            if (errors.Count > 0)
            {
                return null;
            }

            return new SupplyCommitmentInvoiceRef
            {
                RequestId = request.RequestId!.Value,
                SupplyCommitmentId = id,
                Kind = request.Kind,
                InvoiceNumber = number,
                InvoiceDate = request.InvoiceDate,
                AmountExclVat = request.AmountExclVat,
                ActorReference = actor,
                CreatedAt = DateTime.UtcNow
            };
        }

        private async Task<ServiceResult<PurchaseOrderDetailDto>> DuplicateInvoiceRefAsync(SupplyCommitmentInvoiceRef candidate, CancellationToken cancellationToken)
        {
            var number = candidate.InvoiceNumber.ToLowerInvariant();
            var taken = await _db.SupplyCommitmentInvoiceRefs.AsNoTracking()
                .AnyAsync(r => r.SupplyCommitmentId == candidate.SupplyCommitmentId && r.Kind == candidate.Kind && r.InvoiceNumber.ToLower() == number, cancellationToken);
            return taken
                ? ServiceResult<PurchaseOrderDetailDto>.Conflict(
                    $"{(candidate.Kind == PurchaseOrderRules.KindCreditNote ? "Credit note" : "Invoice")} {candidate.InvoiceNumber} is already recorded on this purchase order.",
                    new { message = $"{candidate.Kind} {candidate.InvoiceNumber} is already recorded on this purchase order.", code = "INVOICE_REF_DUPLICATE" })
                : null;
        }

        private async Task<ServiceResult<PurchaseOrderDetailDto>> InvoiceRefRetryAsync(SupplyCommitmentInvoiceRef existing, SupplyCommitmentInvoiceRef candidate, CancellationToken cancellationToken)
        {
            var same = existing.SupplyCommitmentId == candidate.SupplyCommitmentId
                && existing.Kind == candidate.Kind
                && string.Equals(existing.InvoiceNumber, candidate.InvoiceNumber, StringComparison.Ordinal)
                && existing.InvoiceDate == candidate.InvoiceDate
                && existing.AmountExclVat == candidate.AmountExclVat
                && string.Equals(existing.ActorReference, candidate.ActorReference, StringComparison.Ordinal);
            return same
                ? await GetAsync(existing.SupplyCommitmentId, cancellationToken)
                : ServiceResult<PurchaseOrderDetailDto>.Conflict($"RequestId {candidate.RequestId} was already used for a different invoice reference.");
        }
    }
}
