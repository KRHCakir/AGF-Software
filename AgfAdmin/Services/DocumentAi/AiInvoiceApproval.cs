using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services.DocumentAi
{
    // Goedkeuren van een AI-voorstel gebeurt in het gewone opslagpad van de inkoopfactuur (PurchaseInvoiceService.CreateAsync), in dezelfde
    // transactie als de factuur zelf: de lezing wordt "goedgekeurd door" de ingelogde gebruiker (nooit van de client), met de goedgekeurde
    // waarden en wat de gebruiker vóór goedkeuren aanpaste (oud voorstel en nieuwe waarde); het originele document wordt een bijlage met
    // hash; een bevestigde orderkoppeling zet het factuurnummer op die order, zoals "Factuur ontvangen" (append-only, nooit dubbel).
    public sealed class AiInvoiceApproval(AppDbContext db)
    {
        public sealed record Prepared(AiExtraction Extraction, SupplyCommitment Order, List<AiAdjustmentDto> Adjustments);

        public async Task<Prepared> PrepareAsync(CreatePurchaseInvoiceDto request, Vendor vendor, Dictionary<string, List<string>> errors, CancellationToken cancellationToken)
        {
            if (request.AiExtractionId == null)
            {
                if (request.LinkPurchaseOrderId != null) ValidationErrors.Add(errors, "linkPurchaseOrderId", "A purchase order is linked only when approving an AI proposal.");
                return null;
            }
            var extraction = await db.AiExtractions.SingleOrDefaultAsync(e => e.Id == request.AiExtractionId, cancellationToken);
            if (extraction == null)
            {
                ValidationErrors.Add(errors, "aiExtractionId", $"AI reading {request.AiExtractionId} does not exist.");
                return null;
            }
            if (extraction.Status != AiReadingRules.StatusProposed || extraction.FileDeletedAt != null)
            {
                ValidationErrors.Add(errors, "aiExtractionId", $"AI reading {extraction.Id} is {extraction.Status.ToLowerInvariant()}; only an open proposal can be approved.");
                return null;
            }
            SupplyCommitment order = null;
            if (request.LinkPurchaseOrderId != null)
            {
                order = await db.SupplyCommitments.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.LinkPurchaseOrderId, cancellationToken);
                if (order == null || order.Status == SupplyRules.StatusCancelled || (vendor != null && order.VendorId != vendor.Id))
                {
                    ValidationErrors.Add(errors, "linkPurchaseOrderId", "The purchase order must exist, not be cancelled and belong to the supplier of this invoice.");
                }
            }
            var proposal = AiInvoiceReadingService.ToProposal((JsonObject)JsonNode.Parse(extraction.ProposalJson));
            var matches = await new AiInvoiceMatching(db).MatchAsync(proposal, cancellationToken);
            return new Prepared(extraction, order, Adjustments(proposal, matches, request, vendor?.Name));
        }

        public async Task ApplyAsync(Prepared prepared, IncomingInvoice invoice, decimal net, string actor, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var extraction = prepared.Extraction;
            extraction.Status = AiReadingRules.StatusApproved;
            extraction.ApprovedBy = actor;
            extraction.ApprovedAt = now;
            extraction.IncomingInvoice = invoice;
            extraction.ApprovedValuesJson = invoice.RequestPayload;
            extraction.AdjustmentsJson = JsonSerializer.Serialize(prepared.Adjustments, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            db.DocumentAttachments.Add(new DocumentAttachment
            {
                IncomingInvoice = invoice, FileName = extraction.FileName, MediaType = extraction.MediaType, SizeBytes = extraction.SizeBytes, Sha256 = extraction.Sha256,
                StorageKey = extraction.StorageKey, Source = "AiUpload", AiExtraction = extraction, CreatedBy = actor, CreatedAt = now,
            });
            if (prepared.Order == null) return;
            var key = PurchaseOrderRules.InvoiceNumberKey(invoice.InvoiceNumber);
            if (await db.SupplyCommitmentInvoiceRefs.AnyAsync(r => r.SupplyCommitmentId == prepared.Order.Id && r.Kind == PurchaseOrderRules.KindInvoice && r.InvoiceNumberKey == key, cancellationToken))
            {
                return;
            }
            db.SupplyCommitmentInvoiceRefs.Add(new SupplyCommitmentInvoiceRef
            {
                RequestId = Guid.NewGuid(), SupplyCommitmentId = prepared.Order.Id, Kind = PurchaseOrderRules.KindInvoice, InvoiceNumber = invoice.InvoiceNumber, InvoiceNumberKey = key,
                InvoiceDate = invoice.InvoiceDate == null ? null : DateOnly.FromDateTime(invoice.InvoiceDate.Value), AmountExclVat = net, IncomingInvoice = invoice,
                ActorReference = actor, CreatedAt = now,
            });
        }

        // Wat de AI voorstelde tegenover wat de gebruiker goedkeurde, per veld dat verschilt (regels op volgorde).
        public static List<AiAdjustmentDto> Adjustments(AiInvoiceProposalDto proposal, AiMatchesDto matches, CreatePurchaseInvoiceDto request, string vendorName)
        {
            var list = new List<AiAdjustmentDto>();
            void Compare(string field, string proposed, string final)
            {
                if (!string.Equals(Normalize(proposed), Normalize(final), StringComparison.Ordinal)) list.Add(new AiAdjustmentDto(field, proposed, final));
            }
            if (matches.SuggestedVendorId != request.VendorId) list.Add(new AiAdjustmentDto("vendor", proposal.VendorName, vendorName));
            Compare("invoiceNumber", proposal.InvoiceNumber, request.InvoiceNumber);
            Compare("invoiceDate", proposal.InvoiceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PurchaseInvoiceRules.AsUtcDate(request.InvoiceDate)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(request.Currency)) Compare("currency", proposal.Currency, request.Currency.ToUpperInvariant());
            var lines = request.Lines ?? new List<CreatePurchaseInvoiceLineDto>();
            for (var i = 0; i < Math.Max(lines.Count, proposal.Lines.Count); i++)
            {
                if (i >= proposal.Lines.Count) { list.Add(new AiAdjustmentDto($"lines[{i}]", null, "added")); continue; }
                var proposed = proposal.Lines[i];
                if (i >= lines.Count || lines[i] == null) { list.Add(new AiAdjustmentDto($"lines[{i}]", proposed.Description, "removed")); continue; }
                var final = lines[i];
                Compare($"lines[{i}].description", proposed.Description, final.Description);
                Compare($"lines[{i}].quantity", Number(proposed.Quantity), Number(final.Quantity));
                Compare($"lines[{i}].unit", proposed.Unit, final.Unit);
                Compare($"lines[{i}].unitPrice", Number(proposed.UnitPrice), Number(final.UnitPrice));
                Compare($"lines[{i}].vatPercentage", Number(proposed.VatPercentage), Number(final.VatPercentage));
                var suggestedCost = matches.LineCostArticles.FirstOrDefault(c => c.Index == i)?.CostArticleId;
                if (final.CostArticleId != null || suggestedCost != null) Compare($"lines[{i}].costArticleId", suggestedCost?.ToString(CultureInfo.InvariantCulture), final.CostArticleId?.ToString(CultureInfo.InvariantCulture));
            }
            if (matches.SuggestedOrderId != request.LinkPurchaseOrderId)
            {
                list.Add(new AiAdjustmentDto("purchaseOrder", matches.SuggestedOrderId?.ToString(CultureInfo.InvariantCulture), request.LinkPurchaseOrderId?.ToString(CultureInfo.InvariantCulture)));
            }
            return list;
        }

        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static string Number(decimal? value) => value == null ? null : (value.Value / 1.000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
    }
}

namespace AgfAdmin.Services.DocumentAi
{
    // Weergavenaam van een model-id voor op de factuur: "claude-haiku-5-5" is "Claude Haiku 5.5", de demolezer "Demolezer".
    public static class AiModelNames
    {
        public static string Display(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return null;
            if (model == DemoInvoiceAi.Model) return "Demolezer";
            var parts = model.Split('-');
            var numbers = parts.Reverse().TakeWhile(p => p.All(char.IsDigit)).Reverse().ToArray();
            var words = parts.Take(parts.Length - numbers.Length).Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]);
            return string.Join(' ', words) + (numbers.Length > 0 ? " " + string.Join('.', numbers) : "");
        }
    }
}
