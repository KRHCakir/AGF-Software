using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Definitief maken (knop, of later een gekoppelde factuur) en corrigeren na definitief (stap 4a). Gedeelde hulpstukken (versie,
    // herhaling, opslaan) staan in PurchaseOrderService.Edit.cs.
    public sealed partial class PurchaseOrderService
    {
        public async Task<ServiceResult<PurchaseOrderDetailDto>> FinalizeAsync(int id, FinalizePurchaseOrderDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (!HasCommand(request?.RequestId, request?.ExpectedVersion, errors)) return Validation<PurchaseOrderDetailDto>(errors);
            var replay = await ReplayAsync(request.RequestId!.Value, id, PurchaseOrderDocumentRules.KindFinalize, cancellationToken);
            if (replay != null) return replay;
            var order = await _db.SupplyCommitments.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (order == null) return ServiceResult<PurchaseOrderDetailDto>.NotFound($"Purchase order {id} does not exist.");
            // Al definitief (een tweede klik, of intussen door een factuur): niets nieuws, de eerste vastlegging blijft.
            if (PurchaseOrderDocumentRules.IsDefinitive(order)) return await GetAsync(id, cancellationToken);
            if (order.Status == SupplyRules.StatusCancelled) return Conflict(NotEditableCode, $"Purchase order {order.OrderNumber} is cancelled and is not made definitive.");
            if (order.EditVersion != request.ExpectedVersion) return Conflict(StaleCode, $"Purchase order {order.OrderNumber} has been changed in the meantime; reload it first.");
            _db.PurchaseOrderChanges.Add(PurchaseOrderDocumentRules.Finalize(order, PurchaseOrderDocumentRules.ViaManual, Actor(), DateTime.UtcNow, request.RequestId!.Value));
            return await SaveAsync(order, request.RequestId!.Value, cancellationToken);
        }

        // Stap 4b: een factuur wordt aan de order gekoppeld. Maakt de (gevolgde) order definitief via Invoice met de auditregel erbij; de
        // aanroeper slaat op in zijn eigen transactie. Geeft false als de order al definitief of geannuleerd was (dan verandert er niets).
        public async Task<bool> FinalizeForInvoiceAsync(int orderId, CancellationToken cancellationToken = default)
        {
            var order = await _db.SupplyCommitments.SingleAsync(c => c.Id == orderId, cancellationToken);
            // Een geannuleerde order wordt nooit definitief, ook niet via een factuur (zelfde regel als FinalizeAsync).
            if (order.Status == SupplyRules.StatusCancelled) return false;
            var change = PurchaseOrderDocumentRules.Finalize(order, PurchaseOrderDocumentRules.ViaInvoice, Actor(), DateTime.UtcNow, Guid.NewGuid());
            if (change == null) return false;
            _db.PurchaseOrderChanges.Add(change);
            return true;
        }

        public async Task<ServiceResult<PurchaseOrderDetailDto>> CorrectAsync(int id, CorrectPurchaseOrderDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (!HasCommand(request?.RequestId, request?.ExpectedVersion, errors)) return Validation<PurchaseOrderDetailDto>(errors);
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) AddError(errors, "reason", "A reason is required for a correction.");
            else if (reason.Length > PurchaseOrderDocumentRules.MaxReasonLength) AddError(errors, "reason", $"The reason may have at most {PurchaseOrderDocumentRules.MaxReasonLength} characters.");
            // Weggelaten (null) = ongewijzigd laten; een lege of witte tekst = expliciet wissen.
            var reference = Blank(request.SupplierReference);
            if (reference?.Length > PurchaseOrderRules.MaxSupplierReferenceLength) AddError(errors, "supplierReference", $"At most {PurchaseOrderRules.MaxSupplierReferenceLength} characters.");
            var note = Blank(request.Note);
            if (note?.Length > PurchaseOrderRules.MaxNoteLength) AddError(errors, "note", $"At most {PurchaseOrderRules.MaxNoteLength} characters.");
            var lines = request.Lines ?? new List<CorrectPurchaseOrderLineDto>();
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i]?.UnitPrice is { } price && !CostArticleRules.IsUnitPrice(price)) AddError(errors, $"lines[{i}].unitPrice", "Unit price, when given, must be 0 or more with at most 2 decimals.");
                if (lines[i] != null && lines.Take(i).Any(other => other?.Id == lines[i].Id)) AddError(errors, $"lines[{i}]", $"Line {lines[i].Id} is given more than once.");
            }
            if (errors.Count > 0) return Validation<PurchaseOrderDetailDto>(errors);
            var replay = await ReplayAsync(request.RequestId!.Value, id, PurchaseOrderDocumentRules.KindCorrection, cancellationToken);
            if (replay != null) return replay;

            var order = await _db.SupplyCommitments.Include(c => c.Lines).ThenInclude(l => l.ProductVariant).ThenInclude(v => v.Product)
                .AsSplitQuery().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (order == null) return ServiceResult<PurchaseOrderDetailDto>.NotFound($"Purchase order {id} does not exist.");
            var refused = Refusal(order, request.ExpectedVersion!.Value, PurchaseOrderDocumentRules.KindCorrection);
            if (refused != null) return refused;
            var blocked = PurchaseOrderDocumentRules.CorrectionBlockedReason(order, _db.BusinessCalendar.Today());
            if (blocked != null) return Conflict(LockedCode, blocked);

            // Eerst alle regels controleren, pas daarna iets wijzigen: een geweigerde correctie laat de order in de context ongemoeid.
            for (var i = 0; i < lines.Count; i++)
            {
                if (order.Lines.All(l => l.Id != lines[i]?.Id)) AddError(errors, $"lines[{i}]", $"Line {lines[i]?.Id} does not belong to purchase order {id}.");
            }
            if (errors.Count > 0) return Validation<PurchaseOrderDetailDto>(errors);

            var fields = new List<PurchaseOrderChangeField>();
            if (request.SupplierReference != null && !string.Equals(order.SupplierReference, reference, StringComparison.Ordinal))
            {
                fields.Add(new(PurchaseOrderDocumentRules.FieldSupplierReference, null, PurchaseOrderDocumentRules.ActionChanged, order.SupplierReference, reference));
                order.SupplierReference = reference;
            }
            if (request.Note != null && !string.Equals(order.Note, note, StringComparison.Ordinal))
            {
                fields.Add(new(PurchaseOrderDocumentRules.FieldNote, null, PurchaseOrderDocumentRules.ActionChanged, order.Note, note));
                order.Note = note;
            }
            for (var i = 0; i < lines.Count; i++)
            {
                var line = order.Lines.Single(l => l.Id == lines[i].Id);
                if (lines[i].UnitPrice == null || line.UnitPrice == lines[i].UnitPrice) continue;
                fields.Add(new(PurchaseOrderDocumentRules.FieldUnitPrice, line.Id, PurchaseOrderDocumentRules.ActionChanged, Number(line.UnitPrice), Number(lines[i].UnitPrice)));
                line.UnitPrice = lines[i].UnitPrice;
            }
            if (fields.Count == 0)
            {
                AddError(errors, "correction", "Change at least one price, the supplier's order number or the note.");
                return Validation<PurchaseOrderDetailDto>(errors);
            }
            return await SaveChangeAsync(order, request.RequestId!.Value, PurchaseOrderDocumentRules.KindCorrection, reason, fields, cancellationToken);
        }
    }
}
