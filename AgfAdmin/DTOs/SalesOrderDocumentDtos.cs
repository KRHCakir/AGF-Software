namespace AgfAdmin.DTOs
{
    // Bevestigen van een verkooporder (documenten deel 1): laden op (datum en tijd, optioneel) en kenteken (optioneel, mag met de hand).
    public sealed class ConfirmSalesOrderDto
    {
        public DateTime? LoadAt { get; set; }
        public string LicensePlate { get; set; }
    }

    // Laden op en kenteken aanpassen na bevestigen: een andere bon geeft een nieuwe versie van de laadbon.
    public sealed class UpdateSalesOrderLoadingDto
    {
        public DateTime? LoadAt { get; set; }
        public string LicensePlate { get; set; }
    }

    // Instellingen van de laadbon per bedrijf: taal van de kopteksten (en, nl), klantnaam en leverancierskolom.
    public sealed class LoadingSlipSettingsDto
    {
        public string Language { get; set; }
        public bool ShowCustomer { get; set; }
        public bool ShowSupplier { get; set; }
    }

    public sealed class SalesOrderDocumentsDto
    {
        public int SalesOrderId { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public string ConfirmedBy { get; set; }
        public DateTime? LoadAt { get; set; }
        public string LicensePlate { get; set; }
        // Er is een nieuwere versie van de laadbon dan de eerste en die is nog niet doorgegeven of verstuurd.
        public bool NeedsResend { get; set; }
        public List<SalesOrderDocumentDto> Documents { get; set; } = new();
    }

    public sealed class SalesOrderDocumentDto
    {
        public int Id { get; set; }
        public string Kind { get; set; }
        public string Number { get; set; }
        public int Version { get; set; }
        public string Reason { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? SentAt { get; set; }
        public string SentBy { get; set; }
        public bool Latest { get; set; }
    }
}
