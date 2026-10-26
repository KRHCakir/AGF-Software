namespace AgfAdmin.Models
{
    // Betalingsconditie (stamgegeven): termijn in dagen vanaf een basis (factuurdatum of einde factuurmaand), optioneel met
    // betalingskorting. Klant- en leveranciersrol verwijzen er verplicht naar; hun PaymentTermsDays is een spiegel van Days die
    // alleen RelationService en PaymentConditionResolver schrijven (TraceabilityGuard regel 16). Inactief in plaats van verwijderen
    // zodra een rol ernaar verwijst. Rekenregels in PaymentConditionRules (gespiegeld in de frontend).
    public sealed class PaymentCondition
    {
        public int Id { get; set; }

        // Idempotentiesleutel van het aanmaakcommando (uniek, null voor gemigreerde en automatisch aangemaakte condities).
        public Guid? RequestId { get; set; }
        public string RequestPayload { get; set; }

        // Code: verplicht, hoofdletterongevoelig uniek (CodeKey = kleine letters, unieke index), bijv. "30D", "EOM30".
        public string Code { get; set; }
        public string CodeKey { get; set; }

        public string DescriptionNl { get; set; }
        public string DescriptionEn { get; set; }

        public int Days { get; set; }
        public string Basis { get; set; } = PaymentConditionBasis.InvoiceDate;

        // Betalingskorting: beide of geen van beide (database-check). Kortingsdatum = factuurdatum + DiscountDays.
        public decimal? DiscountPercent { get; set; }
        public int? DiscountDays { get; set; }

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Optimistisch concurrency-token; centraal verhoogd in AppDbContext.
        public int Version { get; set; }
    }

    public static class PaymentConditionBasis
    {
        // Vervaldatum = factuurdatum + dagen.
        public const string InvoiceDate = "InvoiceDate";
        // Vervaldatum = laatste dag van de factuurmaand + dagen.
        public const string EndOfMonth = "EndOfMonth";
        public static readonly IReadOnlyList<string> All = new[] { InvoiceDate, EndOfMonth };
    }
}
