namespace AgfAdmin.Models
{
    // Bevestiging van de volledige aflevering van één verzending: een magazijnverzending óf een DirectTrade-verzending (precies één
    // verwijzing, passend bij ShipmentKind; database-check). Append-only. Verandert geen voorraad, broncapaciteit, reservering of
    // verzonden hoeveelheid. "Afgeleverd" volgt uitsluitend uit een ACTIEVE bevestiging: een bevestiging zonder opgeslagen intrekking
    // (DeliveryState.IsActive, de enige definitie). Een verzending heeft hooguit één actieve bevestiging; na intrekken kan een nieuwe
    // bevestiging met het volgende volgnummer worden vastgelegd. Een gecorrigeerde of teruggenomen verzending kan niet worden bevestigd,
    // en een verzending met een actieve bevestiging kan niet administratief worden gecorrigeerd (guard-regel 13).
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

        // Volgnummer per verzending (1, 2, ...): uniek per verzending en soort (database), altijd het hoogste opgeslagen nummer plus één,
        // en alleen toegestaan als alle eerdere bevestigingen van de verzending zijn ingetrokken (guard). Bestaande rijen kregen 1.
        public int Sequence { get; set; }

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

        // Omgekeerde navigatie: de (hooguit ene) intrekking. Lees "actief" nooit hieruit af zonder DeliveryState.
        public ShipmentDeliveryRetraction Retraction { get; set; }
    }
}
