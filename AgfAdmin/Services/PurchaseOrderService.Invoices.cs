using AgfAdmin.DTOs;
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

        // De facturen die regels van deze order factureren: bedrag van die regels en het verschil met de prijs op de bon.
        private async Task<List<PurchaseOrderLinkedInvoiceDto>> LinkedInvoicesAsync(int orderId, CancellationToken cancellationToken)
        {
            var rows = await _db.IncomingInvoiceLines.AsNoTracking().Where(l => l.SupplyCommitmentLine.SupplyCommitmentId == orderId)
                .Select(l => new
                {
                    l.IncomingInvoiceId, l.IncomingInvoice.InvoiceNumber, l.IncomingInvoice.InvoiceDate, l.IncomingInvoice.Status, l.IncomingInvoice.Currency,
                    l.IncomingInvoice.OrderPriceDecision, l.LineTotal, l.Quantity, l.OrderUnitPrice,
                })
                .ToListAsync(cancellationToken);
            return rows.GroupBy(r => r.IncomingInvoiceId).OrderBy(g => g.Key).Select(g =>
            {
                var first = g.First();
                var differences = g.Select(r => PurchaseInvoiceOrderLinkRules.Difference(r.LineTotal, r.Quantity, r.OrderUnitPrice)).Where(d => d != null).ToList();
                return new PurchaseOrderLinkedInvoiceDto
                {
                    Id = g.Key, InvoiceNumber = first.InvoiceNumber, InvoiceDate = first.InvoiceDate, Status = first.Status, Currency = first.Currency,
                    AmountExclVat = g.Sum(r => r.LineTotal), Difference = differences.Count == 0 ? null : differences.Sum(d => d.Value), Decision = first.OrderPriceDecision,
                };
            }).ToList();
        }

        // Orders van deze leverancier om een factuur aan te koppelen, nieuwste eerst: zonder zoekterm alleen orders met nog iets open om te
        // factureren; met zoekterm alles wat erbij past (zoals de orderlijst: ordernummer, ordernummer leverancier, partij, factuurnummer).
        // Geannuleerde orders nooit. PriceAdjustable: de prijs op de bon kan bij een verschil worden aangepast (open magazijnorder).
        public async Task<List<PurchaseOrderInvoiceCandidateDto>> InvoiceCandidatesAsync(int vendorId, string query, int? orderId, CancellationToken cancellationToken = default)
        {
            // De hele lijst, niet één pagina: anders vallen oudere open orders (of de gevraagde order) buiten de kandidaten.
            var list = await ListAllAsync(query, "all", vendorId, null, cancellationToken);
            var ids = list.Value.Where(i => i.StoredStatus != SupplyRules.StatusCancelled && (orderId == null || i.Id == orderId)).Select(i => i.Id).ToList();
            if (orderId != null && ids.Count == 0 && string.IsNullOrWhiteSpace(query)) return new List<PurchaseOrderInvoiceCandidateDto>();
            var facts = await LoadFactsAsync(ids, cancellationToken);
            var orders = await _db.SupplyCommitments.AsNoTracking().Where(c => ids.Contains(c.Id))
                .Select(c => new
                {
                    c.Id, c.OrderNumber, c.SupplierReference, c.OrderedOn, c.ExpectedAt, PartijNumber = c.Partij.Number, c.DocumentStatus, c.Route,
                    Lines = c.Lines.OrderBy(l => l.Id).Select(l => new { l.Id, l.ProductVariantId, Product = l.ProductVariant.Product.Name, Variant = l.ProductVariant.Name, l.ExpectedUnit, l.UnitPrice, PartijNumber = l.Partij.Number }).ToList(),
                })
                .ToListAsync(cancellationToken);
            var byId = orders.ToDictionary(o => o.Id);
            var result = new List<PurchaseOrderInvoiceCandidateDto>();
            foreach (var id in ids)
            {
                var order = byId[id];
                var lineFacts = facts.For(id).Lines.ToDictionary(l => l.Id);
                var lines = order.Lines.Where(l => lineFacts.ContainsKey(l.Id)).Select(l =>
                {
                    var f = lineFacts[l.Id];
                    return new PurchaseOrderInvoiceCandidateLineDto
                    {
                        Id = l.Id, ProductVariantId = l.ProductVariantId, ProductName = l.Product, ProductVariantName = l.Variant, Unit = l.ExpectedUnit,
                        ExpectedQuantity = f.ExpectedQuantity, ReceivedQuantity = f.ReceivedQuantity, InvoicedQuantity = f.InvoicedQuantity, OpenQuantity = f.OpenToInvoice,
                        UnitPrice = l.UnitPrice, PartijNumber = l.PartijNumber ?? order.PartijNumber,
                    };
                })
                // Een geheel vervangen regel zonder ontvangst en zonder factuur doet niet mee.
                .Where(l => l.ExpectedQuantity > 0m || l.ReceivedQuantity > 0m || l.InvoicedQuantity > 0m).ToList();
                if (string.IsNullOrWhiteSpace(query) && orderId == null && lines.All(l => l.OpenQuantity <= 0m)) continue;
                result.Add(new PurchaseOrderInvoiceCandidateDto
                {
                    Id = order.Id, OrderNumber = order.OrderNumber, SupplierReference = order.SupplierReference, OrderedOn = order.OrderedOn,
                    ExpectedAt = order.ExpectedAt == null ? null : DateTime.SpecifyKind(order.ExpectedAt.Value, DateTimeKind.Utc), PartijNumber = order.PartijNumber,
                    DocumentStatus = order.DocumentStatus, Route = order.Route,
                    PriceAdjustable = order.DocumentStatus == PurchaseOrderDocumentRules.StatusOpen && order.Route == SupplyRules.RouteWarehouse,
                    InvoiceNumbers = facts.For(id).Refs.Where(r => r.Kind == PurchaseOrderRules.KindInvoice).OrderBy(r => r.CreatedAt).Select(r => r.InvoiceNumber).ToList(),
                    Lines = lines,
                });
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
