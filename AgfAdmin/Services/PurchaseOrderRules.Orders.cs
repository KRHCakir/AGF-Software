namespace AgfAdmin.Services
{
    // Inkooporder: weergavestatus, factuurnummers en de factuurindicator (zie PurchaseOrderRules.cs).
    public static partial class PurchaseOrderRules
    {
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;

        public const string KindInvoice = "Invoice";
        public const string KindCreditNote = "CreditNote";
        public static readonly IReadOnlyList<string> InvoiceKinds = new[] { KindInvoice, KindCreditNote };

        public const string DecisionReturnToSupplier = "ReturnToSupplier";
        public const string DecisionClaimDiscount = "ClaimDiscount";
        public const string DecisionDispose = "Dispose";
        public static readonly IReadOnlyList<string> RejectionDecisions = new[] { DecisionReturnToSupplier, DecisionClaimDiscount, DecisionDispose };

        // Na terugsturen of korting claimen hoort er een creditnota van de leverancier te komen; bij vernietigen niet.
        public static bool ExpectsCreditNote(string decision) => decision is DecisionReturnToSupplier or DecisionClaimDiscount;

        // Korting claimen houdt de partij: die gaat naar Blocked (later vrij te geven). Terugsturen en vernietigen keuren definitief af.
        public static bool KeepsGoods(string decision) => decision == DecisionClaimDiscount;

        public static string RejectionQualityStatus(string decision) => KeepsGoods(decision) ? PalletRules.QualityBlocked : PalletRules.QualityRejected;

        // Afgesplitste inhoud en inhoud die al in zijn geheel is afgekeurd vallen al onder een afkeuring; nog eens afkeuren zou dubbel tellen.
        public static bool IsCoveredByRejection(int? originRejectionLineId, bool wholeRejected) => originRejectionLineId != null || wholeRejected;

        // Weergavestatus van de inkooporder (los van de opgeslagen Status, die bij ontvangst niet wordt bijgewerkt):
        // Ordered (Besteld), PartiallyReceived, Received, Rejected (Afgekeurd), Cancelled. Delivered (DirectTrade bij de klant
        // afgeleverd) telt voor de inkoop als Received: de goederen zijn geleverd.
        public const string OrderOrdered = "Ordered";
        public const string OrderPartiallyReceived = "PartiallyReceived";
        public const string OrderReceived = "Received";
        public const string OrderRejected = "Rejected";
        public const string OrderCancelled = "Cancelled";
        public static readonly IReadOnlyList<string> OrderStatuses = new[] { OrderOrdered, OrderPartiallyReceived, OrderReceived, OrderRejected, OrderCancelled };
        public static readonly IReadOnlyList<string> OpenOrderStatuses = new[] { OrderOrdered, OrderPartiallyReceived };

        public const string InvoiceNone = "None";
        public const string InvoiceReceived = "Received";
        public const string InvoiceCreditNoteExpected = "CreditNoteExpected";

        public static bool IsInvoiceKind(string value) => value != null && InvoiceKinds.Contains(value, StringComparer.Ordinal);
        public static bool IsOrderStatus(string value) => value != null && OrderStatuses.Contains(value, StringComparer.Ordinal);

        // Per regel: verwacht en ontvangen (in de eenheid van de regel). Geen ontvangst = Besteld; alles binnen = Ontvangen.
        public static string OrderStatus(string storedStatus, IEnumerable<(decimal Expected, decimal Received)> lines)
        {
            if (storedStatus == SupplyRules.StatusCancelled) return OrderCancelled;
            if (storedStatus == SupplyRules.StatusRejected) return OrderRejected;
            if (storedStatus == SupplyRules.StatusDelivered || storedStatus == SupplyRules.StatusReceived) return OrderReceived;
            var list = lines.ToList();
            if (list.Count == 0 || list.All(l => l.Received <= 0m)) return OrderOrdered;
            return list.All(l => l.Received >= l.Expected) ? OrderReceived : OrderPartiallyReceived;
        }

        // Volledig afgekeurd: er is iets ontvangen en alles wat ontvangen is, is afgekeurd (niets goedgekeurd verkocht of over).
        // Korting claimen telt hier niet mee: die partij blijft.
        public static bool IsFullyRejected(decimal receivedQuantity, decimal rejectedQuantity) =>
            receivedQuantity > 0m && rejectedQuantity >= receivedQuantity;

        // Creditnota verwacht zolang er na de laatste vastgelegde creditnota nog een afkeuring met terugsturen of korting is.
        public static string InvoiceIndicator(IEnumerable<(string Kind, DateTime CreatedAt)> refs, IEnumerable<(string Decision, DateTime RejectedAt)> rejections)
        {
            var refList = refs.ToList();
            var lastCreditNote = refList.Where(r => r.Kind == KindCreditNote).Select(r => (DateTime?)r.CreatedAt).DefaultIfEmpty(null).Max();
            if (rejections.Any(r => ExpectsCreditNote(r.Decision) && (lastCreditNote == null || r.RejectedAt > lastCreditNote)))
            {
                return InvoiceCreditNoteExpected;
            }
            return refList.Any(r => r.Kind == KindInvoice) ? InvoiceReceived : InvoiceNone;
        }

        // Factuurnummers worden getrimd opgeslagen en zonder hoofdlettergevoeligheid vergeleken.
        public static string NormalizeInvoiceNumber(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
