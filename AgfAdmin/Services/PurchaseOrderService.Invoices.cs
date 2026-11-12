using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Stap 4b, de kant van de order: hoeveel er per orderregel besteld, ontvangen en al gefactureerd is, en de prijs op de bon aanpassen
    // aan een factuur (met auditregel). Aangeroepen door PurchaseInvoiceOrderLinks in de transactie van de factuur.
    public sealed partial class PurchaseOrderService
    {
        public sealed record LineQuantities(decimal Expected, decimal Received, decimal Invoiced);

        // Per orderregel van deze orders: besteld en ontvangen (in de eenheid van de regel, zoals de orderstatus) en wat er al op
        // facturen staat (afgewezen facturen tellen niet). Met excludeInvoiceId telt die factuur niet mee.
        public async Task<Dictionary<int, LineQuantities>> LineQuantitiesAsync(List<int> orderIds, int? excludeInvoiceId = null, CancellationToken cancellationToken = default)
        {
            var facts = await LoadFactsAsync(orderIds, cancellationToken);
            var lineIds = facts.ByOrder.Values.SelectMany(f => f.Lines).Select(l => l.Id).ToList();
            var invoiced = await InvoicedByLineAsync(lineIds, excludeInvoiceId, cancellationToken);
            return facts.ByOrder.Values.SelectMany(f => f.Lines)
                .ToDictionary(l => l.Id, l => new LineQuantities(l.ExpectedQuantity, l.ReceivedQuantity, invoiced.GetValueOrDefault(l.Id)));
        }

        public async Task<Dictionary<int, decimal>> InvoicedByLineAsync(List<int> lineIds, int? excludeInvoiceId = null, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<int, decimal>();
            foreach (var batch in lineIds.Chunk(500))
            {
                var ids = batch.ToList();
                var rows = await _db.IncomingInvoiceLines.AsNoTracking()
                    .Where(l => l.SupplyCommitmentLineId != null && ids.Contains(l.SupplyCommitmentLineId.Value)
                        && l.IncomingInvoice.Status != PurchaseInvoiceOrderLinkRules.StatusRejected && (excludeInvoiceId == null || l.IncomingInvoiceId != excludeInvoiceId))
                    .Select(l => new { Line = l.SupplyCommitmentLineId!.Value, l.Quantity })
                    .ToListAsync(cancellationToken);
                foreach (var row in rows) result[row.Line] = result.GetValueOrDefault(row.Line) + row.Quantity;
            }
            return result;
        }

        // "Bon aanpassen aan de factuur": de prijs op deze orderregels wordt de factuurprijs, met één Edit-auditregel (oud, nieuw, wie en
        // "n.a.v. factuur …"). Alleen een open magazijnorder (zoals Aanpassen, regel 29); de aanroeper controleert dat en slaat op, apart van
        // het definitief maken (regel 29: nooit samen in één save).
        public void AdjustPricesForInvoice(SupplyCommitment order, IReadOnlyDictionary<int, decimal> prices, string invoiceNumber)
        {
            var fields = new List<PurchaseOrderChangeField>();
            foreach (var line in order.Lines.OrderBy(l => l.Id))
            {
                if (!prices.TryGetValue(line.Id, out var price) || line.UnitPrice == price) continue;
                fields.Add(new(PurchaseOrderDocumentRules.FieldUnitPrice, line.Id, PurchaseOrderDocumentRules.ActionChanged, Number(line.UnitPrice), Number(price)));
                line.UnitPrice = price;
            }
            if (fields.Count == 0) return;
            var now = DateTime.UtcNow;
            order.EditVersion++;
            order.UpdatedAt = now;
            _db.PurchaseOrderChanges.Add(new PurchaseOrderChange
            {
                RequestId = Guid.NewGuid(), SupplyCommitmentId = order.Id, Kind = PurchaseOrderDocumentRules.KindEdit, Reason = InvoiceReason(invoiceNumber),
                ActorReference = Actor(), CreatedAt = now, ChangesJson = PurchaseOrderDocumentRules.Serialize(fields),
            });
        }

        public static string InvoiceReason(string invoiceNumber)
        {
            var reason = $"n.a.v. factuur {invoiceNumber}";
            return reason.Length <= PurchaseOrderDocumentRules.MaxReasonLength ? reason : reason[..PurchaseOrderDocumentRules.MaxReasonLength];
        }
    }
}
