using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Een inkoopfactuur koppelen aan de bon (stap 4b), in het gewone opslagpad van de factuur (PurchaseInvoiceService.CreateAsync).
    // Vóór die stap bestond de koppeling alleen als verwijzing: "Factuur ontvangen" legde nummer en bedrag op de order vast zonder
    // inkoopfactuur, en "Verwijzing ontvangst" op de factuur is vrije tekst. Nu factureert elke gekoppelde factuurregel één orderregel
    // (IncomingInvoiceLine.SupplyCommitmentLineId), met de prijs die op de bon stond. Na opslaan, in dezelfde transactie: de verwijzing op
    // elke order (zoals Factuur ontvangen, met de factuur erbij), bij "Bon aanpassen" de prijs op de orderregel met auditregel, en daarna
    // wordt elke gekoppelde order Definitief (stap 4a). Een order die al Definitief was blijft dat en verandert alleen via Correctie.
    public sealed class PurchaseInvoiceOrderLinks(AppDbContext db, ICurrentUser user)
    {
        public sealed record LinkedLine(int Index, IncomingInvoiceLine Line, SupplyCommitmentLine OrderLine, decimal InvoiceUnitPrice, bool Differs);
        public sealed record Prepared(List<LinkedLine> Lines, string Decision);

        private readonly PurchaseOrderService _orders = new(db, user);

        // Controleert de koppelingen (na het opbouwen van de regels, in dezelfde volgorde als het verzoek) en vult per factuurregel de
        // orderregel en de prijs op de bon in. Null als er niets gekoppeld is.
        public async Task<Prepared> PrepareAsync(CreatePurchaseInvoiceDto request, Vendor vendor, List<IncomingInvoiceLine> lines, Dictionary<string, List<string>> errors,
            CancellationToken cancellationToken)
        {
            var requested = request.Lines.Select((line, index) => (line, index)).Where(x => x.line.SupplyCommitmentLineId != null).ToList();
            if (requested.Count == 0) return null;
            var ids = requested.Select(x => x.line.SupplyCommitmentLineId!.Value).Distinct().ToList();
            var orderLines = await db.SupplyCommitmentLines.Include(l => l.SupplyCommitment).ThenInclude(c => c.Lines)
                .Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);

            var linked = new List<LinkedLine>();
            foreach (var (line, index) in requested)
            {
                var key = $"lines[{index}]";
                var id = line.SupplyCommitmentLineId!.Value;
                if (requested.Any(other => other.index < index && other.line.SupplyCommitmentLineId == id))
                {
                    ValidationErrors.Add(errors, $"{key}.supplyCommitmentLineId", $"Purchase order line {id} is already on this invoice; invoice it on one line.");
                    continue;
                }
                if (!orderLines.TryGetValue(id, out var orderLine))
                {
                    ValidationErrors.Add(errors, $"{key}.supplyCommitmentLineId", $"Purchase order line {id} does not exist.");
                    continue;
                }
                var order = orderLine.SupplyCommitment;
                var number = OrderName(order);
                if (order.VendorId != vendor.Id)
                {
                    ValidationErrors.Add(errors, $"{key}.supplyCommitmentLineId",
                        $"Purchase order {number} is from another supplier; an invoice of {vendor.Name} is only linked to orders of {vendor.Name}.");
                    continue;
                }
                if (order.Status == SupplyRules.StatusCancelled)
                {
                    ValidationErrors.Add(errors, $"{key}.supplyCommitmentLineId", $"Purchase order {number} is cancelled and is not invoiced.");
                    continue;
                }
                if (line.CostArticleId != null)
                {
                    ValidationErrors.Add(errors, $"{key}.costArticleId", "A line linked to a purchase order line is that article, not a cost article.");
                    continue;
                }
                if (line.ProductVariantId != orderLine.ProductVariantId)
                {
                    ValidationErrors.Add(errors, $"{key}.productVariantId", $"A line linked to purchase order {number} has the article of its order line.");
                    continue;
                }
                var unit = string.IsNullOrWhiteSpace(line.Unit) ? null : line.Unit.Trim();
                if (orderLine.ExpectedUnit != null && !string.Equals(unit, orderLine.ExpectedUnit, StringComparison.Ordinal))
                {
                    ValidationErrors.Add(errors, $"{key}.unit", $"A line linked to purchase order {number} is invoiced in the unit of its order line ({orderLine.ExpectedUnit}).");
                    continue;
                }
                var invoiceLine = lines[index];
                var price = PurchaseInvoiceOrderLinkRules.InvoiceUnitPrice(invoiceLine.UnitPrice, invoiceLine.DiscountPercentage);
                invoiceLine.SupplyCommitmentLineId = id;
                invoiceLine.OrderUnitPrice = orderLine.UnitPrice;
                linked.Add(new LinkedLine(index, invoiceLine, orderLine, price, PurchaseInvoiceOrderLinkRules.HasPriceDifference(orderLine.UnitPrice, price)));
            }
            if (errors.Count > 0) return null;

            // Meer dan besteld of ontvangen: alleen na de waarschuwing (AcceptOverInvoicing). Wat al op andere facturen staat telt mee.
            var quantities = await _orders.LineQuantitiesAsync(linked.Select(l => l.OrderLine.SupplyCommitmentId).Distinct().ToList(), null, cancellationToken);
            if (request.AcceptOverInvoicing != true)
            {
                foreach (var link in linked)
                {
                    var q = quantities.GetValueOrDefault(link.OrderLine.Id) ?? new PurchaseOrderService.LineQuantities(link.OrderLine.ExpectedQuantity, 0m, 0m);
                    var basis = PurchaseInvoiceOrderLinkRules.InvoiceBasis(q.Expected, q.Received);
                    if (q.Invoiced + link.Line.Quantity > basis)
                    {
                        ValidationErrors.Add(errors, $"lines[{link.Index}].quantity",
                            $"Invoiced {q.Invoiced + link.Line.Quantity:0.###} in total, more than the {basis:0.###} {(q.Received > 0m ? "received" : "ordered")} on purchase order {OrderName(link.OrderLine.SupplyCommitment)}; confirm to save anyway.");
                    }
                }
            }

            // Een prijsverschil vraagt een keuze, eenmaal per factuur; zonder verschil wordt er niets gekozen.
            var differing = linked.Where(l => l.Differs).ToList();
            string decision = null;
            if (differing.Count > 0)
            {
                decision = request.OrderPriceDecision?.Trim();
                if (decision == null || !PurchaseInvoiceOrderLinkRules.Decisions.Contains(decision, StringComparer.Ordinal))
                {
                    ValidationErrors.Add(errors, "orderPriceDecision",
                        $"The invoice differs from the purchase order on {differing.Count} line(s): choose {PurchaseInvoiceOrderLinkRules.AdjustOrder} (the order gets the invoice price) or {PurchaseInvoiceOrderLinkRules.RecordDifference} (the order stays, the difference is kept).");
                }
                else if (decision == PurchaseInvoiceOrderLinkRules.AdjustOrder)
                {
                    foreach (var order in differing.Select(l => l.OrderLine.SupplyCommitment).Distinct())
                    {
                        if (PurchaseOrderDocumentRules.IsDefinitive(order) || order.Route != SupplyRules.RouteWarehouse)
                        {
                            ValidationErrors.Add(errors, "orderPriceDecision", PurchaseOrderDocumentRules.IsDefinitive(order)
                                ? $"Purchase order {OrderName(order)} is already definitive: change its price with Correction on the order (with a reason), or record the difference."
                                : $"Purchase order {OrderName(order)} is a direct trade order and is not edited: record the difference.");
                        }
                    }
                }
            }
            return errors.Count > 0 ? null : new Prepared(linked, decision);
        }

        // Na het opslaan van de factuur, in dezelfde transactie. Elke stap is een eigen save: regel 29 staat een aanpassing en het
        // definitief maken van een order nooit samen toe. extraOrderId: de order die bij het goedkeuren van een AI-voorstel gekoppeld werd.
        public async Task ApplyAsync(Prepared prepared, IncomingInvoice invoice, int? extraOrderId, CancellationToken cancellationToken)
        {
            var actor = user?.Actor(PurchaseOrderRules.MaxActorLength) ?? CurrentUserExtensions.UnknownActor;
            var byOrder = (prepared?.Lines ?? new List<LinkedLine>()).GroupBy(l => l.OrderLine.SupplyCommitment).ToList();
            var key = PurchaseOrderRules.InvoiceNumberKey(invoice.InvoiceNumber);
            var now = DateTime.UtcNow;
            foreach (var group in byOrder)
            {
                var order = group.Key;
                // Staat het nummer al op de order (eerder via "Factuur ontvangen"), dan blijft die verwijzing zoals ze is (append-only); de
                // koppeling zelf zit in de factuurregels.
                var exists = await db.SupplyCommitmentInvoiceRefs.AsNoTracking()
                    .AnyAsync(r => r.SupplyCommitmentId == order.Id && r.Kind == PurchaseOrderRules.KindInvoice && r.InvoiceNumberKey == key, cancellationToken);
                if (!exists)
                {
                    db.SupplyCommitmentInvoiceRefs.Add(new SupplyCommitmentInvoiceRef
                    {
                        RequestId = Guid.NewGuid(), SupplyCommitmentId = order.Id, Kind = PurchaseOrderRules.KindInvoice, InvoiceNumber = invoice.InvoiceNumber, InvoiceNumberKey = key,
                        InvoiceDate = invoice.InvoiceDate == null ? null : DateOnly.FromDateTime(invoice.InvoiceDate.Value),
                        AmountExclVat = group.Sum(l => l.Line.LineTotal), IncomingInvoiceId = invoice.Id, ActorReference = actor, CreatedAt = now,
                    });
                }
            }
            await db.SaveChangesAsync(cancellationToken);

            if (prepared?.Decision == PurchaseInvoiceOrderLinkRules.AdjustOrder)
            {
                foreach (var group in byOrder)
                {
                    var prices = group.Where(l => l.Differs).ToDictionary(l => l.OrderLine.Id, l => l.InvoiceUnitPrice);
                    if (prices.Count > 0) _orders.AdjustPricesForInvoice(group.Key, prices, invoice.InvoiceNumber);
                }
                await db.SaveChangesAsync(cancellationToken);
            }

            var orderIds = byOrder.Select(g => g.Key.Id).Concat(extraOrderId is int extra ? new[] { extra } : Array.Empty<int>()).Distinct().ToList();
            var finalized = false;
            foreach (var orderId in orderIds) finalized |= await _orders.FinalizeForInvoiceAsync(orderId, cancellationToken);
            if (finalized) await db.SaveChangesAsync(cancellationToken);
        }

        private static string OrderName(SupplyCommitment order) =>
            string.IsNullOrWhiteSpace(order.SupplierReference) ? order.OrderNumber : $"{order.SupplierReference} ({order.OrderNumber})";
    }
}
