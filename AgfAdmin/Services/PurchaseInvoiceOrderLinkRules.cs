namespace AgfAdmin.Services
{
    // Rekenregels voor een inkoopfactuur gekoppeld aan de bon (stap 4b), zonder database; gespiegeld in
    // agf-frontend/src/lib/invoiceOrderLink.ts. "Bon" is de inkooporder met haar orderregels (aantal, eenheid, prijs).
    public static class PurchaseInvoiceOrderLinkRules
    {
        // Wat er met een prijsverschil gebeurt: de prijs op de orderregel wordt de factuurprijs (met auditregel), of de bon blijft staan en
        // het verschil blijft zichtbaar op factuur en order. De factuur wint altijd voor het bedrag dat betaald wordt.
        public const string AdjustOrder = "AdjustOrder";
        public const string RecordDifference = "RecordDifference";
        public static readonly IReadOnlyList<string> Decisions = new[] { AdjustOrder, RecordDifference };

        // Factuurstatus die niet meetelt als gefactureerd.
        public const string StatusRejected = "Rejected";

        // De prijs per eenheid die de factuur rekent: prijs min de regelkorting, op 2 decimalen (zoals een prijs op de orderregel).
        public static decimal InvoiceUnitPrice(decimal unitPrice, decimal discountPercentage) =>
            InvoiceMath.Round2(unitPrice * (1m - discountPercentage / 100m));

        // Een verschil met de bon: de bon heeft geen prijs (op afrekening), of de factuur rekent per eenheid iets anders. Vergeleken met de
        // prijs na korting zonder afronding: 0,01% korting op 13,50 is 13,49865 en dus een verschil, ook al rondt dat af op 13,50.
        public static bool HasPriceDifference(decimal? orderUnitPrice, decimal unitPrice, decimal discountPercentage) =>
            orderUnitPrice == null || unitPrice * (1m - discountPercentage / 100m) != orderUnitPrice.Value;

        // Het bedrag volgens de bon voor dit aantal (null zonder prijs op de bon).
        public static decimal? OrderAmount(decimal quantity, decimal? orderUnitPrice) =>
            orderUnitPrice == null ? null : InvoiceMath.Round2(quantity * orderUnitPrice.Value);

        // Verschil in bedrag: wat de factuur rekent min wat de bon zegt voor hetzelfde aantal (null zonder prijs op de bon).
        public static decimal? Difference(decimal lineNet, decimal quantity, decimal? orderUnitPrice) =>
            OrderAmount(quantity, orderUnitPrice) is decimal order ? lineNet - order : null;

        // Waartegen gefactureerd wordt: wat ontvangen is zodra er iets ontvangen is, anders wat besteld is (een factuur vóór aankomst).
        public static decimal InvoiceBasis(decimal expectedQuantity, decimal receivedQuantity) => receivedQuantity > 0m ? receivedQuantity : expectedQuantity;

        // Wat nog openstaat om te factureren (nooit onder 0).
        public static decimal OpenQuantity(decimal expectedQuantity, decimal receivedQuantity, decimal invoicedQuantity) =>
            Math.Max(0m, InvoiceBasis(expectedQuantity, receivedQuantity) - invoicedQuantity);

        // Gefactureerd per order: Niet (geen factuurregel), Deels of Gefactureerd (elke regel tot de basis). Een order met alleen een oude
        // "Factuur ontvangen"-verwijzing (zonder regels) telt als gefactureerd: zo legde je dat vóór stap 4b vast.
        public const string BillingNone = "None";
        public const string BillingPartial = "Partial";
        public const string BillingFull = "Full";
        public static readonly IReadOnlyList<string> BillingStatuses = new[] { BillingNone, BillingPartial, BillingFull };

        public static string BillingStatus(IEnumerable<(decimal Expected, decimal Received, decimal Invoiced)> lines, bool hasInvoiceReference)
        {
            var list = lines.ToList();
            if (list.All(l => l.Invoiced <= 0m)) return hasInvoiceReference ? BillingFull : BillingNone;
            return list.All(l => l.Invoiced >= InvoiceBasis(l.Expected, l.Received)) ? BillingFull : BillingPartial;
        }
    }
}
