namespace AgfAdmin.Services
{
    // Documentontvangers en mailconcepten (documenten deel 3): constanten en kleine regels zonder database.
    public static class DocumentMailRules
    {
        public const string OwnerRelation = "Relation";
        public const string OwnerWarehouse = "Warehouse";
        public const string RoleTo = "To";
        public const string RoleCc = "Cc";
        public static readonly IReadOnlyList<string> Roles = new[] { RoleTo, RoleCc };

        // Documentsoorten waarvoor een adres kan gelden (vlaggen); de orderbevestiging en de factuur volgen later.
        public const string KindOrderConfirmation = "OrderConfirmation";
        public const string KindLoadingSlip = "LoadingSlip";
        public const string KindTransportOrder = "TransportOrder";
        public const string KindInvoice = "Invoice";
        public const string KindOther = "Other";
        public static readonly IReadOnlyList<(string Name, int Flag)> Kinds = new[]
        {
            (KindOrderConfirmation, 1), (KindLoadingSlip, 2), (KindTransportOrder, 4), (KindInvoice, 8), (KindOther, 16),
        };

        public static int Flag(string kind) => Kinds.FirstOrDefault(k => string.Equals(k.Name, kind, StringComparison.OrdinalIgnoreCase)).Flag;
        public static IReadOnlyList<string> Names(int flags) => Kinds.Where(k => (flags & k.Flag) != 0).Select(k => k.Name).ToList();

        public const string StatusDraft = "Draft";
        public const string StatusSending = "Sending";
        public const string StatusSent = "Sent";
        public const string StatusFailed = "Failed";
        public const string StatusDiscarded = "Discarded";
        public const string StatusSuperseded = "Superseded";
        public static readonly IReadOnlyList<string> Statuses = new[] { StatusDraft, StatusSending, StatusSent, StatusFailed, StatusDiscarded, StatusSuperseded };

        public const string SecurityNone = "None";
        public const string SecurityStartTls = "StartTls";
        public const string SecuritySslOnConnect = "SslOnConnect";
        public static readonly IReadOnlyList<string> Securities = new[] { SecurityNone, SecurityStartTls, SecuritySslOnConnect };

        public const int MaxNameLength = 200;
        // 15 × 254 tekens past in het Aan- of Cc-veld van een mailconcept (MaxAddressListLength).
        public const int MaxRecipientsPerOwner = 15;
        public const int MaxAddressListLength = 4000;
        public const int MaxSubjectLength = 300;
        public const int MaxBodyLength = 10000;
        public const int MaxErrorLength = 500;
        public const int ConfirmAboveRecipients = 10;
        // Eén verzending duurt nooit langer dan dit (eigen time-out, los van het verzoek); "bezig" langer dan StuckAfter is dus echt verlaten.
        public static readonly TimeSpan SendTimeout = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(5);
        public const int MaxSendsPerMinute = 10;

        // Adressen in een veld Aan of Cc: gescheiden door komma, puntkomma of regeleinde; dubbele (hoofdletterongevoelig) één keer.
        public static List<string> SplitAddresses(string value) =>
            (value ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public static string JoinAddresses(IEnumerable<string> addresses) => string.Join(", ", addresses.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
