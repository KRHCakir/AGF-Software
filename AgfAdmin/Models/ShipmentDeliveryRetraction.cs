namespace AgfAdmin.Models
{
    // Intrekking van een administratief onjuiste afleverbevestiging (magazijn of DirectTrade). Append-only en gekoppeld aan precies één
    // bevestiging (unieke index: hooguit één intrekking per bevestiging); de bevestiging zelf blijft ongewijzigd bewaard. Verandert geen
    // voorraad, broncapaciteit, reservering of verzonden hoeveelheid. Soort, verzending en order zijn een kopie van de bevestiging
    // (guard toetst gelijkheid). Niet bedoeld voor retour, schade of tekort.
    public sealed class ShipmentDeliveryRetraction
    {
        public int Id { get; set; }

        public Guid RequestId { get; set; }

        public int ShipmentDeliveryConfirmationId { get; set; }
        public ShipmentDeliveryConfirmation Confirmation { get; set; }

        public string ShipmentKind { get; set; }
        public int? SalesDispatchId { get; set; }
        public int? DirectTradeShipmentId { get; set; }

        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        public string Reason { get; set; }
        public string ActorReference { get; set; }

        // Expliciete verklaring: vlag, taalonafhankelijke code en vaste tekst (database-checks).
        public bool RecordedInErrorConfirmed { get; set; }
        public string ConfirmationCode { get; set; }
        public string ConfirmationStatement { get; set; }

        // Echt UTC-tijdstip van vastleggen.
        public DateTime RecordedAt { get; set; }
    }
}
