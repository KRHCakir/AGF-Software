namespace AgfAdmin.Services
{
    // Vaste waarden en rekenregels van de inkooporder (een SupplyCommitment met ordernummer). Gespiegeld in
    // agf-frontend/src/lib/purchaseOrderRules.ts; beide kanten hebben tests op dezelfde gevallen.
    // Deze file: ordernummer en bestelgegevens. Status en factuur: PurchaseOrderRules.Orders.cs; afkeuren: PurchaseOrderRules.Rejection.cs.
    public static partial class PurchaseOrderRules
    {
        public const string NumberPrefix = "IO";
        public const int MaxSupplierReferenceLength = 100;
        public const int MaxNoteLength = 1000;
        public const int MaxInvoiceNumberLength = 100;
        public const int MaxExplanationLength = 500;
        public const int MaxActorLength = 100;

        public static readonly IReadOnlyList<string> Channels = new[] { "Phone", "Email", "Portal", "Other" };

        // Vergelijkingssleutel van een factuurnummer (zoals SearchCodeKey): getrimd, kleine letters (invariant); leeg = null.
        public static string InvoiceNumberKey(string invoiceNumber) =>
            string.IsNullOrWhiteSpace(invoiceNumber) ? null : invoiceNumber.Trim().ToLowerInvariant();

        public static bool IsChannel(string value) => value != null && Channels.Contains(value, StringComparer.Ordinal);

        // "IO-2026-0001": minstens vier cijfers; boven 9999 groeit het nummer gewoon door (IO-2026-10000).
        public static string FormatNumber(int year, int sequence) => $"{NumberPrefix}-{year}-{sequence:D4}";

        // Nummering van bestaande toezeggingen bij de migratie (AddPurchaseOrders): op CreatedAt en dan Id, per kalenderjaar van
        // CreatedAt in Europe/Amsterdam, vanaf 1 zonder gaten; OrderedOn = die kalenderdatum. C#-spiegel van de SQL in die migratie.
        public static IEnumerable<(int Id, int Year, int Sequence, string OrderNumber, DateOnly OrderedOn)> InitialNumbers(IEnumerable<(int Id, DateTime CreatedAt)> existing) =>
            existing
                .Select(c => (c.Id, c.CreatedAt, Date: AmsterdamBusinessCalendar.ToAmsterdamDate(c.CreatedAt)))
                .GroupBy(c => c.Date.Year)
                .SelectMany(year => year.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                    .Select((c, index) => (c.Id, year.Key, index + 1, FormatNumber(year.Key, index + 1), c.Date)));
    }
}
