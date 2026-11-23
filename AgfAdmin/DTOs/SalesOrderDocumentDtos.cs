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
        // Feedback 22 november: de levering waarvoor dit geldt; leeg = de eerste levering (die van de order zelf, zoals vóór de leveringen).
        public int? SalesDeliveryId { get; set; }
    }

    // Instellingen van de laadbon per bedrijf: taal van de kopteksten (en, nl), klantnaam en leverancierskolom.
    public sealed class LoadingSlipSettingsDto
    {
        public string Language { get; set; }
        public bool ShowCustomer { get; set; }
        public bool ShowSupplier { get; set; }
        public string WarehouseAddress { get; set; }
        // Certificaten deel 2: het GGN van het product per regel (standaard aan). Weggelaten bij opslaan = ongewijzigd.
        public bool? ShowGgn { get; set; }
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
        // Hetzelfde voor de transportopdracht (documenten deel 2), en of er transport is ingevuld (zonder transport geen opdracht).
        public bool TransportOrderNeedsResend { get; set; }
        public bool HasTransport { get; set; }
        public List<SalesOrderDocumentDto> Documents { get; set; } = new();
        // Feedback 22 november: per levering de verkoop-partij, laden op, kenteken en of de laadbon of transportopdracht opnieuw moet.
        public List<SalesDeliveryDocumentsDto> Deliveries { get; set; } = new();
    }

    public sealed class SalesDeliveryDocumentsDto
    {
        public int SalesDeliveryId { get; set; }
        public int Sequence { get; set; }
        public string PartijNumber { get; set; }
        public DateTime? LoadAt { get; set; }
        public string LicensePlate { get; set; }
        public bool NeedsResend { get; set; }
        public bool TransportOrderNeedsResend { get; set; }
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
        // De levering van dit document en haar volgnummer (1 = de eerste levering).
        public int SalesDeliveryId { get; set; }
        public int DeliverySequence { get; set; }
        // Verstuurd per mail (documenten deel 3): naar wie; leeg = niet per mail (of alleen doorgegeven).
        public string SentTo { get; set; }
    }
}
