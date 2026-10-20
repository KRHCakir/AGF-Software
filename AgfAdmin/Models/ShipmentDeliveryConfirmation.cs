namespace AgfAdmin.Models
{
    // Bevestiging van de volledige aflevering van één verzending: een magazijnverzending óf een DirectTrade-verzending (precies één
    // verwijzing, passend bij ShipmentKind; database-check). Append-only. Verandert geen voorraad, broncapaciteit, reservering of
    // verzonden hoeveelheid; "afgeleverd" volgt alleen uit het bestaan van deze bevestiging. Een gecorrigeerde of teruggenomen verzending
    // kan niet worden bevestigd, en een bevestigde verzending kan niet meer administratief worden gecorrigeerd (guard-regel 13).
    // Werkelijke afleverdatum (kalenderdatum, Europe/Amsterdam) en UTC-vastlegtijd staan apart.
    public sealed class ShipmentDeliveryConfirmation
    {
        public int Id { get; set; }

        public Guid RequestId { get; set; }

        public string ShipmentKind { get; set; }   // "Warehouse" of "DirectTrade"

        public int? SalesDispatchId { get; set; }
        public SalesDispatch SalesDispatch { get; set; }

        public int? DirectTradeShipmentId { get; set; }
        public DirectTradeShipment DirectTradeShipment { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        // Werkelijke afleverdatum: niet vóór de verzenddatum en niet na vandaag, beide in Europe/Amsterdam (service en guard).
        public DateOnly DeliveredOn { get; set; }
        public string ReceiptReference { get; set; }   // optioneel, bijvoorbeeld getekende CMR of ontvangstbon
        public string ActorReference { get; set; }

        // Expliciete verklaring: vlag, taalonafhankelijke code en vaste tekst (database-checks).
        public bool DeliveredInFullConfirmed { get; set; }
        public string ConfirmationCode { get; set; }
        public string ConfirmationStatement { get; set; }

        // Echt UTC-tijdstip van vastleggen.
        public DateTime RecordedAt { get; set; }
    }
}
