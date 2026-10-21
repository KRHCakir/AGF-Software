namespace AgfAdmin.Services
{
    // Vaste lijst van optionele velden op een inkoopfactuur. Een bedrijf schakelt per veld in of uit (CompanySettings);
    // uitgeschakelde velden worden bij invoer genegeerd, historische waarden op bestaande documenten blijven staan.
    // Financiële basisvelden (leverancier, nummer, datum, regels, btw, totalen) zijn nooit uitschakelbaar.
    public static class PurchaseInvoiceRules
    {
        public static readonly IReadOnlyList<string> OptionalFields = new[]
        {
            "receivedDate", "paymentReference", "remarks", "grossWeightKg", "netWeightKg", "containerNumber", "transporter",
            "driverName", "licensePlate", "incoterms", "customsValue", "originCountry", "receiptReference"
        };

        public const string DefaultOptionalFields = "receivedDate,paymentReference,remarks,originCountry,receiptReference";
        public const decimal DefaultVatPercentage = 21m;
        public const int DefaultPaymentTermsDays = 30;
        public const string DefaultCurrency = "EUR";
        public const int MaxLines = 200;
        public const int MaxPaymentTermsDays = 365;

        public static bool IsOptionalField(string key) => OptionalFields.Contains(key, StringComparer.Ordinal);

        public static HashSet<string> ParseEnabled(string stored) =>
            (stored ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(IsOptionalField).ToHashSet(StringComparer.Ordinal);

        public static string Serialize(IEnumerable<string> enabled) =>
            string.Join(',', OptionalFields.Where(f => enabled.Contains(f, StringComparer.Ordinal)));

        // ISO 4217-vorm: drie hoofdletters. Alleen een opgeslagen code; er wordt nooit omgerekend en er zijn geen koersen.
        public static bool IsCurrencyCode(string value) => value != null && value.Length == 3 && value.All(c => c >= 'A' && c <= 'Z');
    }
}
